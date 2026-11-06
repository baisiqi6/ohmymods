# #298 source/host delivery — Native pending (2026-10-27)

Base `release/v9.5.13@4d3931dbcaa28aaa326992221c77f39337604ff8` was fetched and remains current. Feature commits: `faad862d71357ca3b80684de5e164993c8c3e5ea`, review repair `a735fdacb1c048c75acd5447baafdddec55e6135`. This receipt accompanies a Draft PR; no merge, release, normal installation or Native game run occurred for Gems.

## Result and scope

The user-approved 28+28 Gems use four native fixed-amount Gem chests ×7 on each physical island 11/13. Existing native Chest/Persistent owns the loot and object rows; one native Prefs batch key records once-only issuance per durable campaign/island. Existing islands can receive their single batch without changing terrain or wallet balance. First-save GUID absence defers issuance.

Owned spill iterators complete their native Drop portion in one bounded call window, preserving native fade cleanup. Batch state stages only with a complete exact native island capture. The existing physical Msk writer admits the actual outgoing payload only if frozen batch and object proofs match, independent of Msk owner/key. Its PC-only integration is three lines before the existing Msk early return. No new save engine, parallel identity, global scheduler or per-frame scan was introduced.

Only the three new Gem files, ModPanel Tick/Clear, that single writer predicate, owned tests, necessary disabled boundary in three explicit-source Msk test projects and task documents changed. Main/version/build entry points, Android pins, native shop/mount prices and installed packages remain unchanged. Android's explicitly included Msk source excludes this call under `#if !ANDROID`; this is a source-boundary check, not Android build/device acceptance.

## Independent review

Astra full review initially requested changes. Both independently reproduced issues were repaired: skipped native restoration of an unopened chest now retains its original proof and blocks destructive saving; an unrelated fresh campaign can save before acquiring its first durable GUID while previous campaign rewards remain protected. Incremental Astra re-review **APPROVED**. Partial restoration, repeated empty captures, old-load spill evidence, markerless rows and same-slot recreation were checked.

The exploratory pre-first-Tick markerless schedule still has an unestablished Native reachability assumption: the private expanded probe reports 83 pass/1 fail. It is preserved and not relabeled passing or a confirmed product defect. The unchanged author suite independently reports 78/0. Native ABI/frame timing remains outside these host conclusions.

## Verification

Root independently reran the final exact source after the repair:

| Check | Result |
|---|---|
| Source-linked Gem suite, explicit private wrapper | **78 passed /0 failed**; all input pins reread unchanged |
| Mac 2.4.0 full source NoDeploy | **0 warnings /0 errors**, 348 Compile inputs /275 resources |
| Windows 2.4.3 actual installed `.281.3` frozen-source minimal overlay NoDeploy | **0 warnings /0 errors**, existing 38 resources retained; no latest Palace import |
| Final diff whitespace | `git diff --check` passes |

Root output SHA256: Mac `234c4edc92c997875ff30fb6f116c2f47f3d1d228b07328a455150c40824dc0c`; Windows minimal overlay `6f706fcd43b973358db107846879ad18d89de9648b7525b2474ab183c803de18`. These are private verification outputs, not installed DLLs. Earlier author builds have different paths/metadata and separate output hashes; they are not substituted for Root runs.

The first Root test-wrapper rerun included old generated obj files after changing its intermediate path; the first explicit-wrapper attempt set an SDK property too late. Both wrapper failures and logs remain. The corrected explicit source list and command-line properties reran the unchanged product/test sources successfully; no product or assertions were altered to resolve these harness errors.

Attributed implementation evidence: actual Gem predicate→actual Msk async/sync prefix composition **28/28**; Msk pipeline **233 assertions**, legacy gate **18 cases /405 assertions**; transition **17**, map-result **22 cases /230 assertions**. These were read and retained, not rebranded as Root reruns. The adapter retains **1/41 baseline failure** (`CatalogInconsistent`); the full public-release Win243 tree retains **19 baseline compile errors**; unchanged split/runtime/fresh projects lack existing transition glue. Private baseline replays match; none is called green or repaired by this feature.

Actual Mac/Win native Chest/writer body mappings, trampoline swallowing behavior and independently decoded resource prices are preserved locally. The long unique Level.OnLevelLoaded and Chest iterator bodies were checked; a factory hook alone is invalid because the native player trigger inlines it. No interop DLL is treated as original method implementation.

## Remaining Native acceptance

Prepare a fresh private launch package/window, then separately test Mac and Windows: new/old island once-only issuance, unopened/one-open/all-open save and cold reload, partial drop boundary, return voyage, campaign switch, feature OFF, actual ground placement, pool/scene replacement and controlled capture/Prepare failure. Host checks do not establish detour ABI, actual delivery, disk/crash atomicity, multiplayer, Android devices or player acceptance. No Gems were added to the user's normal save.

Private evidence: `mount-gem-chests-20261027/root-verify/final-a735/`, `implementation/review-repair-1/`, `full-review/` and `writer-prefix-tests/`; native contract and initial economy evidence under `shield-purchase-windows-20261027/readonly-20261027T110732Z/`. Player saves and native game binaries are not committed.

Provenance: Mac Max / Codex · Maintainer Operator · acting_for=ohmymods Mac Operator.
