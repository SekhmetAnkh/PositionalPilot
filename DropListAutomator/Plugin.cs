using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using DropListAutomator.IPC;
using DropListAutomator.Planning;
using DropListAutomator.UI;

namespace DropListAutomator;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/dropauto";

    private readonly PluginServices services;
    private readonly Configuration config;
    private readonly GatherBuddyRebornIpc gbr;
    private readonly MainWindow window;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commands,
        IClientState clientState,
        IObjectTable objects,
        ITargetManager targets,
        IDataManager data,
        ICondition condition,
        IFramework framework,
        IChatGui chat,
        IPluginLog log)
    {
        services = new PluginServices(pluginInterface, commands, clientState, objects, targets, data, condition, framework, chat, log);
        config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        config.Initialize(pluginInterface);

        var logger = new ThrottledLogger(services);
        gbr = new GatherBuddyRebornIpc(services, logger);
        var lifestream = new LifestreamIpc(services, logger);
        var vnavmesh = new VnavmeshIpc(services, logger);
        var rotationSolver = new RotationSolverRebornIpc(services, logger);
        var commandBridge = new CommandBridge(services);
        var planner = new MaterialPlanner(services);
        var monsterRoutePlanner = new MonsterRoutePlanner(services);
        var monsterNavigator = new MonsterNavigator(services, config, lifestream, vnavmesh, rotationSolver, commandBridge, monsterRoutePlanner);
        window = new MainWindow(config, gbr, lifestream, vnavmesh, rotationSolver, monsterNavigator, commandBridge, planner);

        commands.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Drop List Automator.",
        });

        pluginInterface.UiBuilder.Draw += Draw;
        pluginInterface.UiBuilder.OpenConfigUi += OpenConfig;
        framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        services.PluginInterface.UiBuilder.Draw -= Draw;
        services.PluginInterface.UiBuilder.OpenConfigUi -= OpenConfig;
        services.Framework.Update -= OnFrameworkUpdate;
        services.Commands.RemoveHandler(CommandName);
        window.Dispose();
    }

    private void Draw() => window.Draw();

    private void OnFrameworkUpdate(IFramework framework) => window.Update();

    private void OpenConfig() => window.IsOpen = true;

    private void OnCommand(string command, string args)
    {
        var arg = args.Trim().ToLowerInvariant();
        switch (arg)
        {
            case "":
            case "open":
                window.IsOpen = true;
                break;
            case "gbr on":
                gbr.SetAutoGatherEnabled(true);
                services.Chat.Print("DropListAutomator: GBR auto-gather enabled.");
                break;
            case "gbr off":
                gbr.SetAutoGatherEnabled(false);
                services.Chat.Print("DropListAutomator: GBR auto-gather disabled.");
                break;
            case "status":
                gbr.RefreshAvailability();
                window.RefreshDependencies();
                services.Chat.Print(window.BuildStatusLine());
                break;
            case "stop":
                window.StopAutomation();
                services.Chat.Print("DropListAutomator: stopped navigation and dependency automation.");
                break;
            default:
                services.Chat.Print("Usage: /dropauto [open|status|stop|gbr on|gbr off]");
                break;
        }
    }
}
