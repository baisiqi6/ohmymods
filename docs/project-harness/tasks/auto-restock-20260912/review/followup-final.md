## Revised verdict: **PASS** (0×P0, 0×P1, 0×P2 remaining; P3s listed separately)

### P1 (synthetic selection leak) — verified fixed in current revision

Re-read `PatchEconomy_AutoRestock.cs:461-474` and `:292-293` against native source:

- The `finally` now captures `o.Shop.PlayerSelecting` and calls native `Deselect(selected)` when neither `selected.selectedPayable` nor `selected._completingPayable` references this shop (pointer comparison via `SamePayable`). This is correct: the pre-commit gates (`ShopBuyable:286-288`) guarantee no player-owned selection/interaction existed on entry, so any `PlayerSelecting` observed after `TransactionComplete` is the synthetic one set by `PerformPay` (Payable.cs:730-740 → `Select`, Payable.cs:507-519). `Deselect` (Payable.cs:617-629) resets `selectedByP1/P2` and `PlayerSelecting`, which then lets the existing `interactingPlayer` cleanup guard fire. Player-owned state is preserved by the engagement checks as claimed.
- The new preflight at line 292 (`NetworkBigBoss.IsOnline || shop.priceIncrease != 0` → require a nearest crowned player) closes an additional latent offline failure I had not previously flagged: with `priceIncrease != 0` and no crowned player, native `PerformPay` would call `CanPay(null)` → `Select(null)` → NRE at `player.playerId` (Payable.cs:510) *after* the treasury debit, which would have burned gold and faulted the shop. `Payable.get_priceIncrease` is present in the actual interop API, so the gate compiles against the real assembly.

### Disputed P2 #1 (promotion gap overbuy) — **retracted**

Verified in native source: `Peasant.HandleToolPickup` (Peasant.cs:283-289) sets `tool.pickedUp = true`, calls `_character.Promote(tool, null)` and `Pool.Despawn(tool)` in one synchronous stack; `Character.Promote(DroppableTool)` (Character.cs:504) → `Promote(string)` (Character.cs:535) → `ReplaceBy` (Character.cs:607-654) does `Pool.Spawn` of the replacement, parent assignment, then `Pool.Despawn` of the old body with no yield or coroutine. The Worker→Berserker path is likewise fully synchronous (Worker.cs:850-855, 883-893). Therefore the tool's `OnPickedUp` (stock −1) and the new body's `AddCharacter` (live +1) both occur inside the same native call stack; the mod's dirty-flag handlers flush both deltas in the same subsequent `Refresh`, so net count never dips and no overbuy window exists for the four authorized roles. My original finding assumed an async transition that does not exist. Tools dropped/carried outside shops remain uncounted, which matches the stated spec (shop tools only) and is bounded.

### Disputed P2 #2 (mid-reservation context recheck) — **retracted as a defect; downgraded to P3**

The Operator's characterization is accurate: the early `return false` is a fail-correct fail-closed gate; the candidate has already been safely deposited (`CarriedCoins != 0` guard prevents reservation with unbanked coins), no claim or gold is lost, and the existing collector scan reassigns the idle helper within one cycle. I had already noted no dupe/money impact; continuing to iterate candidates after a context invalidation would weaken the authority gate for no correctness gain. Withdrawn as P2.

### Also noted

The ledger staging retry (setting `_lastObservedStash = int.MinValue` on staging failure so `Banker.Update` re-stages PlayerPrefs without a second debit) is consistent with the described observer pattern and is debit-idempotent by construction; no double-spend path identified from the reviewed code.

### P3 (non-blocking, unchanged in substance)

1. Mid-reservation context flap can idle one collector for ~one scan cycle (the retracted P2 #2) — performance observation only.
2. `UpdateSummaries` may show "无可用店铺" when the shop cache is empty for scan-suppressed reasons — cosmetic.
3. In-flight orders freeze while pending roster characters resolve (up to 6 refreshes) — latency only.
4. `FinalizePurchase:448` manual `HookShopAddItem` is redundant with the native `AddItem` postfix — harmless.

**Verdict rationale:** the sole P1 is correctly remediated with native-conformant cleanup and an additional preflight that hardens an offline NRE-after-debit path; both disputed P2s are refuted by the exact native code (synchronous promotion) and by the fail-closed design intent respectively. No P0-P2 defects remain in the current revision.