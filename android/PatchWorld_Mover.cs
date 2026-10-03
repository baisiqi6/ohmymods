// Adapted from il2cpp/PatchWorld_Mover.cs; desktop source is unchanged.
// 旧 ThreadStatic lease/reentrancy 补偿已按设备原生诊断删除：native-diagnostic/trace.log
// 三个入口原生地址互异（0x…BC50/FA60/FAD0，folded=False）且 9/9 maxDepth=1，证明单次设速
// 不会级联进入其它入口。ScalePlayerSpeed 是唯一注册入口，由 Operator 在 Probe.cs 对
// SetSpeed(float)/SetSpeed(float,int)/SetSpeedToGoal(float) 显式注册为 prefix。
using Mover = Il2Cpp.Mover;
using System;
using UnityEngine;

namespace KingdomEnhancedMod;

public static class PatchWorld_Mover
{
    private const float MaxSpeedCap = 15f;

    internal static void ScalePlayerSpeed(Mover __instance, ref float moveSpeed)
    {
        if (!ModConfig.Enabled.Value || ModConfig.SpeedMultiplier.Value <= 1) return;
        if (moveSpeed <= 0f || float.IsNaN(moveSpeed) || float.IsInfinity(moveSpeed)) return;

        try
        {
            Player player = __instance.GetComponent<Player>();
            if (player == null || !player.hasLocalAuthority || !OptionalQoLScope.IsActive || !OptionalQoLScope.IsCurrent(player)) return;

            moveSpeed = Mathf.Min(moveSpeed * ModConfig.SpeedMultiplier.Value, MaxSpeedCap);
        }
        catch (Exception e)
        {
            MelonLoader.MelonLogger.Error(e);
        }
    }
}
