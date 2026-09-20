# Hero guard facing + bounded neighbor diagnostics

本组只覆盖两个新文件（编译时直接把生产源码编进来，跑真实代码路径）：

* `il2cpp/HeroArcherGuardFacing.cs` —— 守位租约 + 朝外写入/归还责任（只写原生 `Mover` 朝向模式）；
* `il2cpp/HeroArcherLiveDiagnostics.cs` —— 有界只读邻居高度诊断（operator 在 PromoteHero 后触发）。

## 运行

```powershell
# 1) 无游戏进程回归（不需要游戏）
C:/Users/ADMIN/dotnet8/dotnet.exe run --project tests/hero-guard-facing/Tests.csproj

# 2) actual 2.4 interop 编译核对（枚举成员/Camera.main 等成员存在性的编译期证据）
C:/Users/ADMIN/dotnet8/dotnet.exe build tests/hero-guard-facing/Interop.csproj `
  -p:BaseIntermediateOutputPath=obj/interop/ -p:OutputPath=bin/interop/

# 3) 接线核验（可选；先构建 il2cpp/bin/Debug/KingdomEnhancedMod.dll）
pwsh -File tests/hero-guard-facing/Wiring.ps1
```

`Tests.csproj` 用 `Stubs.cs` 复刻原生边界：`Mover.FacingMode{Ahead=0,Left=-1,Right=1,Target=2}`、
`goalMode`、`_goalPosition`、`_movingToGoal.value`、`_pauseTimeout`、可注入的
`ThrowOnSetFacing/ThrowAfterSetFacing/ThrowOnReadFacing/OnSetFacing`（半途写失败/读失败/归还失败）、
`Director.IsNight`、`Kingdom.Archers` 为 `HashSet<Archer>`（须 Cast 后枚举）、
`behaviour/shoot` 为 `IHaglet`（Cast 读 `started/latestGoto`）。

## 覆盖（review 修订后）

* **守位租约**：`goto==8 + Position 目标` 才学习；8→1 且守位点未变时保留；换目的地/Object 跟随/
  非白名单 goto/天亮/身份变化 → 释放并作废租约；状态 1 无 8 证据绝不接管；退化目标不学习。
* **静止/任务挂起**：离位、`_movingToGoal`、`_pauseTimeout`、shoot 协程、骑士/编队/登船/塔位/玩家控制、
  非英雄；回归守位点/暂停结束/射击结束 → 同点重新断言。
* **外来朝向**：`Target`、第三方固定侧绝不覆盖；原生重置回 `Ahead` 时重新断言。
* **失败路径**（review 要求的一半用例）：setter 未写入抛错（责任保留、下帧重试）、
  setter 写入后抛错（半途失败仍保责任）、读 `facingMode` 抛错（不写不持有）、
  归还失败（保留 pending，下一帧 Tick 重试成功）、**停用状态也重试**、
  stale 释放失败保留重试、池复用（life 变化）不写新所有者、`Restore(archer)` 桥即时归还且幂等、
  桥失败保留 pending、世界缺失 fail-closed。
* **诊断**：一次绑定（≤2 最近同 world 邻居、跳过死者与英雄本人）、0.1s 节拍、≤12 批自停、
  无邻居立即结束、同 hero 生命不重复 arm、新生命按 3 次预算上限拒绝第 4 次、
  `Clear()` 才重置预算、邻居/英雄身份变化与**换世界**立即中止、全程零 `FindObjects`、零原生写入。

`InteropStubs.cs` 只补 `HeroArcherRuntime`/`KingdomEnhancedPlugin` 两个 MOD 侧类型；其余成员全部来自
E 盘 2.4 interop 程序集（已由 operator 编译 PASS 0W0E）。编译是静态核对，不等于 Unity 运行期行为测试；
实机验收见 `docs/project-harness/tasks/hero-live-fixes-20260915/guard-result.md`。
