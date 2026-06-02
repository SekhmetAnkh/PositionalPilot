namespace PositionalPilot.Core.Model;

public static class PositionalActionMap
{
    private static readonly IReadOnlyDictionary<uint, PositionalRequirement> Map = new Dictionary<uint, PositionalRequirement>
    {
        // Mirrored from RotationSolverReborn RotationSolver.Basic/Helpers/ConfigurationHelper.cs ActionPositional.
        [3554] = PositionalRequirement.Flank, // Fang and Claw
        [3556] = PositionalRequirement.Rear, // Wheeling Thrust
        [88] = PositionalRequirement.Rear, // Chaos Thrust
        [25772] = PositionalRequirement.Rear, // Chaotic Spring
        [66] = PositionalRequirement.Rear, // Demolish
        [56] = PositionalRequirement.Flank, // Snap Punch
        [36947] = PositionalRequirement.Flank, // Pouncing Coeurl
        [2258] = PositionalRequirement.Rear, // Trick Attack
        [2255] = PositionalRequirement.Rear, // Aeolian Edge
        [3563] = PositionalRequirement.Flank, // Armor Crush
        [24382] = PositionalRequirement.Flank, // Gibbet
        [36970] = PositionalRequirement.Flank, // Executioner's Gibbet
        [24383] = PositionalRequirement.Rear, // Gallows
        [36971] = PositionalRequirement.Rear, // Executioner's Gallows
        [7481] = PositionalRequirement.Rear, // Gekko
        [7482] = PositionalRequirement.Flank, // Kasha
        [34610] = PositionalRequirement.Flank, // Flanksting Strike
        [34611] = PositionalRequirement.Flank, // Flanksbane Fang
        [34612] = PositionalRequirement.Rear, // Hindsting Strike
        [34613] = PositionalRequirement.Rear, // Hindsbane Fang
        [34621] = PositionalRequirement.Flank, // Hunter's Coil
        [34622] = PositionalRequirement.Rear, // Swiftskin's Coil
    };

    public static bool TryGetRequirement(uint actionId, out PositionalRequirement requirement) =>
        Map.TryGetValue(actionId, out requirement);
}
