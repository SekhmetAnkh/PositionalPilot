using Dalamud.Configuration;
using Dalamud.Plugin;

namespace DropListAutomator;

[Serializable]
internal sealed class Configuration : IPluginConfiguration
{
    [NonSerialized]
    private IDalamudPluginInterface? pluginInterface;

    public int Version { get; set; } = 1;
    public string TeleporterCommandTemplate { get; set; } = "/tp {0}";

    public void Initialize(IDalamudPluginInterface pi) => pluginInterface = pi;

    public void Save() => pluginInterface?.SavePluginConfig(this);
}
