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

    public SafetyGate(Configuration config, BossModIpc bossMod)
    {
        this.config = config;
        this.bossMod = bossMod;
    }

    public bool CanEvaluate(GameSnapshot snapshot, CachedSafetyState safety, out string reason)
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
        if (snapshot.TargetOmnidirectional == true)
            return Block("target does not require positionals", out reason);
        if (PositionalMovementRules.ShouldBlockForTargetOfTarget(snapshot.TargetTargetsPlayer, snapshot.TargetIsTrainingDummy))
            return Block("target is targeting player", out reason);
        if (s.RequiredDependencies.HasFlag(RequiredDependencies.RequireVnavmesh) && !safety.VnavmeshReady)
            return Block("vnavmesh unavailable or navmesh not ready", out reason);
        if (s.RequiredDependencies.HasFlag(RequiredDependencies.RequireBossModSafety) && !bossMod.Available)
            return Block("BossMod safety unavailable", out reason);
        if (s.RequiredDependencies.HasFlag(RequiredDependencies.RequireCombatSolver) && !safety.RotationSolverAvailable)
            return Block("selected combat intent source unavailable", out reason);
        if (s.DisableDuringCasting && snapshot.IsCasting)
            return Block("player is casting", out reason);
        if (s.DisableDuringUpcomingDamage && safety.NextDamageIn is { } damage && damage <= s.UpcomingDamageBlockSeconds)
            return Block($"damage in {damage:F1}s", out reason);
        if (s.DisableDuringUpcomingKnockback && safety.NextKnockbackIn is { } knockback && knockback <= s.UpcomingKnockbackBlockSeconds)
            return Block($"knockback in {knockback:F1}s", out reason);
        if (s.DisableDuringDowntime && safety.NextDowntimeIn is { } downtime && downtime <= 0)
            return Block("encounter downtime active", out reason);
        if (safety.BossModNavigating)
            return Block("BossMod currently owns navigation", out reason);
        if (safety.BossModHasNaviTarget)
            return Block("BossMod has navigation target", out reason);
        if (!snapshot.TargetAlive || !snapshot.TargetTargetable)
            return Block("target not alive/targetable", out reason);
        if (snapshot.TargetHitboxRadius <= 0)
            return Block("invalid target hitbox", out reason);

        reason = string.Empty;
        return true;
    }

    public bool CanMoveTo(GameSnapshot snapshot, CachedSafetyState safety, Vector3 destination, out string reason)
    {
        if (!CanEvaluate(snapshot, safety, out reason))
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
