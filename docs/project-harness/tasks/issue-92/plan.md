# Windows v10.7.35 height adjustment release and local release sync

Task: issue-92
Issue: https://github.com/baisiqi6/ohmymods/issues/92
Owner: windows-codex-ohmymods-operator
Branch: win/release-height-20260930
Mode: high-risk / Coordinate-managed

User explicitly requests repackaging/publication of the latest height adjustment and updating C:/Users/ADMIN/projects/ohmymods/release. This authorizes the necessary source commit, branch/PR, normal merge, new immutable tag/Release and bounded local output sync. No player save/config edits, auto-launch/stop, force-push, old asset/tag overwrite or unrelated dirty-file cleanup.

## Scope and version

Public baseline v10.7.34 at 5bad710e048f6e6ce65b365269c6d67e0e008478 on release/v9.5.13. Reviewed installed candidate 1B5C0D201D7AAACEA0E2B055863AC2CB7175BB135899FD8F61D638107F3F24F8 contains only two height constants and related tests/comments versus that gameplay baseline: medieval knight followers and hero each multiply current size by0.95. Follower Y1.12→1.064; hero VisualScale0.933333→0.886667 shared by body/cloth/anchor. Knights and other styles retain their values. One complete appearance adjustment +0.0.1 yields10.7.35. No fix claimed for combat scale oscillation, bloodmoon, Deadlands animation, late waves, embarked archers or beggar timing; date jump is not implemented.

## Procedure

1. Register/approve/accept only this new task through official Coordinate CLI with exact reviewed script, lock, backup and readback. Preserve the other133 canonical items and issue90's unresolved closeout compatibility defect. Import only the resulting state commit into private integration worktrees while preserving all dirty/untracked bytes. Never import private history into public release ancestry.
2. Reuse the clean Windows release checkout at5bad on a new release-height branch. Before writing, verify GitHub Issue92 claim, current release branch and unrelated openPR76/16. Copy only the three already-reviewed height source/test files. Use the time-routed worker for bounded version/player-document edits; original historical release sections and full current feature descriptions stay intact. Include this plan. Verify exact include list/diff and secret exclusion.
3. Run existing hero-scale/native-visuals/GreekScaleScope checks and actual2.4 full build. Compare DLL to the installed1B5C candidate: gameplay IL,20PNG resources and348hooks unchanged; only formal version/build metadata/log may differ. Existing relevant candidate regressions may be reused only when exact same source and final tree evidence proves applicability; do not falsely claim new full-repository testing or CI.
4. Independently review source/doc/test results and PR. Merge normally into release/v9.5.13 with actual checks/gates, no bypass; no configured CI is not CI passed. Build again from exact clean merged commit. Package with existing official script, strict allowlist/CRC/manifest/doc/required-file checks, compare all306 bootstrap/runtime bytes to the verified v10.7.34 release. Review exact final commit/tag/DLL/ZIP/release-notes before irreversible publication.
5. Create only a new absent v10.7.35 tag and Release, upload ZIP+sha256, read back peeled tag, non-draft/non-prerelease/Latest, asset size/digest and downloaded bytes. If partial completion occurs inspect/reuse the same release, do not duplicate or overwrite.
6. Snapshot the user-named local release directory and affected file bytes before sync. Copy verified new ZIP/checksum and current Chinese docs (install/version rules/update log/capabilities/user guide/roadmap), plus missing34 and new35 version notes. Keep all old ZIPs/checksums/historical docs and unrelated project changes. Rebuild related-documents7z from the directory's player .txt/.md documents, excluding ZIPs/other binary/personal artifacts; verify archive integrity and every extracted file hash. Use staged writes, exact target path checks and post-copy hashes; no recursive delete/reset/clean. Archive byte backup retained privately. Local root source/branch is not changed to pretend it matches the release checkout.
7. Store publication/sync receipts and report the release URL and user-named directory. Mac remains independent and is not claimed synchronized by Windows publication. The current installed candidate already has the gameplay adjustment; no extra game install is required in this release task.

## Acceptance and known lifecycle limit

Formal artifact/code/docs identity, relevant verification, independent approval, successful remote content readback and local release output verification must all pass. Other feature tasks retain pending live acceptance. Managed closeout uses the official review/receipt protocol only; coordinate#9 currently blocks the deployed closeout/review-result contract. Record delivery separately and keep this task incomplete if that compatibility issue still exists; no manual JSON/DB/event fabrication, repair bypass or unrelated server implementation. After supported repair resume the same task with fresh packet review.
