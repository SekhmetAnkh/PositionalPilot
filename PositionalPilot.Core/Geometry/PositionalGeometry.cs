using System.Numerics;
using PositionalPilot.Core.Model;

namespace PositionalPilot.Core.Geometry;

public static class PositionalGeometry
{
    private const float TwoPi = MathF.PI * 2f;
    private const float FrontHalfAngle = MathF.PI / 4f;
    private const float RearHalfAngle = MathF.PI * 3f / 4f;

    public static BorderDestination CreateBorderDestination(
        Vector3 playerPosition,
        TargetSnapshot target,
        PositionalRequirement requirement,
        BorderSide side,
        PositionalPilotSettings settings)
    {
        var direction = GetBorderDirection(target.RotationRadians, side);
        if (requirement is PositionalRequirement.Rear or PositionalRequirement.Flank)
            direction = NudgeBorderDirection(target.RotationRadians, side, requirement, DegreesToRadians(settings.PositionalNudgeDegrees));

        var radius = target.HitboxRadius + settings.DesiredDistanceFromTargetHitbox;
        var point = target.Position + direction * radius;
        var distance = DistanceXZ(playerPosition, point);
        var deviation = GetRequirementDeviation(point, target, requirement);
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
        DirectionToAngle(GetBorderDirection(targetRotation, side));

    public static Vector3 GetFaceVector(float targetRotation) =>
        NormalizeXZ(new Vector3(MathF.Sin(targetRotation), 0, MathF.Cos(targetRotation)));

    public static Vector3 GetBorderDirection(float targetRotation, BorderSide side)
    {
        var face = GetFaceVector(targetRotation);
        var rear = -face;
        var right = new Vector3(face.Z, 0, -face.X);
        var left = -right;
        return NormalizeXZ(rear + (side == BorderSide.Left ? left : right));
    }

    public static bool AngleMatchesRequirement(
        float worldAngle,
        float targetRotation,
        PositionalRequirement requirement,
        out float deviation) =>
        AngleMatchesRequirement(worldAngle, targetRotation, requirement, 0, out deviation);

    public static bool IsPositionInRequiredSlice(
        Vector3 position,
        TargetSnapshot target,
        PositionalRequirement requirement,
        float sectorMarginDegrees = 0)
    {
        if (requirement == PositionalRequirement.Any)
            return true;

        if (requirement is PositionalRequirement.Front or PositionalRequirement.None or PositionalRequirement.Unknown)
            return false;

        var angle = GetFacingAngleToPosition(position, target);
        var margin = DegreesToRadians(sectorMarginDegrees);
        return requirement == PositionalRequirement.Rear
            ? angle >= RearHalfAngle + margin
            : angle >= FrontHalfAngle + margin && angle <= RearHalfAngle - margin;
    }

    public static PositionalRequirement ClassifyPositionRelativeToTarget(Vector3 position, TargetSnapshot target)
    {
        var angle = GetFacingAngleToPosition(position, target);
        if (angle < FrontHalfAngle)
            return PositionalRequirement.Front;
        if (angle > RearHalfAngle)
            return PositionalRequirement.Rear;
        return PositionalRequirement.Flank;
    }

    public static float GetFacingAngleToPosition(Vector3 position, TargetSnapshot target)
    {
        var dir = NormalizeXZ(position - target.Position);
        if (dir == Vector3.Zero)
            return 0;

        var face = GetFaceVector(target.RotationRadians);
        var dot = Math.Clamp(Vector3.Dot(face, dir), -1.0f, 1.0f);
        return MathF.Acos(dot);
    }

    public static bool AngleMatchesRequirement(
        float worldAngle,
        float targetRotation,
        PositionalRequirement requirement,
        float sectorMarginDegrees,
        out float deviation)
    {
        deviation = 0;
        if (requirement is PositionalRequirement.None or PositionalRequirement.Front or PositionalRequirement.Unknown)
            return false;

        if (requirement == PositionalRequirement.Any)
            return true;

        var position = PointOnRing(worldAngle, 1);
        var target = new TargetSnapshot(Vector3.Zero, targetRotation, 0);
        var faceAngle = GetFacingAngleToPosition(position, target);
        var margin = DegreesToRadians(sectorMarginDegrees);

        if (requirement == PositionalRequirement.Rear)
        {
            deviation = MathF.Abs(MathF.PI - faceAngle);
            return faceAngle >= RearHalfAngle + margin;
        }

        deviation = MathF.Min(MathF.Abs(MathF.PI / 2f - faceAngle), MathF.Abs(RearHalfAngle - faceAngle));
        return faceAngle >= FrontHalfAngle + margin && faceAngle <= RearHalfAngle - margin;
    }

    public static PositionalRequirement MapBossModPositional(int raw)
    {
        // BossModReborn BossMod/Data/ActionID.cs: public enum Positional { Any, Flank, Rear, Front }
        return raw switch
        {
            0 => PositionalRequirement.Any,
            1 => PositionalRequirement.Flank,
            2 => PositionalRequirement.Rear,
            3 => PositionalRequirement.Front,
            _ => PositionalRequirement.Unknown,
        };
    }

    public static float DistanceXZ(Vector3 a, Vector3 b)
    {
        var dx = a.X - b.X;
        var dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    private static Vector3 NudgeBorderDirection(float targetRotation, BorderSide side, PositionalRequirement requirement, float nudgeRadians)
    {
        var face = GetFaceVector(targetRotation);
        var rear = -face;
        var right = new Vector3(face.Z, 0, -face.X);
        var left = -right;
        var sideDirection = side == BorderSide.Left ? left : right;
        var offsetFromRear = Math.Clamp(MathF.PI / 4f + (requirement == PositionalRequirement.Rear ? -nudgeRadians : nudgeRadians), 0, MathF.PI / 2f);

        return NormalizeXZ(rear * MathF.Cos(offsetFromRear) + sideDirection * MathF.Sin(offsetFromRear));
    }

    private static float GetRequirementDeviation(Vector3 point, TargetSnapshot target, PositionalRequirement requirement)
    {
        if (requirement == PositionalRequirement.Any)
            return 0;

        var angle = GetFacingAngleToPosition(point, target);
        if (requirement == PositionalRequirement.Rear)
            return MathF.Abs(MathF.PI - angle);
        if (requirement == PositionalRequirement.Flank)
            return MathF.Min(MathF.Abs(MathF.PI / 2f - angle), MathF.Abs(RearHalfAngle - angle));
        return MathF.PI;
    }

    private static Vector3 PointOnRing(float angle, float radius) =>
        new(MathF.Sin(angle) * radius, 0, MathF.Cos(angle) * radius);

    private static float NormalizeAngle(float angle)
    {
        angle %= TwoPi;
        return angle < 0 ? angle + TwoPi : angle;
    }

    private static Vector3 NormalizeXZ(Vector3 vector)
    {
        vector.Y = 0;
        var length = MathF.Sqrt(vector.X * vector.X + vector.Z * vector.Z);
        return length <= 0.0001f ? Vector3.Zero : vector / length;
    }

    private static float DirectionToAngle(Vector3 direction) =>
        NormalizeAngle(MathF.Atan2(direction.X, direction.Z));

    private static float DegreesToRadians(float degrees) => degrees * MathF.PI / 180f;
}
