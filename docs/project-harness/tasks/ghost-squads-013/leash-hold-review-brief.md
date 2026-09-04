# reviewer 任务书：审查 PatchDivine_GhostLeashHold（希腊幽灵 leash 驻守修复）

## 审查对象

- 新增文件：`il2cpp/PatchDivine_GhostLeashHold.cs`（未提交，git 工作树中）
- 需求与设计定案：`docs/project-harness/tasks/ghost-squads-013/leash-hold-worker-brief.md`
- 关联既有实现（不应被改动）：`il2cpp/PatchDivine_GhostSquads.cs`
- 原生参考源码（只读）：`game-source/Assembly-CSharp-2.1.0/` 下
  `WarriorGhostLeaderGreece.cs`、`WarriorGhostGreece.cs`、`WarriorGhostLeader.cs`、
  `WarriorGhost.cs`、`HelsGhost.cs`、`SummonGhostSteedAbility.cs`、`Mover.cs`
- Operator 已独立构建通过：`0 警告 / 0 错误`（dotnet8 build -c Debug，禁用部署复制）

## 需求背景（一句话）

原生希腊幽灵（Cerberus 召唤）`StartDeathCountdown` 是"离召唤者超 `_maxPlayerDistance` 即处决"，
而其冲锋 AI 永续向外推进，站桩玩家会看着小队冲出边界集体自杀。修复：去掉处决，改为
边界钉住驻守（ForceStop+Pause 压制原生 SetGoal）+ 60s 定时消亡兜底（防 HasGhosts 锁技能）。

## 逐条核对清单

1. 两个 HarmonyPrefix 是否正确拦截 `WarriorGhostLeaderGreece.StartDeathCountdown` 与
   `WarriorGhostGreece.StartDeathCountdown`，且 mod 关闭/无世界权威时 `return true` 完全走原版。
2. 钉住机制是否真能压制原生冲锋：原生 Greece `Charge()` 每 1s `SetGoal` 向外；补丁每 0.5s
   `ForceStop()+Pause(0.75f)`。请核对 `Mover.cs` 中 `Pause`/`_pauseTimeout`/`ForceStop`/
   goal 施加速度的实际语义（暂停期间目标是否完全不生效、Pause 是否取 Max 不被 shorter 覆盖），
   判断是否存在间隙导致幽灵继续外溢或抖动。
3. 60s 定时消亡是否可靠：`KillUnit()` 调用路径、原生死亡表现、`_activeGhosts` 列表回收链
   （OnDisable → GhostHolder.RemoveActiveGhost）是否完整，`HasGhosts` 门能否解除。
4. 生命周期安全：池化复用（Pool despawn → SetActive(false) → 协程停止；再召唤重新走
   StartDeathCountdown）、场景切换、Summoner 为 null、协程内异常——是否存在协程泄漏、
   双重监督、或对象销毁后访问。
5. IL2CPP/Interop 正确性：`__instance._maxPlayerDistance`（protected，跨代理）、
   `ghost._mover`（archer 侧为 private）、`WrapToIl2Cpp()`、`System.Collections.IEnumerator`、
   bool Prefix 语义（HarmonyX）。与仓库既有补丁（如 PatchDivine_GhostSquads 对
   `ability._rider` 等私有字段的访问）风格一致性。
6. 联机安全：仅 world-authority 运行监督；客户端表现（PositionSync/动画同步）是否被破坏；
   authority 迁移（断线重连换主机）时已启动协程的幽灵会怎样。
7. 范围检查：`git status --porcelain` 应只有 4 项（AGENTS.md / collaboration-protocol.md 是
   Operator 的协作规范更新、任务书、新补丁文件），北境行为、SummonGhostSteedAbility 原生
   逻辑、ModConfig 均未被触碰。
8. 副作用评估：补丁作用于类级，原生第一队希腊小队同样改为驻守（这是设计意图，确认无额外
   意外——例如其他使用 WarriorGhostGreece/LeaderGreece 的场景是否只有 Cerberus 召唤）。

## 输出

`approved` / `changes_requested`（附证据与修改建议）/ `blocked`（附阻塞原因）。
每条结论给证据（文件:行号 或 源码引用）。中文输出，保留英文标识符。

## 边界

- 只读审查：不得写任何文件、不得 commit/push、不得部署。
- 如需运行只读命令（git status/diff、grep 等）可以执行，但不得有任何 mutation。
