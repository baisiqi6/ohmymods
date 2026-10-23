# Palace paid-preparation handoff host regression

Runs the current production `Engine.PrepareAtPlaying` body, its
`PalacePaidResponsibility.PrepareAtPlaying` proxy and the complete
`if (_paidPreparationGame != managers.game.Pointer) { ... }` caller block verbatim against
controlled host boundaries. The build-time extractor reads each production file once into an
in-memory raw-byte snapshot, strictly UTF-8 decodes it, copies the preparation field block, the
method, the proxy line and the caller block byte-for-byte into
`obj/<Configuration>/<TargetFramework>/generated/`, and writes `extraction.json` with both source
SHA-256/byte lengths, every extracted region SHA-256/length, the two readiness-window slices, and
the generated compilation-input digests. Metadata has no timestamp or volatile path field.

The same extractor also writes `PalacePaidPreparation.Digests.cs`, which is compiled into the test
assembly as immutable constants. Before any case, the runner builds its own raw-byte snapshots of
the current sources (one read each), re-extracts every region, reads metadata and generated files,
and requires **current == metadata == compiled digest** for source, field, method, proxy, caller,
window, generated-file and metadata identities. A mismatch is a hard failure (exit 2) before
`RunAll`, never a warning and never a case assertion. A stale test DLL that is run after only the
extractor regenerated metadata/generated files therefore fails at this gate; a full rebuild adopts
the new digests and any wrong behaviour must fail through the behavioural assertions (exit 1).
Generated files stay under `obj/` and are not committed.

## Dependencies and commands

Requires only the .NET 8 SDK (target `net8.0`). In this workspace the toolchain is
`/Users/Admin/projects/ohmymods/.local/toolchains/dotnet`; use a PATH that contains the matching
`dotnet`. Run sequentially from the repository root:

```sh
dotnet build -c Release tests/palace-paid-preparation/PalacePaidPreparation.Tests.csproj \
  -p:UseSharedCompilation=false -nodeReuse:false
dotnet run -c Release --no-build \
  --project tests/palace-paid-preparation/PalacePaidPreparation.Tests.csproj
```

The project sets `UseSharedCompilation=false` and `NodeReuse=false`; the documented build switches
make the same policy explicit. The extractor has its own small `Extractor/` project and is invoked
by the `ExtractPreparationSurface` target before compilation. A normal build only writes this
test's own `obj/` and `bin/`; it never builds, writes or deploys the BepInEx plugin package.

`PASS` lines and the final JSON (case/assertion counts, source/region/window hashes, every result)
are the machine-readable evidence. Exit 0 means all cases passed; exit 1 means a case failed; exit 2
means the extracted production identity or the approved readiness-window bytes did not match.

## Contract covered

- `WaitingForOwner` is a pure guard: no marker consumption, no store directory/read, no cold
  handle, no preparation field writes, and a single fixed caller reason. It stays inside the
  already-bounded 20s/0.25s readiness window.
- `Attempted` covers an accepted attempt, a cached same-owner result and every terminal failure
  (hooks unverified, store failure, held journal, read exception, cold-restore failure, any
  entry-level exception). The caller consumes the per-game marker only on `Attempted` and before
  the original cold-handle branch; later ticks in the same game never retry.
- The unchanged different-epoch policy prepares a new game pointer once; same-game owner drift
  fails closed because the marker stays consumed.
- The approved 20s deadline line and the 0.25s probe slice are byte-pinned from the pre-fix source.

## Boundaries

- Exact production text: the field block, `PrepareAtPlaying`, the proxy line and the complete
  caller block are extracted, not mirrored.
- Controlled plain stubs: owner admission, native store directory/read result, cold restore/bind,
  payer/transient checks, semantic owner identity, manager/game pointers and the log sink. No Unity,
  BepInEx, native call, real journal file IO, save write, payment or config is used.
- The compiled digest binds the actual compiled test assembly to the exact snapshot used to
  generate its three compile inputs; it is not a source-text-only claim. The wrapper still executes
  only the three extracted production fragments plus stubs, not the complete `Update`, Unity
  lifecycle, native/Harmony installation or a full game run.
- The 80-tick window case invokes the extracted caller block at the existing 0.25s cadence and then
  stops; it does **not** execute `PalaceVisibleEntryScene.Update`, a real clock, Unity lifecycle,
  Harmony installation, the complete `CanPay` path or payment/save/return acceptance.
- This suite proves the local H3 outcome/consumption contract. It is not a native-cause proof and
  does not replace an actual paid/save/return game run.
