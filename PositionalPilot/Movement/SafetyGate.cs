using System.Numerics;
using PositionalPilot.Core.Geometry;
using PositionalPilot.Core.Model;
using PositionalPilot.Game;
using PositionalPilot.IPC;

namespace PositionalPilot.Movement;

internal sealed class SafetyGate
{
    private readonly Configuration config;
    private readonly BossModIpc bossMod;
    private readonly VnavmeshIpc vnavmesh;
    private readonly RotationSolverIpc rotationSolver;

    public SafetyGate(Configuration config, BossModIpc bossMod, VnavmeshIpc vnavmesh, RotationSolverIpc rotationSolver)
    {
        this.config = config;
        this.bossMod = bossMod;
        this.vnavmesh = vnavmesh;
        this.rotationSolver = rotationSolver;
    }

    public bool CanEvaluate(GameSnapshot snapshot, out string reason)
    {
        var s = config.Settings;
        if (!s.Enabled)
            return Block("plugin disabled", out reason);
        if (s.MovementMode == MovementMode.Disabled)
            return Block("movement mode disabled", out reason);
        if (!snapshot.HasPlayer)
            return Block("player unavailable", out reason);
        if (!snapshot.HasTarget)
            return Block("no current target", out reason);
        if (s.OnlyInCombat && !snapshot.InCombat)
            return Block("not in combat", out reason);
        if (s.OnlyMeleeJobs && !GameStateReader.IsMeleeJob(snapshot.JobId))
            return Block($"not a melee job (job {snapshot.JobId})", out reason);
        if (s.RequiredDependencies.HasFlag(RequiredDependencies.RequireVnavmesh) && !vnavmesh.IsReady())
            return Block("vnavmesh unavailable or navmesh not ready", out reason);
        if (s.RequiredDependencies.HasFlag(RequiredDependencies.RequireBossModSafety) && !bossMod.TryGetRecommendedPositional(out _))
            return Block("BossMod safety/positional unavailable", out reason);
        if (s.RequiredDependencies.HasFlag(RequiredDependencies.RequireCombatSolver) && !rotationSolver.Available)
            return Block("RotationSolverReborn unavailable", out reason);
        if (s.DisableDuringCasting && snapshot.IsCasting)
            return Block("player is casting", out reason);
        if (s.DisableDuringUpcomingDamage && bossMod.TryGetNextDamageIn(out var damage) && damage <= s.UpcomingDamageBlockSeconds)
            return Block($"damage in {damage:F1}s", out reason);
        if (s.DisableDuringUpcomingKnockback && bossMod.TryGetNextKnockbackIn(out var knockback) && knockback <= s.UpcomingKnockbackBlockSeconds)
            return Block($"knockback in {knockback:F1}s", out reason);
        if (s.DisableDuringDowntime && bossMod.TryGetNextDowntimeIn(out var downtime) && downtime <= 0)
            return Block("encounter downtime active", out reason);
        if (bossMod.IsBossModNavigating())
            return Block("BossMod currently owns navigation", out reason);
        if (!snapshot.TargetAlive || !snapshot.TargetTargetable)
            return Block("target not alive/targetable", out reason);
        if (snapshot.TargetHitboxRadius <= 0)
            return Block("invalid target hitbox", out reason);

        reason = string.Empty;
        return true;
    }

    public bool CanMoveTo(GameSnapshot snapshot, Vector3 destination, out string reason)
    {
        if (!CanEvaluate(snapshot, out reason))
            return false;

        var distance = PositionalGeometry.DistanceXZ(snapshot.PlayerPosition, destination);
        if (distance > config.Settings.MaxMoveDistance)
            return Block($"destination too far ({distance:F1})", out reason);
        if (distance < config.Settings.StopWithinYalms)
            return Block("already at destination", out reason);
        if (!bossMod.IsPositionSafe(destination))
            return Block("BossMod reports destination unsafe", out reason);
        if (!bossMod.IsDashSafe(snapshot.PlayerPosition, destination))
            return Block("BossMod reports route unsafe", out reason);

        reason = string.Empty;
        return true;
    }

    private static bool Block(string value, out string reason)
    {
        reason = value;
        return false;
    }
}
