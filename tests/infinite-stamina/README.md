# Infinite stamina (steed) managed regression

Run (installed .NET 8, offline sources):

```
C:/Users/ADMIN/dotnet8/dotnet.exe run --project tests/infinite-stamina/InfiniteStamina.Tests.csproj -c Release
```

25 scenarios, 254 assertions, `RESULT: 25 passed, 0 failed`. Never ship the stubs.

Real-link layout:

- `InfiniteStamina.Tests.csproj` compiles **`../../il2cpp/PatchRide_InfiniteStamina.cs`** verbatim (no copy).
- `Stubs.cs` supplies the rest: Harmony attributes, minimal UnityEngine, `Player`/`Steed`/`SteedAbility`/
  `GlideMovementSteedAbility`/`RunningAttackSteedAbility` fakes whose `UpdateActionState`/`Activate`/
  `OnPushedObjects` mirror the 2.1.0 source bodies (drain arithmetic, end `Clamp01`, `Stamina <= 0` →
  `BecomeTired`, `TryToGallop` `IsTired` gate, glide `Stamina < _staminaCost` gate), plus stub
  `ModConfig.InfiniteSteedStamina` / `OptionalQoLScope` / `KingdomEnhancedPlugin`.
- `Harness.cs` is a mini Harmony: it reads the production `[HarmonyPatch]` annotations by reflection,
  binds `__instance`/`__state`/`__exception` by parameter name, and runs prefix → native → postfix →
  finalizer with real Harmony exception semantics (no finalizer = native exception propagates; finalizer
  return value is the final throw). It records `Steed`/ability field writes **only while hooks run**, so
  "the patch never wrote field X" is falsifiable.
- Every scenario has a **control run** with the feature off, so each guarantee is anchored to native
  behaviour instead of an assumption (e.g. `dt=3s, run=-2.0` really does `BecomeTired` unpatched).

Covered: off/exclusions (remote, menu, old scene, no steed, rider mismatch) zero-takeover, negative-only
rate zeroing with restore, positive rates never written, Run and Glide extreme-delta depletion, already-tired
steed recovery and gallop gate, walk/stand native recovery untouched, disable → natural drain with no rollback
of stamina or fatigue, mid-call steed swap, mid-call switch/scene change (no refill but rates still returned),
client local player with `HasWorldAuth=false`, split-screen P1/P2 isolation, native exception passthrough with
rate return, re-entrancy without inner mis-takeover, external rate writes preserved (CAS), ability entries
(`SteedAbility.Activate`, `GlideMovementSteedAbility.Activate`, `RunningAttackSteedAbility.OnPushedObjects`)
refilling while skill cost/CD/duration/push side effects stay native, ability entry exclusions, plus the
review-driven set: cross-wiring (`remote._steed` pointing at a local steed) rejected, mid-call authority loss
(no refill, rates still returned), prefix field-write exception (other fields returned, native still runs),
external write of `0` between Postfix and Finalizer not overwritten (single-restore `Borrow`), ability entry
off→on no-op, and ability mid-call target swap not refilling the replacement.

The harness injects `Steed.MidCall` (inside the simulated native body), `Steed.ThrowOnWrite` (a setter that
throws) and `HarmonyHarness.AfterPostfix` (external write between Postfix and Finalizer) — all documented
simulation points, not production hooks.

Not covered (needs the real game): actual Harmony detours, IL2CPP field access, native inlining/short-method
limits, real multiplayer/authority assignment, save/pool reuse, and frame-rate feel.

`interop-compile/InteropCompile.csproj` is a separate, intentionally non-runnable project: it compiles the same
production file against the **real 2.4 interop DLLs** to prove the referenced members exist with those shapes
(`build` → 0 warnings, 0 errors). It has no `Program`/output artifact to ship.
