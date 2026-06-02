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
    private ulong lastTargetId;
    private BorderDestination? currentDestination;
    private BorderSide selectedSide = BorderSide.None;
    private string destinationFailureReason = string.Empty;
    private Vector3? lastFailedPathDestination;
    private DateTime lastFailedPathTime = DateTime.MinValue;

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
    public GameSnapshot LastSnapshot { get; private set; } = new(false, default, 0, 0, false, false, false, false, 0, string.Empty, default, 0, 0, false, false);

    public void Update()
    {
        RefreshDependencyStatus();
        LastSnapshot = game.Read();

        if (State == MovementState.EmergencyStopped)
            return;

        if (LastSnapshot.HasTarget && lastTargetId != 0 && LastSnapshot.TargetId != lastTargetId)
        {
            Stop("target changed");
            currentDestination = null;
            selectedSide = BorderSide.None;
        }

        if (LastSnapshot.HasTarget)
            lastTargetId = LastSnapshot.TargetId;

        if (config.Settings.DisableDuringManualMovement &&
            State == MovementState.Moving &&
            LastSnapshot.IsManuallyMoving &&
            !vnavmesh.IsNavigating())
        {
            Stop("manual movement detected");
            EnterCooldown();
            return;
        }

        if (!safety.CanEvaluate(LastSnapshot, out var reason))
        {
            BlockReason = reason;
            if (State == MovementState.Moving)
                Stop(reason);
            State = State == MovementState.Cooldown ? State : MovementState.Blocked;
            return;
        }

        if (!bossMod.TryGetRecommendedPositional(out var positional) ||
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

        if (config.Settings.MovementMode == MovementMode.SuggestOnly)
        {
            EvaluateDestination(LastSnapshot, positional);
            State = MovementState.Idle;
            return;
        }

        if (DateTime.UtcNow < nextRepath)
            return;

        var wasMoving = State == MovementState.Moving;
        var previousDestination = currentDestination?.Position;
        State = MovementState.Evaluating;
        var selected = EvaluateDestination(LastSnapshot, positional);
        if (selected == null)
        {
            BlockReason = string.IsNullOrWhiteSpace(destinationFailureReason) ? "no safe destination" : destinationFailureReason;
            State = MovementState.Blocked;
            return;
        }

        if (!safety.CanMoveTo(LastSnapshot, selected.Position, out reason))
        {
            BlockReason = reason;
            State = MovementState.Blocked;
            return;
        }

        if (PositionalGeometry.DistanceXZ(LastSnapshot.PlayerPosition, selected.Position) <= config.Settings.StopWithinYalms)
        {
            Stop("destination reached");
            State = MovementState.Idle;
            return;
        }

        if (wasMoving &&
            vnavmesh.IsNavigating() &&
            previousDestination.HasValue &&
            PositionalGeometry.DistanceXZ(previousDestination.Value, selected.Position) < config.Settings.RetargetThresholdYalms)
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

        if (config.Settings.EnableRotationSolverCoordination)
            rotationSolver.PauseOrNoCasting(MathF.Max(0.25f, config.Settings.RepathCooldownMs / 1000f));
        BlockReason = string.Empty;
        State = MovementState.Moving;
        nextRepath = DateTime.UtcNow.AddMilliseconds(config.Settings.RepathCooldownMs);
        logger.Debug(config, "movement-start", $"Moving to {selected.Position} for {positional}");
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

    public void RefreshDependencyStatus()
    {
        bossMod.RefreshAvailability();
        vnavmesh.RefreshAvailability();
        rotationSolver.RefreshAvailability();
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

        var navTolerance = GetVnavmeshTolerance();
        var destination = PositionalGeometry.CreateBorderDestination(snapshot.PlayerPosition, target, positional, selectedSide, config.Settings);
        if (!HasPositionalToleranceBuffer(target, destination, navTolerance))
        {
            destinationFailureReason = "border destination too close to positional edge";
            currentDestination = null;
            logger.Debug(config, "border-destination", $"{destinationFailureReason}; side={selectedSide}; positional={positional}; tolerance={navTolerance:F2}");
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

    private float GetVnavmeshTolerance() => MathF.Max(config.Settings.StopWithinYalms, 1.0f);

    private static bool HasPositionalToleranceBuffer(TargetSnapshot target, BorderDestination destination, float moveTolerance)
    {
        if (destination.Requirement == PositionalRequirement.Any)
            return true;

        var radius = MathF.Max(0.1f, PositionalGeometry.DistanceXZ(target.Position, destination.Position));
        var toleranceAngle = MathF.Asin(MathF.Min(1.0f, moveTolerance / radius));
        var guardAngle = 2.0f * MathF.PI / 180.0f;
        return destination.AngularDeviationRadians + toleranceAngle + guardAngle <= MathF.PI / 4f;
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
