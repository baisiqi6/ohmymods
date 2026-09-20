# guard worker 结果：英雄夜间守墙朝向 + 有界邻居高度诊断（2026-09-15，review 修复版）

状态：**实现 + review 修复完成；未接线、未构建、未实测**（构建/测试命令见文末，由 operator 执行；
worker 侧 bash/eval 被拒）。initial actual 2.4 interop 编译已由 operator 跑过 PASS 0W0E。

新增/更新（只碰本 slice 文件）：

| 文件 | 作用 |
|---|---|
| `il2cpp/HeroArcherGuardFacing.cs` | 守位租约 + 朝外写入/归还责任（写失败/读失败绝不丢责任） |
| `il2cpp/HeroArcherLiveDiagnostics.cs` | 有界只读邻居诊断（预算按 arm 消费、世界上下文每批核对、无邻居立即结束） |
| `tests/hero-guard-facing/**` | 无游戏进程回归（含半途写失败/读失败/归还失败重试/停用重试/未知状态/无邻居/重复 setup 预算/换世界）+ interop 编译核对 + 接线核验 |
| `docs/project-harness/tasks/hero-live-fixes-20260915/guard-result.md` | 本文件 |

## 1. Operator 接线（`HeroArcherRuntime.cs`）

必需（行为本身）：

```csharp
// (A) Tick() try 块第一行（在 HeroArcherRange.RetryCleanup(); 之前）
HeroArcherGuardFacing.Tick();

// (B) 逐 actor 循环的 if (state.Hero) 块末尾（既有 if (!eligible){...continue;} 之后）
HeroArcherGuardFacing.Evaluate(archer);
```

必需（运行时撤销/池复用路径的**即时**归还，消除 2 帧 stale 朝向）：在 `RetireHero`、
`ResetIdentityTracking`、`Forget` 三处各加一行（`state.Ref` 即该 actor）：

```csharp
HeroArcherGuardFacing.Restore(state.Ref);        // 立即 CAS 归还；失败保留责任由 Tick 重试
```

必需（零残留）：`ClearInternal(...)` 末尾加

```csharp
HeroArcherGuardFacing.Clear();                   // pending-safe：归还失败保留重试，不丢责任
```

诊断触发（review 修订：不再由本模块自动发现 hero）：

```csharp
// PromoteHero：HeroArcherVisuals.Apply(archer) 成功之后
HeroArcherVisuals.Apply(archer);
if (HeroArcherVisuals.HasVisual(archer)) HeroArcherLiveDiagnostics.OnHeroSetup(archer);   // ← 新增

// RetireHero / ClearInternal（Remove / Disable）：释放诊断会话并重置预算
HeroArcherLiveDiagnostics.Clear();               // ← 新增
```

`Evaluate` 只在帧内真实在位英雄（≤2）上调用；`Restore/Clear` 是幂等的（无凭据即 no-op）。

## 2. 行为契约（review 修订后）

**守位租约（替代“宽墙深带即证据”）**

1. **学习只在 `latestGoto==8`（原生城墙态）+ `goalMode==Position`**：此时 `_goalPosition` 即原生下发的
   守位目标，记录 `GuardGoalX/GuardSide`。深度带 `[-8,48]` 只作学习期 sanity（防退化值），
   **不是**证据、也不用于到位判定。
2. **goto 白名单 = {8, 1}**：其它 goto（2/16/32/64/1024/2048/8192/16384/65536 及任何未知值）一律
   释放朝向并**作废租约**；回到 8 才重新学习。
3. **8→1 保留条件**：夜间 + 守位点未变（Position 目标仍等于 `GuardGoalX`，或已是 `Off` 静止态）。
   换目的地/Object 跟随/天亮/身份变化 → 释放并作废租约。
4. **持有条件**（在租约之上）：位置仍在 `GuardGoalX`（±0.1）、`!_movingToGoal.value`、
   `shoot` 协程未运行、`_pauseTimeout<=0`、无狩猎目标、非骑士随从/编队/登船/塔位/玩家控制，
   且 `HeroArcherRuntime.IsHero`（购买/开关/单机 gate/资格）。
5. 射击/移动/被推开等**挂起**不清租约：条件恢复即在同一守位点重新断言。

**写入责任（写失败/读失败绝不丢责任）**

* 接管时**先登记** `Owned/Written/MoverPointer`，**再**调 `SetFacingMode`：setter 抛错或读回抛错都
  保留责任，交由后续 Tick 的 CAS 复核（字段仍是我们写的值 → 归还；不是 → 释放）。
* 归还只在「字段现值 == 我们写入的值」且对象仍是同一 pointer/Mover（life 明确不同才判换主；
  life 未知 0 仍以 CAS 为准）时写回 `Ahead`；写失败/读回失败保留责任，**下一帧重试，停用状态也重试**。
* 身份确认变化（pointer 换主、life 明确不同、对象销毁、Mover 换过）→ **不写新所有者**，直接丢弃；
  读取异常（unknown interop access）→ 保留责任，绝不当作身份无效。
* 容量 ≤4：满时只淘汰“无责任”的最旧凭据；全是待恢复凭据时**跳过新登记**（绝不丢弃失败归还责任）。
* `Restore(archer)`（运行时撤销桥）与 `Clear()` 均 pending-safe：归还失败保留重试。
* 只写原生 `Mover.SetFacingMode(mode, null)`（只从 `Ahead` 接管，归还即 `Ahead`）；
  **绝不手写 `transform.localScale`**，不碰战斗/伤害/射程/箭矢/视觉/存档/配置；无 Harmony/协程/扫描。

**有界邻居诊断**

* 触发：operator 在 `PromoteHero` 成功挂载视觉后调 `OnHeroSetup`；`RetireHero`/停用调 `Clear()`。
  本模块**不自行发现 hero**（无逐帧世界/NPC 扫描）。
* 绑定：`Kingdom.Archers` 一次扫描（不 FindObjects、不重扫），同 world 最近的 ≤2 名其他存活弓箭手，
  身份 = goId + pointer + life。
* 采样：0.1s × ≤12 批，由既有 hero Tick 桥驱动；只读 root y/localScale.y、原生 body
  （enabled/sprite/bounds/position.y）与 `Camera.main` y。
* 中止：英雄/邻居身份变化、**当前 world 上下文变化**（world/gameLayer 指针或不再属于该层）→ 立即停止；
  世界/集合未就绪有界等待（≤5 次）；**扫描完成且无邻居 → 立即结束**。
* 预算：**每次 arm 消费一个名额**（同一 hero 生命 ptr/GO/life 去重），每个启用/世界上下文 ≤3 次会话，
  只有显式 `Clear()`（Stop/Remove/Disable/换世界）才重置；`Stop` 只结束当前会话，不动预算。
  异常/放弃原因只记一次日志，但**无论如何都会结束会话**（`Fail` 重复同 key 也必须停）。

## 3. 已核事实 vs 待核项

**已核（actual 2.4 interop 静态元数据 `BepInEx/interop/_interop_methods.txt` + 仓库生产先例）**：
`Mover::SetFacingMode`(06004D50)、`get/set_facingMode`(06004D8B/8C)、`get_goalMode`(06004D31)、
`get__goalPosition`(06004D79)、`get__movingToGoal`(06004D89)、`get__pauseTimeout`(06004D77)、
`Archer::get__huntingTarget`(06000595)、`get_shoot`(060005A7)、`get_behaviour`(060005A5)、
`get__guardSide`(0600059B)、`get__mover`(0600056B)、`get__embarkee`(06000601)、`get__damageable`(06000579)、
`ShouldPlayerControl`(06000474)、`GetFormation`(060004C0)、`Kingdom::get_Archers`(06004017)、
`GetBorderSideIntact`(06004006)、`get_campfirePosition`(0600414E)、`Director::get_IsNight`(06002651)、
`Haglet::get_started/latestGoto`(0600A346/A353)。枚举成员名（`Mover.FacingMode`/`GoalMode`）与
`Camera.main` 已由 operator 的 `Interop.csproj` 编译 PASS 确认。

**待实测（不得标 done）**：实机“英雄停在墙后时朝外、开火后回位、天亮/走动/撤离恢复原生朝向”；
真实日志出现 `[HeroGuardFacing/release:*]` 与 `[HeroGuardDiag] bind/b=/stop`（≤12 批）；
邻居高度结论需玩家在“附近角色跳动”现场取日志；联机边界不变（英雄整体离线 fail-closed，本 slice 无 RPC）。

## 4. Operator 命令（按序，只读/编译，不启动游戏）

```powershell
C:/Users/ADMIN/dotnet8/dotnet.exe build tests/hero-guard-facing/Interop.csproj `
  -p:BaseIntermediateOutputPath=obj/interop/ -p:OutputPath=bin/interop/     # 已 PASS 0W0E（复核用）
C:/Users/ADMIN/dotnet8/dotnet.exe run --project tests/hero-guard-facing/Tests.csproj
C:/Users/ADMIN/dotnet8/dotnet.exe build il2cpp/KingdomEnhancedMod.csproj -c Debug -p:BepInExPluginsPath=
pwsh -File tests/hero-guard-facing/Wiring.ps1                                # 接线核验（可选桥缺失只告警）
```
