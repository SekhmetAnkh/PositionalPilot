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
    private readonly WrathComboIpc wrathCombo;
    private readonly TrueNorthAction trueNorth;
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
    private DateTime nextTrueNorthAllowed = DateTime.MinValue;
    private PositionalRequirement lastMovementPositional = PositionalRequirement.Unknown;
    private uint lastMovementActionId;
    private uint currentMovementActionId;

    public MovementController(Configuration config, GameStateReader game, BossModIpc bossMod, VnavmeshIpc vnavmesh, RotationSolverIpc rotationSolver, WrathComboIpc wrathCombo, TrueNorthAction trueNorth, ThrottledLogger logger)
    {
        this.config = config;
        this.game = game;
        this.bossMod = bossMod;
        this.vnavmesh = vnavmesh;
        this.rotationSolver = rotationSolver;
        this.wrathCombo = wrathCombo;
        this.trueNorth = trueNorth;
        this.logger = logger;
        safety = new SafetyGate(config, bossMod);
    }

    public MovementState State { get; private set; } = MovementState.Idle;
    public string BlockReason { get; private set; } = "not evaluated";
    public PositionalRequirement CurrentPositional { get; private set; } = PositionalRequirement.Unknown;
    public BorderSide CurrentBorderSide => selectedSide;
    public Vector3? ChosenDestination => currentDestination?.Position;
    public CachedSafetyState LastCachedSafety => cachedSafety;
    public string LastNoCastingReason { get; private set; } = "not evaluated";
    public string LastTrueNorthDecision { get; private set; } = "not evaluated";
    public PositionalRequirement CurrentMovementPositional { get; private set; } = PositionalRequirement.Unknown;
    public string CurrentMovementPositionalSource { get; private set; } = "not evaluated";
    public string CurrentMovementMode => PositionalMovementRules.MovementModeName(CurrentMovementPositional);
    public RotationSolverNextActionInfo LastRotationSolverNextAction => rotationSolver.GetNextGcdActionInfo();
    public WrathComboNextActionInfo LastWrathComboNextAction => wrathCombo.GetInferredNextActionInfo();
    public WrathLocalPrediction LastWrathLocalPrediction { get; private set; } =
        new(0, 0, 0, "not evaluated", PositionalRequirement.Unknown, false);
    public GameSnapshot LastSnapshot { get; private set; } = new(false, default, 0, 0, false, false, false, false, 0, string.Empty, 0, 0, default, 0, 0, null, false, false, false, false);

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

        CurrentPositional = cachedSafety.Positional;
        var movementPositional = ResolveMovementPositional();
        var frontEscape = PositionalMovementRules.CanFrontEscape(IsPlayerCurrentlyInFront(LastSnapshot), LastSnapshot.TargetTargetsPlayer, LastSnapshot.TargetIsTrainingDummy);
        if (frontEscape && !PositionalMovementRules.IsCommittedPositional(movementPositional))
            CurrentMovementPositionalSource = "front escape to rear/flank border";

        var bypassRepathCooldown = PositionalMovementRules.ShouldBypassRepathCooldown(
            lastMovementPositional,
            movementPositional,
            lastMovementActionId,
            currentMovementActionId,
            frontEscape);

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
            MaybeUseTrueNorthFallback(LastSnapshot, movementPositional, BlockReason);
            State = MovementState.Blocked;
            RecordMovementCadence(movementPositional);
            return;
        }

        if (!CanArriveForCommittedMovement(selected, out var budgetReason))
        {
            BlockReason = budgetReason;
            MaybeUseTrueNorthFallback(LastSnapshot, movementPositional, budgetReason);
            State = MovementState.Blocked;
            RecordMovementCadence(movementPositional);
            return;
        }

        RefreshSafetyState(true);
        if (!safety.CanMoveTo(LastSnapshot, cachedSafety, selected.Position, out reason))
        {
            BlockReason = reason;
            MaybeUseTrueNorthFallback(LastSnapshot, movementPositional, reason);
            MaybeTriggerNoCasting(LastSnapshot, selected);
            State = MovementState.Blocked;
            RecordMovementCadence(movementPositional);
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
            RecordMovementCadence(movementPositional);
            return;
        }

        MaybeTriggerNoCasting(LastSnapshot, selected);

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

        BlockReason = string.Empty;
        State = MovementState.Moving;
        nextRepath = DateTime.UtcNow.AddMilliseconds(config.Settings.RepathCooldownMs);
        RecordMovementCadence(movementPositional);
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
        wrathCombo.RefreshAvailability();
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

        if (!PositionalMovementRules.IsCommittedPositional(positional))
        {
            var borderDestination = PositionalGeometry.CreateBorderDestination(snapshot.PlayerPosition, target, positional, selectedSide, config.Settings);
            if (!IsCandidateAllowed(snapshot, target, borderDestination))
                return RejectDestination("destination is not rear/flank safe", positional);

            currentDestination = borderDestination;
            logger.Debug(config, "border-destination", $"Selected {borderDestination.Position} on {selectedSide} border for {positional}");
            return currentDestination;
        }

        var candidates = PositionalDestinationPlanner
            .EnumerateCandidates(snapshot.PlayerPosition, target, positional, selectedSide, config.Settings)
            .Where(candidate => IsCandidateAllowed(snapshot, target, candidate))
            .Select(candidate => candidate with
            {
                Score = PositionalDestinationPlanner.ScoreCandidate(candidate, snapshot.PlayerPosition, target, config.Settings, currentDestination?.Position),
            })
            .Where(candidate => float.IsFinite(candidate.Score))
            .OrderBy(candidate => candidate.Score)
            .ToList();

        if (candidates.Count == 0)
            return RejectDestination("no safe committed positional candidate", positional);

        currentDestination = candidates[0];
        selectedSide = currentDestination.Side;
        logger.Debug(config, "border-destination", $"Selected {currentDestination.Position} on {selectedSide} for {positional}; candidates={candidates.Count}; score={currentDestination.Score:0.00}");
        return currentDestination;
    }

    private BorderDestination? RejectDestination(string reason, PositionalRequirement positional)
    {
        destinationFailureReason = reason;
        currentDestination = null;
        logger.Debug(config, "border-destination", $"{destinationFailureReason}; side={selectedSide}; positional={positional}");
        return null;
    }

    private bool IsCandidateAllowed(GameSnapshot snapshot, TargetSnapshot target, BorderDestination destination)
    {
        if (!IsDestinationInRequestedSlice(target, destination))
        {
            destinationFailureReason = "candidate is not in requested slice";
            return false;
        }

        if (!PositionalDestinationPlanner.IsCandidateInMeleeRange(destination.Position, target, config.Settings))
        {
            destinationFailureReason = "candidate outside melee range";
            return false;
        }

        if (RecentlyFailedPath(destination.Position))
        {
            destinationFailureReason = "recent vnavmesh failure for border destination";
            return false;
        }

        if (!bossMod.IsPositionSafe(destination.Position) || !bossMod.IsDashSafe(snapshot.PlayerPosition, destination.Position))
        {
            destinationFailureReason = "BossMod reports border destination unsafe";
            return false;
        }

        return true;
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
        if (PositionalMovementRules.ShouldBlockForTargetOfTarget(snapshot.TargetTargetsPlayer, snapshot.TargetIsTrainingDummy))
            return Block("target is targeting player", out reason);

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
            IsSelectedCombatIntentAvailable(),
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

    private void RecordMovementCadence(PositionalRequirement movementPositional)
    {
        lastMovementPositional = movementPositional;
        lastMovementActionId = currentMovementActionId;
    }

    private PositionalRequirement ResolveMovementPositional()
    {
        CurrentMovementPositional = PositionalRequirement.Any;
        CurrentMovementPositionalSource = "nearest rear/flank border";
        currentMovementActionId = 0;

        if (config.Settings.CombatIntentSource == CombatIntentSource.WrathCombo)
            return ResolveWrathComboMovementPositional();

        var next = rotationSolver.GetNextGcdActionInfo();
        if (!next.EventsAvailable)
            return PositionalRequirement.Any;

        var resolved = PositionalMovementRules.ResolveRsrMovementRequirement(
            next.NextGcdRequirement,
            next.NextGcdUpdatedAt,
            next.NextActionRequirement,
            next.NextActionUpdatedAt,
            DateTime.UtcNow,
            config.Settings.RsrNextActionMaxAgeMs,
            out var source);

        CurrentMovementPositional = resolved;
        CurrentMovementPositionalSource = source switch
        {
            "RSR next GCD" => $"RSR next GCD: {next.NextGcdActionName}",
            "RSR next action" => $"RSR next action: {next.NextActionName}",
            _ => source,
        };
        currentMovementActionId = source switch
        {
            "RSR next GCD" => next.NextGcdActionId,
            "RSR next action" => next.NextActionId,
            _ => 0,
        };
        return resolved;
    }

    private PositionalRequirement ResolveWrathComboMovementPositional()
    {
        if (!wrathCombo.ActionEventsAvailable)
        {
            CurrentMovementPositionalSource = "WrathCombo action event unavailable";
            LastWrathLocalPrediction = new(
                wrathCombo.LatestActionId,
                wrathCombo.LatestWeaponskillOrSpellActionId,
                LastSnapshot.WrathPredictionSnapshot.ComboActionId,
                CurrentMovementPositionalSource,
                PositionalRequirement.Unknown,
                false);
            return PositionalRequirement.Any;
        }

        var snapshot = LastSnapshot.WrathPredictionSnapshot with
        {
            RawActionId = wrathCombo.LatestActionId,
            RawActionUpdatedAt = wrathCombo.LatestActionUpdatedAt,
            FilteredWeaponskillOrSpellId = wrathCombo.LatestWeaponskillOrSpellActionId,
            FilteredWeaponskillOrSpellUpdatedAt = wrathCombo.LatestWeaponskillOrSpellUpdatedAt,
            EnableSamMeikyoAnticipation = config.Settings.EnableSamMeikyoWrathAnticipation,
            MaxAgeMs = config.Settings.RsrNextActionMaxAgeMs,
            Now = DateTime.UtcNow,
        };
        var prediction = WrathLocalPositionalPredictor.Predict(snapshot);
        LastWrathLocalPrediction = prediction;

        if (!prediction.IsFreshOrUsable || !PositionalMovementRules.IsCommittedPositional(prediction.Requirement))
        {
            CurrentMovementPositionalSource = prediction.Source;
            return PositionalRequirement.Any;
        }

        CurrentMovementPositional = prediction.Requirement;
        CurrentMovementPositionalSource = prediction.Source;
        currentMovementActionId = prediction.ComboActionId != 0
            ? prediction.ComboActionId
            : prediction.FilteredWeaponskillOrSpellId;
        return prediction.Requirement;
    }

    private static bool IsPlayerCurrentlyInFront(GameSnapshot snapshot)
    {
        if (!snapshot.HasTarget)
            return false;

        var target = new TargetSnapshot(snapshot.TargetPosition, snapshot.TargetRotation, snapshot.TargetHitboxRadius);
        return PositionalGeometry.ClassifyPositionRelativeToTarget(snapshot.PlayerPosition, target) == PositionalRequirement.Front;
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
        if (config.Settings.CombatIntentSource != CombatIntentSource.RotationSolverReborn)
            return "NoCasting only supports RotationSolver source";
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
        var resolved = PositionalMovementRules.ResolveRsrMovementRequirement(
            next.NextGcdRequirement,
            next.NextGcdUpdatedAt,
            next.NextActionRequirement,
            next.NextActionUpdatedAt,
            DateTime.UtcNow,
            config.Settings.RsrNextActionMaxAgeMs,
            out var source);
        if (resolved is not (PositionalRequirement.Rear or PositionalRequirement.Flank))
            return "next action not positional";
        if (resolved != selected.Requirement)
            return $"next positional {resolved} does not match movement {selected.Requirement}";

        var target = new TargetSnapshot(snapshot.TargetPosition, snapshot.TargetRotation, snapshot.TargetHitboxRadius);
        if (PositionalGeometry.IsPositionInRequiredSlice(snapshot.PlayerPosition, target, resolved))
            return "already in slice";

        duration = Math.Clamp(config.Settings.NoCastingDurationSeconds, 0.1f, 2.0f);
        return "triggered";
    }

    private bool IsSelectedCombatIntentAvailable() =>
        config.Settings.CombatIntentSource == CombatIntentSource.WrathCombo
            ? wrathCombo.Available
            : rotationSolver.Available;

    private bool CanArriveForCommittedMovement(BorderDestination selected, out string reason)
    {
        if (!PositionalMovementRules.IsCommittedPositional(selected.Requirement))
        {
            reason = "border hold has no action budget";
            return true;
        }

        var budget = GetAvailableMovementBudgetSeconds();
        return PositionalMovementBudgetPolicy.CanArriveInTime(
            selected.DistanceFromPlayer,
            budget,
            config.Settings,
            out reason);
    }

    private float? GetAvailableMovementBudgetSeconds()
    {
        // TODO: RotationSolver next-action events currently expose action IDs only, not GCD remaining,
        // action-ahead, or primary target id. Keep committed movement fail-closed until reliable timing IPC exists.
        return null;
    }

    private void MaybeUseTrueNorthFallback(GameSnapshot snapshot, PositionalRequirement movementPositional, string movementBlockReason)
    {
        if (DateTime.UtcNow < nextTrueNorthAllowed)
        {
            LastTrueNorthDecision = "cooldown";
            return;
        }

        if (!TrueNorthFallbackPolicy.ShouldUseTrueNorth(snapshot, movementPositional, config.Settings, out var reason))
        {
            LastTrueNorthDecision = reason;
            return;
        }

        if (trueNorth.TryUse())
        {
            LastTrueNorthDecision = $"{reason}; triggered after {movementBlockReason}";
            nextTrueNorthAllowed = DateTime.UtcNow.AddMilliseconds(MathF.Max(500, config.Settings.NoCastingCooldownMs));
            logger.Debug(config, "true-north", LastTrueNorthDecision);
            return;
        }

        LastTrueNorthDecision = "True North use failed";
        nextTrueNorthAllowed = DateTime.UtcNow.AddMilliseconds(1000);
    }

    private static bool IsDestinationInRequestedSlice(TargetSnapshot target, BorderDestination destination)
    {
        if (destination.Requirement == PositionalRequirement.Any)
        {
            var slice = PositionalGeometry.ClassifyPositionRelativeToTarget(destination.Position, target);
            return slice is PositionalRequirement.Rear or PositionalRequirement.Flank &&
                   PositionalGeometry.GetFacingAngleToPosition(destination.Position, target) >= MathF.PI * 3f / 4f - 0.01f;
        }

        return PositionalGeometry.IsPositionInRequiredSlice(destination.Position, target, destination.Requirement) &&
               PositionalGeometry.ClassifyPositionRelativeToTarget(destination.Position, target) != PositionalRequirement.Front;
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
