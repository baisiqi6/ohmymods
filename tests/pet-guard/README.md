# 宠物与隐士防抓回归（pet-guard）

Run from the repository root:

```sh
C:/Users/ADMIN/dotnet8/dotnet.exe run -c Release --project tests/pet-guard/PetGuardTests.csproj
```

The fixture directly links `il2cpp/PatchRoles_PetGuard.cs` and `il2cpp/PatchRoles_Hermit.cs`, so the shared
switch (`ModConfig.Enabled && ModConfig.PetGuardEnabled`) is exercised for both receipt families against
managed Unity/game stubs. It does not establish native detour compatibility, actual callback ordering,
multiplayer synchronization, save-file round-trips, or in-game safety.

Coverage (26 cases):

- Exactly two long Droppable lifecycle hooks (OnEnable postfix / OnDisable prefix); no short getter detour;
  the hermit pair is unchanged.
- Non-dog droppables register no pet receipt; hermit droppables still protect through the shared switch.
- Dog `Anybody`/`EnemyOnly` → `Nobody` and exact original restoration (native original and general pickup untouched).
- Global switch off suppresses protection even with the pet-guard switch left on; null config entries are inert.
- Duplicate enables / pool generations keep one receipt, one write, and a per-generation original.
- External policy ownership is never overwritten; clients register but never read or write policy.
- Boat restore window: owned dog aboard an in-scope boat keeps native `Nobody` while the switch is off and
  restores after leaving the boat (Boat component ancestor and `BoatBody` tag); leaving the game layer
  retires the receipt without a restore.
- Recall: off→on edge and every new load/world generation rewrite `Stolen` dogs/hermits to
  `Roaming(CurrentLand)` and spawn through the native-shaped path (prefab choice, `SetupDog`, colour,
  kept player, cross-island land rewrite); dog0/dog2 independent; idempotent across ticks and generations.
- Live same-id dog / same-type hermit defers the recall without a status rewrite (bounded deferral log),
  then recovers once the instance is gone; non-stolen positions (incl. hermit `Passenger`) are never touched.
- Loading marks a stale generation and recall waits for playing; missing prerequisites (no prefab / no P1)
  defer without a partial rewrite; clients never recall; recall faults are contained with one bounded warning.

Current result: **not executed in the worker session** (execution tools were approval-blocked). The operator
must run this suite together with `tests/HermitPickupPolicyRegression` and the main build.
