namespace PositionalPilot.Core.Model;

public sealed record WrathLocalPredictionSnapshot
{
    public uint JobId { get; init; }
    public byte PlayerLevel { get; init; }
    public uint RawActionId { get; init; }
    public DateTime RawActionUpdatedAt { get; init; } = DateTime.MinValue;
    public uint FilteredWeaponskillOrSpellId { get; init; }
    public DateTime FilteredWeaponskillOrSpellUpdatedAt { get; init; } = DateTime.MinValue;
    public uint ComboActionId { get; init; }
    public IReadOnlyCollection<uint> PlayerStatusIds { get; init; } = EmptySet;
    public IReadOnlyDictionary<uint, float> PlayerStatusRemainingSeconds { get; init; } = EmptyTimes;
    public IReadOnlyCollection<uint> TargetStatusIds { get; init; } = EmptySet;
    public IReadOnlyCollection<uint> ActionReadyIds { get; init; } = EmptySet;
    public int? MonkCoeurlFury { get; init; }
    public int? NinjaKazematoi { get; init; }
    public bool? SamuraiHasGetsu { get; init; }
    public bool? SamuraiHasKa { get; init; }
    public uint ViperDreadCombo { get; init; }
    public bool EnableSamMeikyoAnticipation { get; init; } = true;
    public int MaxAgeMs { get; init; } = 1500;
    public DateTime Now { get; init; } = DateTime.UtcNow;

    private static readonly IReadOnlyCollection<uint> EmptySet = Array.Empty<uint>();
    private static readonly IReadOnlyDictionary<uint, float> EmptyTimes = new Dictionary<uint, float>();
}
