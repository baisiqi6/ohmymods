# Issue 104：传送特效增密/分色/方向化多阶段

用户 2026-10-03 授权（Issue→PR→merge→冷安装）：把哥布林与税收助手共用的传送线特效从
"10 根、白色混合、无方向"升级为"16 根、样式自有金/黑、Arrival/Departure 双向多阶段"，
保持现有样式分组（助手 0..3 金色横纹、4..7 黑色竖纹、哥布林黑色竖纹）与全部业务契约
（金币/账本/拾取/任务/网络/持久化/时长 0.12/0.18 显形窗口不改）；先以代码驱动对照预览评审（私有）。

## 变更面

- 生产：`il2cpp/CoinCourierTeleportFx.cs`（重写特效数学与表）、
  `il2cpp/BankAssistantTeleportVisuals.cs`（两端方向化接线）、
  `il2cpp/CoinCourierRuntime.cs`（哥布林双句柄：出发 Departure + 到达 Arrival）。
- 测试：适配 `tests/coin-courier-visuals`、`tests/bank-assistant-teleport-visuals`、
  `tests/coin-courier-runtime-bridge`、`tests/coin-courier-visual-lifecycle`；
  新增 `tests/teleport-fx-sequence`（相位/帧包络断言 + 真实生产帧导出器）。
- 文档：本目录 plan/validation；预览与导出产物只在私有证据目录（公开文档不含本机路径）。

## 设计

### 1. 样式与色阶（style-owned，Base RGB 不再混色）

- `Horizontal` = 清晰金色阶（亮金 / 深金 / 淡金 / 琥珀）；两端色随 alpha 权重轻微压暗，主体清晰金。
- `Vertical` = 高不透明近黑阶（核心黑 / 次黑 / 暖黑 / 最深）；start/end 双色按 alpha 权重加克制
  暖色（+R .085 / +G .050 / +B .024）。LineRenderer 只沿线段在两端色之间插值，所以整条线实际读作
  "暖近黑"（不是仅端点变亮、主体不变）；亮底仍近黑不白不灰，暗底对比实际弱于亮底，属已知取舍。
- 传入 `color` 的 RGB 不再参与任何混色（白色不会把黑线洗灰/洗白）；只有 `.a` 缩放整体 alpha。
- 无白色线/白闪：全部色阶条目 r>g>b 明确有色相，最大通道受条目 Brightness 限制。

### 2. 单表 16 根线（几何/宽度/时程唯一事实来源）

- 表长派生 `DashesPerEffect = 16`；0..9 行保持现网几何/宽度/端点明暗（骨架 6 + 原填空 4），
  新增 10..15 行 6 根填空细线（H 行 y≈.565/.395/.335/.215/.165/.115，V 列
  x≈-.235/-.045/.03/.165/.23/.275），不收扩原包络、脚锚/收尖/主辅粗细分档全保留。
- **全组统一微斜、线间互相平行**：单一共享 `TiltSlope = .10`（≈5.7°），H: rise = half × .10、V:
  lean = half × .10；同一样式内所有线同方向同斜率（H 以横轴为主、V 以纵轴为主），rise/lean 与半长
  同乘（scale / 包络 shrink 同步，方向任何年龄不变）；既不是旧的逐线正负斜度，也不是 0° 水平/垂直。
  不同线只用长短/粗细/位置/时差区分，无交叉锯齿、无随机抖动（用户 2026-10-03 两次澄清的最终定稿）。
- 宽度（世界值）16 条互不相同：骨架 .01248~.02496，10 条辅线 .01209~.01287（且 ≤ .0135）。
- 每条线新增 4 个时程列：`ArrivalAt`（激活时间 = share × anchor）、`DepartAt`（收束开始 =
  share × anchor）、`TailAt`（残线结束 = anchor + (寿命−anchor) × share）、`ArrivalFrom`
  （入场起点长度系数）。16 行的三个 timing 列各自互不重复 → 两个方向的窗口天然错落。

### 3. 方向化包络（`LifetimeSeconds` = .32s，60FPS 约 19~20 帧）

- `Arrival`（入场）：`entry = ArrivalAt×anchor`；长度 = ArrivalFrom → 1.0 线性增长，**峰值恰在
  anchor**（晚激活线也在 anchor 前完成淡入）；anchor 后按 TailAt 缩到残线长度并渐退；密度
  （可见线数）在 [0, anchor] 单调增、之后单调减，总长度和同样先增后减。
- `Departure`（离场）：t=0 即 16 根满长（仅 0.025s 的 alpha 淡入防硬闪，不是"少→多"）；
  每条线按 DepartAt 错落收束（长度 1→.35、alpha 1→0）；`DepartAt > 0.55` 的少数残线收短后
  在 anchor 后按 TailAt 淡出。密度与总长度全程单调减（收束窗口逐线错开，非同时 alpha 变化）。
- 曲线只做线性包络（`Ramp` 纯函数），无随机数、无逐帧分配。

### 4. API 与校验

- 新增 `CoinCourierTeleportDirection { Departure = 0, Arrival = 1 }`（默认值保持旧语义）。
- 保留旧 Begin 3/5/6 参重载形状（Horizontal + **Departure** + `DefaultAnchorSeconds` 0.12）；
  新增 8 参方向重载 `(pos, color, scale, layer, order, style, direction, anchorSeconds)`，以及 9 参
  身体重载 `(…, anchorSeconds, bodySource)`：`bodySource` 缺失（null）或解析失败 → Begin 无效
  （fail-closed），不静默降级为模板几何。
- 校验全部在占用池槽前：非法 style / 未知 direction / 非有限或越界 anchor（<.03 或 > .208）
  拒收并 WarnOnce；池复用（Begin）整体覆盖 style/direction/anchor/base alpha/scale，无残留。
- `MaxConcurrent` 17 → 18（8 助手双端 16 + 哥布林双端 2）；16×18=288 条 LineRenderer，
  沿用池复用/共享材质/类型初始化 curve，热路径仍只写 4 顶点 + 双色 + widthMultiplier。

### 5. 调用方接线（契约不变，仅方向/句柄拆分）

- `BankAssistantTeleportVisuals`：From = Departure、To = Arrival，锚点都用既有
  `RevealDelaySeconds`（.12）。两端都成功才隐藏、恢复捕获 enabled、等待/残影/借用/清理契约不变。
- `CoinCourierRuntime`（哥布林）：
  - `OnJumpComplete` 先清自己上一轮的两个句柄 → 在**出发位置**起 Departure（先取样再移
    `S.Position`）→ 移到落点/银行 → 在**落点**起 Arrival（锚点 `TeleportSeconds` .18，先线后人）。
  - `TickTeleport` 完成时不再重播/重启 Arrival（业务位置/phase/Visible/ActionConsumed/校验全保持），
    也不取消 Departure。
  - `CancelTeleportFx`（Clear/失权/换 world 用）取消自己的一对句柄；旧代句柄天然 no-op。
  - `SnapHome` 先撤销失效落点的一对句柄，再按原路径即时回家显形（`S.Visible` 即刻为真，绝不新增
    业务等待）；随后起的 Arrival 只是常规入场动画（age 0 透明、按 .18 增长），与已完成的即时显形
    并不对齐——人先出现、线随后淡入，这是安全兜底特例，不是"即时峰值 burst"。
- 零新增 Harmony hook / 用户配置项 / 全场扫描。

## 测试

- `tests/teleport-fx-sequence`（新）：方向/样式矩阵的逐帧包络断言（密度/长度单调与峰值锚点、
  错落、全组统一微斜且互相平行（单一 `TiltSlope=.10`，任意年龄比率恒 .10、跨线叉积≈0、H 以 x
  为主/V 以 y 为主）、非白/近黑、拒收、身体适配（缩放/框中心/倾斜保形）、native 证据 fallback
  （全部 80 例 fixture 对真实 crop alpha 宽/高/底边、4 角色 firstIdle × H（真实 caller 形状）+
  V（generic 覆盖）完整 Begin 包络、同名不同 mesh 各自成框、非证据 sprite
  拒收）、池上限 18、stale 句柄、同帧/暂停、两方向并存不互cancel）+ 导出器
  （真实生产 `Begin/TickForFrame` + LineRenderer stubs、真实调用参数 `.95/.82/.42/.85`，
  导出逐帧 positions/colors/width/visibility/anchor/direction 为 JSON，供私有预览渲染）。
- 适配四个现有套件：数量/时长/颜色/平行契约翻转（10→16、.24→.32、白→金/黑、去掉正负斜度）与
  goblin 双句柄、bank 两端方向断言；`greek-bank-assistants-scope` 仅必要的既有期待/stub 适配
  （池上限一行 17→18 + 编译用 stub），无经济断言改动。

## 已知边界

- 静态帧导出只能证明生产曲线/几何/颜色数据；真实 Unity 材质、排序、夜景观感仍需实机确认。
- 助手/哥布林业务时长（.12/.18）与所有经济/存档行为未改；实机验证（含暗底黑色可读性、
  密集齐射帧耗时、联机）待合并后安装由玩家验收。

### 6. 身体适配（fit）与 native 证据 fallback（后续加做，Operator 评审要求）

- `Begin` 可带 `bodySource`（角色 SpriteRenderer）：一次 fit 把 16 根线的"中心分布/主轴长度"
  分别映射到可见身体框，再做统一收紧 `tighten = min(1, 1.05·bodyW/envW, 1.05·bodyH/envH)`，
  最终包络两轴 ≤ 1.05 body 且互相接近，中心对到 body.Center；斜率始终由最终半长导出（.10），
  宽高差异不拉歪方向；缩放的 x 符号（flip）由 `TransformVector` 读取，旋转/斜切拒收。
- 解析：not-packed（可读纹理）扫 alpha；packed 仅证据 native（32×32/PPU32/pivot(16,0)/5~10 顶点，
  名 `banker_idle/walk/run` 仅作资格门）走 `sprite.vertices` 保守近似；其它 packed/不可读/未知
  一律拒收——**没有** sprite.rect 回退。失败（含失败）缓存避免热循环；`CoinCourierTeleportBodyBox.Clear()`
  随池/世界清空。
- native 保守近似实测（operator json 全部 80 例）：width 与真实 crop alpha 宽一致（差 ≤1px），
  height 保守（≥ 真实 alpha 高、最多多 2px），脚底与真实 alpha 底边误差 ≤2px；bbox 按各 sprite
  自身 vertices 计算（同名 bamboo 不与 banker 共享缓存/不按名合并）。
- 样式槽位分组不变：助手槽 0..3 = Horizontal（旧 4 名）、4..7 = Vertical（新增 4 名），
  Goblin = Vertical；16 根/组的身体包络在独立审查中以真实自有 PNG 全 160 帧 + 80 native ×
  正负 X 共 480 run × 20 frame 复核：W/body ∈ 1.011..1.05、H/body ∈ .93394..1.04868、
  center 误差 ≤ 5e-8、slope .10 最大误差 1.6e-6（几何数据实证，不等于实机材质/观感验收）。
- 代码驱动预览（私有，未入库）：生产导出器输出正确的逐帧四点 / start-end RGBA / width curve +
  scale/PPU 真实比例；示意角色只作显形时序参照，不是游戏录像。
