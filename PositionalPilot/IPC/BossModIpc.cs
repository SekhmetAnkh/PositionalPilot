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
    private readonly ICallGateSubscriber<int> forbiddenZonesCount;
    private readonly ICallGateSubscriber<float> forbiddenZonesNextActivation;
    private readonly ICallGateSubscriber<float> nextKnockbackIn;
    private readonly ICallGateSubscriber<float> nextDowntimeIn;
    private readonly ICallGateSubscriber<bool, object> pauseMovement;
    private readonly ICallGateSubscriber<bool> isNavigating;
    private readonly ICallGateSubscriber<Vector3?> naviTargetPos;
    private readonly ICallGateSubscriber<float> playerSpeed;

    public BossModIpc(PluginServices services, ThrottledLogger logger)
        : base(services, logger)
    {
        var pi = services.PluginInterface;
        recommended = pi.GetIpcSubscriber<int>("BossMod.Hints.RecommendedPositional");
        isPositionSafe = pi.GetIpcSubscriber<Vector3, bool>("BossMod.Hints.IsPositionSafe");
        isDashSafe = pi.GetIpcSubscriber<Vector3, Vector3, bool>("BossMod.Hints.IsDashSafe");
        nextDamageIn = pi.GetIpcSubscriber<float>("BossMod.Hints.NextDamageIn");
        forbiddenZonesCount = pi.GetIpcSubscriber<int>("BossMod.Hints.ForbiddenZonesCount");
        forbiddenZonesNextActivation = pi.GetIpcSubscriber<float>("BossMod.Hints.ForbiddenZonesNextActivation");
        nextKnockbackIn = pi.GetIpcSubscriber<float>("BossMod.Timeline.NextKnockbackIn");
        nextDowntimeIn = pi.GetIpcSubscriber<float>("BossMod.Timeline.NextDowntimeIn");
        pauseMovement = pi.GetIpcSubscriber<bool, object>("BossMod.AI.PauseMovement");
        isNavigating = pi.GetIpcSubscriber<bool>("BossMod.AI.IsNavigating");
        naviTargetPos = pi.GetIpcSubscriber<Vector3?>("BossMod.AI.NaviTargetPos");
        playerSpeed = pi.GetIpcSubscriber<float>("BossMod.AI.PlayerSpeed");
    }

    public override void RefreshAvailability() =>
        SetAvailability(
            "BossModReborn IPC providers not found",
            () => recommended.HasFunction,
            () => isPositionSafe.HasFunction,
            () => isDashSafe.HasFunction);

    public bool TryGetRecommendedPositional(out PositionalRequirement positional)
    {
        if (TryCall(nameof(TryGetRecommendedPositional), () => recommended.InvokeFunc(), out var raw))
        {
            positional = PositionalGeometry.MapBossModPositional(raw);
            return positional != PositionalRequirement.Unknown;
        }

        positional = PositionalRequirement.Unknown;
        return false;
    }

    public bool IsPositionSafe(Vector3 worldPos) =>
        TryCall(nameof(IsPositionSafe), () => isPositionSafe.InvokeFunc(worldPos), out var safe) && safe;

    public bool IsDashSafe(Vector3 from, Vector3 to) =>
        TryCall(nameof(IsDashSafe), () => isDashSafe.InvokeFunc(from, to), out var safe) && safe;

    public bool TryGetNextDamageIn(out float seconds) => TryCall(nameof(TryGetNextDamageIn), () => nextDamageIn.InvokeFunc(), out seconds);

    public bool TryGetForbiddenZonesCount(out int count) => TryCall(nameof(TryGetForbiddenZonesCount), () => forbiddenZonesCount.InvokeFunc(), out count);

    public bool TryGetForbiddenZonesNextActivation(out float seconds) => TryCall(nameof(TryGetForbiddenZonesNextActivation), () => forbiddenZonesNextActivation.InvokeFunc(), out seconds);

    public bool TryGetNextKnockbackIn(out float seconds) => TryCall(nameof(TryGetNextKnockbackIn), () => nextKnockbackIn.InvokeFunc(), out seconds);

    public bool TryGetNextDowntimeIn(out float seconds) => TryCall(nameof(TryGetNextDowntimeIn), () => nextDowntimeIn.InvokeFunc(), out seconds);

    public void PauseMovement(bool pause) => TryCall(nameof(PauseMovement), () => pauseMovement.InvokeAction(pause));

    public bool IsBossModNavigating() => TryCall(nameof(IsBossModNavigating), () => isNavigating.InvokeFunc(), out var navigating) && navigating;

    public bool TryGetBossModNaviTarget(out Vector3 pos)
    {
        if (TryCall(nameof(TryGetBossModNaviTarget), () => naviTargetPos.InvokeFunc(), out var nullable) && nullable.HasValue)
        {
            pos = nullable.Value;
            return true;
        }

        pos = default;
        return false;
    }

    public bool TryGetPlayerSpeed(out float speed) => TryCall(nameof(TryGetPlayerSpeed), () => playerSpeed.InvokeFunc(), out speed);
}
