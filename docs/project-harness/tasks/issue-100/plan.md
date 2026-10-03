# Issue 100：Greek 主银行家固定墙基活动域

用户 2026-10-03 授权（先 Issue 再 PR，合并后安装实测）：当前 Greek 本体银行家的活动与玩家金币
收集范围限定为 campfire 左右各最近的**固定墙基**之间；墙未建、被毁、升级或新增外墙都不改变该
范围。范围外君主扔出的金币仍由既有税收助手传送拾取并经现有入账事务结算。不弱化未知世界/owner/
联机门，不重写账本、扣款、存档或助手传送表现。

## 原生依据（独立只读调查）

- 未建墙基 Wall0 没有 `Wall` 组件，只有 `PayableUpgrade.nextPrefab` 直接指向带 `Wall` 的预制；
  建成墙与残骸都在当前 gameLayer。
- `Kingdom._orderedWalls` / `GetWall` 只反映“当前已建墙”，`GetBorderSide` 是地形边界：都不能
  代表固定墙基，且会在墙缺失时错误扩域（旧实现即依赖这些回退）。
- 同名 Wall0 装饰存在于脚手架/塔内，只带 BiomeSpriteSwapper；按组件判定天然排除。
- Greek 原生布局内层 ±9；Overgrown 内层 ±8.75、外层 ±17 —— 不能硬编码。
- `Managers.OnLevelLoaded(bool)` 在岛屿加载/读档 `TryPopObjectsToScene` 之后由 ProgramDirector
  调用，是“完整加载成功”的可靠通知点；`fromSave=false`（新岛）同样是成功路径，不是失败。

## 设计

### 1. `il2cpp/MainBankerFixedDomain.cs`（新文件）

- 独立 `Managers.OnLevelLoaded` Postfix（与既有 ShieldAudio Prefix 共存）：正常返回后才标记；
  标记时**绑定当次 world/kingdom/gameLayer/scene context key**；key 当场读不到则本次不武装
  （绝不延期用任意的未来 current 代替）；换代立即撤销旧域 proof；不受 ModEnabled 开关影响。
- 安全维护点（Banker.Update 固定域前缀）做一次结构快照：遍历当前 gameLayer 子树
  includeInactive；候选=真实建筑根 `Wall` 组件，或 `PayableUpgrade.nextPrefab` 直接含 `Wall`；
  campfire 左右各取最近；两侧齐全、坐标有限、`left < campfire < right` 才发布。
- 捕获入库时对“要代表的这条通知”取入场 local 快照（key + generation）：读取前与发布前都与
  该 local 比较；扫描期间同 context 再次成功加载会作废本次快照并保留新通知 pending，
  绝不把旧快照发布到新代数。任何异常/非有限坐标/缺单侧都拒绝且**不部分发布**；
  发布后直到下一次成功加载通知不再重选（升级同坐标归并、倒塌、新增外墙都不改变已发布坐标）。
- 当前上下文可读且不等于通知绑定的 key 时，该通知（含 pending）**永久作废**，必须等 fresh
  成功通知；未收到成功通知/半加载/context 读不到 → 域未知 fail-closed（绝不凭 Playing 补域）；
  捕获失败有界低频重试（30 帧）；context 可读但不匹配 → 永久撤销旧 proof（不得跨 scene 复活）。

### 2. 银行家行为（`PatchEconomy_Banker.cs`）

- `TryGetMainBankerDomain` / `IsInMainBankerDomain` 改读固定域缓存；严格 `left < x < right`
  （等于边界归助手）；NaN/Infinity 一律拒绝；删除 GetWall/border 回退。
- 活动半径 `min(8.75, 域内最小距离 − 0.25)`；域未知时归还原生值（复用 owned 字段收据）；
  归还流程一开始即吊销该 profile 的“已应用域代数”标记：部分归还（后续 setter/身份故障）后
  同代重新开启，首个前缀立即重新应用。
- 方向扫描器只在域内且位置/缩放有限时贴墙，否则收敛为 0：仅优化，不做硬门。
- 新增独立 `Banker.Update` 前缀（prepare → profile → movement；与既有 ledger priming 前缀共存，
  不改其语义）：
  - prepare：维护点捕获（结构快照只读，不解网络 auth）；
  - profile：按各本体 `DomainAppliedGeneration` 刷新域内 wander/扫描，域换代后每个本体各自对齐；
  - movement：仅 `Current∈{GrabCoin(0), Idle(1)}`、`movingToGoal` 且 Position goal 域外时
    `Mover.Stop + GoToState(1)`；任何 `_executeQueuedState` 本帧不覆盖；goal 向内一律不动
    （Actor 域外也自然归位）；绝不停止财务 DropOff(4)/Payout(5) 或 StopAllCoroutines。
- 低频认领回收：registrar 快照里只清 `friendlyClaimer == banker.gameObject` 的域外 Player/Coins
  （原生 `ClearFriendlyClaimIfClaimer` 限定归还）；不动他人认领、农田来源、策略或余额；
  两条 target 清理路径都带 current world/layer/scene 门，绝不写旧/异层对象；
  明确证据（域外 Player/Coins）才清 `_targetCoin`。
- `Droppable.TryFriendlyClaim` 门补“当前本体 + 当前层 + 币坐标有限”；**资格成立后位置读取
  故障同样 fail-closed**。
- 新增 `Wallet.SuckCurrency` 前缀（最终硬门）：碰撞拾取可能绕过认领，只对 exact 当前 authority
  本体钱包（owner 由钱包 GameObject 取，读档期 `kingdom.banker` 可为 null 不作唯一来源）、
  同层 Player/Coins；域未知/非有限/域外/资格后位置读故障 `__result=false` 且不进入原生拾取；
  其他钱包/币保持原生。

### 3. 助手（`PatchEconomy_BankAssistants.cs`）

- 扫描与结算同源使用固定域；NaN/Infinity 币在 `IsTrackableCoin` 与 `CanCommitPickup` 两侧拒绝；
  等于边界的币归助手。交易/记账/传送算法不变。

## 测试

- 新增 `tests/banker-fixed-foundations`：以生产真实入口（OnLevelLoaded Postfix、TryCapture、
  Banker.Update 固定域前缀、两个门 Prefix、低频巡检）驱动，覆盖选择器/加载门/换代/冻结/
  边界/有限性/movement/认领回收/钱包门/网络门不变。
- 适配 `tests/greek-bank-scope`（旧契约 GetWall/border 回退移除，夹具改走通知+捕获发布域）
  与 `tests/greek-bank-assistants-scope`（夹具同改，新增非有限币 scan/commit 用例）；
  `tests/coin-courier-economy` 仅补全新成员 stub 以保持编译面（行为断言未改）。

## 已知边界（公开说明）

- 已证来源=成功完整加载后的结构快照与正常升级/摧毁替换。损坏存档或第三方在加载前预先删除
  内层墙，无法从当前对象集合辨别“缺失的内层”与“本来较宽的合法布局”，不在本实现恢复范围；
  此类快照按最近候选发布或（结构异常时）fail-closed，公开记录。
- 实机验证待做：读档/换岛首次捕获的真实事件顺序、连续升级/摧毁观感、密集投币回收频率、
  主客机联机表现。未启动游戏、未触碰玩家存档。
