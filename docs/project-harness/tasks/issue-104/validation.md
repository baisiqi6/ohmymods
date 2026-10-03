# Issue 104 验证记录

## 变更面（新旧源码差异仅限本清单）

- 生产：`il2cpp/CoinCourierTeleportFx.cs`（特效数学/单表重写）、
  `il2cpp/BankAssistantTeleportVisuals.cs`（两端方向化接线）、
  `il2cpp/CoinCourierRuntime.cs`（哥布林双句柄：出发 Departure + 到达 Arrival）。
- 测试：`tests/teleport-fx-sequence`（新：相位/帧契约 + 生产帧导出器）；
  `tests/coin-courier-visuals`、`tests/bank-assistant-teleport-visuals`、
  `tests/coin-courier-runtime-bridge`、`tests/coin-courier-visual-lifecycle` 的必要期待/stub 适配；
  `tests/greek-bank-assistants-scope` 仅 1 行池上限期望（17→18，见下）＋ stub 增补
  `Transform.TransformVector` 与 `Sprite.vertices`（该套件直接编译生产 FX 代码，属必要的编译适配，
  断言未改）。
- 文档：本目录 plan/validation。无 PNG/资产、版本、配置、Harmony hook、经济/存档行为改动。

## 托管回归（本机 .NET 8，`dotnet run -c Release`）

| 套件 | 结果 |
|---|---|
| `tests/teleport-fx-sequence/SequenceTests.csproj`（新） | 14 passed, 0 failed |
| `tests/coin-courier-visuals/Regression.csproj` | 23 passed, 0 failed |
| `tests/bank-assistant-teleport-visuals/Regression.csproj` | 17 passed, 0 failed |
| `tests/coin-courier-runtime-bridge/CoinCourierRuntimeBridgeTests.csproj` | ALL PASS — 223 checks |
| `tests/coin-courier-visual-lifecycle/CoinCourierVisualLifecycleTests.csproj` | 11 passed, 0 failed |
| `tests/coin-courier-shop/CoinCourierShopTests.csproj`（链接 runtime-bridge stubs） | ALL PASS — 73 checks |
| `tests/coin-courier-runtime/CoinCourierRuntimeTests.csproj`（业务时长常量回归） | ALL PASS — 124 checks |
| `tests/greek-bank-assistants-scope/Regression.csproj`（编译生产 FX） | 73 passed, 0 failed |

## 行为契约翻转（现有套件）

- `coin-courier-visuals`：线数 10→16、寿命 .24→.32、白/灰为主+少量金色阶 → style-owned 清晰金/
  近黑；默认 Begin 形状语义明确为 Departure（起步满长），旧的"逐线入场+全组收缩"峰值采样改为
  Arrival 锚点采样；`StaggeredEntrance` 改测 Arrival 激活错落；旧的"正负斜度 ~4.6-7.8°"契约整体
  删除，改为全组统一微斜（单一 `TiltSlope = .10` ≈ 5.7°：任意年龄每线比率恒 .10、同一样式全组
  同向、两线正规化方向叉积 ≈ 0（互相平行）、H 以横为主/V 以竖为主、中点不漂移、两次 Begin 确定）。
- `bank-assistant-teleport-visuals`：两端共 32 根线、池上限 18、到达端（Arrival）在 t=0 是短起步
  （旧断言的"到达端即满长上探头部"翻转），"approved light lean"断言改为统一微斜 .10（全组同向、
  比率恒 .10）；并新增"出发满长/到达短起步"的方向证据与锚点增长断言。
- `greek-bank-assistants-scope`：仅必要的既有期待/stub 适配——1 行期望值 `MaxConcurrent` 17→18
  （该行注释公式"8 assistants x 2 ends + 1 goblin"是旧单端哥布林公式；issue-104 用户要求哥布林
  双端，正确值为 18）＋ 编译用 stub；无经济断言改动。
- `coin-courier-runtime-bridge` / `coin-courier-visual-lifecycle`：FX 替身补 8 参方向重载
  （记录 direction/anchor/position），并新增/扩展哥布林双柄用例（见下）；生命周期套件行为断言未改。

## 本批覆盖的契约

- 单表 16 根：长度/行/中心/宽度 16 项互不相同；6 骨架 + 10 填空细线；脚锚、包络、收尖、
  主辅粗细档保持；表内模板行分布 H y≈.09~.66 / V x≈.06~.84 —— 这是**模板**分布，最终屏幕几何由 fit 到可见身体框决定（≤1.05 body），不是固定 .09~.66/.06~.84。
- 统一微斜、互相平行（非 0°、非逐线正负斜度）：单一共享 `TiltSlope = .10`（≈5.7°）；任意年龄、
  两方向、两样式下每线方向比率恒 .10（H: |dy|/|dx|、V: |dx|/|dy|）、全组同向、任意两线正规化
  方向叉积 ≈ 0（float 误差内，实测 ≤ 3.3e-7）；主轴投影正确（H 只沿 x 主导、V 只沿 y 主导）；
  rise/lean 与半长同乘（scale / 包络 shrink 同步），中点不随收缩漂移；两次 Begin 逐点确定
  （visuals 的 UniformTiltContract 与 sequence 的 StripesShareOneUniformTilt 双重覆盖，后者
  1/120s 采样跨整寿命，span 退化分支不除零）。
- 包络说明：微斜让 H 行上下边缘与 V 列左右边缘轻微外扩（H 顶行 +half×.1、V 列 ±half×.1），
  不改变线心/脚锚/位置/物理，也不裁剪端点（裁剪会破坏平行）；实测 V 最外端 |x| ≤ .2995 仍在
  既有箱体内。
- 样式色阶：H 全 16 根清晰金（r>g>b、r-b ≥ .2、不达旧洗白色）；V 全 16 根近黑（max 通道 ≤ .2、
  r≥b 暖端）；把传入 Base 从白改成品红时 RGB 逐分量不变（只有 alpha 生效）。暖色加在 start/end
  双色上、LineRenderer 沿线段插值，因此整条线是暖近黑（不是"端点抬升、主体不变"）；亮底近黑
  不白不灰，暗底对比实际弱于亮底，属已知取舍。
- 方向包络：Arrival 密度/总长在 [0, anchor] 单调增、峰值恰在 anchor（全部 16 根满长）之后单调减；
  Departure t=0 即 16 根满长（仅 .025s 统一淡入防硬闪），密度/总长全程单调减，anchor 处只剩
  1~6 根 ≤ 62% 长的残线。两方向都不是"所有线只同时改 alpha"（激活/收束窗口逐线错开且互不重复）。
- 校验：未知 style/direction、NaN/Infinity/≤.02/>.208 的 anchor 在占用池槽前拒收；最大合法
  anchor（.208）可用；暂停（0/NaN/负）、同帧去重、`Clear` 后 stale 句柄均不改变状态。
- 池：上限 18（8 助手双端 + 哥布林双端），19 次 Begin 后 ActiveCount=18 且最旧句柄失效；
  Departure 与 Arrival 同 owner 并存时 Begin 到达不取消出发。
- 哥布林接线：一次跳转恰好 1 个 Departure（原位置、Bank home）+ 1 个 Arrival（落点、.18 锚点、
  在隐藏相起播、`!LastVisible`）；TeleportIn 完成不重播、不取消两端；`SnapHome` 先取消自身两柄
  并立即回家显形（断言同帧 `LastVisible=true`），随后起的只是常规 Arrival（age 0 透明、.18 增长，
  与已完成的显形不对齐，不是即时峰值 burst）；场景清空取消两柄且从不 `Clear` 共享池。
- 助手接线：From=Departure、To=Arrival（RevealDelaySeconds）；两端都成功才隐藏、失败 fail-open、
  原 enabled 恢复、.12 显形后残影期仍持有可取消——原有生命周期契约全部保持。

## actual 161refs 构建

以精确 2.4 BepInEx/Il2CppInterop 引用（161 references 模板，输出直落本任务私有证据目录，
不部署、不写游戏目录）编译整棵 `il2cpp/*.cs`：**0 warning / 0 error**（构建日志
`evidence/actual-arm/build.log`，产物 DLL SHA-256
`783e083aa56ff01189146acf68d3e24982961658da694bc36a44c04f5d3e0ce9`）；构建含本轮全部生产改动（身体适配、
native fallback、方向化 API）。

## 代码驱动预览与尺寸实证（私有，未入库）

生产导出器（`tests/teleport-fx-sequence --export`）以**生产** `Begin/TickForFrame` + LineRenderer
stubs 导出逐帧四点 / start-end RGBA / width curve keys+multiplier / visibility，并按 sprite 的
scale/PPU 复现真实屏幕比例；示意角色只作显形时序参照，**不是游戏录像**（私有产物不入库，
公开文档不含本机路径）。

独立审查（production 未改）以真实 5 张自有 PNG 全部 160 帧 + 80 native × 正负 X 共 480 run ×
20 frame 复核：W/body ∈ 1.011..1.05、H/body ∈ .93394..1.04868、center 误差 ≤ 5e-8、
slope .10 最大误差 1.6e-6。该项仅覆盖几何/包络数据，**不等于**实机材质/排序/观感或跨平台验收。

## 热路径 / 资源上限

- 每条线仍是 4 顶点 + start/end 双色 + `widthMultiplier` 写入；无逐帧数组/curve/闭包分配；
  widthCurve 类型初始化建一次、池复用不重建；共享 Sprites/Default 材质不变。
- LineRenderer 上限：10×17=170 → 16×18=288（+69%）；池满时复用最旧组，代际取消语义不变。

## 未验证（实机）

真实 Unity 材质/排序与夜景表现、暗底黑色可读性、密集场景帧耗时、主客机联机、
助手/哥布林的实机观感与显形时序。本批未启动游戏、未触碰玩家存档、未提交/安装/发布。
