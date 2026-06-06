# PositionalPilot

PositionalPilot is a Dalamud plugin scaffold for assistive melee positional movement in FFXIV. When explicitly enabled, it can suggest or request small movements around the rear/flank border of the current target, then commit deeper into Rear or Flank when the selected combat source indicates or confidently implies a known positional is next.

The plugin is off by default. It has no stealth, hiding, anti-detection, ban-evasion, or ToS-bypass behavior.

## Dependencies

- BossModReborn: required by default for safety checks and AI/navigation priority.
- vnavmesh: required by default for movement.
- RotationSolverReborn / CombatReborn: optional by default; can be used as the positional intent source through cached next-action events and for narrow NoCasting coordination when enabled.
- WrathCombo: optional by default; can be selected as the combat intent source through Avarice-style local prediction from combo action, gauges, statuses, cooldowns, target debuffs, and filtered last weaponskill/spell diagnostics.
- Avarice: optional/reference-only. Source inspection found `Avarice.CardinalDirection` but no rear/flank/range movement IPC, so PositionalPilot uses local geometry.

## Verified IPC

BossModReborn:

- `BossMod.Hints.RecommendedPositional` -> `int`
- `BossMod.Hints.IsPositionSafe` -> `Vector3 to => bool`
- `BossMod.Hints.IsDashSafe` -> `Vector3 from, Vector3 to => bool`
- `BossMod.Hints.NextDamageIn` -> `float`
- `BossMod.Timeline.NextKnockbackIn` -> `float`
- `BossMod.Timeline.NextDowntimeIn` -> `float`
- `BossMod.AI.IsNavigating` -> `bool`
- `BossMod.AI.NaviTargetPos` -> `Vector3?`

BossMod positional enum mapping was verified as `Any=0`, `Flank=1`, `Rear=2`, `Front=3`. PositionalPilot exposes this for diagnostics, but it does not use BossMod recommended positionals as movement intent. BossMod safety and AI/navigation still have priority: unsafe destinations are rejected and active BossMod navigation blocks ppilot movement.

vnavmesh:

- `vnavmesh.Nav.IsReady` -> `bool`
- `vnavmesh.Nav.Pathfind` -> `Vector3 from, Vector3 to, bool fly => List<Vector3>?`
- `vnavmesh.Nav.PathfindWithTolerance` -> `Vector3 from, Vector3 to, bool fly, float range => List<Vector3>?`
- `vnavmesh.Path.MoveTo` -> `List<Vector3> waypoints, bool fly`
- `vnavmesh.Path.Stop`
- `vnavmesh.Path.IsRunning` -> `bool`
- `vnavmesh.SimpleMove.PathfindAndMoveTo` -> `Vector3 dest, bool fly => bool`
- `vnavmesh.SimpleMove.PathfindAndMoveCloseTo` -> `Vector3 dest, bool fly, float range => bool`

RotationSolverReborn:

- `RotationSolverReborn.TriggerSpecialState` -> `SpecialCommandType`
- `RotationSolverReborn.TriggerSpecialStateWithDuration` -> `SpecialCommandType, float`
- `RotationSolverReborn.ActionUpdater.NextGCDActionChanged` -> event payload `uint actionId`
- `RotationSolverReborn.ActionUpdater.NextActionChanged` -> event payload `uint actionId`
- `RotationSolverReborn.ChangeOperatingMode` -> `StateCommandType`
- `RotationSolverReborn.ActionCommand` -> `string action, float time`

No pull/query-style IPC for next action, next positional, current rotation state, GCD prediction, or target selection was found. PositionalPilot subscribes to the action-change events and caches the latest next GCD and next action.

The local positional action map mirrors RotationSolverReborn's melee positional table for DRG, MNK, NIN, RPR, SAM, and VPR. Fresh known next-GCD positionals drive movement first; if next GCD is unknown, a fresh known next-action event can drive movement for any mapped melee action. Unknown action IDs fall back to rear/flank border hold and never trigger NoCasting.

WrathCombo:

- `WrathCombo.IPCReady` -> `bool`
- `WrathCombo.GetAutoRotationState` -> `bool`
- `WrathCombo.IsCurrentJobAutoRotationReady` -> `bool`
- `OnActionUsed` -> event payload `ActionType actionType, uint actionId`

No WrathCombo next-action or next-positional prediction IPC was found. PositionalPilot can still select WrathCombo as the combat intent source by listening to `OnActionUsed` for diagnostics while predicting locally from `ActionManager.Combo.Action`, filtered last weaponskill/spell, player gauges, player statuses, target debuffs, cooldown/action availability, and level checks. Raw Wrath action events are shown separately and never overwrite the filtered weaponskill/spell predictor state. Ambiguous branches fail closed to rear/flank border hold.

Avarice:

- `Avarice.CardinalDirection` -> `IntPtr gameObjectAddress => CardinalDirection`

No useful rear/flank/range IPC was found.

## Commands

- `/ppilot`: open the configuration window.
- `/ppilot on`: enable assist movement.
- `/ppilot off`: disable and stop movement.
- `/ppilot stop`: emergency stop, disables plugin and stops vnavmesh.
- `/ppilot suggest`: toggle SuggestOnly mode.
- `/ppilot status`: print compact target, movement, block, and current-job stat status.
- `/ppilot debug`: toggle throttled debug logging.

## Configuration UI

The configuration window is organized into a compact Dashboard, grouped Settings, Statistics, and Advanced diagnostics. The default view shows movement intent, target state, dependency health, and current-job positional stats; raw IPC/event details are kept in Advanced.

## Positional Statistics

PositionalPilot tracks successful positionals per session, per class/job, and lifetime. Like Avarice, it uses local action-effect result data rather than movement proximity: a known positional action must resolve from the local player and include a damage effect whose potency marker matches a known successful positional hit. Session data resets when Dalamud/plugin state resets or when manually cleared; lifetime data is saved in plugin config.

## Safety Philosophy

PositionalPilot prefers doing nothing over unsafe movement. Assist movement requires explicit enablement, stops immediately when a safety gate fails, and degrades gracefully when dependencies are missing or IPC calls fail.

## Build

Open `PositionalPilot.sln` with a Dalamud API 15 development environment. The pure geometry tests target `net6.0`; the plugin project targets `net10.0-windows` and expects Dalamud dev assemblies under `%APPDATA%\XIVLauncher\addon\Hooks\dev\`.

## Dalamud Custom Repository

Use this URL in Dalamud's Custom Plugin Repositories list:

```text
https://raw.githubusercontent.com/SekhmetAnkh/SekhmetPlugins/main/pluginmaster.json
```

The repository manifest points to the latest GitHub release asset named `PositionalPilot-latest.zip`, generated by DalamudPackager.

## Manual Test Steps

1. Load Dalamud with BossModReborn, vnavmesh, and optionally RotationSolverReborn or WrathCombo.
2. Enable the plugin with `/ppilot on`.
3. Check `/ppilot status`.
4. Enter a dummy or striking target scenario.
5. Test SuggestOnly with `/ppilot suggest`.
6. Test AssistMove with `/ppilot on`.
7. Test emergency stop with `/ppilot stop`.
8. Disable or unload dependencies and verify blocked/missing status.
9. Test a real duty only with BossMod safety active and confirm it refuses movement when safety data is uncertain.

## Known Limitations

- Movement is intentionally gated on BossMod safety IPC by default.
- Movement uses a single destination per update and does not probe multiple vnavmesh paths.
- `Any` uses loose rear/flank border holding only: the neutral anchors are calculated from the target's facing vectors and validated behind the target between rear and flank, never flank/front.
- Fresh known RSR next-GCD/next-action positional changes or fresh known Wrath local predictions can bypass the repath cooldown once, so it reacts faster without repeatedly querying vnavmesh.
- BossMod recommended positionals are not converted into ppilot movement destinations. If the selected combat source does not provide a fresh known Rear/Flank intent, ppilot holds the nearest rear/flank border.
- If the player is currently in the target's front slice, ppilot treats that as an escape signal and bypasses normal repath cooldown to move toward an intended rear/flank border when BossMod safety allows it.
- If target-of-target confirms the current non-dummy target is targeting the player, ppilot blocks assist movement to avoid orbiting or spinning. Training/striking dummies ignore this block for dummy testing. If target-of-target cannot be read, `/ppilot status` reports it as unknown rather than treating it as confirmed.
- Safety/dependency checks are cached briefly to avoid polling BossMod/vnavmesh every frame.
- Targets whose `BNpcBase.IsOmnidirectional` flag is true are treated as not requiring positionals, so assist movement is blocked.
- Fresh known RotationSolver next-GCD or next-action positionals select the movement slice, so PositionalPilot can pre-position instead of relying on True North.
- RotationSolver NoCasting coordination is off by default; enabling it may briefly request NoCasting when the resolved RSR next GCD or next-action positional is Rear/Flank, the player is not already in that slice, and True North is not available. This can happen before issuing movement or after a distance/safety block so RSR has time to let movement happen.
- WrathCombo source does not use NoCasting and does not control Wrath settings. It reads Wrath availability/action-use status for diagnostics, filters Wrath action events to weaponskills/spells, and uses local Avarice-style prediction for selected high-confidence next positionals.
- Positional statistics require the action-effect tracker hook to be available. If it cannot hook the local client action-effect handler, the UI reports the tracker as unavailable and no success data is recorded.
- Avarice is not required because it does not expose the needed rear/flank/range IPC.
- No next-positional query IPC was found for RotationSolverReborn or WrathCombo, so event data can be stale or unavailable and unknown branches fail closed.
- The overlay is a simple text overlay, not a world-space marker.
