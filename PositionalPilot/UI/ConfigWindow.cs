using Dalamud.Bindings.ImGui;
using PositionalPilot.Core.Model;
using PositionalPilot.IPC;
using PositionalPilot.Movement;

namespace PositionalPilot.UI;

internal sealed class ConfigWindow
{
    private readonly Configuration config;
    private readonly BossModIpc bossMod;
    private readonly RotationSolverIpc rotationSolver;
    private readonly VnavmeshIpc vnavmesh;
    private readonly AvariceIpc avarice;
    private readonly MovementController controller;

    public ConfigWindow(Configuration config, BossModIpc bossMod, RotationSolverIpc rotationSolver, VnavmeshIpc vnavmesh, AvariceIpc avarice, MovementController controller)
    {
        this.config = config;
        this.bossMod = bossMod;
        this.rotationSolver = rotationSolver;
        this.vnavmesh = vnavmesh;
        this.avarice = avarice;
        this.controller = controller;
    }

    public bool IsOpen { get; set; }

    public void Draw()
    {
        if (!IsOpen)
            return;

        var open = IsOpen;
        if (!ImGui.Begin("PositionalPilot", ref open))
        {
            IsOpen = open;
            ImGui.End();
            return;
        }

        IsOpen = open;
        DrawMain();
        ImGui.Separator();
        DrawDependencies();
        ImGui.Separator();
        DrawCurrentState();
        ImGui.Separator();
        DrawSafety();
        ImGui.Separator();
        DrawTuning();

        ImGui.End();
    }

    public void DrawOverlay()
    {
        if (!config.Settings.ShowOverlay || config.Settings.MovementMode != MovementMode.SuggestOnly)
            return;

        ImGui.SetNextWindowBgAlpha(0.35f);
        ImGui.SetNextWindowPos(new System.Numerics.Vector2(24, 260), ImGuiCond.FirstUseEver);
        ImGui.Begin("PositionalPilot Overlay", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings);
        ImGui.TextUnformatted($"Positional: {controller.CurrentPositional}");
        ImGui.TextUnformatted(controller.ChosenDestination.HasValue ? "Safe candidate ready" : "No safe candidate");
        if (!string.IsNullOrWhiteSpace(controller.BlockReason))
            ImGui.TextUnformatted(controller.BlockReason);
        ImGui.End();
    }

    private void DrawMain()
    {
        var enabled = config.Settings.Enabled;
        if (ImGui.Checkbox("Enabled", ref enabled))
        {
            config.Settings.Enabled = enabled;
            controller.ClearEmergencyStop();
            config.Save();
        }

        var mode = (int)config.Settings.MovementMode;
        if (ImGui.Combo("Movement mode", ref mode, "Disabled\0SuggestOnly\0AssistMove\0"))
        {
            config.Settings.MovementMode = (MovementMode)mode;
            controller.ClearEmergencyStop();
            config.Save();
        }

        if (ImGui.Button("Emergency stop"))
            controller.EmergencyStop();

        var debug = config.Settings.DebugLogging;
        if (ImGui.Checkbox("Debug logging", ref debug))
        {
            config.Settings.DebugLogging = debug;
            config.Save();
        }
    }

    private void DrawDependencies()
    {
        ImGui.TextUnformatted("Dependencies");
        DrawDependency("BossModReborn", bossMod.Available, bossMod.LastError);
        DrawDependency("RotationSolverReborn", rotationSolver.Available, rotationSolver.LastError);
        DrawDependency("vnavmesh", vnavmesh.Available, vnavmesh.LastError);
        DrawDependency("Avarice", avarice.Available, avarice.LastError ?? "optional; only CardinalDirection IPC found");
    }

    private static void DrawDependency(string name, bool available, string? error)
    {
        ImGui.BulletText($"{name}: {(available ? "available" : "missing/error")}");
        if (!available && !string.IsNullOrWhiteSpace(error))
            ImGui.TextDisabled(error);
    }

    private void DrawCurrentState()
    {
        var s = controller.LastSnapshot;
        ImGui.TextUnformatted("Current state");
        ImGui.TextUnformatted($"Target: {(s.HasTarget ? s.TargetName : "none")}");
        ImGui.TextUnformatted($"Target hitbox: {s.TargetHitboxRadius:F2}");
        ImGui.TextUnformatted($"Recommended positional: {controller.CurrentPositional}");
        ImGui.TextUnformatted($"Chosen destination: {controller.ChosenDestination?.ToString() ?? "none"}");
        ImGui.TextUnformatted($"Movement state: {controller.State}");
        ImGui.TextUnformatted($"Block reason: {controller.BlockReason}");
    }

    private void DrawSafety()
    {
        ImGui.TextUnformatted("Safety");
        var deps = config.Settings.RequiredDependencies;
        var boss = deps.HasFlag(RequiredDependencies.RequireBossModSafety);
        var nav = deps.HasFlag(RequiredDependencies.RequireVnavmesh);
        var rsr = deps.HasFlag(RequiredDependencies.RequireCombatSolver);
        if (ImGui.Checkbox("Require BossMod safety", ref boss)) SetFlag(RequiredDependencies.RequireBossModSafety, boss);
        if (ImGui.Checkbox("Require vnavmesh", ref nav)) SetFlag(RequiredDependencies.RequireVnavmesh, nav);
        if (ImGui.Checkbox("Require combat solver", ref rsr)) SetFlag(RequiredDependencies.RequireCombatSolver, rsr);
        CheckboxSetting("Disable during casting", v => config.Settings.DisableDuringCasting = v, config.Settings.DisableDuringCasting);
        CheckboxSetting("Disable during manual movement", v => config.Settings.DisableDuringManualMovement = v, config.Settings.DisableDuringManualMovement);
        CheckboxSetting("Disable during upcoming damage", v => config.Settings.DisableDuringUpcomingDamage = v, config.Settings.DisableDuringUpcomingDamage);
        ImGui.DragFloat("Damage block seconds", ref config.Settings.UpcomingDamageBlockSeconds, 0.1f, 0.1f, 10f);
        CheckboxSetting("Disable during upcoming knockback", v => config.Settings.DisableDuringUpcomingKnockback = v, config.Settings.DisableDuringUpcomingKnockback);
        ImGui.DragFloat("Knockback block seconds", ref config.Settings.UpcomingKnockbackBlockSeconds, 0.1f, 0.1f, 15f);
        CheckboxSetting("Disable during downtime", v => config.Settings.DisableDuringDowntime = v, config.Settings.DisableDuringDowntime);
        CheckboxSetting("Only in combat", v => config.Settings.OnlyInCombat = v, config.Settings.OnlyInCombat);
        CheckboxSetting("Only melee jobs", v => config.Settings.OnlyMeleeJobs = v, config.Settings.OnlyMeleeJobs);
        CheckboxSetting("Show overlay", v => config.Settings.ShowOverlay = v, config.Settings.ShowOverlay);
    }

    private void DrawTuning()
    {
        ImGui.TextUnformatted("Movement tuning");
        ImGui.DragFloat("Max move distance", ref config.Settings.MaxMoveDistance, 0.1f, 0.5f, 20f);
        ImGui.DragFloat("Distance from hitbox", ref config.Settings.DesiredDistanceFromTargetHitbox, 0.1f, 0.1f, 10f);
        ImGui.DragFloat("Candidate ring extra", ref config.Settings.CandidateRingExtraDistance, 0.1f, 0f, 10f);
        ImGui.DragInt("Candidate count", ref config.Settings.CandidateCount, 1, 8, 96);
        ImGui.DragInt("Repath cooldown ms", ref config.Settings.RepathCooldownMs, 10, 100, 5000);
        ImGui.DragFloat("Minimum improvement", ref config.Settings.MinimumImprovementYalms, 0.05f, 0f, 5f);
        ImGui.DragFloat("Stop within yalms", ref config.Settings.StopWithinYalms, 0.05f, 0.05f, 3f);
        if (ImGui.IsItemDeactivatedAfterEdit())
            config.Save();
    }

    private void SetFlag(RequiredDependencies flag, bool value)
    {
        if (value)
            config.Settings.RequiredDependencies |= flag;
        else
            config.Settings.RequiredDependencies &= ~flag;
        config.Save();
    }

    private void CheckboxSetting(string label, Action<bool> setter, bool current)
    {
        var value = current;
        if (ImGui.Checkbox(label, ref value))
        {
            setter(value);
            config.Save();
        }
    }
}
