# Worker result: tax assistant continuous coin batch

状态：实现完成，**未构建/未运行/未安装**（本次 worker 仅获 read/edit/write，无 shell；按任务书交 Operator 执行命令）。

## 改了什么

### 1. `il2cpp/PatchEconomy_BankAssistants.cs`（唯一生产文件）

新增常量与字段：
- `internal const int TRIP_TARGET = 20;`（静态类）＋协调器别名 `TRIP_TARGET`：每趟目标 20 枚。
- `internal const float WAIT_GAP_SECONDS = COIN_MATURITY_SECONDS + 2f * SCAN_INTERVAL;`（= 4.2s）＋别名。
- `AssistantState.WaitDeadline`（float，Time.time，>0 = 正在等下一枚成熟币）。

新增私有判定（全部零分配）：
- `GetTripCapacity() => Math.Min(TRIP_TARGET, GetAssistantCapacity())`：**容量 helper 原逻辑与下限 100 完全不动**，20 恒成立；实际容量仍作上限语义。
- `TripComplete(helper) => helper.CarriedCoins >= GetTripCapacity()`：完成判定唯一入口。
- `TryStartWaitForNextCoin(helper)`：活跃 + actor 有效 + 本趟已入账 >0 + 未满趟时建立一次 deadline 并返回是否仍在等待；`WaitDeadline` 已 >0 时**不再续期**；空手/满趟/actor 消失立即返回 false。
- `ClearWaitDeadline(helper)`。

接线（全部复用既有扫描与扫描生命周期，**无新 Update/hook/逐帧日志/场景扫描**）：
- `TryChainNextTarget`：顶部满趟 → 清 deadline+回家+退出；补链成功后清 deadline；补链失败 → `TryStartWaitForNextCoin` 为 true 时**保持 active 原地静止**（Moving=false、Speed=0），false 才清 deadline/回家/退出。返回值语义不变：Sweep 里 `if (!TryChainNextTarget(helper)) return;` 依原链停止本帧继续扫，active 不被误当 deactivate。
- `ScanAndDispatch`「满趟或演员消失」块改用 `TripComplete`，并在两条收工分支清 deadline；域解析失败分支回家前清 deadline。
- `SelectNextCollectors` 完成检查改用 `TripComplete`（与上两处一致）。
- `CleanupNoCandidates`（count<=0 路径）：活跃无目标者先走同一个 `TryStartWaitForNextCoin`，等待中直接 `continue`（**不从已清空的旧 MatureBuffer 重新 Assign**），到期才清 deadline+回家+退出；`RollbackOwnedClaimPolicies` 失败仍原样 `_cleanupPending=true; return;` 不绕过。
- 成功拾取（目标币与顺吸两处）后 `ClearWaitDeadline`，`TryChainNextTarget` 随即重新判定；20th 由 `TripComplete` 在 sweep 内即回家+释放 target+return false，不再吃第 21 枚、不再二次入账。
- 清理点全覆盖：`TeleportHomeAndDeposit`（首行）、`TryReserveForRestock`（租用前）、`ReleaseRestockAssistant`（finally）、`EnsureFourActors`（替换/新生成两处）、`ResetAll` 顶部（**在失 authority/Unknown 提前 return 之前**，与 RestockReserved 同处）。
- `Time.time` 语义、暂停门禁（Update 早退 + timeScale）、authority/scope 门禁、网络 RPC 注册、入账事务、补货租用门禁全部未改；等待只让 actor 静止，不开任何旁路。

### 2. `tests/greek-bank-assistants-scope/Program.cs`（+7 用例）与 `Harness.cs`

`Harness.ResetStatics` 增加 `WaitDeadline` 归零（防止用例间残留）。Program.cs 新增可观测助手 `ActiveCollector/ActivateCollector/WaitDeadline/GapIsFresh/ScanTick` 与用例：
1. `an empty snapshot keeps a carried collector waiting one bounded gap, then homes it` —— count<=0 短暂空快照不回家、deadline 恒为 4.2s、后续扫描不续期、到期回家清 carried 并释放，全程 0 入账/0 RPC/0 despawn。
2. `a coin maturing inside the gap resumes the same trip` —— 等待期间新币成熟（gap 内）→ 分配目标、清 deadline、保持 active、恰一次 claim/policy RPC、未到币不入账、actor 跑向币而非回家。
3. `the twentieth coin closes the trip without eating a twenty-first` —— Carried=19 + 目标币 + 0.2 外邻近币：sweep 命中 20th 即回家，恰 1 枚 despawn、恰 1 次入账（stash/共享键 101）、第 21 候选原地存活且认领/策略还原。
4. `an empty-handed collector is still released immediately` —— 空手不等待、不回家、不认领。
5. `a restock lease ends a pending gap instead of inheriting it` —— 等待中被补货租用：清 deadline、先回家清 carried、租约释放后再清一次；下一趟从新的断流重新计时。
6. `authority loss, disable and world exit drop a pending gap` —— 失 authority（在延迟清理前清 deadline）、关总开关（含 actor 回池）、跨 world 退出/返回（新 actor 接管后按当前时间重新计时，不继承旧 deadline）。
7. `a pending gap leaves native bank sync and other worlds untouched` —— 等待期间原银行家共享账本同步照常（107 落盘），其他 world 不改币、不改账本、不带走等待。

## Operator 需要执行的命令（worker 无 shell）

```bash
# 1) 测试套件（含 7 个新用例；期望全 PASS）
C:/Users/ADMIN/dotnet8/dotnet.exe run --project tests/greek-bank-assistants-scope/Regression.csproj -c Debug

# 2) 生产构建（BepInExPluginsPath 置空 = 不做开发环境复制，输出停在 il2cpp/bin/Debug/）
C:/Users/ADMIN/dotnet8/dotnet.exe build il2cpp/KingdomEnhancedMod.csproj -c Debug -p:BepInExPluginsPath=

# 3) DLL 审计（before.dll 基线与 receipts/dll-audit.json）
powershell -File docs/project-harness/tasks/tax-collector-batch-20260915/audit-candidate.ps1
```

审计兼容性预期：改动只在 `BankAssistantCoordinator`（内部方法增删改）与 `PatchEconomy_BankAssistants` 常量（const 不生成方法）内，符合脚本 allowlist 正则；Harmony patch 类型集与 4 张嵌入 PNG 不变。

## 未验证边界（未实机、未构建、未安装）

- 本次未编译、未跑测试、未装 E 盘、未启动游戏、未 commit/push/发布；结果以 Operator 的构建/测试输出为准。
- 实机待验：连续投币时每趟约 20 枚、断流 4.2s 后回家、恢复投币后同批继续；观感（静止等待是否被玩家看作“卡住”）需实机确认。
- 已知设计点：到期检测发生在下一次 0.6s 扫描（最多 +0.6s 超时）；到期回家后若已有成熟币，下一轮扫描会重新入选开始新一趟（Carried 归零），不会丢币、不会重复入账。
- 未改动：`ad57eb75` 英雄读档修复与全部 hero 改动、版本号/build 文字、harness/进度文档、存档与配置、游戏 DLL。
