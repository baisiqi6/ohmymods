# Review 请求：serpent-leash-025 大蛇离墙缰绳（boss 行为变更，需 reviewer 审核）

## 你是谁

OMP reviewer（只读）。不修改任何文件、不 commit、不部署。产出 verdict
（approve / must-fix / question）+ 逐条理由，must-fix 给行号与修法。

## 审查对象

- 实现：`il2cpp/PatchWorld_SerpentLeash.cs`（新文件，约 120 行，Operator 直接实现）
- 游戏源码参考（2.1.0，只读）：`game-source/Assembly-CSharp-2.1.0/WorldEatingSerpent.cs`
  （全部状态机/位置逻辑）、`MtOlympusGates.cs`（SerpentAnchor 属性）
- 模式先例：`il2cpp/PatchWorld_DefenseSpacing.cs`（World.OnLevelLoaded 协程宿主范式）
- 编译已过（0 error 0 warning），已部署 E 盘测试副本（build=2.2.0-serpent1）。

## 背景（2.4.0 IL2CPP，BepInEx 6 + HarmonyX + Il2CppInterop）

用户问题：希腊最终岛城墙推近奥林匹斯山门后，大蛇白天在墙外很近处活动，
墙边小兵被它的攻击覆盖。需求：让大蛇离城墙远一些。

## 实现概述（核对代码与下列论证的一致性）

大蛇休息位 = `MtOlympusGates.SerpentAnchor`（关卡加载瞬移 `OnLevelLoadedHandler→
UpdatePosition(GatePosition)`；Moving 状态回巢 `SetGoal(anchor.gameObject,...)`）。
本补丁把锚点 x 推到 `右墙(GetBorderSideIntact)+14`，只向右、幂等、10s 复扫 +
OnEnable postfix 即时。

Operator 的随迁论证（请逐条独立核验，找反例）：
1. 冲锋线 `GetMinChargePositionX = max(锚点-0.55, 墙+4+6)`：锚点右移后由锚点项主导，
   大蛇在墙+14 休息、longRangeScanner(6) 只覆盖到墙+8，墙边小兵白天不再被
   ShouldAttack/ShouldPrepareCharge 选中；玩家把部队推到墙+8 外冲锋照常。
2. `IsBlockingGate`：`蛇.x - GatePosition <= 8`——GatePosition 就是锚点本身，
   蛇贴新锚点 → 距离 0，照常挡门。
3. 弱点锚点 `CalculateWeakPointAnchors` 按 worldBounds 均分，与蛇锚点无关。
4. 嘴部传送门是大蛇子物体，跟随蛇本体。
5. 联机：锚点是场景对象不走网络同步；FSM/位置权威端决定，客户端蛇是傀儡；
   墙位置双端经同步后计算收敛一致（瞬态分歧只影响无形的空锚点 transform）。

## 重点审查清单

1. **锚点右移的副作用**：全源码检索 SerpentAnchor 的所有消费方（不止
   WorldEatingSerpent——MtOlympusGates 自身/其他类/动画/特效有没有引用？），
   确认移动该 Transform 不会影响门体视觉或其他玩法。
2. **几何边界**：`GetBorderSideIntact(Side.Right)` 无墙/墙被毁时的返回值；
   targetX 是否可能把锚点推过世界右边界（worldBounds.right 之外）造成
   蛇出图/水下渲染异常？是否需要与 worldBounds.right 取 min？
3. **幂等与复扫**：10s 复扫期间城墙右扩的窗口行为；锚点只向右推是否会
   在墙后退（被毁）后留在一个不合理远的位置（可接受？）。
4. **OnEnable postfix 可靠性**：Unity 消息在 2.4.0 AOT 下 HarmonyX 可钩性
   （参考：Banker.Awake prefix 在生产中工作；若 OnEnable 不可钩，10s 复扫兜底
   是否足够）。
5. **大蛇状态机中是否有其他直接写 transform 位置的路径**会被锚点右移干扰
   （如 Submerged/Stunned/Puking 的位置假设）。

## 明确非目标

- 不改大蛇伤害/血量/攻击频率（难度不变，只改活动位置）。
- 不处理左侧（大蛇与山门只在右侧）。
