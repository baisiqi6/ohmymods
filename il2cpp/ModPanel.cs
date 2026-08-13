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
/// - 中文界面：Font.CreateDynamicFontFromOSFont("Microsoft YaHei") 防 IMGUI 默认字体方块
/// - 挂载：DontDestroyOnLoad 空 GameObject + 原生 ClassInjector 注册（ScaleRegistryHolder 同款模式）
///
/// IL2CPP 注意：自定义 MonoBehaviour 必须 ClassInjector 注册 + IntPtr 构造（docs §5.3）。
/// </summary>
public class ModPanel : MonoBehaviour
{
    private static bool _shown;
    private static GUISkin _skin;
    private static Rect _window = new Rect(20, 60, 620, 560);
    private static Vector2 _scroll;

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
            _skin = ScriptableObject.Instantiate(GUI.skin);
            var font = TryLoadCjkFont();
            if (font != null) _skin.font = font;
        }
        GUI.skin = _skin;
        GUILayout.BeginArea(_window, "Kingdom Enhanced Mod", GUI.skin.window);
        _scroll = GUILayout.BeginScrollView(_scroll);
        DrawControls();
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    /// <summary>
    /// 加载游戏中文字体（IL2CPP 裁剪了 Font.CreateDynamicFontFromOSFont——"Method unstripping failed"）。
    /// 游戏本地化 UI 用像素字体（日志特征 'Zpix'），从中文字体资源里选第一个可用的。
    /// </summary>
    private static Font TryLoadCjkFont()
    {
        try
        {
            var fonts = Resources.LoadAll<Font>("");
            for (int i = 0; i < fonts.Length; i++)
            {
                var f = fonts[i];
                if (f == null) continue;
                string n = f.name.ToLower();
                if (n.Contains("zpix") || n.Contains("cjk") || n.Contains("chinese") || n.Contains("han") || n.Contains("yahei"))
                {
                    return f;
                }
            }
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogWarning("[Panel] CJK font load failed: " + e.Message);
        }
        return null;  // 找不到则用默认字体（英文/方块兜底）
    }

    private static void DrawControls()
    {
        GUILayout.BeginVertical();

        GUILayout.Space(6);
        GUILayout.Label("总开关 Enabled", GUILayout.Height(24));
        ModConfig.Enabled.Value = GUILayout.Toggle(ModConfig.Enabled.Value, " 启用 mod（关闭后全部走原版逻辑）", GUILayout.Height(28));

        GUILayout.Space(12);
        GUILayout.Label("无限金币 InfiniteMoney", GUILayout.Height(24));
        ModConfig.InfiniteMoney.Value = GUILayout.Toggle(ModConfig.InfiniteMoney.Value, " 开启后玩家金币用不完", GUILayout.Height(28));

        GUILayout.Space(12);
        GUILayout.Label("君主移动速度 SpeedMultiplier: " + ModConfig.SpeedMultiplier.Value + "x", GUILayout.Height(24));
        ModConfig.SpeedMultiplier.Value = Mathf.RoundToInt(GUILayout.HorizontalSlider(ModConfig.SpeedMultiplier.Value, 1, 5, GUILayout.Height(26)));

        GUILayout.Space(12);
        GUILayout.Label("快速建造 FastBuild", GUILayout.Height(24));
        ModConfig.FastBuild.Value = GUILayout.Toggle(ModConfig.FastBuild.Value, " 建筑约 2 秒建成", GUILayout.Height(28));

        GUILayout.Space(12);
        GUILayout.Label("地图大小 MapSizeMultiplier: " + ModConfig.MapSizeMultiplier.Value.ToString("0.0") + "x", GUILayout.Height(24));
        ModConfig.MapSizeMultiplier.Value = GUILayout.HorizontalSlider(ModConfig.MapSizeMultiplier.Value, 1f, 5f, GUILayout.Height(26));

        GUILayout.Space(12);
        GUILayout.Label("怪物数量 EnemyCountMultiplier: " + ModConfig.EnemyCountMultiplier.Value.ToString("0.0") + "x", GUILayout.Height(24));
        ModConfig.EnemyCountMultiplier.Value = GUILayout.HorizontalSlider(ModConfig.EnemyCountMultiplier.Value, 1f, 5f, GUILayout.Height(26));

        GUILayout.Space(12);
        GUILayout.Label("怪物时间线 EnemyTimelineSpeed: " + ModConfig.EnemyTimelineSpeed.Value.ToString("0.0") + "x", GUILayout.Height(24));
        ModConfig.EnemyTimelineSpeed.Value = GUILayout.HorizontalSlider(ModConfig.EnemyTimelineSpeed.Value, 1f, 5f, GUILayout.Height(26));

        GUILayout.Space(16);
        if (GUILayout.Button("关闭面板（Ctrl+F10 / F5）", GUILayout.Height(36)))
        {
            _shown = false;
        }

        GUILayout.EndVertical();
    }
}
