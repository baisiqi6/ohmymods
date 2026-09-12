# worker 任务书：希腊幽灵小队 leash 处决改为边界驻守（ghost-squads-013 后续）

## 背景（根因，Operator 已核实）

用户报告：Cerberus 召唤的希腊亡灵小队一直向前冲锋，超出离玩家一定距离就集体死亡，"像赶着自杀"。
北境小队正常（跟随玩家、30 秒消亡）。

原生 2.1/2.4 源码证据（`game-source/Assembly-CSharp-2.1.0/`）：

- `WarriorGhostLeaderGreece.cs`：`ShouldFollowPlayer()=false`、`ShouldCharge()=true`（无条件）；
  `StartDeathCountdown()` 启动 `DespawnWhenTooFarAway()`——每 0.5s 检查
  `|x − Summoner.x| > _maxPlayerDistance` 即 `KillUnit()`（999 伤害处决）。
  其 `Charge()` 无敌人时每 1s 设 goal 为"离营火反方向 5 单位"，即永续向外推进。
- `WarriorGhostGreece.cs`（弓箭手）：同样的 `DespawnWhenTooFarAway` 处决；
  `ShouldCharge()=(_shootingTarget==null && _warriorGhostLeader==null && hp>0)`
  （队长死后弓箭手也会向外冲）。
- 基类 `WarriorGhostLeader._maxPlayerDistance` 默认 12（protected）；`WarriorGhost._maxPlayerDistance`
  同样 protected 默认 12。基类 `HelsGhost` 有 public `Summoner`、`Duration`、`KillUnit()`。
- 这是原生设计，不是本 mod 引入的 bug。本 mod 的 `PatchDivine_GhostSquads.cs` 把召唤扩成
  4 队（2 希腊 + 2 北境），希腊队沿用上述原生行为，所以问题更明显了。

## 设计定案（Operator 锁死，不要改方案）

把希腊幽灵（leader 与 archer 两个类）的"距离处决"替换为"边界驻守 + 定时消亡"：

1. **不再处决**：跳过原生 `StartDeathCountdown`（即不启动 `DespawnWhenTooFarAway`）。
2. **边界驻守**：每个幽灵自启一个监督协程，每 0.5s 检查与召唤者的 x 距离；
   当 `|dx| >= _maxPlayerDistance − 1.0` 时，`Mover.ForceStop()` + `Mover.Pause(0.75f)` 把它钉在原地
   （原生 `Charge()` 每 1s 会重新 SetGoal 向外，Pause 语义参考原生 `HandleOnReceiveDamage` 的
   `_mover.Pause(1f)`——暂停期间目标不生效，我方 0.5s 节奏持续压制即稳定驻守）。
   驻守期间 Update/FSM 照常跑：砍击（leader Slash）、射箭（archer Shoot）不受影响。
   玩家重新接近使 `|dx| < 阈值` 后不再钉，原生冲锋自动恢复。
3. **定时消亡**：监督协程启动时记 `expireAt = Time.time + 60s`，到期 `KillUnit()`
   （走原生死亡表现）。必须补这个消耗机制：原生 Greece 幽灵唯一死期就是 leash 处决，
   若只去掉处决，`SummonGhostSteedAbility.IsAbilityReadyAndNotInUse` 的 `HasGhosts` 门会永久锁技能。
4. **兜底**：监督过程中 `Summoner == null` → 空引用风险，直接 `KillUnit()`；tick 内异常 → 记一次错误日志并
   `KillUnit()`（失败也不能留下永生幽灵）。幽灵死亡/回收后协程随对象销毁自然终止，tick 内仍要判
   组件/GameObject 有效。
5. 北境幽灵（基类行为）与北境神器召唤完全不动；`PatchDivine_GhostSquads.cs` 现有逻辑一行不改
   （类级补丁自动覆盖原生第一队 + 补充第二队，这正是想要的）。

## 实现契约

- **新建且只新建一个文件**：`il2cpp/PatchDivine_GhostLeashHold.cs`，namespace `KingdomEnhancedMod`。
  csproj 是 SDK 风格隐式通配 + 插件 `harmony.PatchAll(assembly)`，新文件自动参与编译与注册，
  不需要也不允许改任何其他文件。
- 结构参照 `il2cpp/PatchDivine_GhostSquads.cs` 的现有风格（静态类 + 独立 `[HarmonyPatch]` 类 +
  一次性失败日志 + `KingdomEnhancedPlugin.Instance?.LogSource.LogInfo/LogError`，日志前缀 `[GhostLeashHold]`）。
- 两个 Prefix 补丁：
  - `[HarmonyPatch(typeof(WarriorGhostLeaderGreece), nameof(WarriorGhostLeaderGreece.StartDeathCountdown))]`
  - `[HarmonyPatch(typeof(WarriorGhostGreece), nameof(WarriorGhostGreece.StartDeathCountdown))]`
  - 前缀逻辑：`if (!ModConfig.Enabled.Value || !NetworkBigBoss.HasWorldAuth) return true;`
    （mod 关闭或无世界权威时走原版处决，与全仓库分流惯例一致）；
    否则 `__instance.StartCoroutine(Supervise(...).WrapToIl2Cpp()); return false;`
    （协程返回 `System.Collections.IEnumerator`，必须 `using BepInEx.Unity.IL2CPP.Utils.Collections;`
    后 `.WrapToIl2Cpp()`，照抄 PatchDivine_GhostSquads.cs 第 266 行的用法）。
  - 两个 StartDeathCountdown 都是 public 虚方法；SummonRoutine 经 HelsGhost 虚分发调用，prefix 生效。
- 监督协程常量：`TickSeconds=0.5f`、`HoldMargin=1f`、`LifetimeSeconds=60f`。
- 字段访问：`_maxPlayerDistance`（leader protected / archer protected，Il2CppInterop 代理均已暴露为属性，
  直接 `__instance._maxPlayerDistance` 读序列化真值，不要硬编码 12）；
  Mover 优先 `_mover` 字段属性，拿不到就 `GetComponent<Mover>()`。
- 钉住判定对两侧对称（玩家前进甩开、玩家后退拉开都算超距）。
- 日志：成功启动不刷屏；驻守触发、到期消亡各记 LogInfo 且同队列存活期间限流（简单做法：每个幽灵实例
  只记一次"holding"，全局到期消亡只在 20 个计数变化时记一条汇总，或干脆只在首次/异常时记——别每 0.5s 刷）。
- C# 现代语法（IL2CPP 主线，LangVersion latest），不引入任何新包/依赖。

## 验收（必须全部给出证据）

1. 在 `il2cpp/` 执行：
   `C:/Users/ADMIN/dotnet8/dotnet.exe build -c Debug -p:BepInExPluginsPath=`
   （空 BepInExPluginsPath 用于禁用向开发环境复制，只验证编译；以实际输出为准）
2. 构建输出必须 `0 Warning(s)` / `0 Error(s)`，贴出输出结尾若干行。
3. `git status --porcelain` 显示仅新增 `il2cpp/PatchDivine_GhostLeashHold.cs`（任务书本文件除外）。
4. 汇报：新文件完整内容、关键设计点如何落实（钉住节奏 vs 原生 1s SetGoal、到期 KillUnit、异常兜底）。

## 禁止

- 禁止 git commit / push / stash / reset；禁止部署到任何游戏目录（E 盘/D 盘都不许写）。
- 禁止修改本任务书之外的任何文件（包括 PatchDivine_GhostSquads.cs、ModConfig、csproj、文档）。
- 禁止改北境幽灵行为、禁止动 SummonGhostSteedAbility 的原生协程/冷却现有补丁。
- 不需要写单元测试；以编译 + 代码审查为验收，实机验证由 Operator 负责。

## 输出

中文汇报，保留英文路径/标识符。最终报告包含：变更摘要、验收证据（构建输出结尾、git status）、
新文件全文。
