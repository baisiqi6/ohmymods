# CalendarHud 银行余额列 — Worker 交付说明

修改仅涉及 `cwd/CalendarHud.cs`（canonical 未动）。基线为 canonical `CalendarHud.cs`（2026-09-06 时点），在其上增量修改。

## 改动内容

1. **常驻时间条新增银行余额列**：总宽度 `Width` 688 → 824，新增约 136px 的第四列，位于"下一季"列右侧，含竖直分隔线（`new Color(1,1,1,0.12f)`，与既有列分隔线同款）、"银行"小标题、程序化钱币图标（IconShape index 6，金色 tint）、金色余额数字（`_number` 样式 + `Gold` 色）。
2. **图标数组** 6 → 7。index 6 为钱币（厚圆环 + 中心实心圆，纯 `Mathf` 判定，复用既有 3x3 超采样生成管线）。**index 5 显式保留为箭头**（原 fallthrough 改为 `if (index == 5)` 分支），index 6 走独立 return，不会误入默认 arrow。
3. **读取点与频率**：`Tick()` 内现有 0.5s 限频缓存块（`_nextRead`）中调用 `BankAssistantCoordinator.GetStashedCoinsForPanel()`，一次调用、零额外扫描；结果格式化为 `_bankText` 字符串缓存。`Draw()` 只读 `_bankText`，不在 OnGUI 里做任何游戏查询。
4. **未就绪 / 0 / 巨额**：返回 -1 → 显示 "—"；返回 0 → 明确显示 "0 币"；2147483647 → "2147483647 币"，由既有 `Label()` 自适应字号（min 8pt + `TextClipping.Clip`）保证不越列。
5. **失效清理**：`Clear()`（禁用开关、切世界/场景/director、加载态、异常路径共用）复位 `_bankText = "—"`，不会显示上个世界的余额。
6. **未改动**：季节进度条仍表达季节进度（宽度随 Width 变宽但仍按 `_snapshot.Progress` 填充，与银行列无关）；CalendarReader 算法、GUI 状态保存/恢复、`LogOnce` 限频异常记录、ImGuiCompat 路径、ModConfig 开关均原样保留。未新增 hook/扫描/组件/每帧读取，无新 Unity API、无 GUI.DrawTexture、无 emoji 字体依赖。

## 几何尺寸（逻辑坐标，Draw 内 scale 前）

- 整体：824 x 86（原 688 x 86），背景 `_back` 纹理按新宽度拉伸，圆角逻辑不变。
- 银行列：分隔线 x+692（y+18，h45）；标题"银行" (x+708, y+12, 90x19)；图标 26px (x+706, y+35)；数字 (x+740, y+33, 78x34)。
- 其余列坐标与原版完全一致（王国历 / 时刻 / 本季 / 下一季）。

## 兼容性检查需求（operator 编译/审计时关注）

- **屏宽适配**：scale = `clamp(min(W/1280, H/720), 0.45, 1)`。W≥1280 时 logical 宽 1280+，824 居中无越界；640 宽（如 640x360/640x480）时 W/1280=0.5 主导，logical 宽恰 1280，824 仍居中适配。若出现极端窄高比使 logical 宽 < 824，现有 0.45 下限即为最后收缩手段（未新增机制）。
- **数据口径**：与 ModPanel 主面板完全同源（`GetStashedCoinsForPanel()` 直读主银行家 `_stashedCoins`，不含助手背包/玩家钱包）；确认 worker 改动未触碰 `PatchEconomy_BankAssistants.cs`。
- **编译检查点**：`Icons` 数组长 7 与 `IconShape` 分支一一对应；`BankAssistantCoordinator` 可见性（同 assembly internal）；`stashed.ToString() + " 币"` 无 culture 依赖问题（int 十进制默认无分组符）。
- **行为检查点**：切世界/回主菜单后 HUD 银行列应显示 "—" 而非旧值；银行家未解析时显示 "—"，解析后 0.5s 内刷新；存款为 0 显示 "0 币"。
- 未运行游戏/未编译（按约定由 operator 负责）；120 秒/4 人默认值与 ModPanel 描述未触碰。
