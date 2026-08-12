using System;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 君主移动速度倍率（ModConfig.SpeedMultiplier）。
///
/// 实现原理（沿用 Mono 版 Patch_Mover v3 的结论）：
///   - 玩家（Player）是 Rewired 输入直接控制，每帧调 Mover.SetSpeed(walkSpeed/runSpeed, direction)
///     （不走 SetGoal）；SetSpeed 直接写 _moveSpeed（GoalMode.Off，不走 Lerp）。
///   - 在 SetSpeed / SetSpeedToGoal 前用 prefix 缩放 moveSpeed 参数即真实生效；幂等（每次只乘一次）。
///   - 只对 Player 生效（单位速度不受影响），speed &lt;= 0 不动，上限 MaxSpeedCap。
/// IL2CPP 变化：Mono 的 ConditionalWeakTable&lt;Mover,Player&gt; 缓存弃用（Il2Cpp 对象缓存表不可靠），
///   改为每次 prefix 内联 GetComponent&lt;Player&gt;()（每帧一次，开销可忽略）。
///
/// 2.4.0 签名验证（E:/QQ/.../BepInEx/interop/Assembly-CSharp.dll）：
///   - Mover.SetSpeed(float moveSpeed)          存在 ✓ public
///   - Mover.SetSpeed(float moveSpeed, int dir) 存在 ✓ public
///   - Mover.SetSpeedToGoal(float moveSpeed)    存在 ✓ public
///   - Mover.Update()                           存在 ✓ private
///   - Mover._moveSpeed                         存在 ✓ public float
///   结论：Mover 无漂移。此前 get_type_members.py 报"仅构造函数"是该脚本 bug——其正则未匹配
///   `unsafe` 关键字（Cpp2IL 桩方法均带 unsafe），漏报全部方法；实际 Update/SetSpeed 均健在。
///   （详见 notes-world.md「Mover 漂移结论」）
/// </summary>
[HarmonyPatch(typeof(Mover))]
public static class PatchWorld_Mover
{
    private const float MaxSpeedCap = 15f;

    [HarmonyPatch(nameof(Mover.SetSpeed), new[] { typeof(float) })]
    [HarmonyPrefix]
    public static void SetSpeed_Prefix(Mover __instance, ref float moveSpeed)
        => ScalePlayerSpeed(__instance, ref moveSpeed);

    [HarmonyPatch(nameof(Mover.SetSpeed), new[] { typeof(float), typeof(int) })]
    [HarmonyPrefix]
    public static void SetSpeedDir_Prefix(Mover __instance, ref float moveSpeed)
        => ScalePlayerSpeed(__instance, ref moveSpeed);

    [HarmonyPatch(nameof(Mover.SetSpeedToGoal))]
    [HarmonyPrefix]
    public static void SetSpeedToGoal_Prefix(Mover __instance, ref float moveSpeed)
        => ScalePlayerSpeed(__instance, ref moveSpeed);

    private static void ScalePlayerSpeed(Mover __instance, ref float moveSpeed)
    {
        if (!ModConfig.Enabled.Value || ModConfig.SpeedMultiplier.Value <= 1) return;
        if (moveSpeed <= 0f) return;

        try
        {
            Player player = __instance.GetComponent<Player>();
            if (player == null) return;

            moveSpeed = Mathf.Min(moveSpeed * ModConfig.SpeedMultiplier.Value, MaxSpeedCap);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
    }
}
