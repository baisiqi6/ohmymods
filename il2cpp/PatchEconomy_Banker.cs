using System;
using Coatsink.Common;
using UnityEngine;
using HarmonyLib;

namespace KingdomEnhancedMod;

/// <summary>
/// 银行家增强：共享存款（PlayerPrefs 跨岛）、扫描范围 300、10Hz 扫描、90% 收集、
/// 移动加速 ×3、夜间不休息、去重防 NetID 903 冲突、残留克隆清理。
/// 迁移自 Mono Patch_Banker.cs（UMM + Harmony 1.2）。
///
/// 2.4.0 签名验证结果（get_type_members.py 核对 interop Assembly-CSharp.dll）：
///   - Banker.Awake(): private void —— 存在。
///   - Banker.HandleOnDayStart(): private void —— 存在。
///   - Banker.Update(): private void —— 存在。
///   - Banker.DropOff(): private IEnumerator —— 存在（Mono 为 void/协程，postfix 仅用 __instance，签名兼容）。
///   - Banker.Hide(): private IEnumerator —— 存在（同上）。
///   - Banker.FinaliseEmerge(): private IEnumerator —— 存在（同上）。
///   - Banker.Payout(): private IEnumerator —— 存在（同上）。
///   - Banker.OpenCastleDoor(): private void —— 存在。
///   - Banker.ShouldHide(): private bool —— 存在。
///   - 字段（interop 暴露为 public 属性，替代 Mono 反射）：_wallet(Wallet)、_stashedCoins(int)、
///     coinScanRange(float)、_coinScanner(Scanner)、coinGatherTargetPercentage(float)、
///     walkSpeed(float)、runSpeed(float) —— 全部存在。
///   - Scanner.range / rangeBehind / _interval —— 存在。
///   - Castle.SetStash(int): public void —— 存在。
///
/// 迁移说明：
///   - 所有字段访问由 Mono 反射改为 interop public 属性直接访问。
///   - FindObjectsOfType&lt;Banker&gt;() 返回 Il2CppArrayBase&lt;Banker&gt;（非 Banker[]），用 var + .Length/foreach。
///   - 共享存款 PlayerPrefs 键名沿用 MyMod_SharedBankStash。
/// </summary>
[HarmonyPatch(typeof(Banker))]
public static class PatchEconomy_Banker
{
    private const string SHARED_STASH_KEY = "MyMod_SharedBankStash";
    private static int sharedStash = -1;
    private static int _bankerCheckFrame = 0;

    // === 共享存款 ===

    private static int LoadSharedStash()
    {
        if (!PlayerPrefs.HasKey(SHARED_STASH_KEY)) return 500;
        return PlayerPrefs.GetInt(SHARED_STASH_KEY);
    }

    private static void SaveSharedStash(int value)
    {
        PlayerPrefs.SetInt(SHARED_STASH_KEY, value);
        PlayerPrefs.Save();
    }

    private static void EnsureLoaded()
    {
        if (sharedStash < 0)
        {
            sharedStash = LoadSharedStash();
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[Economy] Banker shared stash loaded: " + sharedStash);
        }
    }

    private static void SyncStash(Banker __instance)
    {
        EnsureLoaded();
        __instance._stashedCoins = sharedStash;
    }

    private static void UpdateSharedStash(Banker __instance)
    {
        EnsureLoaded();
        int currentStash = __instance._stashedCoins;
        if (currentStash > sharedStash)
        {
            sharedStash = currentStash;
            SaveSharedStash(sharedStash);
        }
    }

    // === Postfix 补丁 ===

    [HarmonyPatch(typeof(Banker), nameof(Banker.FinaliseEmerge))]
    [HarmonyPostfix]
    public static void FinaliseEmerge_Postfix(Banker __instance)
    {
        if (!ModConfig.Enabled.Value) return;
        try { SyncStash(__instance); }
        catch (Exception e) { KingdomEnhancedPlugin.Instance?.LogSource.LogError(e); }
    }

    [HarmonyPatch(typeof(Banker), nameof(Banker.HandleOnDayStart))]
    [HarmonyPostfix]
    public static void HandleOnDayStart_Postfix(Banker __instance)
    {
        if (!ModConfig.Enabled.Value) return;
        try { SyncStash(__instance); }
        catch (Exception e) { KingdomEnhancedPlugin.Instance?.LogSource.LogError(e); }
    }

    [HarmonyPatch(typeof(Banker), nameof(Banker.DropOff))]
    [HarmonyPostfix]
    public static void DropOff_Postfix(Banker __instance)
    {
        if (!ModConfig.Enabled.Value) return;
        try { UpdateSharedStash(__instance); }
        catch (Exception e) { KingdomEnhancedPlugin.Instance?.LogSource.LogError(e); }
    }

    [HarmonyPatch(typeof(Banker), nameof(Banker.Hide))]
    [HarmonyPostfix]
    public static void Hide_Postfix(Banker __instance)
    {
        if (!ModConfig.Enabled.Value) return;
        try { UpdateSharedStash(__instance); }
        catch (Exception e) { KingdomEnhancedPlugin.Instance?.LogSource.LogError(e); }
    }

    [HarmonyPatch(typeof(Banker), nameof(Banker.Payout))]
    [HarmonyPostfix]
    public static void Payout_Postfix(Banker __instance)
    {
        if (!ModConfig.Enabled.Value) return;
        try
        {
            EnsureLoaded();
            sharedStash = __instance._stashedCoins;
            SaveSharedStash(sharedStash);
        }
        catch (Exception e) { KingdomEnhancedPlugin.Instance?.LogSource.LogError(e); }
    }

    [HarmonyPatch(typeof(Banker), nameof(Banker.OpenCastleDoor))]
    [HarmonyPostfix]
    public static void OpenCastleDoor_Postfix(Banker __instance)
    {
        if (!ModConfig.Enabled.Value) return;
        try
        {
            EnsureLoaded();
            __instance._stashedCoins = sharedStash;
            Castle castle = SingletonMonoBehaviour<Managers>.Inst.kingdom.castle;
            if (castle != null) castle.SetStash(sharedStash);
        }
        catch (Exception e) { KingdomEnhancedPlugin.Instance?.LogSource.LogError(e); }
    }

    // === Awake - 去重 + 扫描范围 + 移速 ===

    /// <summary>
    /// 关键：Banker.Awake 硬编码 RegisterObject(903, Dynamic)。多个 Banker 实例同时 Awake 时
    /// NetID 903 冲突 → 网络层崩溃 → 原生池丢失。Prefix 检测：场景已有其他 Banker 时销毁自己并跳过 Awake。
    /// </summary>
    [HarmonyPatch(typeof(Banker), nameof(Banker.Awake))]
    [HarmonyPrefix]
    public static bool Awake_Prefix(Banker __instance)
    {
        if (!ModConfig.Enabled.Value) return true;
        try
        {
            var allBankers = UnityEngine.Object.FindObjectsOfType<Banker>();
            string names = "";
            foreach (var b in allBankers)
            {
                if (b != null) names += "[" + b.gameObject.name + "]";
            }
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                "[Economy] Banker.Awake_Prefix: current=" + __instance.gameObject.name
                + " total=" + allBankers.Length + " all=" + names);

            foreach (var b in allBankers)
            {
                if (b == null || b == __instance) continue;
                if (b.gameObject.activeInHierarchy || b.gameObject.name == "Banker(Clone)")
                {
                    KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                        "[Economy] Banker.Awake_Prefix: destroying duplicate " + __instance.gameObject.name
                        + " (already have " + b.gameObject.name + ")");
                    UnityEngine.Object.Destroy(__instance.gameObject);
                    return false; // 跳过 Awake（不注册 903）
                }
            }
            return true;
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
            return true;
        }
    }

    [HarmonyPatch(typeof(Banker), nameof(Banker.Awake))]
    [HarmonyPostfix]
    public static void Awake_Postfix(Banker __instance)
    {
        if (!ModConfig.Enabled.Value) return;
        try
        {
            __instance.coinScanRange = 300f;

            Scanner scanner = __instance._coinScanner;
            if (scanner != null)
            {
                scanner.range = 300f;
                scanner.rangeBehind = 300f;
                scanner._interval = 0.1f;
            }

            __instance.coinGatherTargetPercentage = 0.9f;

            // 移动加速 ×3（自然高效；避免过高倍率穿模）
            float ws = __instance.walkSpeed;
            if (ws > 0 && ws < 5f) __instance.walkSpeed = ws * 3f;
            float rs = __instance.runSpeed;
            if (rs > 0 && rs < 5f) __instance.runSpeed = rs * 3f;
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
    }

    // === Update - 银行家数量控制 ===

    [HarmonyPatch(typeof(Banker), nameof(Banker.Update))]
    [HarmonyPostfix]
    public static void Update_Postfix(Banker __instance)
    {
        if (!ModConfig.Enabled.Value) return;

        int frame = Time.frameCount;
        if (frame - _bankerCheckFrame < 120) return;
        _bankerCheckFrame = frame;

        try
        {
            var allBankers = UnityEngine.Object.FindObjectsOfType<Banker>();
            int count = allBankers.Length;

            // 清理旧存档残留的 Banker_Extra 克隆（Persistent.path 冲突 → NetID 903 duplicate key → 网络崩溃）
            bool hasOriginal = false;
            foreach (var b in allBankers)
            {
                if (b != null && b.gameObject.name != "Banker_Extra") { hasOriginal = true; break; }
            }
            bool cleaned = false;
            foreach (var b in allBankers)
            {
                if (b != null && b.gameObject.name == "Banker_Extra" && hasOriginal)
                {
                    UnityEngine.Object.Destroy(b.gameObject);
                    cleaned = true;
                }
            }
            if (cleaned)
            {
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                    "[Economy] Destroyed stale Banker_Extra clones (persistent path conflict)");
                return;
            }

            if (count > 5)
            {
                for (int i = 5; i < count; i++)
                    UnityEngine.Object.Destroy(allBankers[i].gameObject);
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                    "[Economy] Destroyed " + (count - 5) + " excess bankers");
                return;
            }

            // 补员到 5 个：2.4.0 Banker.Awake 仍硬编码 NetID 903 唯一，克隆走 Awake 必冲突，
            // 不走 Awake 则无 FSM。故保持单银行家（与 Awake_Prefix 去重一致），不补员。
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
    }

    // === ShouldHide - 夜间不休息 ===

    [HarmonyPatch(typeof(Banker), nameof(Banker.ShouldHide))]
    [HarmonyPrefix]
    public static bool ShouldHide_Prefix(ref bool __result)
    {
        if (!ModConfig.Enabled.Value) return true;
        try
        {
            __result = false;
            return false;
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
            return true;
        }
    }
}
