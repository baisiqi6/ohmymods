using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 临时只读诊断：确认塔位弩手是否满足原生 ShouldShootEnemy 条件，以及
/// 满足后是否真正进入 FireArrow。只记录带 CrossbowmanMarker 且已在塔位的实例，
/// 不改任何攻击状态；用于定位“塔上弩手不攻击”的现场分支。
/// </summary>
internal static class PatchRoles_CrossbowmanTowerDiag
{
    private const float FireLogInterval = 1f;
    private static readonly Dictionary<int, float> LastFireLog = new();

    internal static bool IsEnabled(Archer archer)
    {
        return ModConfig.Enabled.Value
            && archer != null
            && archer.gameObject != null
            && archer.inGuardSlot
            && PatchRoles_Crossbowman.IsCrossbowman(archer);
    }

    internal static void LogEnter(Archer archer)
    {
        if (!IsEnabled(archer)) return;
        try
        {
            int id = archer.GetInstanceID();
            Scanner scanner = archer._enemyScanner;
            GameObject closest = scanner != null ? scanner.GetClosest() : null;
            ArrowAttack active = archer.ActiveArrowAttack;
            ArrowAttack native = archer._arrowAttack;
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                "[CrossbowTowerDiag] ENTER id=" + id
                + " mode=" + archer._desiredAttackMode + "/" + archer._attackMode
                + " cooldown=" + archer._cooldown.ToString("F2")
                + " harmless=" + archer.harmless
                + " target=" + (closest != null ? closest.name : "<none>")
                + " activeSO=" + (active != null ? active.name : "<null>")
                + " activeIsNative=" + (active != null && native != null && active.Pointer == native.Pointer)
                + " scanner=" + (scanner != null ? scanner.range.ToString("F1") : "<null>")
                + " towerRange=" + archer.towerShootRange);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[CrossbowTowerDiag/state] " + e);
        }
    }

    internal static void LogFire(Archer archer)
    {
        if (!IsEnabled(archer)) return;
        try
        {
            int id = archer.GetInstanceID();
            float now = Time.time;
            if (LastFireLog.TryGetValue(id, out float last) && now - last < FireLogInterval)
                return;
            LastFireLog[id] = now;
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                "[CrossbowTowerDiag] " + (archer.harmless ? "BLOCKED_HARMLESS" : "FIRE") + " id=" + id
                + " target=" + (archer._shootingTarget != null ? archer._shootingTarget.name : "<none>")
                + " activeSO=" + (archer.ActiveArrowAttack != null ? archer.ActiveArrowAttack.name : "<null>"));
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[CrossbowTowerDiag/fire] " + e);
        }
    }

    internal static void LogBoltHit(Arrow arrow, Damageable damageable, bool accepted)
    {
        if (!ModConfig.Enabled.Value || arrow == null || arrow.gameObject == null) return;
        if (!arrow.gameObject.name.StartsWith("KEM_CrossbowBolt", StringComparison.Ordinal)) return;
        try
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                "[CrossbowTowerDiag] BOLT_HIT accepted=" + accepted
                + " target=" + (damageable != null ? damageable.gameObject.name : "<none>")
                + " source=" + arrow._damageSource
                + " damage=" + arrow.hitDamage
                + " archer=" + (arrow.archer != null ? arrow.archer.name : "<none>"));
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[CrossbowTowerDiag/hit] " + e);
        }
    }
}

[HarmonyPatch(typeof(Archer), "EnterGuardSlot")]
internal static class Archer_EnterGuardSlot_CrossbowTowerDiag_Patch
{
    [HarmonyPostfix]
    private static void Postfix(Archer __instance)
    {
        PatchRoles_CrossbowmanTowerDiag.LogEnter(__instance);
    }
}

[HarmonyPatch(typeof(Arrow), "TryDamage")]
internal static class Arrow_TryDamage_CrossbowTowerDiag_Patch
{
    [HarmonyPostfix]
    private static void Postfix(Arrow __instance, Damageable damageable, ref bool __result)
    {
        PatchRoles_CrossbowmanTowerDiag.LogBoltHit(__instance, damageable, __result);
    }
}

[HarmonyPatch(typeof(Archer), "FireArrow")]
internal static class Archer_FireArrow_CrossbowTowerDiag_Patch
{
    [HarmonyPostfix]
    private static void Postfix(Archer __instance)
    {
        PatchRoles_CrossbowmanTowerDiag.LogFire(__instance);
    }
}
