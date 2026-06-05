using Dalamud.Plugin.Ipc;
using FFXIVClientStructs.FFXIV.Client.Game;
using PositionalPilot.Core.Model;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace PositionalPilot.IPC;

internal sealed class WrathComboIpc : IpcAdapterBase, IDisposable
{
    private const uint ActionCategorySpell = 2;
    private const uint ActionCategoryWeaponskill = 3;
    private readonly ICallGateSubscriber<bool> ipcReady;
    private readonly ICallGateSubscriber<bool> autoRotationState;
    private readonly ICallGateSubscriber<bool> currentJobAutoRotationReady;
    private readonly ICallGateSubscriber<ActionType, uint, object> onActionUsed;
    private bool eventsSubscribed;
    private DateTime nextEventSubscribeAttempt = DateTime.MinValue;

    public WrathComboIpc(PluginServices services, ThrottledLogger logger)
        : base(services, logger)
    {
        var pi = services.PluginInterface;
        ipcReady = pi.GetIpcSubscriber<bool>("WrathCombo.IPCReady");
        autoRotationState = pi.GetIpcSubscriber<bool>("WrathCombo.GetAutoRotationState");
        currentJobAutoRotationReady = pi.GetIpcSubscriber<bool>("WrathCombo.IsCurrentJobAutoRotationReady");
        onActionUsed = pi.GetIpcSubscriber<ActionType, uint, object>("OnActionUsed");
    }

    public bool ActionEventsAvailable { get; private set; }
    public string? EventLastError { get; private set; }
    public uint LatestActionId { get; private set; }
    public DateTime LatestActionUpdatedAt { get; private set; } = DateTime.MinValue;
    public uint LatestWeaponskillOrSpellActionId { get; private set; }
    public DateTime LatestWeaponskillOrSpellUpdatedAt { get; private set; } = DateTime.MinValue;
    public bool? AutoRotationEnabled { get; private set; }
    public bool? CurrentJobReady { get; private set; }

    public override void RefreshAvailability()
    {
        SetAvailability(
            "WrathCombo IPC providers not found",
            () => ipcReady.HasFunction || autoRotationState.HasFunction || currentJobAutoRotationReady.HasFunction || eventsSubscribed);

        AutoRotationEnabled = autoRotationState.HasFunction && TryOptionalCall(nameof(GetAutoRotationState), () => autoRotationState.InvokeFunc(), out var enabled)
            ? enabled
            : null;
        CurrentJobReady = currentJobAutoRotationReady.HasFunction && TryOptionalCall(nameof(IsCurrentJobAutoRotationReady), () => currentJobAutoRotationReady.InvokeFunc(), out var ready)
            ? ready
            : null;

        TrySubscribeEvents();
        ActionEventsAvailable = onActionUsed.HasAction || eventsSubscribed;
    }

    public WrathComboNextActionInfo GetInferredNextActionInfo()
    {
        var requirement = PositionalActionInference.TryInferWrathNextRequirement(LatestWeaponskillOrSpellActionId, out var inferred)
            ? inferred
            : PositionalRequirement.Unknown;
        return new WrathComboNextActionInfo(
            LatestActionId,
            GetActionName(LatestActionId),
            LatestActionUpdatedAt,
            LatestWeaponskillOrSpellActionId,
            GetActionName(LatestWeaponskillOrSpellActionId),
            LatestWeaponskillOrSpellUpdatedAt,
            requirement,
            ActionEventsAvailable);
    }

    public void Dispose()
    {
        if (!eventsSubscribed)
            return;

        TryCall("UnsubscribeOnActionUsed", () => onActionUsed.Unsubscribe(OnActionUsed));
        eventsSubscribed = false;
    }

    private bool GetAutoRotationState() => autoRotationState.InvokeFunc();

    private bool IsCurrentJobAutoRotationReady() => currentJobAutoRotationReady.InvokeFunc();

    private void TrySubscribeEvents()
    {
        if (eventsSubscribed)
            return;
        if (DateTime.UtcNow < nextEventSubscribeAttempt)
            return;

        try
        {
            onActionUsed.Subscribe(OnActionUsed);
            eventsSubscribed = true;
            ActionEventsAvailable = true;
            EventLastError = null;
        }
        catch (Exception ex)
        {
            EventLastError = $"{nameof(TrySubscribeEvents)}: {ex.Message}";
            Logger.Warning($"{nameof(WrathComboIpc)}:{nameof(TrySubscribeEvents)}", EventLastError);
            ActionEventsAvailable = false;
            nextEventSubscribeAttempt = DateTime.UtcNow.AddSeconds(5);
        }
    }

    private void OnActionUsed(ActionType actionType, uint actionId)
    {
        if (actionType != ActionType.Action)
            return;

        var now = DateTime.UtcNow;
        LatestActionId = actionId;
        LatestActionUpdatedAt = now;

        if (TryGetActionCategory(actionId, out var category) &&
            category is ActionCategoryWeaponskill or ActionCategorySpell)
        {
            LatestWeaponskillOrSpellActionId = actionId;
            LatestWeaponskillOrSpellUpdatedAt = now;
        }
    }

    private bool TryGetActionCategory(uint actionId, out uint category)
    {
        category = 0;
        try
        {
            var row = Services.Data.GetExcelSheet<LuminaAction>().GetRowOrDefault(actionId);
            if (row == null)
                return false;

            category = row.Value.ActionCategory.RowId;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private string GetActionName(uint actionId)
    {
        if (actionId == 0)
            return "none";

        try
        {
            var row = Services.Data.GetExcelSheet<LuminaAction>().GetRowOrDefault(actionId);
            return row?.Name.ToString() ?? $"#{actionId}";
        }
        catch
        {
            return $"#{actionId}";
        }
    }
}

internal sealed record WrathComboNextActionInfo(
    uint LatestActionId,
    string LatestActionName,
    DateTime LatestActionUpdatedAt,
    uint LatestWeaponskillOrSpellActionId,
    string LatestWeaponskillOrSpellActionName,
    DateTime LatestWeaponskillOrSpellUpdatedAt,
    PositionalRequirement InferredNextRequirement,
    bool EventsAvailable);
