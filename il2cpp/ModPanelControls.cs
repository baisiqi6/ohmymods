using UnityEngine;

namespace KingdomEnhancedMod
{
    /// <summary>
    /// 设置面板共享纯绘制（PC ModPanel 与 Android 公共面板单一来源，Issue #144）。
    ///
    /// - DrawPanelSurface：面板底板与金色顶线（纹理经 ImGuiCompat，绕开 interop 的
    ///   GUI.DrawTexture 桩）。
    /// - DrawWideCard：PC 原卡片几何（scale=1 时与旧 ModPanel.Card 逐字一致：标题宽
    ///   width-210、值框 162x32、卡高 122）；调用方提供坐标与高度，调用方的配置回调留在原位。
    /// - MeasureRow/RowHeight/DrawRow：Android 窄端卡片形态。每行每帧只 Measure 一次
    ///   （显式 RowMetrics 传给绘制，不做双份 Calc）；标题用换行样式实测真实折行，帮助行
    ///   MutedLabel CalcHeight；值徽章宽度夹取到卡内，触控高度不小于 48*scale。
    ///
    /// 本文件不登记手势、不持有全局 GUI 状态、不使用 GUI.Button。
    /// </summary>
    internal static class ModPanelControls
    {
        internal const string PanelTitle = "王国 · 增强设置";
        internal const string PanelSubtitle = "KINGDOM ENHANCED  /  调整你的王国";
        internal const string CloseLabel = "关闭";
        internal const float RowGap = 8f; // 行间距（乘以 scale 由 RowHeight 的调用方保持）

#if !ANDROID
        internal static void DrawPanelSurface(Rect rect, ModPanelStyles styles)
        {
            ImGuiCompat.DrawTexture(rect, styles.BackTexture);
            ImGuiCompat.DrawTexture(new Rect(rect.x, rect.y, rect.width, 3f), styles.GoldTexture);
        }
#endif

#if !ANDROID
        /// <summary>PC 卡片：调用方给出 Rect，几何与原 ModPanel.Card 一致（scale=1）。</summary>
        internal static void DrawWideCard(float x, float y, float width, float height,
            string title, string value, string help, ModPanelStyles styles, float scale)
        {
            GUI.Box(new Rect(x, y, width, height), GUIContent.none, styles.Card);
            GUI.Label(new Rect(x + 14f * scale, y + 10f * scale, width - 210f * scale, 34f * scale),
                title, styles.Label);
            GUI.Box(new Rect(x + width - 180f * scale, y + 12f * scale, 162f * scale, 32f * scale),
                value, styles.Value);
            GUI.Label(new Rect(x + 16f * scale, y + 88f * scale, width - 32f * scale, 28f * scale),
                help, styles.MutedLabel);
        }
#endif

#if ANDROID
        /// <summary>Android 面板底板：显式 group origin + local clip（native Group 不裁 matrix 背景，产生处交集）。</summary>
        internal static void DrawPanelSurface(Rect rect, ModPanelStyles styles, Vector2 origin, Rect clip)
        {
            ImGuiCompat.DrawSolidTexture(rect, styles.BackTexture, origin, clip);
            ImGuiCompat.DrawSolidTexture(new Rect(rect.x, rect.y, rect.width, 3f), styles.GoldTexture, origin, clip);
        }

        /// <summary>Android 公共控件：纯色 1x1 底 + native 文字；文字由调用方当前 Group 裁剪，无 Box 装饰。</summary>
        internal static void DrawControl(Rect rect, string label, GUIStyle style, Texture2D background, Vector2 origin, Rect localClip)
        {
            ImGuiCompat.DrawSolidTexture(rect, background, origin, localClip);
            GUI.Label(rect, label, style);
        }
#endif

#if ANDROID
        /// <summary>Android 窄端行的实测尺寸；由 MeasureRow 一次算出，RowHeight/DrawRow 复用。</summary>
        internal readonly struct RowMetrics
        {
            internal readonly float PadX, PadY, Gap, TitleWidth, TitleHeight, ValueWidth, ValueHeight, TopHeight, HelpHeight, MinHeight;

            internal RowMetrics(float padX, float padY, float gap, float titleWidth, float titleHeight,
                float valueWidth, float valueHeight, float topHeight, float helpHeight, float minHeight)
            {
                PadX = padX; PadY = padY; Gap = gap;
                TitleWidth = titleWidth; TitleHeight = titleHeight;
                ValueWidth = valueWidth; ValueHeight = valueHeight;
                TopHeight = topHeight; HelpHeight = helpHeight; MinHeight = minHeight;
            }
        }

        internal static RowMetrics MeasureRow(float width, string title, string value, string help,
            bool badge, ModPanelStyles styles, float scale)
        {
            float padX = 12f * scale;
            float padY = 9f * scale;
            float gap = 6f * scale;
            float inner = Mathf.Max(1f, width - padX * 2f);
            var valueContent = new GUIContent(value);
            var valueSize = ModPanelStyles.MeasureSize(styles.Value, valueContent);
            float valueWidth = badge
                ? Mathf.Max(72f * scale, Mathf.Ceil(valueSize.x) + 16f * scale)
                : Mathf.Max(48f * scale, Mathf.Ceil(valueSize.x) + 8f * scale);
            // 最长值也不允许徽章越出卡片边界（极小宽度下优先保证 inside bounds）。
            valueWidth = Mathf.Min(valueWidth, inner);
            float titleWidth = Mathf.Max(0f, inner - valueWidth - 10f * scale);
            float titleHeight = styles.NarrowLabel.CalcHeight(new GUIContent(title), Mathf.Max(0f, titleWidth));
            float valueHeight = badge
                ? Mathf.Max(30f * scale, Mathf.Ceil(valueSize.y) + 8f * scale)
                : Mathf.Max(24f * scale, Mathf.Ceil(valueSize.y));
            float topHeight = Mathf.Max(titleHeight, valueHeight);
            float helpHeight = string.IsNullOrEmpty(help) ? 0f : styles.MutedLabel.CalcHeight(new GUIContent(help), inner);
            return new RowMetrics(padX, padY, gap, titleWidth, titleHeight, valueWidth, valueHeight,
                topHeight, helpHeight, 48f * scale);
        }

        /// <summary>行高（含 48*scale 最小触控高度）；调用方另加 RowGap*scale 行距。</summary>
        internal static float RowHeight(in RowMetrics m)
            => Mathf.Ceil(Mathf.Max(m.MinHeight, m.PadY * 2f + m.TopHeight + (m.HelpHeight > 0f ? m.Gap + m.HelpHeight : 0f)));

        /// <summary>按 MeasureRow 的几何绘制窄端行：卡片底/徽章底为 1x1 纯色 primitive，
        /// 文字 native Label；origin 为该 group 局部 (0,0) 的绝对位置，localClip 为同空间裁剪矩形。</summary>
        internal static void DrawRow(float x, float y, float width, float height,
            string title, string value, string help, bool badge, bool valueOn, in RowMetrics m, ModPanelStyles styles,
            Vector2 origin, Rect localClip)
        {
            GUIStyle valueStyle = valueOn ? styles.ValueOn : styles.Value;
            ImGuiCompat.DrawSolidTexture(new Rect(x, y, width, height), styles.CardTexture, origin, localClip);
            GUI.Label(new Rect(x + m.PadX, y + m.PadY, m.TitleWidth, m.TitleHeight), title, styles.NarrowLabel);
            var valueRect = new Rect(x + width - m.PadX - m.ValueWidth, y + m.PadY, m.ValueWidth, m.ValueHeight);
            if (badge)
            {
                DrawControl(valueRect, value, valueStyle, valueOn ? styles.GoldTexture : styles.BackTexture, origin, localClip);
            }
            else GUI.Label(valueRect, value, valueStyle);
            if (m.HelpHeight > 0f)
                GUI.Label(new Rect(x + m.PadX, y + m.PadY + m.TopHeight + m.Gap,
                    Mathf.Max(1f, width - m.PadX * 2f), m.HelpHeight), help, styles.MutedLabel);
        }
#endif
    }
}
