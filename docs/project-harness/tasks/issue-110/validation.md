# Issue 110 验证记录

基线 `release/v9.5.13 @ 14e6f64`（worktree `ohmymods-goblin-coin-leisure-20261003`）。
本文件只记录 worker 侧证据；合并/安装/发布由 Operator 走 Issue→PR→merge→closing 流程。

## 1. 变更面（严格清单）

生产（仅 2 个文件）：

| 文件 | 性质 |
|---|---|
| `il2cpp/CoinCourierLeisureCoin.cs` | 新增：纯相位采样 + 每 view 惰性金币子对象 + 共享 4px 程序金币 |
| `il2cpp/CoinCourierVisuals.cs` | 接线：view 句柄 `LeisureCoin`、Render 金币采样（含同帧去重命中分支）、Destroy/Shutdown 资源归还 |

测试：`tests/coin-courier-visuals/{Regression.csproj,Stubs.cs,Program.cs}`（新 4 用例 + 预览导出 +
stub 补 `Texture2D.SetPixels32/Apply`）、`tests/coin-courier-visual-lifecycle/{csproj,Program.cs}`
（新 3 用例，既有 11 条断言未改）。

工具/产物：`tools/preview_coin_courier_leisure.py`（新）、
`artifacts/coin-courier-leisure-20261003/{samples.json,preview.gif,contact-sheet.png,preview-manifest.json}`（新）。
文档：本目录 plan/validation。

**未改**（哈希冻结见 §5）：`CoinCourierRuntime*.cs`、`CoinCourierCoinFlight.cs`、`CoinCourierShop.cs`、
`CoinCourierEconomy/Persistence/Purse/SaveData/Targeting/BankScope/CampaignState.cs`、`CoinCourierTeleportFx.cs`、
`KingdomEnhancedMod.csproj`、`tools/prepare_coin_courier_b_atlas.py` 与 `artifacts/coin-courier-blueprint-b-20260927/**`、
`il2cpp/Assets/*.png`（20 张，含 `CoinCourierBAtlas.png`）、版本号/配置项/Harmony hook/Mono 线。

## 2. 托管回归（本机 .NET 8 / `dotnet run -c Release`，日志在私有任务 `evidence/suites/`）

| 套件 | 结果 |
|---|---|
| `tests/coin-courier-visuals/Regression.csproj` | **31 passed, 0 failed**（基线 23/0，本 issue 新增 8 条） |
| `tests/coin-courier-visual-lifecycle/CoinCourierVisualLifecycleTests.csproj` | **15 passed, 0 failed**（基线 11/0，本 issue 新增 4 条） |
| `tests/coin-courier-runtime-bridge/CoinCourierRuntimeBridgeTests.csproj` | **ALL PASS — 223 checks**（未改，回归） |

新用例覆盖（意义断言，非复制常量）：

- `leisure coin samples follow hand, toss, catch and bag`：8 姿态门控；循环=帧表推导 2.4s；起点=帧 8
  手锚、t=1.5s 精确落在帧 10 手锚（掌心）；抛起段 y 单调到顶点且 `peak ∈ (头顶, 小幅上界)`、水平朝抬手方向；
  收袋段终点=袋口且末段隐藏；1/60s 相邻步进连续（≤2px）；坐标全在 1/32 网格；自转宽度只取 4 档且至少 3 档出现；
  同相位幂等、`p+7×2.4s` 周期一致；NaN/±Inf/负相位确定性回退起点。
- `leisure coin attaches only to a visible leisure view`：Run/隐藏/Idle 都不惰性建子对象；可见 Leisure 才建、
  挂在 view 根下、排序=身体+1、材质复用身体；**同一身体帧（都在 0..0.6s 帧 8 窗口）内相位推进时身体切图不重写、
  金币仍按采样转动**；抛起相位金币离开掌心上升；离开 Leisure / hidden 立即关；身体取帧失败（确证坏图集）时金币让位。
- `leisure coin mirrors with the view and keeps its own scale`：镜像只由父级 ±scale 承担，脚点不动、
  金币本地位置不变、世界偏移 x 取反 y 保持、子对象不重建。金币资源用例另证 4×4 像素（1 高光 + 8 外圈 + 3 亮心、
  四角透明）、纹理/切图只建一次、view 销毁不释放共享资源、`ShutdownModule` 释放、第二个 view 复用同一份共享切图、
  原生回收后不逐帧热建。
- lifecycle 新增：运行时满袋驻留真实进入 Leisure 并挂上金币（一个 view 一个子对象、跨循环无 churn）；
  金币子对象被原生回收后一次性关门（10 次 Render 不新建、告警恰 1 次、身体照常）；图集同帧卸载重建时金币照旧，
  确证坏图集时金币让位且不残留/不重复。

## 3. 真实引用（ARM / BepInEx 6 IL2CPP interop）构建

`evidence/actual-arm/`（私有任务目录，Operator 后续组合用）：

- 由 crossbow 任务模板复制 `Integration.csproj` + `PluginInfo.cs`，`Compile/EmbeddedResource` 根改本 worktree，
  **161 个真实引用**与 `Version 10.8.35`/PluginInfo 保持，`BepInExPluginsPath` 为空（不部署）。
- `dotnet build Integration.csproj -c Release -t:Rebuild -p:BepInExPluginsPath=`：
  **Build succeeded. 0 Warning(s), 0 Error(s)**（`build.log`）。
- 产物 `bin/Release/net6.0/KingdomEnhancedMod.dll`：
  `sha256 49bec19bfaeed49a00f0dcd7b2b376192d11ba3f8f0a1a284649af1c58116b74`（`final-dll.sha256`）；
  第二次强制 Rebuild 复现同一 SHA（`Deterministic=true`）。
- 内容核验：DLL 内含 `CoinCourierLeisureCoin`（类型/成员 `TrySample`/`WidthScale`）与
  `KEM_CoinCourierLeisureCoin`、以及新告警字面量（UTF-16 串命中），证明新代码确实参与编译。

## 4. 预览真实性（非游戏录像）

- `artifacts/coin-courier-leisure-20261003/samples.json`：由真实 C# 控制台
  `--preview-samples` 导出（`CoinCourierLeisureCoin.TrySample` + `CoinCourierPoseTable.FrameIndex`），
  idle 2s + leisure 2.4s @60Hz 共 **264** 采样；leisure 144 采样里金币可见 142、末段 2 个隐藏（进袋）。
- `tools/preview_coin_courier_leisure.py` 只按 samples + 现 atlas 回放：`preview.gif`（2 个完整循环、
  20fps、3x 最近邻，176 帧）、`contact-sheet.png`（关键时刻 1x/6x、浅/深底、镜像行，3294×1220）、
  `preview-manifest.json`（输入/输出 SHA-256 与参数，含 “not in-game footage” 声明）。
- 无 imagegen、无位图编辑；atlas 未被改动（`1924cc0c…` 与已批准蓝图副本逐字节一致）。
- 程序化像素核验：t=2.60 币在帧 8 手锚、t=3.05 顶点落在头顶上方 2-3px 且币面朝前、t=3.62 精确落在
  帧 10 抬手掌心、t=4.35 到达袋口、t=4.40 隐藏；世界脚点锚 (32,52) 不动。

## 5. 冻结哈希（真实源/测试/文档/工具/预览；不含 bin/obj）

见私有任务 `evidence/change-hashes.json`（逐文件 SHA-256）与下文 PNG 快照。关键条目：

- `il2cpp/CoinCourierLeisureCoin.cs`、`il2cpp/CoinCourierVisuals.cs`（哈希见 freeze；含审查后
  资源保护修订：存活绑定失效→共享有界重建+重绑、重建失败隐藏且缓存关门、异常路径先关旧金币、Hide
  不因 Failed 早退、退路只停用自有 child）、
  `tests/coin-courier-visuals/{Regression.csproj,Stubs.cs,Program.cs}`、
  `tests/coin-courier-visual-lifecycle/{CoinCourierVisualLifecycleTests.csproj,Program.cs}`、
  `tools/preview_coin_courier_leisure.py`、`docs/project-harness/tasks/issue-110/{plan,validation}.md`、
  `artifacts/coin-courier-leisure-20261003/*`。
- `il2cpp/Assets/*.png` 20 张未变；其中 `CoinCourierBAtlas.png = 1924cc0c801f41cd…b303d7`（与
  `artifacts/coin-courier-blueprint-b-20260927/animation-draft/atlas/CoinCourierBAtlas.png` 相同）。

## 6. 尚待玩家实机（与代码交付状态分别记录）

- 玩家实机观感：把玩/抛接节奏与手部对齐、金币大小（3~4 local px）在真实镜头缩放下的辨识度、镜像/朝向、
  暂停冻结、隐藏/传送/配送切换、连续多圈不漂移。
- 店铺预览牌 Leisure 片段现在也会显示这枚金币（同一 Render 接线，属预期），实机确认是否接受。
- 跨岛/读档后新 view 的重新惰性创建、原生回收边界（view 存活但金币子对象单独被回收→本 view 关闭装饰）
  在真实运行中的表现。
- 与既有累计 B 组合后的整机构建/安装由 Operator 执行；本 worker 未安装、未启动游戏、未写玩家数据。

## 7. 独立初审 P2 修订（Astra DRAFT-REVIEW 后）

原两处 P2 已修，生产仍只有 `CoinCourierVisuals.cs` + `CoinCourierLeisureCoin.cs` 两个文件：

1. **存活 view 的切图/纹理失效恢复**：`Render` 在可见帧检查自有绑定的 `SpriteUsable`；失效时走共享
   `EnsureSprite` 有界重建并重绑（多 view 只重建一次，各自下一帧绑同一份 fresh），重建失败先关本 view
   并保持模块 failed 直到 `ShutdownModule`——没有逐帧热点，也不留 `enabled` 残影。
2. **异常路径先关旧金币**：`CoinCourierVisuals.Render` 的 catch 先 `Hide` 再 `WarnOnce`；helper 自身
   catch 先 `HideChild` 再标 `Failed`+告警；`HideChild` 先写自有 renderer，写失败时退路 `SetActive(false)`
   自有 child 根（不碰 actor/body root/其它 view）；`Hide` 不再因 `Failed` 早退，仍活 renderer 一律关。

新增/加强断言（不删旧断言）：绑定置空同帧重绑且不重建不重写身体切图、共享缓存被回收后一次重建+第二
view 绑同一 fresh、texture 为 null 的坏绑定重绑、两个 view 各自失效路径；body renderer 为 null /
Transform 缺失在 Visible+Run 与 hidden 两分支都必须关掉旧金币；子对象部分回收（renderer 丢失、根还活）
先停用自有根再一次 Failed 且 5 帧不重建、告警恰 1 次；`Failed` 但 renderer 仍活时 `Hide` 仍关；共享重建
注入失败→本 view 隐藏+模块 `_state=2`+6 帧无重试+Shutdown 后新 view 正常；lifecycle 运行时真实
fake-null：丢纹理后下一帧有界重建（精确 1 次）且金币继续播、无子对象 churn。

几何/采样未改：`samples.json`（`ecd2439f…`）、`preview.gif`（`68ce453d…`）、`contact-sheet.png`（`44c9102a…`）
与首候选逐字节一致，未重画预览。人物每帧脸型差异属原 atlas 母版问题（用户已反馈），本轮**不**处理，
由 Operator 另立 scope；本切片不因此视为整个用户需求完成。

## 8. 用户确认 V1 范围（2026-10-04）

先交付金币动作，沿用原图集；逐帧脸部统一留作实机反馈后的 V2。本文件前述资源验证仍仅覆盖该金币显示代码，不将其转述为游戏内观感已验。上述旧基线/构建哈希代表当时的候选；最终公线兼容、组合构建和安装另记录。
