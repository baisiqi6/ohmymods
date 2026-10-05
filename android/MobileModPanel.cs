using System;
using System.Collections.Generic;
using UnityEngine;
using KingdomEnhancedMod;

namespace OhMyMods.AndroidProbe;

/// <summary>
/// 公共面板容器（Issue #144）：固定外框 + 唯一 hotControl 手势 + 内容视口滚动 + 中文导航。
///
/// Root ProbeTicker 只调用 Draw(FloatLayout) / CancelGesture() / Dispose()；浮球事件先于面板
/// 捕获（本类只处理 hotControl==0 且未落在浮球触控方内的 MouseDown）。四个 MobileMenu 与
/// MobilePopulation 只做 presentation，经 Toggle/Step/Info 这一条公共路径登记 tap rect 并绘制。
///
/// 交互契约：
/// - 卡片不使用 GUI.Button，也不抢 hotControl；固定导航/关闭同手势但不滚动。
/// - 手势与滚动状态由 PanelGesture 单点持有：任意方向位移达到 10*Scale 后 sticky Moved，
///   拖回原点/水平移动也零动作；End 先按实际抬手位置归账；release 与按下同一 tap rect 且
///   未 Moved 才恰好触发一次动作。滚轮/拖拽/帧末收束共用同一 ScrollY。
/// - 外框、chrome、内容都在 BeginGroup(panelRect) 内绘制（chrome 用 local 0,0,w,h；内容再嵌
///   viewport 减 panelOrigin 的 localRect），字形/命中外框不伸出；行 tap rect 由 physical
///   viewport 算成 absolute 并与外框/视口求交，Draw 与 Hit 一份几何；ProcessEvent 在所有
///   GroupEnd 之后只读 physical 鼠标坐标。
/// - viewport 几何变化时（内容尺寸/安全区变化）先释放旧的自有 hotControl 再重取 control id。
/// - 折叠/换页只取消本面板手势（CancelGesture 是清自有 scroll/capture 的唯一责任），不调用
///   Root 的整体 Cancel/FloatInput.Reset；held TouchClaims 由 FloatInput 按 Ended 自然释放。
///   Draw 异常不在此吞掉，由 Root 唯一的 renderFailed/cancel/Hide 处理；本类只保证自有皮肤
///   与 Group 在 finally 恢复。不触碰 GUI.matrix/enabled/depth。
/// </summary>
internal sealed class MobileModPanel
{
    private const int PanelControlHint = 0x4F4D5041;
    private const float WheelStep = 24f; // 每格滚动量（乘以当前 Scale）

    private static readonly string[] NavLabels = { "主页", "玩家", "世界", "生成", "人口" };
    private static readonly Action CalendarToggle = MobileCalendar.Toggle;

    private readonly List<Hit> hits = new();
    private readonly PanelGesture gesture = new();
    private readonly Action[] chips;
    private readonly Action closeAction;
    private readonly Action openVegetation;
    private readonly Action closeVegetation;

    private ModPanelStyles styles;
    private FloatLayout layout;
    private Rect panelRect;
    private Rect viewport;
    private int ownControlId;
    private float cursor;
    private float scrollMax;
    private float rowWidth;
    private float rowScale = 1f;
    private float appliedScale = -1f;
    private bool disposed;

    internal MobileModPanel()
    {
        chips = new Action[] { ShowRoot, ShowPlayer, ShowWorld, ShowGeneration, ShowPopulation };
        closeAction = Close;
        openVegetation = OpenVegetationPage;
        closeVegetation = CloseVegetationPage;
    }

    /// <summary>菜单行内导航（返回世界/进入植被页）用的自有动作；随面板实例缓存，无每帧委托分配。</summary>
    internal Action OpenVegetation => openVegetation;
    internal Action CloseVegetation => closeVegetation;

    private struct Hit
    {
        internal Rect Rect;
        internal Action Action;
    }

    internal void Draw(FloatLayout current)
    {
        if (disposed) return;
        layout = current;
        if (current == null || !current.Expanded) return;
        Event e = Event.current;
        if (e == null) return;
        if (styles == null) styles = ModPanelStyles.Create(GUI.skin);
        if (appliedScale != current.Scale)
        {
            styles.SetScale(current.Scale);
            appliedScale = current.Scale;
        }
        var nextViewport = new Rect(current.ContentX, current.ContentY, current.ContentWidth, current.ContentHeight);
        if (nextViewport != viewport && gesture.Captured) CancelGesture();
        viewport = nextViewport;
        panelRect = new Rect(current.PanelX, current.PanelY, current.PanelWidth, current.PanelHeight);
        ownControlId = GUIUtility.GetControlID(PanelControlHint, FocusType.Passive, panelRect);
        hits.Clear();
        cursor = 0f;
        rowWidth = current.ContentWidth;
        rowScale = current.Scale;
        GUISkin entrySkin = GUI.skin;
        GUI.skin = styles.Skin;
        try
        {
            GUI.BeginGroup(panelRect);
            try
            {
                DrawChrome(panelRect.width, panelRect.height);
                if (CanShowContent)
                {
                    var contentGroup = new Rect(viewport.x - panelRect.x, viewport.y - panelRect.y, viewport.width, viewport.height);
                    GUI.BeginGroup(contentGroup);
                    try { DrawPage(); }
                    finally { GUI.EndGroup(); }
                }
            }
            finally { GUI.EndGroup(); }
        }
        finally
        {
            GUI.skin = entrySkin;
        }
        scrollMax = current.ScrollMax(cursor);
        gesture.ClampToMax(scrollMax);
        ProcessEvent(current, e);
    }

    /// <summary>Root 整体 Cancel/浮球折叠路径：取消自有手势并清滚动（不清别人 hotControl）。</summary>
    internal void CancelGesture()
    {
        gesture.Cancel();
        gesture.ResetScroll();
        if (ownControlId != 0 && GUIUtility.hotControl == ownControlId) GUIUtility.hotControl = 0;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        gesture.Cancel();
        hits.Clear();
        ModPanelStyles owned = styles;
        styles = null;
        if (owned != null) owned.Dispose();
    }

    // ---- 菜单呈现 API：一条公共路径登记 tap rect 并绘制 ----

    /// <summary>开关行：值文案 已开启/已关闭。</summary>
    internal void Toggle(string title, bool value, string help, Action action)
        => Row(title, value ? "已开启" : "已关闭", help, true, value, action);

    /// <summary>档位行：右侧显示当前档位文案，点按调用一次现有 Cycle*/Toggle*。</summary>
    internal void Step(string title, string value, string help, Action action)
        => Row(title, value, help, true, false, action);

    /// <summary>只读行（人口等）：无 tap rect。</summary>
    internal void Info(string title, string value, string help)
        => Row(title, value, help, false, false, null);

    private void Row(string title, string value, string help, bool interactive, bool valueOn, Action action)
    {
        var metrics = ModPanelControls.MeasureRow(rowWidth, title, value, help, interactive, styles, rowScale);
        float height = ModPanelControls.RowHeight(metrics);
        float localY = cursor - gesture.ScrollY;
        if (action != null)
        {
            // tap rect 由 physical viewport 算成 absolute 并裁剪到视口：Draw 与 Hit 同一几何来源。
            Rect tap = ImGuiCompat.IntersectRect(new Rect(viewport.x, viewport.y + localY, rowWidth, height), viewport);
            if (tap.width > 0f && tap.height > 0f) hits.Add(new Hit { Rect = tap, Action = action });
        }
        ModPanelControls.DrawRow(0f, localY, rowWidth, height, title, value, help, interactive, valueOn, metrics, styles,
            viewport.position, new Rect(0f, 0f, viewport.width, viewport.height));
        cursor += height + ModPanelControls.RowGap * rowScale;
    }

    private bool CanShowContent => viewport.width > 0f && viewport.height > 0f && rowWidth > 0f;

    // ---- 面板 chrome（固定，不滚动；local 坐标绘制，hit 换 screen 后裁到外框）----

    private void DrawChrome(float width, float height)
    {
        float scale = rowScale;
        float pad = 12f * scale;
        ModPanelControls.DrawPanelSurface(new Rect(0f, 0f, width, height), styles,
            panelRect.position, new Rect(0f, 0f, width, height));
        var closeSize = ModPanelStyles.MeasureSize(styles.Button, new GUIContent(ModPanelControls.CloseLabel));
        float closeWidth = Mathf.Max(56f * scale, closeSize.x);
        float closeHeight = Mathf.Max(34f * scale, closeSize.y);
        var closeRect = new Rect(width - pad - closeWidth, 8f * scale, closeWidth, closeHeight);
        float titleWidth = Mathf.Max(0f, closeRect.x - 6f * scale - pad);
        if (titleWidth > 8f * scale)
            GUI.Label(new Rect(pad, 6f * scale, titleWidth, 40f * scale),
                Ellipsize(styles.CompactTitle, ModPanelControls.PanelTitle, titleWidth), styles.CompactTitle);
        ChromeButton(closeRect, ModPanelControls.CloseLabel, styles.Button, closeAction);
        if (!CanShowContent) return;
        bool root = !(layout.PlayerPage || layout.WorldPage || layout.GenerationPage || layout.PopulationPage);
        float gap = 6f * scale;
        float chipWidth = (width - pad * 2f - gap * 4f) / 5f;
        if (chipWidth <= 1f) return;
        float navY = 52f * scale;
        float navHeight = 40f * scale;
        for (int i = 0; i < NavLabels.Length; i++)
        {
            bool active = i == 0 ? root
                : i == 1 ? layout.PlayerPage
                : i == 2 ? layout.WorldPage
                : i == 3 ? layout.GenerationPage
                : layout.PopulationPage;
            GUIStyle style = active ? styles.NavActiveTab : styles.NavTab;
            ChromeButton(new Rect(pad + i * (chipWidth + gap), navY, chipWidth, navHeight),
                Ellipsize(style, NavLabels[i], chipWidth), style, chips[i]);
        }
    }

    private void ChromeButton(Rect local, string label, GUIStyle style, Action action)
    {
        // 公共控件：纯色底（active 导航金、其余卡片底）+ native 文字；hit 与视觉同用 ImGuiCompat.IntersectRect。
        ModPanelControls.DrawControl(local, label, style,
            style == styles.NavActiveTab ? styles.GoldTexture : styles.CardTexture,
            panelRect.position, new Rect(0f, 0f, panelRect.width, panelRect.height));
        var screen = new Rect(panelRect.x + local.x, panelRect.y + local.y, local.width, local.height);
        Rect clipped = ImGuiCompat.IntersectRect(screen, panelRect);
        if (clipped.width > 0f && clipped.height > 0f) hits.Add(new Hit { Rect = clipped, Action = action });
    }

    // ---- 内容页 ----

    private void DrawPage()
    {
        if (layout.PopulationPage) { MobilePopulation.Draw(this, rowWidth, rowScale); return; }
        if (layout.PlayerPage) { MobilePlayerMenu.Draw(this, rowWidth, rowScale); return; }
        if (layout.VegetationPage) { MobileVegetationMenu.Draw(this, rowWidth, rowScale); return; }
        if (layout.WorldPage) { MobileWorldMenu.Draw(this, rowWidth, rowScale); return; }
        if (layout.GenerationPage) { MobileGenerationMenu.Draw(this, rowWidth, rowScale); return; }
        DrawRoot();
    }

    private void DrawRoot()
    {
        Info(ModPanelControls.PanelSubtitle, "", "");
        Toggle("常驻时间与季节", ModConfig.CalendarEnabled.Value, "顶部显示总天数、整点与季节进度。", CalendarToggle);
        Info("日历状态", MobileCalendar.PanelText, "");
    }

    // ---- 事件（唯一 hotControl 手势；GroupEnd 之后只读 physical 鼠标坐标）----

    private void ProcessEvent(FloatLayout current, Event e)
    {
        Vector2 point = e.mousePosition;
        switch (e.type)
        {
            case EventType.MouseDown:
                if (e.button != 0 || GUIUtility.hotControl != 0 || !current.HitPanelBody(point.x, point.y)) break;
                if (gesture.Begin(point.x, point.y, TryHit(point), viewport.Contains(point), 10f * current.Scale))
                {
                    GUIUtility.hotControl = ownControlId;
                    e.Use();
                }
                break;
            case EventType.MouseDrag:
                if (GUIUtility.hotControl != ownControlId || !gesture.Captured) break;
                gesture.Move(point.x, point.y, scrollMax);
                e.Use();
                break;
            case EventType.MouseUp:
                if (GUIUtility.hotControl != ownControlId || !gesture.Captured) break;
                int action = gesture.End(point.x, point.y, TryHit(point), scrollMax);
                GUIUtility.hotControl = 0;
                e.Use();
                if (action >= 0 && action < hits.Count) hits[action].Action?.Invoke();
                break;
            case EventType.ScrollWheel:
                if (e.delta.y == 0f || !viewport.Contains(point)) break;
                gesture.Wheel(-e.delta.y * WheelStep * current.Scale, scrollMax);
                e.Use();
                break;
        }
    }

    private int TryHit(Vector2 point)
    {
        for (int i = hits.Count - 1; i >= 0; i--)
            if (hits[i].Rect.Contains(point)) return i;
        return -1;
    }

    // ---- 导航/折叠动作（只作用于本面板状态；CancelGesture 清自有 scroll/capture）----

    private void ShowRoot() => SetPage(0);
    private void ShowPlayer() => SetPage(1);
    private void ShowWorld() => SetPage(2);
    private void ShowGeneration() => SetPage(3);
    private void ShowPopulation() => SetPage(4);

    private void SetPage(int page)
    {
        FloatLayout current = layout;
        if (current == null) return;
        bool player = page == 1, world = page == 2, generation = page == 3, population = page == 4;
        bool same = current.PlayerPage == player && current.WorldPage == world
            && current.GenerationPage == generation && current.PopulationPage == population
            && !current.VegetationPage;
        if (same) return;
        current.PlayerPage = player;
        current.WorldPage = world;
        current.GenerationPage = generation;
        current.PopulationPage = population;
        current.VegetationPage = false;
        CancelGesture();
    }

    private void OpenVegetationPage()
    {
        FloatLayout current = layout;
        if (current == null || current.VegetationPage) return;
        current.VegetationPage = true;
        CancelGesture();
    }

    private void CloseVegetationPage()
    {
        FloatLayout current = layout;
        if (current == null || !current.VegetationPage) return;
        current.VegetationPage = false;
        CancelGesture();
    }

    private void Close()
    {
        FloatLayout current = layout;
        if (current != null) current.Expanded = false;
        CancelGesture();
    }


    private static string Ellipsize(GUIStyle style, string text, float maxWidth)
    {
        if (ModPanelStyles.MeasureSize(style, new GUIContent(text)).x <= maxWidth) return text;
        for (int length = text.Length - 1; length > 0; length--)
        {
            string candidate = text.Substring(0, length) + "…";
            if (ModPanelStyles.MeasureSize(style, new GUIContent(candidate)).x <= maxWidth) return candidate;
        }
        return "…";
    }
}
