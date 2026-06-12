using PositionalPilot.Core.Geometry;
using PositionalPilot.Core.Model;
using PositionalPilot.Game;

namespace PositionalPilot.Movement;

internal static class TrueNorthFallbackPolicy
{
    public static bool ShouldUseTrueNorth(
        GameSnapshot snapshot,
        PositionalRequirement requirement,
        PositionalPilotSettings settings,
        out string reason)
    {
        if (!settings.EnableTrueNorthFallback)
            return Block("True North fallback disabled", out reason);
        if (!PositionalMovementRules.IsCommittedPositional(requirement))
            return Block("no committed positional", out reason);
        if (!GameStateReader.IsMeleeJob(snapshot.JobId))
            return Block("not a melee job", out reason);
        if (!snapshot.HasPlayer || !snapshot.HasTarget || !snapshot.TargetAlive || !snapshot.TargetTargetable)
            return Block("missing valid target", out reason);
        if (snapshot.TargetOmnidirectional != false)
            return Block("target does not require positionals", out reason);
        if (snapshot.IsCasting)
            return Block("player is casting", out reason);
        if (!snapshot.TrueNorthAvailable)
            return Block("True North unavailable", out reason);

        var target = new TargetSnapshot(snapshot.TargetPosition, snapshot.TargetRotation, snapshot.TargetHitboxRadius);
        if (PositionalGeometry.IsPositionInRequiredSlice(snapshot.PlayerPosition, target, requirement))
            return Block("already in required slice", out reason);
        if (IsOutsideMeleeRange(snapshot, settings))
            return Block("outside melee range", out reason);

        reason = "movement unavailable; using True North fallback";
        return true;
    }

    public static bool IsOutsideMeleeRange(GameSnapshot snapshot, PositionalPilotSettings settings)
    {
        if (!snapshot.HasTarget)
            return false;

        var distanceFromHitbox = PositionalGeometry.DistanceXZ(snapshot.PlayerPosition, snapshot.TargetPosition) - snapshot.TargetHitboxRadius;
        return distanceFromHitbox > MathF.Max(0.1f, settings.MeleeRangeYalms);
    }

    private static bool Block(string value, out string reason)
    {
        reason = value;
        return false;
    }
}
