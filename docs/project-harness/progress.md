## 2026-08-17 — bank-assistants-005：捡币改链式顺吸（reviewer 两轮通过，待部署）

- 用户反馈：助手逐枚"定位→走→捡→停→等下个扫描节拍"卡顿严重，跟不上扔币节奏；原生银行家是
  批量认领+Wallet接触吸附一路连吸。根因三层：结算后`Target=null`+动画归零停死；下一枚分配只在
  `ScanAndDispatch`节拍（0.5s）；单收集者+逐枚0.22精确定位。
- 修复三件套（只改`PatchEconomy_BankAssistants.cs`，助手无Wallet/仅权威侧/Deposit原子入账等
  不变量全保）：**链式目标**——每枚结算当帧`TryChainNextTarget`接最近未认领成熟币（认领失败退让
  次近候选），奔跑动画全程不停；**顺路扫吸**——移动中`SWEEP_RADIUS=0.35`内成熟币走与目标币完全
  相同的认领→`CanCommitPickup`→`SetFake/pickedUp`→`DepositFromAssistant`→池回收事务，多币认领
  原策略用独立`SweepPolicies`字典按币记录回滚；**积压扩容**——`_collectorIndex`单值改
  `ActiveCollector[4]`集合，目标活跃数=1+成熟币/8上限4，轮转补位。`SCAN_INTERVAL`0.5→0.3。
- 委派链：worker=OMP deepseek-v4-flash thinking=max；reviewer=GLM5.3 subagent（kimi K3当月
  配额403耗尽按协作规范回落）——首轮**changes_requested**揪出两个真bug：①`AssignNextTarget`
  单候选穿透（最近币被村民原生认领时每0.3s回家瞬移循环最长20s）②`SelectNextCollectors`轮转
  跳位（3-4并发只激活3个）。Operator各≤10行修复后复核**approved**；另按WARN加了联机门禁
  预检（防client未追上时O(N²)空转帧尖峰）。经济原子性/认领一致性/状态机首轮即全PASS。
- 独立构建0 warning/0 error；DLL 204,800 bytes、SHA-256=
  `7E1E9B80BE388FB763F349F08025FD7D02DAE5E0B4CAC9580AD2D363B143A161`；checklist validator
  0 warning。待用户退出后部署E盘实测：沿币串一路跑一路吸无停顿、积压≥8出第二助手、
  主银行家行为不变。

## 2026-08-16 — special-tower-rebuild-018：交互不出现根因=源prefab解析到基座资产（候选集修复待部署）

- 用户实测驻守工匠修复版（A05A6551）仍无重建交互；23:50会话日志仅开局一条
  `Ready source=Tower Ballista`，全程无Blocked——CanSelect从未作用于重建payable。
- 根因实证：存档（`Release/global-v35`，gzip解压grep）已建弩箭塔prefabPath=
  `Prefabs/Buildings and Interactive/greece/Tower Ballista_greece`（2座），而补丁在
  PoolManager.Init前缀经Tower6基座模板route+GetAssetSwap解析到**基座资产**`Tower Ballista`
  ——组件加错资产，真实建造/恢复实例全来自`_greece`变体（坑24：PayableManager只对已注册
  payable调CanSelect，故静默无日志）。上轮"驻守工匠阻断"修复非主因，保留。
- 修复：EnsurePrefabLayout改候选集——GetAssetSwap结果（try/catch）+`Resources.LoadAll<Ballista>`
  按名含"Tower Ballista"扫描，安全检查（无FireTower/OilFireArcherTower/TowerKnight）通过的
  全部候选幂等配置（HashSet+biome重置）；Ready日志列出全部源名，跳过候选汇总输出。惰性克隆
  （FastSpawn→FastClone→Instantiate(_prefab)）保证Init时配好即遗传给恢复实例。
- worker=OMP deepseek-v4-flash thinking=max；Operator逐行审查（46行删除全属旧单源段，网关/
  token/prepare逻辑零改动）；独立构建0 warning/0 error。本增量未启用独立reviewer（仅prefab解析、
  不触付款/RPC契约，协议规则2裁量）。坑24沉淀；checklist validator 0 warning。禁部署Debug构建DLL为
  201,216 bytes、SHA-256=`CC3EC9F59C70D2218228B50FE7A17D02A7C8AB112C1D01F03483B63B0B2ADD8D`；
  修改后源码SHA-256=`C1CE18B2B88E0B2B0174F0EF591E1AD202F888342CC2C480C4E8F367D2741159`。
- 待用户退出游戏后部署E盘副本；实测验收点：启动日志`Ready sources=[... Tower Ballista_greece ...]`、
  已建弩箭塔出现18金币提示、付款回六级塔。

## 2026-08-16 — ghost-squads-013：希腊幽灵leash处决改边界驻守候选编译通过（待部署）

- 用户报告希腊亡灵小队一直向外冲锋、超距即集体死亡。根因为原生设计：`WarriorGhostLeaderGreece`/
  `WarriorGhostGreece` 的 `StartDeathCountdown` 即"离召唤者超`_maxPlayerDistance`处决"，而其冲锋AI
  无敌人时每秒向营火反方向推进，站桩玩家必然看到小队冲出边界自杀（D22，非mod引入，四队扩展放大了暴露面）。
- 修复=边界驻守+定时消亡：Prefix拦截两个Greece类的`StartDeathCountdown`（mod关闭/无世界权威走原版），
  监督协程每0.5s检查，`|dx|>=上限−1`时`ForceStop()+Pause(0.75)`钉住驻守（砍击/射箭照常，玩家回接近
  自动恢复冲锋）；60s到期`KillUnit()`补消耗机制，否则`HasGhosts`门会锁死技能。Summoner丢失/异常/
  启动失败均兜底，不留永生幽灵。北境行为与GhostSquads既有逻辑零改动。
- 委派链（新协作规范首次执行）：worker=OMP `deepseek-v4-flash` thinking=max（沙箱只读无法自建，
  AST+逐符号语义核对后由Operator独立构建0 warning/0 error）；reviewer首选OMP `kimi-code/k3`因
  当月配额403耗尽，按备选顺位回落GLM5.3 subagent thinking=max，结论approved——核对Mover暂停
  Max语义下0.5s+0.75s钉住节奏数学无间隙、`yield break`在try-with-catch内合法、KillUnit回收链完整、
  联机parity；两个非阻塞观察项（弓箭手Shoot收尾UnPause的有界抖动、HelsHead若接希腊prefab同样驻守）。
- 新增仅`il2cpp/PatchDivine_GhostLeashHold.cs`（源码SHA-256=
  `8CFC3101AB89831A5411E63579CBDCE65284153DAB28775D89BA4EF46436A478`）；禁部署Debug构建DLL为
  200,192 bytes、SHA-256=`06AE5B2D4DF9D55CF533225FB00E4385C0E693D27C0DD23B31FB0C1D0EE86ADF`。
  同步D22、checklist validator 0 warning。用户退出后已于23:58部署E盘独立副本（旧DLL备份
  `KingdomEnhancedMod.dll.before-ghost-leash-hold-20260816-2358.bak`），待用户实机反馈
  （驻守距离、60s消亡、北境不变、HasGhosts解锁）。

## 2026-08-16 — special-tower-rebuild-018：驻守工匠交互修复静态通过

- 实机日志只有`Ready source=Tower Ballista target=Tower6 price=18 biome=5`而没有`Rebuilding`；根因是旧候选
  把正常常驻工匠视为阻断。现已移除人数门禁，不清actor、不改职业/钱包/存档；旧塔由原生Pay销毁后，
  当前与排队工匠在下一次工作循环观察Unity-null并走原生清理。
- bolt可失败回收已移到离线最终CanPay成功之后、TransactionComplete之前。失败取消并退币；成功用同帧
  payable/player/world/scene token进入原生Pay。部分回收不会重新挂失活bolt，而是归一Reloading/currentWork0。
- 在线首版整体fail closed，避免付款RPC批准后的主客分叉；本地分屏仍支持。新增按实例与原因变化、30秒
  限流的阻断诊断。worker构建0 warning/0 error，reviewer最终APPROVED；源码SHA-256=
  `C7FF31FFD1E6D025D63CCD615AB582D9B2A3A7E88C57C784B42374B461CA3F78`，禁部署DLL SHA-256=
  `113BE01ED8F8ABAAD52571DCEF74829A14CB7B7B4F5210191AE8F804CF0D6696`（196,608 bytes）。
- 代码与首轮文档已由提交`1f9f988`推送；从干净提交重建0 warning/0 error。确认游戏进程为0后只部署
  E盘独立副本，构建/部署DLL均为196,608 bytes、SHA-256=
  `A05A6551061C48DE4ADB20BCC6290D1948638F27C06DB6B19D5026F48E82514E`；原192,000-byte DLL已备份为
  `KingdomEnhancedMod.dll.before-special-tower-worker-20260816-2225.bak`。当前ZIP未刷新。

## 2026-08-16 — special-tower-rebuild-018：首版已部署，刷新ZIP待生成

- 用户退出后确认游戏进程为0；从已推送提交`703be83`重新构建0 warning/0 error，并只部署E盘独立
  测试副本。构建/部署DLL均为181,760 bytes、SHA-256=
  `947131C76EF465B35AC21862E273E29D87AB0A8C2D97136E9CA15062F97E9CBD`，覆盖前旧DLL已保留备份。
- 本轮仍只开放安全空闲Ballista付费重建为当前biome原生六级普通箭塔；Fire/OilFire/Knight/
  Berserker/Baker/Mead来源继续fail closed。静态与部署门禁通过，保存往返、跨岛、分屏和联机待实测。

## 2026-08-16 — 刷新测试候选：友好巨魔闭环与视觉比例

- 用户退出游戏后确认 `KingdomTwoCrowns` 进程为0；当前IL2CPP源码重新构建0 warning/0 error，
  只覆盖E盘独立测试副本。构建与部署DLL均为174,080 bytes，SHA-256=
  `116972F641D20C2801F3113C12F7B94B6DEF23B33F29684786994321071A5749`；旧DLL另存为非DLL扩展名备份。
- 本次刷新候选收录已获实机核心闭环的友好巨魔反制：7次友好目标注入、6次真实原生伤害，六个目标
  均降至0生命；仍保留关闭恢复、Squid/CrownStealer、换岛和联机边界为待回归项。
- 同包收录Dead Lands银行助手绝对y=1.25（北境仍1.2）与火焰塔隐士绝对y=1.25；只改视觉y，
  不改变经济、调度、朝向、Passenger/Roaming或网络逻辑。玩家说明已从“下一候选/待部署”更新为
  “本次刷新候选已包含”，待干净提交后生成直装ZIP并校验三方DLL哈希。

## 2026-08-16 — friendly-troll-balance-008：反制追击修复静态通过，待部署

- 最新实机日志已经确认反制单位的稳定 10% 指定阶段生效，本轮共观察到 9 个被指定的 TrollWeak；但没有出现候选注入或原生伤害证据，因此当前只能确认“标记成功”，不能确认“已经攻击友好巨魔”。
- 新增一次性四阶段诊断：友好巨魔登记、反制巨魔进入原生目标查询、友好目标被临时注入、友好巨魔收到该反制巨魔的原生伤害。每阶段按实例或稳定身份去重，不做每帧日志、不全场扫描，也不改变概率、AI、目标、伤害、RPC 或对象池。
- 伤害诊断只订阅活动 FriendlyTroll 自己的 OnReceiveDamage，并在回池、失活、组件指针变化时精确解绑；未使用全局 Damageable 热路径 Harmony patch。worker 构建 0 warning / 0 error，独立 reviewer 静态 APPROVED；提交 `045994d` 已推送。确认进程为0后已只部署独立测试副本，构建/部署 DLL SHA-256 均为`33C23C6C780B26550453C4320D4C35B980B4E391BF8802C757EF2A40FD2C34C5`（167,936 bytes）；release zip 未刷新，待实测四阶段日志。
- 实机四阶段结果定位到设计缺口：41个友好巨魔与8个反制巨魔都正确进入登记/索敌，但原生查询半径只有2，注入与伤害均为0。新修订增加单一0.25秒中央追击器，只在普通行走态和2～10格外圈内朝最近友好巨魔移动；2格内完全交回原版冲撞。当前8×41规模约每秒1,312次简单距离比较，无全场扫描、LINQ、RPC或新池。源码SHA-256=`12700B854332A2CB8F12A21BD8669731321C5AD2358C6F9CFE1626A99375574E`；提交`3ee2be7`已推送。确认进程为0后只部署独立副本，构建/部署DLL均为`8F122777143698C2FD0F51D0BE1E388849C4802C1DE299F93A2CA2918AAB72BF`（172,032 bytes）；release zip未刷新，待实测。

## 2026-08-16 — save-repair-017：当前存档火焰塔隐士Passenger恢复已原子执行

- 用户授权修复当前Call of Olympus存档中未生成的火焰塔隐士。原生证据确认Fire=index6、
  Passenger=5，且规范Passenger状态为`player=0/land=0`；用户同意只将campaign/currentReign两份
  Fire `position`从0改为5，不创建第六座小屋、不插入Hermit/CRPC/NetID对象。
- 游戏退出后锁定输入817,111 bytes / SHA-256=`C3A8CEF5B3B59B0C4A763235B138381ED6327ABAAA2311F95530624AC17E55E8`。
  全campaign 11,665对象中无Dynamic/non-Dynamic netID980、无三种Fire名称、无既有Passenger。
- 专用默认dry-run脚本经worker/reviewer逐行审查；真实dry-run得到candidate SHA-256=
  `5C43780197C30F2B2F843D7139A5281A76CD836C9295F9307310F9A24FEE0DFE`。reviewer明确`APPLY_APPROVED`
  后原子执行，新备份保持原输入hash，最终源818,055 bytes且hash与candidate一致。
- 写后独立复读：before两份Fire均0/0/0，after均5/0/0；归一两处position后整root DeepEquals=True，
  reviewer最终`EXECUTION_APPROVED`。尚待首次读档携带、放下变Roaming、火焰塔升级与重复读档/换岛验收。

## 2026-08-16 — 隐士视觉与友好巨魔追击微调已部署，待实机

- 希腊弩箭塔隐士按 `HermitType.Ballista` 精确设为 y=1.20，骑士塔隐士按
  `HermitType.Knight` 精确设为 y=1.05；两者沿用现有 OnEnable/ScaleRegistry/OnDestroy 生命周期，
  只改变 y，保留 x 朝向、z、能力与其他隐士。
- 友好巨魔只把原生追击速度从 2 提高到 3（1.5倍），把索敌距离从 10 提高到 20（2倍）。
  冲撞速度、冲撞距离、伤害、冷却、Squid/CrownStealer 筛选与约10%反制巨魔机制均不变。
  每个对象池实例从原 profile 计算目标值；关闭 Mod 与回池前恢复，避免重复累乘。
- 独立 reviewer 静态 APPROVED；Debug 构建 0 warning / 0 error。用户退出后确认游戏进程为0，
  只覆盖独立测试副本；构建/部署 DLL SHA-256 均为
  `8571E740D8CD4C94E5552D13B7CD1AC5D3124FF863733191257A864B4E92FB94`（164,352 bytes）。未写Steam、
  未重打zip；待实测两类隐士朝向、巨魔3/20、关闭恢复与回池复用。

## 2026-08-16 — crash-unload-016：出航卸载栈溢出首修候选

- 02:45 与 02:54 两次崩溃的 `Player.log` / `Player-prev.log` 均以同一末链结束：旧岛保存完成后进入
  `Managers.PrepareUnload`，level 层级禁用触发持盾 Worker 的 `NpcShieldUser.SetShieldEnabled(false)`，
  随后在 `pickupShieldSound -> AudioPool/AudioEmitter.ResetAndPlay` 出现 disabled audio source，Windows WER
  均记录 `0xc00000fd` 栈溢出。相同 StackHash 在稀疏工具分配部署前已经出现，不能归因于工具优化。
- 最窄首修只在 Mod 启用且 `PrepareUnload` 同步作用域内，临时屏蔽带盾 Worker 的收盾音效；原生盾牌状态、
  子物体、事件、再生、编队、碰撞力和 RPC 全部继续执行。正常/异常路径分别由 Postfix/Finalizer 幂等恢复，
  下一场景还会无条件清除任何陈旧作用域；关闭 Mod 时完整走原版。
- 当前源码已禁部署构建 0 warning / 0 error，DLL SHA-256=`ACC466D928534F7620F7610A9C20590F301FAA617DA33EF96B53DBAEDD21D0A9`。
  这是高置信、可逆的因果候选，不是已完成的运行时证明。用户退出后已确认游戏进程消失，并已只覆盖
  独立测试副本；构建与部署 DLL SHA-256 均为
  `ACC466D928534F7620F7610A9C20590F301FAA617DA33EF96B53DBAEDD21D0A9`（162,816 bytes）。
- 运行时门禁保持：高人口岛连续至少两次完整出航并进入新岛、无新增 WER 栈溢出、卸载摘要
  `suppressed > 0` 且不再出现对应 disabled-audio 末链；平时拾盾/破盾声音与联机盾牌状态不得回归。

## 2026-08-16 — tool-assignment-015：先部署零行为探针，再决定稀疏替换

- 高人口岛的原生工具分配每约3秒运行一次，并以注册居民数构建大矩阵；当居民远多于工具时，
  这是清理多余人口后仍值得优先处理的周期性性能尖峰。
- 2.4虽公开化`DroppableRegistrar.ReassignClaimers`包装器，但原生内部调用可能绕过Harmony thunk。
  因此第一阶段只加入完全放行原版的Prefix/Postfix探针，每个Registrar最多记录前4次居民数、工具数、
  调用间隔和原算法耗时；不写目标、不写claim、不替换算法，也不会持续刷屏。
- 探针源码提交`20c457b`已推送。用户退出后从该提交Debug重建0 warning/0 error，并只部署独立测试副本；
  构建/部署DLL SHA-256均为`BDC91E72BF5B287E4BF3DD8BDEEB3CCF57B6B2C32D03FA742074024855F3E723`。
  实机日志连续命中：582 carriers、7～8 droppables，后三次间隔约3秒，原版耗时约9～10毫秒。
- 第二阶段现已实现：只在carriers不少于128且eligible tools不超过四分之一时，用原生评分缓存与补丁私有
  JobAssigner求解工具×居民小矩阵；目标仍经居民自身接口两阶段更新。全局JobAssigner、其他工作系统、
  资格与claim协议不改。与Horn隐士y=1.15一起提交为`147ea44`并推送；用户退出后从该提交重建
  0 warning/0 error并只部署独立测试副本，构建/部署DLL SHA-256均为
  `2CE66091A760E0ECE0455B5B2599371156CB295EDC8E2522FEE7F292D72ADF09`。
- 本轮一次切岛闪退由Windows记录为`coreclr.dll / 0xc00000fd`栈溢出，末尾位于场景卸载；探针4次后已停止且
  没有相关异常，当前不能归因于探针。后续必须重复切岛；若复现则先停发并单独定位。

## 2026-08-16 — role-qol-001：号角隐士 y=1.15

- 2.4枚举确认号角隐士为`HermitType.Horn`，与Horse马厩隐士是两个独立类型。
- 沿用Baker/Horse现有生命周期，只在Horn启用时绝对设置localScale.y=1.15并登记ScaleRegistry，
  OnDestroy精确注销；x朝向、z、能力、其他隐士及存档均不改。已随上方候选部署独立副本，待观感确认。

## 2026-08-16 — ability-cooldowns-014：两项30秒冷却微调为22.5秒

- 2.4资源实读确认 HermesStaff 基础冷却为30秒且每只转化目标附加值为0；Cerberus 召唤冷却也为30秒，
  并在最后一名亡灵消失后才开始计时。本候选统一缩短25%，目标均为22.5秒。
- 只修改冷却配置：法杖控制范围/上限/永久性与四支亡灵小队的数量、希腊/北境行为、持续时间、
  回收、对象池和RPC均不改变。源码提交`14ebb6f`已推送；游戏退出后干净Debug重建0 warning/0 error并只部署
  独立测试副本；最终刷新后的构建/部署DLL SHA-256均为
  `0744BC6B6A55D1792EB95391988D9D9400091255F7D934AD1DF1D16437BA037F`。刷新候选包已通过结构、UTF-8、
  源码排除与三方DLL哈希门禁；功能保持doing，等待用户实机计时。

## 2026-08-16 — 希腊北境外观居民统一 y=1.125

- 用户确认希腊世界的北境外观居民 y=1.05 视觉上过于接近原尺寸，要求与真正北境居民统一。
- 普通 `Peasant_norselands` 与乞丐晋升生成的 `WarriorPeasant` 两条路径现在都绝对设置 y=1.125，继续只保留 x 朝向与 z，不改角色行为、转职、配色或网络逻辑。
- 游戏当前运行中，因此本轮只进行源码/文档修改与禁部署构建；提交推送后等待退出再部署独立副本和刷新候选包。

## 2026-08-16 — 主银行家提款目标 39 → 100

- 当前2.4资源的`playerMaxCoins=39`与Mod钱包容量2000不匹配；原生提款量为`min(ceil(国库*0.25)+银行家随身金币, playerMaxCoins-玩家金币)`，并以每0.15秒1枚生成物理金币。
- 为避免直接提高到2000导致最长约5分钟持续吐币和大量物理对象，本候选只把Enabled状态下的目标提高到100；25%比例、逐枚节奏、账本与助手逻辑不改。
- WorkProfile新增原`playerMaxCoins`捕获，Mod Disabled时与扫描/速度参数一并恢复，避免同一Banker实例残留增强值。Debug构建0 warning/0 error，独立静态审查APPROVED；等待干净提交重建、独立副本部署与提款实测。

## 2026-08-15 — fleetboat-recovery-009：候选已部署，等待实机验证

- 用户提供的异常存档显示四个 `GodIsland*` 神像交付任务均 completed，但 carryForward 小船数为 0、
  所有岛无 FleetBoatSaveData，且最近载入为死亡换君主后的 sailingIn。已在游戏未运行时备份原始
  `global-v35` 至 `Release/KEM-backups/global-v35.before-fleetboat-recovery-20260815-162933`，
  767,054 bytes，SHA-256=`1D50D6CE1B0DD49D30F85C0BB8B57BB88C0AFE599FC718B18F705E93D7359822`。
- 新增 `PatchWorld_FleetBoatRecovery`：只在非 challenge 的 Greece campaign/scene 与 world-authority
  生效；ApplyToScene 前捕获 carry 所有权目标，原生完整返回后按 active 优先、否则 standby 的唯一表示
  计算缺口。四个 GodIsland 交付任务给出 0～4 所有权下限，绝不把 active/standby/carry 相加。
- standby 与 active 严格互斥恢复；riverless/前置不完整或首个生成失败时才在零 active 状态回退 standby，
  active 部分成功后不混写 standby。生成只复用当前 biome 原生同步池，无新 RPC/syncID/sidecar。
- worker 与独立 reviewer 静态 APPROVED；源码提交 `7710977` 已推送。随后从干净提交重新 Debug 构建，
  0 warning / 0 error，并在确认游戏未运行后只覆盖独立测试副本。构建与部署 DLL SHA-256 均为
  `774F5ACFF413C76493456596ADE35D905C58CC9299F054747266FF2CF09607F3`。当前公开 zip 未刷新，
  运行时任务保持 doing，等待异常档首次恢复、重复读档、换岛和死亡重生门禁。
- 用户首次载入后体感未看到四艘船，但只读证据确认恢复已实际发生：日志唯一摘要为
  `expected=4 active=0 standby=0 carry=0 desired=4 missing=4 recovered=4 mode=spawned-from-zero`，之后无
  FleetBoat/unknown pool/duplicate syncID/RPC异常；20:08 autosave 的当前第3岛含4个 `FleetBoatSaveData`，
  BoatNumber=1～4、CurrentState=Idle，位置 x约38.31～41.26。当前不重复补船，避免制造8艘；先沿登陆点、
  水道和左右外墙确认视觉位置，必要时下一候选只加一次延迟位置诊断。
- 随后换岛日志确认原生 carry-forward 已生成4艘、恢复补丁未补船，但新岛四个 Idle 实例最终停在完全相同的
  x=37.96。死亡前旧正常档证明四船本应保持同一侧并按 BoatNumber 约1单位错开，问题是原生换岛生成后没有完成横向归位。
- 第二阶段已实现并经独立 reviewer 静态 APPROVED：ApplyToScene 后由单批次runner等待2～4艘船全部 Idle、编号唯一、
  原生side/base及Mover/FSM有效，再仅调用一次原生 `UpdateBase(true)`。不改side、状态、坐标、数量、任务、standby、
  carryForward、对象池或RPC；活动/编队/航行状态只等待或超时。提交`e643d9f`已推送；从干净提交重建0 warning/0 error，
  并在游戏未运行时只部署独立测试副本。构建/部署DLL SHA-256=`8A829791422A575A4157DC036F943DC7446FE8C98600D080BA686A57E5A6F039`，待实机。

## 2026-08-15 — role-qol-001：马厩隐士 y=1.10 已部署独立副本

- 2.1/2.4 双端核对确认吹笛解锁、用于马厩升级的隐士是 `HermitType.Horse`（标签
  `HermitHorsekeeper`），不是 `HermitType.Horn`。沿用既有缩放守护：Horse OnEnable 绝对设置 y=1.10，
  保留x/z；OnDestroy精确注销，Baker仍为1.15，其他类型零写入。
- worker实现与独立reviewer静态APPROVED；源码提交 `82333a1` 已推送。用户退出后从该干净提交重新
  Debug构建0 warning/0 error，并只覆盖独立测试副本；构建/部署DLL SHA-256均为
  `BAF335AF932260819F01AAC3F9C93D4B3C4E1F22FF0FDA58075A8DE339E435D6`。未打包或启动游戏，
  等待Horse=1.10、Horn/其他隐士不变的观感验证，任务保持doing/review_approved。

## 2026-08-15 — candidate-package-007：友好巨魔与视觉微调候选已刷新

- 用户退出游戏后，从干净提交 `b875c10ca421fe96106c83dfac913c1bd4778f9f` 重新构建并仅部署到
  独立测试副本；Debug 构建 0 warning / 0 error。构建、独立副本和 zip 内 DLL SHA-256 三方均为
  `E8B06EC90772390262F5D3B1325059097391EBE0D04E6CC5E479BE66DBECB8BD`。
- 刷新后的 zip SHA-256=`4C44CDCC79B4CF30E58EE6CA20087692B797FA629055D02D61CACC436744832C`，
  40,565,824 bytes / 312 entries；manifest commit 与构建提交一致、Dirty=false。插件 DLL 恰 1、
  root dotnet 187、BepInEx/dotnet 0、版本顶层目录 0、required entries 无缺失、反编译源码条目 0，
  包内 20 个常规文本项及 `.doorstop_version` 严格 UTF-8 通过；独立 reviewer 最终 APPROVED。
- 本包新增包含：友好巨魔只排除 Squid 并恢复 CrownStealer 为正常目标、约 10% TrollWeak 反制单位、
  Dead Lands/北境银行助手 y=1.2、希腊普通居民及乞丐晋升居民 y=1.05。静态审查已通过；战斗 canary、
  实际冲撞与视觉观感仍是运行时门禁，相关功能任务继续保持 doing。Steam、共享存档、Mono 未修改。

## 2026-08-15 — 视觉微调：Dead Lands 助手 y=1.2、希腊居民 y=1.05

- Dead Lands 银行助手从上一测试包的 y=1.25 调回绝对 y=1.2，与北境助手一致；只改双方确定性
  prefab 的 y，欧洲/幕府比例、x 朝向、收币调度和经济逻辑均不变。
- 希腊世界的普通 Peasant（包括映射使用的北境外观）与乞丐晋升得到的 WarriorPeasant 统一为
  绝对 y=1.05；真正北境世界的 Peasant_norselands 仍保持 y=1.125。只改 y 并继续登记现有
  ScaleRegistry，不改变转职、配色、网络或行为。
- 初始源码构建 DLL SHA-256=`E13F6836F79DBEE630FC3ED3FCB3CC2848B3CE6A7C3015C0102EDF7F13A0A02A`；
  游戏退出后已随上方综合候选重新构建、部署并打包，等待实机观感确认。

## 2026-08-15 — friendly-troll-balance-008：候选已部署，待实机验证

- 友好巨魔选敌现只精确排除长期悬空的 Squid；旧的 CrownStealer 排除已删除，未使用当前高度阈值。
  过滤发生在公开 StateMachine 推进中、候选枚举之前，并在正常/异常路径逐项恢复敌人集合；已有 Squid
  目标也会被清空。全局状态机入口对非友好巨魔仅做 O(1) 字典旁路。
- 普通 TrollWeak 中约 10% 按存档/岛屿/统治期与动态 NetID 的稳定哈希成为反制单位；只有世界权威端
  在其原生目标查询期间临时加入 active FriendlyTroll，随后恢复目标缓存。未新增 RPC、序列化、pool、
  prefab、碰撞体或全场扫描。概率是大量同步池槽的长期平均，同统治期复用同一槽保持相同结果。
- 独立 reviewer 静态 APPROVED；初始源码构建 DLL SHA-256=
  `084981C255AE05EA7EBB9A3F8199E2D3B8DEDE6EB321A7F5F05BB0FEF6317F50`。游戏退出后已随上方综合候选
  重新构建、部署并打包；待验证两个公开 IL2CPP hook canary、真实冲撞伤害、CrownStealer 与普通 Troll
  边界。税收助手调度零改动。

## 2026-08-15 — bank-assistants-005：Dead Lands 助手 y=1.25

- Dead Lands 外观对应固定 controller index 2，本轮在双方确定性 prefab 构建时把 localScale.y 绝对设为
  1.25；北境 index 3 继续为 1.2。两者都继承 source x/z，朝向逻辑仍只改 x，不会对象池累乘。
- 调度与经济逻辑零改动：助手按欧洲→幕府→Dead Lands→北境轮转，不随机；同一时刻严格只有一个
  collector。满载回城是同步传送/收尾，完成后若仍有成熟金币才轮到下一位，不存在返程期间并发收币。
- 独立 reviewer APPROVED；Debug构建0 warning/0 error，构建、独立副本与刷新后zip内DLL SHA-256均为
  `9E71AFF5B155EF6D50DCD9EB0CFBA1098824382CF2C0547FEE431D485F8376BB`。刷新后zip SHA-256=
  `7F736F339F22AFBC7FCD00659863167753A91B566643CD4818F6401CCFB42ADC`，结构与UTF-8门禁均通过；
  待实机观感确认。

## 2026-08-15 — candidate-package-007：综合测试候选包已生成

- 重新打包当前综合候选，包含酿酒师 y=1.15、银行助手行为版、主船原生兵种扩容及此前候选改动；
  包内文档已统一说明“本测试候选包已包含、仍待实机门禁、尚未转为公开稳定能力”。
- 最终 zip SHA-256=`952FB1ECF3EEE011FA2AF8FC0956D13069D24EA5777C31EAA980692497D2087F`，
  40,558,266 bytes / 312 entries；manifest commit=`8ea703b1c9f4ed045608cc0b1594b773e849cfbb`、Dirty=false。
  构建、独立副本、包内 DLL SHA-256 三方均为
  `C4003C445EAC67037C1BD295BBAD7E21B8A68E00C3DA900037E26F0BF8C683E0`。
- 插件 DLL 恰 1、root dotnet 187、BepInEx/dotnet 0、版本顶层目录 0、required entries 无缺失、
  20 个文本项严格 UTF-8 全通过；独立 reviewer APPROVED。Steam、共享存档、Mono 未修改。

## 2026-08-15 — role-qol-001：酿酒师隐士 1.15 倍候选已构建

- 当前版本的酿酒师外观对应 `HermitType.Baker`。新补丁只在该隐士启用或对象池复用时，把 y 轴
  绝对设为 1.15，并登记到现有缩放守护机制；x 朝向、z、能力和其他隐士均不修改，也没有资源扫描或累乘。
- 为避免真实销毁后的 Unity instanceID 在同一进程复用并把 1.15 误套给其他单位，缩放注册表新增
  单对象注销；只在酿酒师 OnDestroy 时移除，OnDisable 不移除，保持对象池复用语义。
- IL2CPP Debug 构建 0 warning / 0 error，DLL SHA-256=
  `C4003C445EAC67037C1BD295BBAD7E21B8A68E00C3DA900037E26F0BF8C683E0`，独立 reviewer 静态
  APPROVED。游戏退出后已部署独立副本，构建/部署哈希一致；等待游戏内外观/对象池复用验证，不进入当前正式 zip。

## 2026-08-15 — boat-capacity-006：主船原生兵种扩容（进行中）

- 用户最终要求仅调整大船：独立弓箭手保持 4，工匠 8、骑士/侍从小队 6、长矛兵/重装步兵 8、
  农民保持 3；奥林匹斯小船保持原生容量。
- 2.4.0 静态核对确认原生五类已有登船所有者与乘客组件，容量字段可在主船注册前安全调整；
  狂战士与忍者没有该原生接口，因此不能只加两个数字。当前按高风险跨岛/联网任务设计为两类独立
  轻量适配器，必须在网络组件注册前进入 prefab，并完整复用原生登船、换岛存档与下船链。
- 深入核对发现狂战士/忍者不仅缺乘客接口，还缺上船 AI 分支，原生跨岛清单也只硬编码五类；
  用户判断收益不高并明确取消这两类登船。此前未完成的 adapter、RPC 与 sidecar 方案已全部撤销，
  不会进入构建或存档。
- 最终最小补丁仅在 `Boat.OnEnable` 原生注册调用期间临时写四个容量，注册完成或异常时恢复原字段，
  避免同一主船对象在关闭 Mod 后继续残留增强值。Debug 构建 0 warning / 0 error，
  DLL SHA-256=`DF1B21214D487F7AFEBFCD2E606301B1B4CB8BA40ED773BAE2DC58594A0B5772`；
  独立 reviewer 静态 APPROVED；代码提交 `c27d244` 已推送候选分支并进入现有 Draft PR #1，尚待
  独立副本实测。小船、Mono、Steam、共享存档和当前正式 zip 均未修改。

## 2026-08-15 — 银行助手系统（进行中）

- 用户进一步明确主银行家的固定活动区是左右从城堡向外数第二道墙之间，而不是全部领地或第一道墙。
  最新实现用两侧有序墙列表的 index 1 定义该区；第二墙未同时就绪时对称回退第一墙，再回退有效外墙，
  并让主银行家的扫描前后距离精确止于当前管辖边界。该区外（包括外层领地内）的玩家金币满 3 秒后归助手。
- 当前收集助手对同批后续金币采用 6 单位阈值：首枚或远目标才近距传送，6 单位内直接跑去，避免逐枚闪现；
  北境外观助手仅把 y 设为 1.2。独立 reviewer 静态 APPROVED；Debug 构建 0 warning / 0 error，
  DLL SHA-256=`51BFFEEF87FCC6846AF4FB253270DD0F6FE50C814DF7EBD3596A5320F8C8013B`。独立副本当前运行中，
  因此本轮尚未部署，等待安全退出后覆盖并实测。
- 四套外观与四助手生成已在独立副本确认。用户随后调整产品契约：主银行家应保持增强移速、安全期全天工作并
  覆盖扩张后的全部墙内区域；四名助手空闲时不应僵站，同一批墙外金币也不应四人一起行动。
- 当前行为版已实现唯一 collector 与批次轮转：只有当前助手会近距传送并连续收取成熟墙外金币，其余三名在
  城堡附近的独立墙内走廊巡逻，到端点分别停留 2/3/4/5 秒。主银行家保留原生状态机，walk=1.95、run=3.6，
  scanner 每 1 秒工作且低频跟随左右墙更新；墙外认领门禁不变。夜间不隐藏，运行中重新启用时仅在领地安全
  才立即出现，避免攻城时主动开门。独立 reviewer 静态 APPROVED；Debug 构建 0 warning / 0 error，
  构建与独立测试副本 DLL SHA-256 均为
  `91B6FDB52831BAA15B14E54B047F989E3B7639FC3DCE856A0522F4472AF41B62`。待游戏内实测。
- 首次候选已部署独立副本。实机日志确认四名助手池和实例都成功生成，但旧的动画资源入口无法取得
  `banker`、`banker_bamboo`、`banker_deadlands`、`banker_norselands` 四套控制器，代码又统一回退到
  当前世界控制器，造成四名助手外观相同。调度器还会在成熟列表第一枚金币暂不可认领时放弃后续候选，
  固定 800 距离也不覆盖所有加宽地图。
- 修复版沿 `BiomeHolder` 的世界风格替换表取得控制器 direct reference，并要求四套 exact name 与实例 ID
  全部唯一；缺失时整套助手 fail closed，不再复制相同外观。扫描改为统一掉落列表的全岛范围、逐候选尝试，
  临时原生认领不再重置 3 秒观察计时；诊断日志按状态去重，只保留首次分配和首次入账事件。
- 双端在资源稍晚就绪时都会以 2 秒退避重试固定池注册，客户端随后仍立即退出，不生成助手、不认领金币、
  不写国库。修复版 Debug 构建 0 warning / 0 error，DLL SHA-256=
  `F3BAB6CB492335D23E9CA3D958315545EE679100B0460EE511E8B160E5B99409`，独立 reviewer 静态 APPROVED；
  自动部署因当前桌面无 E 盘写权限而被拒绝，旧测试 DLL `0203BC71...` 保持不变；待 operator 手动部署
  独立副本复测。Steam、共享存档与当前正式 zip 均未修改。
- 2.4.0 资源实证：游戏只有一个完整 `Banker` prefab，通过 `banker`、`banker_bamboo`、
  `banker_deadlands`、`banker_norselands`、`banker_greece` 五套动画控制器形成五种世界外观。
- 用户拍板采用“1 名主银行家 + 4 名助手”：希腊外观主银行家继续独占国库、利息、提款、城堡门和
  存档；另外四套外观只作为无 `Banker` 行为的收币助手，避免固定 NetID 903 冲突和重复计息。
- 新任务 `bank-assistants-005` 已完成候选代码与 Debug 构建；边界为 world-authority 单写、金币单目标认领、统一低频扫描、
  墙外落地 3 秒后才分配；墙内金币继续留给主银行家/原生单位。为避免换岛时在途金币丢失，成功拾取即原子记入主银行家，满载/无目标回城只作
  视觉与容量节奏；同时修正旧共享账本可能覆盖日息/回滚提款的同步时点。四名助手使用轻量同步外观，
  不复制完整银行家、钱包或持久化身份；中央扫描频率为 2 Hz。最新构建 0 warning / 0 error，
  首次部署 DLL SHA-256=`0203BC714DCE13A20B6E9F753FF8D21E13083316DA936085F41DC758869A164C`。
  该首次版本的外观与调度回归已由上方修复版取代；候选仍不属于当前正式 zip 能力。

## 2026-08-15 — role-qol-001：狂战士公开 Promote 修复获用户实机验收

- 用户使用最新综合候选再次招募狂战士，确认当前招募序列没有问题；这证明从未命中的私有
  `Worker.TryPickupBerserkerTool` 迁移到公开 `Character.Promote(DroppableTool,IUnitController)` 后，
  1–5 普通、第 6 名长柄斧队长的循环已在游戏内生效。
- 本次运行的构建/独立副本 DLL SHA-256 均为
  `6E0C474B9D665CB2649F00071C2D02C09B44A0DACF3E49057D462E3D9EAE5AE0`。换岛延续和完整退出后重置
  尚未单独留证，可作为后续回归项，不再视为当前已发现缺陷。
- `role-qol-001` 仍保持 doing，因为同一组合任务中的隐士防绑架尚未取得游戏内命中证据；不把
  狂战士通过自动扩写为隐士也通过。

## 2026-08-15 — ninja-runtime-003：最新综合候选已部署并获用户体验验收

- 核对发现用户上一轮实际加载的是旧 DLL `88CE41D4...`，因此灌木半间距尚未生效；游戏退出后已将
  最新构建仅部署到独立测试副本。构建产物与测试副本 DLL SHA-256 均为
  `6E0C474B9D665CB2649F00071C2D02C09B44A0DACF3E49057D462E3D9EAE5AE0`。
- 用户重新运行后确认本轮忍者行为没有明显问题、整体逻辑自洽，灌木三槽
  `-0.55/0/+0.55` 的视觉间距合适。该反馈作为当前综合候选的游戏内体验验收证据。
- 仍未逐项留证的边界是树被砍、帐篷摧毁后的占用解绑，以及灌木跨侧池复用；任务暂保持 doing，
  后续回归补齐这些边界后再关闭并重打正式 zip。Steam 与共享存档未修改。

## 2026-08-15 — ninja-runtime-003：灌木三槽间距按实机观感减半

- 用户反馈当前忍者表现良好，且实机能看到灌木左右两个独立蹲守位置；但原 local x=`±1.1` 视觉上
  过宽，左右忍者接近灌木边缘。按用户要求改为 `-0.55/0/+0.55`，容量仍为 3、每槽仍单占用，
  不改变树 1 槽、乞丐帐篷 5 槽或原生近墙选择顺序。
- IL2CPP Debug 构建 0 warning/0 error，DLL SHA-256=
  `6E0C474B9D665CB2649F00071C2D02C09B44A0DACF3E49057D462E3D9EAE5AE0`；`git diff --check` 通过。
  当时尚未部署；随后已在游戏退出后只更新独立测试副本，并由用户确认新间距合适。正式 zip、
  Steam 与共享存档未修改。

## 2026-08-15 — ninja-runtime-003：伏击点扩展为灌木 3 / 树 1 / 乞丐帐篷 5

- 用户补充：若墙外未砍树，成熟灌木可能不足；Greece 忍者还应能在树下与乞丐帐篷蹲守。
  静态核对原生选择逻辑：`Kingdom.RegisterHidingSpot` 会把同侧列表按靠城墙方向排序，
  `Ninja.GetHidingSpot` 选择第一个墙外且未占用的槽，所以三种载体统一登记即可自然实现
  “谁离城墙近且没人就选谁”，无需自定义类型优先级。
- `PatchRoles_Ninja` 已抽出通用奇数槽锚点：成熟宽灌木最初使用 local x=`-1.1/0/+1.1` 三槽，
  后续按实机观感收紧为 `-0.55/0/+0.55`；
  每棵 Greece `PayableTree` 增加中心一槽；每个 Greece `BeggarCamp` 增加
  local x=`-2/-1/0/+1/+2` 五槽。每槽仍是原生单占用，父灌木禁用、树砍伐或帐篷摧毁时
  由原生 `OnDisable` 注销并通知占用 Ninja；仅 world-authority 创建。
- 帐篷只复用已实机命中的 `BeggarCamp.Awake` 一次补槽，未叠加同帧权限/Start 入口，避免新组件
  在原生 `Start` 前被手工登记、随后二次登记。树走 `PayableTree.OnEnable`，池复用时仅在 sided list
  缺失才清旧占用并补登记。
- IL2CPP Debug 构建 0 warning/0 error，DLL SHA-256=
  `68149B124362F823B265BD7A0CF25B3B390B4C566026269FADB3E0182AE0C55A`；checklist validator 与
  `git diff --check` 通过。本轮未部署、未启动游戏、未修改 Steam/共享存档/正式 zip；上一轮 reviewer
  approval 不覆盖新增树/帐篷范围，任务保持 doing，待静态复核与独立副本实测。

## 2026-08-15 — 玩家更新日志与 Git 归档审计

- 新增 `release/MOD_UPDATE_AND_FIX_LOG_ZH.txt`，以第一次正式发布包为基线，用玩家可理解的语言
  汇总首发后的忍者战斗修复、设置面板/终端降噪，以及三槽草丛、角色缩放、5+1 狂战士和隐士
  防绑架候选能力；明确区分“日志已确认”和“仍待实机”，不把当前候选误写成正式 zip 已包含。
- `pack-il2cpp.ps1` 已将该 TXT 加入未来候选包的复制与必备条目门禁；本次未运行打包脚本，
  当前正式 zip 未修改。使用说明、能力路线图与安装说明同步为每个成熟宽灌木三个错开忍者伏击位。
- Git 审计时，当前分支 `master` 的最后一次本地提交为 `02037fb`（2026-08-13 20:31 +08:00）；
  首发最终 zip 的生成时间以及此后全部候选修改均晚于该提交，说明此前并非每次更新后都有提交。
- 用户已明确授权以后每次项目改动完成后 commit + push。已创建私有 GitHub 仓库
  `https://github.com/baisiqi6/ohmymods` 并配置为 `origin`；首次 push 被历史中的 123.77 MB
  `ktc-il.txt` 拒绝。按用户补充要求，`game-source/`、`Assembly-CSharp/` 与 `ktc-il.txt` 只保留
  本机并加入 `.gitignore`，首次上传前从可推送历史中移除，不上传反编译参考源码。
- 清理后的 `master` 与 `agent/post-release-candidate` 已推送成功，草稿 PR 为
  `https://github.com/baisiqi6/ohmymods/pull/1`。`master` 保存首发前历史基线，候选分支保存当前全部
  改动；PR 保持 Draft，直到三个 doing 项的游戏内门禁通过。历史中的旧发布 ZIP 约 67.85 MB，
  GitHub 仅给出大文件警告，未阻断；后续正式包优先考虑转入 GitHub Releases，避免 Git 历史膨胀。

## 2026-08-15 — log-hygiene-004：候选已部署，待实机验证

- 旧 `Player.log` 约 39 MB，主要由设置面板注入静态 `Zpix` 后触发的 IMGUI/TextCore 字体转换
  级联造成：`Unable to find a font file` 与 `Unable to load font face` 各 17,482 次。
  已删除 `Resources.LoadAll<Font>("")`、`TryLoadCjkFont` 和 `_skin.font=Zpix`，改为复用 Unity
  默认 `GUI.skin`；F5/Ctrl+F10、英文配置名、数值与全部控件保持，中文 glyph 可能降级为方框。
- 钱包容量保障和四类左右商店队列的幂等业务写入保持不变，仅将重复成功日志从 Info 降为 Debug。
  PlayFab/证书、原生商店选址、游戏 uGUI BestFit 和卸载音频警告不做屏蔽。
- 独立 reviewer 静态 APPROVED；operator 复建 Debug 0 warning/0 error，构建与独立副本 DLL
  SHA-256 均为 `EC651F6C43C06E1BA41ED7A16BE6BD8E01EBC44C2EF3939EA95021BF60E9CEF3`。
  游戏未运行，Steam、共享存档和当前发布 zip 均未修改；待完整重启后打开面板/切场景复核新日志。
- 随后的新运行日志为 74 KB：两类 `Unable to find/load font ... Zpix` 均为 0，钱包/商店重复 Info
  也为 0；仅剩游戏原生 TextMesh/BestFit 静态字体提示。尚未取得用户对中文显示和控件操作的明确
  口头验收，因此保持 doing，不提前关闭。
- 后续合并三槽灌木与狂战士 Promote 修复后，当前构建/独立副本 DLL SHA-256 已更新为
  `88CE41D4D27C21F0B7BDB1D90A1286F9A0FAF1964225338E8487F7FD90B3821F`；字体实现未变。

## 2026-08-15 — ninja-runtime-003：对象池运行通过，三槽灌木已构建待部署

- 用户实测忍者攻击数次后停住、敌人不再攻击、天亮不恢复钓鱼形态。最新独立副本
  `Player.log` 给出直接因果链：`ThrowingStar` 池缺失导致 `Ninja.ThrowStar()` NRE；
  `Smokebomb` 池缺失导致 `Ninja.SmokebombRoutine()` NRE，并向上中断 `Ninja.Behaviour`。
  根因是跨 biome 迁移只注册了 Ninja/ToolNinja 主池，遗漏随角色使用的投射物和烟雾池。
- 原版 Ninja 并不按竹子名称选点，而是读取 Kingdom 的 `HidingSpot` 列表，再只接受城墙外且未占用的点。
  希腊 Grass 本身不带 HidingSpot；当前设计只在实际生成的成熟 thicket 实例上幂等补 HidingSpot，
  保留原生城墙过滤、单点占用、禁用解绑和昼夜状态机，不给每片 Grass 增加组件。
- 忍者夜行攻击形态 y=1.1、白天钓鱼形态 y=1.0，以及希腊银行家 y=1.075 已按现有
  `ScaleRegistryHolder` 实现，只写 localScale.y。对象池、草丛伏击和缩放最终独立 reviewer 静态
  APPROVED；Debug 构建 0 warning/0 error。构建与独立副本 DLL SHA-256 均为
  `EC651F6C43C06E1BA41ED7A16BE6BD8E01EBC44C2EF3939EA95021BF60E9CEF3`（仅叠加
  log-hygiene-004 的面板/日志降噪，忍者实现未变）；Steam、共享存档和当前发布 zip均未修改，
  等待用户执行完整战斗/昼夜/草丛日志门禁。
- 新一轮独立副本日志已运行候选 `EC651F...`：ThrowingStar/Smokebomb 注册成功，相关
  `Pool not found`、`NullReferenceException` 均为 0；字体大刷屏也为 0。用户要求一个宽灌木可让
  多名忍者错开蹲守，已扩展为 Left/Center/Right 三个独立子锚点（local x=-1.1/0/+1.1），仍保持
  一槽一人。三槽实现获独立 reviewer 静态 APPROVED，operator Debug 构建 0 warning/0 error，
  三槽实现与随后狂战士 Promote 修复合并后，operator Debug 构建 0 warning/0 error；游戏退出后
  已仅部署独立副本，构建与部署 DLL SHA-256 均为
  `88CE41D4D27C21F0B7BDB1D90A1286F9A0FAF1964225338E8487F7FD90B3821F`。

## 2026-08-15 — role-qol-001：候选已部署，待实机验证

- 新增狂战士招募序列：只统计 world-authority 下工匠使用普通 `BerserkerTool` 最终成功的转职，
  第 1–5 名为普通狂战士，第 6 名为 `BerserkerLeader`，随后循环。临时 Holder 映射由
  Postfix/Finalizer 恢复；购买、失败、读档/对象池生成及 `BerserkerLeaderTool` 升级不计数。
  序号按用户批准设计在当前进程内跨岛延续，完整退出后重置，不写 PlayerPrefs。
- 新增隐士防绑架：仅将隐士的 `Droppable.CanBePickedUpByEnemy()` 结果改为 false，同时覆盖 Troll
  的选目标和最终抓取门禁；不修改伤害、移动、乘骑、其他 NPC/物品或网络状态。已被抓住的隐士
  不会被主动释放。
- 最终独立 reviewer 静态 APPROVED；已随 ninja-runtime-003 候选构建部署独立副本，构建与部署 DLL
  SHA-256=`EC651F6C43C06E1BA41ED7A16BE6BD8E01EBC44C2EF3939EA95021BF60E9CEF3`（仅叠加
  log-hygiene-004 的面板/日志降噪）。未启动、未打包；必须以
  `slot 1..6` 和首次 `Prevented an enemy from kidnapping a hermit` 日志证明两个 IL2CPP hook 实机命中后
  才能关闭任务。
- 用户随后实测招募了大量狂战士但没有二级队长；同一 `LogOutput.log` 已确认普通 Berserker 与
  BerserkerLeader pool 都注册成功，但 `Berserker recruitment slot` 为 0。根因不是第六次 prefab，
  而是私有 `Worker.TryPickupBerserkerTool` 的原生内部调用绕过 Harmony thunk，序列从未进入。
- 已删除私有 helper hook/context，迁移到 Hammer 路径已证明命中的公开
  `Character.Promote(DroppableTool,IUnitController)`；用 active Worker + active、未拾取的普通
  BerserkerTool 收窄，且仅返回 tag/effective prefab 匹配后推进。独立 reviewer 静态 APPROVED，
  operator Debug 构建 0 warning/0 error；游戏退出后已仅部署独立副本，构建与部署 DLL SHA-256
  均为 `88CE41D4D27C21F0B7BDB1D90A1286F9A0FAF1964225338E8487F7FD90B3821F`。

## 2026-08-13 — 当前权威状态（取代下方同日早期记录）

- 最终 IL2CPP 发布包已生成：钱包偏移 X=+3.70/Y=-1.50；Debug 构建 0 warning/0 error；构建、
  独立副本、zip 内 DLL SHA-256 三方一致为
  `1D989035EDC066D3671E64A59330F8D205DAD83DD41F1A8BDBC91838CDE299CD`。加入中文使用说明，以及面向玩家的
  当前能力、骑士小队等未来计划与共创邀请 TXT 后，最终 zip SHA-256=
  `30E3853FCC43BE62C4D8944FD652D1A2DB4E96FD05AFF0E75D038C1E13563690`，40,532,301 bytes；
  目录结构、单份根 dotnet runtime、UTF-8 安装/使用/未来计划说明与构建 manifest 门禁通过。Steam 正式目录未修改。
- 首发门禁收口：用户确认钱包扩容可用并要求沿用原版物理溢出，不再以“2000 停止拾取”为验收；
  北境原生 Worker 判别/盾牌回归与神器法杖超过原版 5 秒仍不恢复均通过。双人分屏由用户明确降级为
  发布后反馈观察项，不再阻断首发。钱包最终 UI 偏移为 X=+3.70、Y=-1.50；进入最终打包。
- 用户实测确认：Hammer 拾取卡顿完全消失；每个乞丐帐篷约 6 秒补员、5 人停止；狂战士商店出现。
  忍者商店仍未出现。新 `Player.log` 证明 NinjaLeft/NinjaRight 均已入队并反复尝试摆放，但两者都从
  同一个右侧边界开始搜索，说明旧队列中的 NinjaLeft side 已损坏。已启动 `ninja-placement-002`：
  显式写入 Left/Right、修复存档既有队列并重新规划；暂不绕过原生 CanShopFit 或降低科技门槛。
- `ninja-placement-002` 已获最终 reviewer APPROVED；修复覆盖 Ninja/Shield 左右四种新旧队列，IL2CPP
  Debug 构建 0 warning/0 error。构建与独立副本 DLL SHA-256 均为
  `06EA69A3DC0A9F339661B729FD361586697FF67C02B65560BB8C987F5AF4C7F7`；等待用户实机确认左右搜索区间。
- 第二轮实机复测仍未出现忍者商店；新日志定位到旧空 `shopSide` 的 IL2CPP 生成 getter在
  `Nullable<Side>(IntPtr)`/`CreateGCHandle` 直接 NRE，且 Start 阶段手动 Trigger 早于 core 初始化。
  第三轮已改为按类型直接 setter 覆盖四类 side、完全不读旧 getter，并移除过早 Trigger；reviewer
  APPROVED，Debug 构建 0 warning/0 error，SHA-256=`6E3537383F26E3F897ACEB955040779BB18A9CE128A0D8B97C61DD5ED9E87701`。
  游戏退出后已部署第三轮 DLL；构建与独立副本 SHA-256 均为
  `6E3537383F26E3F897ACEB955040779BB18A9CE128A0D8B97C61DD5ED9E87701`，等待复测。
- 第三轮实机通过：LogOutput 记录两次 sided-shop 规范化且无 Error/Exception，用户确认忍者商店出现；
  `ninja-placement-002` 关闭。Player.log 仅剩 NinjaRight 受原生选址条件限制继续排队，不属于队列方向故障。
- 运行时 hotfix-002：Hammer 卡顿定位为每次转职同步 `Resources.LoadAll<Character>`，已改为每世界初始化缓存；
  忍者商店 NRE 定位为 IL2CPP `Nullable<Side>` 默认 null 解包，左右商店现显式传 Side 并在 ShopPlanner.Start 后补建；
  删除希腊全商店 CreateItem 接管，恢复已注册 sync pool 的原生产出。每个乞丐帐篷临时设
  `spawnInterval=1f/maxBeggars=5`，原生扫描段使实际约 6 秒补一个。
- hotfix-002 已获 reviewer APPROVED，IL2CPP Debug 构建 0 warning/0 error；构建、独立测试副本、候选 zip
  内 DLL SHA-256 均为 `95C0F2DE6CD7285BC639D6691287F70DA99CCA1476D71E6702F21F12C6F57944`，已进入实机复测。
- 用户确认后续只打开 IL2CPP 版本做端到端验证；Mono 降级为冻结历史/自用线，不再是发布门禁。
- 独立副本 20:26 日志仍有 7 组 `NpcShieldUser.SetShieldEnabled` NRE；根因是 Worker.OnEnable
  早于 CRPC/NetworkPostbox 注册完成。下方“异常 32→0”只代表更早一轮问题，不代表当前候选通过。
- 当前发布 zip 不是候选：包内 DLL 为旧构建（54,784 bytes，SHA-256
  `5C045D73CDD9D91A9675C8B19F468D2B52EB23497208F8A778A6D098C0BEEB19`），且同时包含根
  `dotnet/` 与重复的 `BepInEx/dotnet/`。旧的 7.6MB/39MB 描述均为 historical/superseded。
- **历史门禁（已取代）**：本轮早期曾把容量 2000、双人分屏和北境世界判别全部列为首发门禁；
  当前以本节顶部的最终收口为准——容量采用用户确认的原版物理溢出语义，北境验证已通过，分屏降为发布后观察。
  Steam 正式目录与共享存档仍禁止自动修改。
- 安全说明：历史“无反作弊/零封号、风险实质为零”不是发布保证。联机/平台风险不能用绝对表述；
  玩家应只在接受 mod 风险的环境中使用，并保持双方版本一致。
- 盾牌/锤子修复已获独立 reviewer APPROVED；IL2CPP Debug 构建 0 warning/0 error，候选 DLL
  SHA-256=`48A022CA45B14050031CA8F339543D4EEDD5A1CD5D044DB2F21EDBC3D2854CC6`。候选 zip 的
  根目录结构、单份 dotnet runtime 与 DLL 哈希门禁通过；构建、独立副本、zip 内 DLL 三方哈希一致，
  已进入独立副本实机验收阶段。

## 2026-08-12 — 2.1.0 两个 bug 修复（乞丐拾取 + 友好巨魔永久控制）

## 2026-08-13 — Steam 实机验证与修复

- 发布包两个坑：① 漏打 dotnet\ 运行时目录（67MB，doorstop 配置指向 dotnet\coreclr.dll，缺失→静默失败无日志）
  ② BepInEx 6 IL2CPP 首次启动生成 interop 后需二次启动才加载插件（已知机制）。zip 重打 39MB 含 dotnet。
- Steam 2.4.0 r23488 实机：插件加载成功、patch 激活（Holder 加角色/Worker 替换/sync 池注册）。
- NRE×32 修复：NpcShieldUser.Awake 在希腊 Worker 裸加组件时 damageable null→订阅 NRE→AddComponent 回滚→
  EnsurePickupCapability 每 OnEnable 死循环。修复：Awake prefix 分流（无 Damageable→安全版跳过订阅）+
  EquipShieldIfNorselands shield null 防御（希腊 worker 装备盾牌 NRE）。验证：Il2CppException 32→0。
- 封号评估：KTC 无 VAC/无反作弊/单机，社区 BepInEx mod 多年零封号——风险实质为零。

## 2026-08-13 — IL2CPP 迁移（Steam 2.4.0 发布线，用户拍板）

- 决策：发布受众 = Steam 正版玩家 → BepInEx 6 + Il2CppInterop 迁移（scope.md 更新为执行中）。
- M0 骨架：il2cpp/KingdomEnhancedMod.csproj + Plugin + ModConfig（BepInConfig 替代 UMM Settings），零错误部署验证。
- 三 worker 并行迁移：经济域（CurrencyBag/Banker/ShopPlanner/SidedShop）、角色域（Holder/Castle/Knight/Character/Worker/World/BeggarCamp）、
  世界战斗域（Mover/Construction/Level/Kingdom/EnemyManager/Artemis/HermesStaff/FriendlyTroll）——全部零错误零警告。
- 关键漂移：Mover"漂移"是 get_type_members.py 正则 bug 误报（unsafe 方法漏报），实际无漂移；其余漂移（BagCurrency.Reset→ResetVisuals、
  Wallet 多币种、ShopType 重排、Level.GenerateInternal+seed 等）已适配并记录待冒烟验证（notes-*.md 共 14 项）。
- Mono 侧池修复经 HotfixReviewer 抓 P0（syncID=119 跨biome冲突每帧 NRE）+P1（根因误判：真根因是读档恢复先于 InitPools）
  → 重写为 SpawnGO 池缺失兜底，部署 GOG 2.1.0。
- 实机验证：E:/QQ 2.4.0 加载 KingdomEnhancedMod v2.4.0 成功，零错误零异常。
- 发布包：release/KingdomEnhancedMod_v2.4.0_IL2CPP.zip（7.6MB，doorstop+BepInEx core+插件+配置+安装说明，开箱即用）。
- 待办：MigrationReviewer 交叉审核中；14 项待决策需游戏内冒烟验证。

- 乞丐拾取：根因链 扔金币→乞丐捡→Promote("Peasant")→UpgradeTransitionFX→Sparkles 池缺失
  （2.1.0 InitPools 只注册当前 biome 池资产）→NRE→拾取中断。修复：RegisterAllBiomePools
  全 biome 池去重补注册（Patch_PoolManager）。
- 友好巨魔：2.0.1 ShouldRevertToTroll 恒 false（原生永久），2.1.0 改为 `_expirationTime <= Time.time`
  （_duration=5f）——补 prefix 强制 false 实现永久控制（Patch_HermesStaff）。
- checklist feature-002/003 已登记；HotfixReviewer 交叉审核中。

## 2026-08-12 — 赫尔墨斯钱袋三件套（精细化改造第一项）

- 解锁：开局强制 `ChangeCurrencyBag(Hermes, 0/1)`（Patch_CurrencyBag，OnGameStartHandler postfix）。
- 扩容：`ChangeCurrencyBag` postfix 按类型设 `Player.wallet.TotalCapacity`（Hermes 2000 / Bag 1000，
  每局重设幂等——TotalCapacity 非持久字段）。
- UI：`BagCurrency.Reset` prefix 视觉堆叠上限 300→600；`CurrencyBag.Awake` postfix 整体放大 1.3x
  （金币堆子物体继承）。
- 机制澄清（防后人重踩）：游戏**没有"钱袋容器碰撞空间"**——容量是数字（Wallet.TotalCapacity），
  拾取靠金币×玩家物理碰撞重叠 + 点击 OverlapCircle，钱袋是 HUD 视觉对象。
- 待用户实测：钱包 2000 上限、堆叠 600、视觉放大效果。


# ohmymods — 进展

## 2026-08-12 — arch-002 收尾（命名对齐 + Probe 裁剪 + 文档同步）

- 命名对齐：Patch_Shop.cs → Patch_ShopPlanner.cs、Patch_Enemy.cs → Patch_EnemyManager.cs
  （Main.cs 注册名同步更新，maint-002/003 done）。
- Patch_Probe.cs 已删除，不再注册（maint-002 done）。
- build.bat 通配化（`for %%F in (Main.cs Patch_*.cs)`）+ 编译成功自动部署到 Mods/MyMod（maint-003 done）。
- 文档同步（Worker B）：architecture.md 模块清单按最终态重写（商店注册为 Prefix 全量替换）、
  domain-model.md 关闭 R3/R4 + 新增 D8（速度倍率 SetGoal 入口/地图幂等/银行家补员删除原因/Enabled 契约统一）、
  biome-asset-system.md / unit-spawning.md 的商店注册描述改 Prefix、unit-spawning.md 自洽方案标注废弃
  （指向 patch-patterns.md 坑10）、MOD开发文档.md 归档到 docs/legacy/。
- 剩余：Mover.Update 双 postfix 合并、Main.OnGUI 反射缓存（Worker A）。

## 2026-08-12 — GOG 2.1.0 迁移完成（Mono 最后版本）

- **注入方案**：UMM 21.0.32 自带 winhttp（旧 UnityDoorstop）不识别 Unity 2022.3.51f1 →
  改用 BepInEx 5.4.23.3 的 winhttp（x86）+ `[General] target_assembly=` 格式配置指向
  UnityModManager.dll（详见 runbook "注入方案"）。
- **API 差异修复（4 处）**：
  1. `Pool.syncID` int→short（Patch_Castle 显式转换）
  2. `EquipShield NRE`：NpcShieldUser.Awake 在 HasWorldAuth 未就绪时提前 return → regenWait
     为 null → 装备前反射补初始化
  3. `Worker.OnTriggerEnter2D` 新增 npcShieldUser==null 早退 → 希腊工人无法拾取
     BerserkerTool → 狂战士商店卡死；OnEnable 补组件+回填字段（EnsurePickupCapability）
  4. 其余 21 项 patch 目标 2.1.0 验证全部存在，零 not found
- 2.1.0 反编译源码入库 `game-source/Assembly-CSharp-2.1.0/`。

## 2026-08-12 — 架构交叉审查 + P0/P1 修复

- ArchReviewer（kimi K3）审查结论：**无需框架级升级**（单 DLL + Patch 类 + harness 骨架对 19 patch 规模合适）。
- **P0 修复**：① Patch_Mover 速度倍率写错字段（_moveSpeed 被 _goalSpeed Lerp 覆盖，从不生效）→ 改 patch SetGoal/SetGoalSpeed/SetGoalNoHaglet 入口缩放 speed 参数，幂等无累积；② Patch_Kingdom 地图倍率非幂等（Init+每岛加载指数放大 4→8→16→32）→ 基准值缓存幂等设置。
- **P1 修复**：③ Main.Enabled 契约统一（Patch_PoolManager/SidedShop/WorkerScale 补检查）；④ 银行家"5 个"补员删除——Banker.Awake 硬编码 NetID 903 唯一，克隆无法注册网络且与去重自相矛盾（每 120 帧 Instantiate/Destroy 刷屏）；共享银行增强保留。
- Info.json GameVersion 1.1.4→2.0.1、Version→1.1.0。
- 剩余 arch-002：Probe 裁剪、命名对齐、文档同步、双 postfix 合并、build.bat 通配化+部署脚本化、OnGUI 反射缓存。

## 2026-08-12 — kingdom-mod skill 迁入

- 原 `.omp/skills/kingdom-mod/`（6 文件）全部迁入 `docs/project-harness/game-logic-map/`。
- 链接改为相对路径；功能清单更新到当前状态（狂战士 hack 已退役、Patch_Mover 确认为速度倍率、新增坑 11/12）。
- 原 skill 已删除；`maint-001` 核实完成（Patch_Mover 是玩家速度倍率，保留）。

## 2026-08-12 — harness 实例化

### 已完成（核心功能全部就绪）
- 狂战士/忍者：希腊世界商店原生生成（槽位劫持 12/13），hack 退役。
- 北境形象：Worker/Peasant 的 tagCharacterPairs 替换 + sync 池注册。
- 北境工匠出生带盾（SetShieldEnabled，绕过无盾牌商店的缺口）。
- 单位缩放：y 轴守护机制（OnEnable 登记 + Mover.Update postfix 恢复），
  北境工匠 1.175 / 北境居民 1.125 / 希腊工匠 1.075 / 狂战士 1.2 / 鹿 0.55 / 小动物 1.8。
- 性能清理：删除每帧 FindObjectsOfType 兜底（ScaleAllWorkers），零每帧扫描。
- 地图扩展、希腊猫生成。

### 验证状态
- 每次改动后 build.bat 编译通过（csc.exe，C# 5）。
- 游戏内实测：盾牌可见 ✓、缩放生效 ✓（多轮调参 1.3→1.175 / 1.2→1.125）。
- 待测：清理后的完整回归（狂战士/忍者购买、读档恢复、缩放一致性）。

### 风险
- R1：存档携带 localScale.y（Serializer 写完整 transform）——卸载 mod 后旧档尺寸可能不符。
- R2：狂战士（Berserker）无缩放登记，转化后回 1.0（当前意图）。
- R3：Patch_Mover.cs 旧方案遗留待清理。
- R4：Patch_Probe.cs 调试日志待裁剪。

### 下一步（按 checklist）
1. maint-001 ✅（Patch_Mover 已核实为玩家速度倍率并修复，见 D8）。
2. maint-002 ✅（Patch_Probe.cs 已删除，arch-002）。
3. maint-003 ✅（build.bat 通配化 + 自动部署，arch-002）。
4. 完整回归测试。
## 2026-08-15 — population-performance-010：硬上限与旧档清理静态通过

- 当前异常岛只读统计为1,132名角色，其中Worker 458、Peasant 301、Beggar 158；全岛只有2个BeggarCamp与2座面包房。原生营地只重算附近乞丐，面包房又会清除camp引用，导致旧的“附近最多5人”不断释放名额并积累人口。
- 新候选保留原生营地协程，但在world-authority协调器健康时由中央调度按稳定营地归属约6秒补1、每营地硬上限5；去面包房或走远不再释放该营地名额。异常、失权和Mod关闭都有原生参数fallback。
- ApplyToScene稳定后只由authority建立一次scene清理批次，每帧最多同步回收1名超额普通Beggar；settler、面包房/进食、控制、被抓、inert/石化、DespawnOnLoad及pool/header不安全对象一律保护，受保护者超过5时允许可解释残余。
- worker与独立reviewer静态APPROVED；Debug构建0 warning/0 error，当前候选DLL SHA-256=`085D84C644D6C48E046A87AAD5BE3BFCFB6154929EE8B47914533DCDF572D9DA`。游戏已退出，等待干净提交重建、存档备份、独立副本部署与实机。
- 后续工具分配优化另开切片：原版每3秒会让约922个carrier参与近方阵匹配；必须先做放行原版的hook探针，确认命中后才考虑稀疏反向矩阵，禁止全局替换JobAssigner。
## 2026-08-15 — save-repair-011：land 7 乞丐158→10已原子修复

- 人口候选首次实机确实命中，但摘要为`before=158 assigned=158 protected=158 removed=0 residual=148`；运行时安全身份门禁过严，因此内置旧档清理不能算通过。用户随后明确授权对当前异常存档做一次性直接修复。
- 强校验脚本经worker/reviewer多轮审查与真实dry-run后APPLY_APPROVED：锁定输入SHA/长度/schema/campaign/land/对象数/精确营地prefab与坐标；只删除无外部引用的普通Beggar；非目标root/island与幸存对象内容/顺序DeepEquals；同卷File.Replace并有已验证backup/rollback。
- dry-run和Apply均为`before=158 removed=148 after=10 groups=5/5`。原始备份`global-v35.before-direct-beggar-prune-20260815-232403`为751,068 bytes/SHA=`68D4F779DA3CFA45A659D2082B2B15F135777699EC4A309F1F6AEAE14C724B16`；最终存档748,730 bytes/SHA=`2C681C5C2CA01E6BBCBB5F05BDEA32FC63A0D86EA563F68325D12C08D088F87A`。
- 写后独立复读确认version16/currentCampaign1/currentLand7/objects2046/Beggar10/左右5+5，临时文件0。等待独立副本实际读档；若失败必须退出不保存并恢复上述备份。

## 2026-08-15 — save-repair-012：land 7 普通居民删除350名

- 纠正此前人口统计：真正带WorkerData的工匠只有14名；421个名称以Worker开头的对象实际是Peasant prefab的对象池残留名。用户确认应删除350名普通居民，不删除真正工匠。
- 从383名组件/profile完全一致、未被抓/石化/inert、钱包全0、无外部引用的希腊Peasant中，按确定性createOrder加载顺序保留最低33并删除最高350；createOrder不表示年龄且未重编号。
- worker/reviewer逐行审查、真实dry-run与独立只读内存复算后APPLY_APPROVED。原始备份`global-v35.before-direct-peasant-prune-20260815-235118`为748,730 bytes/SHA=`2C681C5C2CA01E6BBCBB5F05BDEA32FC63A0D86EA563F68325D12C08D088F87A`；最终存档728,071 bytes/SHA=`63884D91421A7B74AD0049C8FB00BFD3E910857F05005490B2704E856FE93FED`。
- 写后独立复读确认land7 objects1696、Worker14、Peasant383（Greek288/Norse95）、Beggar10/左右5+5，临时文件0。等待独立副本实际读档和卡顿体感；失败时退出不保存并恢复本任务备份。
## 2026-08-16 — ghost-squads-013：改为保留两套原生行为

- 2.4资源确认 Cerberus 原生只生成1名希腊亡灵骑士与4名弓箭手；原生协程只有一个共享队长引用，不能仅把数量字段改成4/16，否则成员归队关系错误。
- 北境神器亡灵与希腊坐骑亡灵只共享编队接口，实际AI不同。用户明确要求保留差异：最终仍为四个独立
  1+4编队，但两支希腊队主动向外作战并按距离回收，两支北境队跟随君主并按原生30秒Duration消亡。
- 两个北境完整行为克隆池固定预留syncID30130/30131，主客同序注册；不新增RPC，不改原生冷却、雾效
  或首队配置。第一版“北境仅视觉”部署包已被该决定取代。
- 修订源码提交 `0cd629e` 已推送；从该干净提交重建0 warning / 0 error并部署独立副本，构建/部署
  DLL SHA-256均为`024ADAC72A4D2D76B63827C19C6D9511105CDDBA977EBE196D2FF62354564A39`。正在刷新候选包。
## 2026-08-16 — friendly-troll-balance-008：解除无敌后反制攻击实机闭环通过

- 最新独立副本日志出现54次友好巨魔登记与12个反制弱巨魔查询，但候选注入、真实伤害仍为0；没有相关异常。
- 当前存档只读复核发现54个 `Troll_friendly` 的 Damageable 全部保持 `invulnerable=true`。原生索敌与受伤入口
  均会拒绝无敌目标，因此此前的稳定标记与10格追击虽已运行，仍不可能进入原生冲撞伤害闭环。
- 新修复只在 world-authority、Playing、活动实例和原生指针一致时通过公开属性解除无敌；以
  `currentAtFirstCapture || isInvulnerableInitially` 保存可逆基线，关闭Mod和正常回池前恢复。加载、卸载、
  失权与失活对象不写；联机header/catch-up未就绪时只挂起一次，随后用公开属性补发一次，不扩RPC协议。
- worker禁部署构建0 warning / 0 error，独立reviewer最终APPROVED；源码SHA-256=`73934E38B3C1DB59CA27C14C9FF3F64F310C7F9E6697DC6A6E64AAF691D32542`，
  Debug DLL SHA-256=`BDF1FB4415E05E8F9596D19A024D020210ECCCEE7B6291D72D68CECBA9A4AB4B`。
  源码与记录提交 `0495a68` 已推送。再次确认游戏进程为0后只部署E盘独立测试副本；构建/部署DLL均为
  173,568 bytes且SHA-256一致。G盘当前未挂载，未写G盘；release zip未刷新。下一步实测真实注入与伤害。
- 20:03后的当前E盘实机日志给出完整闭环：`friendly-active=56`、`counter-query=8`、
  `friendly-injected=7`、`native-damage=6`。六次原生Troll伤害的`hpAfterEvent=0`，证明反制弱巨魔已把
  友好巨魔选为目标并实际击杀，而不只是追到附近或写日志。
- 同一BepInEx日志没有Exception/Error/unknown pool/duplicate sync/RPC异常；Player.log也没有
  NullReference、StackOverflow、ArgumentException或Pool/RPC相关异常。Player.log另有原生Stats保存路径的
  `gse orca` LogError栈，与FriendlyTroll目标/伤害链无关。本任务核心攻击能力通过，Disabled恢复、联机catch-up/
  authority迁移、换岛与Squid/CrownStealer边界仍待回归。
## 2026-08-16 — 视觉微调：Dead Lands助手与Fire隐士 y=1.25

- Dead Lands银行助手对应固定controller index 2，本轮由绝对y=1.20改为1.25；北境index 3仍为1.20。
  只写prefab初始y，继承原x朝向/z，调度、经济、同步与对象池逻辑零改。
- 火焰塔隐士按`HermitType.Fire`精确设为绝对y=1.25，沿用现有OnEnable/ScaleRegistry/OnDestroy生命周期；
  乘骑时原生会临时写回单位缩放，注册器会继续恢复目标y，需作为实机观感门禁。
- 原生2.4资源确认Fire可离岛且可上船，因此已拥有的Passenger/Roaming状态正常读档、放下与换岛不依赖Cabin。
  但其`lostOnCrownLost=true`，失冠/死亡换君主会按原生规则转为CoinLocked/land0；当前又没有Fire小屋，
  所以不能承诺死亡后仍可重新获得，后续需单独决定是否修复这一所有权缺口。
- 两处代码经worker构建与独立reviewer最终APPROVED；初始Debug候选DLL SHA-256=
  `7A75716A8748A09497314C7DAE32B1B760B81A1416520C7509C5BD958E691208`（174,080 bytes）。随后已随综合候选
  提交、推送，并在确认游戏退出后部署E盘独立副本；当前综合部署DLL为181,760 bytes、SHA-256=
  `947131C76EF465B35AC21862E273E29D87AB0A8C2D97136E9CA15062F97E9CBD`，实机观感仍待验证。

## 2026-08-16 — special-tower-rebuild-018：首版安全重建已编译

- 完成态Ballista/Fire/Knight/OilFire/Berserker等资源均无原生PayableUpgrade，不能靠改nextPrefab实现互换；直接替换还会绕过next NetID、Persistent、隐士和驻员清理链。
- 用户接受“两步重建”：先把旧专精塔付费恢复为当前世界六级普通箭塔，再携目标隐士走原版专精。首版只开放安全空闲的Ballista来源，目标价格从运行时原生六级塔读取；重建本身不消耗隐士、不计特种塔统计。
- 新补丁在PoolManager建池前为当前biome安全Ballista prefab确定性追加原生PayableUpgrade和无状态marker；Disabled仍保留CRPC/Persistent组件布局，只关闭选择/付款。最终付款前回收已装填bolt，再完整执行原生Pay。
- Fire/OilFire/TowerKnight/Baker/Mead因库存、隐藏驻员或同级PayableShield生命周期未审清，首版保持fail closed。禁部署Debug构建0 warning/0 error；随后已提交、推送并在游戏退出后部署独立测试副本，实机门禁仍待完成。
- 当前源码SHA-256=`DB882F8A43BC56A58C901B7101535738B2288E0114119046D6414B45BD755023`，Debug DLL SHA-256=`D41870F063085B1852410393ACE358B42D8A81334948F5ED787D2C166B52A0A7`；checklist validator 0 warning、相关文本严格UTF-8通过。

## 2026-08-16 — fleetboat-formation-019：动态同侧小船编队候选已编译

- 2.4 Player Formation资源确认原生只有一个FleetBoat槽且该类型间距为0；候选实现只在world-authority举旗时按原生Side与完整可加入门禁快照0～4艘小船，多船间距绝对设为1。
- 原生`TryRecruit`、`UnregisterUnit`与`OnDisable`生命周期保持主导；空船槽即时封为Gap，解除旗帜后在单位清空时恢复该Player独立捕获的原版数组。协调器仅每0.5秒检查最多4个预留槽，不改FleetBoat Side/FSM/RPC/容量。
- 禁部署Debug构建0 warning / 0 error；源码SHA-256=`8550F056A982A7FAD570EBBC77929F65C99957F1F86993BC9D5D19DF66CEFCDF`，独立reviewer已APPROVED。游戏进程为0后，192,000-byte DLL SHA-256=`3595BEB72A7CD30871FD778F7F7FCCFBD6ED6AF36C9181AC1BFF634DBD54B3F3`已只部署到E盘独立副本，并保留部署前备份；仍需1/2/4船、N=0、离队、分屏/联机及native Hook canary回归。
