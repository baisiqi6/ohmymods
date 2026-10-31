# Issue #316 — Windows one-click support collector verification

## Result and scope

Issue: https://github.com/baisiqi6/ohmymods/issues/316 . Base `release/v9.5.13@cb467d4c1a23474f9678794bb42c3f12cfd4b5cf`.

The root-level CMD/PS5.1 tool collects allowlisted logs, OhMyMods configuration and native saves into a local ZIP. The shared source manifest pins all three tool files; the full Windows packager and standalone add-on consume the same bytes. Existing published v11.1.0 assets, plugin/DLL, runtime lock, game data and normal installations were not changed. The current source-tree player README intentionally adds eight lines explaining the optional collector; this documentation change is included in the PR scope, not retroactively applied to old release assets. No game launched, closed, or requested to save.

This does **not** diagnose or fix the player's fresh-save/second-island save failure. That investigation still needs the player's collected evidence.

## Verification performed

| Check | Actual result |
|---|---|
| Existing Windows package baseline | 13 tests passed before change |
| Public current package tests | `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tests/windows243-package -v`: 17 passed |
| Independent package review | APPROVE; 25 synthetic checks (includes public 17); all 435 runtime entries and old synthetic-fixture payload bytes unchanged; actual player README additionally has eight lines of intended tool usage instructions |
| Windows PowerShell 5.1.19041.6456 x64 | 13 collector cases passed, no skips |
| Independent producer regression cases | Four passed on r3; same four fail on frozen r1 |
| Windows CMD entry | Three controlled stub-process cases passed: exit 0/1/2, exact Unicode/space script path and launch flags |
| ZIP normal-reader verification | Correct Chinese filename, UTF-8 EFS bit 0x0800; every captured entry size/SHA matches manifest |
| Independent final collector review | APPROVE; eight frozen source/test pins and the pins-document hash unchanged before/after review |

All Windows work ran against newly created synthetic TEMP roots, with no Explorer opening or real player-save access. The normal process observation in the public suite is replaced only inside its child PS test host; the false/true probe flips both reported flags and exit status. No globally named fake KingdomTwoCrowns process is created. The earlier unmocked normal-path smoke used only explicit synthetic roots and also preserved source bytes.

## Corrections and rejected intermediate evidence

- Ancestor junctions previously bypassed lexical overlap validation. Root resolution now rejects existing reparse ancestors before reading inputs or creating output. The old candidate actually wrote its report inside a synthetic GameRoot; corrected native regression rejects it with zero output there.
- EXE/plugin metadata hashing previously bypassed limits. Streaming metadata and copy attempts now share an actual-read budget; failed attempts consume their reads. No prefix hash is emitted as a complete hash. Metadata records show limit rejection, size, change status and hashes only when complete.
- Traversal now uses a lazy disposable iterator, counting directory and skipped-entry visits against twice MaxFiles; file count is separate. The old candidate produced 40 depth-skip rows under a two-file fixture cap; corrected output stops with one entry-limit diagnostic.
- Change detection now compares initial size, copied bytes, final size and mtime. A deterministic length 8→12 change with preserved mtime is flagged; this remains a heuristic, never an atomic-snapshot claim.
- Actual .NET Framework ZIP decoding exposed missing EFS on new UTF8Encoding(false), masked by a forced-encoding self-reader. Filename encoding now uses Encoding.UTF8; readback uses normal decoding. Old external-name corruption and unset flag, then corrected names/flags/hashes, are retained as red/green evidence. JSON encoding is unchanged.
- Copy failures report the underlying GetBaseException IO class, preserving failure behavior.

The first independent fixture root was too long for .NET4 MAX_PATH and masked the metadata scenario. It is not used as proof of that bug. The short-root rerun established the actual three failures; the final same-harness run establishes all four. One runner used the wrong WorkRoot parameter name and was corrected without changing assertions. These failures remain in private evidence.

The bounded external worker produced leaf files but exited 1 without its final report. Its self-report was never counted as acceptance. Operator corrections, actual tests and separate reviewers established the delivered result. Initial GLM requests for fixes and subsequent corrections/encoding approvals are retained.

## Limits and delivery boundary

- ZIP is a best-effort copy of disk files. A running game/observed file change is labelled live; even quiescent output is not an atomic backup. Missing/locked/oversized/reparse files are visible in the manifest. No original file is modified or repaired; no upload occurs.
- LocalData output reduces common Desktop sync exposure, but external synchronization configured by a user is outside this tool's control. ZIP can contain personal identifiers from logs/saves and is intended for private manual sending.
- Root checks address ordinary local misconfiguration, not hostile filesystem replacement races or SUBST aliases. A non-blocking P3 remains only for advanced test-root overrides: drive-relative `\foo` or `/foo` resolves on the current drive. The no-argument one-click defaults are fully qualified; development tests must use complete drive-qualified paths.
- Synthetic PS5.1/CMD/package checks are not real-player permission, antivirus, desktop-shell or gameplay acceptance. Tests deliberately avoid the normal game/save paths. No new full lazy package is published by this task; a local small add-on ZIP is prepared for existing players.
- Source review/build/PR delivery are distinct from merge, public release, installation and player-data diagnosis. The Issue remains open until the linked PR is merged under project policy.

Private local evidence: `.local/tasks/windows-support-collector-20261031/`; key receipts `windows-r3.receipt.json`, `unicode-positive-r3-proof.json`, `package-final-tests.log`, `package-review/review.md`, `collector-review/r3/source-review-r3.md`, and source pins under `candidate-r3/pins.json`. Raw Windows fixtures/provider logs are not committed.

Final decision gate: GLM5.3 max approved commit/push/Ready PR/local add-on build after reading actual receipts. Source-tree README diff was explicitly accepted as intended tool documentation; delivery sidecar records the clean source commit and exact ZIP/file hashes. Checklist schema validation passed with 0 warnings; existing cross-task missing-plan/state-builder warnings remain and were not repaired in this tooling task.
