using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using System.Numerics;
using PositionalPilot.Core.Geometry;

namespace PositionalPilot.Game;

internal sealed class GameStateReader
{
    private readonly PluginServices services;
    private Vector3 lastPlayerPosition;
    private DateTime lastPositionSample = DateTime.MinValue;

    private static readonly HashSet<uint> MeleeJobs = new()
    {
        2, 4, 20, 22, 29, 30, 34, 39,
    };

    public GameStateReader(PluginServices services) => this.services = services;

    public GameSnapshot Read()
    {
        var player = services.ClientState.LocalPlayer;
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
                default,
                0,
                0,
                false,
                false);
        }

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
            target.Position,
            target.Rotation,
            target.HitboxRadius,
            target.CurrentHp > 0,
            target.IsTargetable);
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
        default,
        0,
        0,
        false,
        false);
}
