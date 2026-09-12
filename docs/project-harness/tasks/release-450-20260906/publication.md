# v4.5.0 published

- GitHub Release: https://github.com/baisiqi6/ohmymods/releases/tag/v4.5.0 (published, stable, independently confirmed Latest).
- Release tag v4.5.0 peels to source commit c2032185a5bc213f085a5831abedbbaacfab8b36. Candidate branch pushed without force; master/previous tags unchanged. Follow-up receipt commits do not move this tag.
- Local full installer: C:/Users/ADMIN/projects/ohmymods/release/KingdomEnhancedMod_v4.5.0_IL2CPP.zip.
- ZIP SHA256 658c68074e6a618e0eb78b4a8b6a7618b77bbddf71260dd90e25774f43539e06; 39440865 bytes, 313 entries. GitHub asset digest and size match exactly.
- DLL SHA256 aa4b1959c16e1dbbdcc5546437f559725e029ba4386b456a55ad6c5b09e3bd8a; 374272 bytes, assembly4.5.0.0/plugin4.5.0. Actual embedded IL confirms no shared Dispose patch and Samurai disable cleanup is not gated by Enabled.
- Built with deployment disabled from a clean detached worktree at the release commit; 0 warnings/errors. ZIP CRC, complete bootstrap/root dotnet/BepInEx runtime, required docs, manifest commit/version/DLL hash and strict allowlist passed. No personal plugin config, saves, logs, backups, game binaries or interop cache included. Existing packaging code was used unchanged; external verification caught and corrected only a validator false positive on Microsoft.Extensions.Logging.Abstractions.dll (a runtime dependency, not a log).
- User saved/exited after readiness question. The actual embedded ZIP DLL was backed up/atomically deployed to E and observed for48.4s until scene/ClockDiag; game responsive, no early crash. Game was stopped at that point. Save before/after hash identical (95172D158C29DCE1B42790BAF20FD9B416B5D22C66F0A8446BE5AADD73DE00B1); this differs from earlier startup receipt because the user had played and saved between tests. No save rollback occurred.
- Regressions: calendar95702 assertions, knight27 scenarios, crossbow9 scenarios/1462 assertions. Final added Samurai cleanup is one removed gate, independently reviewed and verified in compiled IL plus final startup.
- Final public game behavior matches the previously tested candidate plus that one cleanup fix and version stamps. General user feedback is recorded; online/island/authority edge checks remain doing. No claim that this release fixes the separate system BSOD cause.

The full ZIP was prepared and validated while waiting for game exit; publication remained gated on the subsequent exact-package startup test. Clean-worktree package and runtime artifacts were not rebuilt or changed between testing and upload.
