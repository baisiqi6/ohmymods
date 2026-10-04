# Issue 110：金币哥布林休闲动作把玩/抛接金币

玩家希望金币哥布林的休闲循环更贴合人物：抬手时把玩一枚金币、抛起再接回掌心、最后收回袋边。
本切片只加**纯显示**的装饰金币，不参与经济/拾取/任务；角色图集、身高（+5%）、脚点、Idle/Leisure
时钟、配送/购买/持久化全部保持。初始基线 `release/v9.5.13 @ 14e6f64`；V1 最终交付将按当前公线重新验证。

## 变更面

- 生产：**新增** `il2cpp/CoinCourierLeisureCoin.cs`（纯相位采样 + 每 view 一个惰性金币子对象 +
  共享自有 4px 程序金币纹理/切图）；`il2cpp/CoinCourierVisuals.cs`（view 句柄新增
  `LeisureCoin` 字段、Render 在身体切图之后（含同帧去重命中分支）接线金币、Destroy 释放本 view
  子对象、ShutdownModule 释放共享金币资源）。
- 测试：`tests/coin-courier-visuals/`（csproj 编入新文件、stub 补 `Texture2D.SetPixels32/Apply`、
  新增 8 个用例、`--preview-samples` 导出真实采样）；`tests/coin-courier-visual-lifecycle/`
  （csproj 编入新文件、新增 4 个用例，未改既有断言）；`tests/coin-courier-runtime-bridge/` 未改
  （其自备 Visuals 替身）。
- 工具/产物：**新增** `tools/preview_coin_courier_leisure.py`、
  `artifacts/coin-courier-leisure-20261003/{samples.json,preview.gif,contact-sheet.png,preview-manifest.json}`。
- 未改：`CoinCourierRuntime/CoinCourierRuntimeRules/CoinCourierCoinFlight/CoinCourierShop/
  CoinCourierEconomy/CoinCourierPersistence/CoinCourierTeleportFx`、全部 PNG/atlas、
  `tools/prepare_coin_courier_b_atlas.py` 与旧 manifest、版本号、配置项、Harmony hook、
  Mono 线、经济/存档/网络行为。

## 设计

### 1. 采样是纯函数（无时钟/无随机/无分配）

`CoinCourierLeisureCoin.TrySample(pose, phaseSeconds, out Sample)`：只有 `Leisure` 返回 true，
其余 7 个姿态返回 false（调用方立即隐藏）。相位先按 Leisure 整圈（`4 × 0.6s = 2.4s`，由
`CoinCourierPoseTable` 推导，不复制常量）取模；NaN/±Inf/负值回退循环起点，与
`FrameIndex` 的非法相位策略一致；同相位幂等（暂停不漂移）。输出 X/Y 是相对脚点（view root 原点）
的本地坐标并**量化到 1 图集像素 = 1/32 单位**，另带自转横向压缩与可见性。

### 2. 段时序与手部锚点（按真实图集逐帧核对）

段边界锚在 Leisure 帧节奏上：把玩 `[0, 1F)`、上抛/下落 `[1F, 2.5F)`、接住/跟手 `[2.5F, 10F/3)`、
收袋 `[10F/3, 4F)`（F=0.6s）。手锚（cell 像素、左上原点，脚点 pivot (28,12) → 顶起第 44 行）：

| 帧 | 像素 (x,y) | 说明 |
|---|---|---|
| 8 | (33,29) | 手垂在腰带前（把玩起点）|
| 9 | (33,28) | 基本同位（起抛）|
| 10 | (25,25) | 抬手到胸前（接住）|
| 11 | (33,29) | 收回腰带前（跟手）|

- 抛起：从帧 8 手锚到帧 10 手锚，`y = lerp + 17px·4u(1-u)`（0.9s），顶点在头顶点上方约 2-3px
  （“低小弧线、顶点略高于头”），水平线性、落点即抬手掌心（t=1.5s 精确等于帧 10 手锚）。
- 接住：先在掌心停 0.30s（接住段前 60%；轻转），随后 0.20s 内跟手回到帧 11 手锚。
- 收袋：0.36s 内平滑移动到袋口 (22,24)，最后 10% 已进袋（`Visible=false`），循环回起点重新拿出来。
- 自转：段内累计圈数连续（把玩 0.30s/圈、空中 0.45s/圈——起抛/顶点/接住都落在币面帧、
  掌心 0.60s/圈、收袋 0.80s/圈），宽度量化成 6 档 `1 / .75 / .5 / .25 / .5 / .75`（币面↔侧边）。

### 3. 渲染与生命周期（fail-closed、零热建）

- `CoinCourierVisuals.Render`：可见且身体当前帧画得出来时调用金币渲染；**同帧去重命中也照样采样**
  （金币相位不因身体跳帧而停）；不可见或身体取帧失败时 `Hide`（只关不建，绝不留下悬空币）。
- 每 view 至多一个自有子对象 `KEM_CoinCourierLeisureCoin`，**首次可见 Leisure 才惰性创建**（老套件的
  decode-only 计数不受影响）；子对象 parent = view 根，随父级 ±镜像与 0.625/0.65625 缩放，
  金币自身只写 `localPosition`（量化）与 `localScale.x`（自转压缩），排序=身体层号+1、材质复用
  身体 material，不写身体 sprite/材质/缩放/z。
- 失败恢复有界：创建失败或子对象被原生回收（fake-null）时本 view 永久关门（一次性告警、绝不逐帧重试）；
  视图根重建由运行时既有路径负责，新 view 重新惰性创建。共享 4px 纹理/切图是模块级资源，
  `ShutdownModule` 释放（`Destroy(view)` 只拆本 view 子对象）；缓存被原生回收时走一次有界重建，
  确证不可用后保持关门（与图集缓存同策略）。
- 存活 view 的资源保护（审查后修订）：可见帧若自有绑定（切图或其纹理）已失效，先走共享
  `EnsureSprite` 有界重建再重绑（两个 view 只重建一次，各自下一帧绑同一份 fresh）；重建失败则先关
  本 view 金币（缓存保持 failed 直到 `ShutdownModule`，不逐帧重试）。身体 Transform/renderer 或 helper
  写属性任一步抛出时，都**先关闭旧金币再告警**——`HideChild` 先关自有 renderer，renderer 不可写时退路
  停用自有 child 根（绝不碰 actor/body root 或其它 view），且不因 `Failed` 标记而跳过仍活 renderer。

## 边界与非目标

- 不生成 `DroppableCurrency`、不碰钱包/银行/账本/任务/物理；不新增 Harmony hook、配置项、全场扫描。
- 店铺预览牌（`CoinCourierShop._marker`）走同一 Render：其 Leisure 片段同样会显示金币，属预期
  （它是同一角色的展示视图）。
- 实机观感（抛接手感、金币大小/时序、镜像、暂停、商店预览）待玩家验收。

## 2026-10-04 V1 交付范围确认

用户明确要求先实装把玩、抛起、接住金币，沿用现有脸部图集；只有游戏体验后认为需要固定每帧脸部时再做 V2。此前脸部一致性要求不再作为本轮 V1 前置，生成的脸部草稿不属于交付内容。

本项按 VERSIONING 新功能小项记 minor +1：既有休闲循环的单一显示扩展，无新界面/配置/存档。基于当前 Mac 累计候选10.8.38，个人实测候选为10.9.38；此记录不创建正式tag/release、不替其他未发布条目重复计分。

代码交付完成条件：必要验证和独立审查通过且 PR 合并后关闭 Issue110；安装与玩家观感单独记录。游戏正在运行时只准备无部署候选，不热替换 DLL、不启动或结束游戏、不写玩家配置和存档。
