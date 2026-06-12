namespace PositionalPilot.Core.Model;

public static class PositionalMovementBudgetPolicy
{
    public static bool CanArriveInTime(
        float distance,
        float? availableActionBudgetSeconds,
        PositionalPilotSettings settings,
        out string reason)
    {
        var required = EstimateMovementSeconds(distance, settings);
        if (!availableActionBudgetSeconds.HasValue || !float.IsFinite(availableActionBudgetSeconds.Value))
        {
            reason = "GCD timing unavailable; committed movement blocked fail-closed";
            return false;
        }

        var budget = MathF.Max(0, availableActionBudgetSeconds.Value);
        if (required > budget)
        {
            reason = $"positional move too late ({distance:0.0}y needs {required:0.0}s, budget {budget:0.0}s)";
            return false;
        }

        reason = $"movement budget ok ({distance:0.0}y needs {required:0.0}s, budget {budget:0.0}s)";
        return true;
    }

    public static float EstimateMovementSeconds(float distance, PositionalPilotSettings settings) =>
        distance / MathF.Max(0.1f, settings.EstimatedCombatMoveSpeed) + MathF.Max(0, settings.ArrivalBufferSeconds);

    public static float CalculateBudgetSeconds(float gcdRemainingSeconds, float gcdActionAheadSeconds, PositionalPilotSettings settings)
    {
        var actionAhead = float.IsFinite(gcdActionAheadSeconds) && gcdActionAheadSeconds >= 0
            ? gcdActionAheadSeconds
            : settings.FallbackActionAheadSeconds;
        return MathF.Max(0, gcdRemainingSeconds - actionAhead);
    }
}
