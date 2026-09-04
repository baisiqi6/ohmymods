# E-copy deployment — non-Norse follower style fix

Verified 2026-09-05 00:11 +08:00 after the game process exited.

- Source package: `C:/Users/ADMIN/projects/ohmymods/release/KingdomEnhancedMod_v4.0.0_IL2CPP.zip`
- Package SHA-256: `123B646770CFED9CE708F2E7BDEDDB16FD0C56867E7BE82B58DE7F2AC4546762`
- Target DLL SHA-256: `714B51D3B9532A897FCA6D6F6D0D37B8F5E6C7BA6307419D423FB71A300B1D00` (312320 bytes)
- Previous target SHA-256: `1DB33FD50487AC392E17CE5E0CB121581B2B57C092F3B066A426D76D9A315C73`
- New backup: `KingdomEnhancedMod.dll.before-follower-fix-20260905-001118-60e5d7b3.bak`
- Atomic rollback: `KingdomEnhancedMod.dll.rollback-follower-fix-20260905-001118-60e5d7b3.bak`

The package hash, staged extraction hash, destination hash, backup hash and rollback
hash were checked. The game process was absent at the initial and immediately-before-
replacement checks. The change only narrows follower skin selection to verified
Northlands pool identity; runtime behavior still requires player regression.
