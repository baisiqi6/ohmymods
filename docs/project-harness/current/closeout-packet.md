# Closeout Packet

## Freshness Metadata

- generated_at: `2026-09-06T18:37:30Z`
- source_plan_sha256: `eefc016e9f5dcd96a48c704eee7e5e18e866d98c31478b72eada05d31a654b56`
- canonical_plan_path: `docs/project-harness/tasks/coordinate-release-audit-20260906/plan.md`
- checklist_item: `coordinate-release-audit-20260906`
## Subject

- Checklist item: `coordinate-release-audit-20260906`
- Reviewer: `omp-k3-r5f-result-reviewer`
- Updated at: `2026-09-06T18:37:30Z`
- Workflow mode: `high-risk`
- Canonical plan path: `docs/project-harness/tasks/coordinate-release-audit-20260906/plan.md`

## Item Snapshot

- Title: v4.5.0 发布材料一致性审计
- Status: todo
- Workflow status: closeout_requested
- Priority: p1
- Owner: None
- Session: None
- Dependencies: None

## Acceptance

Use the plan acceptance criteria as source of truth: docs/project-harness/tasks/coordinate-release-audit-20260906/plan.md

## Verification



## Handoff

{'from': None, 'to': None, 'reason': None}

## Review Inputs

- Scope: `docs/project-harness/scope.md`
- Architecture: `docs/project-harness/architecture.md`
- Domain model: `docs/project-harness/domain-model.md`
- Progress: `docs/project-harness/progress.md`
- Review output target: `docs/project-harness/current/review.md`

## Canonical Plan Content

```md
# v4.5.0 发布材料一致性审计

Task ID：`coordinate-release-audit-20260906`。

## 问题与目标

v4.5.0 发布包含 source commit、插件/程序集版本、ZIP/DLL 摘要、构建记录与实测边界。需要一份可复核的审计报告，区分这些已提交材料互相支持的事实、仍需实物复验的声明，以及任何不一致。此任务同时验证本项目新接入的 Coordinate 受管任务流程。

审计基线为已提交的 `2437c6d28de71849168884cc8e4ff9fb73fe8579`；该提交记录的 release tag 指向 `c2032185a5bc213f085a5831abedbbaacfab8b36`。两者用途不同，不能因为 receipt 后续提交推进而称 tag 指错。工作区中的后续游戏候选与用户未提交改动不在本次验收范围。

## 输入与唯一产出

输入限定为本任务隔离工作树中的已跟踪文件：

- `scripts/package_release.py`；
- `il2cpp/KingdomEnhancedMod.csproj`；
- `docs/project-harness/tasks/release-450-20260906/` 中的发布、构建、测试、metadata、package receipt 与接受边界记录；
- `release/` 中的玩家说明文档。

worker 只可新增或修订本目录的 `result.md`。harness 状态、配置、plan、Git 与部署动作归 Operator；worker 不修改其它文件。

## 验收标准

1. 报告列出实际读取的每个输入的仓库相对路径和 SHA-256，并说明读取的是哪一个 Git 基线；不使用绝对个人目录作可复现接口。
2. 逐项核对 tag/source commit、plugin/assembly version、ZIP/DLL size/hash、package receipt 与公开说明中的对应值，给出具体路径或字段定位。
3. 区分“本轮静态读取/重算已验证”“材料中记载但本轮未独立运行验证”“不一致/未知”。没有 ZIP/DLL 实物就不能宣称重新验过其哈希、CRC 或实际 IL；没有运行测试就不能把原测试记录当成本轮新测试。
4. 对发布脚本的文件边界做静态检查，记录已实现的排除/验证条件与无法仅靠源码证明的运行时事实；不执行打包脚本。
5. 保留联机、换岛、authority 等原有待实测边界。审计完成不改变这些游戏功能的 doing/done，不代表新的游戏版本验收。
6. 独立 reviewer 检查实际报告及输入，Operator 重新计算输入 hashes；出现问题则返工、生成新 review packet 并重新绑定 review SHA。
7. 当前实例 checklist validator 通过；已有游戏任务字段保持不变。完整过程经 Coordinate file/record、真实 Windows managed worker、正常 completion receipt 收口，不能用 repair-only 或伪造历史事件。

## 约束

不修改/构建/部署游戏代码或 DLL；不运行游戏；不访问 Steam 安装目录、测试游戏目录、配置、日志或存档；不发送 Discord/KOOK/其它消息；不创建游戏 Release/tag；不读取 credential；不直接改 checklist JSON。

## 交付与恢复

Operator 先发布 initial task 文件，再由 Remote MCP 记录。worker/report 和独立 review 完成后，先提交并部署 review-approved 文件，再准备 completion；done 文件部署后才 consume。同 operation/receipt 的网络重试保持幂等，新的审计修订使用独立 revision job key并保留旧证据。

最终 owned 工具、任务与进展记录合入主开发分支，持久主工作区及服务器读回一致后，才能退役本任务工作树。未能安全合入时保留工作树和明确的待集成状态，不覆盖原 Operator 的未提交材料。
```

## Recent Progress Context

```md
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

## 2026-09-01 — v3.5.1 crash audit：FriendlyTroll 生命周期递归修复

- 审计同步到远端 `64ce116`（v3.5.1-dev4）；E 盘独立副本仍运行 dev3，未覆盖运行时 DLL。日志确认 `ErrorLog.log` 存在 `Troll.OnDestroy` 递归导致的 `Stack overflow`，与此前小岛/切岛闪退风险一致。
- 仅移除 FriendlyTroll 补丁内三个私有 `OnDestroy` Harmony 钩子（Troll、Squid、追踪协调器），保留公开 `OnDisable` 生命周期清理；追踪 tick 增加惰性 registry prune，覆盖卸载时缺失回调而不改变目标、伤害、RPC 或对象池逻辑。
- worker 构建 0 warning/0 error，独立 reviewer `REVIEW_APPROVED`。最终源码 SHA-256=`98C8BF44BFB47843DB111D17EB5B9FD4246798492FBB438008DDEE02B29698E5`，重新构建 Debug DLL（300,544 bytes）SHA-256=`8CD2A9F74D0229ABEFF87357F2B8DE8659556F28108F0AA63946650D8BE68EE4`。待游戏退出门禁复核后部署 E 盘，并实测切岛/卸载无 StackOverflow。
- 同轮审计确认 FleetBoatBerth 的 `timeout-unsafe-state` 是有界等待后不写状态；SpecialTower 已做全层级诊断，当前没有可证明的低风险额外改动。FriendlyTroll identity/header 缺失仍按 fail-closed 处理，避免错误指定普通巨魔。

## 2026-09-01 — v3.5.1 known-bugs audit：农民停工、夜怪迟到、空白塔基与守家图腾

- 本轮不改存档、不覆盖运行中的 E 盘 DLL；ZCode 0.16.5 已确认，但 headless CLI 因用户 CLI 配置缺少 model provider 被安全阻断，四个隔离 worktree 仍保留用于后续 worker 接入。
- `PatchPerformance_ToolAssignment` 的稀疏替换在 `eligibleDroppables==0` 时会把全部 carrier 的目标清成 null；新增放行原生分配的 fail-open 门，避免昼夜切换期间农民/农夫被整体清空到闲置聚集。
- `PatchWorld_TowerSpots` 改用最近原生塔基的地表 Y（全岛中位数仅作回退），并仅规范化补放根对象 active 状态；新增一次性 renderer 健康日志，用于区分“无视觉子树”和“地形/Z遮挡”，不强开子渲染器。
- `PatchPerformance_NightVolley` 新增限频 `[ClockDiag]` 只读采样（Director.currentTime/IsNight、Kingdom.isDaytime、CurrentIslandDays、Time.timeScale），用于验证农民、夜怪、存档异常是否同源时钟失配；不写入任何原生时间状态。当前 Debug 构建0 warning/0 error，候选 DLL SHA-256=`06CD4A49BDB281658C49BA880BD47FEA1B87ED4692D517D08E174EC5028175B7`（302,080 bytes）。
- 游戏退出后已部署到 E 盘独立副本，部署 hash 与候选完全一致；旧 DLL 已备份为 `KingdomEnhancedMod.dll.before-known-bugs-20260901-230425.bak`（300,544 bytes/SHA=`70DAD3B55C0BB23FCFAE3C00B5CAD4E6557FD35FA40E881DA4BEE2698FE342F9`）。守家图腾与蛇吐怪暂未做无证据的行为放宽，下一步先采样 `[ClockDiag]`、`[TowerSpots] visual health` 及图腾付款路径，再决定最小修复。

## 2026-09-02 — 弩炮弹速、守家编队与塔基避障修复候选

- 实机 `[ClockDiag]` 显示昼夜状态按原生顺序推进，农民停工/夜怪迟到不是全局时钟损坏；守家雕像左右两侧均已正确挂载。混用外观的根因是原生 `Knight.CanJoinFormation` 对盾墙编队不限制北境风格，而普通骑士随从又没有 `NpcShieldUser+HasShield`，导致队长能守家、随从不能进入原生 Shield 近战状态。
- 希腊盾墙现在只在原结果为 true 时进一步排除非北境风格 Knight；不改随从、盾牌、职业或其它编队。北境带盾弓手仍由原生 `Archer.TryRecruit` 进入 `AttackMode.Shield`，该状态包含近身攻击，不要求动画状态名必须叫 Melee。
- 弩炮塔原生瞄准和发射共同读取 `BoltData.ShootForce`；通过 getter postfix 将其从默认20统一放大到25（×1.25），避免只改发射速度导致瞄准解与真实弹道不一致，也不修改自定义弩手。
- 补放塔基现在复用 `ScatteredObject.AvoidOverlapWith` 的非 Tower 标签避障、同层/非船过滤和原生 overlap region；仍由自有塔间距实现增密。注册前补 `FixedTransform.Fix()`。既有未建 `KEM_TowerSpot` 若与建筑重叠，只在离线 world-authority、level0、SemiStatic/Persistent/CRPC 完整门禁下按 Deregister→DontPersist→Disable→Destroy 退役；原生塔基、已建塔和检查异常均不删除。
- worker 静态核对与独立 reviewer 最终 `REVIEW_APPROVED`；Debug 构建0 warning/0 error。确认游戏进程为0后已部署 E 盘独立副本，DLL为306,688 bytes、SHA-256=`0C874B1DE6CBAF97C8AD686EBFA7A3733FF28DD2BB08B9017D4F1BCB1DD090A2`；部署前备份为 `KingdomEnhancedMod.dll.before-bolt-shield-tower-20260902-210000.bak`，SHA-256=`06CD4A49BDB281658C49BA880BD47FEA1B87ED4692D517D08E174EC5028175B7`。待实测弩炮弹道、1币守家成员/近战、重叠塔基清理与存读档。

## 2026-09-03 — norse-follower-load-restore：读档后立即原生替换北境随从

- 复核确认旧逻辑只有 `Archer.AssignJob` 和 5s `PatrolPass` 会调用真实 `Archer_norselands` `Promote`；`KnightStyle.ApplyFollowerSkinTo` 另有只改 `RuntimeAnimatorController` 的换皮路径。因此读档后的守家窗口可能出现“北境动画、无盾组件”的混合状态。
- 新增 `World.OnLevelLoaded` 安全恢复协程：下一帧登记场上 Archer，等待 Knight 风格状态、Holder/Pool/CRPC 就绪后，在 world-authority 侧立即把北境风格骑士名下的非真实 `Archer_norselands` 随从换成原生 prefab，并调用原生 `SetShieldEnabled`；风格尚未解析、池/RPC 未就绪时最多 24 次×0.25s 有界重试，普通风格随从移出队列，客户端不写。
- 新增 `TryGetResolvedStyleIndex` 区分“尚未解析”与“非北境”，避免恢复协程过早丢弃候选；新增一次性 `load restore summary`（converted/shieldReady/pending）诊断；保留原有 AssignJob/5s巡检作为后续招募和池复用兜底。Debug 构建0 warning/0 error，DLL 309,760 bytes、SHA-256=`EFBB0B0F98F4719280363ABAEBDBB8768758B3F8BE190F85009758DFA0A4DA26`。确认游戏进程为0后已部署 E盘；上一版已备份为 `KingdomEnhancedMod.dll.before-norse-load-restore-20260903-205511.bak`，最新部署前版本另备份为 `KingdomEnhancedMod.dll.before-norse-load-restore-diag-20260903-205643.bak`。待实测“读档即替换、盾牌/AttackMode.Shield、再次读档幂等”。
- 2026-09-05：用户实测仍有塔基视觉重叠。隔离 worker 定位为补放网格只按 Payable 交互半径避障，未覆盖塔基 Renderer/ScatteredObject 视觉宽度，且历史 KEM 塔基彼此重叠时原逻辑不会回收。commit `2e85449` 已将视觉 bounds 与原生区域取并集、以视觉宽度抬高最小间距，并按 X 稳定顺序回收合法的自有未建造重叠基底；worker/reviewer `REVIEW_APPROVED`，Debug 0W/0E。待游戏退出后部署 E 盘测试副本并观察 `[TowerSpots] scan ... generatedUnbuilt=... retired=...`。
- 2026-09-05：针对高人口岛每隔数秒掉帧和大蛇吐怪晚到，完成性能/行军补偿候选：3秒、5秒巡检错峰；白天弓箭手拥挤检测改为可复用空间分桶；税收助手扫描间隔改为0.6秒且无候选时只做最小所有权清理；大蛇嘴部首波在夜幕前按额外距离（秒→游戏小时换算）有限预置，普通传送门不变。worker 初版遇 ZCode provider 缺失后按授权回退，reviewer `REVIEW_APPROVED`，主树 Debug 0W/0E；待实机验证吐怪提前量、夜间波次和卡顿是否改善。
- 2026-09-05 部署：游戏进程确认退出后，commit `7836358` 构建的 DLL（316,928 bytes，SHA-256 `7349F7017AC7687F832A5A634A1248041873D85961B7E562DB3FD17EDD805966`）已部署到 E 盘独立测试副本；部署前备份 `KingdomEnhancedMod.dll.before-perf-serpent-20260905-005305-677.bak`，备份哈希与原 DLL `714B51D3...` 一致，目标哈希与构建哈希一致。未改存档/G盘。

- 2026-09-06 00:31：权限恢复，面板/人口滑块/常驻日历HUD已同步主仓库并部署E独立副本；游戏退出、备份/回滚及目标hash核验通过，SHA256=A86D036741EF61F6301485ACABAFA20864BAEC37D790C660291D2509F1F0858C。HUD默认关闭，F5→世界开启；0W/0E编译、95702日历断言与独立审查完成，实机仍待验证。

## 2026-09-06 — 面板与日历HUD的IMGUI兼容热修
- 用户反馈F5面板无法调出。E测试副本日志有358次`NotSupportedException: Method unstripping failed`，堆栈明确到`GUI.DrawTexture(Rect,Texture)`→`ModPanel.DrawPanel`→`OnGUI`；快捷键并非根因。
- Cecil遍历实际目标interop发现全部DrawTexture重载最终转到throw桩，HUD三处调用同样潜伏此问题。ZCode worker独立快照实现缓存GUIStyle背景+原生GUI.Box绘制，面板失败关闭并只记录一次，样式缓存成功后才提交；Operator修正Texture2D类型和命名工厂委托以通过net6/IL2CPP编译。
- Worker session=`sess_d9becb8c-8fbf-4b15-8131-3080deb46483`，ZCode0.16.5；本轮native `model.sdk.stream.completed`证明bigmodel/GLM-5.3，max仅请求、无独立effort证明。候选构建0W/0E；175个可达Unity包装方法检查无unstripping失败桩，仍需实机视觉验收。
- 01:36热修部署完成：独立复核通过，HUD readiness覆盖全部样式；canonical0W/0E、175方法interop检查、allowlist/diff通过。E副本DLL SHA256=A9B115D201889A56C045A14F859C03E1EB662374651334B723C562E0471CEC04（342528 bytes），退出门禁/备份/原子替换/目标hash核验完成；用户实机视觉与交互待复验，release压缩包未更新。


## 2026-09-06 — Coordinate 接入与发布材料审计（准备中）
- 在隔离工作树补齐 EXharness 标准实例工具，新增 `coordinate-release-audit-20260906`。既有游戏条目保持不变。
- 当前只完成 initial file half，尚未向服务器 record，尚未执行发布材料审计或完成 Gate F。
- 未运行游戏、构建、部署或存档操作；原 Operator 的游戏开发继续独立进行。

### 2026-09-06 — Coordinate 静态审计结果待审
- Windows native MCP 完成 initial record 与同参数幂等重放；win-omp 已完成唯一 result.md 产出。
- Operator 复算 27 个输入 Git blob 的 SHA-256/字节数全部一致；结果结论仍待独立审查。未运行游戏构建、游戏测试、DLL 部署或存档操作。
```

## Current Review Content

```md
**CHANGES_REQUESTED**

审查对象：
- report SHA-256: `29ff365add1aa8dcdde9475756b58a6feae100f12f3f29da3748aded7d231422`（result.md）
- packet SHA-256: `9247c8b919b3d7633baaffbd31c8f17ff4ee5e91ebd98f9d34f597dccdc46ccb`
- plan SHA-256: `eefc016e9f5dcd96a48c704eee7e5e18e866d98c31478b72eada05d31a654b56`（与报告 §2 表末 plan.md 条目一致，交叉成立）

本结论不预先标 task done，不构成 Gate F 或任何游戏版本发布通过证明，也不因尚待 completion 而循环批准。worker 自称"无不一致"未作为证据；以下均基于 packet 内全文的独立复算。

**独立复算通过的部分**（界定返工边界）：startup 时长 15:55:51.5100453→15:56:39.8617666+08:00 = 48.3517213 s，报告 48.351721 s 与 "48.4s" 四舍五入一致；采样点 20 个、全部 responding=true 属实；publication.md 第 4/5/6/7/8/9/10 行定位逐行复核正确；csproj 第 12 行 `<Version>4.5.0</Version>` 正确；receipt/manifest/github-release 的 ZIP/DLL hash、39440865、313、374272、c203218 逐字段一致；UPDATE_LOG 第 4 行、CAPABILITIES 第 3 行、USER_GUIDE 第 5 行定位正确；脚本 leak_check 排除词、doorstop 豁免、config 只放行 BepInEx.cfg、无 CRC/白名单校验逻辑均与源码相符；已核验/记载未复验标注纪律整体成立，无把材料自述当本轮实测的越界。验收标准 1–5 的结构覆盖完整。

**P0**：无。

**P1**（阻断批准的字段级事实错误，最小修复后即可进入后续 completion 步骤）：

1. §3.8 时间线："tag 对象 07:57:15+08:00" 与 §3.1 "tagger 时间 2026-09-06T15:57:15+08:00" 互相矛盾。按 §3.8 字面归一，07:57:15+08:00 = 2026-09-05T23:57:15Z，早于 manifest GeneratedUtc 07:55:01Z，所印序列非单调，该节 "[已核验，内部一致]" 结论不被自己列出的数字支持。15:57:15+08:00 = 07:57:15Z 与全链（startup 止 07:56:39Z → asset createdAt 07:57:23Z → publishedAt 07:57:30Z → receipt commit 07:59:43Z）严丝合缝，§3.8 系时区换算笔误。最小修复：重读 tag 对象确认后，§3.8 改为 "tag 对象 07:57:15Z（= 15:57:15+08:00）"，全序列统一用 Z 表述。

**P2**（精度缺陷，同轮一并修）：

1. §3.9 "四份当前文档顶部均带 4.5.0 标识与 2026-09-06 日期"：日期仅存在于 UPDATE_AND_FIX_LOG（第 4 行）与 CAPABILITIES（第 3 行）；MOD_V4.5.0版本更新说明.txt 与 USER_GUIDE 顶部无日期。修复：版本标识 4/4、日期 2/4 并点名文件。
2. §3.9 "共用同一套'新说明优先于后文历史数值'层级约定"：该明示约定仅 USER_GUIDE、CAPABILITIES 承载；UPDATE_LOG 以 "以下为历史版本记录：" 分隔实现同义分层；V4.5.0 说明无历史区段、约定不适用。修复：按实际承载方式分述。
3. §3.9 "安装指引…在四份当前文档间一致"：CAPABILITIES 无安装指引章节；相同安装文本实见于 3 份（V4.5.0 说明/USER_GUIDE/UPDATE_LOG）。修复：改为三份，并注明 CAPABILITIES 仅在观察项中含联机同版本要求。
4. §4 "个人 KingdomEnhancedMod.cfg 与其它 .cfg 不可能入包"：超出源码可静态证明范围——按名过滤 .cfg 只在 BepInEx/config 分支存在；leak_check 不排除 core/unity-libs/dotnet 下的杂散 .cfg（报告下一条已自认黑名单非白名单）。修复：限定为 "config 目录下除 BepInEx.cfg 外不入包"。
5. §3.1 字段定位 "plan.md 第 3 行"：release-450 plan.md 第 3 行只提 "a new v4.5.0 tag"，不含 commit c203218；tag→commit 绑定的实际定位是审计 plan 问题与目标段（第 9 行）。修复：更正定位或从该处定位清单删除 plan.md。

以上均不改变审计实质结论（材料交叉一致、无实物项未冒称重验、游戏边界保留）。修订后按验收标准 6 生成新 review packet 并重新绑定 review SHA。


Reviewer provenance: Windows OMP 17.3.4, kimi-code/k3, native session 01a077db-df47-7000-ab5c-dccd1d8ac6ef. Read-only review of the supplied complete source texts and frozen packet.
```

## Closeout Questions

1. 当前实现是否已经覆盖 acceptance
2. verification 是否足以支持从 `doing` 进入 `done`
3. 还有没有阻止 closeout 的高优先级问题
4. 如果不能 done，最关键的剩余工作是什么
