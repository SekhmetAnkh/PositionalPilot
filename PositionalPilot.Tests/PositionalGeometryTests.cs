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
        Assert.Equal(PositionalRequirement.Front, PositionalGeometry.MapBossModPositional(3));
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
    public void PositionSliceDetectsRearAndFlank()
    {
        var target = new TargetSnapshot(Vector3.Zero, 0, 1);

        Assert.True(PositionalGeometry.IsPositionInRequiredSlice(new Vector3(0, 0, -4), target, PositionalRequirement.Rear));
        Assert.True(PositionalGeometry.IsPositionInRequiredSlice(new Vector3(4, 0, 0), target, PositionalRequirement.Flank));
        Assert.False(PositionalGeometry.IsPositionInRequiredSlice(new Vector3(0, 0, 4), target, PositionalRequirement.Rear));
    }

    [Theory]
    [InlineData(3554, PositionalRequirement.Flank)]
    [InlineData(3556, PositionalRequirement.Rear)]
    [InlineData(66, PositionalRequirement.Rear)]
    [InlineData(56, PositionalRequirement.Flank)]
    [InlineData(2255, PositionalRequirement.Rear)]
    [InlineData(3563, PositionalRequirement.Flank)]
    [InlineData(24382, PositionalRequirement.Flank)]
    [InlineData(24383, PositionalRequirement.Rear)]
    [InlineData(7481, PositionalRequirement.Rear)]
    [InlineData(7482, PositionalRequirement.Flank)]
    [InlineData(34610, PositionalRequirement.Flank)]
    [InlineData(34612, PositionalRequirement.Rear)]
    [InlineData(34621, PositionalRequirement.Flank)]
    [InlineData(34622, PositionalRequirement.Rear)]
    public void RotationSolverMeleePositionalMapCoversKnownActions(uint actionId, PositionalRequirement expected)
    {
        Assert.True(PositionalActionMap.TryGetRequirement(actionId, out var requirement));
        Assert.Equal(expected, requirement);
    }

    [Fact]
    public void RotationSolverMeleePositionalMapFailsClosedForUnknownActions()
    {
        Assert.False(PositionalActionMap.TryGetRequirement(999999, out _));
    }

    [Fact]
    public void FrontIsNeverTreatedAsAValidPpilotSlice()
    {
        var target = new TargetSnapshot(Vector3.Zero, 0, 1);

        Assert.False(PositionalGeometry.AngleMatchesRequirement(0, 0, PositionalRequirement.Front, out _));
        Assert.False(PositionalGeometry.IsPositionInRequiredSlice(new Vector3(0, 0, 4), target, PositionalRequirement.Front));
        Assert.Equal("front blocked", PositionalMovementRules.MovementModeName(PositionalRequirement.Front));
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

    [Fact]
    public void CommittedPositionalAngleMovesDeeperThanOldNudge()
    {
        var oldSettings = new PositionalPilotSettings { PositionalNudgeDegrees = 12 };
        var newSettings = new PositionalPilotSettings { PositionalNudgeDegrees = 30 };
        var target = new TargetSnapshot(Vector3.Zero, 0, 1);

        var oldRear = PositionalGeometry.CreateBorderDestination(Vector3.Zero, target, PositionalRequirement.Rear, BorderSide.Left, oldSettings);
        var newRear = PositionalGeometry.CreateBorderDestination(Vector3.Zero, target, PositionalRequirement.Rear, BorderSide.Left, newSettings);
        var oldFlank = PositionalGeometry.CreateBorderDestination(Vector3.Zero, target, PositionalRequirement.Flank, BorderSide.Left, oldSettings);
        var newFlank = PositionalGeometry.CreateBorderDestination(Vector3.Zero, target, PositionalRequirement.Flank, BorderSide.Left, newSettings);

        Assert.True(newRear.AngularDeviationRadians < oldRear.AngularDeviationRadians);
        Assert.True(newFlank.AngularDeviationRadians < oldFlank.AngularDeviationRadians);
        Assert.True(PositionalGeometry.IsPositionInRequiredSlice(newRear.Position, target, PositionalRequirement.Rear));
        Assert.True(PositionalGeometry.IsPositionInRequiredSlice(newFlank.Position, target, PositionalRequirement.Flank));
    }

    [Fact]
    public void BorderDeadzoneDoesNotApplyToCommittedPositionals()
    {
        var settings = new PositionalPilotSettings
        {
            BorderHoldDeadzoneYalms = 1.25f,
            PositionalCommitDeadzoneYalms = 0.35f,
        };

        Assert.True(PositionalGeometry.DistanceXZ(Vector3.Zero, new Vector3(0.8f, 0, 0)) <= settings.BorderHoldDeadzoneYalms);
        Assert.True(PositionalGeometry.DistanceXZ(Vector3.Zero, new Vector3(0.8f, 0, 0)) > settings.PositionalCommitDeadzoneYalms);
    }

    [Fact]
    public void RsrNextGcdChangeBypassesCooldownForCommittedPositionals()
    {
        Assert.True(PositionalMovementRules.ShouldBypassRepathCooldown(PositionalRequirement.Any, PositionalRequirement.Rear, 0, 34622));
        Assert.True(PositionalMovementRules.ShouldBypassRepathCooldown(PositionalRequirement.Rear, PositionalRequirement.Flank, 34622, 34621));
        Assert.False(PositionalMovementRules.ShouldBypassRepathCooldown(PositionalRequirement.Any, PositionalRequirement.Any, 34622, 34621));
    }

    private static float AngleOf(Vector3 point)
    {
        var angle = MathF.Atan2(point.X, point.Z);
        return angle < 0 ? angle + MathF.PI * 2f : angle;
    }
}
