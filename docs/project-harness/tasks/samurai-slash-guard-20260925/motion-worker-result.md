# 武士燕返硬失效修复 — worker 回执（2026-09-25）

角色：有界 worker（OMP deepseek/deepseek-v4-flash thinking=max）。cwd `C:/Users/ADMIN/projects/ohmymods-wt-choreo-fix`，
分支 `win/samurai-choreo-hard-validity`，基线 67d39b9。只实现 P0（燕返硬失效），未触碰另一 worker 的残影文件。

## 0. 结论

- 活动编舞（Choreo）的存续资格从完整 `Eligible` 换成硬失效子句：`Tick`、`ChoreoRoutine`、`ChoreoHitScan`
  共用同一个 `ChoreoInvalidClause`，6 处旧调用点全部替换（设计 review P2-1）。
- 触发门（`StartChoreo` 的调用点）与撤退阶梯（`ValidMotion`/`BeforeNativeUpdate`）仍用完整 `Eligible`。
- 设计 review 的最小修正全部落实：P2-1（六处）、P2-2A（cap 可达、软旗不再先 return）、P2-2B（软失效不再
  Remove 状态，避免双写者）、P2-2C（硬集补 owner 身份 + `gameObject==null`）。
- 软旗（retreating/charging/_shouldCharge/formation/FSM/手动控制等）不再切断两腿，也不再静默命中。
- 测试：`tests/samurai-motion` 214 passed / 0 failed（基线 198/0，净增 16 条）；红验证 16 红 → 恢复后全绿。
- 未编译主工程、未部署、未启动游戏、未 commit/push、未改 Main.cs/build.bat、未动玩家存档。

## 1. 改动路径与行号

### il2cpp/PatchRoles_SamuraiPowerDash.cs（md5 `9d7a33b0f53c367f837e77cacf7f6de8`，1483 行，+77/−22 → diff 110 行）

| 位置 | 内容 |
|---|---|
| 27–34（类注释） | 记录硬失效/软旗拆分与 2026-09-25 墙线截断根因 |
| 979–1016 | 新增 `ChoreoInvalidClause`（返回空串=存活，否则子句名）：`token / owner / gone / disabled / inactive / config / authority / style / mover / body / inert / grabbed / dead` |
| 1018–1019 | 新增 `ChoreoValid`（子句为空即真），三消费方共用 |
| 870–882（`Tick`） | choreo 分支提到任何软 `Eligible` return 之前；硬失效→`Finale(...,"hard-invalid:"+clause)`，否则 3 s cap→`"tick-cap"`；随后 `return` |
| 883–891（`Tick`） | 原 `Finale("ineligible")+Remove` 块只服务 Return/Walk 与实例 id 复用（活动 choreo 已在上方返回，不可能被软失效 Remove） |
| 1102–1161（`ChoreoRoutine`） | 三处 `!Eligible` → 子句判定（循环头 / 命中后 / turn 后），step 带子句：`out-interrupt:<clause>` 等 |
| 1210–1226（`ChoreoHitScan`） | 三处 `!Eligible` → `ChoreoValid`（入口 + 循环内两处）；软旗不再让命中静默失效 |
| 1238–1263（`Finale`） | 收尾日志独立 try 包裹（`choreo-close-log`）——日志失败不可能跳过效果归还（brief「异常日志不能自身掩盖收尾」） |
| 未改 | `Eligible` 本体（183–208）、`BeforeNativeUpdate`（216）、`ValidMotion`（280）、`StartChoreo`、`OnKnightDisabled`、效果快照归还、3 s cap、1.2 s 腿窗、`ShouldSlash` 压制、`HasActiveMotion`、无新 hook/配置/存档字段/全场扫描 |

### tests/samurai-motion/Program.cs（md5 `4eb4d95517e271e133849642532a1450`，1615 行，+109/−16）

| 行 | 内容 |
|---|---|
| 221–228 | 既有 queued-task 用例翻转：原生队列保留 + 行程存活 + 命中不再被静默（新契约） |
| 461–472 | 新增：触发门仍拒绝 `isRetreating`（日/夜两态，无 goal/无保护） |
| 748–759 | 新增：协程被第三方停掉 + 软旗置真时，3 s cap 仍可达并收尾（P2-2A 反例回归） |
| 787–860 | 中断矩阵重构：11 条软旗（含 retreating/charge pending/FSM/手动控制/harmless/stationary/embark/pillar）断言"走完两腿回家"；8 条硬失效断言"终幕+归还效果" |
| 865–876 | 新增：实例 id 复用（owner 丢失）硬收尾，替换者零继承 |
| 877–884 | 新增：硬失效收尾行的 `step=hard-invalid:dead` 子句 |
| 969–986 | 新增（夜间）：出腿中途 `isRetreating=true`，每帧 Tick 下两腿仍到出发点；turn 命中继续且每腿去重（1→2，不超） |
| 1529–1538 | Forward + manual control 命中帧不再被打断（Return 行原样，`ValidMotion` 未动） |
| 既有保留 | paused 行程保持、cap、OnDisable、外目标重申、mover 替换、外暂停迟滞、夜列队让位等原断言全绿 |

## 2. 测试命令与计数

Windows 命令（任务书指定运行时）：

```
C:/Users/ADMIN/dotnet8/dotnet.exe run -c Release      # 各自 tests/<suite>/ 目录下
```

| 套件 | 改动前 | 改动后 |
|---|---|---|
| tests/samurai-motion | 198 / 0 | **214 / 0**（+16） |
| tests/samurai-night-formation | 24 / 0 | 24 / 0 |
| tests/samurai-retreat | 43 / 0 | 43 / 0 |
| tests/samurai-diagnostics | 9 / 0 | 9 / 0 |
| tests/samurai-visuals（另一 worker 领地，仅基线核对） | 17 / 0 | 17 / 0 |
| tests/archer-night-band | 32 / 0 | 32 / 0 |

未编译主工程（按任务书由 Operator 统一编译；本文件不引入任何新 API，全部符号在改动前已被本文件使用）。

## 3. 根因红验证（旧 Eligible 恢复到活动路径）

1. 红改动（仅临时，一行）：`ChoreoInvalidClause` 主体在 `token/owner` 两行后接
   `if (!Eligible(k)) return "eligible"; return "";`。
2. 命令：`cd tests/samurai-motion && C:/Users/ADMIN/dotnet8/dotnet.exe run -c Release`
   → **RESULT: 198 passed, 16 failed**。16 红全部是新契约用例：
   - 11 条 `In-flight soft flag never cuts the trip: <flag>`（"the trip came home despite …"）
   - `The hard cap still closes a trip whose coroutine died under a soft flag`（软门先 return 导致 cap 不可达——P2-2A 反例实锤）
   - `In-flight new native queued task keeps its queue and the trip keeps its hits`（命中被静默）
   - `Forward damage callback interrupts old frame: manual control`（命中帧被打断）
   - `A retreat onset mid-outbound and mid-home never cuts the trip or its hits`（出腿即被截断）
   - `A hard invalidation names its clause in the bounded close line`（旧门只能记 `eligible`，记不出子句）
   - 硬失效矩阵/身份/子句以外的既有用例保持绿（契约保持项在红态也绿，说明红是契约翻转不是同义反复）。
3. 恢复新代码：文件哈希回到 `ccd5e4073253d572b749d3e4716cf255` → 再跑 **214 / 0**。
4. 随后按 brief 做收尾日志隔离（`Finale` 里独立 try）→ 哈希 `9d7a33b0f53c367f837e77cacf7f6de8` → 再跑 **214 / 0**。
5. 测试文件全程未为凑绿删改既有断言，仅在必须翻转的 6 条上按设计 review P3-2 改定契约（见下）。

## 4. 旧契约翻转清单（改动前实测 6 条 FAIL，均已按新契约重写）

| 旧断言 | 新断言 | 依据 |
|---|---|---|
| `In-flight interruption: charging/formation/manual control/other FSM task` → 关闭行程 | 软旗组：走完两腿回家、效果自然归还 | design-review P3-2 指定四行翻转 |
| `In-flight new native queued task stops old hits…` → 命中 0 | 队列保留 + 命中 1 + 行程存活 | P3-1「FSM 变动不打断、不让命中失效」 |
| `Forward damage callback … manual control` → 第二目标 0 | 第二目标 1（同一命中帧继续） | 同上 |
| （新增行）`retreating` | 软旗组 + 夜间专项用例 | brief 必测项 |

## 5. 已知限制（不修饰）

1. **有界续行**：软旗期间行程最多拖到 3 s 总帽（单腿 1.2 s）；真 embark/编队到达可能被延迟 ≤3 s（设计 review P3-1 已留档为有意行为）。
2. **命中恢复范围**：`_harmless`/`isStationary`/embark/手动控制中途翻真，命中在剩余窗内继续生效；触发门仍拒绝这些状态开新程。
3. **APRetreat 未反写**：行程中 `isRetreating` 翻真时原生可能播撤退姿势；按 handoff §5 只保留观测，不动 animSync。
4. `knight.gameObject == null` 时 `Tick` 早退（既有行为）；对象销毁靠 `OnDisable` 收尾。
5. `_fsm == null` 有意不入硬集（协程不需要 FSM）；`_fsm._executeQueuedState`/FSM 状态一律软。
6. style 解析瞬时失败（`NeedsRederive` 等）会硬收尾——与旧契约一致，只是活动期现在可达。
7. 暂停（`timeScale=0`）不关行程；cap 用受 timeScale 缩放的 `Time.time`，暂停可超 3 s 墙钟（既有语义，原测试保留）。
8. 子句日志共用既有预算（12 行/会话 + 6 s/骑士）。
9. **实机未验**：未编译主工程、未部署 E 盘、未启动游戏。两腿完整一气呵成、墙外命中、CD 3 s、残影观感、换岛/联机均待 Operator 装机实测。

## 6. 边界与事实说明

- 本轮开始时沙箱拒绝 bash（无交互审批），由 Operator 以任务限定授权恢复；所有命令均为本机本地执行，未使用 MCP/Coordinate、未提交任何 job、未触碰远端状态。
- 未修改 `SamuraiDashVisuals.cs` 及其测试（另一 worker 负责）、未改 `Main.cs`/`build.bat`、未 commit/push/merge/reset/clean/deploy、未运行游戏、未读写玩家存档。
- `git status` 仅本任务两个文件为 modified；`docs/project-harness/tasks/samurai-slash-guard-20260925/` 为未跟踪任务目录（含本回执）。

Operator补记：worker交付后新增命中回调翻retreat与仅回程翻retreat两例；最终216/0见operator-motion.log，GLM5.3/max独立motion-review.md APPROVE。
