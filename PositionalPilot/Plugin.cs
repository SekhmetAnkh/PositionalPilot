using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using PositionalPilot.Core.Model;
using PositionalPilot.Game;
using PositionalPilot.IPC;
using PositionalPilot.Movement;
using PositionalPilot.UI;

namespace PositionalPilot;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/ppilot";
    private readonly PluginServices services;
    private readonly Configuration config;
    private readonly ThrottledLogger logger;
    private readonly BossModIpc bossMod;
    private readonly RotationSolverIpc rotationSolver;
    private readonly VnavmeshIpc vnavmesh;
    private readonly AvariceIpc avarice;
    private readonly GameStateReader gameState;
    private readonly MovementController movement;
    private readonly ConfigWindow window;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commands,
        IClientState clientState,
        IObjectTable objects,
        ITargetManager targets,
        ICondition condition,
        IFramework framework,
        IChatGui chat,
        IPluginLog log)
    {
        services = new PluginServices(pluginInterface, commands, clientState, objects, targets, condition, framework, chat, log);
        config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        config.Initialize(pluginInterface);
        logger = new ThrottledLogger(services);
        bossMod = new BossModIpc(services, logger);
        rotationSolver = new RotationSolverIpc(services, logger);
        vnavmesh = new VnavmeshIpc(services, logger);
        avarice = new AvariceIpc(services, logger);
        gameState = new GameStateReader(services);
        movement = new MovementController(config, gameState, bossMod, vnavmesh, rotationSolver, logger);
        window = new ConfigWindow(config, bossMod, rotationSolver, vnavmesh, avarice, movement);

        services.Commands.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open PositionalPilot. Args: on, off, stop, suggest, status, debug",
        });
        services.Framework.Update += OnFrameworkUpdate;
        services.PluginInterface.UiBuilder.Draw += Draw;
        services.PluginInterface.UiBuilder.OpenConfigUi += OpenConfig;
    }

    public void Dispose()
    {
        movement.Stop("plugin disposed");
        services.Framework.Update -= OnFrameworkUpdate;
        services.PluginInterface.UiBuilder.Draw -= Draw;
        services.PluginInterface.UiBuilder.OpenConfigUi -= OpenConfig;
        services.Commands.RemoveHandler(CommandName);
    }

    private void OnFrameworkUpdate(IFramework framework) => movement.Update();

    private void Draw()
    {
        window.Draw();
        window.DrawOverlay();
    }

    private void OpenConfig() => window.IsOpen = true;

    private void OnCommand(string command, string args)
    {
        var arg = args.Trim().ToLowerInvariant();
        switch (arg)
        {
            case "":
                window.IsOpen = true;
                break;
            case "on":
                config.Settings.Enabled = true;
                config.Settings.MovementMode = MovementMode.AssistMove;
                movement.ClearEmergencyStop();
                config.Save();
                services.Chat.Print("PositionalPilot assist movement enabled.");
                break;
            case "off":
                config.Settings.Enabled = false;
                config.Settings.MovementMode = MovementMode.Disabled;
                movement.Stop("disabled by command");
                config.Save();
                services.Chat.Print("PositionalPilot disabled.");
                break;
            case "stop":
                movement.EmergencyStop();
                services.Chat.Print("PositionalPilot emergency stop: disabled and movement stopped.");
                break;
            case "suggest":
                config.Settings.Enabled = true;
                config.Settings.MovementMode = config.Settings.MovementMode == MovementMode.SuggestOnly ? MovementMode.Disabled : MovementMode.SuggestOnly;
                movement.ClearEmergencyStop();
                config.Save();
                services.Chat.Print($"PositionalPilot suggest mode: {config.Settings.MovementMode == MovementMode.SuggestOnly}");
                break;
            case "status":
                PrintStatus();
                break;
            case "debug":
                config.Settings.DebugLogging = !config.Settings.DebugLogging;
                config.Save();
                services.Chat.Print($"PositionalPilot debug logging: {config.Settings.DebugLogging}");
                break;
            default:
                services.Chat.Print("Usage: /ppilot [on|off|stop|suggest|status|debug]");
                break;
        }
    }

    private void PrintStatus()
    {
        var snap = movement.LastSnapshot;
        services.Chat.Print($"PositionalPilot: enabled={config.Settings.Enabled}, mode={config.Settings.MovementMode}, state={movement.State}");
        services.Chat.Print($"Deps: BossMod={bossMod.Available} ({bossMod.LastError ?? "ok"}), RSR={rotationSolver.Available} ({rotationSolver.LastError ?? "ok"}), vnavmesh={vnavmesh.Available} ({vnavmesh.LastError ?? "ok"}), Avarice={avarice.Available} ({avarice.LastError ?? "optional"})");
        services.Chat.Print($"Target: {(snap.HasTarget ? snap.TargetName : "none")}, positional={movement.CurrentPositional}, destination={movement.ChosenDestination?.ToString() ?? "none"}, block={movement.BlockReason}");
    }
}
