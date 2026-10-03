using System;
using Coatsink.Common;
using UnityEngine;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace KingdomEnhancedMod;

/// <summary>
/// 金币哥布林专用一币取款结果。三态语义：NotApplied=确定未扣（可结束/重选）、
/// Applied=已提交（之后的展示/落盘故障不改变经济结论，绝不重扣或回退成未扣）、
/// Indeterminate=无法确认（Before/After 为 -1 表示该值未知，调用方必须冻结钱包）。
/// 类型就放在本文件，离线测试工程只链接本文件即可，不额外耦合哥布林模块。
/// </summary>
internal enum CourierBankOutcome
{
    NotApplied = 0,
    Applied = 1,
    Indeterminate = 2,
}

internal enum CourierBankReason
{
    None = 0,
    ModDisabled,
    NoAuthority,
    Online,
    Paused,
    GateClosed,
    PrimeFailed,
    Empty,
    Unreadable,
    WriteUnknown,
    ReadbackUnknown,
    WriteFault,
    PresentationFailed,
}

internal readonly struct CourierBankDebit
{
    internal readonly CourierBankOutcome Outcome;
    internal readonly CourierBankReason Reason;
    internal readonly int Before;
    internal readonly int After;

    internal CourierBankDebit(CourierBankOutcome outcome, CourierBankReason reason, int before, int after)
    {
        Outcome = outcome;
        Reason = reason;
        Before = before;
        After = after;
    }
}

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
    // 增强收币阈值：原生容量×1，收满再回库；原为0.5，半容量即回库。
    private const float ENHANCED_COIN_GATHER_TARGET_PERCENTAGE = 1f;
    // 活动半径上限（Issue 100 起只作上限）：实际取 min(此值, 域内最小距离-epsilon)。
    private const float ENHANCED_WANDER_RANGE = 8.75f;
    // 域内活动 epsilon：设计安全余量 0.25，保持标准 ±9 域的既有 8.75 游走半径；
    // 此余量不是已证原生网格常量。
    private const float WANDER_DOMAIN_EPSILON = 0.25f;
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
        /// <summary>最后一次按哪个固定域代数写入域相关字段（-1=从未；0=域未知时写过）。</summary>
        internal int DomainAppliedGeneration = -1;

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

    /// <summary>
    /// Issue 100 固定域查询：唯一来源是 MainBankerFixedDomain 在一次成功完整加载后
    /// 发布的结构快照（campfire 左右最近固定墙基）。旧实现的 GetWall(1)/GetWall(0)/
    /// border 回退已全部移除：那些只反映“当前已建墙/地形边界”，不是固定墙基，
    /// 且会在墙缺失时扩域。域未知（未收到成功加载通知/半加载/换代未捕获/快照失败）
    /// 一律 fail-closed。参数 kingdom 保留给既有调用方作非空前置；上下文一致性
    /// 由域缓存自身按 world/kingdom/layer/scene 校验。
    /// </summary>
    internal static bool TryGetMainBankerDomain(Kingdom kingdom,
        out float left, out float right)
    {
        left = 0f;
        right = 0f;
        if (kingdom == null) return false;
        return MainBankerFixedDomain.TryGetDomain(out left, out right);
    }

    internal static bool IsInMainBankerDomain(Kingdom kingdom, float x)
    {
        return TryGetMainBankerDomain(kingdom, out float left, out float right)
            && MainBankerFixedDomain.IsInside(x, left, right);
    }

    internal static bool IsInMainBankerDomain(float x, float left, float right)
    {
        return MainBankerFixedDomain.IsInside(x, left, right);
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
            Claim(ref profile.GatherPercentage, banker.coinGatherTargetPercentage, ENHANCED_COIN_GATHER_TARGET_PERCENTAGE);
            banker.coinGatherTargetPercentage = ENHANCED_COIN_GATHER_TARGET_PERCENTAGE;
            Claim(ref profile.WalkSpeed, banker.walkSpeed, 1.95f);
            banker.walkSpeed = 1.95f;
            Claim(ref profile.RunSpeed, banker.runSpeed, 3.6f);
            banker.runSpeed = 3.6f;
            ApplyWanderForDomain(banker, profile);
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
        // 记录本次按哪个域代数写入（域未知=0）；换代后由各本体 prefix 依此刷新，
        // 不再依赖“谁拿到一次刚发布的 bool”。
        profile.DomainAppliedGeneration = MainBankerFixedDomain.ReadyGeneration;
    }

    /// <summary>
    /// Issue 100：按已发布域代数为单个本体刷新域相关字段（wander/扫描）。每个本体
    /// 各自对齐；readyGeneration==0（域未知）时不刷新本体值——fail-closed 回收由
    /// 120 帧巡检与离开 scope 的归还路径负责。
    /// </summary>
    internal static void RefreshDomainProfileForGeneration(Banker banker, int readyGeneration)
    {
        if (banker == null || readyGeneration <= 0) return;
        WorkProfile profile = EnsureProfile(banker);
        if (profile == null || profile.DomainAppliedGeneration == readyGeneration) return;
        ApplyEnhancedWorkProfile(banker);
    }

    /// <summary>
    /// Issue 100：wander 半径=min(8.75, 域内最小距离-epsilon)，活动目标不越过固定墙基。
    /// 域未知时归还原生值（fail-closed），绝不写无根据的常量；receipt 复用既有 owned 通道。
    /// </summary>
    private static void ApplyWanderForDomain(Banker banker, WorkProfile profile)
    {
        Managers managers = Managers.Inst;
        Kingdom kingdom = managers != null ? managers.kingdom : null;
        float campfire = kingdom != null ? kingdom.campfirePosition : float.NaN;
        if (kingdom == null
            || !MainBankerFixedDomain.TryGetDomain(out float left, out float right)
            || !MainBankerFixedDomain.IsFinite(campfire))
        {
            if (NeedsRestore(ref profile.WanderRange, banker.wanderRange))
            {
                banker.wanderRange = profile.WanderRange.Original;
                profile.WanderRange.Owned = false;
            }
            return;
        }

        float minDistance = Mathf.Min(campfire - left, right - campfire);
        float desired = Mathf.Min(ENHANCED_WANDER_RANGE,
            Mathf.Max(0f, minDistance - WANDER_DOMAIN_EPSILON));
        Claim(ref profile.WanderRange, banker.wanderRange, desired);
        banker.wanderRange = desired;
    }

    /// <summary>
    /// 方向扫描器只是优化，不是硬门：硬边界由 claim/钱包门保证。这里在已证域内时把
    /// 扫描范围贴到两侧固定墙基；域未知、本体位置/缩放非有限或本体已在域外时一律
    /// 收敛为 0（扫描不到任何币），等待回位与下一次对齐。绝不用 max(0.1,...) 把
    /// 域外方向当合法绝对边界。
    /// </summary>
    private static bool ConfigureScannerForDomain(Banker banker, WorkProfile profile)
    {
        if (banker == null || profile == null) return false;
        Scanner scanner = banker._coinScanner;
        if (!MainBankerFixedDomain.TryGetDomain(out float left, out float right))
            return FailClosedScanner(profile, banker, scanner);

        float x;
        float scale;
        try
        {
            x = banker.transform.position.x;
            scale = banker.transform.localScale.x;
        }
        catch
        {
            return FailClosedScanner(profile, banker, scanner);
        }
        if (!MainBankerFixedDomain.IsFinite(x) || !MainBankerFixedDomain.IsFinite(scale)
            || !MainBankerFixedDomain.IsInside(x, left, right))
            return FailClosedScanner(profile, banker, scanner);

        float magnitude = Mathf.Abs(scale);
        bool facesRight = scale >= 0f;
        float forward = (facesRight ? right - x : x - left) / magnitude;
        float behind = (facesRight ? x - left : right - x) / magnitude;
        if (!MainBankerFixedDomain.IsFinite(forward) || !MainBankerFixedDomain.IsFinite(behind)
            || forward < 0f || behind < 0f)
            return FailClosedScanner(profile, banker, scanner);

        float scanRange = Mathf.Max(forward, behind);
        Claim(ref profile.CoinScanRange, banker.coinScanRange, scanRange);
        banker.coinScanRange = scanRange;
        WriteScannerValues(profile, scanner, forward, behind, 1f);
        return true;
    }

    private static bool FailClosedScanner(WorkProfile profile, Banker banker, Scanner scanner)
    {
        Claim(ref profile.CoinScanRange, banker.coinScanRange, 0f);
        banker.coinScanRange = 0f;
        WriteScannerValues(profile, scanner, 0f, 0f, 1f);
        return false;
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

        // 开始归还即撤销“已应用域代数”标记：若后续字段 setter 抛错（部分归还），
        // profile 会保留，而标记若仍等于当前域代数，重新 enable 的首帧 prefix 会跳过
        // Apply，wander/扫描器停留在原生值。撤销后下一次 prefix 必定重新应用。
        // 只动本 profile 元数据，不碰 ledger/save/finance。
        profile.DomainAppliedGeneration = -1;

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

    /// <summary>One synchronous procurement debit; native shop PerformPay records CoinsSpent.</summary>
    internal static bool TrySpendForAutoRestock(Banker banker, int amount)
    {
        if (Time.timeScale <= 0f || amount <= 0 || amount > 200
            || !GreekBankScope.IsAuthorityBanker(banker)) return false;
        var managers = Managers.Inst;
        var kingdom = managers != null ? managers.kingdom : null;
        if (managers == null || managers.game == null || managers.game.state != Game.State.Playing
            || kingdom == null
            || !BankAssistantCoordinator.IsCurrentRestockBanker(banker)
            || !TryPrimeSharedLedger(banker)) return false;
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

    /// <summary>
    /// 金币哥布林的银行闸门：唯一实现是 <see cref="CoinCourierBankScope"/> 的 903 登记解析器；
    /// 传入的 banker 必须就是当前权威解析结果本体（原生读档允许 kingdom.banker 为 null，
    /// 只有它非空且指向别体才是冲突）。与 GreekBankScope.IsAuthorityBanker 不同：
    /// 允许所有 biome，且不读写任何账本。只做身份读取；离线与暂停由资金入口另行验证。
    /// </summary>
    internal static bool IsCourierBankAuthority(Banker banker)
        => CoinCourierBankScope.IsCurrentAuthorityBanker(banker);

    /// <summary>
    /// Read-only scheduling hint for the courier. A Greek banker's native field is not
    /// authoritative until the existing shared-ledger prime belongs to this banker and
    /// world. Unknown must still reach the normal withdrawal path, which owns priming,
    /// commit, readback and fault handling. No ledger or PlayerPrefs access occurs here.
    /// </summary>
    internal static bool TryReadCourierStash(Banker banker, out int coins)
    {
        coins = 0;
        try
        {
            if (!IsCourierBankAuthority(banker)) return false;
            GreekBankScope.Scope scope = GreekBankScope.Current();
            if (scope == GreekBankScope.Scope.Unknown) return false;
            if (scope == GreekBankScope.Scope.Active && (_needsReprime || !IsPrimedFor(banker))) return false;
            coins = banker._stashedCoins;
            return true;
        }
        catch (Exception)
        {
            coins = 0;
            return false;
        }
    }

    /// <summary>
    /// 最小专用一币取款：只服务金币哥布林装袋，不暴露通用 debit/refund，不经过
    /// 税收助手/自动采购资格门。希腊先 prime 共享账并同调用同步内存账；其他世界
    /// 只扣自身原生 _stashedCoins，绝不读写共享键。提交后的 Castle/Stats/PlayerPrefs
    /// 故障只记原因，绝不让结果退化成 NotApplied 或再次扣款；写入异常一律先读回实际
    /// 字段判定，读不回即 Indeterminate 交给调用方冻结钱包。
    /// </summary>
    internal static CourierBankDebit TryWithdrawOneCoinForCourier(Banker banker)
    {
        if (Time.timeScale <= 0f) return new CourierBankDebit(CourierBankOutcome.NotApplied, CourierBankReason.Paused, -1, -1);
        if (!NetworkBigBoss.HasWorldAuth) return new CourierBankDebit(CourierBankOutcome.NotApplied, CourierBankReason.NoAuthority, -1, -1);
        if (NetworkBigBoss.IsOnline) return new CourierBankDebit(CourierBankOutcome.NotApplied, CourierBankReason.Online, -1, -1);
        if (ModConfig.Enabled == null || !ModConfig.Enabled.Value)
            return new CourierBankDebit(CourierBankOutcome.NotApplied, CourierBankReason.ModDisabled, -1, -1);
        bool authority;
        try { authority = IsCourierBankAuthority(banker); }
        catch (Exception e)
        {
            LogCourierError("gate read fault", e);
            return new CourierBankDebit(CourierBankOutcome.NotApplied, CourierBankReason.GateClosed, -1, -1);
        }
        if (!authority) return new CourierBankDebit(CourierBankOutcome.NotApplied, CourierBankReason.GateClosed, -1, -1);

        GreekBankScope.Scope scope = GreekBankScope.Current();
        if (scope == GreekBankScope.Scope.Unknown)
            return new CourierBankDebit(CourierBankOutcome.NotApplied, CourierBankReason.GateClosed, -1, -1);
        bool greek = scope == GreekBankScope.Scope.Active;

        if (greek && !TryPrimeSharedLedger(banker))
            return new CourierBankDebit(CourierBankOutcome.NotApplied, CourierBankReason.PrimeFailed, -1, -1);

        int before;
        try { before = banker._stashedCoins; }
        catch (Exception e)
        {
            LogCourierError("treasury read fault", e);
            return new CourierBankDebit(CourierBankOutcome.NotApplied, CourierBankReason.Unreadable, -1, -1);
        }
        if (before <= 0)
            return new CourierBankDebit(CourierBankOutcome.NotApplied, CourierBankReason.Empty, before, before);
        int updated = before - 1;

        bool writeFault = false;
        try
        {
            // 本赋值就是原子经济提交；从这里开始结果不允许再变成 NotApplied。
            banker._stashedCoins = updated;
        }
        catch (Exception e)
        {
            // 原生 setter 异常不代表没写进去：先读回实际字段再判定。
            int observed;
            try { observed = banker._stashedCoins; }
            catch (Exception readFault)
            {
                LogCourierError("treasury write unverifiable", e);
                LogCourierError("treasury readback fault", readFault);
                return new CourierBankDebit(CourierBankOutcome.Indeterminate, CourierBankReason.ReadbackUnknown, before, -1);
            }
            if (observed != updated)
            {
                LogCourierError("treasury write fault", e);
                return new CourierBankDebit(CourierBankOutcome.NotApplied, CourierBankReason.WriteFault, before, observed);
            }
            writeFault = true; // 写入实际已落：按已提交继续
        }

        if (greek)
        {
            _sharedStash = updated;
            _lastObservedStash = updated;
        }

        bool presentation = false;
        if (greek)
        {
            try
            {
                PlayerPrefs.SetInt(SHARED_STASH_KEY, updated);
                _sharedLedgerDirty = true;
            }
            catch (Exception e)
            {
                _lastObservedStash = int.MinValue; // 既有 Update 会重试落盘这笔已提交的扣款
                presentation = true;
                LogCourierError("courier debit committed; ledger staging failed", e);
            }
        }
        try
        {
            Managers managers = Managers.Inst;
            Kingdom kingdom = managers != null ? managers.kingdom : null;
            if (kingdom != null && kingdom.castle != null) kingdom.castle.SetStash(updated);
            if (managers != null && managers.stats != null)
                managers.stats.SetStat(Stat.CoinsInBank, updated, false);
        }
        catch (Exception e)
        {
            presentation = true;
            LogCourierError("courier debit committed; display refresh failed", e);
        }

        CourierBankReason reason = writeFault ? CourierBankReason.WriteFault
            : presentation ? CourierBankReason.PresentationFailed
            : CourierBankReason.None;
        return new CourierBankDebit(CourierBankOutcome.Applied, reason, before, updated);
    }

    private static void LogCourierError(string what, Exception error)
        => KingdomEnhancedPlugin.Instance?.LogSource.LogError(
            "[CoinCourier] " + what + ": " + error.GetType().Name);

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

        // Issue 100：固定域的结构快照/刷新已移到独立的 Banker.Update 固定域 prefix
        // （prepare→profile→movement，发生在原生本帧行为之前），这里不再重复捕获。

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

            // Issue 100：低频快照回收本银行家仍持有的域外 Player/Coins 认领——
            // 原生 ClaimCoins 一次可能认领多枚，不能只清 _targetCoin。
            ReleaseOutsideDomainBankerClaims(__instance);

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

    // === Issue 100 固定域：域外认领低频回收 ===

    private const float CLAIM_RELEASE_SCAN_RANGE = float.MaxValue;
    private static readonly Il2CppReferenceArray<DroppableCurrency> _domainClaimBuffer =
        new Il2CppReferenceArray<DroppableCurrency>(1024);

    /// <summary>
    /// 低频快照归还：遍历 registrar 掉落物列表，只释放 exact
    /// friendlyClaimer==banker.gameObject 的域外 Player/Coins（含非有限坐标）。
    /// 只调原生 ClearFriendlyClaimIfClaimer（对象自带 claimer 限定），不触碰别人的
    /// 认领、农田来源、拾取策略或余额；无权/身份不符/域未知时不做任何写。
    /// </summary>
    private static void ReleaseOutsideDomainBankerClaims(Banker banker)
    {
        if (banker == null || banker.gameObject == null) return;
        if (!GreekBankScope.IsAuthorityBanker(banker)) return;
        Managers managers = Managers.Inst;
        Kingdom kingdom = managers != null ? managers.kingdom : null;
        DroppableRegistrar registrar = managers != null ? managers.dropManager : null;
        if (kingdom == null || registrar == null) return;
        if (!MainBankerFixedDomain.TryGetDomain(out float left, out float right)) return;

        int count;
        try
        {
            registrar.GetDroppablesInRange<DroppableCurrency>(
                kingdom.campfirePosition, CLAIM_RELEASE_SCAN_RANGE, _domainClaimBuffer, out count, null);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[BankerDomain] claim sweep read failed: " + e.GetType().Name);
            return;
        }
        if (count > _domainClaimBuffer.Length) count = _domainClaimBuffer.Length;
        for (int i = 0; i < count; i++)
            TryReleaseOutsideDomainClaim(banker, _domainClaimBuffer[i], left, right);
        ClearTargetCoinIfOutsideDomain(banker, left, right);
    }

    private static void TryReleaseOutsideDomainClaim(Banker banker, DroppableCurrency coin,
        float left, float right)
    {
        if (coin == null || coin.gameObject == null || !coin.isActiveAndEnabled) return;
        if (coin.droppedBy != DropType.Player || coin.CurrencyType != CurrencyType.Coins) return;
        GameObject claimer;
        try { claimer = coin.friendlyClaimer; } catch { return; }
        if (claimer == null || claimer != banker.gameObject) return; // exact 本人
        if (!GreekBankScope.IsInCurrentLayer(coin)) return;
        float x;
        try { x = coin.transform.position.x; } catch { return; }
        if (MainBankerFixedDomain.IsInside(x, left, right)) return; // 域内保留
        ClearBankerClaim(coin, banker);
    }

    /// <summary>
    /// 明确证据才清 target：目标是非有限/域外 Player/Coins（且已归还本人认领）。
    /// 域内目标一律保留，不影响原生移动/拾取流程。
    /// </summary>
    private static void ClearTargetCoinIfOutsideDomain(Banker banker, float left, float right)
    {
        DroppableCurrency target;
        try { target = banker._targetCoin; } catch { return; }
        if (target == null || target.gameObject == null) return;
        if (target.droppedBy != DropType.Player || target.CurrencyType != CurrencyType.Coins) return;
        // 只处理仍属当前 world/layer/scene 的币：旧/异层残留不得写认领或清引用。
        if (!GreekBankScope.IsInCurrentLayer(target.gameObject)) return;
        float x;
        try { x = target.transform.position.x; } catch { return; }
        if (MainBankerFixedDomain.IsInside(x, left, right)) return;
        try
        {
            if (target.friendlyClaimer == banker.gameObject) ClearBankerClaim(target, banker);
            banker._targetCoin = null;
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[BankerDomain] target clear failed: " + e.GetType().Name);
        }
    }

    private static void ClearBankerClaim(DroppableCurrency coin, Banker banker)
    {
        try { coin.ClearFriendlyClaimIfClaimer(banker.gameObject); }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[BankerDomain] claim release failed: " + e.GetType().Name);
        }
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
/// Issue 100 认领硬门之一：当前希腊本体的原生银行家只能在已发布固定域内认领
/// 玩家金币；域未知/非有限/域外一律否决，并把币留给既有助手。其他世界、
/// 其他认领者、其他币种继续走原生 TryFriendlyClaim。
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
        // 只在当前希腊世界改写当前本体的认领；其他世界/旧层残留走原生实现。
        if (!GreekBankScope.IsActive || __instance == null || claimer == null) return true;
        Banker banker = claimer.GetComponent<Banker>();
        if (banker == null || !GreekBankScope.IsCurrentBanker(banker)) return true;
        DroppableCurrency coin = __instance.TryCast<DroppableCurrency>();
        if (coin == null || coin.gameObject == null
            || coin.droppedBy != DropType.Player
            || coin.CurrencyType != CurrencyType.Coins) return true;
        // 门只作用于当前层的币；异层残留不拦截（也不会被当前域的判断误用）。
        if (!GreekBankScope.IsInCurrentLayer(coin.gameObject)) return true;

        // 已证 current-banker + 本币型别 + 当前层：位置读故障无法证明域内 → fail-closed。
        float x;
        try { x = coin.transform.position.x; }
        catch { __result = false; return false; }
        // NaN/Infinity 拒绝所有认领；域未知/域外同样拒绝（fail-closed）。
        if (MainBankerFixedDomain.TryGetDomain(out float left, out float right)
            && MainBankerFixedDomain.IsInside(x, left, right)) return true;

        __result = false;
        return false;
    }
}

/// <summary>
/// Issue 100 固定域唯一入口：独立 Banker.Update 前缀（与既有 ledger priming 前缀共存，
/// 不修改其语义）。明确顺序 prepare → profile → movement，全部发生在原生本帧
/// Idle/ClaimCoins 之前：
///   - prepare：成功加载通知后的下一个安全维护点捕获固定域（结构快照只读，
///     不需要 worldAuth，也不解除任何网络门；失败有界低频重试）；
///   - profile：按各本体 WorkProfile.DomainAppliedGeneration 刷新域内 wander 半径与
///     扫描范围（每个本体各自对齐，不依赖谁先消费“刚发布”的一次性信号）；
///   - movement：只处理确证的旧域外位置型 goal——
///     仅 Current == GrabCoin(0) / Idle(1) 且 movingToGoal 且 goalMode == Position；
///     任何 _executeQueuedState==true 本帧一律不覆盖（先让原生消费，下一帧按新
///     Current 判断，DropOff(4)/Payout(5) 永不被取消）；goal 非有限不动；
///     goal 在域内一律不动（Actor 域外也自然归位，禁止每帧重排）。
/// 取消动作 = Mover.Stop + GoToState(Idle)，让原生 Update 的 FSM 执行切换；绝不
/// StopAllCoroutines / GoToAndUpdate；GrabCoin 取消或目标滚出域时只归还仍属本人的
/// 域外目标认领并清 target（明确证据）。
/// </summary>
[HarmonyPatch(typeof(Banker), nameof(Banker.Update))]
public static class Banker_FixedDomain_Patch
{
    private const int StateGrabCoin = 0;
    private const int StateIdle = 1;

    [HarmonyPrefix]
    public static void Prefix(Banker __instance)
    {
        Managers managers = Managers.Inst;
        if (managers == null || managers.game == null
            || managers.game.state != Game.State.Playing) return;
        if (GreekBankScope.Current() != GreekBankScope.Scope.Active) return;

        // prepare：捕获只读；不因客机无 worldAuth 而跳过，也不影响任何权威门。
        MainBankerFixedDomain.Maintain(managers);

        if (__instance == null || __instance.gameObject == null) return;
        if (!GreekBankScope.IsCurrentBanker(__instance)) return;

        // profile：本体按已发布域代数各自刷新（首个拿到发布的调用方不再独占信号）。
        PatchEconomy_Banker.RefreshDomainProfileForGeneration(
            __instance, MainBankerFixedDomain.ReadyGeneration);

        try
        {
            if (!GreekBankScope.IsAuthorityBanker(__instance)) return;
            if (!MainBankerFixedDomain.TryGetDomain(out float left, out float right)) return;

            StateMachine fsm = __instance._fsm;
            Mover mover = __instance._mover;
            if (fsm == null || mover == null) return;

            int current = (int)fsm.Current;
            if (current != StateGrabCoin && current != StateIdle) return; // 财务状态不动
            if (fsm._executeQueuedState) return;                          // 已排队切换：让原生消费
            if (!mover.movingToGoal) return;                              // 只处理正在执行的 goal
            if (mover.goalMode != Mover.GoalMode.Position) return;        // 只认位置型 goal

            float goal = mover._goalPosition;
            if (!MainBankerFixedDomain.IsFinite(goal)) return;            // 读不到目标不动作
            if (MainBankerFixedDomain.IsInside(goal, left, right))
            {
                // goal 仍向内：即使目标币已滚出域，也只归还那枚外币认领，
                // 让已有域内移动自然完成（不 Stop、不重排、不清 target）。
                if (current == StateGrabCoin)
                    ReleaseTargetClaimIfCoinOutside(__instance, left, right, clearTarget: false);
                return;
            }

            if (current == StateGrabCoin)
                ReleaseTargetClaimIfCoinOutside(__instance, left, right, clearTarget: true);
            mover.Stop();
            fsm.GoToState(StateIdle);
        }
        catch
        {
            // 读取异常：保留原生流程；最终钱包门仍拒绝域外拾取。
        }
    }

    private static void ReleaseTargetClaimIfCoinOutside(Banker banker, float left, float right,
        bool clearTarget)
    {
        DroppableCurrency target;
        try { target = banker._targetCoin; } catch { return; }
        if (target == null || target.gameObject == null) return;
        if (target.droppedBy != DropType.Player || target.CurrencyType != CurrencyType.Coins) return;
        // 只处理仍属当前 world/layer/scene 的币：旧/异层残留不得写认领或清引用。
        if (!GreekBankScope.IsInCurrentLayer(target.gameObject)) return;
        float x;
        try { x = target.transform.position.x; } catch { return; }
        if (MainBankerFixedDomain.IsInside(x, left, right)) return; // 目标仍在域内：保留
        try
        {
            if (target.friendlyClaimer == banker.gameObject)
                target.ClearFriendlyClaimIfClaimer(banker.gameObject);
            // 明确证据（域外 Player/Coins）才清 target；goal 仍向内时只释放认领。
            if (clearTarget) banker._targetCoin = null;
        }
        catch
        {
        }
    }
}

/// <summary>
/// Issue 100 钱包最终硬门。原生 Wallet.SuckCurrency 的拾取判定在 CanBePickedUp 里
/// 对 Everyone/ExceptDropper 策略根本不看 friendlyClaimer，银行家钱包可能在碰撞中
/// 绕过认领门拾取域外金币。这里做最窄拦截：只对 exact 当前 authority 本体银行家的
/// 钱包（owner._wallet.Pointer == __instance.Pointer，owner 由钱包自身 GameObject 取，
/// 不依赖读档期可能为 null 的 kingdom.banker），当前层、Player/Coins。
/// 域内放行原生；域未知/坐标非有限/域外 __result=false 并跳过原生。
/// 其他钱包、其他币种/来源、其他层保持原生（不在 postfix 退款，不碰余额）。
/// </summary>
[HarmonyPatch(typeof(Wallet), nameof(Wallet.SuckCurrency))]
public static class Wallet_MainBankerFixedDomainCurrency_Patch
{
    [HarmonyPrefix]
    public static bool Prefix(Wallet __instance, DroppableCurrency currency,
        bool playSound, ref bool __result)
    {
        try
        {
            if (__instance == null || currency == null || currency.gameObject == null) return true;
            GameObject walletGo = __instance.gameObject;
            if (walletGo == null) return true;
            Banker owner = walletGo.GetComponent<Banker>();
            if (owner == null || owner._wallet == null
                || owner._wallet.Pointer != __instance.Pointer) return true; // 不是本体钱包
            if (!GreekBankScope.IsAuthorityBanker(owner)) return true;       // 其他世界/客机：原生
            if (currency.droppedBy != DropType.Player
                || currency.CurrencyType != CurrencyType.Coins) return true; // 其他币：原生
            if (!GreekBankScope.IsInCurrentLayer(currency.gameObject)) return true; // 其他层：原生

            // 已证 exact 本体钱包 + 当前层 Player/Coins：位置读故障无法证明域内 → fail-closed。
            float x;
            try { x = currency.transform.position.x; }
            catch { __result = false; return false; }
            if (MainBankerFixedDomain.TryGetDomain(out float left, out float right)
                && MainBankerFixedDomain.IsInside(x, left, right)) return true;

            __result = false; // 域未知/非有限/域外：拒绝进入钱包
            return false;
        }
        catch
        {
            return true; // 资格证明之前读取失败：交给原生（不新增行为）
        }
    }
}
