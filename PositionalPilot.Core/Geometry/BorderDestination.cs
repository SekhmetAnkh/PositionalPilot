using System.Numerics;
using PositionalPilot.Core.Model;

namespace PositionalPilot.Core.Geometry;

public sealed record BorderDestination(
    Vector3 Position,
    BorderSide Side,
    PositionalRequirement Requirement,
    float DistanceFromPlayer,
    float AngularDeviationRadians,
    float Score);
