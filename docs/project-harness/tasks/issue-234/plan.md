# Issue #234 — 盾卫举旗前排与店内维修实现计划

> For agentic workers: use subagent-driven-development / executing-plans within the exact assigned files. Operator owns shared integration and review.

**Goal:** 两金币举旗仅带同侧一名盾卫站在长矛前方；新盾耐久40，脱战后回盾具店由店内铁匠免费花时间修补。
**Architecture:** HeavyShieldRuntime保留移动、朝向、动作和职业生命控制权；新Banner adapter只提供经过当前原生PlayerFormation校验的随行锚点。Repair controller独占单店队列/行程/工时证明，只有显式完成入口可提高耐久。
**Tech Stack:** IL2CPP2.4.0、C#/.NET8、BepInEx6/Il2CppInterop/HarmonyX；Mac玩家兼容net6候选另验证。

## Authority and scope

- GitHub https://github.com/baisiqi6/ohmymods/issues/234；Mac Max/Codex maintainer；基线 release/v9.5.13@6ca5dc24c24eceb2beb5dd7893c65bbacf33d2cc；独立branch codex/shield-banner-repair-20261015。
- 用户已选同侧1名（覆盖先前2名），免费、店内铁匠、不占建造工人。40/脱战6秒/到店工时8秒是Operator初轮候选，不宣称平衡实测通过。
- Android232已交接盾卫玩法核心文件给本任务，仅保留移动端注册/配置/资源/ABI适配责任。新save schema/公开接口在PC合入前集中交接一次；不向对方发送普通进度ACK。
- PR226库存/照明仍Draft，正常版含其私有累积候选。本任务只新增玩法，不覆盖库存hunks或NativeKeyReader等累积实现。当前原版/正常Mod/存档/配置不写入、不启动。候选纯编译必须禁部署。
- Root历史Mono冻结；root七个既有未拥有改动不触碰。canonical通过受管正常CLI，保护全部其他条目/plan/lease；不raw JSON/DB，不force/reconcile。

## Accepted gameplay contract

1. 每次成功举旗只选同侧1名已购、身份/生命和战斗就绪、不在维修队列/行程/工时中的盾卫，离本次旗位置最近优先，同距按职业receipt稳定排序。没有人则空缺，不额外收费、不造人、不抢原生其它兵种席位。该次失员不递补；下次举旗重新选。
2. 随行仍由盾卫controller驱动。读取当前有效PlayerFormation最高存活Pikemen索引的GetFormationPosition世界目标，再向锁定侧外偏约0.4；无存活长矛则读前沿槽位目标。实际数组索引不可写死，不用GetXPosForIndex相对值代替world x，不按角色瞬时x反复换前排。约0.4/边界/时序须实机校准。
3. 成功ActivateFormation返回后校验当前active/side/world再bind；Deactivate不清引用，因此inactive/侧变化/world或身份失配/离岛也必须退出。再次举旗同实例先释放旧binding。收旗生存者回原守位；碎盾/死亡先退临时binding再执行既有Retire/native exit，不用Detach抢跑打断退职。无登船/跨岛运输。
4. 非随旗、未碎且损耗1..39、连续6游戏秒安全才入维修队列。FIFO，同帧receipt稳定顺序；一次修1名，其余继续守位等待。附近敌人（不只普通小怪）或新受击重置安全窗口。复用现有局部enemy scanner，不加全场扫描。
5. 只有到有效盾具店维修点、身份/world/shop life一致、持续安全且8游戏秒工时完成才能修满。暂停不积时；受扰/店撤收迁址/领地失效/离岛取消工时，同world活人回守位，离场不跨world发指令。下次重新完整计时。不改金币，无Builder岗位、假工具或新增士兵。店匠动作使用现有素材前先证实可用。
6. 修满仅明确完成入口；通常UpdateCombatState仍拒绝上升。先验证原来的耐久、职业生命、repair job、shop life和工时证明，再提交对应同一生命；重复完成不得重放，失败不假装已修复。
7. v1旧上限3、v2/v3旧上限4，新v4上限40。先完整旧格式验证，再换算正剩余耐久ceil(old*40/oldMax)；0/broken不复活，权益/receipt/native id/hash-kind不变。视觉磨损按40/30/20/10四档比例。维修与随行进度不落盘，重载恢复职业/磨损后回守位再走新流程。

## Stage G0 — pure policies and compatibility

Files (worker new leaves/tests only):
- il2cpp/Patch_HeavyShieldDurabilityMigration.cs：已发布旧版本耐久比例转换及磨损档；不操作全局状态、文件或原生。
- il2cpp/Patch_HeavyShieldMaintenancePolicy.cs：以传入缩放游戏时间和资格/到店/安全/shop-life事实推进Idle/Queued/Travelling/Repairing/CompletionDue；不自己修复职业。
- il2cpp/Patch_HeavyShieldBannerPolicy.cs：同侧单人确定性选择、一次举旗life binding、active/side/world/职业失配终止；不写原生Formation数组。
- tests/heavy-shield-maintenance-policy/ 与 tests/heavy-shield-banner-policy/：运行实际生产叶子，不写同实现替身。

Exact G0 signatures and required results are fixed below. Inputs are plain value snapshots and outputs are decisions; policies cannot prove native identity or call Unity/Git/files.

- [ ] Write failing cases: opposite-side exclusion, only1 selected from2, deterministic tie, stale activation/side, sameFormation reactivation, no-refill on death, all-repairing means no escort.
- [ ] Write failing cases: before6s no queue; travel is not repair; worker-free queue is FIFO; before8s no completion; paused/nonplaying and oldshoplife cannot complete; threat and relocation abort; repeated completion refuses; durability-zero never repairs.
- [ ] Write version cases: v1 1/3→14,2/3→27,3/3→40; v2/v3 1/4→10,2/4→20,3/4→30,4/4→40; 0 remainsbroken; invalid version/out-of-range rejected; repeat currentv4 no multiplication.
- [ ] Implement minimal pure leaves and run host suites under originalTFM, then independent review. No native/runtime acceptance from these tests.

## Stage G1 — current2.4 adapter and owner integration

Files: new Patch_HeavyShieldBanner.cs, Patch_HeavyShieldRepair.cs; Operator scoped edits HeavyShieldRuntime.cs, HeavyShieldBlockPolicy.cs, HeavyShieldIdentity.cs, HeavyShieldSaveData.cs, HeavyShieldShopShell.cs, HeavyShieldIntegration.cs, HeavyShieldContracts.cs, relevant project/test compile lists, ModConfig help text if descriptions contain obsolete durability. Main registration only by Operator.

- [ ] Verify Activate/Deactivate native address non-folding and registration path against current2.4; keep existing 5combat+Demote hooks intact.
- [ ] Runtime exposes bounded current4-career snapshots to policies; refresh existing local threat facts before deciding repair completion; keep a single Mover/pose/facing writer.
- [ ] Banner reads validated active/player/world/current array and restricted-world target; native array remains owned by FleetBoatFormation. Exact offset clamp/native invocation and one-frame-order constraints need current signatures before code.
- [ ] ShopShell exposes read-only repair site with root/world/shoplife and ground/renderer reference; old private parent identity and Clear invalidate all requests. Repair display cannot become a DroppableTool or paid inventory entry.
- [ ] Add explicit repair completion mutation with expected-before and mature work proof; policy/identity updates remain coherent. Serialize only durable wear, not jobs.
- [ ] Update codec validation/version migration and production callsites; preserve native key checks and PR226 stock cleanup hunks when later building cumulative candidate.
- [ ] Add real-source integration assertions and currentinterop compile checks; review unload/failure/reentry, saved identities, prefabs/pools and rollback boundaries.

## Stage G2 — verification and delivery

- [ ] Run focused host suites for durability, save schema, identity, shop runtime, banner layout coexistence, pose/gait/repair decisions; no broad repeated tests without a new reason. Existing standalone native-key harness gaps must stay disclosed, not patched with fake success.
- [ ] Build net8 with BepInExPluginsPath disabled plus Macnet6 actualreferences, zero unintended runtime copies. Compare allowed source/resource/binary deltas against baseline.
- [ ] Independent code review + GLM5.3/max decision review of actual frozen candidate; fix and re-review.
- [ ] If safe isolated native test environment is available, use backups/no simultaneous games; test both sides/flags/noPike/retreat/death/break/paused/queued/arrival/8s work/threat/shopmove/save-load. Real player saves are not trial input in place. Mac/Windows record separate.
- [ ] Commit/push only owned files, create PR with code/native boundary. Merge/normal install only after applicable authorization and exact candidate review/checks; no automatic install on current gameplay.

## Completion

G0 approval alone cannot close issue234 or authorizeG1 native adapter/merge/install. Entire code scope needs necessary review/verification and all associated PRs merged before managed completion; actual installation/player results separately recorded. Unknown native or platform behavior stays explicitly unverified. No model/self-report/build-only live acceptance.

## G0 exact contracts (frozen before worker dispatch)

All are `internal` in namespace `KingdomEnhancedMod`, no Unity/native/file dependency. Adapters must supply proven snapshots; these values cannot prove a native identity by themselves.

```csharp
internal static class HeavyShieldDurabilityMigration
{
    internal const int CurrentVersion = 4;
    internal const int Maximum = 40;
    internal static bool TryUpgrade(int sourceVersion, int durability, bool broken, out int upgraded);
    internal static int WearBand(int durability); // 0 intact,1 worn,2 critical,3 half; -1 invalid
}
internal readonly record struct HeavyShieldBannerCandidate(Guid Receipt, long Life, int Side, float X, bool Eligible);
internal static class HeavyShieldBannerSelection
{
    internal static bool TrySelect(IReadOnlyList<HeavyShieldBannerCandidate> candidates, int side, float bannerX, out HeavyShieldBannerCandidate chosen);
}
internal sealed class HeavyShieldBannerBinding
{
    internal bool TryBind(long activation, long formation, long world, int side, in HeavyShieldBannerCandidate chosen);
    internal bool IsCurrent(long activation, long formation, long world, int side, bool active, Guid receipt, long life);
    internal void End(); // does not permit rebinding the same activation
    internal void Reset(); // world teardown only; never ordinary loss/empty slot
}
internal readonly record struct HeavyShieldMaintenanceSubject(Guid Receipt, long Life, int Durability, bool Valid, bool InBanner, bool AtShop, bool Threatened, double LastThreatAt);
internal readonly record struct HeavyShieldRepairCompletion(long Serial, Guid Receipt, long Life, long World, long ShopLife, int ExpectedDurability);
internal enum HeavyShieldMaintenancePhase { Idle, Travelling, Repairing, CompletionDue, AwaitingResult, HeldUnknown }
internal sealed class HeavyShieldMaintenancePolicy
{
    internal const double QuietSeconds = 6d;
    internal const double WorkSeconds = 8d;
    internal HeavyShieldMaintenancePhase Phase { get; }
    internal Guid ActiveReceipt { get; }
    internal long ActiveLife { get; }
    internal double WorkedSeconds { get; }
    internal bool IsQueued(Guid receipt, long life);
    internal void Tick(double gameTime, bool playing, long world, long shopLife, bool siteValid, IReadOnlyList<HeavyShieldMaintenanceSubject> subjects);
    internal bool TryTakeCompletion(out HeavyShieldRepairCompletion completion); // emits once per serial
    internal bool ResolveCompletion(long serial, bool accepted, bool uncertain);
    internal void Reset(); // world/owner teardown only
}
```

Policy obligations:
- Native Side is normalized -1/+1; reject other values, NaN/Infinity coordinates/time and nonpositive life/world/formation identifiers. Guid.Empty never selected.
- Candidate scope is the current verified island roster (max4), no global enumeration. Ties use Guid.CompareTo for deterministic equality. Activation monotonic positive token, one choice per activation; End retains last token so death cannot refill.
- Repair eligibility: Valid, durability1..39, not InBanner, not Threatened, gameTime-LastThreatAt>=6; full/broken/invalid values never enter. Input list duplicates or >4 must fail closed for that tick, not merge different lives. FIFO first qualified observation; same-tick tie by Guid; same receipt new Life never inherits progress.
- Queue waiting counts IsQueued=true, so banner selection skips it. Only active queue head receives travel/work; no moving/repairing output for other rows.
- On invalid site/context change or interruption remove old job/progress. Paused Tick never advances work or grants completion; first arrival starts at0, travel time cannot count as work. Use successive playing game-time deltas only; paused calls reset delta baseline. Clock rollback cancels current work.
- Current durability must remain equal to ExpectedDurability through work and completion; a new hit cancels instead of repairing the newly damaged state under an old proof.
- TryTakeCompletion changes phase toAwaitingResult before caller can mutate; a second call for the same serial fails. False definite result cancels work and requires a new complete job; uncertain result goesHeldUnknown until owner/world Reset (no blind replay). Accepted result releases the job; do not requeue that same still-stale subject snapshot until a later Tick proves full/new durability or a new native life.
- `WearBand`: 31..40→0,21..30→1,11..20→2,0..10→3; outside0..40→-1. Upgrade checks source upper bounds before conversion; broken must exactly match zero. Version4 is identity/no multiplication.

G0 worker may create only the three named Patch_ policy files and its two named test directories; does not edit existing owners/Main/config/projects/runtime. Main owns adapter integration after G0 independent review. These signatures are compile-time contracts for independent leaves, not assertions that currentnative adapters already exist.

### G0 review amendments — empty activation and completion fence

- `TryBind` validates the outer positive activation/formation/world/side first. A newer legal activation always consumes its token and ends the prior binding even when chosen is default or ineligible; it returns false with no bound career. Later valid candidates for that same activation remain rejected. Invalid outer context does not consume or revive a binding. Tests cover empty activation→later available candidate and rejected-repeat activation.
- Repair completion serial increases monotonically for the lifetime of a policy instance; `Reset` clears jobs/context/clock but never resets or reuses the serial counter. Overflow fails closed.
- `ResolveCompletion` has effect only in AwaitingResult with the exact current serial. Wrong/old/duplicate serial or wrong phase returns false without changes. If uncertain=true it wins even when accepted=true, and phase becomesHeldUnknown.
- Ordinary Tick inputs (site invalid, threat, snapshot invalid, pause) cannot erase AwaitingResult/HeldUnknown and start another job. Tick can update the playing observation but preserves that transaction barrier until explicit matching Resolve or a real owner/world teardown Reset.
- `TryTakeCompletion` requires the latest observation to be playing=true; a CompletionDue job cannot emit while paused. After pause, resume still requires the current job/context/subject to validate before any grant; no paused/expired proof authorizes a repair.
- Tests cover CompletionDue→paused Take refusal; stale completion afterReset and a newjob; wrongphase/doubleResolve; uncertain-over-accepted; invalidTick whileAwaiting/HeldUnknown never replays or auto-releases.
