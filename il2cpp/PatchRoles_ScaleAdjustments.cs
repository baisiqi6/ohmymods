using System;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 状态化角色缩放：只通过现有 ScaleRegistry 维护 y，保留 x 的朝向符号。
/// Ninja 的 isFisher=false 是夜行衣攻击形态，true 是白天钓鱼形态。
/// </summary>
[HarmonyPatch]
public static class NinjaStyleScale_Patch
{
    private const float AttackStyleY = 1.1f;
    private const float FisherStyleY = 1f;

    [HarmonyPatch(typeof(Ninja), nameof(Ninja.OnStyleSwap))]
    [HarmonyPostfix]
    private static void OnStyleSwap_Postfix(Ninja __instance, int isFisher)
    {
        Apply(__instance, isFisher == 1);
    }

    /// <summary>
    /// Remote peers receive the style through AnimationSync.SetAnimation.  The
    /// native method has already consumed ByteBuffer and written the Animator by
    /// postfix time, so read the final Animator bool rather than touching the
    /// shared network buffer or the authority-only _isFisher field.
    /// </summary>
    [HarmonyPatch(typeof(Ninja), nameof(Ninja.SetAnimation))]
    [HarmonyPostfix]
    private static void SetAnimation_Postfix(Ninja __instance, int animCode)
    {
        if (__instance == null || animCode != Ninja.APIsFisher || __instance._animator == null) return;
        Apply(__instance, __instance._animator.GetBool(Ninja.APIsFisher));
    }

    [HarmonyPatch(typeof(Ninja), nameof(Ninja.Persistent_IBehaviour_ApplyData))]
    [HarmonyPostfix]
    private static void ApplyData_Postfix(Ninja __instance)
    {
        if (__instance != null) Apply(__instance, __instance._isFisher);
    }

    [HarmonyPatch(typeof(Ninja), nameof(Ninja.DeserializeFromData))]
    [HarmonyPostfix]
    private static void DeserializeFromData_Postfix(Ninja __instance)
    {
        if (__instance != null) Apply(__instance, __instance._isFisher);
    }

    private static void Apply(Ninja ninja, bool isFisher)
    {
        if (!ModConfig.Enabled.Value || ninja == null || ninja.gameObject == null) return;

        try
        {
            float targetY = isFisher ? FisherStyleY : AttackStyleY;
            Vector3 scale = ninja.transform.localScale;
            scale.y = targetY;
            ninja.transform.localScale = scale;
            ScaleRegistryHolder.Register(ninja.GetComponent<Mover>(), targetY);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
    }
}

/// <summary>
/// 希腊 Banker y=1.075；其他世界只在同一对象曾被本补丁注册过时恢复基准 1。
/// </summary>
[HarmonyPatch(typeof(Banker), nameof(Banker.OnEnable))]
public static class GreeceBankerScale_Patch
{
    private const float GreeceBankerY = 1.075f;

    [HarmonyPostfix]
    private static void OnEnable_Postfix(Banker __instance)
    {
        if (!ModConfig.Enabled.Value || __instance == null || __instance.gameObject == null) return;

        try
        {
            Mover mover = __instance._mover != null ? __instance._mover : __instance.GetComponent<Mover>();
            if (mover == null) return;

            bool isGreece = BiomeHolder.Inst != null
                && BiomeHolder.Inst.BiomeIndex == BiomeHolder.GreeceBiomeIndex;
            float targetY;
            if (isGreece)
            {
                targetY = GreeceBankerY;
            }
            else
            {
                float registeredY;
                if (!ScaleRegistryHolder.TryGet(mover, out registeredY)
                    || Mathf.Abs(registeredY - GreeceBankerY) > 0.0001f) return;
                targetY = 1f;
            }

            Vector3 scale = __instance.transform.localScale;
            scale.y = targetY;
            __instance.transform.localScale = scale;
            ScaleRegistryHolder.Register(mover, targetY);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
    }
}

/// <summary>
/// 酿酒师与号角隐士固定为 y=1.15，马厩隐士固定为 y=1.10，
/// 弩箭塔隐士固定为 y=1.20，骑士塔隐士固定为 y=1.05，
/// 火焰隐士固定为 y=1.25；其他隐士保持原样。
/// </summary>
[HarmonyPatch]
public static class HermitScale_Patch
{
    private const float BakerHermitY = 1.15f;
    private const float HornHermitY = 1.15f;
    private const float HorseHermitY = 1.10f;
    private const float BallistaHermitY = 1.20f;
    private const float KnightHermitY = 1.05f;
    private const float FireHermitY = 1.25f;

    [HarmonyPatch(typeof(Hermit), nameof(Hermit.OnEnable))]
    [HarmonyPostfix]
    private static void OnEnable_Postfix(Hermit __instance)
    {
        if (!ModConfig.Enabled.Value || __instance == null || __instance.gameObject == null) return;

        try
        {
            float targetY;
            if (__instance.Type == Hermit.HermitType.Baker)
            {
                targetY = BakerHermitY;
            }
            else if (__instance.Type == Hermit.HermitType.Horn)
            {
                targetY = HornHermitY;
            }
            else if (__instance.Type == Hermit.HermitType.Horse)
            {
                targetY = HorseHermitY;
            }
            else if (__instance.Type == Hermit.HermitType.Ballista)
            {
                targetY = BallistaHermitY;
            }
            else if (__instance.Type == Hermit.HermitType.Knight)
            {
                targetY = KnightHermitY;
            }
            else if (__instance.Type == Hermit.HermitType.Fire)
            {
                targetY = FireHermitY;
            }
            else
            {
                return;
            }

            Mover mover = __instance.mover != null ? __instance.mover : __instance.GetComponent<Mover>();
            if (mover == null) return;

            Vector3 scale = __instance.transform.localScale;
            scale.y = targetY;
            __instance.transform.localScale = scale;
            ScaleRegistryHolder.Register(mover, targetY);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
    }

    [HarmonyPatch(typeof(Hermit), nameof(Hermit.OnDestroy))]
    [HarmonyPostfix]
    private static void OnDestroy_Postfix(Hermit __instance)
    {
        if (__instance == null) return;

        try
        {
            if (__instance.Type != Hermit.HermitType.Baker
                && __instance.Type != Hermit.HermitType.Horn
                && __instance.Type != Hermit.HermitType.Horse
                && __instance.Type != Hermit.HermitType.Ballista
                && __instance.Type != Hermit.HermitType.Knight
                && __instance.Type != Hermit.HermitType.Fire) return;

            Mover mover = __instance.mover != null ? __instance.mover : __instance.GetComponent<Mover>();
            ScaleRegistryHolder.Unregister(mover);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
    }
}
