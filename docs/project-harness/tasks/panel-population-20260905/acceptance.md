# Panel/population candidate acceptance

- Source: current canonical snapshot including deployed Hermes 32/range2x and Samurai patch.
- ZCode attempted first: v0.16.5, failed readonly database, trace 11f2427a-c514-42e0-9239-814d75a852bd. No worker session started.
- Fallback worker: /root/panel_population_worker. Independent reviewer: /root/panel_population_reviewer.
- Changes: ModConfig.cs, ModPanel.cs, PatchPerformance_Population.cs, PatchRoles_BeggarCamp.cs (comments).
- Build: dotnet8 Debug, BepInExPluginsPath empty, --no-restore, 0 warnings/errors. No deployment target used.
- Diff: no whitespace errors (CRLF treated as EOL); changed-file allowlist checked; canonical originals match captured baseline.
- Behavior: 1–120 game seconds/1–20 people, defaults 6/5; 0.5s central settings poll. Re-schedule only when changed; no catch-up bursts; no existing beggar despawn. Native fallback minimum approximately 6 seconds.
- UI: actual Unity IMGUI, four categories, bank display, card rows, gold/dark, scroll; default font preserved. Idle UI does not snap 0.375. Global GUI state restored.
- Pending: write permission for canonical files/E target; game exit; deployment/visual QA and live persistence/host tests. No claim of in-game validation.
- DLL SHA256: 9C3DAABAC6CE3195E9C2BF0764D640B391BD70A3D5E5FEA9C8DA89953336FE0F
