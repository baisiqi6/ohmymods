// 2026-10-17 用户要求恢复皇冠窃贼（CrownStealer）原生速度。
// 本文件在非 ANDROID 构建中不产生任何类型/补丁：PC（Windows/Linux）、Mac 私有 wrapper 与
// il2cpp/*.cs glob 构建都会编到空文件，因此 Mod 不写下面六个水平量，速度全部由原生逻辑负责；
// 不做反乘、逐帧覆写或对象池补偿，也不改伤害/攻击节奏/其他敌人。
// 已运行或池中的旧实例按游戏重启后的原生 prefab 数值工作。
//
// Android 分线保留 Issue196 用户明确要求的、默认 OFF 的独立开关（Android probe 显式编译本文件），
// 其行为与本改动前一致；Android 冻结源 pin 随本文件更新并重建 probe 验证。

#if ANDROID
using System;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// [仅 Android] CrownStealer（皇冠窃贼）移动/跳跃水平速度与冲量 ×0.75
/// （Issue196 用户授权功能，Android 独立开关默认 OFF）。
///
/// 只改这六个水平量：walkSpeed、runSpeed、jumpSpeed、chargeSpeed、
/// wallJumpForce.x、chargeJumpForce.x。两个 Vector2 的 y（跳跃竖直力）逐值原样保留；
/// 跳跃 y、攻击间隔、索敌/攻击范围、伤害与其他敌人都没有改动。
/// Vector2 先取局部副本再改 x 后赋回：interop 成员不能原地写结构体子成员（CS1612）。
///
/// PC/Windows/Mac 主线已按 2026-10-17 用户要求退役本补丁（见文件头）；此处只服务 Android。
///
/// 为什么挂 Awake 前缀而不是 OnEnable：Unity 对每个实例只调用一次 Awake，对象池
/// 复生只重跑 OnEnable，因此这里乘一次不会随池复用累乘，也不需要按实例记账/归还。
/// 已知边界：已经存活或停在池里的实例不会因总开关热切换而恢复原值；
/// 开关只在实例创建（Awake）时生效，重进游戏（重建对象池）后按新开关状态生效。
///
/// 2.4.0 签名验证（Operator 2026-09-30 核对 BepInEx/interop/Assembly-CSharp.dll）：
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
        // Android 分线独立开关（Issue196）：总开关开启但本项关闭时保持原生数值。
        if (ModConfig.CrownStealerSlowdownEnabled?.Value != true) return;

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
#endif
