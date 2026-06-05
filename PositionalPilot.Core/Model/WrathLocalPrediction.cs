namespace PositionalPilot.Core.Model;

public sealed record WrathLocalPrediction(
    uint RawActionId,
    uint FilteredWeaponskillOrSpellId,
    uint ComboActionId,
    string Source,
    PositionalRequirement Requirement,
    bool IsFreshOrUsable);
