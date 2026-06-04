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

    [Theory]
    [InlineData(7478, PositionalRequirement.Rear)] // SAM Jinpu -> Gekko
    [InlineData(7479, PositionalRequirement.Flank)] // SAM Shifu -> Kasha
    [InlineData(24382, PositionalRequirement.Rear)] // RPR Gibbet -> Gallows
    [InlineData(24383, PositionalRequirement.Flank)] // RPR Gallows -> Gibbet
    [InlineData(34621, PositionalRequirement.Rear)] // VPR Hunter's Coil -> Swiftskin's Coil
    [InlineData(34622, PositionalRequirement.Flank)] // VPR Swiftskin's Coil -> Hunter's Coil
    public void WrathComboLastGcdInferenceCoversKnownTransitions(uint actionId, PositionalRequirement expected)
    {
        Assert.True(PositionalActionInference.TryInferWrathNextRequirement(actionId, out var requirement));
        Assert.Equal(expected, requirement);
    }

    [Fact]
    public void WrathComboInferenceFailsClosedForUnknownActions()
    {
        Assert.False(PositionalActionInference.TryInferWrathNextRequirement(61, out _)); // MNK Twin Snakes can branch.
        Assert.False(PositionalActionInference.TryInferWrathNextRequirement(54, out _)); // MNK True Strike can branch by Coeurl stacks.
        Assert.False(PositionalActionInference.TryInferWrathNextRequirement(34608, out _)); // VPR Hunter's Sting needs venom state.
        Assert.False(PositionalActionInference.TryInferWrathNextRequirement(999999, out _));
    }

    [Fact]
    public void FrontIsNeverTreatedAsAValidDestinationButCanTriggerEscape()
    {
        var target = new TargetSnapshot(Vector3.Zero, 0, 1);

        Assert.False(PositionalGeometry.AngleMatchesRequirement(0, 0, PositionalRequirement.Front, out _));
        Assert.False(PositionalGeometry.IsPositionInRequiredSlice(new Vector3(0, 0, 4), target, PositionalRequirement.Front));
        Assert.Equal("front escape", PositionalMovementRules.MovementModeName(PositionalRequirement.Front));
        Assert.True(PositionalMovementRules.ShouldBypassRepathCooldown(PositionalRequirement.Any, PositionalRequirement.Any, 0, 0, true));
        Assert.True(PositionalMovementRules.CanFrontEscape(true, false));
        Assert.True(PositionalMovementRules.CanFrontEscape(true, null));
        Assert.False(PositionalMovementRules.CanFrontEscape(true, true));
        Assert.False(PositionalMovementRules.CanFrontEscape(false, false));
    }

    [Fact]
    public void TargetOfTargetOnlyBlocksWhenConfirmed()
    {
        Assert.True(PositionalMovementRules.ShouldBlockForTargetOfTarget(true));
        Assert.False(PositionalMovementRules.ShouldBlockForTargetOfTarget(false));
        Assert.False(PositionalMovementRules.ShouldBlockForTargetOfTarget(null));
    }

    [Fact]
    public void BorderAnchorsAreRearFlankBorders()
    {
        var settings = new PositionalPilotSettings();
        var target = new TargetSnapshot(Vector3.Zero, 0, 1);

        var left = PositionalGeometry.CreateBorderDestination(Vector3.Zero, target, PositionalRequirement.Any, BorderSide.Left, settings);
        var right = PositionalGeometry.CreateBorderDestination(Vector3.Zero, target, PositionalRequirement.Any, BorderSide.Right, settings);

        Assert.True(left.Position.Z < 0);
        Assert.True(right.Position.Z < 0);
        Assert.True(MathF.Sign(left.Position.X) != MathF.Sign(right.Position.X));
        AssertBorderAnchor(target, left.Position);
        AssertBorderAnchor(target, right.Position);
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

        var anyAngle = PositionalGeometry.GetFacingAngleToPosition(any.Position, target);
        var rearAngle = PositionalGeometry.GetFacingAngleToPosition(rear.Position, target);
        var flankAngle = PositionalGeometry.GetFacingAngleToPosition(flank.Position, target);

        Assert.True(rearAngle > anyAngle);
        Assert.True(flankAngle < anyAngle);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(1.5707964f)]
    [InlineData(3.1415927f)]
    [InlineData(-1.5707964f)]
    public void BorderAnchorsStayOnRearFlankBoundaryAcrossRotations(float rotation)
    {
        var settings = new PositionalPilotSettings();
        var target = new TargetSnapshot(Vector3.Zero, rotation, 1);

        var left = PositionalGeometry.CreateBorderDestination(Vector3.Zero, target, PositionalRequirement.Any, BorderSide.Left, settings);
        var right = PositionalGeometry.CreateBorderDestination(Vector3.Zero, target, PositionalRequirement.Any, BorderSide.Right, settings);

        AssertBorderAnchor(target, left.Position);
        AssertBorderAnchor(target, right.Position);
    }

    [Fact]
    public void FrontFlankBoundaryIsRejectedForBorderHold()
    {
        var target = new TargetSnapshot(Vector3.Zero, 0, 1);
        var frontRight = Vector3.Normalize(PositionalGeometry.GetFaceVector(0) + new Vector3(1, 0, 0)) * 4;
        var frontLeft = Vector3.Normalize(PositionalGeometry.GetFaceVector(0) + new Vector3(-1, 0, 0)) * 4;

        Assert.True(PositionalGeometry.GetFacingAngleToPosition(frontRight, target) < MathF.PI * 3f / 4f);
        Assert.True(PositionalGeometry.GetFacingAngleToPosition(frontLeft, target) < MathF.PI * 3f / 4f);
        Assert.False(PositionalGeometry.ClassifyPositionRelativeToTarget(frontRight, target) == PositionalRequirement.Rear);
        Assert.False(PositionalGeometry.ClassifyPositionRelativeToTarget(frontLeft, target) == PositionalRequirement.Rear);
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

        var oldRearAngle = PositionalGeometry.GetFacingAngleToPosition(oldRear.Position, target);
        var newRearAngle = PositionalGeometry.GetFacingAngleToPosition(newRear.Position, target);
        var oldFlankAngle = PositionalGeometry.GetFacingAngleToPosition(oldFlank.Position, target);
        var newFlankAngle = PositionalGeometry.GetFacingAngleToPosition(newFlank.Position, target);

        Assert.True(newRearAngle > oldRearAngle);
        Assert.True(newFlankAngle < oldFlankAngle);
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

    [Fact]
    public void RsrNextGcdDrivesCommittedMovementForAnyMappedMeleeAction()
    {
        var now = DateTime.UtcNow;

        var resolved = PositionalMovementRules.ResolveRsrMovementRequirement(
            PositionalRequirement.Flank,
            now,
            PositionalRequirement.Rear,
            now,
            now,
            1500,
            out var source);

        Assert.Equal(PositionalRequirement.Flank, resolved);
        Assert.Equal("RSR next GCD", source);
    }

    [Fact]
    public void RsrNextActionFallbackDrivesCommittedMovementWhenNextGcdIsUnknown()
    {
        var now = DateTime.UtcNow;

        var resolved = PositionalMovementRules.ResolveRsrMovementRequirement(
            PositionalRequirement.Unknown,
            now,
            PositionalRequirement.Flank,
            now,
            now,
            1500,
            out var source);

        Assert.Equal(PositionalRequirement.Flank, resolved);
        Assert.Equal("RSR next action", source);
    }

    [Fact]
    public void StaleOrUnknownRsrDataFallsBackToRearFlankBorderHold()
    {
        var now = DateTime.UtcNow;

        var resolved = PositionalMovementRules.ResolveRsrMovementRequirement(
            PositionalRequirement.Flank,
            now.AddSeconds(-5),
            PositionalRequirement.Unknown,
            now,
            now,
            1500,
            out var source);

        Assert.Equal(PositionalRequirement.Any, resolved);
        Assert.Equal("nearest rear/flank border", source);
    }

    [Fact]
    public void AnyBorderHoldRemainsRearFlankOnly()
    {
        var settings = new PositionalPilotSettings();
        var target = new TargetSnapshot(Vector3.Zero, 0, 1);

        var left = PositionalGeometry.CreateBorderDestination(Vector3.Zero, target, PositionalRequirement.Any, BorderSide.Left, settings);
        var right = PositionalGeometry.CreateBorderDestination(Vector3.Zero, target, PositionalRequirement.Any, BorderSide.Right, settings);

        Assert.True(left.Position.Z < 0);
        Assert.True(right.Position.Z < 0);
        Assert.True(MathF.Sign(left.Position.X) != MathF.Sign(right.Position.X));
        AssertBorderAnchor(target, left.Position);
        AssertBorderAnchor(target, right.Position);
        Assert.False(PositionalGeometry.IsPositionInRequiredSlice(left.Position, target, PositionalRequirement.Front));
        Assert.False(PositionalGeometry.IsPositionInRequiredSlice(right.Position, target, PositionalRequirement.Front));
    }

    private static void AssertBorderAnchor(TargetSnapshot target, Vector3 position)
    {
        var angle = PositionalGeometry.GetFacingAngleToPosition(position, target);
        Assert.InRange(angle, MathF.PI * 3f / 4f - 0.001f, MathF.PI * 3f / 4f + 0.001f);
        Assert.True(PositionalGeometry.ClassifyPositionRelativeToTarget(position, target) is PositionalRequirement.Flank or PositionalRequirement.Rear);
        Assert.NotEqual(PositionalRequirement.Front, PositionalGeometry.ClassifyPositionRelativeToTarget(position, target));
    }

    private static float AngleOf(Vector3 point)
    {
        var angle = MathF.Atan2(point.X, point.Z);
        return angle < 0 ? angle + MathF.PI * 2f : angle;
    }
}
