using HarmonyLib;

namespace KingdomEnhancedMod;

/// <summary>
/// 弩箭塔 Bolt 飞行速度提升。Ballista.TrackTarget 与 Bolt.Launch 都读取同一个
/// BoltData.ShootForce：在 getter 结果上做绝对倍率，能同时保持瞄准角计算和实际
/// 发射冲量一致；不改共享 ScriptableObject 字段，因此重复读档/换岛不会累乘。
/// </summary>
[HarmonyPatch(typeof(BoltData), nameof(BoltData.ShootForce), MethodType.Getter)]
internal static class PatchWorld_BallistaBolt
{
    private const float ShootForceMultiplier = 1.25f;

    [HarmonyPostfix]
    private static void Postfix(ref float __result)
    {
        if (ModConfig.Enabled?.Value == true)
            __result *= ShootForceMultiplier;
    }
}
