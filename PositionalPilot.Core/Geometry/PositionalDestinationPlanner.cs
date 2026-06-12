using System.Numerics;
using PositionalPilot.Core.Model;

namespace PositionalPilot.Core.Geometry;

public static class PositionalDestinationPlanner
{
    private const int SliceSamplesPerSide = 5;
    private const float BoundaryMarginDegrees = 8f;
    private const float ScoreBoundaryMarginDegrees = 12f;
    private const float MinimumCenterLandingDistance = 1.25f;
    private const float DeepInteriorHitboxFraction = 0.2f;
    private const float ComfortableSurfaceDistance = 1.2f;

    public static IEnumerable<BorderDestination> EnumerateCandidates(
        Vector3 playerPosition,
        TargetSnapshot target,
        PositionalRequirement requirement,
        BorderSide selectedSide,
        PositionalPilotSettings settings)
    {
        if (requirement is not (PositionalRequirement.Rear or PositionalRequirement.Flank))
        {
            yield return PositionalGeometry.CreateBorderDestination(playerPosition, target, requirement, selectedSide, settings);
            yield break;
        }

        var firstSide = selectedSide is BorderSide.Left or BorderSide.Right ? selectedSide : NearestSide(playerPosition, target, settings);
        var secondSide = firstSide == BorderSide.Left ? BorderSide.Right : BorderSide.Left;
        foreach (var side in new[] { firstSide, secondSide })
            yield return PositionalGeometry.CreateBorderDestination(playerPosition, target, requirement, side, settings);

        yield return CreateAtFaceAngle(playerPosition, target, requirement, firstSide, IdealFaceAngle(requirement, firstSide), settings);

        foreach (var candidate in EnumerateSampledSliceCandidates(playerPosition, target, requirement, firstSide, settings))
            yield return candidate;
        foreach (var candidate in EnumerateSampledSliceCandidates(playerPosition, target, requirement, secondSide, settings))
            yield return candidate;
    }

    public static bool IsCandidateInMeleeRange(Vector3 position, TargetSnapshot target, PositionalPilotSettings settings)
    {
        var surfaceDistance = PositionalGeometry.DistanceXZ(position, target.Position) - target.HitboxRadius;
        return surfaceDistance <= MathF.Max(0.1f, settings.MeleeRangeYalms);
    }

    public static bool IsValidCandidate(BorderDestination candidate, TargetSnapshot target, PositionalPilotSettings settings)
    {
        if (candidate.Requirement is PositionalRequirement.Front or PositionalRequirement.None or PositionalRequirement.Unknown)
            return false;
        if (PositionalGeometry.ClassifyPositionRelativeToTarget(candidate.Position, target) == PositionalRequirement.Front)
            return false;
        if (!PositionalGeometry.IsPositionInRequiredSlice(candidate.Position, target, candidate.Requirement))
            return false;
        return IsCandidateInMeleeRange(candidate.Position, target, settings);
    }

    public static float ScoreCandidate(
        BorderDestination candidate,
        Vector3 playerPosition,
        TargetSnapshot target,
        PositionalPilotSettings settings,
        Vector3? previousDestination = null)
    {
        if (!IsValidCandidate(candidate, target, settings))
            return float.PositiveInfinity;

        var distanceFromCenter = PositionalGeometry.DistanceXZ(candidate.Position, target.Position);
        var innerLimit = MathF.Max(MinimumCenterLandingDistance, target.HitboxRadius * DeepInteriorHitboxFraction);
        var tooDeepPenalty = distanceFromCenter < innerLimit
            ? (innerLimit - distanceFromCenter) * 50f
            : 0f;
        var surfaceDistance = distanceFromCenter - target.HitboxRadius;
        var comfortablePenalty = surfaceDistance >= ComfortableSurfaceDistance
            ? 0f
            : (ComfortableSurfaceDistance - surfaceDistance) * 3f;
        var boundaryPenalty = PositionalGeometry.IsPositionInRequiredSlice(candidate.Position, target, candidate.Requirement, ScoreBoundaryMarginDegrees)
            ? 0f
            : 8f;
        var travelPenalty = PositionalGeometry.DistanceXZ(playerPosition, candidate.Position) * 0.35f;
        var jitterPenalty = previousDestination.HasValue
            ? MathF.Min(2f, PositionalGeometry.DistanceXZ(previousDestination.Value, candidate.Position) * 0.15f)
            : 0f;

        return candidate.AngularDeviationRadians * 2.5f +
               boundaryPenalty +
               comfortablePenalty +
               tooDeepPenalty +
               travelPenalty +
               jitterPenalty;
    }

    private static IEnumerable<BorderDestination> EnumerateSampledSliceCandidates(
        Vector3 playerPosition,
        TargetSnapshot target,
        PositionalRequirement requirement,
        BorderSide side,
        PositionalPilotSettings settings)
    {
        var (min, max) = FaceAngleRange(requirement, side);
        var margin = DegreesToRadians(BoundaryMarginDegrees);
        min += margin;
        max -= margin;
        if (min > max)
            yield break;

        for (var i = 0; i < SliceSamplesPerSide; i++)
        {
            var t = SliceSamplesPerSide == 1 ? 0.5f : i / (float)(SliceSamplesPerSide - 1);
            yield return CreateAtFaceAngle(playerPosition, target, requirement, side, Lerp(min, max, t), settings);
        }
    }

    private static BorderDestination CreateAtFaceAngle(
        Vector3 playerPosition,
        TargetSnapshot target,
        PositionalRequirement requirement,
        BorderSide side,
        float faceAngle,
        PositionalPilotSettings settings)
    {
        var worldAngle = target.RotationRadians + faceAngle;
        var radius = target.HitboxRadius + settings.DesiredDistanceFromTargetHitbox;
        var direction = new Vector3(MathF.Sin(worldAngle), 0, MathF.Cos(worldAngle));
        var point = target.Position + direction * radius;
        point.Y = playerPosition.Y;
        var distance = PositionalGeometry.DistanceXZ(playerPosition, point);
        var deviation = requirement == PositionalRequirement.Rear
            ? MathF.Abs(MathF.PI - MathF.Abs(faceAngle))
            : MathF.Min(MathF.Abs(MathF.PI / 2f - MathF.Abs(faceAngle)), MathF.Abs(MathF.PI * 3f / 4f - MathF.Abs(faceAngle)));
        return new BorderDestination(point, side, requirement, distance, deviation, distance + deviation * 1.5f);
    }

    private static (float Min, float Max) FaceAngleRange(PositionalRequirement requirement, BorderSide side)
    {
        if (requirement == PositionalRequirement.Rear)
            return side == BorderSide.Left
                ? (-MathF.PI, -MathF.PI * 3f / 4f)
                : (MathF.PI * 3f / 4f, MathF.PI);

        return side == BorderSide.Left
            ? (-MathF.PI * 3f / 4f, -MathF.PI / 4f)
            : (MathF.PI / 4f, MathF.PI * 3f / 4f);
    }

    private static float IdealFaceAngle(PositionalRequirement requirement, BorderSide side)
    {
        if (requirement == PositionalRequirement.Rear)
            return side == BorderSide.Left ? -MathF.PI : MathF.PI;
        return side == BorderSide.Left ? -MathF.PI / 2f : MathF.PI / 2f;
    }

    private static BorderSide NearestSide(Vector3 playerPosition, TargetSnapshot target, PositionalPilotSettings settings)
    {
        var left = PositionalGeometry.CreateBorderDestination(playerPosition, target, PositionalRequirement.Any, BorderSide.Left, settings);
        var right = PositionalGeometry.CreateBorderDestination(playerPosition, target, PositionalRequirement.Any, BorderSide.Right, settings);
        return left.DistanceFromPlayer <= right.DistanceFromPlayer ? BorderSide.Left : BorderSide.Right;
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static float DegreesToRadians(float degrees) => degrees * MathF.PI / 180f;
}
