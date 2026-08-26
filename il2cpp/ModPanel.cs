using System;
using UnityEngine;
using Il2CppInterop.Runtime.Injection;

namespace KingdomEnhancedMod;

/// <summary>
/// 游戏内设置面板（补 Mono 版 F5 菜单的 IL2CPP 缺口，2026-08-13 用户需求）。
///
/// - 呼出：Ctrl+F10 或 F5（两者皆可）
/// - 显示所有设置当前值（状态可见）+ 即时调整（ConfigEntry.Value 直改，
///   patch 每次读取 → 即时生效；InfiniteMoney 已有 SettingChanged 接线）
/// - 字体：默认 IMGUI skin 私有拷贝放大字号（22px），不注入字体（坑16）；窗口 880x820
/// - 挂载：DontDestroyOnLoad 空 GameObject + 原生 ClassInjector 注册（ScaleRegistryHolder 同款模式）
///
/// IL2CPP 注意：自定义 MonoBehaviour 必须 ClassInjector 注册 + IntPtr 构造（docs §5.3）。
/// </summary>
public class ModPanel : MonoBehaviour
{
    private static bool _shown;
    private static GUISkin _skin;
    // 2026-08-26 用户反馈面板太小字太小：窗口放大 + skin 私有拷贝放大字号
    private static Rect _window = new Rect(40, 50, 880, 820);
    private static Vector2 _scroll;

    private const int PanelFontSize = 22;
    private const int LabelH = 34;
    private const int ToggleH = 40;
    private const int SliderH = 38;
    private const int ButtonH = 52;

    public ModPanel(IntPtr ptr) : base(ptr) { }

    public static void EnsureCreated()
    {
        if (!ClassInjector.IsTypeRegisteredInIl2Cpp(typeof(ModPanel)))
        {
            ClassInjector.RegisterTypeInIl2Cpp(typeof(ModPanel));
        }

        var go = new GameObject("KingdomEnhancedMod_Panel");
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        go.AddComponent<ModPanel>();
        KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[Panel] ModPanel created (Ctrl+F10 / F5 to toggle)");
    }

    private void Update()
    {
        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        if (ctrl && Input.GetKeyDown(KeyCode.F10))
        {
            _shown = !_shown;
        }
        else if (Input.GetKeyDown(KeyCode.F5))
        {
            _shown = !_shown;
        }
    }

    private void OnGUI()
    {
        if (!_shown) return;

        if (_skin == null)
        {
            // IL2CPP 下把 TextCore/Zpix 静态字体塞给 IMGUI 会在每次 Repaint
            // 重试字体转换并刷 "Unable to find/load font"。固定复用 Unity 已创建的
            // 默认 IMGUI skin 的私有拷贝（Instantiate），只在拷贝上放大字号——
            // 字体本身仍是游戏已加载的默认 IMGUI 字体（坑16：不注入字体），
            // 也不会污染游戏其他 IMGUI 消费方共享的全局 skin。
            _skin = UnityEngine.Object.Instantiate(GUI.skin);
            if (_skin != null)
            {
                _skin.label.fontSize = PanelFontSize;
                _skin.toggle.fontSize = PanelFontSize;
                _skin.button.fontSize = PanelFontSize;
                _skin.box.fontSize = PanelFontSize;
                _skin.window.fontSize = PanelFontSize + 2;
                _skin.textField.fontSize = PanelFontSize;
                _skin.horizontalSlider.fontSize = PanelFontSize;
                _skin.horizontalSliderThumb.fontSize = PanelFontSize;
            }
        }
        if (_skin != null) GUI.skin = _skin;
        GUILayout.BeginArea(_window, "Kingdom Enhanced Mod", GUI.skin.window);
        _scroll = GUILayout.BeginScrollView(_scroll);
        DrawControls();
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private static void DrawControls()
    {
        GUILayout.BeginVertical();

        GUILayout.Space(8);
        GUILayout.Label("总开关 Enabled", GUILayout.Height(LabelH));
        ModConfig.Enabled.Value = GUILayout.Toggle(ModConfig.Enabled.Value, " 启用 mod（关闭后全部走原版逻辑）", GUILayout.Height(ToggleH));

        GUILayout.Space(20);
        GUILayout.Label("无限金币 InfiniteMoney", GUILayout.Height(LabelH));
        ModConfig.InfiniteMoney.Value = GUILayout.Toggle(ModConfig.InfiniteMoney.Value, " 开启后玩家金币用不完", GUILayout.Height(ToggleH));

        GUILayout.Space(20);
        GUILayout.Label("君主移动速度 SpeedMultiplier: " + ModConfig.SpeedMultiplier.Value + "x", GUILayout.Height(LabelH));
        ModConfig.SpeedMultiplier.Value = Mathf.RoundToInt(GUILayout.HorizontalSlider(ModConfig.SpeedMultiplier.Value, 1, 5, GUILayout.Height(SliderH)));

        GUILayout.Space(20);
        GUILayout.Label("快速建造 FastBuild", GUILayout.Height(LabelH));
        ModConfig.FastBuild.Value = GUILayout.Toggle(ModConfig.FastBuild.Value, " 建筑约 2 秒建成", GUILayout.Height(ToggleH));

        GUILayout.Space(20);
        GUILayout.Label("地图大小 MapSizeMultiplier: " + ModConfig.MapSizeMultiplier.Value.ToString("0.0") + "x", GUILayout.Height(LabelH));
        ModConfig.MapSizeMultiplier.Value = GUILayout.HorizontalSlider(ModConfig.MapSizeMultiplier.Value, 1f, 5f, GUILayout.Height(SliderH));

        GUILayout.Space(20);
        GUILayout.Label("怪物数量 EnemyCountMultiplier: " + ModConfig.EnemyCountMultiplier.Value.ToString("0.0") + "x", GUILayout.Height(LabelH));
        ModConfig.EnemyCountMultiplier.Value = GUILayout.HorizontalSlider(ModConfig.EnemyCountMultiplier.Value, 1f, 5f, GUILayout.Height(SliderH));

        GUILayout.Space(20);
        GUILayout.Label("怪物时间线 EnemyTimelineSpeed: " + ModConfig.EnemyTimelineSpeed.Value.ToString("0.0") + "x", GUILayout.Height(LabelH));
        ModConfig.EnemyTimelineSpeed.Value = GUILayout.HorizontalSlider(ModConfig.EnemyTimelineSpeed.Value, 1f, 5f, GUILayout.Height(SliderH));

        // ---- CD 倍率滑块（2026-08-24 需求：神器/坐骑各一个，0.2~1.0，最多缩到 1/5）----
        // 滑块离散化到 5% 步进（Round(v*20)/20）：IMGUI 每帧 Repaint 会有亚像素抖动，
        // 直接写回会把微小浮点噪声写脏 cfg 并反复触发 SettingChanged；吸附步进后
        // 仅在值真正变化时写回（Mathf.Approximately 守门），cfg 安静、回调低频。
        GUILayout.Space(20);
        GUILayout.Label("神器CD倍率 " + PercentText(ModConfig.StaffCooldownMultiplier.Value)
            + "（" + (30f * ModConfig.StaffCooldownMultiplier.Value).ToString("0.#") + "秒/30秒）", GUILayout.Height(LabelH));
        float staffCd = SnapToPercentStep(GUILayout.HorizontalSlider(ModConfig.StaffCooldownMultiplier.Value, 0.2f, 1f, GUILayout.Height(SliderH)));
        if (!Mathf.Approximately(staffCd, ModConfig.StaffCooldownMultiplier.Value))
        {
            ModConfig.StaffCooldownMultiplier.Value = staffCd;
        }

        GUILayout.Space(20);
        // 坐骑原生 CD 因坐骑而异（prefab 序列化），无法像神器那样给固定秒数，
        // 只展示倍率；1.0 时标注"原生"。
        GUILayout.Label("坐骑技能CD倍率 " + PercentText(ModConfig.SteedCooldownMultiplier.Value)
            + (Mathf.Approximately(ModConfig.SteedCooldownMultiplier.Value, 1f) ? "（原生）" : ""), GUILayout.Height(LabelH));
        float steedCd = SnapToPercentStep(GUILayout.HorizontalSlider(ModConfig.SteedCooldownMultiplier.Value, 0.2f, 1f, GUILayout.Height(SliderH)));
        if (!Mathf.Approximately(steedCd, ModConfig.SteedCooldownMultiplier.Value))
        {
            ModConfig.SteedCooldownMultiplier.Value = steedCd;
        }

        GUILayout.Space(20);
        if (GUILayout.Button("关闭面板（Ctrl+F10 / F5）", GUILayout.Height(ButtonH)))
        {
            _shown = false;
        }

        GUILayout.EndVertical();
    }

    /// <summary>倍率 → 百分比文案："0.375"→"37.5%"，"1.0"→"100%"（整数不带小数点）。</summary>
    private static string PercentText(float multiplier)
    {
        return (multiplier * 100f).ToString("0.#") + "%";
    }

    /// <summary>滑块值吸附到 5% 步进（0.20/0.25/.../1.00）：Round(v*20)/20。</summary>
    private static float SnapToPercentStep(float value)
    {
        return Mathf.Round(value * 20f) / 20f;
    }
}
