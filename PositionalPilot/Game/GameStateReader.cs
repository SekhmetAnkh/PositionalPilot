using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using PositionalPilot.Core.Geometry;

namespace PositionalPilot.Game;

internal sealed unsafe class GameStateReader
{
    private const uint TrueNorthActionId = 7546;
    private readonly PluginServices services;
    private Vector3 lastPlayerPosition;
    private DateTime lastPositionSample = DateTime.MinValue;

    private static readonly HashSet<uint> MeleeJobs = new()
    {
        2, 4, 20, 22, 29, 30, 34, 39, 41,
    };

    public GameStateReader(PluginServices services) => this.services = services;

    public GameSnapshot Read()
    {
        var player = services.Objects.LocalPlayer;
        var target = services.Targets.Target as IBattleChara;
        var now = DateTime.UtcNow;

        if (player == null)
            return Empty(false);

        var playerPos = player.Position;
        var moved = false;
        if (lastPositionSample != DateTime.MinValue)
            moved = PositionalGeometry.DistanceXZ(lastPlayerPosition, playerPos) > 0.03f;

        lastPositionSample = now;
        lastPlayerPosition = playerPos;

        if (target == null)
        {
            return new GameSnapshot(
                true,
                playerPos,
                player.Rotation,
                player.ClassJob.RowId,
                services.Condition[ConditionFlag.InCombat],
                services.Condition[ConditionFlag.Casting],
                moved,
                false,
                0,
                string.Empty,
                0,
                0,
                default,
                0,
                0,
                null,
                false,
                false,
                false,
                IsTrueNorthAvailable());
        }

        var targetBaseId = target.BaseId;
        var targetDataId = target.DataId;
        return new GameSnapshot(
            true,
            playerPos,
            player.Rotation,
            player.ClassJob.RowId,
            services.Condition[ConditionFlag.InCombat],
            services.Condition[ConditionFlag.Casting],
            moved,
            true,
            target.GameObjectId,
            target.Name.ToString(),
            targetBaseId,
            targetDataId,
            target.Position,
            target.Rotation,
            target.HitboxRadius,
            TryGetTargetOmnidirectional(targetBaseId),
            IsTargetTargetingPlayer(target, player.GameObjectId),
            target.CurrentHp > 0,
            target.IsTargetable,
            IsTrueNorthAvailable());
    }

    public static bool IsMeleeJob(uint jobId) => MeleeJobs.Contains(jobId);

    private static GameSnapshot Empty(bool hasPlayer) => new(
        hasPlayer,
        default,
        0,
        0,
        false,
        false,
        false,
        false,
        0,
        string.Empty,
        0,
        0,
        default,
        0,
        0,
        null,
        false,
        false,
        false,
        false);

    private bool IsTrueNorthAvailable()
    {
        try
        {
            var actionManager = ActionManager.Instance();
            return actionManager != null &&
                   actionManager->GetActionStatus(ActionType.Action, TrueNorthActionId) == 0;
        }
        catch (Exception ex)
        {
            services.Log.Debug(ex, "Failed to read True North availability");
            return false;
        }
    }

    private bool? TryGetTargetOmnidirectional(uint targetBaseId)
    {
        if (targetBaseId == 0)
            return null;

        try
        {
            var sheet = services.Data.GetExcelSheet<BNpcBase>();
            if (sheet == null)
                return null;

            var row = sheet.GetRowOrDefault(targetBaseId);
            return row?.IsOmnidirectional;
        }
        catch (Exception ex)
        {
            services.Log.Debug(ex, "Failed to read BNpcBase omnidirectional flag for {BaseId}", targetBaseId);
            return null;
        }
    }

    private bool IsTargetTargetingPlayer(IBattleChara target, ulong playerObjectId)
    {
        try
        {
            return target.TargetObjectId == playerObjectId;
        }
        catch (Exception ex)
        {
            services.Log.Debug(ex, "Failed to read target-of-target for {TargetName}", target.Name.ToString());
            return true;
        }
    }
}
