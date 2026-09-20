# 英雄驿站地面基准修复 —— grounding worker 结果

轮次：2026-09-15 北京时间晚间（OMP deepseek-v4-flash thinking=max），有界 slice。
允许范围：`il2cpp/HeroShop.cs`、`tests/hero-shop/**`、本文件。
状态：**实现完成 + 静态审查完成；本机回归/编译未执行**（该 session 的 bash 被 approval 门禁拒绝且
无交互批准 UI，见"验证"）。未 commit、未部署、未启动/关闭游戏、未动用户存档与配置。
owner 桥（f8c25095 已实测 preflight/ready/purchasecompleted）未触碰。

## 诊断（已确认，证据链完整）

- 现状代码：`Create` 里 `_object.transform.position = new Vector3(position, layer.position.y, layer.position.z)`，
  `layer = managers.world.gameLayer`，其 y 通常 0。
- 原生 `ShopPlanner.CreateShop`（`game-source/Assembly-CSharp-2.1.0/ShopPlanner.cs:510-512`）：
  `vector = (position, shopPrefab.transform.position.y, shopPrefab.transform.position.z)`，随后
  `Instantiate(shopPrefab, vector, identity, level.GetLayer(ContentLayers.GameLayer).transform)`。
  即：原生地面商店**直接挂在当前 world 的 GameLayer 下，根 y = 预制体根 y**。
- 实际 2.4 资源证据（`receipts/grounding/resource-grounding.json`，UnityPy 提取
  `resources.assets` + `sharedassets0.assets`；operator 摘要 `grounding/operator-evidence.md`）：
  - 所有 shop 预制体根 `y = 0.875`（z=2.0）；**body SpriteRenderer 就在根节点上**（localPosition=根、`pivot.y=0`、`PPU=32`）→ 根 y 即地面线，无子件偏移；因此 `BodyGroundOffset = 0` 是资源事实而非猜测。
  - Merchant 等子件有各自 offset（0.312 / 0.143 / 0.06 …），**不得**作为地面基准。
- 后果量化：我方整店低 0.875 世界单位 = 0.875×32 = 28 源像素；我方帧高 80px → 底部约 35% 被地面覆盖。
  与 `receipts/grounding/before.png` 的目视结论一致（视觉复核：底部约 20–35% 被地形线切掉，
  原生建筑基部均在地面线之上；见本文件"验证"节）。
- 我方素材 512×80、pivot(0.5,0.025)=底部 2px，保持不变（证据明确要求），故根 y 直接拷贝采样值，
  不额外抬高。

## 实现

`il2cpp/HeroShop.cs`：

1. 新增无 Unity 依赖的 `HeroShopGrounding`（Core 可测，位于 `HeroShopPlacement` 之后）：
   - `BodyGroundOffset = 0f`：2.4 资源中 body renderer 与根重合（上面已述）。
   - `TryResolve(bool sameWorld, bool active, float nativeRootY, out float groundY)`：
     仅当 同 world ∧ active ∧ `float.IsFinite(nativeRootY)` 时接受原生根 y（+0），否则返回 false
     且 `groundY` 保持 0（调用方据此延后创建，绝不回退 GameLayer y）。
2. `Create` 在 x 选址之后新增 `ground-reference` 阶段：
   `TryNativeGroundAnchor(layer, out rootY, out groundY)` 扫描 `payables.AllPayables`（上限 4096，
   `_object` 创建前、`_kingdom/_layer` 赋值前），取第一个 **active ∧ `PayableShop` ∧ `t.IsChildOf(layer)`**
   （当前 world 判据，沿用 AutoRestockCounts:691 / SiegeAmmoCounts:547 既有实机惯例；原版
   `CreateShop`/`RecvPlaceShop` 也是把商店挂在当前 GameLayer 下）的实例，用其**根 world y**
   （不取任何 renderer 子件 Y）。找不到 → `_status = "等待原生商店地面参照"` 并 return；
   Tick 的既有 `_retryAt`（+5s）自然重试。
3. 位置行：`new Vector3(position, groundY, layer.position.z)`。
   **未动**：x 选址（`HeroShopPlacement.Find`/`OverlapsAnyExclusions`）、z/sorting layer/order/material
   复制（仍是"第一个 active 原生 PayableShop 的 `GetComponentInChildren<SpriteRenderer>()`"，按委派要求保持不变）、
   owner 接口桥、`IsLocked`/`WriteUnlocked`、支付/退款/取消、banner 逻辑。banner 与金币槽是 root 子件，
   根移动后整体跟随（证据要求"all follow together"）。
4. 初始化日志一次（`ready` 之前）：
   `[HeroShop] ground source=native-root rootY=<F4> bodyOffset=<F4> groundY=<F4> finalY=<F4>`
   期望实机值 `rootY=0.8750 bodyOffset=0.0000 groundY=0.8750 finalY=0.8750`；旧错误值为 0。

`tests/hero-shop/Program.cs`：新增 8 项纯基准选择断言（同 world 活动根接受 +0.875/-1.25/0；
跨 world 拒绝且输出保持 0；inactive 拒绝；NaN/±Inf/无参照拒绝）。总计 39 项。
`tests/hero-shop/README.md`：同步断言数与 grounding 关口说明。

改动锚点（供逐行审阅）：`il2cpp/HeroShop.cs` L65-86（`HeroShopGrounding`）、L230-236（ground-reference 阶段）、
L241（位置）、L304-306（ground 日志）、L310-331（`TryNativeGroundAnchor`）；`tests/hero-shop/Program.cs` L30-37。

## 验证

- 静态（本 session 实际完成）：
  - 编辑后 `HeroShop.cs` 重新解析，声明结构完整（`HeroShopGrounding` 正常出现），新增代码仅使用本文件/本仓库
    既有 interop 用法（`t.IsChildOf(layer)` 见 `AutoRestockCounts.cs:449,691`、`SiegeAmmoCounts.cs:547`；
    `t.position.y`、`shop.isActiveAndEnabled`、`TryCast<PayableShop>`、`AllPayables` 上限扫描均为本文件既有模式）。
  - 数据集交叉核对：根 y=0.875 在多 biome（norselands/deadlands/greece）样本一致；body renderer `go` == 根节点名。
  - 症状目视复核：`before.png` 经视觉复核确认"mod 商店基部低于原生建筑基部、底部约 20–35% 被切"。
- **未执行（环境阻塞）**：本 session `bash` 被 approval 门禁拒绝（含 `pty:true`，均提示
  "requires approval but no interactive UI available"），因此 Core/Invoker/Interop 回归与 IL2CPP 编译
  无法在本机运行。请 operator 执行（预期输出已给出）：
  1. `C:/Users/ADMIN/dotnet8/dotnet.exe run --project tests/hero-shop/Core.csproj`
     → `HeroShop core: 39 assertions passed.`
  2. `C:/Users/ADMIN/dotnet8/dotnet.exe run --project tests/hero-shop/Invoker.csproj`
     → `PASS 7 invoker/JIT/production pointer adapter assertions; no game/native library loaded.`
  3. `C:/Users/ADMIN/dotnet8/dotnet.exe build tests/hero-shop/Interop.csproj` → 0W0E（对照真实 2.4 interop）
  4. `cd il2cpp && C:/Users/ADMIN/dotnet8/dotnet.exe build -c Debug -p:BepInExPluginsPath=` → 0W0E，
     **不部署**。
- 实机（待 operator，游戏关闭后再做）：安装候选 → 看日志 `ground ... rootY≈0.8750`，且原有
  `owner preflight passed` / `ready x=` / `purchase completed` 保持；目视商店底部与原生商店地面线齐平，
  旗帜/金币槽整体随根上移；跨岛/读档后每次都按新 world 采样。

## 残留风险与边界

- world 判据 `t.IsChildOf(layer)` 依赖 `world.gameLayer` 与原生商店所在层同根：该关系已被在售功能
  （AutoRestockCounts 商店注册、SiegeAmmoCounts 弹药目标）实机验证；若某 world 不成立，商店会保持
  "等待原生商店地面参照"而不出现（日志缺 `ground`/`ready` 行），是可诊断的延后而非错误高度。
  本 session 无游戏执行权，未在 native 层复验；实机一条 `ground` 行即可判定。
- 理论上若某商店预制体内部还有嵌套 `PayableShop`，`IsChildOf` 可能采到它（其根 y 非 0.875）——
  数据集未显示该形态，实机日志 `rootY` 值可直接证伪。
- 无合格原生商店的 world：商店延后出现（有意行为，operator 证据明确"defer creation rather than y0 fallback"）。
- z/sorting 复制仍取"第一个子 renderer"（委派要求本轮不变）；若实机出现 z 错配，属另一 slice。
- 未验证：真实 Unity 渲染目视、两本地玩家、跨岛/读档、联机（保持关闭）。
- 文档同步：`harness-checklist.json` / `progress.md` / `patch-patterns` 等 harness 状态由 operator 统一更新，
  本 worker 未改（超出允许范围）。
