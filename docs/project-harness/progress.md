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
