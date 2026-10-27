# Mount-island gem rewards Implementation Plan

> **For agentic workers:** Use subagent-driven-development to implement the owned task, then obtain independent specification and code review. Steps use checkbox syntax for tracking.

**Goal:** Add exactly 28 one-time Gems on each physical mount island (11 and 13), including existing islands, without modifying normal player data during development.

**Architecture:** Instantiate four native fixed-amount Gem chests of seven Gems in a verified current island. Native objects own loot and persistence; one small native-Prefs payload records that a campaign/island batch has been issued. Live batch state becomes staged only at the exact native island-save marker, then serializes with the same Global payload. Owned native spill iterators finish the Gem-drop portion in one call window, keeping their native final fade wait, so ordinary save/quit cannot interrupt unpaid remainder between frames.

**Tech Stack:** Current IL2CPP line, C#, HarmonyX/Il2CppInterop, native Chest/Persistent, source-linked .NET tests. Base `release/v9.5.13@4d3931dbcaa28aaa326992221c77f39337604ff8`.

## Global constraints

- User approved `28 + 28 = 56`, finite once-only; no wallet/bank top-up, daily refresh, repeated old-island grants, or changes to purchase qualifications.
- Only develop in `codex/mount-gem-chests-20261027`. Do not modify normal saves/configuration/DLL, Steam, Mono, existing private test credentials, or another task's uncommitted files.
- Use the current native contract, not interop method bodies as implementation evidence. Native bodies and source pins are in the private `gem-chest-contract/report.md` evidence for this task.
- Fixed Gem chests are excluded from native `gemCount`; current private island template incomeMultiplier is 1. Verify the live clone is 1 before issuing seven-Gem instances; never change shared/global income settings.
- The amount-zero/removed native chest is not proof that a batch was never issued. Parsed batch marker means no regrant even when every chest has disappeared.
- No saved GUID map yet means wait for the ordinary native save that establishes it. Reuse the existing read-only durable campaign-GUID resolver; do not couple rewards to HeavyShield money-ready or allocate a second identity system.
- Android device behavior and multiplayer are outside this implementation. Keep PC-only integration under the existing platform boundary, and report native acceptance separately from builds/tests.

## Evidence and baseline

Current Mac assets: original Greek unlock budget 50, nominal original supply 55; additional catalog unlocks 36 plus shield permanent rights 8. Approved additional 56 leaves a nominal total margin of 17. These are static configuration totals, not proof of actual delivery or Windows resource parity. The actual Gem-chest block group is 49, which the existing extension planner suppresses, so inherited gemCount6 is not counted as existing delivered supply.

The unchanged legacy `mount-island-split-policy` and `mount-island-runtime` test projects currently fail to compile because their Compile lists omit the existing `MountIslandCurrentTransitionTime` dependency. Preserve the raw baseline logs; do not claim these suites green or change unrelated product code to appease them. New tests must link the real production code they verify. The dedicated .NET SDK is `.local/toolchains/dotnet10/dotnet`; use an isolated CLI home, `DOTNET_GENERATE_ASPNET_CERTIFICATE=false`, and `DOTNET_ROLL_FORWARD=Major` for host tests. Pure builds must disable deployment explicitly.

## Task 1: Source-linked policy and once-only state

**Files:** create `il2cpp/MountIslandGemRewardState.cs`; create `tests/mount-island-gems/MountIslandGemTests.csproj` and `Program.cs` (with test-only API stubs if necessary).

**Interface:** the production state owner supplies the 11/13-only reward plan, strict versioned batch codec, live/staged separation, and a bounded native-iterator drain policy. Runtime integration uses the same source rather than copying algorithms into tests.

- [ ] Write and run failing behavioral tests before production implementation: campaign/island independence; 4×7 budget; completed marker forbids regrant; unknown/malformed/duplicate/future payload preserves old data and refuses issuance; global-only and wrong-island capture cannot publish live state; exact same-instance capture can stage; failed capture keeps previous staged state; repeat callbacks are idempotent.
- [ ] Implement the smallest state/codec that passes those cases. Payload key is `KEM.MountIslandGems.v1`; fields are version and existing campaign GUIDs with batch1 bits for lands11/13. It is an issuance bitset, not an identity/coin ledger. Bound document size and campaign count, keep malformed input observable and closed.
- [ ] Add native-iterator adapter tests: non-owned unchanged; one exact iterator/life is drained once; reentry runs native body without nested draining; finish at state3, not the final ten-second fade; exception, unknown state or the 16-step bound latches unknown and never restarts a fresh factory or replays drops.

## Task 2: Native owned runtime and save adapter

**Files:** create `il2cpp/MountIslandGemRewards.cs` and `MountIslandGemRewards.Hooks.cs`; modify only the optional Tick/Clear call sites in `il2cpp/ModPanel.cs`. Keep public integration `MountIslandGemRewards.Tick()` / `Clear()` small; new Harmony observers live in the owned Hooks file and are picked up by the existing assembly patch registration.

- [ ] Require verified offline ordinary Greek owner, physical island11/13, the current Level/world/gameLayer, and the saved GUID resolver. Use finite placement candidates on native walkable ground, avoiding endpoints, portals, acquisition facilities and player overlap. Do not use town shop placement, which unnecessarily requires walls/territory for exploration rewards.
- [ ] Create all four native `Prefabs/Objects/GemChestFixedAmount` instances while interaction is closed. Set final stable batch/land/index names, parent/transforms, Gems/7/fixed fields; then RecalcCache and verify the inherited path and exact persistent registration. Publish liveIssued and enable interaction only after all four succeed. Roll back a failed partial batch before any box can be opened; never retry by minting over a surviving partial batch.
- [ ] Observe static IslandSave prefix/finalizer and same-instance UpdateSavedWithRevisions; use the exact current scene and capture target, preserve nested scopes, and stage issuance only after normal capture and marker. Validate remaining owned chest rows/IDs and completed native drops rather than assuming any nonthrowing return is complete.
- [ ] Observe Prefs Prepare prefix/finalizer following the existing production capture pattern: serialize only staged state, SetString/readback, and verify the copied srzEntries key/value. Wrong owner, exception, missing marker, global-only save or malformed source never publishes liveIssued. These observations prove capture/serialization, not successful disk write.
- [ ] Resolve the actual `Chest._SpillCurrency_d__26.MoveNext` wrapper/body for both verified platforms, including owner/state fields, and restrict interception to exact owned objects or restored native fixed chests bearing this module's saved names. The factory is inlined in the player path and must not be used as the sole interception point. Drain the same iterator up to state3 with a per-instance guard and 16-step maximum, preserving native SpawnCurrency/Drop and fade cleanup; no new currency loop or shared short-method hook.
- [ ] Scope feature OFF/context changes so no new rewards are issued, while existing native boxes and staged issuance are retained for native saving. `Clear()` discards managed tracking, never deletes already-granted native loot or durable records. Diagnose unknown once per bounded key; no continuous scans, regrant loop or exception swallowing presented as a fix.
- [ ] Run the source-linked runtime adapter cases covering incomplete creation, stale owner, duplicate native callbacks, saved marker with no boxes, owned rows with no marker, input focus/menu changes, feature OFF, island return and pool/scene replacement. Unexpected native failure must never be advertised as exactly-once success.

## Task 3: Independent verification and deliverable

- [ ] Review the full feature diff for state/writer responsibility and actual native-hook addresses before claiming it ready. Confirm short/folded methods are not used.
- [ ] Run new source-linked tests and relevant unchanged regressions that can run with their existing host glue; distinguish baseline failures. Build Mac240/Win243 (and any affected explicit compile consumer) with deployment disabled, recording actual inputs/output hashes.
- [ ] Update this task's receipt and progress boundary; commit/push only owned changes and create a Draft PR linking #298. Do not merge, release or replace a normal DLL on host-test evidence alone.
- [ ] Prepare a private game candidate for the later native window: new island, old island, unopened/one-open/all-open saves, fresh-process reload, return voyage, partial spill boundary, known capture failure, and shield-OFF new-campaign GUID availability. Native tests require a fresh specific candidate and private save environment; do not reuse old launch approvals or restore stale player progress.

User approvals already cover the amount/design and continued isolated development. Do not ask again for those choices; only request genuinely missing information or a concrete normal-data/deployment window when a reviewed result is ready.
