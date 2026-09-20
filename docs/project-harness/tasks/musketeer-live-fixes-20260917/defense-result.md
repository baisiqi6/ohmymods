# 火铳手防守（左右均衡）— defense worker 结果

2026-09-17，OMP DeepSeekFlash max（午间派发）。范围与允许写路径严格按 `defense-worker.md`：
本 worker 只写 `il2cpp/MusketeerDefense.cs`、`tests/musketeer-defense/**`、本文件；未改其他源码/
配置/存档，未安装、未启动游戏、未提交、未构建（无 shell，构建与测试由 Operator 执行）。

**第二轮（`defense-review-fix.md` 复核修订）**：(1) 政策改为**单侧完全安全时子集照样平分**
（用户要的是枪械子集自身的均衡；普通弓手仍走原生危险分配）；(2) Operator 首轮测试在
`Program.cs:173 neutral tie: goes left` 失败，根因是**测试夹具建模错误**（把"原生改写后的侧别"
写在了捕获之前，导致该单位根本不是中性），已用"prefix → 模拟原生 → postfix"三段式夹具修正，
并同步修正 test 6 同类错误（此前恰好经另一条路径通过）；(3) 深度去重普查扩展到**未被改动的普通
弓手**（只读共享扫描，绝不写普通单位），去掉自造的 `MaxReusedDepth=32` 上限，改为沿用单位自身
原生深度；(4) 两个测试工程 obj/bin 隔离（`Directory.Build.props`）。Operator 首轮
`Interop.csproj` 编译已 0W0E（真实 2.4 interop）。本轮仍未运行任何构建/测试（无 shell）。

## 1. 结论（decision-first）

- **根因方向确认（代码级）**：原生 `Kingdom.DistributeFreeArchers` 的侧别分配是 **按全弓手 x 排序后的名次**，
  不是按单位相对营火的位置。左半（排序前半）全部 Left，`i==0` 强制 Left、`i==count-1` 强制 Right。
  火铳手全在同一枪店/货架 x 处转职出生，整批落在排序列表同一段 → 整批同侧；走到那侧后名次更靠边，
  下一轮原生分配继续把同一批放同侧（自我强化）。**单侧完全安全时**原生还保留
  `MAX_SAFE_SIDE_ARCHERS=4` 个"安全侧留守位"给排序最左的 4 个（同一批火铳手很容易整批占掉）。
- **修复**：新增 `il2cpp/MusketeerDefense.cs`，只在原生 `DistributeFreeArchers` 边界做**标记子集的
  最小重平衡**：prefix 捕获改写前侧别，postfix 把子集计数收敛到差 ≤1（Right 拿奇数，同原生奇偶），
  且稳定（重复一轮零写入）。**平分不依赖单侧危险状态**（用户要求枪械子集自身均衡；仅号角 override
  与缺完好墙才让位原生），普通弓手继续原生危险分配。写入只用原生 `Archer.SetGuardSide(side, depth)`；
  不跳原生、不写位置、不 Destroy、不逐帧、不加存档 schema；深度去重读共享扫描（普通弓手只读）。
- **不改身份**：新火铳手在生产/转职瞬间还未绑定职业（绑定发生在 `Character.Promote` postfix），
  本文件不做任何 Identity 改动；新火铳手会在**下一次原生分配事件**（弓手增减/昼夜切换/边界重算/威胁
  状态变化）被纳入子集并落到较少的一侧。

## 2. 根因证据（分级）

**[2.1 逻辑参考]** `game-source/Assembly-CSharp-2.1.0/Kingdom.cs:2651-2725`：
排序键 `leftToRightArchers`（`Kingdom.cs:3927`，`l.transform.position.x.CompareTo(r.transform.position.x)`）；
`num6 = …/2`（两侧同安全态）或 `side==Right ? 4 : count-4`（单侧安全，即 4 个安全侧留守位，
常量 `MAX_SAFE_SIDE_ARCHERS=4`，`Kingdom.cs:3929`）；循环里 `i==0 → Left`、`i==count-1 → Right`、
否则按 `num < num6` 填 Left；最后 `SetGuardSide(side2, num7)` 的 `num7` 只是每侧自增的深度序号。
→ 侧别 = 排序名次函数；同 x 集群必然同侧。

**[2.4 实测字段]** 本仓库生产代码已证 2.4 `Archer` 的 `_guardSide/_guardDepth/_unitSpacingAtWall/
_minDistanceFromWall/_guardRandomOffset` 就是墙后站位公式的输入（`PatchWorld_DefenseSpacing` 注释与运行日志
`first scan: d=6 s=0.11 min=1.75 … side=1`），且 `Archer.SetGuardSide(Side, Int32)` 存在于
2.4 interop 元数据（`tasks/musketeer-20260916/actual-interop.txt`）。**2.4 方法体未反编译**：
"2.4 仍按 x 排序"来自 2.1 对照 + 任务书侦查结论 + 现场行为（下条），本轮不宣称逐字复核；
`Kingdom` 类型也不在已导出的元数据清单里，`DistributeFreeArchers` 的 2.4 存在性/可见性由
`tests/musketeer-defense/Interop.csproj`（对着真实 E 盘 interop 编译）验证，见 §6。

**[运行现场]** 冻结日志：`[Musketeer] saved:records=4:bound=4`（本局买了 4 把）；
枪店 x=89.18；`[DefenseSpacing] archer lineup: side=R total=104 outside=4 …`（夜间右墙 104 人，
其中 87 骑士随从、17 普通弓；左侧未达到 ≥5 人报告阈值 → 左侧只有极少数人）；
用户实报"买了几把火铳手全都往左跑"。与"整批同 x → 排序同段 → 同侧"一致。

## 3. 实现契约（`il2cpp/MusketeerDefense.cs`）

- **边界**：`[HarmonyPatch(typeof(Kingdom), nameof(Kingdom.DistributeFreeArchers))]` prefix+postfix
  （全程序集 `PatchAll` 自动注册，不需要接 `Main`/插件改动；与 `SpecialTowerDuplicateCleanup` 的
  `DistributeTowerArchers` 作用域互不重叠）。
- **子集资格（全部为原生自由弓手语义）**：标记身份（`MusketeerIdentity.IsUnit`）、当前世界层
  （`MusketeerAccess.InWorld`）、`Archer.isAvailable`、无 `_knight`、无 `_guardSlot`/`inGuardSlot`、
  未上船/未待上船（`_embarkee.IsEmbarked/EmbarkableTarget`）、无编队（`GetFormation()==null`）、
  非玩家操控、非隐藏（`harmless`）、`_character` 非 inert/grabbed/stationary、未死亡、非塔上高度（y>2.5）。
- **开关（fail-closed，任一不满足整套不动作、原生语义原样）**：
  `MusketeerAccess.Enabled`（= 离线 + 世界权威 + 当前 biome + 配置开，`MusketeerEnabled`）、
  `Managers.Inst.kingdom` 即传入实例、世界层有效；
  `!kingdom.HasOverrideGuardPosition()`（号角 override 把所有人导向同一面墙）、
  `kingdom.intactWall[Left/Right] != null`（缺墙的一侧没有原生守位目标）。
  **不再读取 `enemies.IsSideCompletelySafe`**：单侧完全安全时子集照样平分（§1），普通弓手的
  原生危险分配不受影响。
- **决策**（排序后按 x 升序）：
  1. 先统计 prefix 捕获的旧侧别；旧侧别中性（0/新职业）的单位先补到较少一侧
     （第一个中性且两侧并列时走 Left，其余并列走 Right → 单个新兵 Left、多个新兵时 Right 拿奇数，
     与原生"第一个排序弓手 Left、Right 拿奇数"一致）。
  2. 差 >1 时最小修正：`moved = |diff|/2`，从超载侧移**最靠近目标墙**的单位
     （Left→Right 取 x 最大者、Right→Left 取 x 最小者）→ 最终差 ≤1。
  3. 已在决定侧的单位：**完全不动**（保留原生本轮侧别与深度）；需要写入的单位（被移/新放/纠偏）
     写入 `SetGuardSide(side, depth)`：
     * 纠偏（原生又把已决定单位翻回错误侧）：沿用该单位**自身原有的原生深度**（≥0 且该侧仍未占用），
       不设自造上限；
     * 被移/新放：取该侧**当前无人占用的最小深度**——占用集 = 我方留守单位当前深度 + 共享
       Archer 扫描里所有"墙防守位型"弓手（含普通弓手，`_knight/_guardSlot/inGuardSlot` 的除外）
       的当前深度。普通弓手只读、绝不写；没有待写单位时**不调用扫描**（无额外成本）。
     * 深度语义 = 墙后原生排名序号；本文件不发明任何深度区间，DefenseSpacing 的钳制拍继续负责把
       过深排名拉回射程（它写深度、本文件只写侧别，两者不互相打架）。
- **稳定性**：重复一轮若两侧计数已平衡且字段与决定一致 → **0 次写入**（无每轮抖动）；只有原生再次
  改写侧别才会纠偏重写（写入值恒定，行为不来回）。日志：每世界一条 canary
  `[MusketeerDefense] native distribution boundary active (canary)`（在该世界第一次上下文有效的分配即输出，
  与单次 split 门是否放行无关 → 用于证明钩子真实命中），每次实际重平衡一条
  `rebalanced: units=… moved=… written=… left=… right=…`（每世界上限 32 条）。
- **重入/异常**：`_capturing` 只由最外层调用持有（嵌套原生分配不覆盖捕获）；若原生抛出导致 postfix
  缺失，下一次分配在 frame/time 变化后丢弃陈旧捕获（自愈）。全部入口 try/catch，失败只记一次日志。

## 4. 与其他 MOD 系统的交互（读过的现有 patch）

- `PatchWorld_DefenseSpacing`：不改侧别分配，只消费 `_guardSide`（白天拥挤重掷用 `_guardSide` 侧、
  夜间 goal 镜像/深定位按位置）；本文件修正侧别后这些监督器自动跟随。`DepthClampPass` 会按射程
  夹 `_guardDepth`（对所有弓手）；本文件只写侧别、深度只做"不与现有占用重复"的选择，不与其争用，
  也不发明深度区间。
- `UnitScanCache`（共享扫描缓存）：本文件**仅在有待写单位时**调用 `GetArchers()` 一次做占用普查；
  共享缓存本身 ≤3s 一次真扫（与 DefenseSpacing 同拍共用），普通弓手只读。世界边界失效由既有
  监督器负责，无需本文件额外接线。
- 弩手（`PatchRoles_Crossbowman` / `PatchRoles_CrossbowDefense`）：`DefenseSpacing` 内已按
  `IsCrossbowman` 分流；本文件只认 `MusketeerIdentity.IsUnit`，两者互不误伤。
- 塔位/骑士岗位：`MusketeerRuntime` 自己的 `AllowJob/AllowSlot/EvictSlotted` 已在管（见 §8）。
- 存档：无新增 schema；侧别沿用原生 `_guardSide` 字段（原生本来就会随存档往返），本文件不落盘。

## 5. 测试覆盖（`tests/musketeer-defense/`）

`Program.cs`（控制台回归，直接执行生产 patch 方法体）覆盖任务书验收项：

- `allleft4 → 2/2`（只写两名、深度互不重复且浅）；
- 奇数（3/5 全左 → 2/1、3/2）、`six-left → 3/3` 且 3 个 mover 深度互异；
- 新人补少侧 / 中性并列走 Left（同原生单弓手奇偶）——**夹具用"prefix → 模拟原生改写 → postfix"
  三段式**，新职业的先前侧别在捕获时确实是中性（首轮夹具错误已修，见头部修订说明）；
- **单侧完全安全**（`safeLeft=true`）时原生 4 人全左批次仍 **2/2**，重复一轮 **0 写入**；
- 重复一轮 **0 写入**（稳定）；原生翻回错误侧时**只纠偏该单位**并沿用其原生深度（99 → 99，无自造上限）；
- 死亡（`isDead`）离开子集、注销/离场后剩余重新收敛；
- 普通弓手（未标记）零写入；单个火铳手不被强行拆；
- 关配置/联机（`Enabled=false`）零写入且无捕获；
- 异世界/非当前层单位排除且不计数；
- 号角 override、缺墙 → 整套跳过；
- 17 种单点排除（骑士/塔位/塔标/原生不可用/上船/待上船/编队/玩家操控/隐藏/inert/grabbed/stationary/
  死亡/塔高/未标记/非激活/异世界）逐一验证"该单位零写入且其余三人仍 2/1"；
- 深度占用普查：普通弓手已占 0/1 → mover 落到 2，普通弓手零写入；无待写单位时**不调用扫描**；
- 嵌套原生分配（内层不捕获、外层仍收敛、内层 postfix 零写入）；异常遗留捕获自愈（stale）；
- canary 每世界一条；空 registry/含 null slot/空王国/异王国/null 世界均安全；
- 全程 0 Destroy、0 自身 FindObjectsOfType；唯一发现来源 = `MusketeerIdentity.CopyUnits`（registry），
  占用普查走共享 `UnitScanCache`。

**未运行**：本 worker 无 shell，未执行任何构建/测试；首轮 Operator 已跑出并回报 1 处夹具失败（已修）
与 `Interop.csproj` 0W0E。修改后需重跑（见 §6）才算验收通过。

## 6. Operator 验证清单（必须）

```powershell
C:/Users/ADMIN/dotnet8/dotnet.exe run --project tests/musketeer-defense/Tests.csproj
# 期望：PASS <N> assertions（无 FAIL 异常）
C:/Users/ADMIN/dotnet8/dotnet.exe build tests/musketeer-defense/Interop.csproj
# 期望：0 warnings / 0 errors，且对着真实 E 盘 2.4 interop
```

obj/bin 已在 `tests/musketeer-defense/Directory.Build.props` 里按项目名隔离
（`obj/<ProjectName>/`、`bin/<ProjectName>/`），两个工程不再共用 `obj/project.assets.json`；
命令行 `-p:BaseIntermediateOutputPath=… -p:OutputPath=…` 仍可覆盖（全局属性优先）。不提交任何产物。

**必须由 interop 编译核对的 2.4 API（本文件全部源码级猜测点）**：
`Kingdom.DistributeFreeArchers`（`nameof` 也验证可见性）、`Kingdom.HasOverrideGuardPosition()`、
`Kingdom.intactWall`（`Sided<GameObject>` 索引器，先例 `kingdom.borderBanner[...]`）、
`Archer.SetGuardSide(Side, Int32)`、`Archer.isAvailable`、`Archer.harmless`、`_guardSide`、`_guardDepth`、
`_knight`、`_guardSlot`、`inGuardSlot`、`_embarkee.IsEmbarked/EmbarkableTarget`、`GetFormation()`、
`ShouldPlayerControl()`、`_character.inert/grabbed/isStationary`、`_damageable.isDead`。
其中 `SetGuardSide/_guardSide/_guardDepth/isAvailable` 已有 2.4 interop 元数据实锤；
其余由 interop 编译核对——**Operator 首轮已回报本工程 0W0E**，第二轮改动只**减少**了 API
（删掉 `Managers.enemies` / `EnemyManager.IsSideCompletelySafe`），并新增 mod 内部
`UnitScanCache.GetArchers(float)`（InteropStubs 同签名替身），不引入新的原生 API 猜测。
**任一编译失败先停在 Operator，不要部署**（`PatchAll` 是全有或全无：运行期缺方法会让整个插件加载失败）。

运行期（实机）证据点：进入游戏后日志应出现 1 条/世界
`[MusketeerDefense] native distribution boundary active (canary)`；买枪后在任一次分配事件
（昼夜切换等）后应出现 `rebalanced: units=4 … moved=2 … left=2 right=2`；随后 4 名火铳手 2 左 2 右，
且重复事件不再出现新的 rebalance 行（稳定）。

## 7. 未覆盖 / 限制 / 待决策（不得宣称完成的部分）

1. **实机未验**：本文件只做了代码级修复与回归代码；"4 名火铳手实机 2/2、稳定不抖、不误伤普通弓手"
   需要 Operator 构建+实机/日志验证。
2. **2.4 方法体未反编译**：同 §2；若 interop 编译通过但实机 canary 不出现（thunk 未命中，坑 17 类），
   Operator 应回报，由 Operator 决定是否换边界（例如 `Kingdom.DistributeArchers` 或
   `Archer.SetGuardSide` 调用点），本文件不得自行扩大范围。
3. **单侧完全安全之夜（行为已按复核修订改变，但仍有限制）**：现在子集照样平分（2/2），因此
   **受保护侧的 2 名火铳手会守安全侧的墙**（用户要的"子集自身均衡"），不再整批蹲安全侧；
   普通弓手仍按原生危险分配（受威胁侧主力不变）。副作用：这类夜里原生每轮都会把靠左的子集成员
   重新分回安全侧，本文件在同一 postfix 内再改回决定侧 → 每个分配事件可能产生 1~2 次**幂等**纠偏写入
   （写入值恒定、不发目标、不位移，仅字段；分配事件稀疏）。可接受，但属于"与原生集中策略并行"的
   既有代价；若未来要消除写入，可改为让子集在安全侧主动让位（另一条策略）。
4. **生产/转职瞬间**：新火铳手绑定发生在 `Promote` postfix，晚于 `OnEnable→DistributeFreeArchers`，
   因此**新买的一把在下一次分配事件前保持原生侧别**（不改变现状，不作弊补写）。
5. **缺墙/号角 override/联机**：整套跳过（fail-closed），行为与原生一致。
6. **深度占用普查的粒度**：占用集来自共享 Archer 扫描（≤3s 缓存）且只统计"墙防守位型"成员；
   塔位/骑士随从/异世界/非激活不入集（它们不按深度站位）。若扫描缓存恰好是旧拍，占用集最多旧 3s
   （与 DefenseSpacing 同口径），影响仅是可能选到刚空出的槽或让开刚占用的槽——都会在下一拍自愈，
   不移动任何普通单位。

## 8. 附带发现（本文件之外，仅报告，未改动）

- `HeroArcherTowerPolicy.AllowJob/AllowSlot` 只保护英雄（`HeroArcherRuntime.Enabled &&
  HeroRecruitment.IsPurchased`）；**火铳手不在其列**。但火铳手的塔位/骑士岗位拒绝由
  `MusketeerRuntime` 自己的 `AllowJob/AllowSlot/EvictSlotted` 承担（已读源码，含
  `Archer.IsAvailableForJob/AssignJob` 钩子与原生 `ExitGuardSlot` 收口）。两者职责重叠但不冲突；
  若 `MusketeerRuntime.EvictSlotted` 用尽重试上限，理论上可残留塔位（既有实现边界，非本轮引入）。
- 两个 `AllowJob` 对 Knight job 的取舍不同：`HeroArcherTowerPolicy` 放行（保留原生 AssignJob 的
  Knight 优先），`MusketeerRuntime` 拒绝（防止骑士 `overrideShootCooldown` 抹掉间隔）。属既有设计
  差异，本文件不改；只作为"岗位边界不在本文件"的复核记录。
- `MusketeerIdentity.CopyUnits`/`IsUnit` 即为本文件的唯一发现/身份来源；未新增任何 Identity 接口，
  未触碰 `MusketeerIdentity`、`MusketeerRuntime`、商店与存档文件。

## 9. 复核修订记录（第二轮，透明留档）

1. **首轮测试失败根因（夹具错误，非生产逻辑）**：`Program.cs:173 neutral tie: goes left` 失败。
   夹具把"原生改写后的侧别"直接写在 `Distribute` 调用**之前**，于是捕获到的先前侧别是 Right 而不是
   中性 → 该单位根本没走"中性补位"路径（计数 1/2，无中性可放，字段与决定一致 → 零写入），断言必然失败。
   首轮 test 6 有同样建模错误，只是碰巧经"超载侧纠偏"路径得到相同结果而通过。
   修复：新增 `Distribute(kingdom, Action simulateNative)` 夹具，严格按
   **prefix 捕获 → 模拟原生改写 → postfix 决策** 三段执行；test 6/7 改为在模拟阶段改写侧别。
   生产逻辑本身在该场景未发现缺陷（逐行推演 + 新夹具复算一致）。
2. **政策修订（按 Operator 复核指令）**：`SplitOpen` 删除 `IsSideCompletelySafe` 不对称门——
   用户要的是枪械子集自身 2/2，单侧完全安全时也必须平分；号角 override、缺完好墙、船/岗位/生命周期
   门全部保留；普通弓手原生危险分配不受影响（本文件从不写普通单位）。
   新增回归：4 人原生全左 + `safeLeft=true` → 2/2，且重复一轮 0 写入。
3. **深度去重与不发明区间**：删除自造 `MaxReusedDepth=32`；纠偏写入沿用单位自身原生深度（99 也保留），
   被移/新放取"该侧最小未占用深度"；占用集现在包含**未被改动的普通弓手**（共享扫描只读，绝不写），
   无待写单位时完全不调用扫描。新增两条回归：普通弓手占 0/1 → mover 落 2；平衡轮不扫描。
4. **工程隔离**：`tests/musketeer-defense/Directory.Build.props` 按项目名隔离 `obj/`、`bin/`，
   `Tests.csproj` 与 `Interop.csproj` 不再共用 `obj/project.assets.json`；不提交任何产物。
5. **接口范围变化**：删除 `Managers.enemies`、`EnemyManager.IsSideCompletelySafe`（减少 2 个核对点）；
   新增 mod 内部依赖 `UnitScanCache.GetArchers(float)`（InteropStubs 同签名替身）。
   **集成需求：无**（全程序集 `PatchAll` 自动注册，无需接 Main/插件）。
   待 Operator 下发的"不存在 API 清单"到达后，逐条与本文件 §6 清单对表；若清单包含本文件仍在用的
   API，按清单结论改边界或降级（不在本文件内擅自扩范围）。
