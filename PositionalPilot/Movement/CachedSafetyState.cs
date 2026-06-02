using PositionalPilot.Core.Model;

namespace PositionalPilot.Movement;

internal sealed record CachedSafetyState(
    bool VnavmeshReady,
    bool VnavmeshNavigating,
    bool BossModNavigating,
    bool BossModHasNaviTarget,
    bool RotationSolverAvailable,
    bool HasPositional,
    PositionalRequirement Positional,
    float? NextDamageIn,
    float? NextKnockbackIn,
    float? NextDowntimeIn,
    DateTime UpdatedAt)
{
    public static CachedSafetyState Empty { get; } = new(
        false,
        false,
        false,
        false,
        false,
        false,
        PositionalRequirement.Unknown,
        null,
        null,
        null,
        DateTime.MinValue);
}
