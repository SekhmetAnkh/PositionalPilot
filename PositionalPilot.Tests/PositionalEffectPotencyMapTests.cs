using PositionalPilot.Core.Model;
using Xunit;

namespace PositionalPilot.Tests;

public sealed class PositionalEffectPotencyMapTests
{
    [Theory]
    [InlineData(7481, 72)]
    [InlineData(7482, 70)]
    [InlineData(34621, 7)]
    [InlineData(36971, 7)]
    public void KnownSuccessfulPotencyMarkersAreTracked(uint actionId, byte marker)
    {
        Assert.True(PositionalEffectPotencyMap.IsTrackedPositionalAction(actionId));
        Assert.True(PositionalEffectPotencyMap.IsSuccessfulPositionalHit(actionId, marker));
    }

    [Fact]
    public void UnknownActionOrMarkerDoesNotCountAsSuccessful()
    {
        Assert.False(PositionalEffectPotencyMap.IsTrackedPositionalAction(7));
        Assert.False(PositionalEffectPotencyMap.IsSuccessfulPositionalHit(7481, 1));
    }
}
