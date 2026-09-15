using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 英雄弓箭手 1.25 倍射程（combat slice；只给**当选英雄本人**用，绝不改共享 SO）。
///
/// 为什么不是"发射后再补冲量"：`ArrowAttack.BestShotInternal` 先按目标解弹道角/速度（用 SO 的初速上限），
/// 最后 `Vector2.ClampMagnitude(solution, _shotMagnitude)` 截断，再由 `FireArrowInternal` 施加冲量。
/// 更高初速会得到**不同的发射角**（同一个目标仍被解算命中，只是抛物线形状变了）——本模块不去标定
/// 近端轨迹"逐点不变"，它只保证解算输入（SO 的两个 magnitude）与公式（gravity/出膛点/prefab）保持原生。
/// 事后补力则会把已解算好的近目标解整体放大（过冲），也会与方法内已发出的网络速度不一致，故不采用。
///
/// 本文件只做四件事（无新 Harmony 钩子、无 getter detour、无全局 SO 写入）：
/// 1) <see cref="Apply"/>：给**一个** actor 建私有克隆（普通箭 + 火矢；`_fireArrowAttack` 与 `_arrowAttack`
///    是同一资产时**只建一个**克隆并同时重定向两个字段），把 actor 的字段重定向到克隆；`ActiveArrowAttack`
///    原本指向哪个原资产就指向对应克隆（原生 buff 自己切 `_fireArrowAttack` 时同样落在克隆上）。
///    同时把 `_enemyScanner.range/rangeBehind` 对齐 `Archer.shootRange`（wildlife 扫描器不动）。
///    **返回 true 仅当克隆与扫描器都就绪**；任何失败都回滚并返回 false（operator 据此撤销英雄）。
/// 2) <see cref="Tick"/>（bool）：镜像扫描器 + 观察所有权；**false = 所有权已丢失，operator 应退役该英雄**。
/// 3) <see cref="Restore"/>：CAS 归还 —— 只写回仍指向本模块克隆的字段、只归还本模块写入的扫描器值；
///    第三方改过的一律不覆盖（fail-closed）。任何异常都**保留 pending 回执**，由 <see cref="RetryCleanup"/> 重试。
/// 4) <see cref="RetryCleanup"/>/<see cref="Clear"/>：供 ModPanel 在功能关闭时也继续服务 pending 归还。
///
/// 身份：所有匹配都用 **native pointer + GameObject InstanceID**（IL2CPP wrapper 每帧可能是不同 C# 对象，
/// 绝不用 ReferenceEquals 认人）；写字段前复核 actor 的当前身份与登记一致。扫描器记录其 Pointer，
/// 身份不符即放弃认领，绝不覆盖。
///
/// 边界：最多 <see cref="MaxHeroes"/> 个 actor；每 actor 幂等（绝不二次 ×1.118、绝不克隆自己的克隆）；
/// 条目在**完全归还**（字段脱钩 + 自有克隆销毁）之前不会被再次出租，也绝不被 Clear 丢弃；
/// 所有入口异常隔离，绝不外抛进原生调用链。实机命中/观感未经本 slice 验证。
/// </summary>
internal static class HeroArcherRange
{
    /// <summary>索敌/射程倍率（用户确认 1.25）。</summary>
    internal const float RangeFactor = 1.25f;
    /// <summary>初速倍率 = sqrt(1.25)：Range = force²/-gravity，射程 ∝ v²。</summary>
    internal const float SpeedFactor = 1.118033988749895f;
    /// <summary>同时受管的英雄上限（每侧 1 名 → 最多 2）。</summary>
    internal const int MaxHeroes = 2;
    private const string CloneName = "KEM_HeroAttack";
    private const float ValueEpsilon = 1e-4f;

    private sealed class Entry
    {
        // ---- 身份（IL2CPP：pointer + GO InstanceID）----
        internal IntPtr Pointer;
        internal int GoId;
        /// <summary>有界强引用（≤2），仅供 pending 清理重试使用；认人一律用 Pointer/GoId。</summary>
        internal Archer Archer;

        // ---- 自有资产（创建即登记，任何失败路径都能找到并销毁）----
        internal ArrowAttack BaseNormal, BaseFire;
        internal ArrowAttack CloneNormal, CloneFire;      // 同一资产时两者是同一个克隆对象
        internal bool ClonesOwned;

        internal bool Applied;                            // 字段已重定向
        internal bool Pending;                            // 还有字段/克隆未清：需重试，且不可再出租
        internal bool NormalClaimLost, FireClaimLost;     // 第三方换走基线：放弃该字段的归还权

        // ---- 扫描器认领 ----
        internal bool ScannerClaimed;
        internal IntPtr ScannerPtr;
        internal float BaseScanRange, BaseScanRangeBehind;
        internal float ClaimedScanRange, ClaimedScanRangeBehind;
        internal bool RangeClaim, BehindClaim;
    }

    private static readonly Entry[] Entries = new Entry[MaxHeroes];
    private static readonly HashSet<string> Logged = new HashSet<string>();

    private static void Once(string key, string message)
    {
        try
        {
            if (Logged.Add(key)) KingdomEnhancedPlugin.Instance?.LogSource?.LogWarning("[HeroRange] " + message);
        }
        catch (Exception) { }
    }

    // ============================================================
    // 身份与观察面
    // ============================================================

    private static bool Identity(Archer archer, out IntPtr pointer, out int goId)
    {
        pointer = IntPtr.Zero;
        goId = 0;
        if (archer == null || archer.gameObject == null) return false;
        try
        {
            pointer = archer.Pointer;
            goId = archer.gameObject.GetInstanceID();
        }
        catch (Exception) { return false; }
        return pointer != IntPtr.Zero && goId != 0;
    }

    /// <summary>按 native pointer + GO InstanceID 找条目（wrapper 换了也认得同一个 actor）。</summary>
    private static Entry Find(Archer archer)
    {
        IntPtr pointer; int goId;
        if (!Identity(archer, out pointer, out goId)) return null;
        for (int i = 0; i < Entries.Length; i++)
        {
            Entry entry = Entries[i];
            if (entry == null || entry.Pointer == IntPtr.Zero) continue;
            if (entry.Pointer == pointer && entry.GoId == goId) return entry;
        }
        return null;
    }

    /// <summary>写字段前的owner复核：当前 wrapper 必须仍指向登记的那个 actor。</summary>
    private static bool StillOwner(Entry entry, Archer archer)
    {
        IntPtr pointer; int goId;
        if (!Identity(archer, out pointer, out goId)) return false;
        return entry.Pointer == pointer && entry.GoId == goId;
    }

    internal static bool IsApplied(Archer archer)
    {
        Entry entry = Find(archer);
        return entry != null && entry.Applied;
    }

    internal static int AppliedCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < Entries.Length; i++)
                if (Entries[i] != null && Entries[i].Applied) count++;
            return count;
        }
    }

    /// <summary>还有多少条待归还的 pending 回执（ModPanel 可据此观察/重试）。</summary>
    internal static int PendingCleanupCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < Entries.Length; i++)
                if (Entries[i] != null && Entries[i].Pending) count++;
            return count;
        }
    }

    // ============================================================
    // Operator/runtime 接入面
    // ============================================================

    /// <summary>
    /// 当选英雄时调用。true = 克隆与扫描器**都**就绪；false = 已回滚（operator 应撤销该英雄）。
    /// 幂等：已应用的 actor 再调用只重新对齐扫描器，绝不二次放大。
    /// </summary>
    internal static bool Apply(Archer archer)
    {
        try
        {
            Entry entry = Find(archer);
            if (entry != null)
            {
                if (entry.Pending) return false;                       // 待归还：绝不复用该条目
                if (entry.Applied)
                {
                    if (!StillOwner(entry, archer)) return false;
                    return ClaimScanner(entry, archer) && EntryHealthy(entry, archer);
                }
            }
            Entry slot = entry ?? Rent(archer);
            if (slot == null) return false;

            ArrowAttack baseNormal = archer._arrowAttack;
            if (baseNormal == null || !Usable(baseNormal))
            {
                Once("no-base-so", "archer has no usable base ArrowAttack; hero range left native");
                ReleaseEntry(slot);
                return false;
            }
            if (IsOwnedClone(baseNormal))
            {
                Once("clone-of-clone", "arrow attack already points at a KEM hero clone; refusing to clone again");
                ReleaseEntry(slot);
                return false;
            }

            slot.BaseNormal = baseNormal;                              // 先记基线：后续任何失败都能清理
            slot.Applied = false;
            slot.Pending = false;
            slot.NormalClaimLost = slot.FireClaimLost = false;
            slot.ClonesOwned = false;

            ArrowAttack cloneNormal = CreateClone(baseNormal, slot, fire: false);
            if (cloneNormal == null)
            {
                Cleanup(slot, archer);                                 // 失败的克隆已自行销毁；这里只释放记账
                return false;
            }

            ArrowAttack baseFire = archer._fireArrowAttack;
            ArrowAttack cloneFire = null;
            if (baseFire != null && Same(baseFire, baseNormal))
            {
                // 同一资产：一个克隆同时服务两个字段；归还时两个字段都回同一基线资产（克隆只销毁一次）。
                cloneFire = cloneNormal;
                slot.BaseFire = baseNormal;
                slot.CloneFire = cloneNormal;
            }
            else if (baseFire != null)
            {
                if (!Usable(baseFire))
                {
                    Once("fire-unusable", "fire ArrowAttack has non-positive magnitude; hero range left native");
                    Cleanup(slot, archer);
                    return false;
                }
                if (IsOwnedClone(baseFire))
                {
                    Once("clone-of-clone-fire", "fire arrow attack already points at a KEM hero clone; refusing");
                    Cleanup(slot, archer);
                    return false;
                }
                slot.BaseFire = baseFire;
                cloneFire = CreateClone(baseFire, slot, fire: true);
                if (cloneFire == null)
                {
                    Cleanup(slot, archer);
                    return false;
                }
            }

            ArrowAttack activeBefore = archer.ActiveArrowAttack;
            slot.Applied = true;
            try
            {
                archer._arrowAttack = cloneNormal;
                if (cloneFire != null) archer._fireArrowAttack = cloneFire;
                if (Same(activeBefore, baseNormal)) archer.ActiveArrowAttack = cloneNormal;
                else if (baseFire != null && Same(activeBefore, baseFire)) archer.ActiveArrowAttack = cloneFire;
            }
            catch (Exception e)
            {
                Once("apply-failed", "hero range apply failed; rolling back: " + e);
                slot.NormalClaimLost = slot.FireClaimLost = false;
                Cleanup(slot, archer);
                return false;
            }

            if (!ClaimScanner(slot, archer))
            {
                // 扫描器认领不了 → 不算"完整加成"：整体回滚（绝不留下半个射程提升）。
                Cleanup(slot, archer);
                return false;
            }
            return true;
        }
        catch (Exception e)
        {
            Once("apply", "hero range apply threw; left native: " + e);
            return false;
        }
    }

    /// <summary>
    /// runtime 对当前英雄每帧调用（**返回值有意义**）：
    /// true = 克隆与扫描器所有权都还在；false = 已丢失（第三方改写了基线或扫描器、或条目处于待归还），
    /// operator 应退役该英雄（随后调用 <see cref="Restore"/>）。
    /// </summary>
    internal static bool Tick(Archer archer)
    {
        try
        {
            Entry entry = Find(archer);
            if (entry == null || !entry.Applied || entry.Pending) return false;
            if (!StillOwner(entry, archer)) return false;

            if (!Owned(entry, archer, normal: true) || !Owned(entry, archer, normal: false))
            {
                Once("base-changed", "a base arrow attack field no longer points at the hero clone; ownership lost");
                return false;
            }
            return ClaimScanner(entry, archer);
        }
        catch (Exception e)
        {
            Once("tick", "hero range tick failed: " + e);
            return false;
        }
    }

    /// <summary>
    /// 归还（池复用/退场/关闭前调用）。void 且不抛：失败会保留 pending 回执，
    /// 由 <see cref="RetryCleanup"/> 继续重试（功能关闭时也可以调用）。
    /// </summary>
    internal static void Restore(Archer archer)
    {
        try
        {
            Entry entry = Find(archer);
            if (entry == null) return;
            if (entry.Archer == null || !SameActor(entry.Archer, archer)) entry.Archer = archer;  // 保留一个可用的 wrapper
            Cleanup(entry, entry.Archer);
        }
        catch (Exception e) { Once("restore", "hero range restore failed: " + e); }
    }

    /// <summary>服务所有 pending 归还（ModPanel 每帧调用；功能关闭时也要调用）。返回是否仍有 pending。</summary>
    internal static bool RetryCleanup()
    {
        for (int i = 0; i < Entries.Length; i++)
        {
            Entry entry = Entries[i];
            if (entry == null || !entry.Pending) continue;
            Archer archer = entry.Archer;
            try { Cleanup(entry, archer); }
            catch (Exception e) { Once("retry", "pending hero range cleanup failed; kept for the next retry: " + e); }
            // Continue through both slots so one failing owner cannot starve the other.
        }
        return PendingCleanupCount > 0;
    }

    /// <summary>整体归还并清空：**绝不丢弃 pending 条目**（仍被引用的克隆要留到脱钩后再销毁）。</summary>
    internal static void Clear()
    {
        for (int i = 0; i < Entries.Length; i++)
        {
            Entry entry = Entries[i];
            if (entry == null) continue;
            try { Cleanup(entry, entry.Archer); } catch (Exception) { }
            if (entry.Pending)
            {
                Once("clear-pending", "hero range cleanup still pending after Clear; receipt kept for RetryCleanup");
                continue;                                             // 保留条目，绝不因为 Clear 丢掉在用克隆
            }
            Entries[i] = null;
        }
        Logged.Clear();
    }

    // ============================================================
    // 记账 / 克隆
    // ============================================================

    private static Entry Rent(Archer archer)
    {
        IntPtr pointer; int goId;
        if (!Identity(archer, out pointer, out goId)) return null;
        for (int i = 0; i < Entries.Length; i++)
        {
            Entry entry = Entries[i];
            // 只在**完全空闲**的条目上出租：Applied / Pending / 仍有克隆或残留引用的一律跳过。
            if (entry != null && (entry.Applied || entry.Pending || entry.Archer != null
                || entry.CloneNormal != null || entry.CloneFire != null))
                continue;
            if (entry == null)
            {
                entry = new Entry();
                Entries[i] = entry;
            }
            entry.Pointer = pointer;
            entry.GoId = goId;
            entry.Archer = archer;
            entry.BaseNormal = entry.BaseFire = entry.CloneNormal = entry.CloneFire = null;
            entry.ClonesOwned = false;
            entry.Applied = false;
            entry.Pending = false;
            entry.NormalClaimLost = entry.FireClaimLost = false;
            entry.ScannerClaimed = false;
            entry.ScannerPtr = IntPtr.Zero;
            entry.RangeClaim = entry.BehindClaim = false;
            return entry;
        }
        Once("cap", "hero range clone table full (" + MaxHeroes + "); extra hero left at native range");
        return null;
    }

    /// <summary>只读可用性：非有限 / 非正 magnitude 一律 fail-closed（不写任何字段）。</summary>
    private static bool Usable(ArrowAttack so)
    {
        try
        {
            return float.IsFinite(so._shotMagnitude) && so._shotMagnitude > 0f
                && float.IsFinite(so._boostedShotMagnitude) && so._boostedShotMagnitude > 0f;
        }
        catch (Exception) { return false; }
    }

    /// <summary>建克隆并在**建立当刻**登记进条目（任何后续失败都能销毁它）。</summary>
    private static ArrowAttack CreateClone(ArrowAttack source, Entry entry, bool fire)
    {
        ArrowAttack clone = null;
        try
        {
            clone = UnityEngine.Object.Instantiate(source) as ArrowAttack;
            if (clone == null)
            {
                Once("clone-null", "ArrowAttack clone returned null; hero range left native");
                return null;
            }
            if (fire) entry.CloneFire = clone; else entry.CloneNormal = clone;
            entry.ClonesOwned = true;
            clone.name = CloneName;
            clone._shotMagnitude *= SpeedFactor;
            clone._boostedShotMagnitude *= SpeedFactor;
            return clone;
        }
        catch (Exception e)
        {
            // Instantiate 成功但后续写入失败：必须销毁刚创建的自有对象，绝不留孤儿。
            if (clone != null)
            {
                if (entry.CloneNormal != null && !Same(entry.CloneNormal, clone)) DestroyOwn(entry.CloneNormal);
                DestroyOwn(clone);
                entry.ClonesOwned = false;
            }
            Once("clone", "ArrowAttack clone failed; hero range left native: " + e);
            return null;
        }
    }

    /// <summary>幂等分支的所有权体检：两个字段都还必须指着本模块克隆。</summary>
    private static bool EntryHealthy(Entry entry, Archer archer)
        => Owned(entry, archer, normal: true) && Owned(entry, archer, normal: false);

    private static bool Owned(Entry entry, Archer archer, bool normal)
    {
        try
        {
            if (normal)
            {
                if (entry.NormalClaimLost || entry.CloneNormal == null) return false;
                return Same(archer._arrowAttack, entry.CloneNormal);
            }
            if (!Same(archer.ActiveArrowAttack, entry.CloneNormal) && !Same(archer.ActiveArrowAttack, entry.CloneFire)) return false;
            if (entry.FireClaimLost) return false;
            if (entry.CloneFire == null) return entry.BaseFire == null && archer._fireArrowAttack == null;
            return Same(archer._fireArrowAttack, entry.CloneFire);
        }
        catch (Exception) { return false; }
    }

    // ============================================================
    // 扫描器（身份 + 值双条件；绝不覆盖第三方写入）
    // ============================================================

    private static bool ClaimScanner(Entry entry, Archer archer)
    {
        try
        {
            if (!StillOwner(entry, archer)) return false;
            Scanner scanner = archer._enemyScanner;
            if (scanner == null)
            {
                Once("scanner-missing", "enemy scanner unavailable; hero range boost not claimed");
                return false;
            }
            IntPtr scannerPtr = scanner.Pointer;
            if (scannerPtr == IntPtr.Zero) return false;
            if (!entry.ScannerClaimed)
            {
                entry.ScannerPtr = scannerPtr;
                entry.BaseScanRange = scanner.range;
                entry.BaseScanRangeBehind = scanner.rangeBehind;
                entry.ClaimedScanRange = entry.BaseScanRange;
                entry.ClaimedScanRangeBehind = entry.BaseScanRangeBehind;
                entry.ScannerClaimed = true;
                entry.RangeClaim = entry.BehindClaim = true;
            }
            else if (entry.ScannerPtr != scannerPtr)
            {
                Once("scanner-replaced", "enemy scanner instance changed; ownership lost (foreign scanner untouched)");
                entry.RangeClaim = entry.BehindClaim = false;
                return false;
            }

            float target = archer.shootRange;                          // runtime 拥有 shootRange（已 ×1.25），这里只镜像
            entry.RangeClaim = AssertField(entry.RangeClaim, scanner.range, target,
                entry.BaseScanRange, entry.ClaimedScanRange, out float writtenRange, "range");
            if (entry.RangeClaim)
            {
                scanner.range = writtenRange;
                entry.ClaimedScanRange = writtenRange;
            }
            entry.BehindClaim = AssertField(entry.BehindClaim, scanner.rangeBehind, target,
                entry.BaseScanRangeBehind, entry.ClaimedScanRangeBehind, out float writtenBehind, "rangeBehind");
            if (entry.BehindClaim)
            {
                scanner.rangeBehind = writtenBehind;
                entry.ClaimedScanRangeBehind = writtenBehind;
            }
            return entry.RangeClaim && entry.BehindClaim;
        }
        catch (Exception e)
        {
            Once("scanner", "enemy scanner claim failed; range boost stays partial: " + e);
            return false;
        }
    }

    /// <summary>
    /// 单个扫描器字段：只有在当前值「属于本模块」（等于原值 / 上次写入值 / runtime 当前 shootRange）时才改写；
    /// 否则放弃该字段的认领并保留第三方值（绝不覆盖）。
    /// </summary>
    private static bool AssertField(bool claim, float current, float target, float original, float lastWritten,
        out float written, string field)
    {
        written = current;
        if (!claim) return false;
        bool ours = Near(current, original) || Near(current, lastWritten) || Near(current, target);
        if (!ours)
        {
            Once("scanner-foreign-" + field, "enemy scanner " + field + " was written by a third party; value preserved");
            return false;
        }
        written = target;
        return true;
    }

    // ============================================================
    // 集中清理（字段脱钩 → 销毁脱钩克隆；失败保留 pending）
    // ============================================================

    /// <summary>
    /// 归还一个条目：扫描器 CAS 归还 → 字段 CAS 脱钩 → 销毁已脱钩的自有克隆。
    /// 返回 true = 完全释放；false = 仍有引用/异常 → 条目置 Pending 供 <see cref="RetryCleanup"/>。
    /// 绝不覆盖第三方写入，绝不销毁仍被 actor 字段引用的克隆。
    /// </summary>
    private static bool Cleanup(Entry entry, Archer archer)
    {
        bool failed = false;
        bool ownerOk = archer != null && StillOwner(entry, archer);

        if (ownerOk && entry.ScannerClaimed)
        {
            try { RestoreScanner(entry, archer); }
            catch (Exception e) { failed = true; Once("restore-scanner", "restoring enemy scanner failed; kept for retry: " + e); }
        }

        if (ownerOk)
        {
            if (!entry.NormalClaimLost && entry.CloneNormal != null)
            {
                try
                {
                    if (Same(archer._arrowAttack, entry.CloneNormal)) archer._arrowAttack = entry.BaseNormal;
                }
                catch (Exception e) { failed = true; Once("restore-normal", "restoring _arrowAttack failed; kept for retry: " + e); }
            }
            if (!entry.FireClaimLost && entry.CloneFire != null)
            {
                try
                {
                    if (Same(archer._fireArrowAttack, entry.CloneFire)) archer._fireArrowAttack = entry.BaseFire;
                }
                catch (Exception e) { failed = true; Once("restore-fire", "restoring _fireArrowAttack failed; kept for retry: " + e); }
            }
            try
            {
                ArrowAttack active = archer.ActiveArrowAttack;
                if (entry.CloneNormal != null && Same(active, entry.CloneNormal))
                    archer.ActiveArrowAttack = entry.BaseNormal;
                else if (entry.CloneFire != null && Same(active, entry.CloneFire))
                    archer.ActiveArrowAttack = entry.BaseFire;
            }
            catch (Exception e) { failed = true; Once("restore-active", "restoring ActiveArrowAttack failed; kept for retry: " + e); }
        }

        // 销毁：只销毁「actor 已知字段都不再引用」的自有克隆；别名克隆只销毁一次。
        ArrowAttack first = entry.CloneNormal;
        ArrowAttack second = entry.CloneFire != null && !Same(entry.CloneFire, entry.CloneNormal) ? entry.CloneFire : null;
        if (!failed && !Referenced(entry, archer, first) && !Referenced(entry, archer, second))
        {
            DestroyOwn(first);
            DestroyOwn(second);
            entry.ClonesOwned = false;
            entry.BaseNormal = entry.BaseFire = null;
            entry.CloneNormal = entry.CloneFire = null;
            entry.Applied = false;
            entry.Pending = false;
            entry.ScannerClaimed = false;
            entry.ScannerPtr = IntPtr.Zero;
            entry.RangeClaim = entry.BehindClaim = false;
            entry.NormalClaimLost = entry.FireClaimLost = false;
            entry.Archer = null;
            entry.Pointer = IntPtr.Zero;
            entry.GoId = 0;
            return true;
        }

        entry.Applied = false;
        entry.Pending = true;                                          // 保留回执：下次 RetryCleanup/ModPanel 继续
        Once("cleanup-pending", "hero range cleanup incomplete (clone still referenced or a write failed); receipt kept");
        return false;
    }

    /// <summary>actor 的已知字段是否仍引用该克隆/是否读不到（读不到一律保守认为仍被引用）。</summary>
    private static bool Referenced(Entry entry, Archer archer, ArrowAttack clone)
    {
        if (clone == null) return false;
        try
        {
            if (archer == null || archer.gameObject == null) return false; // Confirmed destroyed owner has no live field references.
            if (Same(archer._arrowAttack, clone)) return true;
            if (Same(archer._fireArrowAttack, clone)) return true;
            return Same(archer.ActiveArrowAttack, clone);
        }
        catch (Exception) { return true; }
    }

    private static void RestoreScanner(Entry entry, Archer archer)
    {
        Scanner scanner = archer._enemyScanner;
        if (scanner == null) { entry.ScannerClaimed = false; return; }
        if (scanner.Pointer != entry.ScannerPtr) { entry.ScannerClaimed = false; return; }   // 换了实例：不碰
        if (entry.RangeClaim && Near(scanner.range, entry.ClaimedScanRange)) scanner.range = entry.BaseScanRange;
        if (entry.BehindClaim && Near(scanner.rangeBehind, entry.ClaimedScanRangeBehind))
            scanner.rangeBehind = entry.BaseScanRangeBehind;
    }

    private static void ReleaseEntry(Entry entry)
    {
        entry.Archer = null;
        entry.Pointer = IntPtr.Zero;
        entry.GoId = 0;
        entry.BaseNormal = entry.BaseFire = entry.CloneNormal = entry.CloneFire = null;
        entry.ClonesOwned = false;
        entry.Applied = false;
        entry.Pending = false;
        entry.ScannerClaimed = false;
        entry.ScannerPtr = IntPtr.Zero;
        entry.RangeClaim = entry.BehindClaim = false;
        entry.NormalClaimLost = entry.FireClaimLost = false;
    }

    private static bool IsOwnedClone(ArrowAttack so)
    {
        if (so == null) return false;
        for (int i = 0; i < Entries.Length; i++)
        {
            Entry entry = Entries[i];
            if (entry == null) continue;
            if (Same(entry.CloneNormal, so) || Same(entry.CloneFire, so)) return true;
        }
        return false;
    }

    private static bool SameActor(Archer a, Archer b)
    {
        IntPtr pa, pb; int ida, idb;
        return Identity(a, out pa, out ida) && Identity(b, out pb, out idb) && pa == pb && ida == idb;
    }

    private static bool Same(UnityEngine.Object a, UnityEngine.Object b)
        => a != null && b != null && a.Pointer == b.Pointer;

    private static bool Near(float a, float b)
        => Mathf.Abs(a - b) <= ValueEpsilon * Mathf.Max(1f, Mathf.Max(Mathf.Abs(a), Mathf.Abs(b)));

    private static void DestroyOwn(UnityEngine.Object obj)
    {
        if (obj == null) return;
        try { UnityEngine.Object.Destroy(obj); } catch (Exception) { }
    }

    // ---------- 测试钩子（只清本地表与日志；只应在没有在用克隆时调用） ----------

    internal static void ResetForTests()
    {
        for (int i = 0; i < Entries.Length; i++) Entries[i] = null;
        Logged.Clear();
    }

    /// <summary>纯数学：把原生初速按本模块倍率放大后的射程（`force²/-gravity`），供测试核对弹道上限。</summary>
    internal static float ScaledRange(float magnitude, float gravity)
        => gravity >= 0f ? 0f : (magnitude * SpeedFactor) * (magnitude * SpeedFactor) / -gravity;
}
