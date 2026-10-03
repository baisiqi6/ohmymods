# Issue 108：弩手举旗后排（2 币原生举旗 + 最多 4 名弩手）

用户 2026-10-03 要求：花原有 2 枚金币举旗攻城时，弩手作为独立后排加入，最多四名。满员顺序为
`4 重装步兵 → 原生间隔 → 4 普通弓箭手 → 4 火枪手 → 4 弩手`，远程队列使用与弓箭手相同的间距；
人数不足沿用原生空槽压实与招募逻辑，不生成/转职额外单位、不增加举旗费用。旧 #17“弩手只守墙、
不进编队”的保护按下述边界扩展：弩手仍**不得**占普通弓箭手槽位或火枪手槽位，只有本编队自己的
四个弩手槽允许定向入队。

## 变更面

- 生产：`il2cpp/PatchWorld_FleetBoatFormation.cs`（唯一编队数组/事务 owner：新增弩手排槽位、
  共享定向招募事务按 family 参数化、维护清理/补位、脏类型守卫改名覆盖两排）、
  `il2cpp/Patch_MusketeerFormation.cs`（抽出共享 `IsFreeForRearRow` 空闲门、弩手生涯互斥、
  纯布局 `TryCompose` 两排重载、共享 nearest 比较器）、
  `il2cpp/CrossbowmanLifecycle.cs`（稳定生涯 life 号 `FormationLife`、确证池新 life 换号、
  有界自有 registry 快照/预留门）、**新增** `il2cpp/Patch_CrossbowFormation.cs`（弩手侧策略：
  预留门、候选资格、有界收集、入队成功后的宿主 Reconcile 边界调用）。
- 测试：`tests/crossbow-banner-formation/`（新，纯布局 + 策略）、
  `tests/musketeer-formation/`（布局/策略 + e2e 双排与 cross 事务/生命周期用例）、
  `tests/crossbow-lifecycle/`（生涯 life 建立/换号/快照用例）、
  `tests/musketeer-formation/interop-check`（stub 方法名同步）。
- 未改：`PatchRoles_CrossbowDefense.cs`（`QualifyNightDefender` 已排除 `GetFormation()!=null`，
  入队无需接线）；PNG/资产、版本号、配置项、`Main.cs`、经济/存档/网络/数值（移速/伤害/射程/频率）。

## 设计

### 1. 数组仍是单一 owner，弩手排在火枪手的更后方

`PatchWorld_FleetBoatFormation.FormationProfile` 新增 `CrossbowSlots/CrossbowRow`，弩手排与火枪排
同为 `Squire` 槽（4 席），在第一个原生 `Archer` 槽前插入；数组低→高为
`弩手排 → 火枪排 → 原弓手/重步`，原生坐标由高 index 向低镜像绘制，物理顺序即
`重步 → 原生 Gap → 弓手 → 火枪 → 弩手`。`startOffset` 按**实际存在的排**扣减
（两排 8 步、单排 4 步），`UnitSpacing[Squire] = UnitSpacing[Archer]`（真实 2.4 资源
0.21875），原生 Gap/Fleet 块与满员时弓手/重步坐标保持基线。缺员时空 `Squire` 槽不参与原生
累加（`GetXPosForIndex` 只计"有单位或 Gap"的槽），按原生压实语义收紧；不额外补 Gap 或占位。

- 弩手排独立于 `MusketeerEnabled` 与火枪人数：火枪关/0 人时弩手排直接退化到紧贴弓手线。
- 预留门（R1 终稿）：模组开启 + 离线权威/已知 world 即预留，与弩手数、registry 内容、
  火枪开关无关；没有弩手时 4 个空 Squire 槽按原生压实，不生成任何单位。

### 2. 定向招募事务共用，life 证明按职业族取值

两排共用同一套 `TryDirectedRecruit/收据/脏类型闸`，receipt 增加 `Family`：
- 火枪：`MusketeerRuntime.BindingLease / MatchesBindingLease`（不变）；
- 弩手：`CrossbowmanLifecycle.FormationLife / MatchesFormationLife`（新增稳定号）。
- 定向上下文携带 family + captured life（R2）：guard 的放行除精确指针对外，还核同 life 与该行
  身份；同指针在 native 回调内换 life/换职业不会继承豁免。成功终检复核 captured life/current
  world/本 formation 身份，宿主 Reconcile 之后再复核一次（Reconcile 可能同步回调）。
- 带 captured lease 的旧收据在触碰演员前先判 same-life；失配（同 GO 新 life，即使恰好绑定
  本 formation）只撤本 owner 的旧数组引用，绝不 `UnregisterUnit/OnLeave` 新 life。

`CrossbowmanMarker.Revision` 仍是同步重入代次，绝不作为 life。Token 生命周期（PRE 定稿）：
Strip 保留旧 token 供同 life 旧债清账；**真正重新选择职业的 Apply 换新 token**（幂等 Apply/
Reconcile/hide 不换）；确证的真池新 life 在 `Archer.OnEnable` prefix 里、任何 Strip/回调之前
无条件换号。溢出 fail closed（不退回已发号）。`MatchesFormationLife` 只比较 token，不读
Active/Enabled——配置关仍能完成同 life 清账。

### 3. 资格门与守卫

- 共享空闲门 `IsFreeForRearRow`：当前 world/active/enabled、无编队、非骑士随从、非塔位/守位、
  未上船/未待上船、非玩家控制、非 inert/grabbed、未死亡、非英雄——两排逐字共用。
- 弩手额外要求 `CrossbowmanLifecycle.IsCrossbowman`（live、fail-closed），且排除已付火枪生涯；
  火枪资格同样排除弩手身份 → 两排候选互斥，不会互相偷槽。
- `Archer.TryRecruit` 守卫：只有**精确定向对**放行，且同时核 captured life、该行身份与两族互斥、
  离线权威/在线、actor 仍在当前 world layer、当前 world/layer 与 arm 时刻一致、该行功能谓词仍在
  playing；任一不满足即回落到普通守卫（弩手在玩家编队上的任何非定向招募一律拒绝，仍不占
  弓手/火枪/骑士岗位）；脏类型期间全体拒绝。候选只来自
  `CrossbowmanLifecycle` 有界 `_owned` registry（绝不 `FindObjectsOfType`/全场景扫描），
  nearest-first、最多 4、确定性 tie-break（instanceID）。

### 4. 职业包保持

原生 `Archer.TryRecruit` 内部会执行 `ConvertToSoldier`（换控制器+banner 染色）。弩手的
`soldierAnimator` 已生根到死地控制器（换皮表原样穿透 → 同引用），入队后由
`PatchCrossbowFormation.OnSeated` 调用宿主既有 Reconcile 边界（`OnArcherEnablePostfix`）
在同一流程内重新断言弩矢 SO/射程/生根皮肤/旗帜色；不新增数值、时钟或 Harmony 钩子。
收旗走原生 `OnLeaveFormation`/`ConvertToHunter`（既有 postfix 维持外观），
`PatchRoles_CrossbowDefense.QualifyNightDefender` 因 `GetFormation()!=null` 自动排除编队成员，
收旗后按原有夜间守位资格回防。

## 已知边界（不声称为验收）

- 实机左右站位、满员/缺员观感、弩矢攻城、收旗回墙、跨岛与联机均未实机验证；模拟测试不替代实机。
- 读档后约 15s 的弩手重算窗口：弩手排自举旗起始终预留，重算完成/新转职出现的弩手由既有 0.5s
  维护补入（R1），不需要重新举旗；该窗口内弩手尚未获得 identity 前不会入队。
- 两排都存在且人数不足时按原生空槽压实（弓手线随缺员前移），这是用户要求的行为，不是原生位置保持。

## 验证

见 `validation.md`。实现细节与原生证据（含 actual 2.4 ARM64 interop 编译）在私有任务证据目录。
