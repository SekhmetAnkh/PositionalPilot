using System.Numerics;
using Dalamud.Plugin.Ipc;
using PositionalPilot.Core.Geometry;
using PositionalPilot.Core.Model;

namespace PositionalPilot.IPC;

internal sealed class BossModIpc : IpcAdapterBase
{
    private readonly ICallGateSubscriber<int> recommended;
    private readonly ICallGateSubscriber<Vector3, bool> isPositionSafe;
    private readonly ICallGateSubscriber<Vector3, Vector3, bool> isDashSafe;
    private readonly ICallGateSubscriber<float> nextDamageIn;
    private readonly ICallGateSubscriber<float> nextKnockbackIn;
    private readonly ICallGateSubscriber<float> nextDowntimeIn;
    private readonly ICallGateSubscriber<bool> isNavigating;
    private readonly ICallGateSubscriber<Vector3?> naviTargetPos;

    public BossModIpc(PluginServices services, ThrottledLogger logger)
        : base(services, logger)
    {
        var pi = services.PluginInterface;
        recommended = pi.GetIpcSubscriber<int>("BossMod.Hints.RecommendedPositional");
        isPositionSafe = pi.GetIpcSubscriber<Vector3, bool>("BossMod.Hints.IsPositionSafe");
        isDashSafe = pi.GetIpcSubscriber<Vector3, Vector3, bool>("BossMod.Hints.IsDashSafe");
        nextDamageIn = pi.GetIpcSubscriber<float>("BossMod.Hints.NextDamageIn");
        nextKnockbackIn = pi.GetIpcSubscriber<float>("BossMod.Timeline.NextKnockbackIn");
        nextDowntimeIn = pi.GetIpcSubscriber<float>("BossMod.Timeline.NextDowntimeIn");
        isNavigating = pi.GetIpcSubscriber<bool>("BossMod.AI.IsNavigating");
        naviTargetPos = pi.GetIpcSubscriber<Vector3?>("BossMod.AI.NaviTargetPos");
    }

    public override void RefreshAvailability() =>
        SetAvailability(
            "BossModReborn safety IPC providers not found",
            () => isPositionSafe.HasFunction,
            () => isDashSafe.HasFunction);

    public bool TryGetRecommendedPositional(out PositionalRequirement positional)
    {
        if (TryOptionalCall(nameof(TryGetRecommendedPositional), () => recommended.InvokeFunc(), out var raw))
        {
            positional = PositionalGeometry.MapBossModPositional(raw);
            return positional is not PositionalRequirement.Unknown;
        }

        positional = PositionalRequirement.Unknown;
        return false;
    }

    public bool IsPositionSafe(Vector3 worldPos) =>
        TryCall(nameof(IsPositionSafe), () => isPositionSafe.InvokeFunc(worldPos), out var safe) && safe;

    public bool IsDashSafe(Vector3 from, Vector3 to) =>
        TryCall(nameof(IsDashSafe), () => isDashSafe.InvokeFunc(from, to), out var safe) && safe;

    public bool TryGetNextDamageIn(out float seconds) => TryOptionalCall(nameof(TryGetNextDamageIn), () => nextDamageIn.InvokeFunc(), out seconds);

    public bool TryGetNextKnockbackIn(out float seconds) => TryOptionalCall(nameof(TryGetNextKnockbackIn), () => nextKnockbackIn.InvokeFunc(), out seconds);

    public bool TryGetNextDowntimeIn(out float seconds) => TryOptionalCall(nameof(TryGetNextDowntimeIn), () => nextDowntimeIn.InvokeFunc(), out seconds);

    public bool IsBossModNavigating() => TryOptionalCall(nameof(IsBossModNavigating), () => isNavigating.InvokeFunc(), out var navigating) && navigating;

    public bool TryGetBossModNaviTarget(out Vector3 pos)
    {
        if (TryOptionalCall(nameof(TryGetBossModNaviTarget), () => naviTargetPos.InvokeFunc(), out var nullable) && nullable.HasValue)
        {
            pos = nullable.Value;
            return true;
        }

        pos = default;
        return false;
    }
}
