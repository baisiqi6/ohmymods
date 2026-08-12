using System;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 岛宽度放大（ModConfig.MapSizeMultiplier）。
/// 在 Level.GenerateInternal 前临时放大 LevelConfig.minLevelWidth（记录原值），生成后恢复原值
/// （防污染共享的 LevelConfig 资产）。岛越宽 → LevelBlock 内容越多 → 总宽度/地面/世界边界自动连锁。
///
/// 2.4.0 签名验证（E:/QQ/.../BepInEx/interop/Assembly-CSharp.dll）：
///   - Level.GenerateInternal(LevelConfig config, int seed) 存在 ✓ private void
///   - LevelConfig.minLevelWidth                            存在 ✓ public int
///   结论：轻微漂移——Mono 是 GenerateInternal(LevelConfig config)，2.4.0 增加 int seed 参数。
///   挂载用字符串名 "GenerateInternal"（private，nameof 不可访问），prefix/postfix 无需声明 seed。
///   minLevelWidth 由（Mono 的 public 字段）仍为 public int，可直接读写。
/// </summary>
[HarmonyPatch(typeof(Level), "GenerateInternal")]
public static class PatchWorld_Level
{
    private static int _originalWidth;
    private static bool _modified;

    [HarmonyPrefix]
    public static void GenerateInternal_Prefix(Level __instance, LevelConfig config)
    {
        if (!ModConfig.Enabled.Value || ModConfig.MapSizeMultiplier.Value <= 1f || config == null) return;

        try
        {
            _originalWidth = config.minLevelWidth;
            _modified = true;
            config.minLevelWidth = (int)(_originalWidth * ModConfig.MapSizeMultiplier.Value);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
            _modified = false;
        }
    }

    [HarmonyPostfix]
    public static void GenerateInternal_Postfix(Level __instance, LevelConfig config)
    {
        if (!_modified || config == null) return;
        _modified = false;

        try
        {
            config.minLevelWidth = _originalWidth;
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
    }
}
