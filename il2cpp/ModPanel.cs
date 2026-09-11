using System;
using BepInEx.Configuration;
using UnityEngine;
using Il2CppInterop.Runtime.Injection;

namespace KingdomEnhancedMod;

/// <summary>F5 / Ctrl+F10 settings panel. Uses only a private copy of Unity's loaded font/skin.</summary>
public class ModPanel : MonoBehaviour
{
    private static bool _shown;
    private static GUISkin _skin;
    private static GUIStyle _title, _label, _muted, _value, _tab, _activeTab, _button, _card;
    private static Texture2D _back, _cardBack, _gold, _track, _thumb;
    private static Vector2 _scroll;
    private static int _category;
    private static readonly string[] Categories = { "王国", "人口", "世界", "战斗" };
    private static readonly Color Gold = new Color(0.91f, 0.75f, 0.43f);
    private static readonly Color Text = new Color(0.94f, 0.94f, 0.91f);
    private static readonly Color Muted = new Color(0.65f, 0.71f, 0.77f);
    private const float CardHeight = 122f;

    public ModPanel(IntPtr ptr) : base(ptr) { }

    public static void EnsureCreated()
    {
        if (!ClassInjector.IsTypeRegisteredInIl2Cpp(typeof(ModPanel)))
            ClassInjector.RegisterTypeInIl2Cpp(typeof(ModPanel));
        var go = new GameObject("KingdomEnhancedMod_Panel");
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        go.AddComponent<ModPanel>();
        KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[Panel] ModPanel created (Ctrl+F10 / F5 to toggle)");
    }

    private void Update()
    {
        // Shortcuts first so a HUD failure can never swallow F5/Ctrl+F10/Esc handling.
        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        if ((ctrl && Input.GetKeyDown(KeyCode.F10)) || Input.GetKeyDown(KeyCode.F5))
            _shown = !_shown;
        else if (_shown && Input.GetKeyDown(KeyCode.Escape))
            _shown = false;
        try { CalendarHud.Tick(); }
        catch { /* CalendarHud backs off internally; input toggles have already been handled. */ }
    }

    private static bool _faultLogged;

    private void OnGUI()
    {
        if (!_shown)
        {
            CalendarHud.Draw();
            return;
        }
        GUISkin savedSkin = GUI.skin;
        Color savedColor = GUI.color;
        Color savedBackground = GUI.backgroundColor;
        Color savedContent = GUI.contentColor;
        Matrix4x4 savedMatrix = GUI.matrix;
        bool savedEnabled = GUI.enabled;
        bool savedChanged = GUI.changed;
        try
        {
            EnsureStyles();
            GUI.skin = _skin;
            GUI.color = GUI.backgroundColor = GUI.contentColor = Color.white;
            GUI.enabled = true;
            // At 1280x720 this keeps 22px text and a 672px panel; smaller screens scale together.
            float scale = Mathf.Min(1f, Mathf.Min(Screen.width / 1120f, Screen.height / 720f));
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
            float canvasWidth = Screen.width / scale;
            float canvasHeight = Screen.height / scale;
            float width = Mathf.Min(1040f, canvasWidth - 48f);
            float height = Mathf.Min(820f, canvasHeight - 48f);
            Rect panel = new Rect((canvasWidth - width) / 2f, (canvasHeight - height) / 2f, width, height);
            GUI.BeginGroup(panel);
            try { DrawPanel(width, height); }
            finally { GUI.EndGroup(); }
        }
        catch (Exception ex)
        {
            // Close instead of erroring on every GUI event; F5 still reopens afterwards.
            _shown = false;
            if (!_faultLogged)
            {
                _faultLogged = true;
                KingdomEnhancedPlugin.Instance?.LogSource.LogError("[Panel] OnGUI failed, panel closed: " + ex);
            }
        }
        finally
        {
            GUI.skin = savedSkin;
            GUI.color = savedColor;
            GUI.backgroundColor = savedBackground;
            GUI.contentColor = savedContent;
            GUI.matrix = savedMatrix;
            GUI.enabled = savedEnabled;
            GUI.changed = savedChanged;
        }
    }

    private static void EnsureStyles()
    {
        if (_skin != null && _label != null && _button != null && _card != null) return;
        // Build fully into locals and commit only at the end: assigning _skin first would let a
        // partial failure poison the cache (skin present, styles null) and break every retry.
        GUISkin skin = null;
        Texture2D back = null, cardBack = null, gold = null, track = null, thumb = null;
        try
        {
            skin = UnityEngine.Object.Instantiate(GUI.skin);
            skin.hideFlags = HideFlags.HideAndDontSave;
            back = Texture(new Color(0.065f, 0.08f, 0.105f));
            cardBack = Texture(new Color(0.105f, 0.13f, 0.165f));
            gold = Texture(Gold);
            track = Texture(new Color(0.23f, 0.28f, 0.34f));
            thumb = Texture(new Color(0.74f, 0.64f, 0.43f));
            GUIStyle label = Style(skin.label, 22, Text);
            GUIStyle title = Style(skin.label, 30, Gold);
            title.fontStyle = FontStyle.Bold;
            GUIStyle muted = Style(skin.label, 17, Muted);
            muted.wordWrap = true;
            GUIStyle value = Style(skin.box, 21, Gold);
            value.alignment = TextAnchor.MiddleCenter;
            value.normal.background = back;
            GUIStyle tab = Style(skin.button, 22, Muted);
            tab.normal.background = cardBack;
            tab.hover.background = track;
            tab.hover.textColor = Text;
            tab.active.background = gold;
            tab.active.textColor = Color.black;
            GUIStyle activeTab = new GUIStyle(tab);
            activeTab.normal.background = gold;
            activeTab.normal.textColor = new Color(0.10f, 0.11f, 0.13f);
            activeTab.fontStyle = FontStyle.Bold;
            GUIStyle button = new GUIStyle(tab);
            button.fontSize = 19;
            GUIStyle card = new GUIStyle(skin.box);
            card.normal.background = cardBack;
            skin.horizontalSlider.fixedHeight = 8f;
            skin.horizontalSlider.margin = new RectOffset(0, 0, 10, 10);
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
            skin.verticalScrollbarThumb.overflow = new RectOffset(0, 0, 0, 0);
            _skin = skin; _back = back; _cardBack = cardBack; _gold = gold; _track = track; _thumb = thumb;
            _label = label; _title = title; _muted = muted; _value = value; _tab = tab;
            _activeTab = activeTab; _button = button; _card = card;
        }
        catch
        {
            UnityEngine.Object.Destroy(skin);
            UnityEngine.Object.Destroy(back); UnityEngine.Object.Destroy(cardBack);
            UnityEngine.Object.Destroy(gold); UnityEngine.Object.Destroy(track); UnityEngine.Object.Destroy(thumb);
            throw;
        }
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
        style.padding = new RectOffset(8, 8, 2, 2);
        return style;
    }

    private static void DrawPanel(float width, float height)
    {
        ImGuiCompat.DrawTexture(new Rect(0, 0, width, height), _back);
        ImGuiCompat.DrawTexture(new Rect(0, 0, width, 3), _gold);
        GUI.Label(new Rect(24, 18, 520, 42), "王国 · 增强设置", _title);
        GUI.Label(new Rect(26, 62, 560, 28), "KINGDOM ENHANCED  /  调整你的王国", _muted);
        int stashed = BankAssistantCoordinator.GetStashedCoinsForPanel();
        GUI.Box(new Rect(width - 334, 27, 230, 44),
            stashed < 0 ? "银行 · 未就绪" : "银行 · " + stashed + " 币", _value);
        if (GUI.Button(new Rect(width - 84, 27, 58, 44), "关闭", _button)) _shown = false;

        float tabWidth = (width - 72f) / 4f;
        for (int i = 0; i < Categories.Length; i++)
        {
            if (GUI.Button(new Rect(24 + i * (tabWidth + 8), 110, tabWidth, 46),
                    Categories[i], i == _category ? _activeTab : _tab) && _category != i)
            {
                _category = i;
                _scroll = Vector2.zero;
            }
        }

        float viewHeight = height - 224f;
        int cards = _category == 0 || _category == 3 ? 4 : (_category == 2 ? 3 : 2);
        float contentHeight = cards * (CardHeight + 12f);
        Rect viewport = new Rect(24, 176, width - 48, viewHeight);
        Rect content = new Rect(0, 0, width - 74, Mathf.Max(viewHeight, contentHeight));
        _scroll = GUI.BeginScrollView(viewport, _scroll, content, false, true);
        try { DrawControls(content.width); }
        finally { GUI.EndScrollView(); }
        GUI.Label(new Rect(26, height - 40, width - 52, 28),
            "修改自动保存  ·  F5 / Ctrl+F10 开关面板  ·  Esc 关闭", _muted);
    }

    private static void DrawControls(float width)
    {
        float y = 0;
        switch (_category)
        {
            case 0:
                Toggle(ref y, width, "启用增强 Mod", ModConfig.Enabled, "关闭后恢复原版逻辑。");
                Toggle(ref y, width, "无限金币", ModConfig.InfiniteMoney, "立即生效 · 君主支付不再消耗金币。");
                IntegerSlider(ref y, width, "君主移动速度", ModConfig.SpeedMultiplier, 1, 5, "倍", "移动时生效。");
                Toggle(ref y, width, "快速建造", ModConfig.FastBuild, "建造时生效 · 建筑约 2 秒建成。");
                break;
            case 1:
                IntegerSlider(ref y, width, "乞丐刷新间隔", ModConfig.BeggarSpawnIntervalSeconds, 1, 120, "秒",
                    "约 0.5 秒内应用，重新计时；每次补 1 人。原生回退最短约 6 秒。");
                IntegerSlider(ref y, width, "每座乞丐帐篷上限", ModConfig.BeggarCampCapacity, 1, 20, "人",
                    "仅影响后续补员 · 调低或重新读档都不会删除已有乞丐。");
                break;
            case 2:
                Toggle(ref y, width, "常驻时间与银行", ModConfig.ShowCalendarHud,
                    "显示总天数、整点、季节进度和银行金币；关闭此项恢复原本界面。");
                FloatSlider(ref y, width, "地图大小", ModConfig.MapSizeMultiplier, 1, 5, false,
                    "生成新地图时生效。");
                FloatSlider(ref y, width, "箭塔基底密度", ModConfig.TowerSpotMultiplier, 1, 4, false,
                    "重新载入地图时生效 · 1 倍为原生密度。");
                break;
            case 3:
                FloatSlider(ref y, width, "每波怪物数量", ModConfig.EnemyCountMultiplier, 1, 5, false,
                    "后续怪物波次生成时生效。");
                FloatSlider(ref y, width, "怪物时间线推进", ModConfig.EnemyTimelineSpeed, 1, 5, false,
                    "后续进攻计算时生效 · 倍率越高，敌军成长越快。");
                FloatSlider(ref y, width, "法杖神器冷却", ModConfig.StaffCooldownMultiplier, 0.2f, 1, true,
                    "当前 " + (30f * ModConfig.StaffCooldownMultiplier.Value).ToString("0.##") + " 秒 / 原生 30 秒 · 使用时生效。");
                FloatSlider(ref y, width, "坐骑技能冷却", ModConfig.SteedCooldownMultiplier, 0.2f, 1, true,
                    "使用时生效 · 原生冷却因坐骑而异。");
                break;
        }
    }

    private static void Card(float y, float width, string title, string value, string help)
    {
        GUI.Box(new Rect(0, y, width, CardHeight), GUIContent.none, _card);
        GUI.Label(new Rect(14, y + 10, width - 210, 34), title, _label);
        GUI.Box(new Rect(width - 180, y + 12, 162, 32), value, _value);
        GUI.Label(new Rect(16, y + 88, width - 32, 28), help, _muted);
    }

    private static void Toggle(ref float y, float width, string title, ConfigEntry<bool> config, string help)
    {
        Card(y, width, title, config.Value ? "已开启" : "已关闭", help);
        if (GUI.Button(new Rect(22, y + 51, 156, 31), config.Value ? "点击关闭" : "点击开启",
                config.Value ? _activeTab : _button))
            config.Value = !config.Value;
        y += CardHeight + 12;
    }

    private static void IntegerSlider(ref float y, float width, string title, ConfigEntry<int> config,
        int min, int max, string unit, string help)
    {
        Card(y, width, title, config.Value + " " + unit, help);
        float raw = Slider(y, width, config.Value, min, max, min + unit, max + unit, out bool interacted);
        if (interacted && !Mathf.Approximately(raw, config.Value))
        {
            int next = Mathf.Clamp(Mathf.RoundToInt(raw), min, max);
            if (next != config.Value) config.Value = next;
        }
        y += CardHeight + 12;
    }

    private static void FloatSlider(ref float y, float width, string title, ConfigEntry<float> config,
        float min, float max, bool percent, string help)
    {
        string value = percent ? PercentText(config.Value) : config.Value.ToString("0.##") + " 倍";
        Card(y, width, title, value, help);
        float raw = Slider(y, width, config.Value, min, max,
            percent ? PercentText(min) : min + "倍", percent ? PercentText(max) : max + "倍", out bool interacted);
        // In particular, the existing 0.375 CD remains untouched on open/Layout/Repaint.
        if (interacted && !Mathf.Approximately(raw, config.Value))
        {
            float next = Mathf.Clamp(Mathf.Round(raw * 20f) / 20f, min, max);
            if (!Mathf.Approximately(next, config.Value)) config.Value = next;
        }
        y += CardHeight + 12;
    }

    private static float Slider(float y, float width, float value, float min, float max,
        string minText, string maxText, out bool interacted)
    {
        GUI.Label(new Rect(18, y + 50, 76, 30), minText, _muted);
        GUI.Label(new Rect(width - 85, y + 50, 76, 30), maxText, _muted);
        EventType eventType = Event.current.type;
        bool input = eventType == EventType.MouseDown || eventType == EventType.MouseDrag
            || eventType == EventType.KeyDown;
        bool previousChanged = GUI.changed;
        GUI.changed = false;
        float result = GUI.HorizontalSlider(new Rect(96, y + 59, width - 196, 28), value, min, max);
        interacted = input && GUI.changed;
        GUI.changed |= previousChanged;
        return result;
    }

    private static string PercentText(float multiplier) => (multiplier * 100f).ToString("0.#") + "%";
}
