# worker-brief：修复火铳铺"平民拾取完之前不能买第二把"（2026-09-18 上午）

角色：worker（可写，范围严格受限）
cwd：`C:/Users/ADMIN/projects/ohmymods`
诊断与修法已经 operator 对抗审查确认（回执 receipts/decision-review-record.md），按本任务书执行，**不要扩大修改面**。

## Bug 与根因（已证实，直接采用）

用户报告：买火枪手时，买一把之后**有时候**平民拾取完之前不能买第二把。

根因（认领窗口双重锁）：平民认领枪（`Peasant.SetDroppableTarget` 直接赋值 `friendlyClaimer`，原生无超时）发生在走到枪旁拾取**之前**，窗口=走路全程（可达数秒）。此时：
- ① `MusketeerShop.RackCount()`（il2cpp/MusketeerShop.cs:795-801）遇任何 `!item.Unclaimed` 直接返回满容量 3；
- ② `MusketeerRackLayout.Reconcile`（il2cpp/MusketeerShopRules.cs:66）遇 claimed 项置 `complete=false` → `_rackLayoutReady=false`（MusketeerShop.cs:885）→ `CanPurchase()`（:205-214）为 false。
两条链任一触发都锁店；无人认领时枪 unclaimed+placed、计数<3 可买——这就是"有时候"。次要窗口：新枪买完后需等 ≤0.5s 的 MaintainRackLayout 才 IsPlaced，期间 RackCount 也返回满。

## 修复（三处，精确到行为）

1. `il2cpp/MusketeerShopRules.cs` Reconcile :66：claimed 项改为**跳过且不置 `complete=false`**（`if (!item.Unclaimed) continue;`）。被认领的枪无需再锚定；已落架的 `_placed` 回执按 stamp 天然保留（:75-76 只在槽位退出 RackItems 时移除）。
2. `il2cpp/MusketeerShop.cs` RackCount :799：条件改为 `if (item.Unclaimed && !RackLayout.IsPlaced(item)) return MusketeerShopRules.RackCapacity;`——只有"**未被认领**却未落架"才是真异常（保持 fail-closed）；被认领的枪按正常占位**计入** `RackItems.Count`（物理在架上直到拾取，容量语义=架上最多 3 把含认领中）。
3. `il2cpp/MusketeerShop.cs` TryCreateGun：`marked` 成功后置 `_nextRackLayoutAt = 0f;`（下一帧立即 Reconcile，消除新枪 ≤0.5s 瞬态锁；失败路径不触发）。

## 严禁顺手修改的边界（对抗审查划定的不修面）

- `MusketeerIdentity.CollectedOrClaimed` 对 **enemyClaimer/pickedUp** 的判定与 `ReadRackItems` 整体 fail-closed（:835）**保持不变**——敌人认领/已拾取仍锁店。
- `RackContextReady` 的 `StockRestores.Count != 0` 门、`MusketeerIdentity.CanPurchase` 其余门**不动**。
- AnchorRackGun 拒绝锚定被认领枪（:867-868）**不动**；TryCreateGun 的 `RackCount()>=3`+`FirstFreeSlot` 双检**不动**；退款路径**不动**。

## 测试（tests/musketeer-side-rack/，含改旧断言）

**先改两条会被本修复翻转的旧断言为新契约**（tests/musketeer-side-rack/Program.cs）：
- :28 附近 `"player/enemy/picked claim gate even with existing receipt"`（3 把全 placed 其一 claimed）：修复后 Reconcile 应返回 **true**——改为断言 claimed 不再破坏 complete、回执保留。
- :42 附近 `"claimed old stock never moves"`（单把 claimed 未 placed）：修复后返回 **true** 且 moves==0 仍保持——改为断言 claimed 跳过锚定、不置失败。
再**新增**用例：
1. 枪A claimed 未拾取：RackCount 按占位计数（=1）、CanPurchase 仍可买、可买第二把。
2. 认领取消回 unclaimed 且未 placed：RackCount 返回满（fail-closed），Reconcile 锚定成功后恢复。
3. 3 把全被认领：RackCount=3 → 锁店（新边界，物理合理）。
4. TryCreateGun 成功后 `_nextRackLayoutAt==0f`（瞬态锁消除）；失败路径不置 0。
5. enemyClaimer/pickedUp 路径仍整体 fail-closed（不修边界的回归保护）。

运行方式沿用该工程既有做法（先读其 Tests.csproj/README）；你无构建部署授权之外的游戏环境权限，测试在本工程目录跑，回执落 receipts/。

## 构建验证（不部署）

`il2cpp/`：`C:/Users/ADMIN/dotnet8/dotnet.exe build -c Debug -p:BepInExPluginsPath=C:/Users/ADMIN/projects/ohmymods/docs/project-harness/tasks/musketeer-shop-claimlock-20260918/receipts/build-stage` → 0警告0错误，输出回执。

## allowlist

- `il2cpp/MusketeerShop.cs`、`il2cpp/MusketeerShopRules.cs`（仅上述三处）
- `tests/musketeer-side-rack/**`
- `docs/project-harness/tasks/musketeer-shop-claimlock-20260918/**`

## 禁止

改其他任何文件（尤其不得触碰 HeroArcherWallPierce.cs 等未发布候选脏文件）、commit/push、碰游戏/E盘/存档、启动游戏。

## 交付

worker-result.md：改动清单、新旧断言对照、测试输出、已知边界（含：平民消失后 friendlyClaimer 滞留场景从"整店锁死"降级为"该枪占1槽直至接手/拾取"，属改善）。
