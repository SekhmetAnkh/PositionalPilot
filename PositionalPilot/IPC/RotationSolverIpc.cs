using Dalamud.Plugin.Ipc;
using PositionalPilot.Core.Model;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace PositionalPilot.IPC;

internal sealed class RotationSolverIpc : IpcAdapterBase, IDisposable
{
    private const byte SpecialEnd = 0;
    private const byte SpecialNoCasting = 13;

    private readonly ICallGateSubscriber<byte, object> triggerSpecial;
    private readonly ICallGateSubscriber<byte, float, object> triggerSpecialDuration;
    private readonly ICallGateSubscriber<uint, object> nextGcdActionChanged;
    private readonly ICallGateSubscriber<uint, object> nextActionChanged;
    private bool eventsSubscribed;
    private DateTime nextEventSubscribeAttempt = DateTime.MinValue;

    public RotationSolverIpc(PluginServices services, ThrottledLogger logger)
        : base(services, logger)
    {
        var pi = services.PluginInterface;
        triggerSpecial = pi.GetIpcSubscriber<byte, object>("RotationSolverReborn.TriggerSpecialState");
        triggerSpecialDuration = pi.GetIpcSubscriber<byte, float, object>("RotationSolverReborn.TriggerSpecialStateWithDuration");
        nextGcdActionChanged = pi.GetIpcSubscriber<uint, object>("RotationSolverReborn.ActionUpdater.NextGCDActionChanged");
        nextActionChanged = pi.GetIpcSubscriber<uint, object>("RotationSolverReborn.ActionUpdater.NextActionChanged");
    }

    public bool NextActionEventsAvailable { get; private set; }
    public uint LatestNextGcdActionId { get; private set; }
    public uint LatestNextActionId { get; private set; }
    public DateTime LatestNextGcdActionUpdatedAt { get; private set; } = DateTime.MinValue;
    public DateTime LatestNextActionUpdatedAt { get; private set; } = DateTime.MinValue;
    public string? EventLastError { get; private set; }

    public override void RefreshAvailability()
    {
        SetAvailability(
            "RotationSolverReborn coordination IPC providers not found",
            () => triggerSpecial.HasAction || triggerSpecialDuration.HasAction);

        TrySubscribeEvents();
        NextActionEventsAvailable = nextGcdActionChanged.HasAction || nextActionChanged.HasAction || eventsSubscribed;
    }

    public RotationSolverNextActionInfo GetNextGcdActionInfo()
    {
        var actionId = LatestNextGcdActionId;
        var requirement = PositionalActionMap.TryGetRequirement(actionId, out var mapped)
            ? mapped
            : PositionalRequirement.Unknown;
        return new RotationSolverNextActionInfo(
            actionId,
            GetActionName(actionId),
            requirement,
            LatestNextGcdActionUpdatedAt,
            LatestNextActionId,
            GetActionName(LatestNextActionId),
            LatestNextActionUpdatedAt,
            NextActionEventsAvailable);
    }

    public void TriggerSpecialState(byte special) =>
        TryCall(nameof(TriggerSpecialState), () => triggerSpecial.InvokeAction(special));

    public void TriggerSpecialStateWithDuration(byte special, float duration) =>
        TryCall(nameof(TriggerSpecialStateWithDuration), () => triggerSpecialDuration.InvokeAction(special, duration));

    public void PauseOrNoCasting(float duration) => TriggerSpecialStateWithDuration(SpecialNoCasting, duration);

    public void UnpauseOrEndSpecial() => TriggerSpecialState(SpecialEnd);

    public void Dispose()
    {
        if (!eventsSubscribed)
            return;

        TryCall("UnsubscribeNextGCDActionChanged", () => nextGcdActionChanged.Unsubscribe(OnNextGcdActionChanged));
        TryCall("UnsubscribeNextActionChanged", () => nextActionChanged.Unsubscribe(OnNextActionChanged));
        eventsSubscribed = false;
    }

    private void TrySubscribeEvents()
    {
        if (eventsSubscribed)
            return;
        if (DateTime.UtcNow < nextEventSubscribeAttempt)
            return;

        try
        {
            nextGcdActionChanged.Subscribe(OnNextGcdActionChanged);
            nextActionChanged.Subscribe(OnNextActionChanged);
            eventsSubscribed = true;
            NextActionEventsAvailable = true;
            EventLastError = null;
        }
        catch (Exception ex)
        {
            EventLastError = $"{nameof(TrySubscribeEvents)}: {ex.Message}";
            Logger.Warning($"{nameof(RotationSolverIpc)}:{nameof(TrySubscribeEvents)}", EventLastError);
            NextActionEventsAvailable = false;
            nextEventSubscribeAttempt = DateTime.UtcNow.AddSeconds(5);
        }
    }

    private void OnNextGcdActionChanged(uint actionId)
    {
        LatestNextGcdActionId = actionId;
        LatestNextGcdActionUpdatedAt = DateTime.UtcNow;
    }

    private void OnNextActionChanged(uint actionId)
    {
        LatestNextActionId = actionId;
        LatestNextActionUpdatedAt = DateTime.UtcNow;
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

internal sealed record RotationSolverNextActionInfo(
    uint NextGcdActionId,
    string NextGcdActionName,
    PositionalRequirement NextGcdRequirement,
    DateTime NextGcdUpdatedAt,
    uint NextActionId,
    string NextActionName,
    DateTime NextActionUpdatedAt,
    bool EventsAvailable);
