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
        ImGui.TextUnformatted(controller.ChosenDestination.HasValue ? "Safe destination ready" : "No safe destination");
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
        bossMod.RefreshAvailability();
        rotationSolver.RefreshAvailability();
        vnavmesh.RefreshAvailability();
        avarice.RefreshAvailability();

        ImGui.TextUnformatted("Dependencies");
        DrawDependency("BossModReborn", bossMod.Available, bossMod.LastError);
        DrawDependency("RotationSolverReborn", rotationSolver.Available, rotationSolver.LastError);
        DrawDependency("RSR next-GCD events", rotationSolver.NextActionEventsAvailable, rotationSolver.EventLastError);
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
        var positionals = s.TargetOmnidirectional switch
        {
            true => "not required",
            false => "required",
            _ => "unknown",
        };
        ImGui.TextUnformatted("Current state");
        ImGui.TextUnformatted($"Target: {(s.HasTarget ? s.TargetName : "none")}");
        ImGui.TextUnformatted($"Target positionals: {positionals}");
        ImGui.TextUnformatted($"Target targeting player: {s.TargetTargetsPlayer}");
        ImGui.TextUnformatted($"Target hitbox: {s.TargetHitboxRadius:F2}");
        ImGui.TextUnformatted($"Recommended positional: {controller.CurrentPositional}");
        ImGui.TextUnformatted($"Movement positional: {controller.CurrentMovementPositional}");
        ImGui.TextUnformatted($"Movement mode: {controller.CurrentMovementMode}");
        ImGui.TextUnformatted($"Movement source: {controller.CurrentMovementPositionalSource}");
        ImGui.TextUnformatted($"Border side: {controller.CurrentBorderSide}");
        ImGui.TextUnformatted($"Chosen destination: {controller.ChosenDestination?.ToString() ?? "none"}");
        ImGui.TextUnformatted($"Movement state: {controller.State}");
        ImGui.TextUnformatted($"Block reason: {controller.BlockReason}");
        var next = controller.LastRotationSolverNextAction;
        var nextAge = next.NextGcdUpdatedAt == DateTime.MinValue
            ? "never"
            : $"{(DateTime.UtcNow - next.NextGcdUpdatedAt).TotalMilliseconds:F0}ms";
        ImGui.TextUnformatted($"RSR next GCD: {next.NextGcdActionName} ({next.NextGcdActionId})");
        ImGui.TextUnformatted($"RSR next positional: {next.NextGcdRequirement}");
        ImGui.TextUnformatted($"RSR next GCD age: {nextAge}");
        var nextActionAge = next.NextActionUpdatedAt == DateTime.MinValue
            ? "never"
            : $"{(DateTime.UtcNow - next.NextActionUpdatedAt).TotalMilliseconds:F0}ms";
        ImGui.TextUnformatted($"RSR next action positional: {next.NextActionRequirement}");
        ImGui.TextUnformatted($"RSR next action age: {nextActionAge}");
        ImGui.TextUnformatted($"NoCasting: {controller.LastNoCastingReason}");
        if (config.Settings.DebugLogging)
        {
            var cacheAge = controller.LastCachedSafety.UpdatedAt == DateTime.MinValue
                ? "never"
                : $"{(DateTime.UtcNow - controller.LastCachedSafety.UpdatedAt).TotalMilliseconds:F0}ms";
            ImGui.TextDisabled($"Safety cache age: {cacheAge}");
            ImGui.TextDisabled($"RSR next action: {next.NextActionName} ({next.NextActionId})");
            ImGui.TextDisabled($"True North available: {s.TrueNorthAvailable}");
        }
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
        var sideMode = (int)config.Settings.BorderSideMode;
        if (ImGui.Combo("Border side", ref sideMode, "Nearest\0Left\0Right\0"))
        {
            config.Settings.BorderSideMode = (BorderSideMode)sideMode;
            config.Save();
        }

        ImGui.DragFloat("Max move distance", ref config.Settings.MaxMoveDistance, 0.1f, 0.5f, 20f);
        ImGui.DragFloat("Distance from hitbox", ref config.Settings.DesiredDistanceFromTargetHitbox, 0.1f, 0.1f, 10f);
        ImGui.DragFloat("Committed positional angle", ref config.Settings.PositionalNudgeDegrees, 0.5f, 0f, 44f);
        ImGui.DragFloat("Border hold deadzone", ref config.Settings.BorderHoldDeadzoneYalms, 0.05f, 0.05f, 5f);
        ImGui.DragFloat("Positional deadzone", ref config.Settings.PositionalCommitDeadzoneYalms, 0.05f, 0.05f, 3f);
        ImGui.DragFloat("Destination change threshold", ref config.Settings.DestinationChangeThresholdYalms, 0.05f, 0.05f, 5f);
        ImGui.DragInt("Dependency refresh ms", ref config.Settings.DependencyRefreshMs, 50, 250, 10000);
        ImGui.DragInt("Safety refresh ms", ref config.Settings.SafetyRefreshMs, 25, 100, 5000);
        ImGui.DragInt("Repath cooldown ms", ref config.Settings.RepathCooldownMs, 10, 100, 5000);
        ImGui.DragFloat("Stop within yalms", ref config.Settings.StopWithinYalms, 0.05f, 0.05f, 3f);
        CheckboxSetting("Coordinate with RotationSolver: NoCasting only for next positional GCD", v => config.Settings.EnableRotationSolverCoordination = v, config.Settings.EnableRotationSolverCoordination);
        ImGui.DragInt("RSR next action max age ms", ref config.Settings.RsrNextActionMaxAgeMs, 25, 250, 5000);
        ImGui.DragInt("NoCasting cooldown ms", ref config.Settings.NoCastingCooldownMs, 25, 250, 10000);
        ImGui.DragFloat("NoCasting duration seconds", ref config.Settings.NoCastingDurationSeconds, 0.05f, 0.1f, 2f);
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
