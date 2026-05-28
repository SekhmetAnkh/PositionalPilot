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
    private Candidate? currentCandidate;
    private Task<bool>? pendingMoveTask;
    private string candidateFailureReason = string.Empty;

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
    public Vector3? ChosenDestination => currentCandidate?.Position;
    public GameSnapshot LastSnapshot { get; private set; } = new(false, default, 0, 0, false, false, false, false, 0, string.Empty, default, 0, 0, false, false);

    public void Update()
    {
        RefreshDependencyStatus();
        LastSnapshot = game.Read();

        if (State == MovementState.EmergencyStopped)
            return;

        if (pendingMoveTask != null)
        {
            if (!pendingMoveTask.IsCompleted)
                return;

            if (pendingMoveTask.IsFaulted || pendingMoveTask.IsCanceled || !pendingMoveTask.Result)
            {
                var detail = pendingMoveTask.IsFaulted
                    ? pendingMoveTask.Exception?.GetBaseException().Message ?? "task faulted"
                    : pendingMoveTask.IsCanceled
                        ? "task canceled"
                        : "no vnavmesh path to selected candidate";
                pendingMoveTask = null;
                Stop($"vnavmesh path request failed: {detail}");
                EnterCooldown();
                return;
            }

            pendingMoveTask = null;
        }

        if (LastSnapshot.HasTarget && lastTargetId != 0 && LastSnapshot.TargetId != lastTargetId)
        {
            Stop("target changed");
            currentCandidate = null;
        }

        if (LastSnapshot.HasTarget)
            lastTargetId = LastSnapshot.TargetId;

        if (config.Settings.DisableDuringManualMovement && State == MovementState.Moving && LastSnapshot.IsManuallyMoving)
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
            EvaluateCandidate(LastSnapshot, positional);
            State = MovementState.Idle;
            return;
        }

        if (DateTime.UtcNow < nextRepath)
            return;

        State = MovementState.Evaluating;
        var selected = EvaluateCandidate(LastSnapshot, positional);
        if (selected == null)
        {
            BlockReason = string.IsNullOrWhiteSpace(candidateFailureReason) ? "no safe candidate" : candidateFailureReason;
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

        var navTolerance = GetVnavmeshTolerance();
        if (!vnavmesh.TryPathfindAndMoveCloseTo(selected.Position, navTolerance, out pendingMoveTask))
        {
            Stop($"vnavmesh path request failed: {vnavmesh.LastError ?? "IPC call failed"}");
            EnterCooldown();
            return;
        }

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
        rotationSolver.UnpauseOrEndSpecial();
        BlockReason = reason;
        logger.Debug(config, $"stop:{reason}", $"Movement stopped: {reason}");
    }

    private Candidate? EvaluateCandidate(GameSnapshot snapshot, PositionalRequirement positional)
    {
        candidateFailureReason = string.Empty;
        var target = new TargetSnapshot(snapshot.TargetPosition, snapshot.TargetRotation, snapshot.TargetHitboxRadius);
        var candidates = PositionalGeometry.GenerateCandidates(snapshot.PlayerPosition, target, positional, config.Settings)
            .Where(c => bossMod.IsPositionSafe(c.Position) && bossMod.IsDashSafe(snapshot.PlayerPosition, c.Position))
            .OrderBy(c => c.Score)
            .ToArray();

        if (candidates.Length == 0)
        {
            candidateFailureReason = "no BossMod-safe positional candidates";
            currentCandidate = null;
            logger.Debug(config, "candidate-count", "Candidates after BossMod safety filter: 0");
            return null;
        }

        var navTolerance = GetVnavmeshTolerance();
        var pathable = candidates
            .Where(c => HasPositionalToleranceBuffer(target, c, navTolerance))
            .Where(c => vnavmesh.CanPathfind(snapshot.PlayerPosition, c.Position, navTolerance, out _))
            .ToArray();

        if (pathable.Length == 0)
        {
            candidateFailureReason = $"no vnavmesh-pathable candidate with positional buffer from {candidates.Length} safe candidate(s)";
            currentCandidate = null;
            logger.Debug(config, "candidate-count", $"BossMod-safe candidates: {candidates.Length}; buffered vnavmesh-pathable: 0; tolerance={navTolerance:F2}");
            return null;
        }

        currentCandidate = PositionalGeometry.ApplyHysteresis(currentCandidate, pathable, config.Settings, c =>
            bossMod.IsPositionSafe(c.Position) &&
            HasPositionalToleranceBuffer(target, c, navTolerance) &&
            vnavmesh.CanPathfind(snapshot.PlayerPosition, c.Position, navTolerance, out _));
        logger.Debug(config, "candidate-count", $"BossMod-safe candidates: {candidates.Length}; buffered vnavmesh-pathable: {pathable.Length}; selected: {currentCandidate?.Position.ToString() ?? "none"}");
        return currentCandidate;
    }

    private float GetVnavmeshTolerance() => MathF.Max(config.Settings.StopWithinYalms, 1.0f);

    private static bool HasPositionalToleranceBuffer(TargetSnapshot target, Candidate candidate, float moveTolerance)
    {
        if (candidate.Requirement == PositionalRequirement.Any)
            return true;

        var radius = MathF.Max(0.1f, PositionalGeometry.DistanceXZ(target.Position, candidate.Position));
        var toleranceAngle = MathF.Asin(MathF.Min(1.0f, moveTolerance / radius));
        var guardAngle = 2.0f * MathF.PI / 180.0f;
        return candidate.AngularDeviationRadians + toleranceAngle + guardAngle <= MathF.PI / 4f;
    }

    private void EnterCooldown()
    {
        State = MovementState.Cooldown;
        nextRepath = DateTime.UtcNow.AddMilliseconds(config.Settings.RepathCooldownMs);
    }
}
