# Greek knight FireAttacks: implementation API contract

Scope: style 3 knight plus its currently assigned living Archer followers; combat-triggered 8 seconds, 15-second cooldown. This is research only: no canonical/game/config/save mutation, deployment or game execution.

## Duration and original asset

Create a separate native Unity clone of an original, registered FireAttacks BuffData. Keep its original ID, BuffType and visuals; change **only the clone's EffectDuration to 8**. Do not mutate the original or insert the clone into BuffDataStorage.

Actual E-game 2.4 `UnityEngine.Object.Instantiate(UnityEngine.Object)` is public, and its wrapper has a normal `il2cpp_runtime_invoke` body, not an UnstripException stub. `Instantiate<T>(T)` is also present. A conservative form is `UnityEngine.Object.Instantiate((UnityEngine.Object)original).Cast<BuffData>()`. BuffData ID, BuffType, EffectDuration and OverlayGradient getters/setters are public in actual interop.

Asset discovery: after the world/resources are ready, `BuffDataStorage.Init()` loads `Resources.LoadAll<BuffData>("Data/BuffData")` into `BuffDataStorage.buffStorage`, keyed by each original ID. Actual interop exposes `Init`, `GetBuffData(int)`, `buffStorage : Il2CppSystem.Collections.Generic.Dictionary<int,BuffData>` and `initialized`. Enumerate registered values once, filter `BuffType.FireAttacks`, and validate the chosen asset through `GetBuffData(asset.ID)`. An accessible original `HephaestusHammer._anvilPrefab._buffData` is an even more exact identity reference. No numeric FireAttacks asset ID was read from actual serialized resources in this audit: **do not hardcode a guessed ID or assume the enum value 1 is the asset ID**. If multiple original FireAttacks assets exist, report bounded names/IDs and choose deliberately; do not depend on dictionary iteration order.

Never activate the original long-duration asset and then shorten its expiry to 8: the reference native coroutine waits for the first supplied duration, so it could wake later than 8. The first activation must use the 8-second clone.

### Existing enchantment: accepted narrow alternatives

Actual `Buffable._activeBuffsExpirations` is a public getter returning `Dictionary<BuffType,float>`. The original reference algorithm indexes it by BuffType and sets an absolute **scaled `Time.time`** expiry. Reapplying an already-active type refreshes its expiry without another receiver activation/stack.

The worker contract's approach is valid against that reference: read old FireAttacks expiry; if `old >= now + 8`, skip completely; otherwise call `Buffable.ActivateBuff(clone8)`. Because the clone duration is 8 and the old shorter expiry was read on the Unity main thread, this does not shorten a longer enchantment. Retain the `_applicableBuffs.Contains(FireAttacks)` check and receiver readiness checks.

An even smaller active-buff mutation is to set only `expirations[FireAttacks] = max(old, now+8)` when the entry already exists, and use `ActivateBuff(clone8)` only when inactive. It avoids all native refresh side effects, but couples more directly to the internal expiry dictionary and relies on the existing expiry coroutine still running. Reference FireAttacks visuals are indefinite until Deactivate RPC, so extending an existing host expiry needs no repeated Activate RPC. **Do not insert a new expiry entry yourself when inactive**, which would bypass receiver activation and coroutine setup.

2.4 has `Buffable.OnDisable()` and `TryDeactivateBuff(BuffType)` in addition to the 2.1 reference API. Therefore method/field presence is verified for 2.4, while the detailed native refresh/expiry behavior is inferred from the reference and still needs a controlled runtime test. For minimizing direct-internal mutation, retaining the worker's guarded native Activate path is reasonable; do not change it merely to match this optional alternative.

Neither approach globally prevents a later vanilla source from changing the same type's expiry; the guarantee here is that **this skill's own application does not shorten an already longer buff**. Do not mutate the global asset or introduce a global Buffable detour to enforce cross-source policy.

## Actual 2.4 target APIs

- `Archer._shootingTarget : UnityEngine.GameObject` (not Damageable).
- `Archer._meleeTarget : Damageable`.
- `Archer.ActiveArrowAttack : ArrowAttack` and `ArrowAttack.Range : float` are public.
- `Archer.shootRange : float` also exists, but reference shooting selection checks the projectile's **ActiveArrowAttack.Range**. Range computes ballistic range from the attack's shot magnitude/gravity; do not substitute a guessed fixed radius.
- `Knight._enemy : Damageable`, `Knight._enemyScanner : Scanner`, `_slashRange`, `_awarenessRange`, `_harmless`, `_cooldown`, `_character`, `_damageable` and `ShouldPlayerControl()` exist.
- `Scanner.GetClosest()` returns GameObject; another overload is `bool GetClosest(out GameObject)`.
- `Damageable.IsDamagedBy(DamageSource)` and `isDead` exist.

The agreed periodic Update detector (0.25 seconds) must cover the knight **or an assigned Archer's active ranged target**, so it can trigger while the knight stands behind a wall and followers are shooting. A ShouldSlash-only trigger would miss that situation. For the Archer path: require active living Archer, current `_knight.Pointer == knight.Pointer`, non-null active `_shootingTarget`, target Damageable that is enabled/alive and accepts Arrow damage, and horizontal distance from that Archer within its current ActiveArrowAttack.Range. This mirrors the relevant reference range check and does not require new enemy scene scans. Consider an explicit enemy-layer/hostile-target check to avoid treating wildlife, stale targets or scripted non-combat target objects as combat. The reference `_shootingTarget` is also used for hunting and a ruler-statue target, so non-null alone is insufficient.

Native ShouldSlash is useful as reference evidence, not sufficient as the sole trigger for this contract: it checks harmless/cooldown/pusher, updates `_enemy`, then checks current attack range/prediction. `OnAnimSlash` only plays a sound in the reference, so it is not reliable battle-target evidence. Periodic polling must not call ShouldSlash purely as a getter because it changes `_enemy` and would invoke existing Harmony hooks.

For selecting recipients, use the existing shared `UnitScanCache.GetArchers()` default 3-second resource scan; re-check live `_knight` ownership and active/living status on each cast. Do not request `.25f` as the shared cache maxAge merely because this consumer polls every .25s. The cache may omit a newly spawned follower until its next shared refresh; that is a defined freshness limit, not permission to scan the scene every tick.

## Buff/network readiness

Actual public 2.4 APIs:

- `Knight.Buffable`, `Archer.Buffable`; `Buffable._owner`, `_applicableBuffs`, `_activeBuffsExpirations`, `IsBuffActive`, `ActivateBuff`, `DeactivateBuff`, `TryDeactivateBuff`.
- Knight: `parentHeaderRef : CRPCHeader`, `_knightBuffIndex : int`, `_knightBuffEndIndex : int`.
- Archer: `parentHeaderRef : CRPCHeader`, `_archerBuffIndex : int`, `_archerBuffEndIndex : int`.
- Both expose `BeginRegisteringRPCs(CRPCHeader)`.

Require world authority, config/style eligibility, a living active receiver, non-null initialized Buffable owner, FireAttacks applicability, **parentHeaderRef != null and both buff RPC indices >= 0** before first activating each receiver. Do not send these buffs from Awake/OnEnable: reference Awake initializes Buffable, while RPC indices are assigned later by BeginRegisteringRPCs. Re-check per target because followers may be spawned/reused independently. Never call BeginRegisteringRPCs yourself to force readiness.

Reference receiver ActivateBuff updates local FireAttacks behavior, activates visuals and sends the original BuffData.ID. Client RecvBuffKnight/RecvBuffArcher retrieves original `BuffDataStorage.GetBuffData(id)` for visuals. The cloned host asset retaining the original valid ID therefore preserves the lookup; FireAttacks indefinite glow is later ended by the ordinary BuffType end RPC. Knight's fire bool and Archer's fire-arrow state also participate in native serialization. Do not call receiver `Knight.ActivateBuff` directly instead of `Knight.Buffable.ActivateBuff`, because that bypasses expiry bookkeeping.

## Fire-arrow dependency and pool guard

Actual public 2.4 APIs:

- `Archer._fireArrowAttack : ArrowAttack`.
- `ArrowAttack._arrowPrefab : Arrow`.
- `Pool.GetPoolFromPrefabAsset(UnityEngine.GameObject) : Pool` (one GameObject argument; do not pass Arrow directly).
- `Pool.sync : bool`, `Pool.syncID : short`.

Before giving an Archer new FireAttacks, verify `_fireArrowAttack`, its `_arrowPrefab`, and `Pool.GetPoolFromPrefabAsset(fireAttack._arrowPrefab.gameObject)` are non-null. If relying on the native synchronized projectile path, require a synchronized pool (`sync`) and record its actual syncID; do not assume every non-null pool has the client counterpart. No public Pool header property was found; the receiver's CRPC header/index readiness is a separate check. Native `ArrowAttack.FireArrowInternal` spawns through Pool.Spawn and, when world-authoritative and the client has caught up, sends initial velocity through NetworkSoftSimulator. **The last true argument in this Spawn overload is assertNonNullPrefab, not a synchronization flag**; pool.sync is the separate synchronization setting.

Canonical `PatchRoles_Castle.EnsurePoolForCharacter` registers only the character prefab, **not a recursive projectile dependency tree**. ReRegisterModPools covers existing Ninja/Berserker/Worker/Ghost/Norse-character integrations. This audit found no explicit general registration for `_fireArrowAttack._arrowPrefab`. The normal Greek game is expected to have its original fire-arrow pool for Hephaestus, but that expectation is not runtime proof; changing a follower's animator to Greek skin also does not prove its underlying fire-arrow prefab/pool changed. The safe bounded behavior is to skip an unready Archer and log one diagnostic, without registering a new pool in the cast path.

Knight FireAttacks adds native delayed Fire damage during Slash; Archer FireAttacks selects its own `_fireArrowAttack`. Do not add a second damage modifier or apply the Samurai hit helper. Preserve the user's Greek-only scope.

## Verification still required

Controlled validation should record the selected original asset name/ID/BuffType/EffectDuration, clone duration 8, original unchanged, each recipient's original/new expiry and RPC readiness, and actual fire-arrow prefab/pool syncID. Verify inactive activation ends around 8 scaled seconds, a longer original enchantment remains longer, a shorter active buff is extended, repeated applications do not stack, host/client visuals/end align, and native pool/OnDisable cleanup handles reuse. Nothing in this research claims those live outcomes have already passed.

Reproducible metadata evidence: `inspect-apis.ps1` produced `actual-interop-apis.json` and `.txt`, containing 82 bounded method records and actual interop SHA256 hashes. This includes the public Dictionary Values/indexer/TryGetValue APIs and the non-generic Instantiate wrapper IL. The script executes only Cecil's metadata parser, never the game assemblies.
