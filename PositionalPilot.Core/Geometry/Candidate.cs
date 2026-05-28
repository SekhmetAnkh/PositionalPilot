using System.Numerics;
using PositionalPilot.Core.Model;

namespace PositionalPilot.Core.Geometry;

public sealed record Candidate(
    Vector3 Position,
    PositionalRequirement Requirement,
    float DistanceFromPlayer,
    float AngularDeviationRadians,
    float Score);
