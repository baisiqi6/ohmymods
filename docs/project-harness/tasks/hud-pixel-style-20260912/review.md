**Verdict: PASS** — both files are within the stated scope and I found no P0/P1 issues.

**CalendarHud.cs (HUD restyle)**
- Data path is unchanged in effect: `Tick` still gates on `Enabled`, world/scene/director pointer identity, game state (with the same paused-in-known-world allowance), reads the bank from `BankAssistantCoordinator.GetStashedCoinsForPanel()` only inside the 0.5 s cache, and never reads game state in `Draw`. Error paths clear state and back off via `_retryAfter`. No input, economy, or caching regression.
- `Draw` is Repaint-only, saves and restores the full GUI state set (skin, colors, matrix, enabled, changed, depth) in `finally` — correct.
- Resource ownership is clean: textures and `GUIStyle`s are private and per-owned; `MakeTexture` uses `HideAndDontSave`, point filtering, and `Apply(false, true)`. No shared font/material/texture is written. The Zpix lookup runs once (`_fontSearched`), is bounded at 128, and degrades to `GUI.skin.font` if absent.
- Layout fits the 552×54 strip: row 1 ends at y+28, row 2 at y+51. The scale clamp (`Mathf.Min(scale, max(1, Screen.width−24)/Width)`) guarantees the strip can't exceed the screen even at the 2× high-res cap.
- `Label` temporarily mutates only its own per-owned style's `fontSize` and restores it in `finally`, with `GUI.contentColor` reset to white — consistent with the surrounding draw code that resets to white and restores true saved values at the end.

Two P2 observations, neither blocking:
1. `CalendarHud.cs:141` — the shrink-to-fit math can request `fontSize` values below a non-dynamic bitmap font's fixed size. Unity ignores those requests for bitmap fonts, so it's harmless, but the computed value is misleading if you ever debug off it.
2. `CalendarHud.cs:180` — `_fontSearched` never resets, so if the found Zpix font is destroyed in a domain reload, the fallback path uses `GUI.skin.font` forever. That's a safe degradation, just noting it's intentional-looking and fine.

**SpecialTowerDuplicateCleanup.cs (Retained helper)**
- `Retained` (line 407) is exactly as described: logs stage-tagged retention, swallows logging failures, returns -1. I checked every site — all previous bare `return -1` paths now route through `Retained(candidate, stage)` with accurate stage labels; no control-flow, guard, or ordering change. The `-2` post-retirement-throw path and the `done < 0` stop-on-negative semantics in `RemoveDuplicates` are untouched.
- One trivial nit (P2, cosmetic): `Retained` logs `SafeX(root)` but not the tower name/level; adding `root.name` would help the residual-tower investigation, since the stage string is the part you'll actually be reading.

No data, caching, economy, or input regressions found; bounded font lookup and private resources confirmed.