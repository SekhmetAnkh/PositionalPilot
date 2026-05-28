using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace PositionalPilot;

internal sealed class PluginServices
{
    public PluginServices(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commands,
        IClientState clientState,
        ITargetManager targets,
        ICondition condition,
        IFramework framework,
        IChatGui chat,
        IPluginLog log)
    {
        PluginInterface = pluginInterface;
        Commands = commands;
        ClientState = clientState;
        Targets = targets;
        Condition = condition;
        Framework = framework;
        Chat = chat;
        Log = log;
    }

    public IDalamudPluginInterface PluginInterface { get; }
    public ICommandManager Commands { get; }
    public IClientState ClientState { get; }
    public ITargetManager Targets { get; }
    public ICondition Condition { get; }
    public IFramework Framework { get; }
    public IChatGui Chat { get; }
    public IPluginLog Log { get; }
}
