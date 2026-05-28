using System.Numerics;
using PositionalPilot.Core.Model;

namespace PositionalPilot.Core.Geometry;

public static class PositionalGeometry
{
    private const float TwoPi = MathF.PI * 2f;

    public static IReadOnlyList<Candidate> GenerateCandidates(
        Vector3 playerPosition,
        TargetSnapshot target,
        PositionalRequirement requirement,
        PositionalPilotSettings settings)
    {
        if (target.HitboxRadius <= 0 || settings.CandidateCount < 4)
            return Array.Empty<Candidate>();

        var result = new List<Candidate>(settings.CandidateCount);
        var radius = target.HitboxRadius + settings.DesiredDistanceFromTargetHitbox;
        for (var i = 0; i < settings.CandidateCount; i++)
        {
            var angle = i * TwoPi / settings.CandidateCount;
            if (!AngleMatchesRequirement(angle, target.RotationRadians, requirement, out var deviation))
                continue;

            var point = target.Position + new Vector3(MathF.Sin(angle) * radius, 0, MathF.Cos(angle) * radius);
            var distance = DistanceXZ(playerPosition, point);
            if (distance > settings.MaxMoveDistance)
                continue;

            var score = distance + deviation * 1.5f;
            result.Add(new Candidate(point, requirement, distance, deviation, score));
        }

        return result.OrderBy(c => c.Score).ToArray();
    }

    public static bool AngleMatchesRequirement(
        float worldAngle,
        float targetRotation,
        PositionalRequirement requirement,
        out float deviation)
    {
        deviation = 0;
        if (requirement is PositionalRequirement.None or PositionalRequirement.Unknown)
            return false;

        if (requirement == PositionalRequirement.Any)
            return true;

        var rear = NormalizeAngle(targetRotation + MathF.PI);
        var leftFlank = NormalizeAngle(targetRotation + MathF.PI / 2f);
        var rightFlank = NormalizeAngle(targetRotation - MathF.PI / 2f);

        if (requirement == PositionalRequirement.Rear)
        {
            deviation = AbsAngleDelta(worldAngle, rear);
            return deviation <= MathF.PI / 4f;
        }

        var leftDev = AbsAngleDelta(worldAngle, leftFlank);
        var rightDev = AbsAngleDelta(worldAngle, rightFlank);
        deviation = MathF.Min(leftDev, rightDev);
        return deviation <= MathF.PI / 4f;
    }

    public static PositionalRequirement MapBossModPositional(int raw)
    {
        // BossModReborn BossMod/Data/ActionID.cs: public enum Positional { Any, Flank, Rear, Front }
        return raw switch
        {
            0 => PositionalRequirement.Any,
            1 => PositionalRequirement.Flank,
            2 => PositionalRequirement.Rear,
            3 => PositionalRequirement.Unknown,
            _ => PositionalRequirement.Unknown,
        };
    }

    public static Candidate? ApplyHysteresis(Candidate? current, IReadOnlyList<Candidate> ranked, PositionalPilotSettings settings, Func<Candidate, bool> stillSafe)
    {
        if (ranked.Count == 0)
            return current != null && stillSafe(current) ? current : null;

        var best = ranked[0];
        if (current == null || !stillSafe(current))
            return best;

        return current.Score - best.Score >= settings.MinimumImprovementYalms ? best : current;
    }

    public static float DistanceXZ(Vector3 a, Vector3 b)
    {
        var dx = a.X - b.X;
        var dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    private static float AbsAngleDelta(float a, float b)
    {
        var delta = NormalizeAngle(a - b);
        if (delta > MathF.PI)
            delta -= TwoPi;
        return MathF.Abs(delta);
    }

    private static float NormalizeAngle(float angle)
    {
        angle %= TwoPi;
        return angle < 0 ? angle + TwoPi : angle;
    }
}
