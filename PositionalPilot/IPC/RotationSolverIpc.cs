using Dalamud.Plugin.Ipc;

namespace PositionalPilot.IPC;

internal sealed class RotationSolverIpc : IpcAdapterBase
{
    private const byte SpecialEnd = 0;
    private const byte SpecialNoCasting = 13;

    private readonly ICallGateSubscriber<byte, object> triggerSpecial;
    private readonly ICallGateSubscriber<byte, float, object> triggerSpecialDuration;

    public RotationSolverIpc(PluginServices services, ThrottledLogger logger)
        : base(services, logger)
    {
        var pi = services.PluginInterface;
        triggerSpecial = pi.GetIpcSubscriber<byte, object>("RotationSolverReborn.TriggerSpecialState");
        triggerSpecialDuration = pi.GetIpcSubscriber<byte, float, object>("RotationSolverReborn.TriggerSpecialStateWithDuration");
    }

    public override void RefreshAvailability() =>
        SetAvailability(
            "RotationSolverReborn coordination IPC providers not found",
            () => triggerSpecial.HasAction || triggerSpecialDuration.HasAction);

    public void TriggerSpecialState(byte special) =>
        TryCall(nameof(TriggerSpecialState), () => triggerSpecial.InvokeAction(special));

    public void TriggerSpecialStateWithDuration(byte special, float duration) =>
        TryCall(nameof(TriggerSpecialStateWithDuration), () => triggerSpecialDuration.InvokeAction(special, duration));

    public void PauseOrNoCasting(float duration) => TriggerSpecialStateWithDuration(SpecialNoCasting, duration);

    public void UnpauseOrEndSpecial() => TriggerSpecialState(SpecialEnd);
}
