using Dalamud.Plugin.Ipc;

namespace PositionalPilot.IPC;

internal sealed class AvariceIpc : IpcAdapterBase
{
    private readonly ICallGateSubscriber<IntPtr, int> cardinalDirection;

    public AvariceIpc(PluginServices services, ThrottledLogger logger)
        : base(services, logger)
    {
        cardinalDirection = services.PluginInterface.GetIpcSubscriber<IntPtr, int>("Avarice.CardinalDirection");
    }

    public override void RefreshAvailability() =>
        SetAvailability("Avarice CardinalDirection IPC provider not found", () => cardinalDirection.HasFunction);

    public bool TryGetCardinalDirection(IntPtr gameObjectAddress, out int direction) =>
        TryCall(nameof(TryGetCardinalDirection), () => cardinalDirection.InvokeFunc(gameObjectAddress), out direction);
}
