using System;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// CrownStealer（皇冠窃贼）移动/跳跃水平速度与冲量 ×0.75（用户 2026-09-30 授权，降速 25%）。
///
/// 只改这六个水平量：walkSpeed、runSpeed、jumpSpeed、chargeSpeed、
/// wallJumpForce.x、chargeJumpForce.x。两个 Vector2 的 y（跳跃竖直力）逐值原样保留；
/// 跳跃 y、攻击间隔、索敌/攻击范围、伤害与其他敌人都没有改动。
/// Vector2 先取局部副本再改 x 后赋回：interop 成员不能原地写结构体子成员（CS1612）。
///
/// 为什么挂 Awake 前缀而不是 OnEnable：Unity 对每个实例只调用一次 Awake，对象池
/// 复生只重跑 OnEnable，因此这里乘一次不会随池复用累乘，也不需要按实例记账/归还。
/// ModConfig.Enabled 由插件 Init 在 PatchAll 之前初始化，可直接读 .Value。前缀先于
/// 原生 Awake 体执行，Operator 已核对 2.4 的 Awake/OnEnable 都不会重写这六个成员。
///
/// 已知边界：已经存活或停在池里的实例不会因总开关热切换而恢复原值；
/// 开关只在实例创建（Awake）时生效，重进游戏（重建对象池）后按新开关状态生效。
///
/// 2.4.0 签名验证（Operator 2026-09-30 核对 BepInEx/interop/Assembly-CSharp.dll，
/// 按同一结论实现，不再重复大范围侦查）：
///   - CrownStealer.Awake：存在，public virtual；
///   - walkSpeed / runSpeed / jumpSpeed / chargeSpeed：存在（float）；
///   - wallJumpForce / chargeJumpForce：存在（Vector2）；
///   - Awake/OnEnable 均不重写以上六个成员。
/// </summary>
[HarmonyPatch(typeof(CrownStealer), nameof(CrownStealer.Awake))]
public static class PatchRoles_CrownStealer
{
    /// <summary>用户要求的降速比例：原值 ×0.75。</summary>
    private const float SpeedScale = 0.75f;

    [HarmonyPrefix]
    public static void Awake_Prefix(CrownStealer __instance)
    {
        if (__instance == null || !ModConfig.Enabled.Value) return;

        try
        {
            __instance.walkSpeed *= SpeedScale;
            __instance.runSpeed *= SpeedScale;
            __instance.jumpSpeed *= SpeedScale;
            __instance.chargeSpeed *= SpeedScale;

            Vector2 wallJumpForce = __instance.wallJumpForce;
            wallJumpForce.x *= SpeedScale;
            __instance.wallJumpForce = wallJumpForce;

            Vector2 chargeJumpForce = __instance.chargeJumpForce;
            chargeJumpForce.x *= SpeedScale;
            __instance.chargeJumpForce = chargeJumpForce;
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
    }
}
