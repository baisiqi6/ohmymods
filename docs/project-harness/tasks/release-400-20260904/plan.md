# v4.0.0 release gate — 2026-09-04

## Authority and scope

User explicitly requested the release workflow. This authorizes path-scoped commits,
push, a new v4.0.0 tag and GitHub Release marked Latest after review/package gates. Preserve the
existing candidate branch and unrelated worktrees. Do not merge the open PR or
overwrite master as an incidental publication step. Do not edit saves or write the
Steam installation. Never replace a running game's DLL.

## Candidate

- Base: 64ce116520865d4ce30d27e0c5b788aa045ef1d8 on agent/post-release-candidate.
- Prior E-copy candidate: EFBB0B0F98F4719280363ABAEBDBB8768758B3F8BE190F85009758DFA0A4DA26 (309760 bytes).
- User said current state is okay and requested release. Existing LogOutput.log is
  dated 2026-09-03 20:45:53, before the final load-restore deployment; it is not proof
  of that last candidate's runtime behavior. Do not mark unspecified edge cases done.

## Gates

1. Independent read-only OMP review (kimi-code/k3, max) of accumulated gameplay diff.
2. Version consistency: project Version, plugin build stamp, tag and package = 4.0.0.
3. Checklist validator, diff check and no-deployment IL2CPP build.
4. Path-scoped commit; fresh detached worktree at that commit; clean build/package.
5. Inspect ZIP CRC, manifest commit/version/hash, embedded DLL, allowlist and absence
   of save files, private config, logs, backups, reference source and old ZIPs.
6. Push candidate commit and new tag without force. Upload and independently verify
   GitHub Release asset; retain local hash receipt. Existing PR/master left unchanged.

## Distribution choices

Package is IL2CPP only, compatible with game 2.4.0 and BepInEx 6. Omit the tester's
KingdomEnhancedMod.cfg (personal cheats/speed/difficulty); first install generates
defaults, updates keep the user's existing file. Preserve standard BepInEx.cfg.
Announce known pending online/authority/load edge verification honestly.

## Review evidence

OMP native session: 01a06c9d-99a9-7000-8331-a27a17a1cd9f.
Native model_change: kimi-code/k3, resolvedModelIsFallback=false.
Reviewer denied Bash by read-only approval policy and continued with read/glob;
operator performs Git/build/package checks independently. No permission bypass.
Final verdict and publication receipt are to be recorded before closing the gate.

User explicitly superseded the provisional v3.5.1 release number with v4.0.0 while
this same-code review was running. Existing historical development filenames are
retained as evidence, not public version identifiers; no v3.5.1 tag/release exists.
