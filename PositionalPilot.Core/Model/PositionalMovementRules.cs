namespace PositionalPilot.Core.Model;

public static class PositionalMovementRules
{
    public static bool IsCommittedPositional(PositionalRequirement requirement) =>
        requirement is PositionalRequirement.Rear or PositionalRequirement.Flank;

    public static string MovementModeName(PositionalRequirement requirement) =>
        IsCommittedPositional(requirement) ? "committed positional" : "border hold";

    public static bool ShouldBypassRepathCooldown(
        PositionalRequirement previousMovementPositional,
        PositionalRequirement currentMovementPositional,
        uint previousNextGcdActionId,
        uint currentNextGcdActionId)
    {
        if (!IsCommittedPositional(currentMovementPositional))
            return false;

        if (previousMovementPositional == PositionalRequirement.Any)
            return true;

        return currentNextGcdActionId != 0 && currentNextGcdActionId != previousNextGcdActionId;
    }
}
