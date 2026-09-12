# IMGUI hotfix acceptance — 2026-09-06

- Root cause: E game's LogOutput.log contains 358 Method unstripping failed exceptions at GUI.DrawTexture -> ModPanel.DrawPanel -> OnGUI. Shortcut processing reached drawing.
- Actual target interop inspection: all GUI.DrawTexture overloads forward to a throwing stub. Baseline reachability audit reproduces failure; final canonical DLL audit passes175 reachable methods. GUI.Box(Rect,GUIContent,GUIStyle) is a native invoke wrapper.
- Change scope: ModPanel.cs, CalendarHud.cs, new ImGuiCompat.cs. Cached Texture2D background styles replace all five DrawTexture calls. Panel drawing failures close and log once, shortcut input precedes HUD Tick, styles commit on complete initialization. HUD readiness checks all three styles.
- ZCode0.16.5 worker session sess_d9becb8c-8fbf-4b15-8131-3080deb46483; native stream event confirms bigmodel/GLM-5.3 (max effort requested, not independently confirmed). Worker snapshot at C:/Users/ADMIN/Documents/Codex/2026-09-05/ohmymods-operator-2/panel-hotfix-worker. Operator corrected Texture2D factory type and callback compilation issues.
- Independent reviewer /root/panel_hotfix_review found no actionable hotfix regression/blocker; advised all-style HUD readiness, applied. Visual appearance/hover/input not claimed verified.
- Canonical build: 0 warnings, 0 errors; source allowlist and git diff --check passed. Calendar calculation/slider gameplay unchanged, no additional pure calendar tests necessary.
- Deployed canonical DLL A9B115D201889A56C045A14F859C03E1EB662374651334B723C562E0471CEC04 (342528 bytes) at2026-09-06T01:36:21+08:00. Two game-exit gates, previous hash verification, backup, atomic replacement and target/rollback hashes checked. See hotfix-deployment-20260906.json.
- Runtime acceptance pending: F5/Ctrl+F10 open; all four tabs; sliders and scroll; Esc close; World HUD toggle shows hour/season icons; no new panel exceptions. Release ZIPs were not repackaged by this hotfix.
