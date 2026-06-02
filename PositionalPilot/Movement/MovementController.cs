using System.Numerics;
using PositionalPilot.Core.Geometry;
using PositionalPilot.Core.Model;
using PositionalPilot.Game;
using PositionalPilot.IPC;

namespace PositionalPilot.Movement;

internal sealed class MovementController
{
    private readonly Configuration config;
    private readonly GameStateReader game;
    private readonly BossModIpc bossMod;
    private readonly VnavmeshIpc vnavmesh;
    private readonly RotationSolverIpc rotationSolver;
    private readonly SafetyGate safety;
    private readonly ThrottledLogger logger;

    private DateTime nextRepath = DateTime.MinValue;
    private DateTime nextDependencyRefresh = DateTime.MinValue;
    private DateTime nextSafetyRefresh = DateTime.MinValue;
    private CachedSafetyState cachedSafety = CachedSafetyState.Empty;
    private ulong lastTargetId;
    private BorderDestination? currentDestination;
    private BorderSide selectedSide = BorderSide.None;
    private string destinationFailureReason = string.Empty;
    private Vector3? lastFailedPathDestination;
    private DateTime lastFailedPathTime = DateTime.MinValue;
    private DateTime nextNoCastingAllowed = DateTime.MinValue;
    private PositionalRequirement lastMovementPositional = PositionalRequirement.Unknown;
    private uint lastNextGcdActionId;

    public MovementController(Configuration config, GameStateReader game, BossModIpc bossMod, VnavmeshIpc vnavmesh, RotationSolverIpc rotationSolver, ThrottledLogger logger)
    {
        this.config = config;
        this.game = game;
        this.bossMod = bossMod;
        this.vnavmesh = vnavmesh;
        this.rotationSolver = rotationSolver;
        this.logger = logger;
        safety = new SafetyGate(config, bossMod, vnavmesh, rotationSolver);
    }

    public MovementState State { get; private set; } = MovementState.Idle;
    public string BlockReason { get; private set; } = "not evaluated";
    public PositionalRequirement CurrentPositional { get; private set; } = PositionalRequirement.Unknown;
    public BorderSide CurrentBorderSide => selectedSide;
    public Vector3? ChosenDestination => currentDestination?.Position;
    public CachedSafetyState LastCachedSafety => cachedSafety;
    public string LastNoCastingReason { get; private set; } = "not evaluated";
    public PositionalRequirement CurrentMovementPositional { get; private set; } = PositionalRequirement.Unknown;
    public string CurrentMovementPositionalSource { get; private set; } = "not evaluated";
    public string CurrentMovementMode => PositionalMovementRules.MovementModeName(CurrentMovementPositional);
    public RotationSolverNextActionInfo LastRotationSolverNextAction => rotationSolver.GetNextGcdActionInfo();
    public GameSnapshot LastSnapshot { get; private set; } = new(false, default, 0, 0, false, false, false, false, 0, string.Empty, 0, 0, default, 0, 0, null, false, false, false);

    public void Update()
    {
        RefreshDependencyStatus(false);
        LastSnapshot = game.Read();

        if (State == MovementState.EmergencyStopped)
            return;

        if (LastSnapshot.HasTarget && lastTargetId != 0 && LastSnapshot.TargetId != lastTargetId)
        {
            Stop("target changed");
            currentDestination = null;
            selectedSide = BorderSide.None;
            cachedSafety = CachedSafetyState.Empty;
            nextSafetyRefresh = DateTime.MinValue;
        }

        if (LastSnapshot.HasTarget)
            lastTargetId = LastSnapshot.TargetId;

        if (TryBlockWithoutIpc(LastSnapshot, out var earlyReason))
        {
            BlockReason = earlyReason;
            if (State == MovementState.Moving)
                Stop(earlyReason);
            State = State == MovementState.Cooldown ? State : MovementState.Blocked;
            return;
        }

        RefreshSafetyState(false);

        if (config.Settings.DisableDuringManualMovement &&
            State == MovementState.Moving &&
            LastSnapshot.IsManuallyMoving &&
            !cachedSafety.VnavmeshNavigating)
        {
            Stop("manual movement detected");
            EnterCooldown();
            return;
        }

        if (!safety.CanEvaluate(LastSnapshot, cachedSafety, out var reason))
        {
            BlockReason = reason;
            if (State == MovementState.Moving)
                Stop(reason);
            State = State == MovementState.Cooldown ? State : MovementState.Blocked;
            return;
        }

        var positional = cachedSafety.Positional;
        if (!cachedSafety.HasPositional ||
            positional is PositionalRequirement.None or PositionalRequirement.Unknown)
        {
            CurrentPositional = positional;
            BlockReason = "no actionable BossMod positional";
            if (State == MovementState.Moving)
                Stop(BlockReason);
            State = MovementState.Blocked;
            return;
        }

        CurrentPositional = positional;
        var nextAction = rotationSolver.GetNextGcdActionInfo();
        var movementPositional = ResolveMovementPositional(positional, nextAction);
        var bypassRepathCooldown = PositionalMovementRules.ShouldBypassRepathCooldown(
            lastMovementPositional,
            movementPositional,
            lastNextGcdActionId,
            nextAction.NextGcdActionId);

        if (config.Settings.MovementMode == MovementMode.SuggestOnly)
        {
            EvaluateDestination(LastSnapshot, movementPositional);
            State = MovementState.Idle;
            return;
        }

        if (!bypassRepathCooldown && DateTime.UtcNow < nextRepath)
        {
            BlockReason = "cooldown";
            return;
        }

        var wasMoving = State == MovementState.Moving;
        var previousDestination = currentDestination?.Position;
        State = MovementState.Evaluating;
        var selected = EvaluateDestination(LastSnapshot, movementPositional);
        if (selected == null)
        {
            BlockReason = string.IsNullOrWhiteSpace(destinationFailureReason) ? "no safe destination" : destinationFailureReason;
            State = MovementState.Blocked;
            RecordMovementCadence(movementPositional, nextAction);
            return;
        }

        RefreshSafetyState(true);
        if (!safety.CanMoveTo(LastSnapshot, cachedSafety, selected.Position, out reason))
        {
            BlockReason = reason;
            State = MovementState.Blocked;
            RecordMovementCadence(movementPositional, nextAction);
            return;
        }

        var deadzone = GetMovementDeadzone(selected.Requirement);
        if (PositionalGeometry.DistanceXZ(LastSnapshot.PlayerPosition, selected.Position) <= deadzone)
        {
            if (wasMoving && cachedSafety.VnavmeshNavigating)
                Stop($"{CurrentMovementMode} deadzone reached");
            else
                BlockReason = PositionalMovementRules.IsCommittedPositional(selected.Requirement)
                    ? "already in committed slice"
                    : "within border hold deadzone";

            State = MovementState.Idle;
            nextRepath = DateTime.UtcNow.AddMilliseconds(config.Settings.RepathCooldownMs);
            RecordMovementCadence(movementPositional, nextAction);
            return;
        }

        if (wasMoving &&
            cachedSafety.VnavmeshNavigating &&
            previousDestination.HasValue &&
            PositionalGeometry.DistanceXZ(previousDestination.Value, selected.Position) < config.Settings.DestinationChangeThresholdYalms)
        {
            BlockReason = string.Empty;
            nextRepath = DateTime.UtcNow.AddMilliseconds(config.Settings.RepathCooldownMs);
            logger.Debug(config, "movement-continue", $"Continuing toward {previousDestination.Value}; new destination delta below retarget threshold");
            return;
        }

        var navTolerance = GetVnavmeshTolerance();
        if (!vnavmesh.PathfindAndMoveCloseTo(selected.Position, navTolerance))
        {
            Stop($"vnavmesh path request failed: {vnavmesh.LastError ?? "IPC call failed"}");
            lastFailedPathDestination = selected.Position;
            lastFailedPathTime = DateTime.UtcNow;
            currentDestination = null;

            EnterCooldown(2000);
            return;
        }

        MaybeTriggerNoCasting(LastSnapshot, selected);
        BlockReason = string.Empty;
        State = MovementState.Moving;
        nextRepath = DateTime.UtcNow.AddMilliseconds(config.Settings.RepathCooldownMs);
        RecordMovementCadence(movementPositional, nextAction);
        logger.Debug(config, "movement-start", $"Moving to {selected.Position} for {movementPositional} ({CurrentMovementPositionalSource})");
    }

    public void EmergencyStop()
    {
        config.Settings.Enabled = false;
        config.Settings.MovementMode = MovementMode.Disabled;
        Stop("emergency stop");
        State = MovementState.EmergencyStopped;
        BlockReason = "emergency stopped";
        config.Save();
    }

    public void RefreshDependencyStatus(bool force)
    {
        if (!force && DateTime.UtcNow < nextDependencyRefresh)
            return;

        bossMod.RefreshAvailability();
        vnavmesh.RefreshAvailability();
        rotationSolver.RefreshAvailability();
        nextDependencyRefresh = DateTime.UtcNow.AddMilliseconds(config.Settings.DependencyRefreshMs);
    }

    public void ClearEmergencyStop()
    {
        if (State == MovementState.EmergencyStopped)
            State = MovementState.Idle;
    }

    public void Stop(string reason)
    {
        vnavmesh.Stop();
        if (config.Settings.EnableRotationSolverCoordination)
            rotationSolver.UnpauseOrEndSpecial();
        BlockReason = reason;
        logger.Debug(config, $"stop:{reason}", $"Movement stopped: {reason}");
    }

    private BorderDestination? EvaluateDestination(GameSnapshot snapshot, PositionalRequirement positional)
    {
        destinationFailureReason = string.Empty;
        var target = new TargetSnapshot(snapshot.TargetPosition, snapshot.TargetRotation, snapshot.TargetHitboxRadius);

        selectedSide = PositionalGeometry.SelectBorderSide(snapshot.PlayerPosition, target, config.Settings, selectedSide, side =>
        {
            var sideDestination = PositionalGeometry.CreateBorderDestination(snapshot.PlayerPosition, target, PositionalRequirement.Any, side, config.Settings);
            return bossMod.IsPositionSafe(sideDestination.Position) &&
                   bossMod.IsDashSafe(snapshot.PlayerPosition, sideDestination.Position) &&
                   !RecentlyFailedPath(sideDestination.Position);
        });

        var destination = PositionalGeometry.CreateBorderDestination(snapshot.PlayerPosition, target, positional, selectedSide, config.Settings);
        if (!IsDestinationInRequestedSlice(target, destination))
        {
            destinationFailureReason = "destination is outside requested positional slice";
            currentDestination = null;
            logger.Debug(config, "border-destination", $"{destinationFailureReason}; side={selectedSide}; positional={positional}");
            return null;
        }

        if (RecentlyFailedPath(destination.Position))
        {
            destinationFailureReason = "recent vnavmesh failure for border destination";
            currentDestination = null;
            logger.Debug(config, "border-destination", $"{destinationFailureReason}; side={selectedSide}; positional={positional}");
            return null;
        }

        if (!bossMod.IsPositionSafe(destination.Position) || !bossMod.IsDashSafe(snapshot.PlayerPosition, destination.Position))
        {
            destinationFailureReason = "BossMod reports border destination unsafe";
            currentDestination = null;
            logger.Debug(config, "border-destination", $"{destinationFailureReason}; side={selectedSide}; positional={positional}; destination={destination.Position}");
            return null;
        }

        currentDestination = destination;
        logger.Debug(config, "border-destination", $"Selected {destination.Position} on {selectedSide} border for {positional}");
        return currentDestination;
    }

    private bool TryBlockWithoutIpc(GameSnapshot snapshot, out string reason)
    {
        var settings = config.Settings;
        if (!settings.Enabled)
            return Block("plugin disabled", out reason);
        if (settings.MovementMode == MovementMode.Disabled)
            return Block("movement mode disabled", out reason);
        if (!snapshot.HasPlayer)
            return Block("player unavailable", out reason);
        if (!snapshot.HasTarget)
            return Block("no current target", out reason);
        if (settings.OnlyInCombat && !snapshot.InCombat)
            return Block("not in combat", out reason);
        if (settings.OnlyMeleeJobs && !GameStateReader.IsMeleeJob(snapshot.JobId))
            return Block($"not a melee job (job {snapshot.JobId})", out reason);
        if (snapshot.TargetOmnidirectional == true)
            return Block("target does not require positionals", out reason);

        reason = string.Empty;
        return false;
    }

    private static bool Block(string value, out string reason)
    {
        reason = value;
        return true;
    }

    private void RefreshSafetyState(bool force)
    {
        if (!force && DateTime.UtcNow < nextSafetyRefresh)
            return;

        var hasPositional = bossMod.TryGetRecommendedPositional(out var positional);
        var nextDamage = bossMod.TryGetNextDamageIn(out var damage) ? damage : (float?)null;
        var nextKnockback = bossMod.TryGetNextKnockbackIn(out var knockback) ? knockback : (float?)null;
        var nextDowntime = bossMod.TryGetNextDowntimeIn(out var downtime) ? downtime : (float?)null;

        cachedSafety = new CachedSafetyState(
            vnavmesh.IsReady(),
            vnavmesh.IsNavigating(),
            bossMod.IsBossModNavigating(),
            bossMod.TryGetBossModNaviTarget(out _),
            rotationSolver.Available,
            hasPositional,
            positional,
            nextDamage,
            nextKnockback,
            nextDowntime,
            DateTime.UtcNow);
        nextSafetyRefresh = DateTime.UtcNow.AddMilliseconds(config.Settings.SafetyRefreshMs);
    }

    private float GetVnavmeshTolerance() => MathF.Max(config.Settings.StopWithinYalms, 1.0f);

    private float GetMovementDeadzone(PositionalRequirement requirement) =>
        PositionalMovementRules.IsCommittedPositional(requirement)
            ? MathF.Max(config.Settings.PositionalCommitDeadzoneYalms, config.Settings.StopWithinYalms)
            : (config.Settings.BorderHoldDeadzoneYalms > 0 ? config.Settings.BorderHoldDeadzoneYalms : config.Settings.HoldDeadzoneYalms);

    private void RecordMovementCadence(PositionalRequirement movementPositional, RotationSolverNextActionInfo nextAction)
    {
        lastMovementPositional = movementPositional;
        lastNextGcdActionId = nextAction.NextGcdActionId;
    }

    private PositionalRequirement ResolveMovementPositional(PositionalRequirement bossModPositional, RotationSolverNextActionInfo next)
    {
        CurrentMovementPositional = bossModPositional;
        CurrentMovementPositionalSource = "BossMod";

        if (!next.EventsAvailable)
            return bossModPositional;
        if (DateTime.UtcNow - next.NextGcdUpdatedAt > TimeSpan.FromMilliseconds(config.Settings.RsrNextActionMaxAgeMs))
            return bossModPositional;
        if (next.NextGcdRequirement is not (PositionalRequirement.Rear or PositionalRequirement.Flank))
            return bossModPositional;

        CurrentMovementPositional = next.NextGcdRequirement;
        CurrentMovementPositionalSource = $"RSR next GCD: {next.NextGcdActionName}";
        return next.NextGcdRequirement;
    }

    private void MaybeTriggerNoCasting(GameSnapshot snapshot, BorderDestination selected)
    {
        LastNoCastingReason = GetNoCastingBlockReason(snapshot, selected, out var duration);
        if (LastNoCastingReason != "triggered")
        {
            logger.Debug(config, "rsr-nocasting-skip", LastNoCastingReason);
            return;
        }

        rotationSolver.PauseOrNoCasting(duration);
        nextNoCastingAllowed = DateTime.UtcNow.AddMilliseconds(config.Settings.NoCastingCooldownMs);
        logger.Debug(config, "rsr-nocasting", $"Triggered NoCasting for {rotationSolver.GetNextGcdActionInfo().NextGcdActionName}");
    }

    private string GetNoCastingBlockReason(GameSnapshot snapshot, BorderDestination selected, out float duration)
    {
        duration = 0;
        if (!config.Settings.EnableRotationSolverCoordination)
            return "coordination disabled";
        if (!rotationSolver.Available)
            return "RotationSolver unavailable";
        if (!rotationSolver.NextActionEventsAvailable)
            return "next action event unavailable";
        if (cachedSafety.BossModNavigating || cachedSafety.BossModHasNaviTarget)
            return "BossMod navigation active";
        if (snapshot.TargetOmnidirectional == true)
            return "target does not require positionals";
        if (snapshot.TrueNorthAvailable)
            return "True North available";
        if (selected.Requirement is PositionalRequirement.Any or PositionalRequirement.None or PositionalRequirement.Unknown)
            return "border hold only";
        if (DateTime.UtcNow < nextNoCastingAllowed)
            return "cooldown";

        var next = rotationSolver.GetNextGcdActionInfo();
        if (next.NextGcdActionId == 0)
            return "next action unknown";
        if (DateTime.UtcNow - next.NextGcdUpdatedAt > TimeSpan.FromMilliseconds(config.Settings.RsrNextActionMaxAgeMs))
            return "next action stale";
        if (next.NextGcdRequirement is not (PositionalRequirement.Rear or PositionalRequirement.Flank))
            return "next action not positional";
        if (next.NextGcdRequirement != selected.Requirement)
            return $"next positional {next.NextGcdRequirement} does not match movement {selected.Requirement}";

        var target = new TargetSnapshot(snapshot.TargetPosition, snapshot.TargetRotation, snapshot.TargetHitboxRadius);
        if (PositionalGeometry.IsPositionInRequiredSlice(snapshot.PlayerPosition, target, next.NextGcdRequirement))
            return "already in slice";

        duration = Math.Clamp(config.Settings.NoCastingDurationSeconds, 0.1f, 2.0f);
        return "triggered";
    }

    private static bool IsDestinationInRequestedSlice(TargetSnapshot target, BorderDestination destination)
    {
        if (destination.Requirement == PositionalRequirement.Any)
            return true;

        return PositionalGeometry.IsPositionInRequiredSlice(destination.Position, target, destination.Requirement);
    }

    private bool RecentlyFailedPath(Vector3 destination) =>
        lastFailedPathDestination.HasValue &&
        DateTime.UtcNow - lastFailedPathTime < TimeSpan.FromSeconds(5) &&
        PositionalGeometry.DistanceXZ(lastFailedPathDestination.Value, destination) < 0.75f;

    private void EnterCooldown(int? overrideMs = null)
    {
        State = MovementState.Cooldown;
        nextRepath = DateTime.UtcNow.AddMilliseconds(overrideMs ?? config.Settings.RepathCooldownMs);
    }
}
