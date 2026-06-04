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
    private readonly WrathComboIpc wrathCombo;
    private readonly VnavmeshIpc vnavmesh;
    private readonly AvariceIpc avarice;
    private readonly MovementController controller;

    public ConfigWindow(Configuration config, BossModIpc bossMod, RotationSolverIpc rotationSolver, WrathComboIpc wrathCombo, VnavmeshIpc vnavmesh, AvariceIpc avarice, MovementController controller)
    {
        this.config = config;
        this.bossMod = bossMod;
        this.rotationSolver = rotationSolver;
        this.wrathCombo = wrathCombo;
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
        if (ImGui.BeginTabBar("PositionalPilotTabs"))
        {
            DrawTab("Main", DrawMain);
            DrawTab("Status", DrawStatus);
            DrawTab("Safety", DrawSafety);
            DrawTab("Movement", DrawMovement);
            DrawTab("Combat Source", DrawCombatSource);
            DrawTab("Debug", DrawDebug);
            ImGui.EndTabBar();
        }

        ImGui.End();
    }

    public void DrawOverlay()
    {
        if (!config.Settings.ShowOverlay || config.Settings.MovementMode != MovementMode.SuggestOnly)
            return;

        ImGui.SetNextWindowBgAlpha(0.35f);
        ImGui.SetNextWindowPos(new System.Numerics.Vector2(24, 260), ImGuiCond.FirstUseEver);
        ImGui.Begin("PositionalPilot Overlay", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings);
        ImGui.TextUnformatted($"Positional: {controller.CurrentMovementPositional}");
        DrawTooltip("The current movement intent. Any means PositionalPilot is holding a rear/flank border.");
        ImGui.TextUnformatted(controller.ChosenDestination.HasValue ? "Safe destination ready" : "No safe destination");
        DrawTooltip("Shows whether PositionalPilot found one BossMod-safe destination for the current movement intent.");
        if (!string.IsNullOrWhiteSpace(controller.BlockReason))
        {
            ImGui.TextUnformatted(controller.BlockReason);
            DrawTooltip("The current reason PositionalPilot is not issuing or continuing movement.");
        }

        ImGui.End();
    }

    private void DrawTab(string label, Action draw)
    {
        if (!ImGui.BeginTabItem(label))
            return;

        draw();
        ImGui.EndTabItem();
    }

    private void DrawMain()
    {
        DrawCheckboxSetting(
            "Enabled",
            "Master enable. PositionalPilot still only moves in AssistMove mode and after all safety gates pass.",
            value =>
            {
                config.Settings.Enabled = value;
                controller.ClearEmergencyStop();
            },
            config.Settings.Enabled);

        var mode = (int)config.Settings.MovementMode;
        if (ImGui.Combo("Movement mode", ref mode, "Disabled\0SuggestOnly\0AssistMove\0"))
        {
            config.Settings.MovementMode = (MovementMode)mode;
            controller.ClearEmergencyStop();
            config.Save();
        }
        DrawTooltip("Disabled never evaluates movement. SuggestOnly shows intent/status. AssistMove can ask vnavmesh to move when safety passes.");

        if (ImGui.Button("Emergency stop"))
            controller.EmergencyStop();
        DrawTooltip("Immediately disables the plugin, switches movement mode to Disabled, and calls vnavmesh Stop.");

        DrawCheckboxSetting(
            "Debug logging",
            "Enables throttled Dalamud log messages for IPC state, blocks, selected destinations, and movement starts/stops.",
            value => config.Settings.DebugLogging = value,
            config.Settings.DebugLogging);
    }

    private void DrawStatus()
    {
        RefreshDependencyStatus();

        var s = controller.LastSnapshot;
        var positionals = s.TargetOmnidirectional switch
        {
            true => "not required",
            false => "required",
            _ => "unknown",
        };
        var targetTargetsPlayer = s.TargetTargetsPlayer switch
        {
            true => "yes",
            false => "no",
            _ => "unknown",
        };

        ImGui.TextUnformatted("Dependencies");
        DrawDependency("BossModReborn", bossMod.Available, bossMod.LastError, "Used as the safety authority for destination and route checks.");
        DrawDependency("RotationSolverReborn", rotationSolver.Available, rotationSolver.LastError, "Used for RSR next-action events and optional NoCasting coordination.");
        DrawDependency("RSR next-action events", rotationSolver.NextActionEventsAvailable, rotationSolver.EventLastError, "Required for PositionalPilot to know which Rear/Flank action is coming next.");
        DrawDependency("WrathCombo", wrathCombo.Available, wrathCombo.LastError, "Optional combat intent source. Wrath does not expose next-position IPC, so PositionalPilot infers only known transitions.");
        DrawDependency("WrathCombo action events", wrathCombo.ActionEventsAvailable, wrathCombo.EventLastError, "Required for Wrath last-GCD inference. If unavailable, Wrath source falls back to border hold.");
        DrawDependency("vnavmesh", vnavmesh.Available, vnavmesh.LastError, "Used to issue the actual movement request.");
        DrawDependency("Avarice", avarice.Available, avarice.LastError ?? "optional; only CardinalDirection IPC found", "Reference-only status. PositionalPilot does not depend on Avarice for movement.");

        ImGui.Separator();
        ImGui.TextUnformatted("Current state");
        DrawStatusRow("Target", s.HasTarget ? s.TargetName : "none", "The current target used for hitbox, facing, and target-of-target checks.");
        DrawStatusRow("Target positionals", positionals, "Omnidirectional targets do not require positionals, so assist movement is blocked.");
        DrawStatusRow("Target targeting player", targetTargetsPlayer, "Confirmed yes blocks assist movement to avoid chasing a target that is tracking you.");
        DrawStatusRow("Target hitbox", $"{s.TargetHitboxRadius:F2}", "Used with Distance from hitbox to choose the destination ring.");
        DrawStatusRow("BossMod positional", controller.CurrentPositional.ToString(), "Diagnostic only. Movement intent comes from RSR or rear/flank border hold.");
        DrawStatusRow("Combat intent source", config.Settings.CombatIntentSource.ToString(), "Selected source for committed Rear/Flank movement.");
        DrawStatusRow("Movement positional", controller.CurrentMovementPositional.ToString(), "The current movement intent: Any border hold, or committed Rear/Flank.");
        DrawStatusRow("Movement mode", controller.CurrentMovementMode, "Human-readable movement intent mode.");
        DrawStatusRow("Movement source", controller.CurrentMovementPositionalSource, "Why this movement intent was selected.");
        DrawStatusRow("Border side", controller.CurrentBorderSide.ToString(), "The selected rear-left or rear-right staging border.");
        DrawStatusRow("Chosen destination", controller.ChosenDestination?.ToString() ?? "none", "The single destination currently selected for vnavmesh.");
        DrawStatusRow("Movement state", controller.State.ToString(), "Current movement state machine state.");
        DrawStatusRow("Block reason", controller.BlockReason, "The current reason movement is blocked or idle.");

        var next = controller.LastRotationSolverNextAction;
        DrawStatusRow("RSR next GCD", $"{next.NextGcdActionName} ({next.NextGcdActionId})", "Latest cached next-GCD action from RSR.");
        DrawStatusRow("RSR next GCD positional", next.NextGcdRequirement.ToString(), "Mapped positional requirement for the cached next GCD.");
        DrawStatusRow("RSR next GCD age", FormatAge(next.NextGcdUpdatedAt), "Fresh values can drive committed Rear/Flank movement.");
        DrawStatusRow("RSR next action positional", next.NextActionRequirement.ToString(), "Fallback mapped positional when next GCD is unknown or stale.");
        DrawStatusRow("RSR next action age", FormatAge(next.NextActionUpdatedAt), "Fresh fallback values can also drive committed Rear/Flank movement.");
        DrawStatusRow("NoCasting", controller.LastNoCastingReason, "Most recent RotationSolver NoCasting decision.");
        var wrath = controller.LastWrathComboNextAction;
        DrawStatusRow("Wrath last GCD", $"{wrath.LastGcdActionName} ({wrath.LastGcdActionId})", "Latest action observed from WrathCombo's OnActionUsed IPC.");
        DrawStatusRow("Wrath inferred next", wrath.InferredNextRequirement.ToString(), "Conservative next positional inferred from known Wrath transitions.");
        DrawStatusRow("Wrath last GCD age", FormatAge(wrath.LastGcdUpdatedAt), "Fresh inferred values can drive committed movement when WrathCombo is selected.");
    }

    private void DrawSafety()
    {
        ImGui.TextUnformatted("Dependency requirements");
        DrawDependencyFlag(
            "Require BossMod safety",
            "Default on. Requires BossMod destination and dash safety IPC before movement.",
            RequiredDependencies.RequireBossModSafety);
        DrawDependencyFlag(
            "Require vnavmesh",
            "Default on. Blocks AssistMove if vnavmesh IPC or navmesh readiness is unavailable.",
            RequiredDependencies.RequireVnavmesh);
        DrawDependencyFlag(
            "Require combat solver",
            "Default off. Blocks movement when the selected combat intent source is unavailable.",
            RequiredDependencies.RequireCombatSolver);

        ImGui.Separator();
        ImGui.TextUnformatted("Safety gates");
        DrawCheckboxSetting("Disable during casting", "Blocks movement while the local player is casting.", value => config.Settings.DisableDuringCasting = value, config.Settings.DisableDuringCasting);
        DrawCheckboxSetting("Disable during manual movement", "Stops and cools down if you manually move while vnavmesh is not the active mover.", value => config.Settings.DisableDuringManualMovement = value, config.Settings.DisableDuringManualMovement);
        DrawCheckboxSetting("Disable during upcoming damage", "Blocks movement shortly before BossMod-reported incoming damage.", value => config.Settings.DisableDuringUpcomingDamage = value, config.Settings.DisableDuringUpcomingDamage);
        DrawFloatSetting("Damage block seconds", "How soon before BossMod-reported damage PositionalPilot should stand down.", ref config.Settings.UpcomingDamageBlockSeconds, 0.1f, 0.1f, 10f);
        DrawCheckboxSetting("Disable during upcoming knockback", "Blocks movement shortly before BossMod-reported knockback.", value => config.Settings.DisableDuringUpcomingKnockback = value, config.Settings.DisableDuringUpcomingKnockback);
        DrawFloatSetting("Knockback block seconds", "How soon before BossMod-reported knockback PositionalPilot should stand down.", ref config.Settings.UpcomingKnockbackBlockSeconds, 0.1f, 0.1f, 15f);
        DrawCheckboxSetting("Disable during downtime", "Blocks movement during BossMod-reported downtime.", value => config.Settings.DisableDuringDowntime = value, config.Settings.DisableDuringDowntime);
        DrawCheckboxSetting("Only in combat", "Blocks movement outside combat. Turn off for dummy testing if needed.", value => config.Settings.OnlyInCombat = value, config.Settings.OnlyInCombat);
        DrawCheckboxSetting("Only melee jobs", "Blocks movement on non-melee jobs. Melee list includes modern melee jobs such as Viper.", value => config.Settings.OnlyMeleeJobs = value, config.Settings.OnlyMeleeJobs);
        DrawCheckboxSetting("Show overlay", "Shows a small SuggestOnly overlay with movement intent and block status.", value => config.Settings.ShowOverlay = value, config.Settings.ShowOverlay);
    }

    private void DrawMovement()
    {
        var sideMode = (int)config.Settings.BorderSideMode;
        if (ImGui.Combo("Border side", ref sideMode, "Nearest\0Left\0Right\0"))
        {
            config.Settings.BorderSideMode = (BorderSideMode)sideMode;
            config.Save();
        }
        DrawTooltip("Nearest keeps the closest safe rear/flank border. Left/Right force one target-relative rear/flank border when safe.");

        DrawFloatSetting("Max move distance", "Maximum single assist movement distance in yalms. Larger values allow bigger corrections but are less conservative.", ref config.Settings.MaxMoveDistance, 0.1f, 0.5f, 20f);
        DrawFloatSetting("Distance from hitbox", "How far outside the target hitbox the destination ring should sit.", ref config.Settings.DesiredDistanceFromTargetHitbox, 0.1f, 0.1f, 10f);
        DrawFloatSetting("Committed positional angle", "How far from the rear/flank border to commit for a known Rear or Flank positional. Default 30 degrees keeps the destination well inside the slice.", ref config.Settings.PositionalNudgeDegrees, 0.5f, 0f, 44f);
        DrawFloatSetting("Border hold deadzone", "Loose radius for Any border holding. Larger values reduce micro-movement.", ref config.Settings.BorderHoldDeadzoneYalms, 0.05f, 0.05f, 5f);
        DrawFloatSetting("Positional deadzone", "Tighter radius used for committed Rear/Flank movement so border holding does not suppress positional movement.", ref config.Settings.PositionalCommitDeadzoneYalms, 0.05f, 0.05f, 3f);
        DrawFloatSetting("Destination change threshold", "While already navigating, ignore tiny destination changes below this distance to avoid retarget spam.", ref config.Settings.DestinationChangeThresholdYalms, 0.05f, 0.05f, 5f);
        DrawIntSetting("Repath cooldown ms", "Minimum cadence between movement commands unless a front escape or fresh RSR positional bypasses it.", ref config.Settings.RepathCooldownMs, 10, 100, 5000);
        DrawFloatSetting("Stop within yalms", "Distance considered close enough to stop movement.", ref config.Settings.StopWithinYalms, 0.05f, 0.05f, 3f);
    }

    private void DrawCombatSource()
    {
        var source = (int)config.Settings.CombatIntentSource;
        if (ImGui.Combo("Combat intent source", ref source, "RotationSolverReborn\0WrathCombo\0"))
        {
            config.Settings.CombatIntentSource = (CombatIntentSource)source;
            config.Save();
        }
        DrawTooltip("RotationSolver uses next-action events. WrathCombo uses conservative last-GCD inference because Wrath does not expose next-positional prediction IPC.");

        DrawCheckboxSetting(
            "Coordinate with RotationSolver",
            "Default off. Only applies when RotationSolverReborn is selected; WrathCombo has no NoCasting IPC.",
            value => config.Settings.EnableRotationSolverCoordination = value,
            config.Settings.EnableRotationSolverCoordination);
        DrawIntSetting("RSR next action max age ms", "Maximum age for cached RSR next-GCD or next-action data before it is ignored.", ref config.Settings.RsrNextActionMaxAgeMs, 25, 250, 5000);
        DrawIntSetting("NoCasting cooldown ms", "Minimum time between NoCasting requests to avoid spamming RSR IPC.", ref config.Settings.NoCastingCooldownMs, 25, 250, 10000);
        DrawFloatSetting("NoCasting duration seconds", "How long each NoCasting request lasts. Keep short so RSR resumes quickly.", ref config.Settings.NoCastingDurationSeconds, 0.05f, 0.1f, 2f);

        ImGui.Separator();
        var next = controller.LastRotationSolverNextAction;
        DrawStatusRow("Next GCD", $"{next.NextGcdActionName} ({next.NextGcdActionId})", "Latest cached RSR next GCD.");
        DrawStatusRow("Next GCD positional", next.NextGcdRequirement.ToString(), "Mapped positional for the latest next GCD.");
        DrawStatusRow("Next action", $"{next.NextActionName} ({next.NextActionId})", "Diagnostic/fallback RSR next action.");
        DrawStatusRow("Next action positional", next.NextActionRequirement.ToString(), "Mapped positional for the fallback next action.");
        DrawStatusRow("NoCasting", controller.LastNoCastingReason, "The last reason NoCasting did or did not trigger.");

        ImGui.Separator();
        var wrath = controller.LastWrathComboNextAction;
        DrawStatusRow("Wrath auto-rotation", FormatOptionalBool(wrathCombo.AutoRotationEnabled), "WrathCombo auto-rotation state when exposed by IPC.");
        DrawStatusRow("Wrath current job ready", FormatOptionalBool(wrathCombo.CurrentJobReady), "Whether Wrath reports the current job is configured for auto-rotation.");
        DrawStatusRow("Wrath last GCD", $"{wrath.LastGcdActionName} ({wrath.LastGcdActionId})", "Latest action observed from WrathCombo's OnActionUsed IPC.");
        DrawStatusRow("Wrath inferred next", wrath.InferredNextRequirement.ToString(), "Only explicit known transitions infer a committed positional; unknown transitions fall back to border hold.");
        DrawStatusRow("Wrath event status", wrath.EventsAvailable ? "available" : "missing/error", "Wrath inference requires OnActionUsed events.");
    }

    private void DrawDebug()
    {
        RefreshDependencyStatus();
        DrawIntSetting("Dependency refresh ms", "How often PositionalPilot refreshes IPC availability status.", ref config.Settings.DependencyRefreshMs, 50, 250, 10000);
        DrawIntSetting("Safety refresh ms", "How often cached safety state is refreshed. Fresh BossMod safety is still checked before issuing movement.", ref config.Settings.SafetyRefreshMs, 25, 100, 5000);

        ImGui.Separator();
        DrawStatusRow("BossMod error", bossMod.LastError ?? "ok", "Raw BossMod IPC availability or call error.");
        DrawStatusRow("RotationSolver error", rotationSolver.LastError ?? "ok", "Raw RotationSolver coordination IPC availability or call error.");
        DrawStatusRow("RSR event error", rotationSolver.EventLastError ?? "ok", "Raw RSR next-action event subscription error.");
        DrawStatusRow("WrathCombo error", wrathCombo.LastError ?? "ok", "Raw WrathCombo IPC availability or call error.");
        DrawStatusRow("WrathCombo event error", wrathCombo.EventLastError ?? "ok", "Raw WrathCombo OnActionUsed event subscription error.");
        DrawStatusRow("vnavmesh error", vnavmesh.LastError ?? "ok", "Raw vnavmesh IPC availability or call error.");
        DrawStatusRow("Avarice status", avarice.LastError ?? "optional; local geometry active", "Avarice is optional/reference-only. PositionalPilot uses local rear/flank geometry.");
        DrawStatusRow("Safety cache age", FormatAge(controller.LastCachedSafety.UpdatedAt), "Age of cached dependency/safety values.");
        DrawStatusRow("True North available", controller.LastSnapshot.TrueNorthAvailable.ToString(), "NoCasting is suppressed when True North is available, but movement can still happen.");
    }

    private void RefreshDependencyStatus()
    {
        bossMod.RefreshAvailability();
        rotationSolver.RefreshAvailability();
        wrathCombo.RefreshAvailability();
        vnavmesh.RefreshAvailability();
        avarice.RefreshAvailability();
    }

    private void DrawDependency(string name, bool available, string? error, string tooltip)
    {
        ImGui.BulletText($"{name}: {(available ? "available" : "missing/error")}");
        DrawTooltip(tooltip);
        if (!available && !string.IsNullOrWhiteSpace(error))
            ImGui.TextDisabled(error);
    }

    private void DrawDependencyFlag(string label, string tooltip, RequiredDependencies flag)
    {
        var value = config.Settings.RequiredDependencies.HasFlag(flag);
        DrawCheckboxSetting(label, tooltip, enabled =>
        {
            if (enabled)
                config.Settings.RequiredDependencies |= flag;
            else
                config.Settings.RequiredDependencies &= ~flag;
        }, value);
    }

    private void DrawCheckboxSetting(string label, string tooltip, Action<bool> setter, bool current)
    {
        var value = current;
        if (ImGui.Checkbox(label, ref value))
        {
            setter(value);
            config.Save();
        }
        DrawTooltip(tooltip);
    }

    private void DrawFloatSetting(string label, string tooltip, ref float value, float speed, float min, float max)
    {
        if (ImGui.DragFloat(label, ref value, speed, min, max))
            config.Save();
        DrawTooltip(tooltip);
    }

    private void DrawIntSetting(string label, string tooltip, ref int value, int speed, int min, int max)
    {
        if (ImGui.DragInt(label, ref value, speed, min, max))
            config.Save();
        DrawTooltip(tooltip);
    }

    private void DrawStatusRow(string label, string value, string tooltip)
    {
        ImGui.TextUnformatted($"{label}: {value}");
        DrawTooltip(tooltip);
    }

    private static void DrawTooltip(string text)
    {
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(text);
    }

    private static string FormatAge(DateTime timestamp) =>
        timestamp == DateTime.MinValue ? "never" : $"{(DateTime.UtcNow - timestamp).TotalMilliseconds:F0}ms";

    private static string FormatOptionalBool(bool? value) => value switch
    {
        true => "yes",
        false => "no",
        _ => "unknown",
    };
}
