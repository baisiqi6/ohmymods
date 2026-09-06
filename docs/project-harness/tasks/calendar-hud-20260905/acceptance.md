# Calendar HUD acceptance (candidate only)

Source contains previous validated population sliders/panel and latest canonical Hermes/Samurai fixes.

Workers: /root/calendar_logic_worker, /root/calendar_hud_worker. Reviewer: /root/calendar_reviewer. ZCode session database remained outside writable permissions; inherited subagent fallback used.

- User correction preserved: integer hours only, no minutes.
- ShowCalendarHud defaults false, world tab switch; overlay independent of panel visibility.
- Real runtime year/season/cycle table, finite calculation; no constant 16-day assumptions.
- Pure tests compile actual CalendarSnapshot.cs with data stubs: 95,702 assertions passed. This proves calendar logic, not IL2CPP runtime integration.
- Full IL2CPP Debug build with BepInExPluginsPath empty: 0 warnings, 0 errors.
- Source allowlist/whitespace check passed; original population candidate preserved.
- Read-only review passed; no confirmed P0/P1/P2 blockers. Runtime font/layout overlap/initial icon generation performance not verified.
- Layout preview is a diagram rendered from matching geometry with Microsoft YaHei; not a game screenshot or proof of Unity font behavior.
- Deployment NOT performed: user authorized and game exited, but request_permissions returned no write grant for canonical/E. Existing E DLL is still the previously deployed Hermes/Samurai version.

DLL SHA256: A86D036741EF61F6301485ACABAFA20864BAEC37D790C660291D2509F1F0858C
