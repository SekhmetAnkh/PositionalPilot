using FFXIVClientStructs.FFXIV.Client.Game;

namespace PositionalPilot.Game;

internal sealed unsafe class TrueNorthAction
{
    private const uint TrueNorthActionId = 7546;

    private readonly PluginServices services;

    public TrueNorthAction(PluginServices services) => this.services = services;

    public bool TryUse()
    {
        try
        {
            var actionManager = ActionManager.Instance();
            if (actionManager == null ||
                actionManager->AnimationLock > 0 ||
                actionManager->GetActionStatus(ActionType.Action, TrueNorthActionId) != 0)
                return false;

            return actionManager->UseAction(ActionType.Action, TrueNorthActionId);
        }
        catch (Exception ex)
        {
            services.Log.Debug(ex, "Failed to use True North");
            return false;
        }
    }
}
