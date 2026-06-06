using System.Numerics;
using Dalamud.Bindings.ImGui;
using Lumina.Excel.Sheets;
using PositionalPilot.Core.Model;
using PositionalPilot.Game;
using PositionalPilot.IPC;
using PositionalPilot.Movement;

namespace PositionalPilot.UI;

internal sealed class ConfigWindow
{
    private static readonly Vector4 Bg = new(0.08f, 0.09f, 0.11f, 1f);
    private static readonly Vector4 CardBg = new(0.12f, 0.13f, 0.16f, 1f);
    private static readonly Vector4 CardBgSoft = new(0.16f, 0.17f, 0.20f, 1f);
    private static readonly Vector4 Accent = new(0.25f, 0.72f, 0.68f, 1f);
    private static readonly Vector4 AccentSoft = new(0.31f, 0.55f, 0.86f, 1f);
    private static readonly Vector4 Warn = new(0.95f, 0.66f, 0.30f, 1f);
    private static readonly Vector4 Error = new(0.92f, 0.32f, 0.34f, 1f);
    private static readonly Vector4 TextDim = new(0.62f, 0.66f, 0.72f, 1f);

    private readonly PluginServices services;
    private readonly Configuration config;
    private readonly BossModIpc bossMod;
    private readonly RotationSolverIpc rotationSolver;
    private readonly WrathComboIpc wrathCombo;
    private readonly VnavmeshIpc vnavmesh;
    private readonly AvariceIpc avarice;
    private readonly MovementController controller;
    private readonly PositionalStatsService stats;
    private readonly PositionalActionEffectTracker positionalEffects;

    public ConfigWindow(
        PluginServices services,
        Configuration config,
        BossModIpc bossMod,
        RotationSolverIpc rotationSolver,
        WrathComboIpc wrathCombo,
        VnavmeshIpc vnavmesh,
        AvariceIpc avarice,
        MovementController controller,
        PositionalStatsService stats,
        PositionalActionEffectTracker positionalEffects)
    {
        this.services = services;
        this.config = config;
        this.bossMod = bossMod;
        this.rotationSolver = rotationSolver;
        this.wrathCombo = wrathCombo;
        this.vnavmesh = vnavmesh;
        this.avarice = avarice;
        this.controller = controller;
        this.stats = stats;
        this.positionalEffects = positionalEffects;
    }

    public bool IsOpen { get; set; }

    public void Draw()
    {
        if (!IsOpen)
            return;

        var open = IsOpen;
        ImGui.SetNextWindowSize(new Vector2(740, 560), ImGuiCond.FirstUseEver);
        PushWindowStyle();
        if (!ImGui.Begin("PositionalPilot", ref open))
        {
            IsOpen = open;
            ImGui.End();
            PopWindowStyle();
            return;
        }

        IsOpen = open;
        DrawHeader();
        if (ImGui.BeginTabBar("PositionalPilotTabs"))
        {
            DrawTab("Dashboard", DrawDashboard);
            DrawTab("Settings", DrawSettings);
            DrawTab("Statistics", DrawStatistics);
            DrawTab("Advanced", DrawAdvanced);
            ImGui.EndTabBar();
        }

        ImGui.End();
        PopWindowStyle();
    }

    public void DrawOverlay()
    {
        if (!config.Settings.ShowOverlay || config.Settings.MovementMode != MovementMode.SuggestOnly)
            return;

        ImGui.SetNextWindowBgAlpha(0.35f);
        ImGui.SetNextWindowPos(new Vector2(24, 260), ImGuiCond.FirstUseEver);
        ImGui.Begin("PositionalPilot Overlay", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings);
        ImGui.TextUnformatted($"{controller.CurrentMovementPositional} - {controller.CurrentMovementPositionalSource}");
        if (!string.IsNullOrWhiteSpace(controller.BlockReason))
            ImGui.TextDisabled(controller.BlockReason);
        ImGui.End();
    }

    private void DrawHeader()
    {
        RefreshDependencyStatus();
        var enabled = config.Settings.Enabled && config.Settings.MovementMode != MovementMode.Disabled;
        var accent = enabled ? Accent : TextDim;

        using (BeginCard("##header", new Vector2(-1, 82), accent))
        {
            ImGui.TextColored(accent, enabled ? "Active" : "Standing by");
            ImGui.SameLine();
            ImGui.TextUnformatted("PositionalPilot");
            ImGui.TextColored(TextDim, "Safe rear/flank movement, with high-confidence combat intent only.");

            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 6);
            DrawPill(config.Settings.MovementMode.ToString(), enabled ? Accent : TextDim);
            ImGui.SameLine();
            DrawPill(config.Settings.CombatIntentSource.ToString(), AccentSoft);
            ImGui.SameLine();
            DrawPill(controller.State.ToString(), string.IsNullOrWhiteSpace(controller.BlockReason) ? Accent : Warn);
        }
    }

    private void DrawDashboard()
    {
        var s = controller.LastSnapshot;
        var session = stats.SessionStats.GetSuccessfulPositionals(s.JobId);
        var lifetime = stats.LifetimeStats.GetSuccessfulPositionals(s.JobId);
        var job = FormatJob(s.JobId);

        var w = ImGui.GetContentRegionAvail().X;
        var gap = ImGui.GetStyle().ItemSpacing.X;
        var cardW = (w - gap * 2) / 3f;
        using (BeginCard("##intent", new Vector2(cardW, 112), AccentSoft))
            DrawMetric("Intent", controller.CurrentMovementPositional.ToString(), controller.CurrentMovementPositionalSource);
        ImGui.SameLine();
        using (BeginCard("##target", new Vector2(cardW, 112), s.TargetOmnidirectional == true ? Warn : Accent))
            DrawMetric("Target", s.HasTarget ? s.TargetName : "None", s.TargetIsTrainingDummy ? "Dummy block ignored" : FormatTargetStatus(s));
        ImGui.SameLine();
        using (BeginCard("##stats", new Vector2(cardW, 112), Accent))
            DrawMetric("Positionals", session.ToString(), $"{job} session, {lifetime} lifetime");

        ImGui.Spacing();
        using (BeginCard("##movement", new Vector2(-1, 150), string.IsNullOrWhiteSpace(controller.BlockReason) ? Accent : Warn))
        {
            DrawSectionTitle("Current Movement");
            DrawKeyValue("Mode", controller.CurrentMovementMode);
            DrawKeyValue("Border", controller.CurrentBorderSide.ToString());
            DrawKeyValue("Destination", controller.ChosenDestination?.ToString() ?? "none");
            DrawKeyValue("Block", string.IsNullOrWhiteSpace(controller.BlockReason) ? "none" : controller.BlockReason);
        }

        using (BeginCard("##deps", new Vector2(-1, 126), DependenciesReady() ? Accent : Warn))
        {
            DrawSectionTitle("Dependencies");
            DrawDependencyPills();
        }
    }

    private void DrawSettings()
    {
        using (BeginCard("##general-settings", new Vector2(-1, 118), Accent))
        {
            DrawSectionTitle("General");
            DrawCheckboxSetting("Enabled", "Master enable. Movement still requires AssistMove and safety gates.", value =>
            {
                config.Settings.Enabled = value;
                controller.ClearEmergencyStop();
            }, config.Settings.Enabled);

            var mode = (int)config.Settings.MovementMode;
            if (ImGui.Combo("Movement mode", ref mode, "Disabled\0SuggestOnly\0AssistMove\0"))
            {
                config.Settings.MovementMode = (MovementMode)mode;
                controller.ClearEmergencyStop();
                config.Save();
            }

            if (ImGui.Button("Emergency stop"))
                controller.EmergencyStop();
            ImGui.SameLine();
            DrawCheckboxSetting("Show overlay", "Shows a compact SuggestOnly overlay.", value => config.Settings.ShowOverlay = value, config.Settings.ShowOverlay);
        }

        using (BeginCard("##combat-settings", new Vector2(-1, 146), AccentSoft))
        {
            DrawSectionTitle("Combat Source");
            var source = (int)config.Settings.CombatIntentSource;
            if (ImGui.Combo("Combat intent source", ref source, "RotationSolverReborn\0WrathCombo\0"))
            {
                config.Settings.CombatIntentSource = (CombatIntentSource)source;
                config.Save();
            }

            DrawCheckboxSetting("Coordinate with RotationSolver", "Only applies to RotationSolver source; Wrath has no NoCasting path.", value => config.Settings.EnableRotationSolverCoordination = value, config.Settings.EnableRotationSolverCoordination);
            DrawCheckboxSetting("SAM Meikyo Wrath anticipation", "Allows SAM Meikyo/Sen anticipation for Wrath source.", value => config.Settings.EnableSamMeikyoWrathAnticipation = value, config.Settings.EnableSamMeikyoWrathAnticipation);
            DrawIntSetting("Intent max age ms", "Max age for cached RSR/Wrath supplemental combat state.", ref config.Settings.RsrNextActionMaxAgeMs, 25, 250, 5000);
        }

        using (BeginCard("##movement-settings", new Vector2(-1, 286), Accent))
        {
            DrawSectionTitle("Movement");
            var sideMode = (int)config.Settings.BorderSideMode;
            if (ImGui.Combo("Border side", ref sideMode, "Nearest\0Left\0Right\0"))
            {
                config.Settings.BorderSideMode = (BorderSideMode)sideMode;
                config.Save();
            }
            DrawFloatSetting("Max move distance", "Maximum single assist movement distance in yalms.", ref config.Settings.MaxMoveDistance, 0.1f, 0.5f, 20f);
            DrawFloatSetting("Distance from hitbox", "Destination ring distance outside target hitbox.", ref config.Settings.DesiredDistanceFromTargetHitbox, 0.1f, 0.1f, 10f);
            DrawFloatSetting("Committed positional angle", "How deep into Rear/Flank to move for known positionals.", ref config.Settings.PositionalNudgeDegrees, 0.5f, 0f, 44f);
            DrawFloatSetting("Border hold deadzone", "Loose radius for Any border holding.", ref config.Settings.BorderHoldDeadzoneYalms, 0.05f, 0.05f, 5f);
            DrawFloatSetting("Positional deadzone", "Tight radius for committed Rear/Flank movement.", ref config.Settings.PositionalCommitDeadzoneYalms, 0.05f, 0.05f, 3f);
            DrawFloatSetting("Destination change threshold", "Ignore tiny retarget changes while moving.", ref config.Settings.DestinationChangeThresholdYalms, 0.05f, 0.05f, 5f);
            DrawIntSetting("Repath cooldown ms", "Minimum cadence between movement commands.", ref config.Settings.RepathCooldownMs, 10, 100, 5000);
            DrawFloatSetting("Stop within yalms", "Distance considered close enough to stop.", ref config.Settings.StopWithinYalms, 0.05f, 0.05f, 3f);
        }

        using (BeginCard("##safety-settings", new Vector2(-1, 342), Warn))
        {
            DrawSectionTitle("Safety");
            DrawDependencyFlag("Require BossMod safety", "Requires BossMod destination and dash safety IPC.", RequiredDependencies.RequireBossModSafety);
            DrawDependencyFlag("Require vnavmesh", "Blocks AssistMove if vnavmesh is unavailable.", RequiredDependencies.RequireVnavmesh);
            DrawDependencyFlag("Require combat solver", "Blocks when selected combat intent source is unavailable.", RequiredDependencies.RequireCombatSolver);
            DrawCheckboxSetting("Disable during casting", "Blocks movement while local player is casting.", value => config.Settings.DisableDuringCasting = value, config.Settings.DisableDuringCasting);
            DrawCheckboxSetting("Disable during manual movement", "Stops if you manually move while vnavmesh is not active.", value => config.Settings.DisableDuringManualMovement = value, config.Settings.DisableDuringManualMovement);
            DrawCheckboxSetting("Disable during upcoming damage", "Blocks shortly before BossMod incoming damage.", value => config.Settings.DisableDuringUpcomingDamage = value, config.Settings.DisableDuringUpcomingDamage);
            DrawFloatSetting("Damage block seconds", "BossMod damage lead time.", ref config.Settings.UpcomingDamageBlockSeconds, 0.1f, 0.1f, 10f);
            DrawCheckboxSetting("Disable during upcoming knockback", "Blocks shortly before BossMod knockback.", value => config.Settings.DisableDuringUpcomingKnockback = value, config.Settings.DisableDuringUpcomingKnockback);
            DrawFloatSetting("Knockback block seconds", "BossMod knockback lead time.", ref config.Settings.UpcomingKnockbackBlockSeconds, 0.1f, 0.1f, 15f);
            DrawCheckboxSetting("Disable during downtime", "Blocks during BossMod downtime.", value => config.Settings.DisableDuringDowntime = value, config.Settings.DisableDuringDowntime);
            DrawCheckboxSetting("Only in combat", "Blocks movement outside combat.", value => config.Settings.OnlyInCombat = value, config.Settings.OnlyInCombat);
            DrawCheckboxSetting("Only melee jobs", "Blocks movement on non-melee jobs.", value => config.Settings.OnlyMeleeJobs = value, config.Settings.OnlyMeleeJobs);
        }
    }

    private void DrawStatistics()
    {
        using (BeginCard("##stats-controls", new Vector2(-1, 128), Accent))
        {
            DrawSectionTitle("Successful Positionals");
            DrawCheckboxSetting("Track successful positionals", "Uses action-effect result data for known positional actions.", value => config.Settings.TrackSuccessfulPositionals = value, config.Settings.TrackSuccessfulPositionals);
            DrawKeyValue("Tracker", positionalEffects.Available ? "action effects hooked" : "hook unavailable");
            DrawKeyValue("Last event", positionalEffects.LastEvent);
            if (ImGui.Button("Reset session"))
                stats.ClearSession();
            ImGui.SameLine();
            if (ImGui.Button("Reset lifetime"))
                stats.ClearLifetime();
        }

        DrawStatsTable("Session", stats.SessionStats);
        DrawStatsTable("Lifetime", stats.LifetimeStats);
    }

    private void DrawAdvanced()
    {
        using (BeginCard("##advanced-controls", new Vector2(-1, 118), TextDim))
        {
            DrawSectionTitle("Advanced Diagnostics");
            DrawCheckboxSetting("Debug logging", "Enables throttled Dalamud log messages.", value => config.Settings.DebugLogging = value, config.Settings.DebugLogging);
            DrawIntSetting("Dependency refresh ms", "How often IPC availability is refreshed.", ref config.Settings.DependencyRefreshMs, 50, 250, 10000);
            DrawIntSetting("Safety refresh ms", "How often cached safety state is refreshed.", ref config.Settings.SafetyRefreshMs, 25, 100, 5000);
        }

        using (BeginCard("##advanced-state", new Vector2(-1, 184), AccentSoft))
        {
            var next = controller.LastRotationSolverNextAction;
            var wrath = controller.LastWrathComboNextAction;
            var localWrath = controller.LastWrathLocalPrediction;
            DrawSectionTitle("Combat Diagnostics");
            DrawKeyValue("RSR next GCD", $"{next.NextGcdActionName} ({next.NextGcdActionId}) - {next.NextGcdRequirement}, {FormatAge(next.NextGcdUpdatedAt)}");
            DrawKeyValue("RSR next action", $"{next.NextActionName} ({next.NextActionId}) - {next.NextActionRequirement}, {FormatAge(next.NextActionUpdatedAt)}");
            DrawKeyValue("NoCasting", controller.LastNoCastingReason);
            DrawKeyValue("Wrath raw", $"{wrath.LatestActionName} ({wrath.LatestActionId}), {FormatAge(wrath.LatestActionUpdatedAt)}");
            DrawKeyValue("Wrath filtered", $"{wrath.LatestWeaponskillOrSpellActionName} ({wrath.LatestWeaponskillOrSpellActionId}), {FormatAge(wrath.LatestWeaponskillOrSpellUpdatedAt)}");
            DrawKeyValue("Wrath combo", localWrath.ComboActionId.ToString());
            DrawKeyValue("Wrath prediction", $"{localWrath.Requirement} - {localWrath.Source}");
        }

        using (BeginCard("##advanced-deps", new Vector2(-1, 190), Warn))
        {
            DrawSectionTitle("Dependency Errors");
            DrawKeyValue("BossMod", bossMod.LastError ?? "ok");
            DrawKeyValue("RotationSolver", rotationSolver.LastError ?? "ok");
            DrawKeyValue("RSR events", rotationSolver.EventLastError ?? "ok");
            DrawKeyValue("WrathCombo", wrathCombo.LastError ?? "ok");
            DrawKeyValue("Wrath events", wrathCombo.EventLastError ?? "ok");
            DrawKeyValue("vnavmesh", vnavmesh.LastError ?? "ok");
            DrawKeyValue("Avarice", avarice.LastError ?? "optional");
            DrawKeyValue("Safety cache", FormatAge(controller.LastCachedSafety.UpdatedAt));
        }
    }

    private void DrawStatsTable(string label, PositionalStatsBook book)
    {
        using (BeginCard($"##stats-{label}", new Vector2(-1, 190), AccentSoft))
        {
            DrawSectionTitle(label);
            if (book.ByJob.Count == 0)
            {
                ImGui.TextColored(TextDim, "No successful positional data yet.");
                return;
            }

            if (ImGui.BeginTable($"##table-{label}", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchSame))
            {
                ImGui.TableSetupColumn("Job");
                ImGui.TableSetupColumn("Successful");
                ImGui.TableHeadersRow();
                foreach (var row in book.ByJob.OrderByDescending(x => x.Value.SuccessfulPositionals))
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(FormatJob(row.Key));
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(row.Value.SuccessfulPositionals.ToString());
                }
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted("Total");
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(book.TotalSuccessfulPositionals.ToString());
                ImGui.EndTable();
            }
        }
    }

    private void DrawDependencyPills()
    {
        DrawPill("BossMod", bossMod.Available ? Accent : Error);
        ImGui.SameLine();
        DrawPill("vnavmesh", vnavmesh.Available ? Accent : Error);
        ImGui.SameLine();
        DrawPill("RSR", rotationSolver.Available ? Accent : Warn);
        ImGui.SameLine();
        DrawPill("Wrath", wrathCombo.Available ? Accent : Warn);
        ImGui.SameLine();
        DrawPill("Stats", positionalEffects.Available ? Accent : Warn);
    }

    private void DrawTab(string label, System.Action draw)
    {
        if (!ImGui.BeginTabItem(label))
            return;

        ImGui.Spacing();
        draw();
        ImGui.EndTabItem();
    }

    private void RefreshDependencyStatus()
    {
        bossMod.RefreshAvailability();
        rotationSolver.RefreshAvailability();
        wrathCombo.RefreshAvailability();
        vnavmesh.RefreshAvailability();
        avarice.RefreshAvailability();
    }

    private bool DependenciesReady() => bossMod.Available && vnavmesh.Available;

    private string FormatJob(uint jobId) =>
        services.Data.GetExcelSheet<ClassJob>().GetRowOrDefault(jobId)?.NameEnglish.ToString() is { Length: > 0 } name
            ? name
            : $"Job {jobId}";

    private static string FormatTargetStatus(GameSnapshot s)
    {
        if (!s.HasTarget)
            return "No target";
        if (s.TargetOmnidirectional == true)
            return "No positionals";
        if (s.TargetTargetsPlayer == true)
            return "Targeting player";
        return "Positionals enabled";
    }

    private static void DrawMetric(string label, string value, string detail)
    {
        ImGui.TextColored(TextDim, label);
        ImGui.SetWindowFontScale(1.25f);
        ImGui.TextUnformatted(Fit(value, ImGui.GetContentRegionAvail().X));
        ImGui.SetWindowFontScale(1f);
        ImGui.TextColored(TextDim, Fit(detail, ImGui.GetContentRegionAvail().X));
    }

    private static void DrawSectionTitle(string label)
    {
        ImGui.TextColored(TextDim, label.ToUpperInvariant());
        ImGui.Separator();
    }

    private static void DrawKeyValue(string label, string value)
    {
        ImGui.TextColored(TextDim, label);
        ImGui.SameLine(180);
        ImGui.TextUnformatted(value);
    }

    private static void DrawPill(string label, Vector4 color)
    {
        var pad = new Vector2(9, 4);
        var pos = ImGui.GetCursorScreenPos();
        var size = ImGui.CalcTextSize(label) + pad * 2;
        ImGui.GetWindowDrawList().AddRectFilled(pos, pos + size, ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, 0.18f)), 6f);
        ImGui.GetWindowDrawList().AddRect(pos, pos + size, ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, 0.55f)), 6f);
        ImGui.SetCursorScreenPos(pos + pad);
        ImGui.TextColored(color, label);
        ImGui.SetCursorScreenPos(new Vector2(pos.X + size.X, pos.Y));
        ImGui.Dummy(size);
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

    private void DrawCheckboxSetting(string label, string tooltip, System.Action<bool> setter, bool current)
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

    private static void DrawTooltip(string text)
    {
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(text);
    }

    private static string FormatAge(DateTime timestamp) =>
        timestamp == DateTime.MinValue ? "never" : $"{(DateTime.UtcNow - timestamp).TotalMilliseconds:F0}ms";

    private static string Fit(string text, float maxWidth)
    {
        if (ImGui.CalcTextSize(text).X <= maxWidth)
            return text;
        while (text.Length > 1 && ImGui.CalcTextSize(text + "...").X > maxWidth)
            text = text[..^1];
        return text + "...";
    }

    private static CardScope BeginCard(string id, Vector2 size, Vector4 accent)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, CardBg);
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(accent.X, accent.Y, accent.Z, 0.45f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 8f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1f);
        ImGui.BeginChild(id, size, true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, CardBgSoft);
        return new CardScope();
    }

    private static void PushWindowStyle()
    {
        ImGui.PushStyleColor(ImGuiCol.WindowBg, Bg);
        ImGui.PushStyleColor(ImGuiCol.Tab, CardBg);
        ImGui.PushStyleColor(ImGuiCol.TabHovered, CardBgSoft);
        ImGui.PushStyleColor(ImGuiCol.TabActive, CardBgSoft);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, CardBgSoft);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.20f, 0.22f, 0.26f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Button, CardBgSoft);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.20f, 0.28f, 0.32f, 1f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 8f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 8f);
    }

    private static void PopWindowStyle()
    {
        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(8);
    }

    private readonly ref struct CardScope
    {
        public void Dispose()
        {
            ImGui.PopStyleColor();
            ImGui.EndChild();
            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(2);
        }
    }
}
