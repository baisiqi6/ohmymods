# Greek follower fire dependency — 2026-09-11

The asset-level cause is identified: actual `Archer_norselands` and `Archer_Soldier_norselands` prefabs have a **null serialized `_fireArrowAttack`**, while the ordinary Archer has a valid base fire attack. The user's Greek-styled squad followers use the Norse Archer entity. Consequently the current ArcherReady chain fails at `fire == null`, before pool lookup. Existing cast logs aggregate all asset/pool failures, so a repaired-recipient log or detailed runtime sample remains the final confirmation for the particular running instances.

## Actual asset evidence

`inspect-fire-assets.py` uses installed UnityPy read-only and writes only this directory. It reads `resources.assets` metadata plus `globalgamemanagers` ResourceManager; it does not load `.resS` audio/textures or execute game code. Custom MonoBehaviour typetrees are stripped, so standard headers are read with `check_read=False`; explicitly identified PPtr/scalar payload fields are decoded according to the component type and checked offsets. Unrelated payload bytes are not interpreted.

Actual resources.assets SHA256: `B80394A7961425EA6CF7C55E7493FF648EA558FBC46EC575FEBF35569F2C195D`.

| Component/resource path ID | Name | ArrowAttack | FireArrowAttack |
|---:|---|---:|---:|
| 85879 | Archer_norselands | 84408 | **null (0,0)** |
| 85880 | Archer_Soldier_norselands | 84408 | **null (0,0)** |
| 85881 | Archer | 84410 | 84411 |

At payload offset 40 the two adjacent serialized PPtrs are ordinary and fire attack. The Norse payload is `00000000b849010000000000000000000000000000000000`, i.e. `(file0,path84408)` followed by `(file0,path0)`. The ordinary Archer instead points its second field to path84411.

Existing assets and pool collection:

- `Archer_OakAndBirch_FireArrowAttack` 84411 → Arrow component85158 → GO19691 `Fire Arrow`.
- `Archer_Norselands_FireArrowAttack` 84409 also exists → the same Fire Arrow GO19691, but is not assigned to either inspected Norse prefab.
- `GreeceSwapData` scriptable mapping is **84411 → 84407**, `Archer_Greece_FireArrowAttack`.
- Greek fire attack84407 → Arrow component85236 → GO19807 `Fire Sling Bullet`.
- Greek pool collection84732 includes Fire Sling Bullet pool100397, serialized `sync=true`, **syncID113**.
- Fire Arrow pool100400, syncID91, exists as a resource but is **not** in the Greek 90-pool collection.
- Greece prefab-swap entries contain no Fire Arrow19691 → Fire Sling Bullet19807 pair. The relevant swap is the ArrowAttack ScriptableObject, not that GameObject pair.

ResourceManager in actual globalgamemanagers object13 confirms exact load paths:

- `data/arrowdata/archer_oakandbirch_firearrowattack` → resources84411.
- `data/arrowdata/archer_greece_firearrowattack` → resources84407.
- `data/arrowdata/archer_norselands_firearrowattack` → resources84409.

The helper therefore searches only `Resources.LoadAll<ArrowAttack>("Data/ArrowData")`, matches the exact base asset name and rejects duplicate distinct matches. It does not enumerate all Resources during battle.

## Pool semantics and why registration is unnecessary

`GetPoolFromPrefabAsset` is a direct poolsByPrefab lookup. `GetPoolFromPrefabInstance` is an origin lookup for a spawned instance; it is not a fallback for an unmapped prefab asset. Actual 2.4 native disassembly confirms direct dictionary accesses to different static slots. `SpawnGO` calls the mapping path before poolsByPrefab lookup (call at VA0x1806C3D84, followed by lookup at VA0x1806C3DD1); the 2.1 source identifies that path as the biome asset swap. `pool-native.txt` contains bounded disassembly; SpawnGO RVA0x6C3C30, GetPoolFromPrefabAsset RVA0x6C1F10, GetPoolFromPrefabInstance RVA0x6C1FA0 are each unique within the Assembly-CSharp pointer table.

The Spawn-compatible readiness check must map the selected ArrowAttack's prefab through current biome before checking its pool. However, prefab mapping alone cannot cure a null FireArrowAttack. The appropriate fix is to restore the missing instance field to **the world's mapping of the original base fire SO**, and reuse the already-existing native pool. Do not pick the Norse fire SO just because the entity prefab is Norse: that SO points at a pool not present in Greek's collection.

No new pool, random syncID, registration through Castle, global asset patch or shared prefab mutation is needed for this verified chain. PoolManager resets native pools on world load, so the helper rechecks actual pool readiness each time it is called; it does not cache a pool or bypass a missing pool.

## Candidate helper and integration

`PatchRoles_GreekFireAssets.cs` supplies:

- `Ensure(Archer) : bool`: only active/living currently assigned **owner-style3** followers; missing `_fireArrowAttack` only. An existing non-null field is never replaced. Returns readiness of the actual mapped prefab pool, requiring `sync` online.
- `ResetWorld()`: call from existing world-load flow on both peers. It drops mapped-world state and retry/log counters while retaining a valid immutable base-asset reference. World/biome pointer changes also reset the mapped cache defensively.

Asset resolution is shared across followers; failed discovery retries no sooner than 30 scaled seconds. Logs are capped at three per world and successful repairs have a counter. All native membership/lifecycle checks are repeated before the single instance-field write. No Buffable activation, expiry mutation, ActiveArrowAttack replacement, scene scan, timer or new native hook is introduced.

Operator should call Ensure from the existing **both-peer** follower style assignment/patrol, using the owner's actual resolved style3 before any effective visual-style early return. Also call Ensure from GreekFire.ArcherReady after existing RPC/BuffReady validation. Replace the old raw-prefab-pool guard with Ensure's result: retaining that old duplicate guard could reject a correctly mapped prefab.

Both peers need the instance field: client native FireAttacks/guard serialization selects its own `_fireArrowAttack`. A host-only repair before casting is not a complete network solution. The existing style patrol offers bounded eventual readiness; the first client receipt before style/patrol repair should be part of the controlled validation, not assumed covered by the host's repair log.

Changing the field alone does not rewrite an already-active fire selection. That is deliberate scope: the observed defect prevented recipients from receiving FireAttacks at all. Existing non-null data, active attack selection and shared FireAttacks expiry remain untouched; no artifact duration can be shortened by this helper.

## Validation

Helper SHA256: `CBE8D1CD70D179D203E79DF9C3E915A7D5A8BEB777E9F64EC3A88537764452A2`.

`ApiCompile.csproj` compiled the exact helper against actual E-game Assembly-CSharp, UnityEngine.CoreModule, Il2Cppmscorlib and Il2CppInterop.Runtime DLLs: **0 warnings, 0 errors**, recorded in `api-build.txt`. Only surrounding mod logger/config/style-query symbols are compile context; actual game/Unity APIs are not stubbed. This is an API compile, not runtime or gameplay proof.

Actual interop APIs verified public: Archer `_fireArrowAttack` getter/setter; ArrowAttack `_arrowPrefab`; BiomeData.Current and `GetAssetSwapForThis<T>`; Pool.GetPoolFromPrefabAsset(GameObject) and sync; Managers.world. No running game was operated, injected, restarted or modified. No canonical source, configuration, save or deployed binary was written.

Operator validation should confirm repairs on both peers, expected resource name/mapped prefab/native pool113 in Greek, cast recipients greater than1 when valid followers are present, actual fire projectile use and expiration/longer-artifact preservation, then world-load/pool rebuild readiness. The helper intentionally returns false instead of constructing a new pool when the native pool is genuinely unavailable.
