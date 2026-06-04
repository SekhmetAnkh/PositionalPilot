using Dalamud.Bindings.ImGui;
using DropListAutomator.IPC;
using DropListAutomator.Planning;
using System.Numerics;

namespace DropListAutomator.UI;

internal sealed class MainWindow
{
    private readonly Configuration config;
    private readonly GatherBuddyRebornIpc gbr;
    private readonly LifestreamIpc lifestream;
    private readonly VnavmeshIpc vnavmesh;
    private readonly RotationSolverRebornIpc rotationSolver;
    private readonly MonsterNavigator monsterNavigator;
    private readonly CommandBridge commands;
    private readonly MaterialPlanner planner;
    private IReadOnlyList<MaterialRequirement> requirements = [];
    private string targetText;

    public MainWindow(
        Configuration config,
        GatherBuddyRebornIpc gbr,
        LifestreamIpc lifestream,
        VnavmeshIpc vnavmesh,
        RotationSolverRebornIpc rotationSolver,
        MonsterNavigator monsterNavigator,
        CommandBridge commands,
        MaterialPlanner planner)
    {
        this.config = config;
        this.gbr = gbr;
        this.lifestream = lifestream;
        this.vnavmesh = vnavmesh;
        this.rotationSolver = rotationSolver;
        this.monsterNavigator = monsterNavigator;
        this.commands = commands;
        this.planner = planner;
        targetText = config.LastTargetText;
    }

    public bool IsOpen { get; set; }

    public void Dispose() => rotationSolver.Dispose();

    public void Update() => monsterNavigator.Update();

    public void StopAutomation()
    {
        monsterNavigator.Stop();
        gbr.SetAutoGatherEnabled(false);
    }

    public void RefreshDependencies()
    {
        gbr.RefreshAvailability();
        lifestream.RefreshAvailability();
        vnavmesh.RefreshAvailability();
        rotationSolver.RefreshAvailability();
    }

    public string BuildStatusLine() =>
        $"DropListAutomator: GBR={gbr.Available} ({gbr.LastError ?? gbr.GetStatus()}), Lifestream={lifestream.Available} ({lifestream.LastError ?? "ok"}), vnavmesh={vnavmesh.Available} ({vnavmesh.LastError ?? "ok"}), RSR={rotationSolver.Available} ({rotationSolver.LastError ?? "ok"}), MonsterNav={monsterNavigator.State} ({monsterNavigator.StatusText})";

    public void Draw()
    {
        if (!IsOpen)
            return;

        ImGui.SetNextWindowSize(new Vector2(760, 520), ImGuiCond.FirstUseEver);
        var isOpen = IsOpen;
        if (!ImGui.Begin("Drop List Automator", ref isOpen))
        {
            IsOpen = isOpen;
            ImGui.End();
            return;
        }

        IsOpen = isOpen;

        DrawControls();
        ImGui.Separator();
        DrawDependencyStatus();
        ImGui.Separator();
        DrawResults();
        ImGui.End();
    }

    private void DrawControls()
    {
        ImGui.TextUnformatted("Targets");
        ImGui.InputTextMultiline("##targets", ref targetText, 4096, new Vector2(-1, 92));

        if (ImGui.Button("Plan"))
        {
            config.LastTargetText = targetText;
            config.Save();
            requirements = planner.Plan(targetText);
        }

        ImGui.SameLine();
        var autoOpenMlh = config.AutoOpenMonsterLootHunter;
        if (ImGui.Checkbox("Open MLH for drop-only items", ref autoOpenMlh))
        {
            config.AutoOpenMonsterLootHunter = autoOpenMlh;
            config.Save();
        }

        ImGui.SameLine();
        var preferVulcan = config.PreferVulcanCraftCommand;
        if (ImGui.Checkbox("Use Vulcan craft command", ref preferVulcan))
        {
            config.PreferVulcanCraftCommand = preferVulcan;
            config.Save();
        }

        var teleporterCommandTemplate = config.TeleporterCommandTemplate;
        if (ImGui.InputText("Teleporter fallback", ref teleporterCommandTemplate, 128))
            config.TeleporterCommandTemplate = teleporterCommandTemplate;
        if (ImGui.IsItemDeactivatedAfterEdit())
            config.Save();
    }

    private void DrawDependencyStatus()
    {
        RefreshDependencies();
        DrawStatus("GBR", gbr.Available, gbr.Available ? $"IPC v{gbr.GetVersion()}: {gbr.GetStatus()}" : gbr.LastError);
        DrawStatus("Lifestream", lifestream.Available, lifestream.Available ? $"busy={lifestream.IsBusy()}" : lifestream.LastError);
        DrawStatus("vnavmesh", vnavmesh.Available, vnavmesh.Available ? $"ready={vnavmesh.IsReady()}, moving={vnavmesh.IsNavigating()}" : vnavmesh.LastError);
        DrawStatus("RSR", rotationSolver.Available, rotationSolver.Available
            ? $"events={rotationSolver.NextActionEventsAvailable}, next={rotationSolver.LatestNextGcdActionName}/{rotationSolver.LatestNextActionName}"
            : rotationSolver.LastError);
        DrawStatus("Monster nav", monsterNavigator.State != MonsterNavigationState.Failed, monsterNavigator.StatusText);
        ImGui.TextUnformatted("MLH bridge: command-only (/mloot). Vulcan bridge: command-only (/vulcan craft). Monster routes follow GBR's teleport-wait-vnavmesh pattern.");

        if (ImGui.SmallButton("Abort Teleport"))
            lifestream.Abort();
        ImGui.SameLine();
        if (ImGui.SmallButton("Stop Movement"))
            vnavmesh.Stop();
        ImGui.SameLine();
        if (ImGui.SmallButton("RSR Manual"))
            rotationSolver.SetManualMode();
        ImGui.SameLine();
        if (ImGui.SmallButton("Stop All"))
            StopAutomation();
    }

    private void DrawResults()
    {
        if (requirements.Count == 0)
        {
            ImGui.TextDisabled("Enter one crafted item per line, optionally as \"Item Name x3\", then press Plan.");
            return;
        }

        if (!ImGui.BeginTable("materials", 6, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
            return;

        ImGui.TableSetupColumn("Item");
        ImGui.TableSetupColumn("Need", ImGuiTableColumnFlags.WidthFixed, 60);
        ImGui.TableSetupColumn("Missing", ImGuiTableColumnFlags.WidthFixed, 70);
        ImGui.TableSetupColumn("Source", ImGuiTableColumnFlags.WidthFixed, 110);
        ImGui.TableSetupColumn("GBR", ImGuiTableColumnFlags.WidthFixed, 80);
        ImGui.TableSetupColumn("MLH", ImGuiTableColumnFlags.WidthFixed, 80);
        ImGui.TableHeadersRow();

        foreach (var req in requirements)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(req.Name);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(req.Needed.ToString());
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(req.Missing.ToString());
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(req.SourceKind.ToString());
            ImGui.TableNextColumn();
            if (req.SourceKind == MaterialSourceKind.Gatherable && ImGui.SmallButton($"Gather##{req.ItemId}"))
                commands.StartGbrGather(req.Name);
            ImGui.TableNextColumn();
            if (req.SourceKind == MaterialSourceKind.LikelyDropOnly && ImGui.SmallButton($"Lookup##{req.ItemId}"))
                commands.OpenMonsterLootHunter(req.Name);
        }

        ImGui.EndTable();

        if (config.AutoOpenMonsterLootHunter && ImGui.Button("Open MLH For All Drop-Only"))
        {
            foreach (var req in requirements.Where(req => req.SourceKind == MaterialSourceKind.LikelyDropOnly))
                commands.OpenMonsterLootHunter(req.Name);
        }
    }

    private static void DrawStatus(string name, bool available, string? detail)
    {
        ImGui.TextUnformatted($"{name}: {(available ? "ready" : "missing")} ({detail ?? "ok"})");
    }
}
