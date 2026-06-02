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
    public void BorderAnchorsAreRearFlankBorders()
    {
        var settings = new PositionalPilotSettings();
        var target = new TargetSnapshot(Vector3.Zero, 0, 1);

        var left = PositionalGeometry.CreateBorderDestination(Vector3.Zero, target, PositionalRequirement.Any, BorderSide.Left, settings);
        var right = PositionalGeometry.CreateBorderDestination(Vector3.Zero, target, PositionalRequirement.Any, BorderSide.Right, settings);

        Assert.True(left.Position.X > 0);
        Assert.True(left.Position.Z < 0);
        Assert.True(right.Position.X < 0);
        Assert.True(right.Position.Z < 0);
        Assert.True(PositionalGeometry.AngleMatchesRequirement(AngleOf(left.Position), 0, PositionalRequirement.Rear, out _));
        Assert.True(PositionalGeometry.AngleMatchesRequirement(AngleOf(left.Position), 0, PositionalRequirement.Flank, out _));
    }

    [Fact]
    public void NearestSideSelectionKeepsCurrentSafeSide()
    {
        var settings = new PositionalPilotSettings();
        var target = new TargetSnapshot(Vector3.Zero, 0, 1);

        var side = PositionalGeometry.SelectBorderSide(new Vector3(5, 0, 0), target, settings, BorderSide.Right, _ => true);

        Assert.Equal(BorderSide.Right, side);
    }

    [Fact]
    public void AnyUsesBorderAndRearFlankNudgeDeeper()
    {
        var settings = new PositionalPilotSettings { PositionalNudgeDegrees = 12 };
        var target = new TargetSnapshot(Vector3.Zero, 0, 1);

        var any = PositionalGeometry.CreateBorderDestination(Vector3.Zero, target, PositionalRequirement.Any, BorderSide.Left, settings);
        var rear = PositionalGeometry.CreateBorderDestination(Vector3.Zero, target, PositionalRequirement.Rear, BorderSide.Left, settings);
        var flank = PositionalGeometry.CreateBorderDestination(Vector3.Zero, target, PositionalRequirement.Flank, BorderSide.Left, settings);

        var anyAngle = AngleOf(any.Position);
        var rearAngle = AngleOf(rear.Position);
        var flankAngle = AngleOf(flank.Position);
        PositionalGeometry.AngleMatchesRequirement(anyAngle, 0, PositionalRequirement.Rear, out var anyRearDeviation);
        PositionalGeometry.AngleMatchesRequirement(rearAngle, 0, PositionalRequirement.Rear, out var rearDeviation);
        PositionalGeometry.AngleMatchesRequirement(flankAngle, 0, PositionalRequirement.Flank, out var flankDeviation);

        Assert.True(rearDeviation < anyRearDeviation);
        Assert.True(flankDeviation < anyRearDeviation);
    }

    private static float AngleOf(Vector3 point)
    {
        var angle = MathF.Atan2(point.X, point.Z);
        return angle < 0 ? angle + MathF.PI * 2f : angle;
    }
}
