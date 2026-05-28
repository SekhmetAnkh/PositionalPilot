using System.Numerics;

namespace PositionalPilot.Game;

internal sealed record GameSnapshot(
    bool HasPlayer,
    Vector3 PlayerPosition,
    float PlayerRotation,
    uint JobId,
    bool InCombat,
    bool IsCasting,
    bool IsManuallyMoving,
    bool HasTarget,
    ulong TargetId,
    string TargetName,
    Vector3 TargetPosition,
    float TargetRotation,
    float TargetHitboxRadius,
    bool TargetAlive,
    bool TargetTargetable);
