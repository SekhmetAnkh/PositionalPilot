# PositionalPilot

PositionalPilot is a Dalamud plugin scaffold for assistive melee positional movement in FFXIV. When explicitly enabled, it can suggest or request movement toward a safe rear/flank location around the current target.

The plugin is off by default. It has no stealth, hiding, anti-detection, ban-evasion, or ToS-bypass behavior.

## Dependencies

- BossModReborn: required by default for recommended positional and safety checks.
- vnavmesh: required by default for movement.
- RotationSolverReborn / CombatReborn: optional by default; used only for coordination through special-state IPC.
- Avarice: optional/reference-only. Source inspection found `Avarice.CardinalDirection` but no rear/flank/range movement IPC, so PositionalPilot uses local geometry.

## Verified IPC

BossModReborn:

- `BossMod.Hints.RecommendedPositional` -> `int`
- `BossMod.Hints.IsPositionSafe` -> `Vector3 to => bool`
- `BossMod.Hints.IsDashSafe` -> `Vector3 from, Vector3 to => bool`
- `BossMod.Hints.ForbiddenZonesCount` -> `int`
- `BossMod.Hints.ForbiddenZonesNextActivation` -> `float`
- `BossMod.Hints.NextDamageIn` -> `float`
- `BossMod.Timeline.NextKnockbackIn` -> `float`
- `BossMod.Timeline.NextDowntimeIn` -> `float`
- `BossMod.AI.PauseMovement` -> `bool pause`
- `BossMod.AI.IsNavigating` -> `bool`
- `BossMod.AI.NaviTargetPos` -> `Vector3?`
- `BossMod.AI.PlayerSpeed` -> `float`

BossMod positional enum mapping was verified as `Any=0`, `Flank=1`, `Rear=2`, `Front=3`. PositionalPilot maps `Front` to `Unknown` and will not move for it.

vnavmesh:

- `vnavmesh.Nav.IsReady` -> `bool`
- `vnavmesh.Nav.Pathfind` -> `Vector3 from, Vector3 to, bool fly => List<Vector3>?`
- `vnavmesh.Nav.PathfindWithTolerance` -> `Vector3 from, Vector3 to, bool fly, float range => List<Vector3>?`
- `vnavmesh.Path.MoveTo` -> `List<Vector3> waypoints, bool fly`
- `vnavmesh.Path.Stop`
- `vnavmesh.Path.IsRunning` -> `bool`
- `vnavmesh.SimpleMove.PathfindAndMoveTo` -> `Vector3 dest, bool fly => Task<bool>`
- `vnavmesh.SimpleMove.PathfindAndMoveCloseTo` -> `Vector3 dest, bool fly, float range => Task<bool>`

RotationSolverReborn:

- `RotationSolverReborn.TriggerSpecialState` -> `SpecialCommandType`
- `RotationSolverReborn.TriggerSpecialStateWithDuration` -> `SpecialCommandType, float`
- `RotationSolverReborn.ChangeOperatingMode` -> `StateCommandType`
- `RotationSolverReborn.ActionCommand` -> `string action, float time`

No query-style IPC for next action, next positional, current rotation state, GCD prediction, or target selection was found.

Avarice:

- `Avarice.CardinalDirection` -> `IntPtr gameObjectAddress => CardinalDirection`

No useful rear/flank/range IPC was found.

## Commands

- `/ppilot`: open the configuration window.
- `/ppilot on`: enable assist movement.
- `/ppilot off`: disable and stop movement.
- `/ppilot stop`: emergency stop, disables plugin and stops vnavmesh.
- `/ppilot suggest`: toggle SuggestOnly mode.
- `/ppilot status`: print dependency, target, candidate, and block status.
- `/ppilot debug`: toggle throttled debug logging.

## Safety Philosophy

PositionalPilot prefers doing nothing over unsafe movement. Assist movement requires explicit enablement, stops immediately when a safety gate fails, and degrades gracefully when dependencies are missing or IPC calls fail.

## Build

Open `PositionalPilot.sln` with a Dalamud API 15 development environment. The pure geometry tests target `net6.0`; the plugin project targets `net9.0-windows` and expects Dalamud dev assemblies under `%APPDATA%\XIVLauncher\addon\Hooks\dev\`.

## Manual Test Steps

1. Load Dalamud with BossModReborn, vnavmesh, and optionally RotationSolverReborn.
2. Enable the plugin with `/ppilot on`.
3. Check `/ppilot status`.
4. Enter a dummy or striking target scenario.
5. Test SuggestOnly with `/ppilot suggest`.
6. Test AssistMove with `/ppilot on`.
7. Test emergency stop with `/ppilot stop`.
8. Disable or unload dependencies and verify blocked/missing status.
9. Test a real duty only with BossMod safety active and confirm it refuses movement when safety data is uncertain.

## Known Limitations

- Movement is intentionally gated on BossMod positional and safety IPC by default.
- Avarice is not required because it does not expose the needed rear/flank/range IPC.
- RotationSolverReborn is used for `NoCasting` coordination only; no next-positional query IPC was found.
- The overlay is a simple text overlay, not a world-space marker.
