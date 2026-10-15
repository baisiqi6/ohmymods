# Heavy-shield Banner platform boundary (source-linked ANDROID)

Compiles the **real** `il2cpp/Patch_HeavyShieldBanner.cs` together with the real pure
`HeavyShieldContracts` / `HeavyShieldQuota` / `Patch_HeavyShieldBannerPolicy` sources, with
`ANDROID` defined. Compilation of this project is itself part of the evidence: it deliberately
provides no `PatchMusketeerFormation` / `PatchWorld_FleetBoatFormation` / `PatchCrossbowFormation`
types, so a successful build proves the Android preprocessed adapter never references the PC
formation owners.

`NativeFixtures.cs` / `ModFixtures.cs` are canary fixtures: every game/native or mod-runtime
member (`Player`, `Formation`, `Managers`, `HeavyShieldIdentity`, `HeavyShieldRuntime`, …) counts
the access and then throws. The suite asserts that the Android entries `Tick`, `IsAssigned`,
`TryGetOrder`, `Available` and the reflected private `Activated` entry read none of them, consume
no activation token and establish no binding, while `Reset` stays a managed-only cleanup. Positive
controls at the end of `Program.cs` trigger each canary once, proving the counters/throwing
behaviour itself works (a fault fixture that can never fire is not evidence).

```sh
dotnet build tests/heavy-shield-banner-platform/Tests.csproj -c Release -p:BepInExPluginsPath=
dotnet bin/Release/net8.0/HeavyShieldBannerPlatformTests.dll
```

Scope: host/compile-time platform boundary only. No game, native code, Harmony hooks or real
player/formation state is executed; the Android product DLL's metadata/IL is verified separately.
