# Issue #234 当前交付阶段（2026-10-15 用户授权）

原G1技术契约附在下方，标题/尚未授权等文字是当时阶段的历史，不作为现在的拒绝合并/安装理由。本轮继续原operation/owner/session/branch，不重建task；原G1 source head6ffe33d29454a3aaca5aa10bcab8e15bd2a33451。

## 交付范围

同侧1名随旗、40候选耐久、脱战6游戏秒回盾具店免费由原店匠修8游戏秒、v1/v2/v3比例迁移v4。整合先前201/202/207/222/226当前release完成后的真实hunk，保留233/236及既有功能；236已合并仅核其修复纳入安装。先226后237，保留stock生命周期与repair只读工位之间责任。Android只加5个所需纯策略/repair/banner/migration依赖源，不注册新增两个native banner observer、不安装产品。Integration旧host补真实新增3接口fixture，实际跑新整合源。

正常Mac累计候选以已安装911b源/资源组成作为基线，禁止旧237整文件覆盖NativeKeyReader EqualsCurrent/Bind/Release、Palace InstallOnce/Private源与PNG及其它累计功能。按最终实际新源码构成逐hunk/成员验证，233拆分若接入必须Hooks/main/ground成组，不能产生双定义。双TFM真实refs build、实际identity/codec/runtime/shop/Integration保存producer等交叉回归及独立候选审查；原39资源保全或明确审计增量。版本账据最近正式版和当前累计条目审计，最终标识由exact install gate钉，不擅改major、不必发布tag。

用户明确授权merge后安装正常版供玩家实测。本轮不启动游戏；安装前fresh0相关进程、当前输入指纹、鲜备份与恢复能力，精确candidate gate后只改DLL/SUMS及必要版本元数据，保存/config/prefs/game/log保护回读。v4升级后回退需相应存档备份，不能仅换回旧DLL宣称兼容。既有工件Native验收未发生仍pending。

本任务PR237实际合入release且必要代码验证与独审完成后，正常关闭GitHub Issue与受管代码任务。安装与玩家/native各平台结果另记，不把未测标志改true，不等待玩家无限期悬挂；后测问题另Issue/PR。

## 本轮共同行为与交付边界

用户2026-10-15明确授权：已实装PR经必要审查验证合并收尾，236/237检查后合并安装正常版，由用户后续实测；新问题另建Issue/PR。源码审查/编译/host、合并、安装、玩家实机各自留证。未发生的native/Windows/联机不记通过，不再把玩家后测当作代码Issue关闭前置。所有本任务PR进release/v9.5.13且必要代码验证/独审通过后正常关GitHub Issue与受管代码任务。

初始release基准5d931e0fa1700879e41620cf8d48cf2719012bc8；因依赖PR按序合并，每次操作前冻结真实head/base及差异，新增交集重新验证，不把旧绿色结果外推。产品/测试修改仅本任务必要hunk，其他来源/私有累计内容保留。阶段infra phase=ready，owner=mac-codex-ohmymods-main-operator；normal CLI管理task/plan/assignment/closeout，保留foreign rows/order/plans/leases，不裸改JSON/DB或force。原历史状态/失败/验收边界保留，当前记录明确是首次登记或当前阶段修订。

所有构建关闭自动部署(BepInExPluginsPath为空)，真实依赖与source pins留本机。正常Mac安装由issue234的累计候选最终关口统一执行，当前任务不独立覆盖正常DLL；不安装Android、不启动游戏，不改存档/config/prefs/Steam。Android232已交接所列Compile/精准pin/消费兼容hunk供Main，其他产品入口/R诊断/状态不触碰；其native执行不纳入本任务。

## 审查与完成标准

1. 修在错误产生处，状态写入责任清楚；不补fake产品或放宽原断言凑绿。
2. 按实际source/test/define/ref变化执行必要回归及全源接口构建；旧baseline已有失败保留并对照，不冒称全部suite通过。
3. 独立审查所有新集成交集；实质决策按GLM5.3/max，具体merge按最终head/base gate。
4. 正常merge commit保留祖先；整批关联PR未齐不得关本Issue。player/native待验原样附，后续发现另Issue/PR。
5. closeout packet逐任务串行独审/准确摘要/正常receipt消费，doctor/validator通过，释放本任务lease并保全外项。

---

## 保留的历史G1技术契约

# Issue234 G1 adapter contract — proposal for review

Base: clean 9cc51211165901abc705914feaea2c4f8df2df05 in /Users/Admin/.codex/worktrees/shield-banner-repair-20261015/ohmymods. G0 only has been approved; this document proposes G1, not merge/install/native execution.

User decisions remain one same-side escort, 40 candidate durability, free shop-smith repair after 6 safe game seconds and 8 actual work seconds. All queued/travelling/working soldiers remain excluded from a new banner, as previously approved. Broken soldiers keep existing retirement. No asset edits, Builder recruitment, currency changes or field regeneration.

## Ownership and dispatch

- Banner worker only creates il2cpp/Patch_HeavyShieldBanner.cs plus focused tests if a meaningful pure production seam is needed. Root integrates existing owners. New Harmony observers on already statically verified unique Player.ActivateFormation and Formation.OnDisable methods; no Deactivate detour, no native array writes. Existing FleetBoatFormation remains the sole layout writer; current heavy-shield combat/demote detours unchanged.
- Repair worker only creates il2cpp/Patch_HeavyShieldRepair.cs, may extend its owned G0 Patch_HeavyShieldMaintenancePolicy.cs and actual-source maintenance tests for explicit settled-result cleanup. Root integrates Runtime/Identity/SaveData/BlockPolicy/ShopShell/Integration/Combat, compile lists and descriptions. No worker edits existing unrelated owners.
- Both workers read current 2.4 metadata/disassembly in reviewer-native; interop assemblies only prove API signatures, not original C# implementations. No running game, copying DLLs, saves/config or canonical writes.

## Shared types and APIs (Root defines types in HeavyShieldContracts.cs)

```csharp
internal readonly record struct HeavyShieldBannerOrder(float X, int Facing, float Speed, bool Hold);
internal readonly record struct HeavyShieldRepairSite(long World, long Life, IntPtr Root, float X, float Y);
internal readonly record struct HeavyShieldRepairOrder(float X, int Facing, bool Working);
// Existing Entry additions by Root: bool ThreatKnown, Threatened; double LastThreatAt.
// Runtime owns Entry and the only Mover/pose/facing writes.
// Root provides IsMaintenanceEligible(Entry): exact live identity/visual/native ownership, independent of pause.
// Root provides TryCompleteRepair(in completion,in site,out bool uncertain): complete current proofs then commit.
```

### Banner leaf

```csharp
internal static class HeavyShieldBanner
{
  internal static void Tick(in HeavyShieldCampaignToken campaign, long world,
    IReadOnlyList<HeavyShieldBannerCandidate> candidates, bool selecting);
  internal static bool IsAssigned(Guid receipt,long life);
  internal static bool TryGetOrder(Guid receipt,long life,out HeavyShieldBannerOrder order);
  internal static void Reset();
}
```

Observers privately capture successful actual Activate return (__runOriginal) and Formation.OnDisable. Capture only offline enabled main-world local Player/PlayerFormation with finite x, native side -1/+1, exact campaign token/world, valid actual activation. Keep at most two player bindings and monotonically increasing activation counter; do not reuse tokens on Reset. Same formation reactivation consumes a fresh token and ends prior binding. Pending selection processes once on next eligible Tick; no selection while paused/saving. With no eligible soldier, consume the activation with default chosen; never refill this activation. Exclude soldiers already bound by another local player. If outer world/campaign differs, discard old bindings and native references.

For every current binding validate original Player + formation reference/active/side/world and selected receipt/life. Call G0 IsCurrent only with that selected receipt/life, never loop all candidates through its mutating check. On loss, end binding without freeing same activation for refill. Candidate Eligible is for initial selection; don't invalidate assigned soldier solely because repairing eligibility excludes InBanner. Source roster still proves current receipt/life exists; invalid native carrier must end binding.

GetFormationPosition(index,null) yields world x in current ARM64. Choose highest currently alive Pikemen index (actual unitTypes/units, GetGO active, Pikeman damageable alive and its GetFormation matches); otherwise highest current front occupied slot, or last legitimate slot if empty. Validate arrays equal length, bounded <=128, enum/spacing index readability. Check HasActiveDirectedTransaction and HasDirtyRowTypes before touching temporary layouts. During a dirty layout return Hold at current assigned soldier position (Root supplies hold behavior; X may be0 if Hold=true), retaining binding; no guessed coordinates. Restrict final candidate with formation.RestrictPosition(frontX+side*.4f), then finite check. Travel speed use finite positive formation.overrideMoveSpeed when applicable else soldier walk speed (order Speed=0 tells Root to use walk speed). Root applies order only after exact carrier proof. Native read failure ends that binding once with bounded diagnostic, not global retry. Facing remains locked native side. No global scene scans or native recruitment.

### Repair leaf

```csharp
internal static class HeavyShieldRepair
{
  internal static void Tick(long world,IReadOnlyList<HeavyShieldRuntime.Entry> entries,bool advancing);
  internal static bool IsQueued(Guid receipt,long life);
  internal static bool TryGetOrder(in HeavyShieldCareerHandle career,out HeavyShieldRepairOrder order);
  internal static bool IsCompleting(in HeavyShieldRepairCompletion completion);
  internal static void Reset();
}
// ShopShell.TryGetRepairSite(out HeavyShieldRepairSite site): read-only; Root supplies.
// MaintenancePolicy.ConfirmSettled(Guid receipt,long life,int durability): remove matching accepted fence only for exact life + full40; do not clear based on absent rows.
```

Use one policy instance, reused bounded lists of at most4 entries, no second identity ledger. Build actual subjects from Root IsMaintenanceEligible, policy durability, banner assignment, latest threat facts. Unknown threat counts unsafe. AtShop requires finite actual x/y within .20 horizontal/.50 vertical of site. Site must be current shop object/world/life/header, valid sprites and intact territorial footprint, not clearing/retiring/economyfault, including during pause (pause itself is not site invalidation). One repair point near existing smith (Root sets shop x+.55, visual calibration pending); actual existing merchant sprite animation continues, no fake additional craftsman/held-tool asset.

Tick once after Runtime refreshes local enemy scans for all valid entries. advancing false for pause/save/load; full subject/site facts still observed so genuine threat/relocation cancels, but elapsed work freezes and no grant. Policy World/shop invalidation handled as G0; true campaign/world/owner teardown calls Reset, not ordinary inactive-frame retry. Jobs and banner assignments never persisted. Autosave pauses work, does not silently disband banner.

Take completion only after policy latest current facts qualify. Set adapter's exact current completion before calling Runtime.TryCompleteRepair; IsCompleting matches every value and only during this synchronous call. In finally ResolveCompletion once (unknown if unexpected exception after possibly committing), clear completing proof. If accepted, fresh read Runtime policy + Identity claim both exactly full40/current life, then ConfirmSettled. Missing/mismatch must not guess accepted cleanup; hold/report and require owner reset. This closes G0 historical accepted receipt retention in the normal path; no absence-driven pruning.

TryGetOrder only outputs the active travelling/repairing/completion-due exact career and current site; others guard at home. Travelling faces towards the shop; working faces smith. Interruption returns to normal guard, not forced commands across world. No retries of uncertain completion.

## Root integration

1. Runtime Tick: capture bounded owned current roster; validate exact identity/native roots; refresh existing local scanner before repair decisions (all enemy types for threat, native scanner radius unchanged). Per-entry scan-known failure blocks repair; scan cap over32 means threat-unknown. Track LastThreatAt on any nearby live Enemy and any positive incoming Damageable request for exact shield career, not only successful shield block. This observer must not change damage result/old block semantics. Use scaled Time.time; new attachment starts LastThreatAt=now.
2. Context owns one campaign token/world. Reset adapters on genuine teardown/change, not pause, autosave, missing temporary availability. Tick Banner before Repair then Drive. Initial selection excludes repair IsQueued. Dead/broken/unknown/demoting cannot be selected or repaired. Invalid current carrier ends banner; existing retirement remains authority.
3. Home side is Career.Side, never mutable Facing. Repair can face inward without changing home guard anchor/seat. Move and visual SetFacing remain Runtime-only. Drive chooses repair travel/work (safe only), else banner goal, else home guard; combat can interrupt work, existing bash/block poses retained. Guarding banner movement uses actual-motion gait.
4. TryCompleteRepair validates current completion proof, world/site/root/life, fresh finite arrival, advancing/no native damage stack/no save/load, current entry and claim exact expected durability, no pendingbreak/unknown/exit/threat/new threat timestamp, mature policy already proven. Then Identity.TryCompleteRepair(handle,completion,policy) rechecks native identity before any managed write and all expected values. Policy.TryRepair(expected) is pure and succeeds only guarding, no bash, not unknown, exact durability1..39. Once it succeeds, assign claim.Combat=(40,false,false) immediately with no native/allocating call between writes. General UpdateCombatState still monotonic. Wear refresh happens only after both states agree; visual exception never retries repair.
5. Save v4: introduce FingerprintVersion=3 separate from latestVersion4. Validate source-v1 range0..3, sourcev2/3 range0..4, current0..40 before migration. Preserve hashKind3+ required; legacy1/2 only legacykind. Scale every stored claim durability (including quarantined) once; for migration broken argument derives durability==0, preserving PendingBreak/RetirementUnknown verbatim rather than resurrecting/silently sanitizing historic quarantine. Runtime still refuses inconsistent live states. Do not alter native key logic. Add v1/2/3/4 whole-codec regression vectors including v3 canonical hash, missing/duplicate hashKind, old out-of-range, broken/unknown and repeat roundtrip.
6. ShopShell read-only site checks its existing object/_layer/_shopLife/renderer/header against current world/kingdom/intact borders; no movement/payment/inventory writes. Full footprint validation blocks obsolete outside-territory site before PR226 cleanup next tick. Update shop Tick before Runtime Tick so retirement is observed promptly. No dependency on unmerged PR226 to implement this read-only site check; later integration preserves its inventory/lighting hunks.
7. Focused tests actual policies/codec + genuine interop full builds net8/net6 with autodeploy disabled. Independent implementation review. Preserve native pending facts; merge/install requires separate exact-candidate gate.

## Parallel coordination decisions requested with this phase

- PR226 read-only merge audit found only progress.md conflict against PR233 base6ca5dc2, no new confirmed product defect. Permit preparing an owned integration branch/worktree to reconcile that conflict, preserve both saving roles/compile items, rerun affected five host suites and full net8/net6 builds, then independent review. This is NOT approval to merge/install; final exact-head gate remains. Historical native pending claims stay pending. User already requested normal installs and continuing coordination, but has not reported actual restock pickup outcome.
- Android232 discovered a verified prepublication SaveAsync observation leak (R sibling android-profession-archery-20261014/g1-save-abi-contract/report.md). Delegate only unpublished-wrapper conversion-failure cancellation + pure lifecycle tests on a separate peer-owned branch: original callback remains unchanged; release only this unpublished observation/Managed closure, never published/pending unrelated callbacks; no replay/timeout/second ledger; preserve native ABI pending. This grants source ownership for that branch only, no merge/install, no broad rewrite of save core. Main234 does not touch Patch_MusketeerNativeSave.cs. Send one consolidated ownership/blocker message, not repeated ACKs.

## G1 preflight amendments (required before implementation)

- Campaign owner lineage compares Guid/Global/Campaign/OwnerGeneration plus world; do not compare Phase in token equality. Native save legitimately changes Phase to Staged and must not disband banner or Reset repair. Phase is checked for availability only.
- Add read-only HeavyShieldPersistence.SerializationInProgress covering _save/_load/_create/_prepare (the entire capture scopes), and HeavyShieldIntegration.SerializationInProgress covering its outer _save/_load plus persistence. Combine with IslandSaveData.isSavingGame in Runtime advancement/commit gate. Existing ShieldLoadInProgress is only per-row mask, not enough. Do not change native key reader or serializer ownership.
- Completion ordering clarified: Runtime performs both durable managed writes and marks a pending wear refresh, but makes no visual/native call after successful writes. Adapter obtains exact current full40 policy+claim proof BEFORE ResolveCompletion. If any post-commit uncertainty, resolve uncertain and retain barrier. Otherwise ResolveCompletion(accepted) immediately followed by ConfirmSettled exact receipt/life/full40 with no native call in between. Runtime refreshes visual wear after the adapter settles. A visual failure does not change accepted durability or trigger replay.
- All policy flags PendingBreak/RetirementUnknown/BashActive are checked before repair. Root exposes readonly fresh completion-state check if needed; it must validate exact life/receipt and both current durable values. ConfirmSettled is not a general absence-driven cache purge.

## Approved implementation checks (GLM5.3/max R2 and independent preflight)

- Repair threat observations cover both sides regardless of mutable Facing; home-side anchor/seat and outward combat fallback use Career.Side. A maintenance inward-facing turn must never hide threats. The reviewer wording "home-side threat direction" is implemented more conservatively as undirected repair threat detection, retaining home-side combat semantics separately.
- A scanner result above32 is unknown for repair; combat may retain its preexisting capped scan. Hold means actual current x at speed0, never the placeholder X=0.
- Integration.EndPrepare wraps the full existing Persistence.EndNativePrefsPrepare with a try/finally finalizing-depth gate because the existing persistence scope pops before native readback. This is only a narrow outer mask; original persistence/key/readback order stays intact. Serialization gate combines the outer save/load/finalizing scopes, persistence _save/_load/_create/_prepare and IslandSaveData.isSavingGame.
- Root IsMaintenanceEligible additionally requires the exact current session KeyReady, !Unknown and !StageFault; ValidateCareer alone does not prove those.
- ConfirmSettled remains the simple exact receipt/life/full40 API. It is called synchronously only after current completion and two-state full-value proof; never queue it for later callbacks. Tests cover stale/mismatched/full-value confirmation. No post-native interleaving before Resolve accepted and cleanup.
- Partial-write uncertainty freezes the affected entry's future repair and ordinary combat persistence, rather than silently allowing downward writes to hide divergence. No detach/reset to release an uncertain transaction. True owner/world teardown still releases transient references.
- Approved G1 source implementation only. G2 original/native execution, merge, install and task completion remain separate decisions. No gaming environment is launched or changed by this plan revision.
