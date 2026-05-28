using Dalamud.Plugin.Ipc;

namespace PositionalPilot.IPC;

internal abstract class IpcAdapterBase
{
    protected readonly PluginServices Services;
    protected readonly ThrottledLogger Logger;

    protected IpcAdapterBase(PluginServices services, ThrottledLogger logger)
    {
        Services = services;
        Logger = logger;
    }

    public bool Available { get; protected set; }
    public string? LastError { get; protected set; }

    protected bool TryCall(string name, Action action)
    {
        try
        {
            action();
            LastError = null;
            Available = true;
            return true;
        }
        catch (Exception ex)
        {
            Available = false;
            LastError = $"{name}: {ex.Message}";
            Logger.Warning($"{GetType().Name}:{name}", LastError);
            return false;
        }
    }

    protected bool TryCall<T>(string name, Func<T> action, out T value)
    {
        try
        {
            value = action();
            LastError = null;
            Available = true;
            return true;
        }
        catch (Exception ex)
        {
            value = default!;
            Available = false;
            LastError = $"{name}: {ex.Message}";
            Logger.Warning($"{GetType().Name}:{name}", LastError);
            return false;
        }
    }
}
