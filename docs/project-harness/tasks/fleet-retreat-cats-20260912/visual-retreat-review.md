# Independent visual / native retreat scout

## Samurai afterimage status

Canonical SamuraiDashVisuals.cs SHA256 remains 980137C8606D27ABDAAC47D91ABBFEDC5D4F475AD44DD4D5A1C781BDBDEB8173, matching the previously reviewed final visual candidate.

The trigger and lifecycle wiring exists: both forward and return Motion.Begin call SamuraiDashVisuals.Begin; the injected driver's LateUpdate calls Tick; RestoreEffects calls token End, and Knight.OnDisable prefix calls Clear. Source is the Knight gameObject's SpriteRenderer. A valid live style2 actor, non-paused time, source sprite/enabled renderer/sharedMaterial and material.HasProperty(_Overlay) are required. Successful construction logs ready once. Missing source/overlay warns once and retries no faster than five scaled seconds.

The three frozen-pose ghosts and one following white body use self-owned renderers/MPBs, copied sprite, source material reference, numeric sorting layers/order and signed pose. White uses the native-reference _Overlay property, with alpha on renderer.color. Emission requires at least .04 seconds AND .4 horizontal movement. Idle owners do not read materials; existing tails fade within .2 scaled seconds. Original renderer/material/MPB is not modified.

No clear missing trigger or unconditional visibility defect was found in this bounded source review. The last 41-second paused startup contains no ready event, but Begin refuses paused execution and a dash may never have occurred. Therefore it proves neither successful visible afterimages nor failure. Ready would prove the creation path ran, not the final camera/shader output. Actual sprite/shader property-block rendering, transparency, occlusion and camera visibility remain unverified without a real dash observation. The earlier actual-E metadata audit found the selected renderer/MPB APIs reachable, avoiding the known sortingLayerName span shim; API reachability is not visual proof. New effects remain local/host-triggered; do not claim client synchronization.

## Native defensive retreat x3, style2 only

The 2.1 reference Knight.GoToWall selects its retreat branch only when outside its side's intact border and enemies are detected. It calls SetRetreating(true), selects local speed = _retreatSpeed, and calls _mover.SetGoal(targetPos, speed), followed by a three-second native wait. The ordinary branch sets retreating false and uses _runSpeed. Actual E2.4 interop independently exposes Knight.GoToWall, iterator _GoToWall_d__154, isRetreating, _retreatSpeed, _fsm and ShouldPlayerControl; the property/field wrappers are real native invoke or native field-offset accesses.

A ref-speed helper in the existing Mover.SetGoal(float,float) prefix is the narrowest proposed integration. Require:
- enabled mod, world authority, active/living style2 Knight and matching Knight._mover identity;
- actual named FSM GoToWall plus isRetreating;
- finite positive incoming speed matching finite positive _retreatSpeed;
- no player control/_beingControlled, formation, charge/pending charge, boat task, inert/grabbed/stationary/dead or other incompatible mission;
- finite positive product, then only ref speed = incoming * 3.

Place it after the existing recursive-redirect bypass, or otherwise guarantee redirected calls cannot multiply twice. Keep original goal and native method/Wait intact. Do not modify _retreatSpeed, _runSpeed, _multiplier, transform facing, dash constants, return speed or any persistent configuration. Native SetGoal naturally retains its issued goal speed for that retreat walk; the next native movement command replaces it.

Existing Samurai dash/return goals use SetGoalNoHaglet and their eligibility excludes isRetreating, so this overload/state-specific change does not touch those paths. A full regression should cover all other styles, run/charge/return, mismatched speed, player/boat/formation gates, zero/NaN/infinite values, repeated independent native retreat calls and redirect recursion. Specific 2.4 GoToWall-to-SetGoal runtime hook reachability is not established by method presence alone; use existing native-call evidence or a one-time bounded successful-match log during later authorized validation, not another polling/scene-scan probe.

Only source/metadata reads and this report were performed. No canonical/game/config/save change, build, deployment or game operation occurred.
