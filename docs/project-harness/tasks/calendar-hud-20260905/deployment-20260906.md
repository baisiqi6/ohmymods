# Panel + calendar deployment — 2026-09-06

- Deployed at: 2026-09-06T00:31:06.2836631+08:00
- Target: E:\Kingdom.Two.Crowns.Call.of.Olympus\Kingdom.Two.Crowns.Build.22992091\BepInEx\plugins\KingdomEnhancedMod\KingdomEnhancedMod.dll
- Deployed reviewed candidate SHA256: A86D036741EF61F6301485ACABAFA20864BAEC37D790C660291D2509F1F0858C
- Size: 341504 bytes
- Previous SHA256: 8D2337C95CA14BCB8AF9B51ADE2450DE4DBAE73F61E4F21089C26EFA6967AE65
- Verified backup: E:\Kingdom.Two.Crowns.Call.of.Olympus\Kingdom.Two.Crowns.Build.22992091\BepInEx\plugins\KingdomEnhancedMod\KingdomEnhancedMod.dll.before-panel-calendar-20260906-003105-913.bak
- Verified atomic rollback: E:\Kingdom.Two.Crowns.Call.of.Olympus\Kingdom.Two.Crowns.Build.22992091\BepInEx\plugins\KingdomEnhancedMod\KingdomEnhancedMod.dll.rollback-panel-calendar-20260906-003105-913.bak
- Game exit confirmed before staging and immediately before replacement.
- Canonical source matched snapshot baseline and now matches reviewed candidate. Canonical rebuild: 0 warnings/errors; diff/checklist validation passed. Rebuild SHA256 38EE2CDD2E074914CF90F95201CC6EC036C39771D1C296EE92EBBA26400A0376 differs because build directory affects artifacts; the exact earlier-tested candidate was deployed.
- Features: population sliders, four-category panel, optional hourly calendar HUD. Default HUD disabled; F5 > 世界 > 常驻时间显示.
- Existing Hermes 32/range2x and Samurai code preserved. No config/save writes, no commit/push.
- Runtime visual and in-game behavior validation pending.
