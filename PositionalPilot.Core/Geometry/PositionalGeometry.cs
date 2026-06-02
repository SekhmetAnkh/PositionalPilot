using System.Numerics;
using PositionalPilot.Core.Model;

namespace PositionalPilot.Core.Geometry;

public static class PositionalGeometry
{
    private const float TwoPi = MathF.PI * 2f;
    private const float RearFlankBorderOffset = MathF.PI * 3f / 4f;

    public static BorderDestination CreateBorderDestination(
        Vector3 playerPosition,
        TargetSnapshot target,
        PositionalRequirement requirement,
        BorderSide side,
        PositionalPilotSettings settings)
    {
        var angle = GetBorderAngle(target.RotationRadians, side);
        if (requirement is PositionalRequirement.Rear or PositionalRequirement.Flank)
            angle = NudgeBorderAngle(angle, side, requirement, DegreesToRadians(settings.PositionalNudgeDegrees));

        var radius = target.HitboxRadius + settings.DesiredDistanceFromTargetHitbox;
        var point = target.Position + PointOnRing(angle, radius);
        var distance = DistanceXZ(playerPosition, point);
        var deviation = GetRequirementDeviation(angle, target.RotationRadians, requirement);
        var score = distance + deviation * 1.5f;
        return new BorderDestination(point, side, requirement, distance, deviation, score);
    }

    public static BorderSide SelectBorderSide(
        Vector3 playerPosition,
        TargetSnapshot target,
        PositionalPilotSettings settings,
        BorderSide currentSide,
        Func<BorderSide, bool> sideStillSafe)
    {
        if (settings.BorderSideMode == BorderSideMode.Left)
            return sideStillSafe(BorderSide.Left) || !sideStillSafe(BorderSide.Right) ? BorderSide.Left : BorderSide.Right;
        if (settings.BorderSideMode == BorderSideMode.Right)
            return sideStillSafe(BorderSide.Right) || !sideStillSafe(BorderSide.Left) ? BorderSide.Right : BorderSide.Left;

        if (currentSide != BorderSide.None && sideStillSafe(currentSide))
            return currentSide;

        var left = CreateBorderDestination(playerPosition, target, PositionalRequirement.Any, BorderSide.Left, settings);
        var right = CreateBorderDestination(playerPosition, target, PositionalRequirement.Any, BorderSide.Right, settings);
        var leftSafe = sideStillSafe(BorderSide.Left);
        var rightSafe = sideStillSafe(BorderSide.Right);
        if (leftSafe && !rightSafe)
            return BorderSide.Left;
        if (rightSafe && !leftSafe)
            return BorderSide.Right;

        return left.DistanceFromPlayer <= right.DistanceFromPlayer ? BorderSide.Left : BorderSide.Right;
    }

    public static float GetBorderAngle(float targetRotation, BorderSide side) =>
        side == BorderSide.Left
            ? NormalizeAngle(targetRotation + RearFlankBorderOffset)
            : NormalizeAngle(targetRotation - RearFlankBorderOffset);

    public static bool AngleMatchesRequirement(
        float worldAngle,
        float targetRotation,
        PositionalRequirement requirement,
        out float deviation) =>
        AngleMatchesRequirement(worldAngle, targetRotation, requirement, 0, out deviation);

    public static bool AngleMatchesRequirement(
        float worldAngle,
        float targetRotation,
        PositionalRequirement requirement,
        float sectorMarginDegrees,
        out float deviation)
    {
        deviation = 0;
        if (requirement is PositionalRequirement.None or PositionalRequirement.Unknown)
            return false;

        if (requirement == PositionalRequirement.Any)
            return true;

        var rear = NormalizeAngle(targetRotation + MathF.PI);
        var leftFlank = NormalizeAngle(targetRotation + MathF.PI / 2f);
        var rightFlank = NormalizeAngle(targetRotation - MathF.PI / 2f);
        var sectorHalfAngle = MathF.Max(0, MathF.PI / 4f - DegreesToRadians(sectorMarginDegrees));

        if (requirement == PositionalRequirement.Rear)
        {
            deviation = AbsAngleDelta(worldAngle, rear);
            return deviation <= sectorHalfAngle;
        }

        var leftDev = AbsAngleDelta(worldAngle, leftFlank);
        var rightDev = AbsAngleDelta(worldAngle, rightFlank);
        deviation = MathF.Min(leftDev, rightDev);
        return deviation <= sectorHalfAngle;
    }

    public static PositionalRequirement MapBossModPositional(int raw)
    {
        // BossModReborn BossMod/Data/ActionID.cs: public enum Positional { Any, Flank, Rear, Front }
        return raw switch
        {
            0 => PositionalRequirement.Any,
            1 => PositionalRequirement.Flank,
            2 => PositionalRequirement.Rear,
            3 => PositionalRequirement.Unknown,
            _ => PositionalRequirement.Unknown,
        };
    }

    public static float DistanceXZ(Vector3 a, Vector3 b)
    {
        var dx = a.X - b.X;
        var dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    private static float NudgeBorderAngle(float borderAngle, BorderSide side, PositionalRequirement requirement, float nudgeRadians)
    {
        if (side == BorderSide.Left)
            return NormalizeAngle(borderAngle + (requirement == PositionalRequirement.Rear ? nudgeRadians : -nudgeRadians));

        return NormalizeAngle(borderAngle + (requirement == PositionalRequirement.Rear ? -nudgeRadians : nudgeRadians));
    }

    private static float GetRequirementDeviation(float angle, float targetRotation, PositionalRequirement requirement)
    {
        if (requirement == PositionalRequirement.Any)
            return 0;

        AngleMatchesRequirement(angle, targetRotation, requirement, out var deviation);
        return deviation;
    }

    private static Vector3 PointOnRing(float angle, float radius) =>
        new(MathF.Sin(angle) * radius, 0, MathF.Cos(angle) * radius);

    private static float AbsAngleDelta(float a, float b)
    {
        var delta = NormalizeAngle(a - b);
        if (delta > MathF.PI)
            delta -= TwoPi;
        return MathF.Abs(delta);
    }

    private static float NormalizeAngle(float angle)
    {
        angle %= TwoPi;
        return angle < 0 ? angle + TwoPi : angle;
    }

    private static float DegreesToRadians(float degrees) => degrees * MathF.PI / 180f;
}
