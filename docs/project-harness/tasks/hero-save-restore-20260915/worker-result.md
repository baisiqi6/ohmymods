# Worker result: hero paid receipts survive save/load (v2 context+epoch, review-fix round)

Scope: `il2cpp/HeroRecruitment.cs`, `il2cpp/HeroRecruitmentArchive.cs`, `il2cpp/HeroRecruitmentContext.cs`,
`tests/hero-recruitment/**`, this file. `il2cpp/HeroRecruitmentFingerprint.cs` (operator-owned) is used
verbatim, never rewritten. No extra native hooks, no user archive/DLL touched, no commit.

## Root cause (round 1)

`Scope()` hashed `island.realStartDateTime.Ticks`, which the 2.4 island JSON never serializes: every load
recreated it, so the scope rotated, the shop resolved an empty scope and confirmed an empty baseline under
it (log evidence `loaded:7035fbcb:0`), stranding the paid receipts under the previous scope (651e4...).

## What this round changes (review-fix)

**Per-snapshot provenance and hash function** (`HeroRecruitmentArchive.cs`)

* `HeroRecruitmentSnapshot` gains `HashKind` (1 = legacy raw JSON hash, 2 = clock-independent
  `HeroRecruitmentFingerprint`) and `LegacyV1` provenance. v1 files decode as kind 1 / legacy true;
  v2 files must carry both fields (missing/unknown values are corrupt, file stays read-only).
* `Record`/`ConfirmBaseline` take kind+provenance explicitly and refuse to rewrite a stored hash under a
  different kind/provenance. `SaveCapture` writes kind 2 / legacy false; virgin generations write kind 2 /
  legacy false; load confirmations reuse the **matched snapshot's stored hash and flags**, so a legacy
  empty baseline is never laundered into v2 authority.
* Epoch ownership is single: `EnsureContext` refuses an epoch another context owns, `Decode` rejects a
  file where two contexts share an epoch. The epoch cap (8) now fails closed instead of trimming, and no
  code path removes a scope, so paid references cannot be dropped silently.

**Strict, evidence-based migration** (`HeroRecruitmentContext.cs`)

* `generationPending` outranks all paid matches.
* Matching uses each snapshot's own hash kind, over this context's epochs plus every scope **no context
  owns yet**; a scope owned by another context is never a migration source.
* A known context whose own match is a legacy empty baseline or nothing at all still searches unclaimed
  legacy scopes for a nonempty exact match, so the 007 rollback recovers 651 even after the context was
  pinned to 7035, while preserving 7035 as another epoch.
* Precedence: v2-authoritative empty > legacy paid (a released seat is never resurrected); nonempty beats
  empty; disagreeing paid histories, or a v2 paid against a v2 empty, quarantine.
* Legacy paid history is claimed only when **every** receipt NativeId is nonempty and exactly one
  Character record of the loaded island; otherwise `legacy-unproven` quarantine. No fuzzy id matching.
* While any unclaimed legacy paid history remains, "no exact match" and "legacy empty exact match" are
  `unresolved`/`legacy-pending`: quota reserved, no charge, **no fabricated empty baseline**. This is what
  keeps the latest real save quarantined instead of mis-claimed.
* Deterministic pick: authoritative matches first, then baseline-pinned, then ordinal — never
  first/newest blindly. Rollback switches the active epoch and keeps the newer one.

**Runtime** (`HeroRecruitment.cs`)

* `IslandState` carries the matched snapshot's hash/kind/provenance; `LoadCapture` confirms exactly that
  snapshot (or a fresh kind-2 hash when nothing matched and nothing is claimed), and failed loads keep the
  previous active epoch and write nothing; `VirginCapture.Complete` commits epoch + kind-2 baseline +
  context mapping only after its native evidence still holds.

## Tests (`tests/hero-recruitment`)

* Island fixtures in the stub are now valid JSON (`Raw(...)` wrapper), because the kind-2 fingerprint
  refuses opaque labels exactly like the real `JsonUtility` output.
* Real fixtures: `current-island.json`/`hero-identities-before.json` still prove the 007 → 651 recovery
  (2 seats, original GUIDs, with the bug's empty baseline also matching); `latest-*` prove the current save
  has no legacy raw match yet the archive still holds unclaimed paid history, so it must stay quarantined
  byte-identically with no fabricated context.
* New coverage: 7035→paid / 007→651 / 7035-again rollback sequence; v2-authoritative empty beating legacy
  paid; other-context ownership never migrated; epoch cap fails closed (scope preserved, decode rejects a
  9-epoch context); three clock-different island texts reload the same two v2 GUIDs while an object change
  is an unknown snapshot that never charges and never writes; legacy empty pinned with its v1 provenance
  kept; failed load keeps the active epoch; old-reign regeneration appends an epoch and keeps the old
  scope; provenance/kind decode strictness.
* All earlier assertions were kept; only the archive call sites and the two previously-noted adaptations
  (`Load` marks a stub island as loaded, future-schema probe uses `schemaVersion 3`) changed.

## Not claimed

The latest real save is **not** automatically restored by v1 data: with no legacy exact match it stays
quarantined until the operator's separately audited MOD sidecar recovery (frozen latest native SHA, the
linked 7035 origin, the older exact 007 proof, the two existing paid GUIDs and unique NPC records).
Native compressed files are never edited. `GlobalSaveData.filename` was verified by the operator against
real 2.4 interop; the test stub alone is not proof of that name.

## Verification status — truthful

* **Static only.** `bash` and `eval` are denied without interactive approval in this session, so no build,
  no test run, no game run exists for this round. Everything above is code + static review against the
  stubs and the real interop surface.
* Operator commands:
  1. `C:/Users/ADMIN/dotnet8/dotnet.exe build -c Debug -p:BepInExPluginsPath= il2cpp/KingdomEnhancedMod.csproj`
  2. `C:/Users/ADMIN/dotnet8/dotnet.exe run --project tests/hero-recruitment/HeroRecruitmentTests.csproj`
  3. (optional, operator-owned) `C:/Users/ADMIN/dotnet8/dotnet.exe run --project tests/hero-save-fingerprint/Tests.csproj`
* Fixture caveat: the migration fixture assertions fail loudly if a fixture was rewritten with CRLF; the
  test normalizes CRLF, so a failure means a genuinely different fixture.

## Residual risks

1. Legacy scan cost: one kind-1 hash per unclaimed scope (≤128) on a context's first load; kind-2 hashing
   only for this context's own epochs (≤8). Virgin islands skip the scan.
2. Snapshot eviction (≤8 per scope, baseline never evicted) still drops the oldest already-masked history;
   no reservable reference is lost and scopes/epochs are never removed.
3. A scope claimed by a context but whose JSON never matches again (e.g. a save from a build that failed our
   write guards) stays unresolvable by design: unresolved/quarantine, never a guessed owner or a
   fabricated empty baseline.
4. The virgin once-guard is runtime-only (world + island pointer); a state replaced between two
   `ApplyToScene` passes could create a second empty epoch, which cannot lose a purchase (no gameplay
   happens in that window).
5. In-game items still to verify on the operator's side: the real load line (`loaded:legacy-paid:…` for the
   007 rollback), quarantine diagnostics for the latest save, and the audited recovery restoring the two
   current GUIDs.
