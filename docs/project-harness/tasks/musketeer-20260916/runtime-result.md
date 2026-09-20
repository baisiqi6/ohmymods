# Musketeer runtime/combat/visual slice — 结果与接线清单（评审修订版）

状态：**代码完成、测试与 interop 核对工程就绪；未构建/未运行/未安装**（Operator 跑 build/test）。
范围：只写了 allowlist 内文件（4 个生产文件 + tests/musketeer-runtime/** + 本文件）。
本版按 `runtime-review-fix.md` 的 9 条 blocker 逐条修复；每条都有对应回归用例（§6）。

## 0. 交付文件

生产（il2cpp/）：
- `MusketeerAnimation.cs` —— 纯逻辑动画：atlas 66 帧表（帧区间/holds/逐帧锚点，抄自
  `artifacts/musketeer/20260916/atlas.json`）+ 状态机。locomotion 相位 =
  `(unwrapped nt × clipLength) mod authored`（绝不先取 `Fraction`）；未知/非法原生采样
  **无条件优先**（含 Fire）；移动优先于一切非 Fire 枪械表现（走动中绝不举枪冻结）。
- `MusketeerCombat.cs` —— 射速规划（绝对 ×2 + 自检不变量）、地面敌人判据、最近有效命中选择器、
  `TryHandleShot`（Operator prefix 入口）、有界可增长子弹表（≤512，池化视觉）、施法前按剩余射程/寿命
  钳制的扫掠、自适应物理缓冲（32→256，饱和即保守终止）。
- `MusketeerRuntime.cs` —— 每帧对账（哈希表 + 活动列表，容量 4096 对齐 identity sidecar；无全表扫描）、
  箭 SO 克隆、cadence 绝对写入 + CAS 归还、射程/扫描器、**射击决策地面重选**（原生 `ShouldShootEnemy`
  返回边界；扫描器谓词与缓存零写入）、打猎抑制、岗位门 + 原生 `ExitGuardSlot`。
- `MusketeerVisuals.cs` —— embedded atlas 解码、自有子 renderer（沿用英雄的 ownership/回滚范式，
  不套 0.9 与置前平面）；`fallbackReason != None` 时**绝不显示自有帧**（未知/非法/inert/grabbed/
  原生隐藏一律让位）。

测试（tests/musketeer-runtime/）：
- `MusketeerRuntimeTests.csproj` + `UnityStubs.cs` + `NativeStubs.cs` + `Fixture.cs` + `Harness.cs`
  + `AnimationTests.cs`（16 例）+ `CombatTests.cs`（18 例）+ `RuntimeTests.cs`（14 例）
  + `WorldTests.cs`（10 例，世界生命周期）+ `VisualTests.cs`（4 例，池复用视觉）。
- `interop-check/MusketeerInteropCheck.csproj` + `CollabStubs.cs` —— 对真实 2.4 interop 编译 4 个生产
  文件（字段名/可读写性/方法签名/Harmony 特性/Physics2D 形状硬核对）。**需 Operator 带 restore 构建**。

## 1. Operator 必须做的接线

1. `il2cpp/KingdomEnhancedMod.csproj`：内嵌两个资源
   - `Assets/MusketeerAtlas.png` → `LogicalName="KingdomEnhancedMod.MusketeerAtlas.png"`
     （复制 `artifacts/musketeer/20260916/MusketeerAtlas.png`；672x192、12x6、PPU32、pivot(31,2)）
   - `Assets/MusketeerBullet.png` → `LogicalName="KingdomEnhancedMod.MusketeerBullet.png"`
     （用户/Operator 授权的 5x3 弹丸图；Point、PPU32、pivot 居中；代码校验尺寸/非全透明，不符即
     **不构造弹丸**（fail-closed，不会出现隐形伤害），并限频记录一次）
2. `ModPanel.Update()`：`MusketeerIdentity.Tick();` 然后 `MusketeerRuntime.Tick();`（**Identity 先于 Runtime**）。
3. `ModPanel.LateUpdate()`：`MusketeerVisuals.Sync();`（必须在原生 Animator 产出本帧结果之后；有 frameCount 去重）。
4. `ArrowAttack.FireArrowInternal` prefix：
   ```csharp
   [HarmonyPatch(typeof(ArrowAttack), "FireArrowInternal")]
   static class ArrowAttack_FireArrowInternal_Musketeer_Patch {
       [HarmonyPrefix] static bool Prefix(ArrowAttack __instance, GameObject source) =>
           !MusketeerCombat.TryHandleShot(__instance, source);   // true = 跳过原生箭
   }
   ```
   契约：活动火铳手（身份 + 包已装）一律压制原生箭；非火铳手/开关关/身份未装包 → 返回 false 放行。
5. `MusketeerIdentity` 需按 contracts.md 导出 `IsUnit/CopyUnits`。`MusketeerAccess.cs` 无需改动。
6. 面板卡片可选用 `MusketeerRuntime.StatusText`（`armed=N shots=N bullets=N skipped=N`）。
   `MusketeerRuntime` 自带 `Archer.ShouldShootEnemy` postfix（地面重选）与四个岗位钩子，Operator 无需重复注册。

## 2. 玩法数值（评审修订后的单一权威）

**射速：E[N] 摊分 + 绝对写入 + 周期自检（final-corrections #1）**
- 基线只取**未修改的原生 Archer prefab**（`Holder.tagCharacterPairs["Archer"]`，按 world 缓存；含 min/max attempts）；
  实例现值与基线不一致 → `MusketeerCadence.TryPlan` 返回 `live-drift`，**整包 refuse**（该实例保持原生行为）。
- 原生证据（`game-source/Assembly-CSharp-2.1.0/Archer.cs` Shoot 协程约 1000–1060 行）：
  `int attempts = UnityEngine.Random.Range(minAttempts, maxAttempts)` —— int 重载 **max 独占**，max == min 时返回 min；
  **每发都要 prep + interval，cooldown 每轮只结算一次**。所以普通「持续」间隔（非 perfect、不间断的期望值）：
  ```
  E[N]        = max > min ? (min + max - 1) / 2 : min          （min ≥ 1、max ≥ min、有限；否则整包 refuse）
  ordinary    = shootPrepTime + midpoint(_shootIntervalRange) + max(0, shootCooldownTime - _cooldownReduction) / E[N]
  ```
  `ArrowAttack.ShotCooldownSeconds` 是 SO 上的随机值，不是 Archer 射击协程的一部分 → 不采用。
- 写入（绝对值，逐项 before 回执）：
  `shootPrepTime = 2×prefabPrep`；
  `_shootIntervalRange = (2×mid, 2×mid)`（区间收敛到中值，周期确定）；
  `shootCooldownTime = 2×max(0, prefabCooldown - reduction) / E[N] + reduction`；
  `minAttempts = maxAttempts = 1`（火铳手一发一装填，确定性）、`perfectArrowProbability = 0`。
  `TryPlan` 自检 `prep' + interval' + (cooldown' - reduction) == gate`，不一致直接 refuse。
- **本机基准数值（Operator 可直接核对）**：fixture/普通希腊弓 prefab
  `prep=0.5, interval=(0.5,1.5), cooldown=1, reduction=0, minAttempts=1, maxAttempts=3`
  → `E[N]=1.5`；`ordinary=2.1666667s`（旧口径 2.5s 是错的）；`gate=4.3333333s`（旧口径 5s）；
  写入 `prep'=1.0`、`interval'=(2.0,2.0)`、`cooldown'=1.3333333`；
  闸截止 = `now + 4.3333333 - 0.05 = now + 4.2833333`（同值喂给视觉 Reload 窗口）。
- 声明口径：这是**普通、非 perfect、不间断**的持续期望节奏；目标丢失/撤退/编队只会让原生更慢，
  不声称帧级精确实际射速。

**地面敌人判据（目标选择与弹道碰撞同一套）**
- 拒绝：带 `Squid` 组件；`Enemy.Type` 不在白名单（TrollWeak/TrollMedium/ToughTroll/Ogre/Stealer/
  Crusher/Knight/Archer）。Squid 与**未验证的 Boss 系**（Boss/KillerBoss/GauntletBoss/BossWithStealer）
  一律拒绝——保守选择，**不声称已覆盖全部地面 Boss**，待实测证据再放行。
- 允许：不带 `Enemy` 组件的敌方静态结构（无飞行能力）。
- 原生等价门：Enemies 层、非 EnemySpawn/Unspittable/QuestStructure、Damageable 存活/启用/
  `IsDamagedBy(Arrow)`、invulnerable+ignoredWhenInvulnerable 排除、友军巨魔排除、自己排除。
- **撤退视野保留（评审修复 8）**：敌人扫描器**完全不改**（谓词与缓存都不动）→ `ShouldFlee` 的威胁
  视野原样保留（飞行单位不会"看不见"）。射击选择改在原生 `ShouldShootEnemy` 的**返回边界**上：
  若原生选中了非地面目标，就地从原生扫描器**已缓存的候选列表**（`GetAll`，不新增扫描）按原生顺序
  重选第一个合法地面敌人；没有则本次决策不开火（清空目标）。私有 helper 若被 IL2CPP 内联（pit 17），
  钩子不命中时退化为"原生选目标 + 弹道侧地面校验"（仍然安全：绝不伤害非地面目标）；命中时有一次性
  entered 日志（`shoot-decision ground re-selection entered`）。
  说明：不采用"临时安装扫描器过滤"——`Scanner.Refresh` 会把过滤结果缓存 ≤0.5s，归还谓词擦不掉缓存，
  `ShouldFlee` 仍会短暂失去飞行威胁视野；重选方案对扫描器零写入，没有这个残留。

**伤害/弹道**
- 直线恒速（30 u/s）；**施法前**把本帧步长钳到 `min(步长, 剩余射程, 速度×剩余寿命)`，绝不越过射程施法。
- 每段 `Physics2D.LinecastNonAlloc`（Enemies 层）：缓冲按需 32→64→128→256 并**复用**；
  到硬上限仍饱和 → **保守终止**（消费子弹、零伤害，一次性日志），绝不据"证据不全"打更远的敌人；
  段外/非法距离的命中直接跳过（不再当段尾处理）。
- 最近**有效**命中即停（与回调顺序无关）；友军/飞行/未知/死亡/自己为非法候选（不阻挡、不受伤）。
- 伤害固定 2：`Damageable.ReceiveDamage(2, shooterGameObject, DamageSource.Arrow)`；Crusher `!IsStunned`
  → 消费不掉血；消费闩先于伤害（回调重入不可能二次命中）。
- 子弹表 = `Runtime.MaxUnits`（4096，与 identity sidecar 职业上限对齐，**一次全职业齐射也不丢弹**；
  绝不拿"平均占用"当容量论证），硬帽 + 计数器 + 一次性日志；视觉对象池化（复用 GameObject/SpriteRenderer）。

**世界生命周期（评审 world-fix，`MusketeerCombat.EnsureWorldScope`）**
- Tick 与出膛前都过世界守卫：世界身份（gameLayer `Pointer` + `InstanceID`）变化，或当前世界不可用
  （无 gameLayer / 已销毁 / 层级失活）→ 立刻丢弃自有**在场子弹**（旧世界的弹绝不在新世界继续飞或伤害）
  并销毁自有**弹丸池**；当前世界不可用时本帧不出膛且**不消耗射击闸**（fail-closed，绝不打隐形伤害）。
- 同一世界内的暂停、开关切换不做任何事：池与在场子弹原样保留（暂停 `playing=false` 只冻结推进）。
- 复用池条目前校验：已销毁条目直接丢弃；父层不是当前世界 → **重挂到当前层再激活**；失败 → 退休回执。
  池顶坏条目**不会吃掉射击**（同一次调用继续租下一个/新建）。
- `Destroy` 抛异常 → 自有引用进**退休回执**（限频重试），绝不因为一次异常丢掉所有权。
- 贴图/材质与世界无关：守卫与 `ResetPool` **绝不重置/重载**贴图（不重复解码、不漏纹理）。
- 弹丸外观：只取 Operator 的 5x3 贴图（Point/PPU32/居中 pivot）+ **中性默认材质**（绝不继承原生箭/火焰
  材质与染色）；只复制原生箭 renderer 的排序层/序。资产缺失 → 不构造弹丸（fail-closed，限频日志）。

## 3. 生命周期与所有权（评审修订）

- 单位表 = `Dictionary<(pointer, InstanceID)>` + 活动列表；容量 4096 = identity sidecar 的职业记录上限；
  每帧只遍历活动列表（无全表扫描），第 65 名及以后照常装包（回归 `ManyUnits` 用 70 名验证）。
- 所有字段写入都有 before/applied 回执 + CAS：**attempts/perfect 也是 CAS**（只回收我们写入的 1/0）。
- 打猎抑制（唯一常驻的扫描器谓词）：**先记所有权再调用 setter**（2.4 setter 可能"写一半才抛"）；
  归还要回读核对，失败保留回执下一帧重试；第三方接管（指针不同）→ 让位并清账，绝不覆盖。
  敌人扫描器（ShouldFlee/ShouldShootEnemy 共用）**从不写入**：射击决策走 `ShouldShootEnemy` 边界的
  地面重选（读原生缓存列表，零写入）。
- **池复用视觉退场（final-corrections #2）**：`MusketeerVisuals.BeforeReuse(archer)` 由 runtime 的
  `Archer.OnEnable` prefix 调用（可能在物理回调里）：`Animation.Reset()` 清 Fire/Reload/idle 时钟 →
  隐藏自有 renderer → 置 `Detached` 并让出 GameObject 槽位（`HasVisual` 因此绝不接受旧 life 的视觉，
  `Sync` 也绝不拿旧状态替新 life 隐藏原生）→ 归还原生 `forceRenderingOff`（失败进 `PendingReleases`
  凭据重试）→ 自有根进 `PendingRetireRoots`，**销毁交给下一次 Sync**（回调里绝不 Destroy）。
  若旧 life 的归还尚未成功，`Apply` 会先拒绝新视觉（原生保持可见 = 安全），O(1) 查表、无世界扫描。
- `Strip`：字段归还成功 且 克隆已销毁（或从未创建）才 Release；`destroyAllowed=false`（OnEnable prefix，
  可能位于物理回调）只归还字段、把销毁与收尾留给下一帧 Tick。**延后销毁后条目会被释放**，
  池化对象再次被标记时可重新装包（回归 `DeferredDestroyAndReuse`）。
- 岗位：`IsAvailableForJob`/`AssignJob`/`SetGuardSlot`/`EnterGuardSlot` 拒绝骑士与塔位；
  已在槽位走原生 `ExitGuardSlot`（≤3 次、1s 间隔、busy 保护）。原生 AI/Mover/昼夜/守卫目标不动。
- 视觉：自有 renderer 挂在原生 renderer 的 Transform 下（localPosition 0、不缩放、不挪 z），
  `native.forceRenderingOff` CAS；颜色/材质/排序/flip/层每帧只读复制。**`fallbackReason != None`
  时绝不显示自有帧**（未知状态/无 Animator/非法采样/inert/grabbed/原生隐藏 → 归还原生渲染）。
  `MusketeerVisuals.Clear()` 只移除成功归还的条目，归还失败者的回执保留在表里继续重试。

## 4. 动画时钟（atlas.json clocks 逐条落地）

- locomotion：`phase = (unwrapped normalizedTime × clipLength) mod authored duration`；绝不先取
  `Fraction(nt)`（测试用 nt=1.5/clip=2.167 钉住：正确帧 14，先取小数会退到 12）。
- Idle：`elapsed-native-stand-seconds`，只在原生 Stand 相位推进时累计（Idleness=0/冻结 → 冻结）。
- 未知/非法采样（含 NaN/无 clip 长度）**优先于一切枪械表现（含 Fire）** → 返回 -1 归还原生。
- 移动（Walk/Run）优先于非 Fire 的一切枪械表现；**移动中获得目标不会起举枪**（不回冻结成站立姿势）；
  停下后若仍处于姿态条件才 Raise→Aim。
- Fire 0.25s（后坐内嵌）；Reload：t0+0.25 起，`window=min(1.4, max(0, nextEligible-(t0+0.25)))`，
  `authoredSeconds=clamp((now-t0-0.25)/window,0,1)×1.4`，window=0 跳过，完成后停 Aim。
- 出膛原点固定「已举枪」Aim 枪口 [48,14]（本地坐标经 TransformPoint，flipX 修正方向）。

## 5. 明确限制（不得当成已完成/已证明）

1. **未实机验证**：2.4 原生事实来自仓库生产代码/interop dump/2.1 逻辑说明书；我未构建、未进游戏。
2. `Physics2D.LinecastNonAlloc(..., Il2CppStructArray<RaycastHit2D>, int)`、
   `Scanner.additionalRequirements/SetExtraCondition`、`Scanner.ObjectCondition` 的托管委托隐式转换、
   `Scanner.GetAll(out Il2CppReferenceArray<GameObject>)`、`ArrowAttack.Range`、`Crusher.IsStunned`、
   `EnemyType.*`、`World.GroundCollider`、`Animator.GetCurrentAnimatorStateInfo`
   全部由 **interop-check 编译核对**背书（需 Operator 运行）。若真实签名不同：物理调用只有
   `MusketeerCombat.Sweep` 一处、扫描器重选只有 `MusketeerRuntime.FindFirstGroundFoe` 一处、
   过滤器归还只有 `RestoreScannerFilter` 一处要改。
3. `Archer.ShouldShootEnemy` 是私有 helper：按 pit 17 可能有内联/绕过风险。未命中时行为退化为
   "原生选目标 + 弹道侧地面校验"（安全但可能被飞行单位垄断射击）；命中时有一次性 entered 日志，
   实机日志里应能看到 `[Musketeer] shoot-decision ground re-selection entered`。
4. **Boss 系敌人被拒绝**（含 GauntletBoss/BossWithStealer）——保守规则，待实测证据再放行（一行白名单）。
5. **声音**：原生 `Archer.FireArrow` 仍按原生调用播 `shootSound`；子弹与原生调用同帧出膛（正常 1:1），
   但被闸压制的那次调用仍会响一声（空响）。未替换音效资源（避免新资产依赖）。
6. 只做单机/主机权威（沿 `MusketeerAccess.TrackAllowed`）；无 RPC、无网络同步。
7. 视觉未做：英雄那套置前平面/0.9 缩放（用户要求普通职业不套用）；命中特效；Retreat 帧未接线。
8. 射速写入假定实例值等于未修改 prefab 基线：第三方持久改写这三个字段的实例会被 refuse（保持原生），
   这是刻意的 fail-closed，实机日志会显示 `cadence refused (live-drift)`。
9. `MusketeerIdentity` 尚未落盘（identity worker 并行）；Runtime 在 `IsUnit/CopyUnits` 到位前无法通过构建。
10. 弹丸贴图资源（5x3）由 Operator 提供；缺失时不构造弹丸（无隐形伤害），日志限频一次。
11. 未做 DLL 方法审计/独立复核/实机安装——按分工由 Operator 与 reviewer 完成；本文件不声称测试已跑。
12. 世界守卫是 fail-closed：gameLayer 不存在/已销毁/层级失活期间（换岛过渡），火铳手**不出膛且不消耗射击闸**
    （原生箭仍被压制），恢复激活后照常射击；该窗口内不生成任何弹丸视觉。
13. 池复用（`BeforeReuse`）只做回调安全的"退场"，**真正的销毁与失败归还重试发生在 `MusketeerVisuals.Sync()`**
    （ModPanel.LateUpdate）。若 LateUpdate 未接线，旧 life 的自有 renderer 会保持隐藏但不会被销毁，
    原生隐藏的归还也不会重试——接线清单第 3 条因此是硬要求。

## 6. 测试对照（评审 blocker → 回归用例）

| 评审条目 | 用例 |
|---|---|
| 1 编译/警告 | 移除未用字段；`UnityStubs` 补 `using KingdomEnhancedMod`（ArrowAttack 解析） |
| 2 容量（65 名、17+ 弹） | `65+ musketeers all arm (no silent career cap)`（70 名）、`bullet pool holds 40+ concurrent shots without losing any` |
| 3 单一射速权威 | `cadence plan: absolute 2x writes from the prefab baseline only`、`cadence plan refuses drifted live values`、`cadence gate has a frame-quantization tolerance`、`a legitimate attempt right after the gate is never swallowed`、`one gate one bullet` |
| 4 施法前钳制/饱和 | `bullet stops at the exact range boundary and never damages beyond it`、`saturated casts terminate conservatively without damage` |
| 5 延后销毁可重用 | `OnEnable prefix restores fields, defers destroy, then the entry is reusable` |
| 6 渲染优先级 | `target acquisition during walk never freezes a stationary pose`、`invalid/unknown native sampling outranks the gun phase (incl. Fire)`、`movement interrupts gun presentation but not the confirmed Fire` |
| 7 归还回执/CAS | `wildlife filter restore retries when the setter throws`（写前/写后抛两种）、`a third-party wildlife filter takeover is never clobbered`、`third-party field values survive the strip (CAS)`（含 attempts/perfect） |
| 8 撤退视野保留 | `enemy scanner keeps its native threats (ShouldFlee preserved)`、`shoot-decision reselects a ground foe instead of a flyer` |
| 9 弹丸贴图 | `missing bullet sprite fails visible construction (no invisible damage)` |
| world-fix 生命周期 | `world change (previous layer destroyed) discards pool and the first new shot works`、`world change (previous layer merely inactive) resets and the first new shot works`、`inactive current world never fires and never consumes the gate`、`no old live bullet damages the new world`、`pause inside the same world keeps pool and live bullets`、`feature toggle inside the same world keeps the pool reusable`、`stale pool entries are skipped and the same call still rents a working visual`、`pooled visual with a foreign parent is reparented to the current layer`、`destroy failure keeps a retirement receipt and retries later`、`physics capacity is retained (saturation then normal casts stay at 256)` |
| final #1 E[N] 射速 | `cadence baseline = prep + interval mid + cooldown/E[N] (sustained mean)`、`E[N]: exclusive max, equal min==max, N=1 back-compat, invalid rejected`、`cadence plan: absolute 2x writes from the prefab baseline only`（含 N=1 兼容与 attempts 非法 refuse）、`cadence plan refuses drifted live values`（含 attempts 漂移） |
| final #2 池复用视觉 | `reuse resets the old Fire/Reload phase before the new life renders`、`reuse then reapply shows a fresh idle frame (old phase does not carry)`、`reuse without a new career never leaves the native hidden`、`native-hide release failure keeps a receipt and retries on Sync` |
| 原有契约回归 | 动画 16 例（含希腊 wrap/暂停冻结/枪械时钟/未知挂起）、战斗 18 例（最近命中/友军/飞行/Crusher/闩/压制阶梯/射程边界/饱和）、运行时 14 例（装包/幂等/归还/闸/岗位/槽位/关闭） |

运行（Operator）：
- `dotnet run --project tests/musketeer-runtime/MusketeerRuntimeTests.csproj`（期望 `failed=0`）
- `dotnet build tests/musketeer-runtime/interop-check/MusketeerInteropCheck.csproj`（需 restore；核对真实 2.4 interop 签名）
