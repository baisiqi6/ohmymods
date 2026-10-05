using System;
using UnityEngine;

namespace KingdomEnhancedMod
{
    /// <summary>
    /// 设置面板共享样式（PC ModPanel 与 Android 公共面板单一来源，Issue #144）。
    ///
    /// 只创建自有资源：原生皮肤副本（字体链来自调用方传入的 GUI.skin）、1x1 纹理与派生
    /// GUIStyle。不读写 GUI.skin / GUI.matrix / GUI.enabled / GUI.depth 等全局状态——持有者
    /// （PC ModPanel / Android MobileModPanel）负责设置与恢复。SetScale 只改本对象持有的字号
    /// 与内边距：PC 传 1（其 GUI.matrix 已按屏缩放），Android 传 Layout.Scale（identity matrix）。
    ///
    /// Create 为事务式：全部构建成功才提交；失败时销毁已建对象后重抛，缓存不会被半成品污染。
    /// Dispose 只销毁本对象创建的资源，不触碰任何全局对象。
    /// </summary>
    internal sealed class ModPanelStyles : IDisposable
    {
        internal static readonly Color Gold = new Color(0.91f, 0.75f, 0.43f);
        internal static readonly Color Text = new Color(0.94f, 0.94f, 0.91f);
        internal static readonly Color Muted = new Color(0.65f, 0.71f, 0.77f);
        internal static readonly Color PanelBack = new Color(0.065f, 0.08f, 0.105f);
        internal static readonly Color CardBack = new Color(0.105f, 0.13f, 0.165f);
        internal static readonly Color TrackColor = new Color(0.23f, 0.28f, 0.34f);
        internal static readonly Color ThumbColor = new Color(0.74f, 0.64f, 0.43f);
        internal static readonly Color ValueDark = new Color(0.10f, 0.11f, 0.13f);

        // 基准字号与内边距；SetScale(1) 复现 PC 原字号（22/30/17/21/22/19）。
        private const int LabelSize = 22, TitleSize = 30, MutedSize = 17, ValueSize = 21, TabSize = 22, ButtonSize = 19, NavSize = 18, CompactTitleSize = 22;
        private const int LabelPadX = 8, LabelPadY = 2, NavPadX = 4, NavPadY = 2;

        internal GUISkin Skin;
        internal GUIStyle Label;
        internal GUIStyle Title;
        // 窄端派生：换行标题（CalcHeight 真实折行）与 22 号紧凑面板标题；PC 不使用这两个。
        internal GUIStyle NarrowLabel;
        internal GUIStyle CompactTitle;
        internal GUIStyle MutedLabel;
        internal GUIStyle Value;
        internal GUIStyle ValueOn;
        internal GUIStyle Tab;
        internal GUIStyle ActiveTab;
        internal GUIStyle Button;
        internal GUIStyle Card;
        // 窄端导航 chip 变体：同调色板，居中、更小字号（Android 固定外框的分类短行）。
        internal GUIStyle NavTab;
        internal GUIStyle NavActiveTab;
        internal Texture2D BackTexture;
        internal Texture2D CardTexture;
        internal Texture2D GoldTexture;
        internal Texture2D TrackTexture;
        internal Texture2D ThumbTexture;

        private bool disposed;

        private ModPanelStyles() { }

        /// <summary>
        /// 以调用方的原生皮肤为字体链创建整套样式。失败时不留下部分缓存：全部已建对象销毁后重抛。
        /// </summary>
        internal static ModPanelStyles Create(GUISkin source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            GUISkin skin = null;
            Texture2D back = null, cardBack = null, gold = null, track = null, thumb = null;
            try
            {
                skin = UnityEngine.Object.Instantiate(source);
                skin.hideFlags = HideFlags.HideAndDontSave;
                back = Texture(PanelBack);
                cardBack = Texture(CardBack);
                gold = Texture(Gold);
#if !ANDROID
                track = Texture(TrackColor);
                thumb = Texture(ThumbColor);
#endif

                GUIStyle label = Style(skin.label, LabelSize, Text);
                GUIStyle narrowLabel = new GUIStyle(label);
                narrowLabel.wordWrap = true;
                GUIStyle title = Style(skin.label, TitleSize, Gold);
                title.fontStyle = FontStyle.Bold;
                GUIStyle compactTitle = new GUIStyle(title);
                compactTitle.fontSize = CompactTitleSize;
                GUIStyle muted = Style(skin.label, MutedSize, Muted);
                muted.wordWrap = true;
#if ANDROID
                GUIStyle value = Style(skin.label, ValueSize, Gold); // V4 实测：skin.box 克隆自带原生黑背景装饰，Android 文字基底改 label
                value.wordWrap = true;
#else
                GUIStyle value = Style(skin.box, ValueSize, Gold);
#endif
                value.alignment = TextAnchor.MiddleCenter;
#if !ANDROID
                value.normal.background = back;
#endif
                GUIStyle valueOn = new GUIStyle(value);
#if !ANDROID
                valueOn.normal.background = gold;
#endif
                valueOn.normal.textColor = ValueDark;
                valueOn.fontStyle = FontStyle.Bold;
#if ANDROID
                GUIStyle tab = Style(skin.label, TabSize, Muted); // 同上：skin.button 基底的黑装饰背景
#else
                GUIStyle tab = Style(skin.button, TabSize, Muted);
#endif
#if !ANDROID
                tab.normal.background = cardBack;
                tab.hover.background = track;
#endif
                tab.hover.textColor = Text;
#if !ANDROID
                tab.active.background = gold;
#endif
                tab.active.textColor = Color.black;
                GUIStyle activeTab = new GUIStyle(tab);
#if !ANDROID
                activeTab.normal.background = gold;
#endif
                activeTab.normal.textColor = ValueDark;
                activeTab.fontStyle = FontStyle.Bold;
                GUIStyle button = new GUIStyle(tab);
                button.fontSize = ButtonSize;
                GUIStyle card = new GUIStyle(skin.box);
#if !ANDROID
                card.normal.background = cardBack;
#endif
                GUIStyle navTab = new GUIStyle(tab);
                navTab.fontSize = NavSize;
                navTab.alignment = TextAnchor.MiddleCenter;
                GUIStyle navActiveTab = new GUIStyle(activeTab);
                navActiveTab.fontSize = NavSize;
                navActiveTab.alignment = TextAnchor.MiddleCenter;

#if !ANDROID
                // PC slider/scrollbar 皮肤整体只在 PC 生效：Android 端不用这些控件，
                // 且 GUIStyleState 背景写在本 loader 构建已被 strip（Issue #144）。
                skin.horizontalSlider.fixedHeight = 8f;
                WriteOffset(skin.horizontalSlider.margin, 0, 0, 10, 10);
                skin.horizontalSlider.normal.background = track;
                skin.horizontalSliderThumb.fixedWidth = 20f;
                skin.horizontalSliderThumb.fixedHeight = 28f;
                skin.horizontalSliderThumb.normal.background = gold;
                skin.horizontalSliderThumb.hover.background = gold;
                skin.horizontalSliderThumb.active.background = thumb;
                skin.verticalScrollbar.fixedWidth = 16f;
                skin.verticalScrollbar.normal.background = back;
                skin.verticalScrollbarThumb.normal.background = thumb;
                skin.verticalScrollbarThumb.hover.background = gold;
                skin.verticalScrollbarThumb.active.background = gold;
                skin.verticalScrollbarThumb.fixedWidth = 16f;
                skin.verticalScrollbarThumb.fixedHeight = 0f;
                skin.verticalScrollbarThumb.stretchHeight = true;
                WriteOffset(skin.verticalScrollbarThumb.overflow, 0, 0, 0, 0);
#endif

                var styles = new ModPanelStyles();
                styles.Skin = skin;
                styles.Label = label;
                styles.NarrowLabel = narrowLabel;
                styles.Title = title;
                styles.CompactTitle = compactTitle;
                styles.MutedLabel = muted;
                styles.Value = value;
                styles.ValueOn = valueOn;
                styles.Tab = tab;
                styles.ActiveTab = activeTab;
                styles.Button = button;
                styles.Card = card;
                styles.NavTab = navTab;
                styles.NavActiveTab = navActiveTab;
                styles.BackTexture = back;
                styles.CardTexture = cardBack;
                styles.GoldTexture = gold;
                styles.TrackTexture = track;
                styles.ThumbTexture = thumb;
                styles.SetScale(1f);
                return styles;
            }
            catch
            {
                DestroyOwned(skin);
                DestroyOwned(back);
                DestroyOwned(cardBack);
                DestroyOwned(gold);
                DestroyOwned(track);
                DestroyOwned(thumb);
                throw;
            }
        }

        /// <summary>只改本对象持有的字号与内边距；不动任何全局状态。PC 传 1，Android 传 Layout.Scale。</summary>
        internal void SetScale(float scale)
        {
            int F(int baseSize) => Mathf.Max(8, (int)(baseSize * scale));
            int P(int basePad) => Mathf.Max(1, (int)(basePad * scale));
            Label.fontSize = F(LabelSize);
            NarrowLabel.fontSize = F(LabelSize);
            Title.fontSize = F(TitleSize);
            CompactTitle.fontSize = F(CompactTitleSize);
            MutedLabel.fontSize = F(MutedSize);
            Value.fontSize = F(ValueSize);
            ValueOn.fontSize = F(ValueSize);
            Tab.fontSize = F(TabSize);
            ActiveTab.fontSize = F(TabSize);
            Button.fontSize = F(ButtonSize);
            NavTab.fontSize = F(NavSize);
            NavActiveTab.fontSize = F(NavSize);
            int labelPadX = P(LabelPadX), labelPadY = P(LabelPadY);
            WriteOffset(Label.padding, labelPadX, labelPadX, labelPadY, labelPadY);
            WriteOffset(NarrowLabel.padding, labelPadX, labelPadX, labelPadY, labelPadY);
            WriteOffset(Title.padding, labelPadX, labelPadX, labelPadY, labelPadY);
            WriteOffset(CompactTitle.padding, labelPadX, labelPadX, labelPadY, labelPadY);
            WriteOffset(MutedLabel.padding, labelPadX, labelPadX, labelPadY, labelPadY);
            WriteOffset(Value.padding, labelPadX, labelPadX, labelPadY, labelPadY);
            WriteOffset(ValueOn.padding, labelPadX, labelPadX, labelPadY, labelPadY);
            WriteOffset(Tab.padding, labelPadX, labelPadX, labelPadY, labelPadY);
            WriteOffset(ActiveTab.padding, labelPadX, labelPadX, labelPadY, labelPadY);
            WriteOffset(Button.padding, labelPadX, labelPadX, labelPadY, labelPadY);
            int navPadX = P(NavPadX), navPadY = P(NavPadY);
            WriteOffset(NavTab.padding, navPadX, navPadX, navPadY, navPadY);
            WriteOffset(NavActiveTab.padding, navPadX, navPadX, navPadY, navPadY);
        }

        /// <summary>
        /// 共享文本测量：Android 端旧 GUIStyle.CalcSize 的 icall 已被 strip，改用真 native 的
        /// CalcSizeWithConstraints(content, zero)（inf 约束在该实现下结果不一致，不可用）；
        /// PC 端保持原 CalcSize。全部 consumer 只经此一处，无各自绕开。
        /// </summary>
        internal static Vector2 MeasureSize(GUIStyle style, GUIContent content)
        {
#if ANDROID
            return style.CalcSizeWithConstraints(content, Vector2.zero);
#else
            return style.CalcSize(content);
#endif
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            DestroyOwned(Skin);
            DestroyOwned(BackTexture);
            DestroyOwned(CardTexture);
            DestroyOwned(GoldTexture);
            DestroyOwned(TrackTexture);
            DestroyOwned(ThumbTexture);
            Skin = null;
            BackTexture = null;
            CardTexture = null;
            GoldTexture = null;
            TrackTexture = null;
            ThumbTexture = null;
        }

        private static Texture2D Texture(Color color)
        {
            var texture = new Texture2D(1, 1);
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        private static GUIStyle Style(GUIStyle source, int size, Color color)
        {
            var style = new GUIStyle(source);
            style.fontSize = size;
            style.normal.textColor = color;
            style.alignment = TextAnchor.MiddleLeft;
            WriteOffset(style.padding, LabelPadX, LabelPadX, LabelPadY, LabelPadY);
            return style;
        }

        // GUIStyle 的 padding/margin/overflow 整体赋值在 interop 下经 GUIStyle::AssignRectOffset_Injected
        // 桥，该 icall 在当前 loader 构建未注册（设备首启开面板即崩，Issue #144）。改为经 getter 取回
        // 该 style 自有的 RectOffset 后逐字段写同值：字段 setter 不经过缺失桥，写值语义与整体赋值等价。
        // 每个 style 必须各自调 getter 取自己的 RectOffset，不得复用共享实例。
        internal static void WriteOffset(RectOffset offset, int left, int right, int top, int bottom)
        {
            offset.left = left;
            offset.right = right;
            offset.top = top;
            offset.bottom = bottom;
        }

        private static void DestroyOwned(UnityEngine.Object value)
        {
            if (value != null) UnityEngine.Object.Destroy(value);
        }
    }
}
