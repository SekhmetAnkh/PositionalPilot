namespace PositionalPilot.Core.Model;

public static class PositionalEffectPotencyMap
{
    private static readonly Dictionary<uint, HashSet<byte>> SuccessfulPotencyMarkers = new()
    {
        [56] = new HashSet<byte> { 12, 13, 14, 18, 20 },
        [66] = new HashSet<byte> { 14, 15, 17, 18 },
        [88] = new HashSet<byte> { 28, 61 },
        [2255] = new HashSet<byte> { 15, 20, 21, 23, 30, 37, 42, 50, 52, 54, 63, 70 },
        [2258] = new HashSet<byte> { 25 },
        [3554] = new HashSet<byte> { 22, 28, 58, 66 },
        [3556] = new HashSet<byte> { 22, 28, 58, 66 },
        [3563] = new HashSet<byte> { 20, 30, 37, 52, 65, 72 },
        [7481] = new HashSet<byte> { 23, 31, 33, 61, 70, 72 },
        [7482] = new HashSet<byte> { 23, 31, 33, 61, 70, 72 },
        [24382] = new HashSet<byte> { 9, 10, 11, 13 },
        [24383] = new HashSet<byte> { 9, 10, 11, 13 },
        [25772] = new HashSet<byte> { 22, 28, 58, 66 },
        [34610] = new HashSet<byte> { 48, 50, 54, 60, 63, 70 },
        [34611] = new HashSet<byte> { 48, 50, 54, 60, 63, 70 },
        [34612] = new HashSet<byte> { 48, 50, 54, 60, 63, 70 },
        [34613] = new HashSet<byte> { 48, 50, 54, 60, 63, 70 },
        [34621] = new HashSet<byte> { 7 },
        [34622] = new HashSet<byte> { 7 },
        [36947] = new HashSet<byte> { 11, 16 },
        [36970] = new HashSet<byte> { 7 },
        [36971] = new HashSet<byte> { 7 },
    };

    public static bool IsTrackedPositionalAction(uint actionId) => SuccessfulPotencyMarkers.ContainsKey(actionId);

    public static bool IsSuccessfulPositionalHit(uint actionId, byte potencyMarker) =>
        SuccessfulPotencyMarkers.TryGetValue(actionId, out var markers) && markers.Contains(potencyMarker);
}
