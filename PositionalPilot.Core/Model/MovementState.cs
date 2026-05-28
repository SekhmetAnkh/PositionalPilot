namespace PositionalPilot.Core.Model;

public enum MovementState
{
    Idle,
    Evaluating,
    Moving,
    Cooldown,
    Blocked,
    EmergencyStopped,
}
