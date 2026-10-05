using System;
namespace OhMyMods.AndroidProbe;
/// <summary>
/// 浮球与公共面板的纯几何（无 UnityEngine 依赖，host 测试直接编译）。
///
/// 浮球行为保持原样：直径 48*Scale、触控方 72*Scale、默认折叠、点按展开、拖拽后贴边。
/// Resize(width,height) 保留给原 host；Resize(width,height,left,top,right,bottom) 接收
/// RootFloatInput 由 nativeScreen.safeArea 转换到 top-left 原点的安全区绝对边界
/// （left/top = 安全区左上角坐标，right/bottom = 安全区右下角坐标），浮球与面板都被
/// 限制在安全区内。
///
/// 面板外框固定为 min(320*Scale, 可用宽) x min(480*Scale, 可用高)：页面内容只决定内容
/// 视口的 scrollMax（ScrollMax），不反向改变外框，因此不存在上一帧 panelRect 镜像/并集遮罩。
/// </summary>
internal sealed class FloatLayout
{
    public float Scale => Math.Clamp(Math.Min(Width, Height) / 720f, .35f, 2f);
    public float Diameter => 48 * Scale;
    public float TouchSize => 72 * Scale;
    // 固定外框上限 min(320*scale, 可用宽) x min(480*scale, 可用高)；可用量按安全区实际剩余，
    // 极小屏允许收缩到 0（不强行撑大越界）。
    public float PanelWidth => Math.Min(320 * Scale, Math.Max(0, SafeRight - SafeLeft - 8 * Scale));
    public float PanelHeight => Math.Min(480 * Scale, Math.Max(0, SafeBottom - SafeTop - 8 * Scale));
    public float X { get; private set; } = 36;
    public float Y { get; private set; }
    public bool Expanded { get; set; }
    public bool PopulationPage { get; set; }
    public bool PlayerPage { get; set; }
    public bool WorldPage { get; set; }
    public bool GenerationPage { get; set; }
    public bool VegetationPage { get; set; }
    public bool Captured { get; private set; }
    public bool Dragged { get; private set; }
    public float Width { get; private set; }
    public float Height { get; private set; }
    // 安全区绝对边界（top-left 原点）；未调用 6 参 Resize 前为整屏。
    public float SafeLeft { get; private set; }
    public float SafeTop { get; private set; }
    public float SafeRight { get; private set; }
    public float SafeBottom { get; private set; }

    // 面板固定 chrome 与内容视口（纯几何；公共面板据此裁剪与计算滚动范围）。
    // 退化尺寸下 origin 也不越过面板：偏移取 min(raw, 实际面板尺寸)，剩余宽高 >= 0，
    // 保证 viewport 是外框的子集（不承诺 tiny 屏可操作）。
    public float HeaderHeight => 52 * Scale;
    public float NavHeight => 46 * Scale;
    public float ContentX => PanelX + Math.Min(12 * Scale, PanelWidth);
    public float ContentY => PanelY + Math.Min(HeaderHeight + NavHeight, PanelHeight);
    public float ContentWidth => Math.Max(0, PanelWidth - 2 * Math.Min(12 * Scale, PanelWidth));
    public float ContentHeight => Math.Max(0, PanelHeight - Math.Min(HeaderHeight + NavHeight, PanelHeight) - Math.Min(12 * Scale, PanelHeight));
    public float ScrollMax(float contentHeight) => Math.Max(0, contentHeight - ContentHeight);

    private float startX, startY, downX, downY;

    public void Resize(float width, float height) => Resize(width, height, 0, 0, width, height);

    public void Resize(float width, float height, float left, float top, float right, float bottom)
    {
        float w = Math.Max(40, width), h = Math.Max(40, height);
        left = Clamp(left, 0, w - 8);
        top = Clamp(top, 0, h - 8);
        right = Clamp(right, left + 8, w);
        bottom = Clamp(bottom, top + 8, h);
        if (w == Width && h == Height && left == SafeLeft && top == SafeTop && right == SafeRight && bottom == SafeBottom) return;
        float fy = Height > 0 ? Y / Height : .22f;
        bool rightSide = Width > 0 && X > Width / 2;
        Width = w; Height = h;
        SafeLeft = left; SafeTop = top; SafeRight = right; SafeBottom = bottom;
        X = rightSide ? SafeRight - TouchSize / 2 : SafeLeft + TouchSize / 2;
        Y = Clamp(fy * Height, SafeTop + TouchSize / 2, SafeBottom - TouchSize / 2);
        Captured = false; Dragged = false;
    }

    public bool HitBall(float x, float y) => Math.Abs(x - X) <= TouchSize / 2 && Math.Abs(y - Y) <= TouchSize / 2;
    // 位置边距与尺寸边距同用 scale，保证外框在安全区内（含极小屏）。
    public float PanelX => Clamp(X < Width / 2 ? X + Diameter / 2 + 5 * Scale : X - Diameter / 2 - 5 * Scale - PanelWidth,
        SafeLeft + 4 * Scale, Math.Max(SafeLeft + 4 * Scale, SafeRight - PanelWidth - 4 * Scale));
    public float PanelY => Clamp(Y - PanelHeight / 2, SafeTop + 4 * Scale, Math.Max(SafeTop + 4 * Scale, SafeBottom - PanelHeight - 4 * Scale));
    public bool HitPanel(float x, float y) => Expanded && x >= PanelX && x <= PanelX + PanelWidth && y >= PanelY && y <= PanelY + PanelHeight;
    // 面板自身手势的命中：球覆盖处让位（窄屏重叠仍球优先）；触摸过滤仍用完整 HitPanel。
    public bool HitPanelBody(float x, float y) => HitPanel(x, y) && !HitBall(x, y);

    public bool Begin(float x, float y)
    {
        if (!HitBall(x, y)) return false;
        Captured = true; Dragged = false; downX = x; downY = y; startX = X; startY = Y; return true;
    }
    public void Move(float x, float y)
    {
        if (!Captured) return;
        float dx = x - downX, dy = y - downY;
        if (dx * dx + dy * dy >= 100 * Scale * Scale) Dragged = true;
        if (!Dragged) return;
        X = Clamp(startX + dx, SafeLeft + TouchSize / 2, SafeRight - TouchSize / 2);
        Y = Clamp(startY + dy, SafeTop + TouchSize / 2, SafeBottom - TouchSize / 2);
    }
    public bool End(float x, float y)
    {
        if (!Captured) return false;
        Move(x, y); Captured = false;
        bool clicked = !Dragged;
        if (clicked) Expanded = !Expanded;
        X = X < Width / 2 ? SafeLeft + TouchSize / 2 : SafeRight - TouchSize / 2;
        return clicked;
    }
    public void Cancel() { Captured = false; Dragged = false; }
    private static float Clamp(float v, float lo, float hi) => Math.Min(hi, Math.Max(lo, v));
}
