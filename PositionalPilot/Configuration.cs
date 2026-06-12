using Dalamud.Configuration;
using Dalamud.Plugin;
using PositionalPilot.Core.Model;

namespace PositionalPilot;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public PositionalPilotSettings Settings { get; set; } = new();

    [NonSerialized]
    private IDalamudPluginInterface? pluginInterface;

    public void Initialize(IDalamudPluginInterface pi)
    {
        pluginInterface = pi;
        if (Version < 2)
        {
            Settings.BorderHoldDeadzoneYalms = Settings.HoldDeadzoneYalms > 0 ? Settings.HoldDeadzoneYalms : 1.25f;
            Settings.PositionalCommitDeadzoneYalms = Settings.StopWithinYalms > 0 ? Settings.StopWithinYalms : 0.35f;
            if (Settings.PositionalNudgeDegrees <= 12.01f)
                Settings.PositionalNudgeDegrees = 30.0f;

            Version = 2;
            Save();
        }

        if (Version < 3)
        {
            Settings.EnableSamMeikyoWrathAnticipation = true;
            Version = 3;
            Save();
        }

        if (Version < 4)
        {
            Settings.TrackSuccessfulPositionals = true;
            Settings.LifetimeStats ??= new PositionalStatsBook();
            Version = 4;
            Save();
        }

        if (Version < 5)
        {
            Settings.MeleeRangeYalms = 3.0f;
            Settings.EstimatedCombatMoveSpeed = 6.0f;
            Settings.ArrivalBufferSeconds = 0.2f;
            Settings.FallbackActionAheadSeconds = 0.35f;
            Settings.EnableTrueNorthFallback = true;
            Version = 5;
            Save();
        }

        Settings.LifetimeStats ??= new PositionalStatsBook();
    }

    public void Save() => pluginInterface?.SavePluginConfig(this);
}
