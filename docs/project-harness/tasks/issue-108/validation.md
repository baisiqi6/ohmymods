# Issue 108 验证记录

## 变更面

- 生产（4 文件，全部在授权清单内）：
  - `il2cpp/PatchWorld_FleetBoatFormation.cs` — 仍唯一拥有 `unitTypes/units/UnitSpacing/startOffset`、
    profile/coordinator/baseline 与定向招募事务；新增弩手排槽位与 family 参数化。
  - `il2cpp/Patch_MusketeerFormation.cs` — 抽出共享 `IsFreeForRearRow`、火枪资格排除弩手身份、
    纯布局两排重载（旧 4 参重载保留并委托）、共享 nearest 比较器。
  - `il2cpp/CrossbowmanLifecycle.cs` — `FormationLife` 稳定号（首次建立/真池新 life 换号）、
    `FormationLife/MatchesFormationLife` 读者、`CopyOwnedArchers` 有界快照。
  - `il2cpp/Patch_CrossbowFormation.cs`（新）— 预留门/候选资格/有界收集/入队后宿主 Reconcile 边界。
- 测试：`tests/crossbow-banner-formation/`（新）、`tests/musketeer-formation/`（纯布局/策略 + e2e）、
  `tests/crossbow-lifecycle/`、`tests/musketeer-formation/interop-check`（stub 方法名）。
- 文档：本目录 plan/validation。
- 未改：`PatchRoles_CrossbowDefense.cs`（Qualify 已有 `GetFormation()!=null` 排除）、PNG/资产、
  版本/构建配置、Harmony 钩子集合、配置项、经济/存档/网络/数值。

## 托管回归（本机 .NET 8，`dotnet run -c Release`，日志见私有 evidence/test-runs.log）

| 套件 | 结果 |
|---|---|
| `tests/crossbow-banner-formation`（纯布局/策略） | 12 passed, 0 failed |
| `tests/crossbow-banner-formation/integration`（真实 owner+策略+真实 Lifecycle 组合） | 9 passed, 0 failed |
| `tests/musketeer-formation`（纯布局/策略） | 50 passed, 0 failed |
| `tests/musketeer-formation/e2e`（真实生产 owner + 定向事务） | 38 passed, 0 failed |
| `tests/crossbow-lifecycle`（真实生产 CrossbowmanLifecycle） | 42 passed, 0 failed（511 断言） |
| `tests/fleet-greek-squads`（提取生产 owner 片段） | 91 passed, 0 failed |
| `tests/crossbowman-wallpierce`（编译生产 PatchRoles_Crossbowman） | pass=34 fail=0（311 断言） |

R1/R2 修正后的新增覆盖：

- 0 弩手举旗仍预留弩手 Squire×4（无 phantom Gap/单位）；之后新转职/读档重算完成的弩手由既有
  0.5s 维护补入，满员 4 上限。
- 真实职业包组合（integration，编译真实 `CrossbowmanLifecycle`）：native 顺序入队后
  `ActiveArrowAttack`=克隆弩矢 SO、射程 12/扫描器 12、2× 间隔、死地控制器生根与旗色；
  发射读取的 `ActiveArrowAttack` 即弩包；Unregister→Hunter postfix 维持弩皮且
  `GetFormation()==null`（夜守资格链恢复，Defense 源码未改）；火 buff 与合法 formation 间隔
  不被入队 Reconcile 清洗。
- 外域 actor / Menu 窗口：native 回调把 actor 移出当前 world layer、或切到 `Game.State.Menu`
  （timeScale 仍 1）时，成功终检拒绝，直接 fallback 与 seatless 重试都不对 actor 调 native leave
  （只撤本 owner 可确证旧引用、receipt 保留）；合法窗口（auth/offline/未暂停/Playing/当前 scene）
  恢复后恰好归还一次同 life 债、再维护不重复、换 life 的旧债不触新演员；清理路径的演员 mutation
  统一复用同一 window（不读功能开关，feature off + Playing 的同 life 债仍可清）。
- same-life 与门：native 回调内同指针换 life 的嵌套 `TryRecruit` 被严格 directed 拒绝、外层成功
  被拒且只撤旧数组引用（不 `OnLeave` 新 life）；directed scope 在失权/功能关/world 替换/actor
  移出/双族互斥任一变化后失效；Convert/OnSeated 回调关闭功能或换 life 时成功终检拒绝（不冒认
  OnSeated），失权时按既有 authority/currentScene 清账门保留同 life 债、恢复后安全收口；
  带 captured lease 的 seatless 收据在身份关闭（Active=false）时仍按同 life 完成清账；Strip 保留
  token、重新选择换 token、真池边界无条件换号。
- integration 的 native shim 按已核机器码读取 `archer.soldierAnimator`（非当前 Animator 控制器）
  作 BiomeSwap 输入，ConvertToHunter 先写普通 hunter 控制器再走真实 Lifecycle postfix，
  以字段路径与写序证明弩皮/职业包保持；发射为「读取当前 `ActiveArrowAttack`」的静态断言，
  未运行原生发射/物理（如实标注，不等于实机弹道）。

覆盖点（按用户验收清单）：

- 满员 `4 重步 + Gap + 4 弓 + 4 火枪 + 4 弩` 站位的类型序、槽位索引与原生坐标（两侧镜像、
  0..4 船）；满员时原弓手/重步坐标=基线，船队整体后退两排。
- 0..4 缺员（含 Musk0/Cross4、Arch0、弩手 0 席但预留）：原生空槽压实公式
  （弓手线 = 基线 − 缺员数×弓箭步长；最近一排成员距弓手线 1 步，弩手排距火枪排 1 排）。
- 弩手不占弓手槽/火枪槽/骑士岗位：非定向 `TryRecruit` 一律拒绝；普通弓手照旧填满原生 4 弓位；
  塔位/英雄/其他编队/上船/被抓/死亡候选全部排除；nearest-first 与 4 席上限（多余弩手留在场外）。
- 事务失败：`ConvertToSoldier` 抛错回滚 + 下一次维护重试、数组被他人替换时直接 `OnLeave` 一次、
  半注册 + 失败 `Unregister` 的收据在**换 life 后**只清数组引用、绝不对新 life 调 `OnLeave`。
- 反复举旗/收旗幂等、收旗恢复精确基线、关闭（身份失效/全局关）经原生 `Unregister` 释放本排成员且
  不热缩数组、暂停/失权/换场景不写入。

## actual 2.4 ARM64 interop 构建（仅本地证据输出，无部署）

- `evidence/actual-arm/Integration.csproj`：161 条真实引用（`/Applications/ohmymods/arm64/BepInEx`），
  `Compile`/`EmbeddedResource` 指向本候选 clean tree 全部 `il2cpp/*.cs`，`BepInExPluginsPath` 为空，
  强制 `-t:Rebuild`。
- 结果 `0 Warning / 0 Error`；DLL SHA-256
  `28c43a3ba44e40e264911c482d284e993409011adb4d8d398aa024d52c027e12`（Menu 窗口修复后 Rebuild）。
- 二进制含本批新增字面量/元数据（`[BannerRow] `、`rear rows reserved`、`[CrossbowFormation] `、
  `HasDirtyRowTypes`、`MatchesFormationLife`、`CopyOwnedArchers`、`DirectedCrossbowFamily`、
  `IsDirectedActorLive`、`RearRowNearestFirst`），确认新代码真实进入编译。`Archer.TryRecruit`/`Formation.IFormationUnit`/
  `UnitTypes` 的调用与 Harmony 目标属性均对真实 2.4 interop 程序集编译通过。

## 未验证边界（保持待验，不视为通过）

- 实机：左右站位观感、满员/缺员压实、弩矢攻城、收旗回墙、密集战斗帧耗时、跨岛、联机。
- 原生只读核验（本机 2.4 x64 机器码切片，非 ARM 动态验收）：`Archer.TryRecruit` 的
  `RegisterUnit → ConvertToSoldier → … → _currentFormation` 异常窗口确认；`ConvertToSoldier` 对
  `ActiveArrowAttack/shootRange/_shootIntervalRange(_Formation)` 无直接写入（改由 routine
  Stop/`SetDesiredAttackMode` 间接路径，实机运行效果未验，入队即时 + 5s 巡检的宿主 Reconcile 兜底）；
  `OnLeaveFormation` 清 formation 后 `ConvertToHunter`（现有 postfix 维持弩皮）。
- 弩手读档重算完成前（约 15s 窗口）弩手尚无 identity，不会入队；但弩手排自举旗起已经预留，重算完成后由既有 0.5s 维护补入（R1），无需重新举旗。
