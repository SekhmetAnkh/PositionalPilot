namespace PositionalPilot.Core.Model;

public sealed class PositionalPilotSettings
{
    public bool Enabled = false;
    public MovementMode MovementMode = MovementMode.Disabled;
    public RequiredDependencies RequiredDependencies =
        RequiredDependencies.RequireBossModSafety | RequiredDependencies.RequireVnavmesh;
    public float MaxMoveDistance = 6.0f;
    public float DesiredDistanceFromTargetHitbox = 2.2f;
    public BorderSideMode BorderSideMode = BorderSideMode.Nearest;
    public float PositionalNudgeDegrees = 30.0f;
    public int RepathCooldownMs = 500;
    public int DependencyRefreshMs = 2000;
    public int SafetyRefreshMs = 250;
    public float HoldDeadzoneYalms = 1.25f;
    public float BorderHoldDeadzoneYalms = 1.25f;
    public float PositionalCommitDeadzoneYalms = 0.35f;
    public float DestinationChangeThresholdYalms = 1.0f;
    public float StopWithinYalms = 0.35f;
    public float MeleeRangeYalms = 3.0f;
    public float EstimatedCombatMoveSpeed = 6.0f;
    public float ArrivalBufferSeconds = 0.2f;
    public float FallbackActionAheadSeconds = 0.35f;
    public bool EnableTrueNorthFallback = true;
    public CombatIntentSource CombatIntentSource = CombatIntentSource.RotationSolverReborn;
    public bool EnableRotationSolverCoordination = false;
    public int RsrNextActionMaxAgeMs = 1500;
    public int NoCastingCooldownMs = 1500;
    public float NoCastingDurationSeconds = 0.35f;
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
    public bool EnableSamMeikyoWrathAnticipation = true;
    public bool TrackSuccessfulPositionals = true;
    public PositionalStatsBook LifetimeStats = new();
}
