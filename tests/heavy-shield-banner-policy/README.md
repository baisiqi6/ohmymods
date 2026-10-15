# Heavy shield banner policy host tests

Run with .NET 8:

```sh
dotnet run --project tests/heavy-shield-banner-policy/HeavyShieldBannerPolicyTests.csproj --configuration Release
```

The project compiles the production `il2cpp/Patch_HeavyShieldBannerPolicy.cs`
directly. It has no Unity, native interop, stub policy or test-package dependency.

Coverage includes same-side single selection, nearest flag distance, stable
receipt ties, invalid/ambiguous rosters, legal empty-activation consumption,
repeated/stale tokens, same-formation reactivation, no replacement after loss,
context and career-life termination, and owner teardown.

`IsCurrent` validates the already selected owner's current facts and ends its
binding on mismatch. It must not be used to search a roster by querying each
candidate. `End` retains the consumed token; only owner/world teardown may call
`Reset` to restart tokens. The native adapter must establish all supplied facts.

These tests do not verify native formation positioning, game movement, hooks,
combat, saves, installation or player acceptance.
