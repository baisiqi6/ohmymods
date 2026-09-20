Operator final adjustment: only exact Playing/Menu is retained; unreadable context clears immediately. The proposed 2-second grace and broad non-playing retention below are superseded. See stability-acceptance.md.

# HeroShop whole-building flicker: retention-vs-payment split (worker result)

Scope: only `il2cpp/HeroShop.cs`, `tests/hero-shop/**`, this file. No other production, assets,
game/config/save changes; no game start/stop/injection; no commit/reset/push; no delegation; no
web lookup. Bash was rejected in this session, so **nothing was compiled or executed here**; all
commands below are for Main. Buildstamp/integration/deploy/docs stay with Main.

## Root cause (confirmed against the 2.1 logic reference only; 2.4 flow matches)

`Tick` called `Clear()` whenever `TryContext` failed, and `TryContext` hard-required
`managers.game.state == Game.State.Playing`. In 2.4 a pause is `Game.State.Menu` with the world
still loaded: `Game.CheckForPause -> TryShowMenu -> RunSignal.ShowMenu -> Routine.Goto(Menu)`, and
`Menu.Show` only pauses audio and sets `Time.timeScale = 0` — world, kingdom and `world.gameLayer`
stay alive. So every pause destroyed the whole shop (body, banners,
header) and every resume rebuilt it, which is exactly the reported "whole building disappears /
reappears" plus the reopened flag; the three repeated `ready x=74.93` lines are re-creations, not
flag animation.

## Change

New pure core policy `HeroShopRetention` (no Unity dependencies, compiled into the Core and
Invoker test projects) is now the only decision table; `Tick` is a thin adapter.

- `HeroShopRetention.Decide(probe)` outcomes: `ClearFeatureDisabled`, `ClearNetworkLost`,
  `ClearWorldChanged`, `ClearHeaderMismatch`, `ClearContextLost`, `Keep`, `Active`, `Create`,
  `Wait`.
- `HeroShopRetention.CanServe(probe)` is the payment gate: payment is served **only** in the
  `Active` outcome. `HeroShop.CanPurchase` now returns `CanServe(Observe(...))` (plus the existing
  object/owner/save/recruit checks), so a Menu pause retains the shop but can never re-enable
  payment — `CanPay`/`forceBlockPayment` stay blocked.
- `HeroShop.TryContext` no longer reads `Game.State`; it reads the same-scene world refs
  (kingdom + `world.gameLayer`) only. `HeroShop.Observe` reads the state once and reports
  `Playing`. `Create` is reachable only from the `Create` outcome, i.e. `SameScene && Playing`
  (plus the existing `_retryAt` / `isSavingGame` gate) — no spawns while paused, in Intro/level
  intro, or without context.
- Pause (`Keep`): the same object, body, banners and SemiStatic header are untouched (no
  `HeroShopBannerVisuals.Tick`, no frame change, no re-registration); `forceBlockPayment = true`
  and `SuspendPendingTransaction()` cancels an open native transaction exactly once via the
  existing `CancelTransaction`/`DropFloatingCurrency` path so the floating coins return (one log
  line per real suspension, not per frame). No `Time.timeScale` logic is touched; ordinary
  `timeScale = 0` while still `Playing` stays in `Active` as before.
- Immediate clears are kept for real losses: feature flag off (`feature-disabled`), online /
  lost world authority (`network-lost`), foreign kingdom/GameLayer or a resident shop with no
  world (`world-change`), broken native header (`header-mismatch`), missing context where the
  resident shop/layer is dead or the hold expired (`context-lost`), Tick failure (`error`),
  OnDisable (`owner-disabled`). Every automatic `Clear` now takes a reason and logs one bounded
  `clear reason=<name>` line only when it actually had state (no per-frame logs when nothing
  exists); a failed cleanup retries with its original reason and its failure line stays
  once-per-episode. `_clearing` and the OnDisable -> Clear recursion guard are unchanged.
- Transient unreadable manager context with a live shop is deferred (not cleared) for at most
  `HeroShopRetention.ContextHoldSeconds = 2 s`; a real scene/world switch cannot be retained
  because the shop is a child of the old `gameLayer` and dies with the scene (OnDisable clears).
- Grounding (native Bow/Hammer/Scythe root y, `IntPtr` `IsLocked` preflight, PNG/flag behavior)
  is unchanged.

Files: `il2cpp/HeroShop.cs` (policy + `Observe` + reason-carrying `Clear` + suspension helper),
`tests/hero-shop/Program.cs` (39 -> 54 assertions), `tests/hero-shop/Wiring.ps1` (new Cecil
metadata wiring check), `tests/hero-shop/README.md`.

## Verification to run (Main; bash was unavailable to this worker)

1. `dotnet run --project tests/hero-shop/Core.csproj` -> `HeroShop core: 54 assertions passed.`
   New coverage: Menu `Keep` (no clear/no create), resume `Active`, `CanServe` false in Menu /
   true only playing, `Wait` in Menu, `Create` only playing, no spawn without context,
   feature-off/network/world/header/context-lost clears, deferral `Keep` vs bounded
   `ClearContextLost`, hold constant bound.
2. `dotnet run --project tests/hero-shop/Invoker.csproj` -> `PASS 7 ...` (unchanged).
3. `dotnet build tests/hero-shop/Interop.csproj` -> 0 warnings 0 errors against the real
   E-drive 2.4 interop.
4. `powershell -File tests/hero-shop/Wiring.ps1` after the product build -> proves
   `HeroShop.Tick -> HeroShopRetention.Decide`, `HeroShop.CanPurchase -> HeroShopRetention.CanServe`
   and that `HeroShop.Observe` still reads native `Game::get_state`; the policy is not a
   test-only copy.
5. `C:/Users/ADMIN/dotnet8/dotnet.exe build -c Debug` in `il2cpp/` with `BepInExPluginsPath`
   overridden (compile-only) -> 0 warnings 0 errors; then the existing hero-shop audit scripts
   for the method-level delta. Expected delta: changed methods stay inside the existing allowlist
   (`HeroShop.Tick/TryContext/CanPurchase/Clear`, `HeroShopOwner.OnDisable`); added methods
   `HeroShopRetention.Decide/CanServe`, `HeroShop.Observe`, `HeroShop.SuspendPendingTransaction`,
   `HeroShop.Pending`; no method removed, no resource changed.

## Not verified here / still open

No live Unity run: that a real pause keeps the identical instance, that a mid-payment pause
returns the coins natively, the `clear reason=...` lines in a real log, and rendering of the
retained building under the paused menu. Cross-island quota/transport and the remaining hero
work are untouched. Nothing was installed into any game copy or published; no save/config file
was read, written or hashed by this worker.
