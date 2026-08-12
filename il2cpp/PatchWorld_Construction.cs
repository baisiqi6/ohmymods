using System;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 快速建造（ModConfig.FastBuild）：建筑约 2 秒建成。
/// InitializeBuild 前把 _autoBuildRate 设为 50f。
///
/// 2.4.0 签名验证（E:/QQ/.../BepInEx/interop/Assembly-CSharp.dll）：
///   - ConstructionBuildingComponent.InitializeBuild() 存在 ✓ public virtual void
///   - ConstructionBuildingComponent._autoBuildRate      存在 ✓ public float
///   结论：无漂移。Mono 用反射写私有字段 _autoBuildRate，2.4.0 该字段为 public，可直接赋值，
///   且无需 FieldInfo 缓存。
/// </summary>
[HarmonyPatch(typeof(ConstructionBuildingComponent), nameof(ConstructionBuildingComponent.InitializeBuild))]
public static class PatchWorld_Construction
{
    [HarmonyPrefix]
    public static void Prefix(ConstructionBuildingComponent __instance)
    {
        if (!ModConfig.Enabled.Value || !ModConfig.FastBuild.Value) return;

        try
        {
            __instance._autoBuildRate = 50f;
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
    }
}
