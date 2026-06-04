using System.Text.RegularExpressions;

namespace DropListAutomator.IPC;

internal sealed partial class CommandBridge(PluginServices services)
{
    public void TeleporterTeleport(string destination, string commandTemplate)
    {
        var safe = Sanitize(destination);
        if (safe.Length == 0)
            return;

        var template = string.IsNullOrWhiteSpace(commandTemplate) ? "/tp {0}" : commandTemplate.Trim();
        services.Commands.ProcessCommand(string.Format(template, safe));
    }

    public void StartVulcanCraft(string itemName, int quantity)
    {
        var safe = Sanitize(itemName);
        if (safe.Length == 0 || quantity <= 0)
            return;

        services.Commands.ProcessCommand($"/vulcan craft {safe} {quantity}");
    }

    public void StartGbrGather(string itemName)
    {
        var safe = Sanitize(itemName);
        if (safe.Length == 0)
            return;

        services.Commands.ProcessCommand($"/gather {safe}");
    }

    private static string Sanitize(string text) => UnsafeCommandChars().Replace(text.Trim(), string.Empty);

    [GeneratedRegex("[\\r\\n]")]
    private static partial Regex UnsafeCommandChars();
}
