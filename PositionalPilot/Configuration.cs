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
    }

    public void Save() => pluginInterface?.SavePluginConfig(this);
}
