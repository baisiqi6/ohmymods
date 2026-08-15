using System;
using Coatsink.Common;
using UnityEngine;
using HarmonyLib;
using Il2CppInterop.Runtime;

namespace KingdomEnhancedMod;

/// <summary>
/// 银行家增强：NetID 903 唯一性，
/// 以及银行助手的唯一权威入账入口。主银行家积极处理当前城墙内的金币。
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
///     walkSpeed(float)、runSpeed(float)、playerMaxCoins(int) —— 全部存在。
///   - Scanner.range / rangeBehind / _interval —— 存在。
///   - Castle.SetStash(int): public void —— 存在。
///
/// 迁移说明：
///   - 所有字段访问由 Mono 反射改为 interop public 属性直接访问。
///   - FindObjectsOfType&lt;Banker&gt;() 返回 Il2CppArrayBase&lt;Banker&gt;（非 Banker[]），用 var + .Length/foreach。
///   - 跨岛共享账本保留，但不再使用 IEnumerator 起始时点 postfix：
///     日开始先 prime、原生只计息一次、之后保存；存入/提款由余额
///     真实变化后再写回。客户端和未 prime 的实例不得写账本。
/// </summary>
[HarmonyPatch(typeof(Banker))]
public static class PatchEconomy_Banker
{
    private const string SHARED_STASH_KEY = "MyMod_SharedBankStash";
    private const int ENHANCED_PLAYER_PAYOUT_TARGET = 100;
    private static int _sharedStash = -1;
    private static int _primedBankerId;
    private static int _lastObservedStash;
    private static bool _sharedLedgerDirty;
    private static float _nextLedgerFlushAt;
    private static int _bankerCheckFrame = 0;
    private static readonly System.Collections.Generic.HashSet<int> _duplicatesThatSkippedAwake = new();
    private static readonly System.Collections.Generic.Dictionary<int, WorkProfile> _workProfiles = new();

    private sealed class WorkProfile
    {
        public float CoinScanRange;
        public float GatherPercentage;
        public float WalkSpeed;
        public float RunSpeed;
        public float WanderRange;
        public int PlayerMaxCoins;
        public Scanner Scanner;
        public float ScannerRange;
        public float ScannerRangeBehind;
        public float ScannerInterval;
        public bool Enhanced;
    }

    internal static bool TryGetMainBankerDomain(Kingdom kingdom,
        out float left, out float right)
    {
        left = 0f;
        right = 0f;
        if (kingdom == null) return false;

        // GetWall(side, 0) indexes an empty list instead of returning null. Gate
        // every call through the native ordered lists and never mix wall stages.
        var orderedWalls = kingdom._orderedWalls;
        if (orderedWalls != null)
        {
            var leftWalls = orderedWalls[Side.Left];
            var rightWalls = orderedWalls[Side.Right];
            if (leftWalls != null && rightWalls != null)
            {
                if (leftWalls.Count > 1 && rightWalls.Count > 1
                    && TryGetWallPair(kingdom, 1, out left, out right)) return true;
                if (leftWalls.Count > 0 && rightWalls.Count > 0
                    && TryGetWallPair(kingdom, 0, out left, out right)) return true;
            }
        }

        if (!kingdom.HasBorderLoaded) return false;
        float borderLeft = kingdom.GetBorderSide(Side.Left);
        float borderRight = kingdom.GetBorderSide(Side.Right);
        if (IsValidDomain(kingdom, borderLeft, borderRight))
        {
            left = borderLeft;
            right = borderRight;
            return true;
        }

        return false;
    }

    internal static bool IsInMainBankerDomain(Kingdom kingdom, float x)
    {
        return TryGetMainBankerDomain(kingdom, out float left, out float right)
            && IsInMainBankerDomain(x, left, right);
    }

    internal static bool IsInMainBankerDomain(float x, float left, float right)
    {
        return x > left && x < right;
    }

    private static bool IsUsableWall(Wall wall)
    {
        return wall != null && wall.gameObject != null && wall.transform != null
            && wall.gameObject.activeInHierarchy;
    }

    private static bool TryGetWallPair(Kingdom kingdom, int wallIndex,
        out float left, out float right)
    {
        left = 0f;
        right = 0f;
        Wall leftWall = kingdom.GetWall(Side.Left, wallIndex);
        Wall rightWall = kingdom.GetWall(Side.Right, wallIndex);
        if (!IsUsableWall(leftWall) || !IsUsableWall(rightWall)) return false;
        left = leftWall.transform.position.x;
        right = rightWall.transform.position.x;
        return IsValidDomain(kingdom, left, right);
    }

    private static bool IsFiniteOrdered(float left, float right)
    {
        return !float.IsNaN(left) && !float.IsInfinity(left)
            && !float.IsNaN(right) && !float.IsInfinity(right) && left < right;
    }

    private static bool IsValidDomain(Kingdom kingdom, float left, float right)
    {
        return kingdom != null && IsFiniteOrdered(left, right)
            && left < kingdom.campfirePosition && kingdom.campfirePosition < right;
    }

    private static WorkProfile CaptureWorkProfile(Banker banker)
    {
        if (banker == null || banker.gameObject == null) return null;
        int id = banker.gameObject.GetInstanceID();
        if (_workProfiles.TryGetValue(id, out WorkProfile existing)) return existing;

        Scanner scanner = banker._coinScanner;
        WorkProfile profile = new WorkProfile
        {
            CoinScanRange = banker.coinScanRange,
            GatherPercentage = banker.coinGatherTargetPercentage,
            WalkSpeed = banker.walkSpeed,
            RunSpeed = banker.runSpeed,
            WanderRange = banker.wanderRange,
            PlayerMaxCoins = banker.playerMaxCoins,
            Scanner = scanner,
            ScannerRange = scanner != null ? scanner.range : 0f,
            ScannerRangeBehind = scanner != null ? scanner.rangeBehind : 0f,
            ScannerInterval = scanner != null ? scanner._interval : 0f
        };
        _workProfiles[id] = profile;
        return profile;
    }

    private static void ApplyEnhancedWorkProfile(Banker banker)
    {
        if (banker == null) return;
        WorkProfile profile = CaptureWorkProfile(banker);
        if (profile == null) return;

        banker.coinGatherTargetPercentage = 0.5f;
        banker.walkSpeed = 1.95f;
        banker.runSpeed = 3.6f;
        banker.wanderRange = 8.75f;
        banker.playerMaxCoins = ENHANCED_PLAYER_PAYOUT_TARGET;
        ConfigureScannerForDomain(banker);
        profile.Enhanced = true;
    }

    private static bool ConfigureScannerForDomain(Banker banker)
    {
        if (banker == null) return false;
        Scanner scanner = banker._coinScanner;
        Managers managers = Managers.Inst;
        Kingdom kingdom = managers != null ? managers.kingdom : null;
        if (!TryGetMainBankerDomain(kingdom, out float left, out float right))
        {
            banker.coinScanRange = 0f;
            if (scanner != null)
            {
                scanner.range = 0f;
                scanner.rangeBehind = 0f;
                scanner._interval = 1f;
            }
            return false;
        }

        float x = banker.transform.position.x;
        float scaleMagnitude = Mathf.Max(0.01f,
            Mathf.Abs(banker.transform.localScale.x));
        bool facesRight = banker.transform.localScale.x >= 0f;
        float forward = Mathf.Max(0.1f,
            (facesRight ? right - x : x - left) / scaleMagnitude);
        float behind = Mathf.Max(0.1f,
            (facesRight ? x - left : right - x) / scaleMagnitude);
        banker.coinScanRange = Mathf.Max(forward, behind);
        if (scanner != null)
        {
            scanner.range = forward;
            scanner.rangeBehind = behind;
            scanner._interval = 1f;
        }
        return true;
    }

    private static void RestoreWorkProfile(Banker banker)
    {
        if (banker == null || banker.gameObject == null) return;
        if (!_workProfiles.TryGetValue(banker.gameObject.GetInstanceID(),
                out WorkProfile profile) || !profile.Enhanced) return;

        banker.coinScanRange = profile.CoinScanRange;
        banker.coinGatherTargetPercentage = profile.GatherPercentage;
        banker.walkSpeed = profile.WalkSpeed;
        banker.runSpeed = profile.RunSpeed;
        banker.wanderRange = profile.WanderRange;
        banker.playerMaxCoins = profile.PlayerMaxCoins;
        Scanner scanner = banker._coinScanner;
        if (scanner != null && scanner == profile.Scanner)
        {
            scanner.range = profile.ScannerRange;
            scanner.rangeBehind = profile.ScannerRangeBehind;
            scanner._interval = profile.ScannerInterval;
        }
        profile.Enhanced = false;
    }

    private static bool IsCanonicalAuthorityBanker(Banker banker)
    {
        if (!NetworkBigBoss.HasWorldAuth || banker == null) return false;
        var managers = Managers.Inst;
        var kingdom = managers != null ? managers.kingdom : null;
        return kingdom != null && (kingdom.banker == null || kingdom.banker == banker);
    }

    private static void PrimeSharedLedger(Banker banker)
    {
        if (!IsCanonicalAuthorityBanker(banker)) return;
        int id = banker.gameObject.GetInstanceID();
        if (_primedBankerId == id) return;

        if (_sharedStash < 0)
        {
            if (PlayerPrefs.HasKey(SHARED_STASH_KEY))
                _sharedStash = Math.Max(0, PlayerPrefs.GetInt(SHARED_STASH_KEY));
            else
            {
                _sharedStash = Math.Max(0, banker._stashedCoins);
                PlayerPrefs.SetInt(SHARED_STASH_KEY, _sharedStash);
                PlayerPrefs.Save();
            }
        }

        banker._stashedCoins = _sharedStash;
        _primedBankerId = id;
        _lastObservedStash = _sharedStash;
    }

    private static bool TryPrimeSharedLedger(Banker banker)
    {
        try
        {
            PrimeSharedLedger(banker);
            return banker != null && banker.gameObject != null
                && _primedBankerId == banker.gameObject.GetInstanceID();
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[Economy] Failed to prime shared ledger: " + e);
            return false;
        }
    }

    private static void SaveCanonicalLedger(Banker banker)
    {
        if (!IsCanonicalAuthorityBanker(banker)
            || _primedBankerId != banker.gameObject.GetInstanceID()) return;

        int current = Math.Max(0, banker._stashedCoins);
        if (current == _lastObservedStash) return;
        _sharedStash = current;
        _lastObservedStash = current;
        try
        {
            PlayerPrefs.SetInt(SHARED_STASH_KEY, current);
            _sharedLedgerDirty = true;
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[Economy] Failed to stage shared ledger write: " + e);
        }
    }

    private static void FlushSharedLedger(bool force)
    {
        if (!_sharedLedgerDirty) return;
        if (!force && Time.unscaledTime < _nextLedgerFlushAt) return;
        try
        {
            PlayerPrefs.Save();
            _sharedLedgerDirty = false;
            _nextLedgerFlushAt = Time.unscaledTime + 1f;
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[Economy] Failed to flush shared ledger: " + e);
        }
    }

    /// <summary>
    /// 银行助手的唯一入账口。只允许 world-authority 修改主银行家与共享国库，
    /// 并在同一个主线调用内同步 Castle/Stats/PlayerPrefs。返回实际接收量，
    /// 调用方只能清空这部分已携带金币，以保持总量守恒。
    /// </summary>
    public static int DepositFromAssistant(Banker banker, int requestedCoins)
    {
        if (!ModConfig.Enabled.Value || !NetworkBigBoss.HasWorldAuth
            || banker == null || requestedCoins <= 0) return 0;

        if (!TryPrimeSharedLedger(banker)) return 0;

        int current = Math.Max(0, banker._stashedCoins);
        int accepted = Math.Min(requestedCoins, int.MaxValue - current);
        if (accepted <= 0) return 0;

        int updated = current + accepted;
        try
        {
            // This assignment is the atomic economic commit. Once it succeeds,
            // presentation/persistence side effects must never change the return value.
            banker._stashedCoins = updated;
            _sharedStash = updated;
            _lastObservedStash = updated;
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[Economy] Assistant deposit core commit failed: " + e);
            return 0;
        }

        try
        {
            PlayerPrefs.SetInt(SHARED_STASH_KEY, updated);
            _sharedLedgerDirty = true;
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[Economy] Assistant deposit committed, ledger staging failed: " + e);
        }

        try
        {
            var managers = Managers.Inst;
            var kingdom = managers != null ? managers.kingdom : null;
            Castle castle = kingdom != null ? kingdom.castle : null;
            if (castle != null) castle.SetStash(updated);
            if (managers != null && managers.stats != null)
            {
                managers.stats.SetMax(Stat.BiggestStash, updated);
                if (managers.director != null && managers.director.CurrentSeason == Season.Autumn)
                    managers.stats.SetMax(Stat.BiggestWinterStash, updated);
                managers.stats.SetStat(Stat.CoinsInBank, updated, false);
            }
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[Economy] Assistant deposit committed, Castle/Stats refresh failed: " + e);
        }

        return accepted;
    }

    // IEnumerator 完成时点不靠 Harmony postfix 猜测。FinaliseEmerge/DayStart 只做可靠
    // priming；之后由 Update 在真实 _stashedCoins 变化后同步存入/提款结果。
    [HarmonyPatch(typeof(Banker), nameof(Banker.FinaliseEmerge))]
    [HarmonyPrefix]
    public static void FinaliseEmerge_Prefix(Banker __instance)
    {
        if (ModConfig.Enabled.Value) TryPrimeSharedLedger(__instance);
    }

    [HarmonyPatch(typeof(Banker), nameof(Banker.HandleOnDayStart))]
    [HarmonyPrefix]
    public static void HandleOnDayStart_Prefix(Banker __instance)
    {
        if (ModConfig.Enabled.Value) TryPrimeSharedLedger(__instance);
    }

    [HarmonyPatch(typeof(Banker), nameof(Banker.HandleOnDayStart))]
    [HarmonyPostfix]
    public static void HandleOnDayStart_Postfix(Banker __instance)
    {
        if (!ModConfig.Enabled.Value) return;
        // Prefix 先载入，原生方法只计息一次，Postfix 再保存新余额。
        _lastObservedStash = int.MinValue;
        SaveCanonicalLedger(__instance);
        FlushSharedLedger(true);
    }

    [HarmonyPatch(typeof(Banker), nameof(Banker.OpenCastleDoor))]
    [HarmonyPrefix]
    public static void OpenCastleDoor_Prefix(Banker __instance)
    {
        if (ModConfig.Enabled.Value) TryPrimeSharedLedger(__instance);
    }

    // === Awake - 去重 + 恢复 2.4.0 原生参数 ===

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
                    // Prefer the native/castle instance over a stale pre-fix persistent clone.
                    if (b.gameObject.name == "Banker_Extra"
                        && __instance.gameObject.name != "Banker_Extra")
                    {
                        _duplicatesThatSkippedAwake.Add(b.gameObject.GetInstanceID());
                        UnityEngine.Object.Destroy(b.gameObject);
                        continue;
                    }

                    KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                        "[Economy] Banker.Awake_Prefix: destroying duplicate " + __instance.gameObject.name
                        + " (already have " + b.gameObject.name + ")");
                    _duplicatesThatSkippedAwake.Add(__instance.gameObject.GetInstanceID());
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

    [HarmonyPatch(typeof(Banker), nameof(Banker.OnDestroy))]
    [HarmonyPrefix]
    public static bool OnDestroy_Prefix(Banker __instance)
    {
        if (__instance == null || __instance.gameObject == null) return true;
        int id = __instance.gameObject.GetInstanceID();
        if (_duplicatesThatSkippedAwake.Remove(id))
        {
            // Awake was skipped, so this instance never registered auth/RPC 903.
            // Running native OnDestroy would deregister the real Banker's fixed ID.
            return false;
        }

        SaveCanonicalLedger(__instance);
        FlushSharedLedger(true);
        _workProfiles.Remove(id);
        if (_primedBankerId == id) _primedBankerId = 0;
        return true;
    }

    [HarmonyPatch(typeof(Banker), nameof(Banker.Awake))]
    [HarmonyPostfix]
    public static void Awake_Postfix(Banker __instance)
    {
        if (!ModConfig.Enabled.Value) return;
        // Harmony still runs postfixes when our Prefix deliberately skips native Awake.
        // Never attach the coordinator to a duplicate that is already scheduled for
        // destruction, or it can replace the canonical Banker in the static runtime state.
        if (__instance == null || __instance.gameObject == null
            || _duplicatesThatSkippedAwake.Contains(__instance.gameObject.GetInstanceID()))
            return;
        try
        {
            ApplyEnhancedWorkProfile(__instance);

            PatchEconomy_BankAssistants.EnsureForMainBanker(__instance);
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
        if (!ModConfig.Enabled.Value)
        {
            RestoreWorkProfile(__instance);
            return;
        }

        // 在原生协程实际改变余额的帧之后观察，避免 IEnumerator 方法
        // postfix 只在“取得迭代器”时运行而回滚真实存入/提款。
        SaveCanonicalLedger(__instance);
        FlushSharedLedger(false);

        int frame = Time.frameCount;
        if (frame - _bankerCheckFrame < 120) return;
        _bankerCheckFrame = frame;

        try
        {
            // Walls move as the kingdom expands. Refresh the directional scanner at
            // low frequency; the outside-wall claim gate remains the final boundary.
            ApplyEnhancedWorkProfile(__instance);

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
                    _duplicatesThatSkippedAwake.Add(b.gameObject.GetInstanceID());
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

            if (count > 1)
            {
                KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                    "[Economy] Invariant violation: expected exactly one Banker/NetID 903, found " + count);
            }

            // 补员到 5 个：2.4.0 Banker.Awake 仍硬编码 NetID 903 唯一，克隆走 Awake 必冲突，
            // 不走 Awake 则无 FSM。故保持单银行家（与 Awake_Prefix 去重一致），不补员。
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
    }

    [HarmonyPatch(typeof(Banker), nameof(Banker.ClaimCoins))]
    [HarmonyPrefix]
    public static bool ClaimCoins_Prefix(Banker __instance)
    {
        if (!ModConfig.Enabled.Value) return true;
        if (ConfigureScannerForDomain(__instance)) return true;

        // Native ClaimCoins normally clears this first. The fail-closed prefix must
        // preserve that invariant when no canonical wall domain can be resolved.
        if (__instance != null) __instance._targetCoin = null;
        return false;
    }

    // === ShouldHide - 已验证的积极工作模式（夜间不休息） ===

    [HarmonyPatch(typeof(Banker), nameof(Banker.ShouldHide))]
    [HarmonyPrefix]
    public static bool ShouldHide_Prefix(ref bool __result)
    {
        if (!ModConfig.Enabled.Value) return true;
        __result = false;
        return false;
    }

    [HarmonyPatch(typeof(Banker), nameof(Banker.ShouldEmerge))]
    [HarmonyPrefix]
    public static bool ShouldEmerge_Prefix(ref bool __result)
    {
        if (!ModConfig.Enabled.Value) return true;
        Managers managers = Managers.Inst;
        __result = managers != null && managers.kingdom != null
            && managers.kingdom.isSafe;
        return false;
    }
}

/// <summary>
/// Keep the native Banker strictly inside the current wall topology. The assistant
/// scheduler owns player coins outside the walls; all other claimers and droppables
/// continue through the original TryFriendlyClaim implementation unchanged.
/// </summary>
[HarmonyPatch(typeof(Droppable), nameof(Droppable.TryFriendlyClaim))]
public static class Droppable_MainBankerOutsideWallClaim_Patch
{
    [HarmonyPrefix]
    public static bool Prefix(
        Droppable __instance,
        GameObject claimer,
        ref bool __result)
    {
        if (!ModConfig.Enabled.Value || __instance == null || claimer == null) return true;
        Banker banker = claimer.GetComponent<Banker>();
        DroppableCurrency coin = __instance.TryCast<DroppableCurrency>();
        if (banker == null || coin == null || coin.droppedBy != DropType.Player
            || coin.CurrencyType != CurrencyType.Coins) return true;

        Managers managers = Managers.Inst;
        Kingdom kingdom = managers != null ? managers.kingdom : null;
        if (PatchEconomy_Banker.TryGetMainBankerDomain(
                kingdom, out float left, out float right)
            && PatchEconomy_Banker.IsInMainBankerDomain(
                coin.transform.position.x, left, right)) return true;

        __result = false;
        return false;
    }
}
