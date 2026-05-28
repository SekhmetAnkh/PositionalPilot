using System.Numerics;

namespace PositionalPilot.Core.Geometry;

public sealed record TargetSnapshot(
    Vector3 Position,
    float RotationRadians,
    float HitboxRadius);
