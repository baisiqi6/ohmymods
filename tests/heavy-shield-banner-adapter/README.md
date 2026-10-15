# Heavy-shield banner adapter managed checks

Build the complete product against the matching real interop references with deployment disabled, then run:

```sh
dotnet run --project tests/heavy-shield-banner-adapter/Tests.csproj -c Release -- /absolute/path/to/KingdomEnhancedMod.dll
```

The test executable loads that exact product DLL and invokes only its native-free `HeavyShieldBannerRules` and `HeavyShieldBannerRequest` types. There are no game/native replacements, copied policy implementations, or test-only product compilation switches. The existing `tests/heavy-shield-banner-policy` suite covers the underlying G0 selection/binding algorithm separately.

Checks cover living-pikeman/occupied/empty anchor-index precedence and bounds, speed fallback, campaign lineage independent of save phase, monotonic activation/overflow, exclusion of the other local player's selected life, preservation of an existing assignment when initial eligibility changes, and consumption on loss/empty choice/duplicate current receipt/side or life replacement. Ordinary ending retains the G0 activation watermark.

The scope cases additionally require the same installed identity Session object, world and Land. A successful same-island load installs a new Session even when owner generation, world and Land are unchanged; that must end the previous transient banner scope. Null Session and unknown Land never qualify as an equal valid scope. These pure comparisons alone do not prove the first-activation/first-Runtime-Tick integration: the runtime suite links the actual Banner leaf and uses `BannerNativeBoundary.cs` only for controlled native API inputs and observer invocation.

These tests do not execute Harmony hooks, Unity/IL2CPP, native array reads, `GetFormationPosition`, `RestrictPosition`, actual movement, or gameplay. Current 2.4 ARM64 static evidence supports those native calls; platform-specific original/native execution remains a separate validation gate.
