# Final E-copy deployment correction

Verified: 2026-09-04 23:55:19 +08:00.

- Authority: user confirmed game exit after requesting the final E-copy deployment.
- Canonical source: C:/Users/ADMIN/projects/ohmymods/release/KingdomEnhancedMod_v4.0.0_IL2CPP.zip.
- ZIP SHA-256: C413BCBD587DEED4D8A090FC3F5C12FA94899D7E520ADCFAAB843481BD04B169.
- ZIP manifest commit: 7fb005c996c664b6672985203d21b00318320d2e; version 4.0.0.
- Target: E:/Kingdom.Two.Crowns.Call.of.Olympus/Kingdom.Two.Crowns.Build.22992091/BepInEx/plugins/KingdomEnhancedMod/KingdomEnhancedMod.dll.
- Deployed/embedded DLL SHA-256: 1DB33FD50487AC392E17CE5E0CB121581B2B57C092F3B066A426D76D9A315C73; 312320 bytes.
- Old DLL SHA-256: 5B55E669229CE09193138BEFAF9DD5B24093DC470F9774165387322882A48A5A (06f98f9 build, not final code).
- Backup: target path plus .before-final-v4.0.0-20260904-235451-7dae8720.bak.
- Atomic replacement rollback: target path plus .replace-rollback-20260904-235451-7dae8720.bak.
- Both backups match the old DLL hash. Existing backups retained.

ZIP CRC and unique DLL entry checked. Extracted only the DLL to a same-directory
CreateNew staging file, flushed to disk and verified its hash. Game process absent
at initial check and immediately before replacement; target hash unchanged.
The initial File.Replace call with a PowerShell null backup argument failed before
mutation (empty-path binding); rechecked all hashes/processes and used a new explicit
rollback path. Replacement succeeded, and destination and rollback hashes were reread.

No rebuild, release asset replacement, tag movement, source-code edit, save/config
mutation, G-drive or Steam installation changes. Prior E hash-equality claims were
incorrect and are superseded by this receipt. Runtime load/island/shield-wall checks
remain for the player; this receipt proves deployment, not runtime acceptance.
