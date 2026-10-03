# coin-courier-visual-lifecycle

Managed regression suite for the coin-courier display lifecycle: it compiles the **real**
`il2cpp/CoinCourierVisuals.cs` and `il2cpp/CoinCourierRuntime.cs` (plus the
`tests/coin-courier-runtime-bridge` production file set) against local Unity doubles that model
Unity's fake-null semantics and native cascade destruction. No Unity runtime, no game install.

Models the candidate mechanics behind the reported "courier turned invisible after an
in-process save switch while coin collection and the coin trail kept working; a full restart
brought it back" report by actively injecting native destruction while managed handles/caches
stay alive; the actual field trigger remains unproven:

- a natively destroyed shared-atlas texture or frame sprite is replaced through the bounded
  normal decode path, even when the animation frame index is unchanged (the same-frame dedup
  must not bypass recovery); an existing second view rebinds to the new atlas generation;
- a confirmed-bad atlas stays fail-closed without a per-frame retry loop;
- a natively destroyed view root/transform/renderer is detected (fake-null) and replaced in
  place: `world/layer` generation changes keep the original hard clear, but a same-world
  visual-only failure must keep the scene behaviour state (phase, ActionConsumed, plan, sent
  count, visit deadline, cooldowns, hidden teleport, purse) and never route through ClearScene;
- failed replacements keep the stale handle as the session marker and retry on a bounded
  ~0.5 s cadence (no per-frame GameObject churn, no fall-back to first-time initialisation);
- normal operation creates no hidden view/atlas churn.

Install the .NET 8 SDK. Run from the repository root:

```sh
dotnet run -c Release --project tests/coin-courier-visual-lifecycle/CoinCourierVisualLifecycleTests.csproj
```

`Stubs.cs` is copied from `tests/coin-courier-runtime-bridge/Stubs.cs` and upgraded for the real
Visuals surface (embedded-atlas decode via `ImageConversion`, cascade GameObject destruction,
sprite/texture lifetime counters, `GameObject.All` observation). A passing run does not establish
real rendering or in-game behaviour; the in-game pass owns that evidence.
