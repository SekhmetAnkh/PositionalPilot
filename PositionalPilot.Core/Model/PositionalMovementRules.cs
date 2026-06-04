namespace PositionalPilot.Core.Model;

public static class PositionalMovementRules
{
    public static bool IsCommittedPositional(PositionalRequirement requirement) =>
        requirement is PositionalRequirement.Rear or PositionalRequirement.Flank;

    public static bool ShouldBlockForTargetOfTarget(bool? targetTargetsPlayer) =>
        targetTargetsPlayer == true;

    public static bool CanFrontEscape(bool isPlayerInFront, bool? targetTargetsPlayer) =>
        isPlayerInFront && !ShouldBlockForTargetOfTarget(targetTargetsPlayer);

    public static string MovementModeName(PositionalRequirement requirement) =>
        requirement == PositionalRequirement.Front
            ? "front escape"
            : IsCommittedPositional(requirement) ? "committed positional" : "border hold";

    public static bool ShouldBypassRepathCooldown(
        PositionalRequirement previousMovementPositional,
        PositionalRequirement currentMovementPositional,
        uint previousNextGcdActionId,
        uint currentNextGcdActionId,
        bool frontEscape = false)
    {
        if (frontEscape)
            return true;

        if (!IsCommittedPositional(currentMovementPositional))
            return false;

        if (previousMovementPositional == PositionalRequirement.Any)
            return true;

        return currentNextGcdActionId != 0 && currentNextGcdActionId != previousNextGcdActionId;
    }

    public static PositionalRequirement ResolveRsrMovementRequirement(
        PositionalRequirement nextGcdRequirement,
        DateTime nextGcdUpdatedAt,
        PositionalRequirement nextActionRequirement,
        DateTime nextActionUpdatedAt,
        DateTime now,
        int maxAgeMs,
        out string source)
    {
        var maxAge = TimeSpan.FromMilliseconds(maxAgeMs);
        if (now - nextGcdUpdatedAt <= maxAge && IsCommittedPositional(nextGcdRequirement))
        {
            source = "RSR next GCD";
            return nextGcdRequirement;
        }

        if (now - nextActionUpdatedAt <= maxAge && IsCommittedPositional(nextActionRequirement))
        {
            source = "RSR next action";
            return nextActionRequirement;
        }

        source = "nearest rear/flank border";
        return PositionalRequirement.Any;
    }
}
