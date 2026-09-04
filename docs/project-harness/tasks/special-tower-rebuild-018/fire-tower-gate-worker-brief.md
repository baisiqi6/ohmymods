# worker 任务书：火焰塔重建支持——燃料满且无工匠时开放重建（special-tower-rebuild-018 后续）

## 设计定案（用户提出，Operator 已对源码实证）

用户设计："火焰塔的火油燃料两金币一次次买满了、且塔上已经没有工匠时，开放 18 金币重建回
（当前世界六级）普通箭塔的支付交互。"——本质是用两个稳定游戏状态作为重建窗口，
保证与原生燃料购买交互永不照面。

原生源码实证（`game-source/Assembly-CSharp-2.1.0/`，只读）：

- `FireTower.cs`：`extends Workable`；燃料模型 = `_fireJarsActiveNum`（当前火罐数）与
  `_maxFireJars`（序列化上限，默认 12）。`IPayableComponentOwner.CanPay = _fireJarsActiveNum < _maxFireJars`；
  `OnPayHandler` 买一次 +1。
- `PayableComponent.cs`（燃料交互组件）：`CanSelect = CanPay(player) || (locked && 可显示锁因)`；
  而 `FireTower.IsLocked` 恒返回 NotLocked → **燃料满时 CanSelect=false，原生燃料交互完全隐藏**
  （连锁图标都没有）——此时我们的重建 PayableUpgrade 是该对象上唯一可交互项，零冲突。
- 工匠追踪：`Workable : Doable<Worker>`，`Doable._currentActors`（protected HashSet<Worker>）——
  与弩箭塔门控用的 `ballista._currentActors` 同一字段（interop 均暴露）。
- **无工匠 ⇒ 无在飞投射物**：FireTower 状态机 0(装填)→1(索敌) 的唯一转换在 `OnJobFinish`（工匠摇臂）
  内触发；无工匠则永远停在状态 0，`_projectile`（私有字段）恒为 null。买满+无工匠因此同时规避了
  火罐库存清理问题（原 fail-closed 名单的主因）。视觉假火罐（`_fakeFireJars`）是普通 Instantiate
  子物体，随旧塔根销毁，无需处理。
- 用户存档（gzip 解压）实证火焰塔资产名：`Prefabs/Buildings and Interactive/greece/Tower_upgrade_Fire_greece`。

## 实现契约（Operator 锁死；只改 `il2cpp/PatchWorld_SpecialTowerRebuild.cs`）

### 1. 源候选扩展

- 新增火焰塔候选收集：`Resources.LoadAll<FireTower>("")`（LoadAll 按组件类型天然排除
  `OilFireArcherTower`），过滤 `name` 含 `"Tower"`，按 Pointer 去重。
- 安全检查（新方法或扩展现有）：`GetComponent<FireTower>() != null` 且
  `GetComponent<OilFireArcherTower>() == null`（不检查 Ballista 相关——火焰塔资产本来就没有）。
- 弹箭塔候选集与安全检查**保持不变**（互不混入：两类资产组件互斥）。
- 逐候选配置复用现有 marker + PayableUpgrade + `ConfigurePayable`（nextPrefab 同为 tierSix、
  价格同源；`onlyInBuildableRegion=false`、`cooldown=RebuildCooldown` 等本轮已改的语义自动继承）。
  注意：火焰塔资产原生带 `PayableComponent`（燃料交互）——现有"已有原生 PayableUpgrade 则拒绝"
  检查只查 `PayableUpgrade` 类型，不会误拒 ✓ 不要改动该检查的类型。
- Ready 日志扩展为分组列出，如
  `Ready ballista=[Tower Ballista, Tower Ballista_greece] fire=[Tower_upgrade_Fire, Tower_upgrade_Fire_greece] target=Tower6 price=18 biome=5`。
- 幂等集合与 biome 重置沿用现有 `_configuredSources`（含两类源）。

### 2. 门控分流（CanInteract）

按实例组件分流：`GetComponent<Ballista>()` 走现有弩箭塔链；`GetComponent<FireTower>()` 走新火焰塔链：
- 通用前置复用（在线拒绝/世界权威/Playing/kingdom.isSafe/场景内/布局就绪等，与弩箭塔完全一致）；
- 火焰塔专属三条（新增 BlockReason：`FireFuelNotFull`、`FireWorkerPresent`、`FireProjectileInFlight`）：
  1. `fireTower._fireJarsActiveNum >= fireTower._maxFireJars`（燃料满；两字段 private 序列化，interop 暴露）；
  2. `fireTower._currentActors == null || fireTower._currentActors.Count == 0`（无工匠）；
  3. `fireTower._projectile == null`（防御性，按源码推演无工匠时恒真）。
- 未满足时走现有 `Block()`（限流日志照旧）——燃料未满时我们的 payable 不可选，
  原生燃料交互独占，正是设计意图。
- 工匠在玩家看到提示与付款之间上塔的竞态：CanPay/TryPrepare 全链路都过 CanInteract，
  现有机制天然覆盖（CanPay 失败→原生取消退币），不需要额外代码，但汇报里要论证这一点。

### 3. TryPrepare / ConsumePrepared / Pay

- 火焰塔无 bolt/`_bolt` 字段：现有 bolt 处理代码只在 Ballista 分支执行；
  火焰塔路径不碰任何可失败清理（门控已保证无 projectile、无工匠），token 机制照旧。
- `ConsumePrepared`/`Pay_Prefix` 零改动。

### 4. 诊断扩展

`SpecialTowerRebuildDiagnostics.ScanOnce` 同步扫描 `FireTower`：报告
`jars={_fireJarsActiveNum}/{_maxFireJars} actors={n} marker={} payable={} 锁定原因`（复用现有探针格式）。

### 5. 边界与约束

- 只改 `il2cpp/PatchWorld_SpecialTowerRebuild.cs`；现代 C#；不引入依赖。
- 禁止 commit/push/部署；禁止写游戏目录。沙箱无法构建：AST 语法自查 + 逐符号语义核对，
  构建由 Operator 执行。
- OilFire/Knight/Berserker/Baker/Mead 仍保持 fail-closed，不要顺手支持。

## 验收（汇报必须包含）

1. 改动后的关键方法完整代码（候选收集、门控分流、诊断扩展）；
2. 逐条对照设计点落实说明，特别是：燃料未满时两个交互的互斥论证、
   工匠竞态由现有 CanPay 失败路径覆盖的论证、无工匠⇒无 projectile 的论证；
3. `git status --porcelain`（应只有本文件 + 本任务书）；
4. 声明未改动弩箭塔既有链路、未弱化任何门禁/回滚。
