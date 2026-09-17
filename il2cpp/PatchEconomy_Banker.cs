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
///   - 本文件所有增强与账本读写都由 GreekBankScope 限定“当前希腊世界”：
///     其他世界（忍者等）完整走原生实现，跨世界的银行家余额绝不互相污染，
///     已改过的银行工作参数在离开希腊/关闭开关时按捕获原值归还。
/// </summary>
[HarmonyPatch(typeof(Banker))]
public static class PatchEconomy_Banker
{
    private const string SHARED_STASH_KEY = "MyMod_SharedBankStash";
    private const int ENHANCED_PLAYER_PAYOUT_TARGET = 100;
    private static int _sharedStash = -1;
    private static ObjectIdentity _primedBanker;
    private static WorldIdentity _primedWorld;
    private static bool _hasPrimedBanker;
    private static bool _needsReprime;
    private static int _lastObservedStash;
    private static bool _sharedLedgerDirty;
    private static float _nextLedgerFlushAt;
    private static int _bankerCheckFrame = 0;
    private static readonly System.Collections.Generic.HashSet<ObjectIdentity> _duplicatesThatSkippedAwake = new();
    private static readonly System.Collections.Generic.Dictionary<ObjectIdentity, WorkProfile> _workProfiles = new();
    private static readonly System.Collections.Generic.List<ObjectIdentity> _profileKeys = new();
    private static readonly System.Collections.Generic.Dictionary<ObjectIdentity, Banker> _knownBankers = new();
    private static int _nextLateBindFrame;

    /// <summary>
    /// Banker 本体与 GameObject 的双重身份。只用 instanceID 记账会在对象销毁后被复用，
    /// 旧 profile/prime/duplicate 记账会落到新实例上（instanceID 复用错误）。
    /// Pointer + instanceID 联合唯一；读不到身份时调用方保留原状态，绝不迁移。
    /// </summary>
    private readonly struct ObjectIdentity : IEquatable<ObjectIdentity>
    {
        private readonly IntPtr _selfPointer;
        private readonly int _selfId;
        private readonly IntPtr _ownerPointer;
        private readonly int _ownerId;

        private ObjectIdentity(IntPtr selfPointer, int selfId, IntPtr ownerPointer, int ownerId)
        {
            _selfPointer = selfPointer;
            _selfId = selfId;
            _ownerPointer = ownerPointer;
            _ownerId = ownerId;
        }

        internal static bool TryGet(Component component, out ObjectIdentity identity)
        {
            identity = default;
            try
            {
                if (component == null) return false;
                GameObject owner = component.gameObject;
                if (owner == null) return false;
                identity = new ObjectIdentity(component.Pointer, component.GetInstanceID(),
                    owner.Pointer, owner.GetInstanceID());
                return true;
            }
            catch
            {
                return false; // 身份不可读：由调用方暂缓，不做任何迁移或覆盖
            }
        }

        public bool Equals(ObjectIdentity other)
        {
            return _selfPointer == other._selfPointer && _selfId == other._selfId
                && _ownerPointer == other._ownerPointer && _ownerId == other._ownerId;
        }

        public override bool Equals(object obj) => obj is ObjectIdentity other && Equals(other);

        public override int GetHashCode()
            => (_selfId * 397) ^ _selfPointer.GetHashCode() ^ (_ownerId * 31) ^ _ownerPointer.GetHashCode();
    }

    /// <summary>
    /// Scanner 是 interop 对象而不是 Unity Component（无 gameObject/instanceID）：
    /// 身份用 interop Pointer + 包装引用。Pointer 会被原生分配器复用，因此两者一起比。
    /// </summary>
    private readonly struct ScannerIdentity : IEquatable<ScannerIdentity>
    {
        private readonly IntPtr _pointer;
        private readonly object _wrapper;

        private ScannerIdentity(IntPtr pointer, object wrapper)
        {
            _pointer = pointer;
            _wrapper = wrapper;
        }

        internal static bool TryGet(Scanner scanner, out ScannerIdentity identity)
        {
            identity = default;
            try
            {
                if (scanner == null) return false;
                IntPtr pointer = scanner.Pointer;
                if (pointer == IntPtr.Zero) return false;
                identity = new ScannerIdentity(pointer, scanner);
                return true;
            }
            catch
            {
                return false; // 销毁中：调用方保留 receipt，下帧再判
            }
        }

        public bool Equals(ScannerIdentity other)
            => _pointer == other._pointer && ReferenceEquals(_wrapper, other._wrapper);

        public override bool Equals(object obj) => obj is ScannerIdentity other && Equals(other);

        public override int GetHashCode() => (_pointer.GetHashCode() * 397) ^ _wrapper.GetHashCode();
    }

    /// <summary>
    /// prime 凭证绑定的 world 上下文。只按对象身份记账时，同一个 banker 被换岛/换世界
    /// 重挂后仍算“已 prime”，可能把新世界原生余额写进希腊共享账本；world/层/场景三者
    /// 一起比才能在上下文变化时强制重新 prime。
    /// </summary>
    private readonly struct WorldIdentity : IEquatable<WorldIdentity>
    {
        private readonly IntPtr _worldPointer;
        private readonly IntPtr _layerPointer;
        private readonly int _sceneHandle;

        private WorldIdentity(IntPtr worldPointer, IntPtr layerPointer, int sceneHandle)
        {
            _worldPointer = worldPointer;
            _layerPointer = layerPointer;
            _sceneHandle = sceneHandle;
        }

        internal static bool TryGet(out WorldIdentity identity)
        {
            identity = default;
            try
            {
                Managers managers = Managers.Inst;
                World world = managers != null ? managers.world : null;
                Transform layer = world != null ? world.gameLayer : null;
                if (layer == null || layer.gameObject == null) return false;
                identity = new WorldIdentity(world.Pointer, layer.Pointer,
                    layer.gameObject.scene.handle);
                return true;
            }
            catch
            {
                return false; // 加载中/不可读：不视为同一上下文
            }
        }

        public bool Equals(WorldIdentity other)
            => _worldPointer == other._worldPointer && _layerPointer == other._layerPointer
                && _sceneHandle == other._sceneHandle;

        public override bool Equals(object obj) => obj is WorldIdentity other && Equals(other);

        public override int GetHashCode()
            => (_sceneHandle * 397) ^ _worldPointer.GetHashCode() ^ (_layerPointer.GetHashCode() * 31);
    }

    /// <summary>
    /// 本补丁写入过的单个数值。Owned 表示补丁仍维护该字段：Original 是首次写入前的
    /// 基线，Written 是本补丁写入的值。归还时只在当前值仍等于 Written 时写回 Original，
    /// 外部/原生已改写过的值一律保留——跨世界、关开、换岛都不能拿旧基线覆盖别人的值。
    /// </summary>
    private struct OwnedFloat
    {
        internal float Original;
        internal float Written;
        internal bool Owned;
    }

    private struct OwnedInt
    {
        internal int Original;
        internal int Written;
        internal bool Owned;
    }

    private sealed class WorkProfile
    {
        internal Banker Owner;
        internal OwnedFloat CoinScanRange;
        internal OwnedFloat GatherPercentage;
        internal OwnedFloat WalkSpeed;
        internal OwnedFloat RunSpeed;
        internal OwnedFloat WanderRange;
        internal OwnedInt PlayerMaxCoins;
        internal ScannerIdentity ScannerKey;
        internal bool HasScanner;
        internal OwnedFloat ScannerRange;
        internal OwnedFloat ScannerRangeBehind;
        internal OwnedFloat ScannerInterval;

        internal bool Empty => !CoinScanRange.Owned && !GatherPercentage.Owned && !WalkSpeed.Owned
            && !RunSpeed.Owned && !WanderRange.Owned && !PlayerMaxCoins.Owned
            && !ScannerRange.Owned && !ScannerRangeBehind.Owned && !ScannerInterval.Owned;
    }

    /// <summary>落地前记账：外部改写过的旧基线作废；未拥有则重新取当前值作为基线。</summary>
    private static void Claim(ref OwnedFloat slot, float current, float desired)
    {
        if (slot.Owned && current != slot.Written) slot.Owned = false;
        if (!slot.Owned) slot.Original = current;
        slot.Written = desired;
        slot.Owned = true;
    }

    private static void Claim(ref OwnedInt slot, int current, int desired)
    {
        if (slot.Owned && current != slot.Written) slot.Owned = false;
        if (!slot.Owned) slot.Original = current;
        slot.Written = desired;
        slot.Owned = true;
    }

    /// <summary>
    /// 该字段是否可安全归还：只有当前值仍等于本补丁写入值时才写回 Original。
    /// 外部/原生改写过的值直接交还所有权，不再由本补丁维护。
    /// </summary>
    private static bool NeedsRestore(ref OwnedFloat slot, float current)
    {
        if (!slot.Owned) return false;
        if (current == slot.Written) return true;
        slot.Owned = false;
        return false;
    }

    private static bool NeedsRestore(ref OwnedInt slot, int current)
    {
        if (!slot.Owned) return false;
        if (current == slot.Written) return true;
        slot.Owned = false;
        return false;
    }

    private static WorkProfile EnsureProfile(Banker banker)
    {
        if (!ObjectIdentity.TryGet(banker, out ObjectIdentity key)) return null;
        if (_workProfiles.TryGetValue(key, out WorkProfile profile)) return profile;
        profile = new WorkProfile { Owner = banker };
        _workProfiles[key] = profile;
        return profile;
    }

    private static void ForgetWorkProfile(Banker banker)
    {
        if (ObjectIdentity.TryGet(banker, out ObjectIdentity key))
        {
            _workProfiles.Remove(key);
            _knownBankers.Remove(key);
        }
    }

    private static bool IsSkippedDuplicate(Banker banker)
    {
        return ObjectIdentity.TryGet(banker, out ObjectIdentity key)
            && _duplicatesThatSkippedAwake.Contains(key);
    }

    /// <summary>
    /// 记录“Awake 被跳过”的实例身份：它从未注册 NetID 903，OnDestroy 必须跳过原生注销，
    /// 否则会注销真银行家。按身份记录，instanceID 复用不会误伤别的实例。
    /// </summary>
    private static void RecordSkippedDuplicate(Banker banker)
    {
        if (ObjectIdentity.TryGet(banker, out ObjectIdentity key)) _duplicatesThatSkippedAwake.Add(key);
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

    /// <summary>
    /// 应用增强工作参数（只在希腊 scope 调用）。每个字段独立记账：写前捕获当前原值，
    /// 保留 receipt，离开希腊/关闭开关时归还；外部已改写的不覆盖、不猜常量。
    /// </summary>
    private static void ApplyEnhancedWorkProfile(Banker banker)
    {
        if (banker == null) return;
        WorkProfile profile = EnsureProfile(banker);
        if (profile == null) return;

        try
        {
            Claim(ref profile.GatherPercentage, banker.coinGatherTargetPercentage, 0.5f);
            banker.coinGatherTargetPercentage = 0.5f;
            Claim(ref profile.WalkSpeed, banker.walkSpeed, 1.95f);
            banker.walkSpeed = 1.95f;
            Claim(ref profile.RunSpeed, banker.runSpeed, 3.6f);
            banker.runSpeed = 3.6f;
            Claim(ref profile.WanderRange, banker.wanderRange, 8.75f);
            banker.wanderRange = 8.75f;
            Claim(ref profile.PlayerMaxCoins, banker.playerMaxCoins, ENHANCED_PLAYER_PAYOUT_TARGET);
            banker.playerMaxCoins = ENHANCED_PLAYER_PAYOUT_TARGET;
        }
        catch (Exception e)
        {
            // 已记 receipt 保留，下一帧或恢复时继续；异常时不假装写成功。
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[Economy] Banker work profile write failed: " + e);
            return;
        }

        ConfigureScannerForDomain(banker, profile);
    }

    private static bool ConfigureScannerForDomain(Banker banker, WorkProfile profile)
    {
        if (banker == null || profile == null) return false;
        Scanner scanner = banker._coinScanner;
        Managers managers = Managers.Inst;
        Kingdom kingdom = managers != null ? managers.kingdom : null;
        if (!TryGetMainBankerDomain(kingdom, out float left, out float right))
        {
            Claim(ref profile.CoinScanRange, banker.coinScanRange, 0f);
            banker.coinScanRange = 0f;
            WriteScannerValues(profile, scanner, 0f, 0f, 1f);
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
        float scanRange = Mathf.Max(forward, behind);
        Claim(ref profile.CoinScanRange, banker.coinScanRange, scanRange);
        banker.coinScanRange = scanRange;
        WriteScannerValues(profile, scanner, forward, behind, 1f);
        return true;
    }

    /// <summary>
    /// 扫描器字段同样逐字段记账。扫描器被销毁/换体时旧 receipt 无处归还，
    /// 丢弃而不是写到别的对象上。
    /// </summary>
    private static void WriteScannerValues(WorkProfile profile, Scanner scanner,
        float range, float rangeBehind, float interval)
    {
        if (scanner == null)
        {
            DiscardScannerReceipt(profile);
            return;
        }
        if (!ScannerIdentity.TryGet(scanner, out ScannerIdentity key)) return; // 销毁中：保留 receipt 下次再判
        if (profile.HasScanner && !profile.ScannerKey.Equals(key)) DiscardScannerReceipt(profile);
        profile.ScannerKey = key;
        profile.HasScanner = true;
        Claim(ref profile.ScannerRange, scanner.range, range);
        scanner.range = range;
        Claim(ref profile.ScannerRangeBehind, scanner.rangeBehind, rangeBehind);
        scanner.rangeBehind = rangeBehind;
        Claim(ref profile.ScannerInterval, scanner._interval, interval);
        scanner._interval = interval;
    }

    private static void DiscardScannerReceipt(WorkProfile profile)
    {
        profile.ScannerRange.Owned = false;
        profile.ScannerRangeBehind.Owned = false;
        profile.ScannerInterval.Owned = false;
        profile.HasScanner = false;
    }

    /// <summary>
    /// 归还本补丁写过的银行工作参数（离开希腊世界 / 关闭总开关时）。写回的是捕获到的
    /// 原值而非硬编码常量，且只在当前值仍等于本补丁写入值时进行；外部/原生改写过的
    /// 值保留。每个字段的 receipt 只在该字段写入成功后才交出——native setter 抛错时
    /// receipt 保留，下一帧继续，绝不因一次失败永久丢掉归还凭据。
    /// </summary>
    private static void RestoreWorkProfile(Banker banker)
    {
        if (banker == null) return;
        if (!ObjectIdentity.TryGet(banker, out ObjectIdentity key)) return;
        if (!_workProfiles.TryGetValue(key, out WorkProfile profile)) return;

        try
        {
            if (NeedsRestore(ref profile.GatherPercentage, banker.coinGatherTargetPercentage))
            {
                banker.coinGatherTargetPercentage = profile.GatherPercentage.Original;
                profile.GatherPercentage.Owned = false;
            }
            if (NeedsRestore(ref profile.WalkSpeed, banker.walkSpeed))
            {
                banker.walkSpeed = profile.WalkSpeed.Original;
                profile.WalkSpeed.Owned = false;
            }
            if (NeedsRestore(ref profile.RunSpeed, banker.runSpeed))
            {
                banker.runSpeed = profile.RunSpeed.Original;
                profile.RunSpeed.Owned = false;
            }
            if (NeedsRestore(ref profile.WanderRange, banker.wanderRange))
            {
                banker.wanderRange = profile.WanderRange.Original;
                profile.WanderRange.Owned = false;
            }
            if (NeedsRestore(ref profile.PlayerMaxCoins, banker.playerMaxCoins))
            {
                banker.playerMaxCoins = profile.PlayerMaxCoins.Original;
                profile.PlayerMaxCoins.Owned = false;
            }
            if (NeedsRestore(ref profile.CoinScanRange, banker.coinScanRange))
            {
                banker.coinScanRange = profile.CoinScanRange.Original;
                profile.CoinScanRange.Owned = false;
            }

            Scanner scanner = banker._coinScanner;
            if (scanner != null && !ScannerIdentity.TryGet(scanner, out _)) return;
            bool sameScanner = profile.HasScanner && scanner != null
                && ScannerIdentity.TryGet(scanner, out ScannerIdentity scannerKey)
                && scannerKey.Equals(profile.ScannerKey);
            if (sameScanner)
            {
                if (NeedsRestore(ref profile.ScannerRange, scanner.range))
                {
                    scanner.range = profile.ScannerRange.Original;
                    profile.ScannerRange.Owned = false;
                }
                if (NeedsRestore(ref profile.ScannerRangeBehind, scanner.rangeBehind))
                {
                    scanner.rangeBehind = profile.ScannerRangeBehind.Original;
                    profile.ScannerRangeBehind.Owned = false;
                }
                if (NeedsRestore(ref profile.ScannerInterval, scanner._interval))
                {
                    scanner._interval = profile.ScannerInterval.Original;
                    profile.ScannerInterval.Owned = false;
                }
            }
            else
            {
                DiscardScannerReceipt(profile);
            }
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[Economy] Banker work profile restore failed: " + e);
            return; // 保留未归还字段的 receipt，下一帧继续
        }

        // 销毁中的对象取不到身份：此处 entry 会留在表里，由下一次同身份查询或
        // OnDestroy 的 ForgetWorkProfile 清理。
        if (profile.Empty) _workProfiles.Remove(key);
    }

    /// <summary>
    /// prime 凭证 = 本体身份 + world 上下文。任一变化都必须重新 prime：同一个 banker 被
    /// 换岛/换世界重挂后旧凭证作废，否则会把新世界的原生余额当成希腊余额写进共享账本。
    /// 重新 prime 会把共享余额写回本体，因此只在凭证缺失时发生，不会反复打断原生协程。
    /// </summary>
    private static bool IsPrimedFor(Banker banker)
    {
        if (!GreekBankScope.IsAuthorityBanker(banker)) return false;
        if (!ObjectIdentity.TryGet(banker, out ObjectIdentity key)) return false;
        if (!_hasPrimedBanker || !_primedBanker.Equals(key)) return false;
        return WorldIdentity.TryGet(out WorldIdentity world) && _primedWorld.Equals(world);
    }

    /// <summary>
    /// 载入共享金库到当前希腊权威本体。共享 PlayerPrefs 只在“希腊世界 + 当前权威本体 +
    /// 同一 world 上下文”上读写；foreign 银行家的原生余额既不写进共享键，共享键的值也
    /// 不写进 foreign 银行家。身份复用（instanceID/Pointer 回收）与新 world 都必须重新 prime。
    /// </summary>
    private static void PrimeSharedLedger(Banker banker)
    {
        if (!GreekBankScope.IsAuthorityBanker(banker)) return;
        if (IsPrimedFor(banker)) return;
        if (!ObjectIdentity.TryGet(banker, out ObjectIdentity key)) return;
        if (!WorldIdentity.TryGet(out WorldIdentity world)) return;

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
        _primedBanker = key;
        _primedWorld = world;
        _hasPrimedBanker = true;
        _needsReprime = false;
        _lastObservedStash = _sharedStash;
    }

    private static bool TryPrimeSharedLedger(Banker banker)
    {
        try
        {
            PrimeSharedLedger(banker);
            // 返回“这个本体在这个 world 上真的被 prime 过”，而不是某个 instanceID 曾出现过。
            return IsPrimedFor(banker);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[Economy] Failed to prime shared ledger: " + e);
            return false;
        }
    }

    /// <summary>已知离开希腊（其他世界 / 关闭总开关）时吊销 prime 凭证：再入必须重新 prime。</summary>
    private static void SuspendPrimeProof()
    {
        _needsReprime |= _hasPrimedBanker;
        _hasPrimedBanker = false;
        _primedWorld = default;
    }

    private static void SaveCanonicalLedger(Banker banker)
    {
        if (!GreekBankScope.IsAuthorityBanker(banker)) return;
        if (!IsPrimedFor(banker)) return;
        StageLedgerWrite(banker);
    }

    /// <summary>
    /// 销毁时只观察仍属于当前希腊的银行家。其他世界只允许落盘已 staged 的旧内容，
    /// 不能因为 world/layer 指针暂时未更新而读取 foreign 的新余额。
    /// </summary>
    private static void SaveOwnedLedger(Banker banker)
    {
        if (!GreekBankScope.IsAuthorityBanker(banker)) return;
        if (!IsPrimedFor(banker)) return;
        StageLedgerWrite(banker);
    }

    private static void StageLedgerWrite(Banker banker)
    {
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

    /// <summary>希腊世界内的常规落盘。</summary>
    private static void FlushSharedLedger(bool force)
    {
        if (!GreekBankScope.IsActive) return;
        if (!force && Time.unscaledTime < _nextLedgerFlushAt) return;
        FlushStagedLedger();
    }

    /// <summary>
    /// 落盘已 staged 的共享金库内容（内容只可能由希腊世界的权威写入：所有 SetInt 调用点
    /// 都在 scope+身份闸门之后）。离开希腊/关闭/销毁时用它把已有内容写进磁盘，
    /// 绝不在其他世界做新的读或计算新值。
    /// </summary>
    private static void FlushStagedLedger()
    {
        if (!_sharedLedgerDirty) return;
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
    /// 银行助手的唯一入账口。只允许当前希腊世界的权威本体修改主银行家与共享国库，
    /// 并在同一个主线调用内同步 Castle/Stats/PlayerPrefs。返回实际接收量，
    /// 调用方只能清空这部分已携带金币，以保持总量守恒。
    /// </summary>
    public static int DepositFromAssistant(Banker banker, int requestedCoins)
    {
        if (requestedCoins <= 0 || !GreekBankScope.IsAuthorityBanker(banker)) return 0;

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

    /// <summary>One synchronous procurement debit; native shop PerformPay records CoinsSpent.
    /// 只读白天门也在此：夜间（Kingdom.isDaytime=false/不可读）拒绝直接调用，不产生任何扣款。</summary>
    internal static bool TrySpendForAutoRestock(Banker banker, int amount)
    {
        if (Time.timeScale <= 0f || amount <= 0 || amount > 200
            || !GreekBankScope.IsAuthorityBanker(banker)) return false;
        var managers = Managers.Inst;
        var kingdom = managers != null ? managers.kingdom : null;
        if (managers == null || managers.game == null || managers.game.state != Game.State.Playing
            || kingdom == null
            || !PatchEconomy_AutoRestock.IsDaytimeNow()
            || !BankAssistantCoordinator.IsCurrentRestockBanker(banker)
            || !TryPrimeSharedLedger(banker)) return false;
        // Prime 的账本读取/写回也是 native/协作回调：期间可能翻夜。首次扣款写之前
        // 再确认一次白天，夜间绝不提交任何扣款（已扣款回执与 prime 结果不受影响）。
        if (!PatchEconomy_AutoRestock.IsDaytimeNow()) return false;
        int current = banker._stashedCoins;
        if (current < amount) return false;
        int updated = current - amount;
        try
        {
            banker._stashedCoins = updated; // commit; no fallible presentation work until after this block
            _sharedStash = updated;
            _lastObservedStash = updated;
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[AutoRestock] treasury debit failed: " + e);
            return false;
        }
        try
        {
            PlayerPrefs.SetInt(SHARED_STASH_KEY, updated);
            _sharedLedgerDirty = true;
        }
        catch (Exception e)
        {
            _lastObservedStash = int.MinValue; // existing Update retries staging this committed debit
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[AutoRestock] debit committed; ledger staging failed: " + e);
        }
        try
        {
            if (kingdom.castle != null) kingdom.castle.SetStash(updated);
            if (managers.stats != null) managers.stats.SetStat(Stat.CoinsInBank, updated, false);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[AutoRestock] debit committed; display refresh failed: " + e);
        }
        return true;
    }

    // IEnumerator 完成时点不靠 Harmony postfix 猜测。FinaliseEmerge/DayStart 只做可靠
    // priming；之后由 Update 在真实 _stashedCoins 变化后同步存入/提款结果。
    // 全部 priming 限定“希腊世界 + 当前权威本体”：foreign 银行家的原生余额绝不写进
    // 共享金库，共享金库的值也绝不写进 foreign 银行家。
    [HarmonyPatch(typeof(Banker), nameof(Banker.FinaliseEmerge))]
    [HarmonyPrefix]
    public static void FinaliseEmerge_Prefix(Banker __instance)
    {
        if (GreekBankScope.IsAuthorityBanker(__instance)) TryPrimeSharedLedger(__instance);
    }

    [HarmonyPatch(typeof(Banker), nameof(Banker.HandleOnDayStart))]
    [HarmonyPrefix]
    public static void HandleOnDayStart_Prefix(Banker __instance)
    {
        if (GreekBankScope.IsAuthorityBanker(__instance)) TryPrimeSharedLedger(__instance);
    }

    [HarmonyPatch(typeof(Banker), nameof(Banker.HandleOnDayStart))]
    [HarmonyPostfix]
    public static void HandleOnDayStart_Postfix(Banker __instance)
    {
        if (!GreekBankScope.IsAuthorityBanker(__instance)) return;
        // Prefix 先载入，原生方法只计息一次，Postfix 再保存新余额。
        _lastObservedStash = int.MinValue;
        SaveCanonicalLedger(__instance);
        FlushSharedLedger(true);
    }

    [HarmonyPatch(typeof(Banker), nameof(Banker.OpenCastleDoor))]
    [HarmonyPrefix]
    public static void OpenCastleDoor_Prefix(Banker __instance)
    {
        if (GreekBankScope.IsAuthorityBanker(__instance)) TryPrimeSharedLedger(__instance);
    }

    // === Awake - 去重 + 恢复 2.4.0 原生参数 ===

    /// <summary>
    /// 关键：Banker.Awake 硬编码 RegisterObject(903, Dynamic)。多个 Banker 实例同时 Awake 时
    /// NetID 903 冲突 → 网络层崩溃 → 原生池丢失。Prefix 检测：当前层已有其他 Banker 时销毁自己并跳过 Awake。
    /// 只在当前希腊世界生效，且只统计当前 gameLayer/scene 内的实例——旧世界正在销毁的
    /// banker 不属于本世界，不能据此把新世界的本体当成 duplicate 删掉。
    /// 其他世界/未知世界走原生 Awake（原版行为，不删原生实例）。
    /// </summary>
    [HarmonyPatch(typeof(Banker), nameof(Banker.Awake))]
    [HarmonyPrefix]
    public static bool Awake_Prefix(Banker __instance)
    {
        if (!GreekBankScope.IsActive) return true;
        if (__instance == null || __instance.gameObject == null) return true;
        try
        {
            var allBankers = UnityEngine.Object.FindObjectsOfType<Banker>();
            string names = "";
            int live = 0;
            foreach (var b in allBankers)
            {
                if (b == null) continue;
                bool here = GreekBankScope.IsInCurrentLayer(b.gameObject);
                if (here) live++;
                names += "[" + b.gameObject.name + (here ? "" : "@other") + "]";
            }
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                "[Economy] Banker.Awake_Prefix: current=" + __instance.gameObject.name
                + " inLayer=" + GreekBankScope.IsInCurrentLayer(__instance.gameObject)
                + " total=" + allBankers.Length + " live=" + live + " all=" + names);

            foreach (var b in allBankers)
            {
                if (b == null || b == __instance) continue;
                if (!GreekBankScope.IsInCurrentLayer(b.gameObject)) continue;
                if (b.gameObject.activeInHierarchy || b.gameObject.name == "Banker(Clone)")
                {
                    // Prefer the native/castle instance over a stale pre-fix persistent clone.
                    if (b.gameObject.name == "Banker_Extra"
                        && __instance.gameObject.name != "Banker_Extra")
                    {
                        RecordSkippedDuplicate(b);
                        UnityEngine.Object.Destroy(b.gameObject);
                        continue;
                    }

                    KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                        "[Economy] Banker.Awake_Prefix: destroying duplicate " + __instance.gameObject.name
                        + " (already have " + b.gameObject.name + ")");
                    RecordSkippedDuplicate(__instance);
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

        // owned cleanup：Awake 被跳过的 duplicate 即使此刻已离开希腊/关模组/失权，
        // 也必须跳过原生 OnDestroy，否则会注销真银行家的固定 NetID 903。
        if (IsSkippedDuplicate(__instance))
        {
            if (ObjectIdentity.TryGet(__instance, out ObjectIdentity skipped))
                _duplicatesThatSkippedAwake.Remove(skipped);
            ForgetWorkProfile(__instance);
            return false;
        }

        // New observations require current Greek scope; only already staged values
        // may be flushed after a world change.
        SaveOwnedLedger(__instance);
        FlushStagedLedger();
        ForgetWorkProfile(__instance);
        if (ObjectIdentity.TryGet(__instance, out ObjectIdentity key)
            && _hasPrimedBanker && _primedBanker.Equals(key))
        {
            _hasPrimedBanker = false;
            _primedWorld = default;
        }
        return true;
    }

    [HarmonyPatch(typeof(Banker), nameof(Banker.Awake))]
    [HarmonyPostfix]
    public static void Awake_Postfix(Banker __instance)
    {
        if (GreekBankScope.Current() == GreekBankScope.Scope.Inactive) return;
        // Harmony still runs postfixes when our Prefix deliberately skips native Awake.
        // Never attach the coordinator to a duplicate that is already scheduled for
        // destruction, or it can replace the canonical Banker in the static runtime state.
        if (__instance == null || __instance.gameObject == null
            || IsSkippedDuplicate(__instance))
            return;
        try
        {
            if (ObjectIdentity.TryGet(__instance, out ObjectIdentity identity))
                _knownBankers[identity] = __instance;
            // Awake 时本体可能还没 SetParent 进 gameLayer（Castle.CatchupToLevel 先
            // Instantiate 再 SetParent）：身份就绪就立刻增强并绑定协调器，否则由
            // Update 的低频补做（见 Update_Postfix 的 IsBound 重试）。
            if (!GreekBankScope.IsCurrentBanker(__instance)) return;
            ApplyEnhancedWorkProfile(__instance);

            PatchEconomy_BankAssistants.EnsureForMainBanker(__instance);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
    }

    // === Update - 观测入账 / 工作参数 / 银行家数量控制 ===

    // Existing panel tick also services disabled native Bankers (e.g. clients).
    internal static void TickOwnedProfiles()
    {
        GreekBankScope.Scope scope = GreekBankScope.Current();
        if (scope == GreekBankScope.Scope.Active)
        {
            if (_knownBankers.Count == 0 || Time.frameCount < _nextLateBindFrame) return;
            _nextLateBindFrame = Time.frameCount + 30;
            _profileKeys.Clear();
            _profileKeys.AddRange(_knownBankers.Keys);
            foreach (ObjectIdentity key in _profileKeys)
            {
                Banker owner = _knownBankers[key];
                if (owner == null) { _knownBankers.Remove(key); continue; }
                if (!ObjectIdentity.TryGet(owner, out ObjectIdentity actual)) continue;
                if (!actual.Equals(key)) { _knownBankers.Remove(key); continue; }
                if (!GreekBankScope.IsCurrentBanker(owner)) continue;
                if (!_workProfiles.ContainsKey(key)) ApplyEnhancedWorkProfile(owner);
                if (!PatchEconomy_BankAssistants.IsBound(owner))
                    PatchEconomy_BankAssistants.EnsureForMainBanker(owner);
            }
            _profileKeys.Clear();
            return;
        }
        if (scope != GreekBankScope.Scope.Inactive) return;
        SuspendPrimeProof();
        FlushStagedLedger();
        if (_workProfiles.Count == 0) return;
        _profileKeys.Clear();
        _profileKeys.AddRange(_workProfiles.Keys);
        foreach (ObjectIdentity key in _profileKeys)
        {
            if (!_workProfiles.TryGetValue(key, out WorkProfile profile)) continue;
            Banker owner = profile.Owner;
            if (owner == null) { _workProfiles.Remove(key); continue; }
            if (!ObjectIdentity.TryGet(owner, out ObjectIdentity actual)) continue;
            if (!actual.Equals(key)) { _workProfiles.Remove(key); continue; }
            RestoreWorkProfile(owner);
        }
        _profileKeys.Clear();
    }

    // This adds a managed prefix to the already patched Banker.Update target.
    // Re-entry must prime before native work; initial loading keeps its existing
    // OpenCastleDoor/FinaliseEmerge/DayStart priming points.
    [HarmonyPatch(typeof(Banker), nameof(Banker.Update))]
    [HarmonyPrefix]
    public static void Update_Prefix(Banker __instance)
    {
        Managers managers = Managers.Inst;
        if (managers == null || managers.game == null || managers.game.state != Game.State.Playing
            || !GreekBankScope.IsAuthorityBanker(__instance)) return;
        if (_needsReprime || (_hasPrimedBanker && !IsPrimedFor(__instance)))
            TryPrimeSharedLedger(__instance);
    }

    [HarmonyPatch(typeof(Banker), nameof(Banker.Update))]
    [HarmonyPostfix]
    public static void Update_Postfix(Banker __instance)
    {
        GreekBankScope.Scope scope = GreekBankScope.Current();
        if (scope != GreekBankScope.Scope.Active)
        {
            // 离开希腊世界或总开关关闭：归还本补丁写过的银行工作参数（原值来自
            // 首次写入前的捕获，非硬编码），把已 staged 的共享余额落盘，并吊销 prime
            // 凭证——再入希腊必须重新 prime，绝不让新世界的原生余额溜进共享账本。
            // Unknown（加载中/身份/世界读取异常）暂缓：既不新增增强也不归还不吊销，
            // 保留 receipt 等世界明确后再处理。
            if (scope == GreekBankScope.Scope.Inactive)
            {
                RestoreWorkProfile(__instance);
                FlushStagedLedger();
                SuspendPrimeProof();
            }
            return;
        }

        // 行为增强同样要求“当前层的本体”：旧层残留/身份未就绪时不改、不记、不扫。
        // 客机（无 world auth）仍保留本地行为与视觉，只禁经济写入。
        if (!GreekBankScope.IsCurrentBanker(__instance)) return;

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

            // Awake 可能早于当前世界/层就绪（Castle.CatchupToLevel 先 Instantiate 再
            // SetParent），那样 EnsureForMainBanker 会被身份闸门挡下：低频补绑一次。
            if (!PatchEconomy_BankAssistants.IsBound(__instance))
                PatchEconomy_BankAssistants.EnsureForMainBanker(__instance);

            var allBankers = UnityEngine.Object.FindObjectsOfType<Banker>();
            int count = 0;
            bool hasOriginal = false;
            // 只统计/清理当前层的银行家：旧世界正在销毁的实例不属于本世界，
            // 不能据此销毁新世界的本体。
            foreach (var b in allBankers)
            {
                if (b == null || !GreekBankScope.IsInCurrentLayer(b.gameObject)) continue;
                count++;
                if (b.gameObject.name != "Banker_Extra") hasOriginal = true;
            }
            bool cleaned = false;
            if (hasOriginal)
            {
                foreach (var b in allBankers)
                {
                    if (b == null || b.gameObject.name != "Banker_Extra"
                        || !GreekBankScope.IsInCurrentLayer(b.gameObject)) continue;
                    RecordSkippedDuplicate(b);
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
                    "[Economy] Invariant violation: expected exactly one Banker/NetID 903 in the current layer, found " + count);
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
        // 只在当前希腊世界的本体上改写认领行为；其他世界/旧层残留完全走原生实现。
        if (!GreekBankScope.IsCurrentBanker(__instance)) return true;
        WorkProfile profile = EnsureProfile(__instance);
        if (profile == null) return true;
        if (ConfigureScannerForDomain(__instance, profile)) return true;

        // Native ClaimCoins normally clears this first. The fail-closed prefix must
        // preserve that invariant when no canonical wall domain can be resolved.
        if (__instance != null) __instance._targetCoin = null;
        return false;
    }

    // === ShouldHide - 已验证的积极工作模式（夜间不休息） ===

    [HarmonyPatch(typeof(Banker), nameof(Banker.ShouldHide))]
    [HarmonyPrefix]
    public static bool ShouldHide_Prefix(Banker __instance, ref bool __result)
    {
        if (!GreekBankScope.IsCurrentBanker(__instance)) return true;
        __result = false;
        return false;
    }

    [HarmonyPatch(typeof(Banker), nameof(Banker.ShouldEmerge))]
    [HarmonyPrefix]
    public static bool ShouldEmerge_Prefix(Banker __instance, ref bool __result)
    {
        if (!GreekBankScope.IsCurrentBanker(__instance)) return true;
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
        // 只在当前希腊世界改写原生银行家的外墙认领；其他世界走原生实现。
        if (!GreekBankScope.IsActive || __instance == null || claimer == null) return true;
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
