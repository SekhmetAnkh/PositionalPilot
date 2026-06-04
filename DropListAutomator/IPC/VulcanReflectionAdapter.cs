using System.Reflection;

namespace DropListAutomator.IPC;

internal sealed record VulcanExecutionPlanSnapshot(
    int ListId,
    string ListName,
    int Version,
    IReadOnlyDictionary<uint, int> Materials)
{
    public string Signature
    {
        get
        {
            var materialHash = string.Join(';', Materials.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}:{pair.Value}"));
            return $"{ListId}:{Version}:{materialHash}";
        }
    }
}

internal sealed class VulcanReflectionAdapter(PluginServices services)
{
    private Type? bridgeType;
    private MethodInfo? getActiveExecutionPlan;
    private FieldInfo? queueProcessorField;
    private string? lastError;

    public bool Available { get; private set; }
    public string? LastError => lastError;

    public VulcanExecutionPlanSnapshot? GetActiveExecutionPlan()
    {
        if (!EnsureBindings())
            return null;

        try
        {
            var plan = getActiveExecutionPlan!.Invoke(null, null);
            return plan == null ? null : BuildSnapshot(plan);
        }
        catch (Exception ex)
        {
            Available = false;
            lastError = $"Failed to read Vulcan execution plan: {ex.GetBaseException().Message}";
            services.Log.Warning(ex, "Failed to read Vulcan execution plan through reflection.");
            return null;
        }
    }

    public bool PauseQueue(string reason)
    {
        var queueProcessor = GetQueueProcessor();
        if (queueProcessor == null)
            return false;

        try
        {
            queueProcessor.GetType().GetMethod("Pause", [typeof(string)])?.Invoke(queueProcessor, [reason]);
            return true;
        }
        catch (Exception ex)
        {
            lastError = $"Failed to pause Vulcan queue: {ex.GetBaseException().Message}";
            services.Log.Warning(ex, "Failed to pause Vulcan queue through reflection.");
            return false;
        }
    }

    public bool ResumeQueue()
    {
        var queueProcessor = GetQueueProcessor();
        if (queueProcessor == null)
            return false;

        try
        {
            queueProcessor.GetType().GetMethod("Resume", Type.EmptyTypes)?.Invoke(queueProcessor, null);
            return true;
        }
        catch (Exception ex)
        {
            lastError = $"Failed to resume Vulcan queue: {ex.GetBaseException().Message}";
            services.Log.Warning(ex, "Failed to resume Vulcan queue through reflection.");
            return false;
        }
    }

    private object? GetQueueProcessor()
    {
        if (!EnsureBindings())
            return null;

        try
        {
            return queueProcessorField!.GetValue(null);
        }
        catch (Exception ex)
        {
            lastError = $"Failed to access Vulcan queue processor: {ex.GetBaseException().Message}";
            services.Log.Warning(ex, "Failed to access Vulcan queue processor through reflection.");
            return null;
        }
    }

    private bool EnsureBindings()
    {
        if (bridgeType != null)
            return true;

        var assembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, "GatherBuddy", StringComparison.OrdinalIgnoreCase));
        if (assembly == null)
        {
            Available = false;
            lastError = "GatherBuddy assembly not loaded.";
            return false;
        }

        bridgeType = assembly.GetType("GatherBuddy.Crafting.CraftingGatherBridge");
        getActiveExecutionPlan = bridgeType?.GetMethod("GetActiveExecutionPlan", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes);
        queueProcessorField = bridgeType?.GetField("_queueProcessor", BindingFlags.NonPublic | BindingFlags.Static);

        Available = bridgeType != null && getActiveExecutionPlan != null && queueProcessorField != null;
        lastError = Available ? null : "GatherBuddy Vulcan internals were not found.";
        return Available;
    }

    private static VulcanExecutionPlanSnapshot BuildSnapshot(object plan)
    {
        var type = plan.GetType();
        var listId = (int)(type.GetProperty("ListId")?.GetValue(plan) ?? -1);
        var listName = (string?)type.GetProperty("ListName")?.GetValue(plan) ?? "Vulcan Plan";
        var version = (int)(type.GetProperty("Version")?.GetValue(plan) ?? 0);
        var materialsObject = type.GetProperty("MaterialsView")?.GetValue(plan);
        var materials = new Dictionary<uint, int>();

        if (materialsObject is System.Collections.IEnumerable enumerable)
        {
            foreach (var entry in enumerable)
            {
                var entryType = entry.GetType();
                var key = entryType.GetProperty("Key")?.GetValue(entry);
                var value = entryType.GetProperty("Value")?.GetValue(entry);
                if (key is uint itemId && value is int quantity && quantity > 0)
                    materials[itemId] = quantity;
            }
        }

        return new VulcanExecutionPlanSnapshot(listId, listName, version, materials);
    }
}
