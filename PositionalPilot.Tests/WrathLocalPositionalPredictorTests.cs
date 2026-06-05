using PositionalPilot.Core.Model;
using Xunit;

namespace PositionalPilot.Tests;

public sealed class WrathLocalPositionalPredictorTests
{
    private static readonly DateTime Now = new(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(7478, PositionalRequirement.Rear)]
    [InlineData(7479, PositionalRequirement.Flank)]
    public void SamuraiComboActionPredictsPositional(uint comboActionId, PositionalRequirement expected)
    {
        var prediction = Predict(new WrathLocalPredictionSnapshot
        {
            JobId = 34,
            PlayerLevel = 100,
            ComboActionId = comboActionId,
        });

        Assert.Equal(expected, prediction.Requirement);
        Assert.True(prediction.IsFreshOrUsable);
    }

    [Fact]
    public void SamuraiMeikyoPredictsGekkoWhenGetsuMissingAndKaPresent()
    {
        var prediction = Predict(new WrathLocalPredictionSnapshot
        {
            JobId = 34,
            PlayerLevel = 100,
            PlayerStatusIds = new[] { 1233u },
            SamuraiHasGetsu = false,
            SamuraiHasKa = true,
            EnableSamMeikyoAnticipation = true,
        });

        Assert.Equal(PositionalRequirement.Rear, prediction.Requirement);
    }

    [Fact]
    public void SamuraiMeikyoCanBeDisabled()
    {
        var prediction = Predict(new WrathLocalPredictionSnapshot
        {
            JobId = 34,
            PlayerLevel = 100,
            PlayerStatusIds = new[] { 1233u },
            SamuraiHasGetsu = false,
            SamuraiHasKa = true,
            EnableSamMeikyoAnticipation = false,
        });

        Assert.Equal(PositionalRequirement.Unknown, prediction.Requirement);
    }

    [Theory]
    [InlineData(87, PositionalRequirement.Rear)]
    [InlineData(36955, PositionalRequirement.Rear)]
    [InlineData(84, PositionalRequirement.Flank)]
    [InlineData(25771, PositionalRequirement.Flank)]
    public void DragoonComboActionPredictsPositional(uint comboActionId, PositionalRequirement expected)
    {
        var prediction = Predict(new WrathLocalPredictionSnapshot
        {
            JobId = 22,
            PlayerLevel = 100,
            ComboActionId = comboActionId,
        });

        Assert.Equal(expected, prediction.Requirement);
    }

    [Fact]
    public void ReaperDirectionalStatusPredictsGallowsRear()
    {
        var prediction = Predict(new WrathLocalPredictionSnapshot
        {
            JobId = 39,
            PlayerLevel = 100,
            PlayerStatusIds = new[] { 2589u },
        });

        Assert.Equal(PositionalRequirement.Rear, prediction.Requirement);
    }

    [Fact]
    public void ReaperDirectionalStatusPredictsGibbetFlank()
    {
        var prediction = Predict(new WrathLocalPredictionSnapshot
        {
            JobId = 39,
            PlayerLevel = 100,
            PlayerStatusIds = new[] { 2588u },
        });

        Assert.Equal(PositionalRequirement.Flank, prediction.Requirement);
    }

    [Fact]
    public void ReaperAmbiguousDirectionalStatusFailsClosed()
    {
        var prediction = Predict(new WrathLocalPredictionSnapshot
        {
            JobId = 39,
            PlayerLevel = 100,
            PlayerStatusIds = new[] { 2587u, 2588u, 2589u },
        });

        Assert.Equal(PositionalRequirement.Unknown, prediction.Requirement);
    }

    [Theory]
    [InlineData(34621, PositionalRequirement.Rear)]
    [InlineData(34622, PositionalRequirement.Flank)]
    public void ViperCoilFollowupPredictsNextPositional(uint lastWeaponskill, PositionalRequirement expected)
    {
        var prediction = Predict(new WrathLocalPredictionSnapshot
        {
            JobId = 41,
            PlayerLevel = 100,
            FilteredWeaponskillOrSpellId = lastWeaponskill,
            FilteredWeaponskillOrSpellUpdatedAt = Now,
        });

        Assert.Equal(expected, prediction.Requirement);
    }

    [Fact]
    public void UnknownActionFailsClosed()
    {
        var prediction = Predict(new WrathLocalPredictionSnapshot
        {
            JobId = 34,
            PlayerLevel = 100,
            ComboActionId = 999999,
        });

        Assert.Equal(PositionalRequirement.Unknown, prediction.Requirement);
        Assert.False(prediction.IsFreshOrUsable);
    }

    [Fact]
    public void RawOgcdDoesNotPolluteSamuraiComboPrediction()
    {
        var prediction = Predict(new WrathLocalPredictionSnapshot
        {
            JobId = 34,
            PlayerLevel = 100,
            ComboActionId = 7478,
            RawActionId = 7546,
            RawActionUpdatedAt = Now,
        });

        Assert.Equal(PositionalRequirement.Rear, prediction.Requirement);
        Assert.Equal(7546u, prediction.RawActionId);
    }

    [Fact]
    public void RawOgcdWithNoComboOrFilteredWeaponskillFailsClosed()
    {
        var prediction = Predict(new WrathLocalPredictionSnapshot
        {
            JobId = 34,
            PlayerLevel = 100,
            RawActionId = 7546,
            RawActionUpdatedAt = Now,
        });

        Assert.Equal(PositionalRequirement.Unknown, prediction.Requirement);
    }

    [Theory]
    [InlineData(7546)]
    [InlineData(7542)]
    [InlineData(7549)]
    public void FilteredWeaponskillDrivesSamuraiWhenRawOgcdArrives(uint rawActionId)
    {
        var prediction = Predict(new WrathLocalPredictionSnapshot
        {
            JobId = 34,
            PlayerLevel = 100,
            RawActionId = rawActionId,
            RawActionUpdatedAt = Now,
            FilteredWeaponskillOrSpellId = 7479,
            FilteredWeaponskillOrSpellUpdatedAt = Now,
        });

        Assert.Equal(PositionalRequirement.Flank, prediction.Requirement);
    }

    private static WrathLocalPrediction Predict(WrathLocalPredictionSnapshot snapshot) =>
        WrathLocalPositionalPredictor.Predict(snapshot with { Now = Now });
}
