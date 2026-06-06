using System.Numerics;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using PositionalPilot.Core.Model;

namespace PositionalPilot.Game;

internal sealed unsafe class PositionalActionEffectTracker : IDisposable
{
    private const string ReceiveActionEffectSignature = "E8 ?? ?? ?? ?? 48 8B 8D ?? ?? ?? ?? 48 33 CC E8 ?? ?? ?? ?? 48 81 C4 00 05 00 00";
    private const byte ActionTypeAction = 1;
    private const byte EffectTypeDamage = 3;

    private readonly PluginServices services;
    private readonly Configuration config;
    private readonly PositionalStatsService stats;
    private readonly ThrottledLogger logger;
    private readonly Hook<ReceiveActionEffectDelegate>? hook;

    private delegate void ReceiveActionEffectDelegate(
        uint casterEntityId,
        Character* casterPtr,
        Vector3* targetPos,
        ActionEffectHandler.Header* header,
        ActionEffectHandler.TargetEffects* effects,
        GameObjectId* targetEntityIds);

    public PositionalActionEffectTracker(PluginServices services, Configuration config, PositionalStatsService stats, ThrottledLogger logger)
    {
        this.services = services;
        this.config = config;
        this.stats = stats;
        this.logger = logger;

        try
        {
            hook = services.GameInterop.HookFromSignature<ReceiveActionEffectDelegate>(
                ReceiveActionEffectSignature,
                ReceiveActionEffectDetour);
            hook.Enable();
        }
        catch (Exception ex)
        {
            services.Log.Warning(ex, "Failed to hook action effect positional tracker");
        }
    }

    public bool Available => hook != null;
    public string LastEvent { get; private set; } = "not observed";

    public void Dispose() => hook?.Dispose();

    private void ReceiveActionEffectDetour(
        uint casterEntityId,
        Character* casterPtr,
        Vector3* targetPos,
        ActionEffectHandler.Header* header,
        ActionEffectHandler.TargetEffects* effects,
        GameObjectId* targetEntityIds)
    {
        hook!.Original(casterEntityId, casterPtr, targetPos, header, effects, targetEntityIds);

        try
        {
            EvaluateActionEffect(casterEntityId, header, effects);
        }
        catch (Exception ex)
        {
            logger.Debug(config, "positional-stat-error", ex.Message);
        }
    }

    private void EvaluateActionEffect(uint casterEntityId, ActionEffectHandler.Header* header, ActionEffectHandler.TargetEffects* effects)
    {
        var player = services.Objects.LocalPlayer;
        if (player == null || casterEntityId != player.EntityId || header == null || effects == null)
            return;

        if (header->ActionType != ActionTypeAction || !PositionalEffectPotencyMap.IsTrackedPositionalAction(header->ActionId))
            return;

        for (var targetIndex = 0; targetIndex < header->NumTargets; targetIndex++)
        {
            var targetEffects = effects[targetIndex].Effects;
            for (var effectIndex = 0; effectIndex < targetEffects.Length; effectIndex++)
            {
                var effect = targetEffects[effectIndex];
                if (effect.Type == EffectTypeDamage &&
                    PositionalEffectPotencyMap.IsSuccessfulPositionalHit(header->ActionId, effect.Param2))
                {
                    stats.RecordSuccess(player.ClassJob.RowId);
                    LastEvent = $"{header->ActionId} success ({effect.Param2})";
                    return;
                }
            }
        }

        LastEvent = $"{header->ActionId} no positional hit";
    }
}
