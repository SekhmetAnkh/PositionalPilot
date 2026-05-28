namespace PositionalPilot.Core.Model;

public sealed class PositionalPilotSettings
{
    public bool Enabled = false;
    public MovementMode MovementMode = MovementMode.Disabled;
    public RequiredDependencies RequiredDependencies =
        RequiredDependencies.RequireBossModSafety | RequiredDependencies.RequireVnavmesh;
    public float MaxMoveDistance = 6.0f;
    public float DesiredDistanceFromTargetHitbox = 2.2f;
    public float CandidateRingExtraDistance = 1.5f;
    public int CandidateCount = 24;
    public int RepathCooldownMs = 500;
    public float MinimumImprovementYalms = 0.75f;
    public float StopWithinYalms = 0.35f;
    public bool DisableDuringCasting = true;
    public bool DisableDuringManualMovement = true;
    public bool DisableDuringUpcomingDamage = true;
    public float UpcomingDamageBlockSeconds = 1.5f;
    public bool DisableDuringUpcomingKnockback = true;
    public float UpcomingKnockbackBlockSeconds = 4.0f;
    public bool DisableDuringDowntime = true;
    public bool OnlyInCombat = true;
    public bool OnlyMeleeJobs = true;
    public bool ShowOverlay = true;
    public bool DebugLogging = false;
}
