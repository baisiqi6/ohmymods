using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

internal static class HeavyShieldNativeHooks
{
    // Actual 2.4 GameAssembly SHA256 CD8C2B...: all are single-slot long native entries.
    internal static readonly MethodBase DamageMethod = AccessTools.Method(typeof(Damageable), nameof(Damageable.ReceiveDamage),
        new[] { typeof(int), typeof(GameObject), typeof(DamageSource), typeof(Vector2) }); // RVA 0x9bcf00
    internal static readonly MethodBase TrollIntentMethod = AccessTools.Method(typeof(Troll), "TryDamage", new[] { typeof(Damageable) }); // 0x7bbf90
    internal static readonly MethodBase TrollImpactMethod = AccessTools.Method(typeof(Troll), "ApplyCollisionDamage", new[] { typeof(Damageable) }); // 0x7b6f60
    internal static readonly MethodBase ArrowHitMethod = AccessTools.Method(typeof(Arrow), "HitObject", new[] { typeof(GameObject), typeof(bool) }); // 0x4c8470
    internal static readonly MethodBase ArrowDamageMethod = AccessTools.Method(typeof(Arrow), "TryDamage", new[] { typeof(Damageable) }); // 0x4c98e0
    internal static readonly MethodBase DemoteMethod = AccessTools.Method(typeof(Character), "Demote", Type.EmptyTypes); // token100666133, RVA0x97e520, 672B, sameSlots1
    internal static bool Installed => HeavyShieldHookQuery.Has(DamageMethod, typeof(HeavyShieldReceiveDamage))
        && HeavyShieldHookQuery.Has(TrollIntentMethod, typeof(HeavyShieldTrollIntent))
        && HeavyShieldHookQuery.Has(TrollImpactMethod, typeof(HeavyShieldTrollImpact))
        && HeavyShieldHookQuery.Has(ArrowHitMethod, typeof(HeavyShieldArrowHit))
        && HeavyShieldHookQuery.Has(ArrowDamageMethod, typeof(HeavyShieldArrowDamage))
        && HeavyShieldHookQuery.Has(DemoteMethod, typeof(HeavyShieldNativeDemote), requirePostfix: true);
}

[HarmonyPatch]
internal static class HeavyShieldNativeDemote
{
    private static MethodBase TargetMethod() => HeavyShieldNativeHooks.DemoteMethod;
    private static void Prefix(Character __instance, out HeavyShieldRuntime.NativeDemoteCapture __state)
        => __state = HeavyShieldRuntime.BeginNativeDemote(__instance);
    private static void Postfix(Character __result, HeavyShieldRuntime.NativeDemoteCapture __state)
        => HeavyShieldRuntime.ObserveNativeDemoteReturn(__state, __result);
    private static Exception Finalizer(Exception __exception, HeavyShieldRuntime.NativeDemoteCapture __state)
    { __state?.Dispose(); return __exception; }
}

[HarmonyPatch]
internal static class HeavyShieldReceiveDamage
{
    private static MethodBase TargetMethod() => HeavyShieldNativeHooks.DamageMethod;
    private static bool Prefix(Damageable __instance, int __0, GameObject __1, DamageSource __2,
        out HeavyShieldCombat.DamageScope __state)
    {
        __state = HeavyShieldCombat.EnterDamage();
        return !HeavyShieldCombat.TryBlock(__instance, __0, __1, __2);
    }
    private static Exception Finalizer(Damageable __instance, Exception __exception, HeavyShieldCombat.DamageScope __state)
    {
        try { HeavyShieldCombat.ObserveDamageEnd(__instance); }
        finally { __state?.Dispose(); }
        return __exception;
    }
}
[HarmonyPatch]
internal static class HeavyShieldTrollIntent
{
    private static MethodBase TargetMethod() => HeavyShieldNativeHooks.TrollIntentMethod;
    private static void Prefix(Troll __instance, Damageable __0, out IDisposable __state)
        => __state = HeavyShieldCombat.EnterTrollIntent(__instance, __0);
    private static Exception Finalizer(Exception __exception, IDisposable __state)
    { __state?.Dispose(); return __exception; }
}
[HarmonyPatch]
internal static class HeavyShieldTrollImpact
{
    private static MethodBase TargetMethod() => HeavyShieldNativeHooks.TrollImpactMethod;
    private static void Prefix(Troll __instance, Damageable __0, out IDisposable __state)
        => __state = HeavyShieldCombat.EnterTroll(__instance, __0);
    private static Exception Finalizer(Exception __exception, IDisposable __state)
    { __state?.Dispose(); return __exception; }
}
[HarmonyPatch]
internal static class HeavyShieldArrowHit
{
    private static MethodBase TargetMethod() => HeavyShieldNativeHooks.ArrowHitMethod;
    private static void Prefix(Arrow __instance, out IDisposable __state) => __state = HeavyShieldCombat.EnterArrowHit(__instance);
    private static Exception Finalizer(Exception __exception, IDisposable __state)
    { __state?.Dispose(); return __exception; }
}
[HarmonyPatch]
internal static class HeavyShieldArrowDamage
{
    private static MethodBase TargetMethod() => HeavyShieldNativeHooks.ArrowDamageMethod;
    private static void Prefix(Arrow __instance, Damageable __0, out IDisposable __state)
        => __state = HeavyShieldCombat.EnterArrowDamage(__instance, __0);
    private static Exception Finalizer(Exception __exception, IDisposable __state)
    { __state?.Dispose(); return __exception; }
}
