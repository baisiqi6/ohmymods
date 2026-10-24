using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomEnhancedMod.PalaceSource
{
    /// <summary>
    /// D1 私有 land12 配置责任缓存（冻结 S3 `TryGetClone` 缓存语义的具名抽取 + 批准的有界责任协议）。
    ///
    /// SOURCE-only，nativeReady=false，无销毁资格：
    /// - 一个当前已发布 clone + 一个待处置责任槽（在途预留 / Unclassified / Ambiguous /
    ///   未发布失败 built / 已退休 published），任何时刻最多两个可识别自有 clone 引用。
    /// - holder pointer 只是实际缓存 key，不是 generation/borrower 证明；无法确认 exact holder 即拒服务。
    /// - 同 exact holder 复用已发布 clone 且每次只读重验；坏 cache / 任何失败 sticky，不自动重建、不重试。
    /// - 换 holder 成功才把旧 published 转 pending；换 holder 失败绝不返回旧 clone。
    /// - 槽位占满在 Instantiate 之前拒绝新分配（fail-closed）；不做驱逐。
    /// - 零 Destroy/DestroyImmediate/Dispose：失败/歧义/退休对象一律有界强引用保留；
    ///   销毁 ABI/线程/borrower 证据缺失，属明确不支持的 SOURCE-only 能力上限。
    /// </summary>
    internal sealed class PalacePrivateConfigCache
    {
        internal const int MaxPublishedReferences = 1;
        internal const int MaxPendingResponsibilityReferences = 1;
        internal const int MaxIdentifiableOwnedClonesIncludingInFlight = 2;

        /// <summary>责任槽状态（Empty = 无任何在途/保留责任）。</summary>
        internal enum PendingKind
        {
            Empty = 0,
            InFlight = 1,          // 已预留，Instantiate 尚未返回可识别 wrapper
            Unclassified = 2,      // wrapper 已入槽，pointer/别名尚未核验；不可 setter
            Ambiguous = 3,         // pointer 不可读/为零/别名同 pointer/Instantiate 前抛出；sticky，不认自有
            OwnedInFlight = 4,     // 已识别为可识别自有 clone，可进入 setter/校验（仍未发布）
            FailedUnpublished = 5, // 自有 built 校验/字段/模板复核失败：不发布、不销毁、保留责任
            RetiredPublished = 6,  // 换 holder 后停止复用的旧 published：不销毁、保留责任
        }

        private readonly object _gate = new object();
        private bool _servicing;

        // 当前已发布 clone（最多 1）
        private LevelConfig _published;
        private ulong _publishedPtr;
        private ulong _publishedHolderPtr;

        // 待处置责任槽（最多 1：在途预留 / 返回对象 / 保留对象）
        private LevelConfig _pending;
        private ulong _pendingPtr;
        private PendingKind _pendingKind = PendingKind.Empty;

        // sticky 失败记忆（latest reason；不自动恢复/不重试）
        private bool _failed;
        private string _failureReason = "not-failed";

        internal PalacePrivateConfigCache() { }

        internal bool IsStickyFailed { get { lock (_gate) return _failed; } }
        internal string FailureReason { get { lock (_gate) return _failureReason; } }

        internal LevelConfig PublishedClone { get { lock (_gate) return _published; } }
        internal ulong PublishedPointer { get { lock (_gate) return _publishedPtr; } }
        internal ulong PublishedHolderPointer { get { lock (_gate) return _publishedHolderPtr; } }

        internal LevelConfig PendingObject { get { lock (_gate) return _pending; } }
        internal ulong PendingPointer { get { lock (_gate) return _pendingPtr; } }
        internal PendingKind PendingState { get { lock (_gate) return _pendingKind; } }

        /// <summary>持有的 CLR wrapper 引用数，含未分类对象；不查询 Unity 存活性、不证明所有权。</summary>
        internal int ResponsibilityReferenceCount
        {
            get
            {
                lock (_gate)
                    return (ReferenceEquals(_published, null) ? 0 : 1) + (ReferenceEquals(_pending, null) ? 0 : 1);
            }
        }

        /// <summary>记录 sticky 失败（latest reason 保留；不清理任何已持有对象；不触发重试）。</summary>
        internal void RecordFailure(string reason)
        {
            lock (_gate) RecordFailureLocked(reason);
        }

        private void RecordFailureLocked(string reason)
        {
            _failed = true;
            _failureReason = reason;
        }

        /// <summary>
        /// 服务入口：同 exact holder 只读重验复用；否则走一次新的有界构建。
        /// holder 为空/pointer 不可读/为零一律拒服务（不进入任何分配）。
        /// </summary>
        internal bool TryGetClone(BiomeHolder holder, out LevelConfig clone, out string report)
        {
            clone = null;
            lock (_gate)
            {
                if (_failed) { report = "failed-sticky:" + _failureReason; return false; }
                if (_servicing)
                {
                    RecordFailureLocked("cache-service-reentry-or-concurrent-refused");
                    report = _failureReason;
                    return false;
                }
                _servicing = true;
            }
            try
            {
                if (ReferenceEquals(holder, null)) { report = "holder null"; return false; }
                ulong holderPtr = PalacePrivateConfigFactory.PointerOf(holder);
                if (holderPtr == 0) { report = "holder pointer unreadable"; return false; }
                LevelConfig published;
                ulong publishedPtr;
                ulong publishedHolderPtr;
                lock (_gate)
                {
                    published = _published;
                    publishedPtr = _publishedPtr;
                    publishedHolderPtr = _publishedHolderPtr;
                }
                if (!ReferenceEquals(published, null) && publishedHolderPtr == holderPtr)
                {
                    var parts = new List<string>(4);
                    if (!PalacePrivateConfigValidation.ValidatePrivateConfig(published, parts, out bool ok) || !ok)
                    {
                        RecordFailure("cached clone failed revalidation: " + string.Join("; ", parts));
                        report = FailureReason;
                        return false;
                    }
                    string cachedReport = "cached holder=" + PalacePrivateConfigFactory.Hex(holderPtr)
                        + " clone=" + PalacePrivateConfigFactory.Hex(publishedPtr);
                    lock (_gate)
                    {
                        // Native reads may reenter or record a fault; consume current state
                        // before returning. This is cache bookkeeping, not native qualification.
                        if (_failed || !ReferenceEquals(_published, published)
                            || _publishedPtr != publishedPtr || _publishedHolderPtr != holderPtr)
                        {
                            if (!_failed) RecordFailureLocked("cached-publication-changed-during-validation");
                            report = _failureReason;
                            return false;
                        }
                        clone = published;
                        report = cachedReport;
                        return true;
                    }
                }
                if (!PalacePrivateConfigFactory.TryBuildClone(holder, holderPtr, this,
                    out LevelConfig built, out string buildReport))
                {
                    report = buildReport;
                    return false;
                }
                clone = built;
                report = buildReport;
                return true;
            }
            catch (Exception e)
            {
                clone = null;
                RecordFailure("cache-service-exception:" + e.GetType().Name + " " + e.Message);
                report = FailureReason;
                return false;
            }
            finally
            {
                lock (_gate) _servicing = false;
            }
        }

        /// <summary>
        /// Instantiate 之前预留唯一在途责任槽；同时阻止并发/重入分配。
        /// 失败（已有保留对象/已有在途/已 sticky）一律 fail-closed 并记录 sticky。
        /// </summary>
        internal bool TryReserveForAllocation(out string reason)
        {
            lock (_gate)
            {
                if (_failed) { reason = "failed-sticky:" + _failureReason; return false; }
                if (_pendingKind == PendingKind.InFlight)
                {
                    RecordFailureLocked("allocation-reentry-or-concurrent-refused");
                    reason = _failureReason;
                    return false;
                }
                if (_pendingKind != PendingKind.Empty)
                {
                    RecordFailureLocked("pending-responsibility-occupied:" + _pendingKind);
                    reason = _failureReason;
                    return false;
                }
                _pending = null;
                _pendingPtr = 0;
                _pendingKind = PendingKind.InFlight;
                reason = null;
                return true;
            }
        }

        /// <summary>Instantiate 返回非 null 后的第一动作：把 exact wrapper 放入预留槽为 Unclassified（先于 pointer 读取）。</summary>
        internal bool AttachReservedUnclassified(LevelConfig built)
        {
            lock (_gate)
            {
                if (_pendingKind != PendingKind.InFlight)
                {
                    // A fault may mark the reserved empty slot ambiguous before the return.
                    // Keep the returned wrapper without overwriting a retained reference.
                    if (ReferenceEquals(_pending, null))
                    {
                        _pending = built;
                        _pendingPtr = 0;
                        _pendingKind = PendingKind.Ambiguous;
                    }
                    RecordFailureLocked("attach-without-reservation");
                    return false;
                }
                _pending = built;
                _pendingPtr = 0;
                _pendingKind = PendingKind.Unclassified;
                return true;
            }
        }

        /// <summary>读取槽内 wrapper 的 pointer；读取抛错/为零均返回 false（对象仍保留在槽内）。</summary>
        internal bool TryReadPendingPointer(out ulong pointer, out string error)
        {
            LevelConfig obj;
            lock (_gate) obj = _pending;
            pointer = 0;
            error = null;
            if (ReferenceEquals(obj, null)) { error = "no reserved wrapper"; return false; }
            try
            {
                pointer = (ulong)obj.Pointer.ToInt64();
                lock (_gate)
                {
                    if (_pendingKind != PendingKind.Unclassified || !ReferenceEquals(_pending, obj))
                    { pointer = 0; error = "pending-changed-during-pointer-read"; return false; }
                    _pendingPtr = pointer;
                    return pointer != 0;
                }
            }
            catch (Exception e)
            {
                error = "pointer-read-exception:" + e.GetType().Name + " " + e.Message;
                return false;
            }
        }

        /// <summary>
        /// 标记预留/槽内对象为 Ambiguous（sticky）：pointer 抛错/为零、template/已持有同 pointer、
        /// 或 Instantiate 在可识别 wrapper 返回前抛出。不认自有、不 setter、不销毁；对象（若有）保留。
        /// </summary>
        internal void MarkReservedAmbiguous(string reason)
        {
            lock (_gate)
            {
                if (_pendingKind == PendingKind.Empty)
                {
                    RecordFailureLocked("ambiguous-without-reservation:" + reason);
                    return;
                }
                _pendingKind = PendingKind.Ambiguous;
                RecordFailureLocked(reason);
            }
        }

        /// <summary>pointer/别名核验通过：只有此刻起才允许对槽内对象写字段/校验。</summary>
        internal bool TryMarkReservedOwnedInFlight(LevelConfig expectedWrapper, ulong expectedPointer, out string reason)
        {
            lock (_gate)
            {
                if (_pendingKind != PendingKind.Unclassified || ReferenceEquals(_pending, null)
                    || !ReferenceEquals(_pending, expectedWrapper) || expectedPointer == 0 || _pendingPtr != expectedPointer)
                {
                    RecordFailureLocked("classify-without-wrapper");
                    reason = _failureReason;
                    return false;
                }
                _pendingKind = PendingKind.OwnedInFlight;
                if (_failed)
                {
                    // Ownership bookkeeping is possible; a known fault cannot enter writes.
                    _pendingKind = PendingKind.FailedUnpublished;
                    reason = _failureReason;
                    return false;
                }
                reason = null;
                return true;
            }
        }

        /// <summary>自有 built 未发布失败：保留责任（不发布、不销毁、不自动重试），sticky。</summary>
        internal void RetainReservedFailedUnpublished(string reason)
        {
            lock (_gate)
            {
                if (_pendingKind != PendingKind.OwnedInFlight && _pendingKind != PendingKind.FailedUnpublished)
                {
                    RecordFailureLocked("retain-without-owned-in-flight:" + reason);
                    return;
                }
                _pendingKind = PendingKind.FailedUnpublished;
                RecordFailureLocked(reason);
            }
        }

        /// <summary>Instantiate 返回 null（无可识别对象）：清空预留、不虚构 owner；仍 sticky。</summary>
        internal void ReleaseReservationNothingReturned(string reason)
        {
            lock (_gate)
            {
                if (_pendingKind == PendingKind.InFlight)
                {
                    _pending = null;
                    _pendingPtr = 0;
                    _pendingKind = PendingKind.Empty;
                }
                RecordFailureLocked(reason);
            }
        }

        /// <summary>
        /// 完整成功（字段+模板复核均通过）才发布：槽内自有 clone 成为新 published；
        /// 旧 published（若有）转为 RetiredPublished 责任（不销毁、不再服务）。
        /// 若期间已记录故障（例如重入被拒），不发布，built 保留为 FailedUnpublished。
        /// </summary>
        internal bool PublishReserved(ulong clonePtr, ulong holderPtr, out string report)
        {
            lock (_gate)
            {
                if (_pendingKind != PendingKind.OwnedInFlight || ReferenceEquals(_pending, null) || clonePtr == 0)
                {
                    RecordFailureLocked("publish-without-owned-in-flight");
                    report = _failureReason;
                    return false;
                }
                if (_failed)
                {
                    _pendingKind = PendingKind.FailedUnpublished;
                    report = "publish-refused-after-fault:" + _failureReason;
                    return false;
                }
                LevelConfig retired = _published;
                ulong retiredPtr = _publishedPtr;
                _published = _pending;
                _publishedPtr = clonePtr;
                _publishedHolderPtr = holderPtr;
                _pending = null;
                _pendingPtr = 0;
                _pendingKind = PendingKind.Empty;
                if (!ReferenceEquals(retired, null))
                {
                    _pending = retired;
                    _pendingPtr = retiredPtr;
                    _pendingKind = PendingKind.RetiredPublished;
                }
                report = "published";
                return true;
            }
        }
    }
}
