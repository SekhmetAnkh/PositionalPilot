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

    public void Initialize(IDalamudPluginInterface pi) => pluginInterface = pi;

    public void Save() => pluginInterface?.SavePluginConfig(this);
}
