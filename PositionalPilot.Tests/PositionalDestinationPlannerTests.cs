using System.Numerics;
using PositionalPilot.Core.Geometry;
using PositionalPilot.Core.Model;
using Xunit;

namespace PositionalPilot.Tests;

public sealed class PositionalDestinationPlannerTests
{
    [Theory]
    [InlineData(PositionalRequirement.Rear)]
    [InlineData(PositionalRequirement.Flank)]
    public void CandidateEnumerationNeverReturnsFrontDestinations(PositionalRequirement requirement)
    {
        var settings = new PositionalPilotSettings();
        var target = new TargetSnapshot(Vector3.Zero, 0, 1);

        var candidates = PositionalDestinationPlanner.EnumerateCandidates(new Vector3(4, 0, -2), target, requirement, BorderSide.Left, settings).ToList();

        Assert.NotEmpty(candidates);
        Assert.All(candidates, candidate =>
            Assert.NotEqual(PositionalRequirement.Front, PositionalGeometry.ClassifyPositionRelativeToTarget(candidate.Position, target)));
    }

    [Theory]
    [InlineData(PositionalRequirement.Rear)]
    [InlineData(PositionalRequirement.Flank)]
    public void CommittedCandidatesAreInRequestedSlice(PositionalRequirement requirement)
    {
        var settings = new PositionalPilotSettings();
        var target = new TargetSnapshot(Vector3.Zero, 0, 1);

        var candidates = PositionalDestinationPlanner.EnumerateCandidates(new Vector3(4, 0, -2), target, requirement, BorderSide.Right, settings).ToList();

        Assert.NotEmpty(candidates);
        Assert.All(candidates, candidate =>
            Assert.True(PositionalGeometry.IsPositionInRequiredSlice(candidate.Position, target, requirement)));
    }

    [Fact]
    public void ScorerPrefersDeeperValidRearCandidateOverBoundary()
    {
        var settings = new PositionalPilotSettings();
        var target = new TargetSnapshot(Vector3.Zero, 0, 1);
        var player = new Vector3(3, 0, -3);
        var boundary = PositionalGeometry.CreateBorderDestination(player, target, PositionalRequirement.Rear, BorderSide.Right, settings);
        var deeper = PositionalDestinationPlanner
            .EnumerateCandidates(player, target, PositionalRequirement.Rear, BorderSide.Right, settings)
            .OrderBy(candidate => candidate.AngularDeviationRadians)
            .First();

        var boundaryScore = PositionalDestinationPlanner.ScoreCandidate(boundary, player, target, settings);
        var deeperScore = PositionalDestinationPlanner.ScoreCandidate(deeper, player, target, settings);

        Assert.True(deeperScore < boundaryScore);
    }

    [Fact]
    public void ScorerRejectsUnsafeInvalidCandidate()
    {
        var settings = new PositionalPilotSettings();
        var target = new TargetSnapshot(Vector3.Zero, 0, 1);
        var front = new BorderDestination(new Vector3(0, 0, 3), BorderSide.Right, PositionalRequirement.Rear, 3, 0, 0);

        var score = PositionalDestinationPlanner.ScoreCandidate(front, Vector3.Zero, target, settings);

        Assert.Equal(float.PositiveInfinity, score);
    }
}
