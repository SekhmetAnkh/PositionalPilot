using PositionalPilot.Core.Model;
using Xunit;

namespace PositionalPilot.Tests;

public sealed class PositionalMovementBudgetPolicyTests
{
    [Fact]
    public void MovementBudgetAllowsMovementWhenEnoughTime()
    {
        var settings = new PositionalPilotSettings();

        var allowed = PositionalMovementBudgetPolicy.CanArriveInTime(3.0f, 1.0f, settings, out _);

        Assert.True(allowed);
    }

    [Fact]
    public void MovementBudgetBlocksMovementWhenTooLate()
    {
        var settings = new PositionalPilotSettings();

        var allowed = PositionalMovementBudgetPolicy.CanArriveInTime(6.0f, 0.7f, settings, out var reason);

        Assert.False(allowed);
        Assert.Contains("too late", reason);
    }

    [Fact]
    public void MissingTimingFailsSafe()
    {
        var settings = new PositionalPilotSettings();

        var allowed = PositionalMovementBudgetPolicy.CanArriveInTime(1.0f, null, settings, out var reason);

        Assert.False(allowed);
        Assert.Contains("unavailable", reason);
    }

    [Fact]
    public void CalculatesBudgetFromGcdRemainingAndActionAhead()
    {
        var settings = new PositionalPilotSettings { FallbackActionAheadSeconds = 0.35f };

        Assert.Equal(1.15f, PositionalMovementBudgetPolicy.CalculateBudgetSeconds(1.5f, float.NaN, settings), precision: 2);
        Assert.Equal(1.25f, PositionalMovementBudgetPolicy.CalculateBudgetSeconds(1.5f, 0.25f, settings), precision: 2);
    }
}
