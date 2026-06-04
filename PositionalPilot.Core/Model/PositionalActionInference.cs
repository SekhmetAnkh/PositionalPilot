namespace PositionalPilot.Core.Model;

public static class PositionalActionInference
{
    private static readonly IReadOnlyDictionary<uint, PositionalRequirement> WrathNextByLastGcd = new Dictionary<uint, PositionalRequirement>
    {
        // SAM: Wrath's basic combo returns Gekko after Jinpu and Kasha after Shifu.
        [7478] = PositionalRequirement.Rear, // Jinpu -> Gekko
        [7479] = PositionalRequirement.Flank, // Shifu -> Kasha

        // RPR: Gluttony/Soul Reaver commonly alternates the pair.
        [24382] = PositionalRequirement.Rear, // Gibbet -> Gallows
        [36970] = PositionalRequirement.Rear, // Executioner's Gibbet -> Executioner's Gallows
        [24383] = PositionalRequirement.Flank, // Gallows -> Gibbet
        [36971] = PositionalRequirement.Flank, // Executioner's Gallows -> Executioner's Gibbet

        // VPR basic combo and Vicewinder pair followups.
        [34610] = PositionalRequirement.Flank, // Flanksting Strike -> Flanksbane Fang
        [34612] = PositionalRequirement.Rear, // Hindsting Strike -> Hindsbane Fang
        [34621] = PositionalRequirement.Rear, // Hunter's Coil -> Swiftskin's Coil
        [34622] = PositionalRequirement.Flank, // Swiftskin's Coil -> Hunter's Coil
    };

    public static bool TryInferWrathNextRequirement(uint lastGcdActionId, out PositionalRequirement requirement) =>
        WrathNextByLastGcd.TryGetValue(lastGcdActionId, out requirement);
}
