using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Plugin.Ipc;

namespace PositionalPilot.IPC;

internal sealed class VnavmeshIpc : IpcAdapterBase
{
    private readonly ICallGateSubscriber<Vector3, bool, Task<bool>> moveTo;
    private readonly ICallGateSubscriber<Vector3, bool, float, Task<bool>> moveCloseTo;
    private readonly ICallGateSubscriber<object> stop;
    private readonly ICallGateSubscriber<bool> isRunning;
    private readonly ICallGateSubscriber<bool> navReady;

    public VnavmeshIpc(PluginServices services, ThrottledLogger logger)
        : base(services, logger)
    {
        var pi = services.PluginInterface;
        moveTo = pi.GetIpcSubscriber<Vector3, bool, Task<bool>>("vnavmesh.SimpleMove.PathfindAndMoveTo");
        moveCloseTo = pi.GetIpcSubscriber<Vector3, bool, float, Task<bool>>("vnavmesh.SimpleMove.PathfindAndMoveCloseTo");
        stop = pi.GetIpcSubscriber<object>("vnavmesh.Path.Stop");
        isRunning = pi.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning");
        navReady = pi.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");
    }

    public bool IsReady() => TryCall(nameof(IsReady), () => navReady.InvokeFunc(), out var ready) && ready;

    public bool IsNavigating() => TryCall(nameof(IsNavigating), () => isRunning.InvokeFunc(), out var running) && running;

    public bool TryPathfindAndMoveTo(Vector3 destination, out Task<bool>? task)
    {
        if (!TryCall(nameof(TryPathfindAndMoveTo), () => moveTo.InvokeFunc(destination, false), out task))
        {
            task = null;
            return false;
        }

        return true;
    }

    public bool TryPathfindAndMoveCloseTo(Vector3 destination, float tolerance, out Task<bool>? task)
    {
        if (!TryCall(nameof(TryPathfindAndMoveCloseTo), () => moveCloseTo.InvokeFunc(destination, false, tolerance), out task))
        {
            task = null;
            return false;
        }

        return true;
    }

    public void Stop() => TryCall(nameof(Stop), () => stop.InvokeAction());
}
