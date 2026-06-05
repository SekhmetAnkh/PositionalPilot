using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.JobGauge.Types;
using Dalamud.Game.ClientState.Objects.Types;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using PositionalPilot.Core.Geometry;
using PositionalPilot.Core.Model;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace PositionalPilot.Game;

internal sealed unsafe class GameStateReader
{
    private const uint TrueNorthActionId = 7546;
    private const uint ActionCategorySpell = 2;
    private const uint ActionCategoryWeaponskill = 3;
    private static readonly uint[] PredictorActionIds =
    {
        56, 66, 2255, 2258, 3563, 7481, 7482, 34620, 34621, 34622,
        3554, 3556, 88, 25772, 36958,
    };
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
                IsTrueNorthAvailable())
            {
                WrathPredictionSnapshot = ReadWrathPredictionSnapshot(player, null, now),
            };
        }

        var targetBaseId = target.BaseId;
        var targetDataId = target.DataId;
        var targetName = target.Name.ToString();
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
            targetName,
            targetBaseId,
            targetDataId,
            target.Position,
            target.Rotation,
            target.HitboxRadius,
            TryGetTargetOmnidirectional(targetBaseId),
            IsTargetTargetingPlayer(target, player.GameObjectId),
            target.CurrentHp > 0,
            target.IsTargetable,
            IsTrueNorthAvailable())
        {
            TargetIsTrainingDummy = IsTrainingDummy(targetName),
            WrathPredictionSnapshot = ReadWrathPredictionSnapshot(player, target, now),
        };
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

    private WrathLocalPredictionSnapshot ReadWrathPredictionSnapshot(ICharacter player, IBattleChara? target, DateTime now)
    {
        IReadOnlyDictionary<uint, float> playerStatusTimes;
        IReadOnlyCollection<uint> playerStatuses;
        if (player is IBattleChara playerBattle)
        {
            playerStatuses = ReadStatuses(playerBattle, out playerStatusTimes);
        }
        else
        {
            playerStatuses = Array.Empty<uint>();
            playerStatusTimes = new Dictionary<uint, float>();
        }
        var targetStatuses = target == null ? Array.Empty<uint>() : ReadStatuses(target, out _);

        return new WrathLocalPredictionSnapshot
        {
            JobId = player.ClassJob.RowId,
            PlayerLevel = player.Level,
            ComboActionId = ReadComboAction(),
            PlayerStatusIds = playerStatuses,
            PlayerStatusRemainingSeconds = playerStatusTimes,
            TargetStatusIds = targetStatuses,
            ActionReadyIds = ReadReadyActions(player.Level),
            MonkCoeurlFury = TryReadGauge<MNKGauge, int>(g => g.CoeurlFury),
            NinjaKazematoi = TryReadGauge<NINGauge, int>(g => g.Kazematoi),
            SamuraiHasGetsu = TryReadGauge<SAMGauge, bool>(g => g.HasGetsu),
            SamuraiHasKa = TryReadGauge<SAMGauge, bool>(g => g.HasKa),
            ViperDreadCombo = TryReadGauge<VPRGauge, uint>(g => Convert.ToUInt32(g.DreadCombo)) ?? 0,
            Now = now,
        };
    }

    private uint ReadComboAction()
    {
        try
        {
            var actionManager = ActionManager.Instance();
            return actionManager == null ? 0 : actionManager->Combo.Action;
        }
        catch (Exception ex)
        {
            services.Log.Debug(ex, "Failed to read combo action");
            return 0;
        }
    }

    private IReadOnlyCollection<uint> ReadStatuses(IBattleChara chara, out IReadOnlyDictionary<uint, float> remainingTimes)
    {
        var statuses = new HashSet<uint>();
        var times = new Dictionary<uint, float>();
        try
        {
            foreach (var status in chara.StatusList)
            {
                if (status.StatusId == 0)
                    continue;

                statuses.Add(status.StatusId);
                times[status.StatusId] = MathF.Max(times.TryGetValue(status.StatusId, out var existing) ? existing : 0, status.RemainingTime);
            }
        }
        catch (Exception ex)
        {
            services.Log.Debug(ex, "Failed to read statuses for {Name}", chara.Name.ToString());
        }

        remainingTimes = times;
        return statuses;
    }

    private IReadOnlyCollection<uint> ReadReadyActions(byte playerLevel)
    {
        var ready = new HashSet<uint>();
        try
        {
            var sheet = services.Data.GetExcelSheet<LuminaAction>();
            foreach (var actionId in PredictorActionIds)
            {
                var row = sheet.GetRowOrDefault(actionId);
                if (row == null || row.Value.ClassJobLevel > playerLevel)
                    continue;

                var category = row.Value.ActionCategory.RowId;
                if (category is not (ActionCategoryWeaponskill or ActionCategorySpell))
                    continue;

                if (ActionManager.Instance()->GetActionStatus(ActionType.Action, actionId) == 0)
                    ready.Add(actionId);
            }
        }
        catch (Exception ex)
        {
            services.Log.Debug(ex, "Failed to read ready action set");
        }

        return ready;
    }

    private TOut? TryReadGauge<TGauge, TOut>(Func<TGauge, TOut> read)
        where TGauge : JobGaugeBase
        where TOut : struct
    {
        try
        {
            return read(services.JobGauges.Get<TGauge>());
        }
        catch (Exception ex)
        {
            services.Log.Debug(ex, "Failed to read {GaugeType}", typeof(TGauge).Name);
            return null;
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

    private bool? IsTargetTargetingPlayer(IBattleChara target, ulong playerObjectId)
    {
        try
        {
            return target.TargetObjectId == playerObjectId;
        }
        catch (Exception ex)
        {
            services.Log.Debug(ex, "Failed to read target-of-target for {TargetName}", target.Name.ToString());
            return null;
        }
    }

    private static bool IsTrainingDummy(string targetName) =>
        targetName.Contains("dummy", StringComparison.OrdinalIgnoreCase);
}
