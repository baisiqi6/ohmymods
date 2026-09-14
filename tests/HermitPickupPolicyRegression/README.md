# Hermit pickup policy regression

Run from the repository root:

```sh
dotnet run -c Release --project tests/HermitPickupPolicyRegression/Regression.csproj
```

The fixture directly links `il2cpp/PatchRoles_Hermit.cs`. It verifies the replacement lifecycle hooks and receipt policy against managed Unity/game stubs; it does not establish native detour compatibility, actual callback ordering, multiplayer synchronization, or in-game safety.

Current result: **24 cases passed, 0 failed** with `C:/Users/ADMIN/dotnet8/dotnet.exe`. Coverage includes:

- Exactly two Harmony targets: Droppable.OnEnable postfix and OnDisable prefix; no short getter detour.
- True same-object Hermit matching, 0/6 → 4 protection, and exact original restoration without writing general pickup or the native reset policy.
- Restrictive policies, duplicate enables, pool generations, external policy ownership, disabled registration, and authority suspension/recovery.
- Pending initial context/parent, bound departure, scene/world/identity changes, nested mounted parents, and inactive pruning without restoration writes.
- Temporary Loading/Managers/world/layer loss retains live Original receipts; 0 and 6 restore after returning to the same Playing or paused world. This case runs 16 combinations.
- Bounded failure logging and one success log containing object id and original/current policy values.

OnDisable only retires the receipt; the fixture's simulated native OnDisable resets its own field. Authority loss and temporary unavailable context suspend receipts without native writes. An external field change permanently relinquishes ownership for that lifecycle generation. Existing world/layer membership is checked through the same gameLayer/scene convention as other production helpers; no full-scene scan is introduced.

Worker implementation scope was only the production Hermit file and this new test directory. Canonical integration, native API audit, complete IL2CPP build, runtime inspection, and controlled gameplay verification belong to the Operator. The built-in worker fallback was used; no external model/provider claim.

Verified production SHA256 at worker handoff: `9A797393D2D7FB188749D2D5A337DF37188147F80D57ED886FD36EC329CB0CE8`.
