# worker-result：火铳铺认领窗口双重锁修复（2026-09-18）

状态：**代码与测试修改已全部完成；三条验证命令因本 worker 会话执行工具全部被审批门禁拦截而未能运行**（证据见下）。未越 allowlist、未触碰任何禁区文件、未 commit/push、未碰游戏/E盘/存档。

## 1. 改动清单（全部在 allowlist 内）

### 生产代码（3 处，与任务书逐条对应）

| # | 文件 | 位置 | 改动 |
|---|------|------|------|
| 1 | `il2cpp/MusketeerShopRules.cs` | `Reconcile` :66-69 | `if (!item.Unclaimed) { complete = false; continue; }` → `if (!item.Unclaimed) continue;` + 注释（claimed 枪等待拾取、无需锚定、不置失败；已落架回执按槽位保留至退出快照） |
| 2 | `il2cpp/MusketeerShop.cs` | `RackCount` :795-803 | 条件 `!item.Unclaimed \|\| !RackLayout.IsPlaced(item)` → `item.Unclaimed && !RackLayout.IsPlaced(item)` + 两行注释；`ReadRackItems()` 失败仍整体返回满容量（fail-closed 不变） |
| 3 | `il2cpp/MusketeerShop.cs` | `TryCreateGun` :945 | `marked` 成功判定之后、`reason=""; created=true; return true;` 之前插入 `_nextRackLayoutAt = 0f;`（下一帧立即 Reconcile，消除新枪 ≤0.5s 瞬态锁；失败路径与 finally 清理不经过该行） |

### 测试

| 文件 | 改动 |
|------|------|
| `tests/musketeer-side-rack/Program.cs` | 翻转 2 条旧断言（见 §2）；新增 claim-window 计数/锁店用例 6 条 Check 调用点（见 §3） |
| `tests/musketeer-side-rack/check-shop-wiring.py` | 新增 8 条静态接线检查（见 §4），用于钉住生产谓词与不修边界 |

## 2. 新旧断言对照（Program.cs）

| 位置 | 旧断言（契约：claimed → 布局失败/锁店） | 新断言（契约：claimed → 跳过占位） |
|------|------|------|
| :28 | `!layout.Reconcile(true,full) && moves==3`"player/enemy/picked claim gate even with existing receipt" | `layout.Reconcile(true,full) && moves==3 && layout.IsPlaced(full[0])`"claimed gun keeps receipt, layout stays complete during claim window"（3 把全 placed 其一 claimed：complete 不再被破坏，回执保留） |
| :42 | `!layout.Reconcile(true,new[]{claimed}) && moves==0`"claimed old stock never moves" | `layout.Reconcile(true,new[]{claimed}) && moves==0`"claimed old stock skipped, never moved, layout stays complete"（单把 claimed 未 placed：跳过锚定、不置失败、仍零移动） |

后续行 :43-45（released claim retry / empty snapshot / receipt 不可跨槽携带）在新语义下逐条复核仍自洽，未改动。

## 3. 新增行为用例（Program.cs :57-76，brief 测试 1–3）

镜像 `MusketeerShop.RackCount` 的逐项谓词（`item.Unclaimed && !layout.IsPlaced(item)` → 满容量；否则计 `items.Count`），与生产谓词逐字对应（由 §4 接线检查钉住生产文本，防镜像漂移）：

1. 枪A claimed 未拾取：`RackCount==1`（占位计数，非锁店）；Reconcile 在从未锚定前也保持 complete。
2. 认领期间买第二把：gunB 锚定成功（moves+1）、`RackCount==2`。
3. 认领取消回 unclaimed 且无回执：`RackCount==RackCapacity`（fail-closed 保持）；Reconcile 锚定成功后恢复计数=2。
4. 3 把全 claimed：Reconcile 通过（moves 不变）、`RackCount==3` → 按容量锁店（新边界）。

## 4. 新增接线检查（check-shop-wiring.py :37-53，brief 测试 4–5）

1. `RackCount` 体包含新谓词原文（claimed 占位 / unanchored-unclaimed fail-closed）。
2. `RackCount` 体保留 `if (!ReadRackItems()) return ...`（身份不可读仍整体关闭）。
3. `ReadRackItems` 体保留 `item.Unclaimed = gun.friendlyClaimer == null && gun.enemyClaimer == null && !gun.pickedUp;`（any-claimant 判定原文不变）。
4. `TryCreateGun` 体含 `_nextRackLayoutAt = 0f` 且位于 `if (!marked)` 之后（brief 测试 4：成功才触发）。
5. `TryCreateGun` 体中 `_nextRackLayoutAt` 恰出现 1 次（失败路径不触发）。
6. `StockClaimProven` 体仍为 `return !CollectedOrClaimed(career.Tool);`。
7. `CollectedOrClaimed` 体仍含 `tool.pickedUp` 与 `tool.enemyClaimer != null`（brief 测试 5：enemy/picked 仍使 `ReadRackItems`→`RackCount` 整体 fail-closed 锁店）。

## 5. 验证状态（如实）

**未能执行**：本 worker 会话（win-omp-worker profile）中 `bash`、`eval`、`hub`、`task` 全部返回 "requires approval but no interactive UI available"（`xd://lsp` 亦无 language server）。未修改审批配置自我提权（属会话级管控，非本 worker 可自行变更）。

**已完成的替代验证（工具可查证）**：
- 三个生产文件最终态逐行复读：`MusketeerShopRules.cs#E4C7` :66-69、`MusketeerShop.cs#59F5` :795-803/:944-948、claim 旗标行 :853 未动。
- 全仓 grep `claim|Claimed|Unclaimed`：无其他生产代码依赖旧"claimed→锁店"契约；`MusketeerIdentity.Sweep()`/`MusketeerPersistence` 走 `CollectedOrClaimed`（enemy/picked），与 friendly 窗口无关，均不需改。
- 接线检查 8 条断言的目标子串已逐条 grep 落位确认（§4）。
- Program.cs 全部断言（含翻转 2 条、新增 6 条 Check 调用点及 MoveAndVerify 内坐标检查）按新代码逐条手工推演通过——**这是分析结论，不替代实际运行**，实际 PASS 计数以运行输出为准。

**待 operator 执行的命令（顺序运行，勿并行，共享 obj/）**：
```
cd C:/Users/ADMIN/projects/ohmymods/tests/musketeer-side-rack
C:/Users/ADMIN/dotnet8/dotnet.exe run
python check-shop-wiring.py
cd C:/Users/ADMIN/projects/ohmymods/il2cpp
C:/Users/ADMIN/dotnet8/dotnet.exe build -c Debug -p:BepInExPluginsPath=C:/Users/ADMIN/projects/ohmymods/docs/project-harness/tasks/musketeer-shop-claimlock-20260918/receipts/build-stage
```
预期：①`PASS <n> side-rack slot/layout/receipt assertions; live pickup remains untested`；②`PASS <n+8> production shop wiring checks...`；③0 警告 0 错误。输出请落 receipts/。

## 6. 已知边界（新契约）

- **friendlyClaimer 滞留降级**：平民认领后消失（死亡/被清理）且原生不清 `friendlyClaimer` 时，行为从"整店锁死"降级为"该枪占 1 槽直至有人接手/拾取"，属改善；3 槽内仍可继续购买。
- **容量边界**：3 把全在认领/架上状态时按容量锁店（物理在架上，语义自洽），待拾取后 `ReleaseStock` 释放槽位恢复。
- **enemyClaimer/pickedUp 不变**：仍经 `StockClaimProven`→`ReadRackItems` 整体 fail-closed 锁店（并可能 RevokeStock），本修复不触碰。
- **≤0.5s 瞬态**：购买成功后下一帧即 Reconcile；若该帧 `MoveAndVerify` 失败（原生物理未恢复等），仍按 fail-closed 锁店等待重试——这是既有保守语义，非本修复回归。
- 实机（真实平民认领窗口内连买、认领取消、敌人抢枪锁店）与联机仍待验；本任务不改变部署边界，未安装。
