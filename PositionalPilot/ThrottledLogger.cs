namespace PositionalPilot;

internal sealed class ThrottledLogger
{
    private readonly PluginServices services;
    private readonly Dictionary<string, DateTime> lastLog = new();

    public ThrottledLogger(PluginServices services) => this.services = services;

    public void Debug(Configuration config, string key, string message, TimeSpan? interval = null)
    {
        if (!config.Settings.DebugLogging)
            return;

        if (!ShouldLog(key, interval ?? TimeSpan.FromSeconds(5)))
            return;

        services.Log.Debug(message);
    }

    public void Warning(string key, string message, TimeSpan? interval = null)
    {
        if (!ShouldLog(key, interval ?? TimeSpan.FromSeconds(15)))
            return;

        services.Log.Warning(message);
    }

    private bool ShouldLog(string key, TimeSpan interval)
    {
        var now = DateTime.UtcNow;
        if (lastLog.TryGetValue(key, out var last) && now - last < interval)
            return false;

        lastLog[key] = now;
        return true;
    }
}
