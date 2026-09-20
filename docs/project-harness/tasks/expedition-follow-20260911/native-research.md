# Expedition followers: native follow versus wall clamp

## Finding

Canonical DefenseSpacing contains a concrete mechanism capable of causing the reported “knight leaves, followers remain at wall” behavior. `NightFollowerAnchorPrefix` replaces native **dynamic Object/Formation follow** with a **fixed Position goal**. It has no knight expedition/charge exclusion. The native Archer follow branch normally issues its Object goal only once and waits for ownership/embark changes, so moving the knight does not itself reissue a goal after the replacement. `NightParkedFollowerSweep` can additionally reapply this wall clamp to an expedition's followers outside the wall every patrol pass.

This is a source-proven semantic conflict, rather than evidence that the running game's particular units have already been traced through it. No live game was operated.

## Exact reference behavior and canonical conflict

- `game-source/Assembly-CSharp-2.1.0/Archer.cs`, Behaviour priorities: an assigned Archer without a special formation selects goto **2**. Higher-priority states include inert512, grabbed256, stationary131072, manual control32768, embark65536, formation1024 and flee64. Goto128 and32 can also remain locked by native priority logic. Therefore `_knight != null` alone does not authorize overriding its movement.
- At the follow2 branch (`Archer.cs:476` onward), native ConvertToSoldier is followed by `SetGoal(_knight.gameObject, runSpeed, -knightFollowDistance, Mover.OffsetMode.Formation)` **once**. It then yields until HasKnight becomes false or the knight embarks, subject to the routine priority machinery. It does not repeat SetGoal merely because the owner position changes.
- Mover Object mode reads `_goalObject.transform.position.x` on each movement update and applies `_goalOffset * _goalObject.transform.localScale.x` for Formation mode. Position mode instead holds the fixed coordinate.
- `il2cpp/PatchWorld_DefenseSpacing.cs:448` NightFollowerAnchorPrefix intercepts GameObject/Formation SetGoal, requires night plus an Archer with `_knight`, but does not exclude pending/active expedition, manual control, formation or embark. It computes a wall-bound anchor, calls the float overload `mover.SetGoal(newAnchor,speed)` and returns false. This discards the moving target and Formation offset mode.
- That prefix also lacks an explicit check that the supplied `goal` is the Archer's actual knight game object. Its interpretation of `anchorX = goal.x + offset` omits the native multiplication by target localScale.x; this is a separate direction-sensitive approximation, not the primary cause of lost tracking.
- `NightParkedFollowerSweep` at ~938 chooses any active Archer with a knight outside the intact wall and reissues the native follow goal. That call reenters the clamp above. No charge/formation/embark/manual task exclusions are present in the inspected branch.
- Native Knight.DoCharge sets `_shouldCharge=true`. Charge routine clears that flag before setting `isCharging=true` after selecting a valid portal/serpent target. A predicate based only on isCharging misses the pending/transition interval. Knight.State.Charge is 3 in the reference; use the actual named member at runtime.

## Actual E-game 2.4 API evidence

Cecil read of actual `BepInEx/interop/Assembly-CSharp.dll` confirms:

- `Archer.behaviour : Coatsink.Common.IHaglet` (not a StateMachine).
- Concrete `Coatsink.Common.Haglet.latestGoto : int`, `started : bool` exist. `IHaglet` exposes `Inspect`, GetGotoName and Goto, but does not itself expose latestGoto in the inspected wrapper; use the project's established cast-to-Haglet pattern with a safe failure path, not `archer.behaviour.latestGoto`.
- `Archer._knight`, `_currentFormation`, `_embarkee`, `knightFollowDistance`, `IsInFormation`, `HasEmbarkableTarget`, `ShouldPlayerControl` exist.
- `Knight.isCharging`, `_shouldCharge`, `_fsm`, `_currentFormation`, `_beingControlled`, `_embarkee`, `_runSpeed`, ShouldPlayerControl and HasEmbarkableTarget exist.
- `Knight.State.Stand`, GoToWall, Assemble and Charge exist as static property accessors. Charge getter reads a native static field; its value was **not read live**. The value3 comes from reference source; prefer `Knight.State.Charge` rather than embedding3.
- `Mover.SetGoal(GameObject,float,float,Mover.OffsetMode) : Coatsink.Common.Wait` exists.
- Actual enum literals: `Mover.GoalMode.Off=0, Position=1, Object=2`; `Mover.OffsetMode.Distance=0, Formation=1, Strict=2`.

The exact goto2 follow-body semantics above are from the reference decompilation, not a claim that the complete 2.4 native coroutine was reconstructed. Capture `Haglet.latestGoto` and Mover mode/target once in the affected scenario to confirm the live state.

## Minimal safe repair contract

1. Add one shared “expedition or externally tasked” guard to BOTH NightFollowerAnchorPrefix and NightParkedFollowerSweep. Skip wall clamping for pending charge, active charge or `_fsm.Current == Knight.State.Charge`, and for owner-controlled/formation/embark tasks as appropriate. Preserve native dynamic goals. Validate that the intercepted goal's pointer equals the assigned knight.gameObject pointer before interpreting it as a follow goal.
2. Merely skipping future clamping does not repair existing Position goals: a follower already in native follow2 can wait indefinitely without reissuing the Object goal.
3. For new wall redirects, keep a bounded per-Archer ownership receipt: native actor/mover/knight pointers, exact goal position/speed written and the original native follow arguments. When an expedition begins, restore Object/Formation **only if** current Position/no-goalObject/value/speed still match that receipt, owner membership is unchanged and the Archer is still in native follow2. Then drop the receipt. This is the strongest proof of movement ownership.
4. For already-contaminated units predating the receipt, a narrowly bounded migration may infer a legacy wall goal only after checking: host/live/living, actual assigned expedition knight, no tower/formation/embark/hand control/pillar/flee/inert/grabbed/stationary task, Haglet.latestGoto==2, mover Position with null target, goal equal within tolerance to this patch's known wall pullback anchor and speed compatible with the original follow speed. Document that this legacy signature is inference. If not matched, leave it alone and log a bounded diagnostic; do not force every assigned Archer to follow.
5. Restore with the exact native call `SetGoal(knight.gameObject, archer.runSpeed, -archer.knightFollowDistance, Formation)` after the new guard is effective. It should be one restoration, not a per-frame goal fight. No UnPause, StopAllCoroutines, forced Goto(2), teleport, direct Transform writes, roster changes, repeated broad scans or restoration of an arbitrary stale goal.
6. A positive Mover._pauseTimeout may belong to shoot/flee/native tasks. Do not clear it. Defer restoration if needed; a valid Object goal can remain installed while the pause naturally expires, but only if task/ownership checks still permit the write.
7. Formation1024 has its own per-frame slot goal; embark65536 has an embark target; flee64 is a separate native goal. They must remain under native control even when the same Archer still has a knight pointer. Stale dead/changed-owner receipts must be removed without changing the new actor.

## Client fire dependency note from preceding task

Actual 2.4 Archer.DeserializeFromData, RecvBuffArcher and ConvertToSoldier are exposed. Reference Deserialize selects Soldier/Hunter visuals, restores tower/attack fields and then assigns ActiveArrowAttack from the fire bool; it does **not** restore `_knight`. Thus style3-plus-owner-only resource repair cannot be assumed to run on clients before native fire selection.

The existing `Archer_ConvertToSoldier_KnightStyleSkin_Patch` postfix runs inside Deserialize before its final fire selection. A separate missing-fire-data-only receiver helper can be called there before the owner-dependent skin method, without adding new native hooks: accept a live Archer with applicable FireAttacks and null fire data; derive current-world original fire asset/pool, assign only that null instance field, never activate a buff or change expiry. Existing ConvertToHunter postfix can cover the corresponding hunter-deserialization path. This is data readiness, distinct from host style3 casting authorization. No new receiver helper or production change was made by this research task.
