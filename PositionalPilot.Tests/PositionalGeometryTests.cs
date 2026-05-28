using System.Numerics;
using PositionalPilot.Core.Geometry;
using PositionalPilot.Core.Model;
using Xunit;

namespace PositionalPilot.Tests;

public sealed class PositionalGeometryTests
{
    [Fact]
    public void BossModMappingMatchesVerifiedEnum()
    {
        Assert.Equal(PositionalRequirement.Any, PositionalGeometry.MapBossModPositional(0));
        Assert.Equal(PositionalRequirement.Flank, PositionalGeometry.MapBossModPositional(1));
        Assert.Equal(PositionalRequirement.Rear, PositionalGeometry.MapBossModPositional(2));
        Assert.Equal(PositionalRequirement.Unknown, PositionalGeometry.MapBossModPositional(3));
    }

    [Fact]
    public void RearAngleAcceptsBehindTarget()
    {
        var accepted = PositionalGeometry.AngleMatchesRequirement(MathF.PI, 0, PositionalRequirement.Rear, out var deviation);

        Assert.True(accepted);
        Assert.True(deviation < 0.001f);
    }

    [Fact]
    public void FlankAngleAcceptsBothSides()
    {
        Assert.True(PositionalGeometry.AngleMatchesRequirement(MathF.PI / 2, 0, PositionalRequirement.Flank, out _));
        Assert.True(PositionalGeometry.AngleMatchesRequirement(MathF.PI * 1.5f, 0, PositionalRequirement.Flank, out _));
    }

    [Fact]
    public void CandidateGenerationRespectsMaxMoveDistance()
    {
        var settings = new PositionalPilotSettings { CandidateCount = 24, MaxMoveDistance = 1 };
        var target = new TargetSnapshot(new Vector3(0, 0, 0), 0, 1);

        var candidates = PositionalGeometry.GenerateCandidates(new Vector3(20, 0, 20), target, PositionalRequirement.Rear, settings);

        Assert.Empty(candidates);
    }

    [Fact]
    public void HysteresisKeepsCurrentWhenImprovementIsSmall()
    {
        var settings = new PositionalPilotSettings { MinimumImprovementYalms = 0.75f };
        var current = new Candidate(new Vector3(0, 0, 0), PositionalRequirement.Rear, 1, 0, 1.0f);
        var best = new Candidate(new Vector3(1, 0, 0), PositionalRequirement.Rear, 1, 0, 0.5f);

        var selected = PositionalGeometry.ApplyHysteresis(current, new[] { best }, settings, _ => true);

        Assert.Same(current, selected);
    }
}
