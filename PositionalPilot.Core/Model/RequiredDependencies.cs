namespace PositionalPilot.Core.Model;

[Flags]
public enum RequiredDependencies
{
    None = 0,
    RequireBossModSafety = 1 << 0,
    RequireVnavmesh = 1 << 1,
    RequireCombatSolver = 1 << 2,
}
