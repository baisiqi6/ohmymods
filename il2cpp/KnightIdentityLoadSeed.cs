// 骑士身份「首次迁移来源种子」（Load 期）：Load 时 sidecar 里没有本 scope/hash 的精确快照（首装/旧档迁移）时，
// 把「原始原生岛快照」里的精确 Knight/KnightData 记录与实际加载出来的 root 上当前生效的收据，一次性写回
// sidecar 的精确快照（ScopeKey = LoadBridge 解析出的稳定上下文 epoch，SnapshotHash = 该 epoch 下的
// 时钟无关指纹 kind2，都是原生排序/Decay 之前算出的原值）。
// 只有解析为「真正无历史的新上下文」才会开始批次；unresolved（冲突/已知历史对不上/仍有未归属历史）时
// LoadScope.ScopeKey 为 null，本模块绝不开始、绝不覆盖历史。
// 这样玩家即使一次原生 Save 都不调用（甚至不重启游戏），下次进入同一座岛也能按同一份快照恢复同样的 GUID/style。
//
// 契约（与 KnightIdentityArchive/KnightIdentityRuntime 严格配套）：
//  * 只在 LoadBridge 已确认 sidecar Missing/Valid 且本 scope/hash 无精确快照（scope.Receipts == null）时 Begin；
//    重复 Begin 幂等（真实接线与测试手动调用可能同时发生，绝不重复初始化/重置已有捕获）。
//  * 记录集合只在 Begin 冻结（原生随后会排序、Decay、最后 objects = null）：之后不重拍、不依赖该 list、不清旧 sidecar。
//    只有「明确读到、且不匹配 Knight/KnightData」的记录才合法忽略；任何读异常（含 componentData2）一律整批拒绝。
//  * Capture 只认本 scope 冻结的 uniqueID + 该记录冻结的 ObjectData.Pointer，以及原生真实 root
//    （非零 GO/Knight 指针、同 life > 0）。冻结 ID 但记录指针不符 → 整批拒绝（record-mismatch），不是跳过。
//  * 一个 uniqueID ↔ 一个 owner/life 双向唯一（Knight 与已知排除的 Squire 共用同一张映射）：重复同 pair 幂等；
//    一个 id 两个 owner、一个 owner 两个 id、同 id 角色改变（Squire→Knight）一律整批冲突。只读，绝不在 Load 期分配 GUID。
//  * Squire 也带 Knight/KnightData 组件，普通混合存档必须能种：只对「精确冻结 ID + record 指针、返回真实非空 owner、
//    有 Knight 组件、明确 tagSquire」的条目标为已知排除（KnownExcluded）。没有回调、owner null、tag 读取异常/未知、
//    被原生 Decay 删掉的记录都不能算排除。Complete 要求 Frozen.Count == Owners.Count + KnownExcluded.Count，
//    且至少一个真实 Knight（全 Squire 绝不写空表）。
//  * Complete 只在原生 __exception == null && __result == true 且全部映射齐备时把批次转 pending；失败/冲突/部分映射
//    整批不入 pending 并记录一次原因，绝不写半张表。重复 Complete 幂等。
//  * Flush 只在既有 5s KnightStyle.IntegrityPass 的 PrimeExisting + ApplyKnightStyle 之后（以及既有 Promote 的
//    PrimeExisting 之后）调用，不新增扫描器/driver/hook：复核本批每个 owner（同 GOid/GOptr/Knightptr/world/scene/
//    gameLayer、同 life、活 Damageable、收据有效且 GUID 唯一）与每个排除条目（仍同身份/life 且仍明确 tagSquire；
//    一旦晋升成 Knight 就不能把新 Knight 漏在原快照外 → 整批安全丢弃，不重新认领），然后整表 TryCreate +
//    AppendSnapshot，并读回 archive 校验；AppendSnapshot 是 void，只有读回一致才标已持久化。失败保留本批，
//    只在后续 Flush 事件上按 5s 退避重试。
//  * 只认实测：inactive / 层级未补齐 / world 未就绪 / 读异常 → 等待（不丢批、不落盘）；GO/Knight 指针、life、scene、
//    tag 角色、Damageable 死亡 → 确定为变化 → 丢该批并日志，绝不重新认领。
//  * 后续新招募永远不进这张原始来源表（那是正常 Save 桥的职责）；本模块不写原生存档、不写 config、无 client 写入。
//  * 内存硬上限：最多 MaxBatches 批 × MaxFrozenRecords 条小条目；只保存冻结值与 owner 必要引用，不持有 ObjectData。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace KingdomEnhancedMod
{
    /// <summary>Load 期首次迁移的来源种子：冻结原始快照记录 → 捕获实际 owner（含已知排除的 Squire）→ 整批写回精确快照。</summary>
    internal static class KnightIdentityLoadSeed
    {
        /// <summary>同时保留的批次数上限（嵌套 scope 各自独立；满则拒绝新批次，不牺牲已有批次）。</summary>
        internal const int MaxBatches = 4;

        /// <summary>单批记录上限（与快照 schema 一致）。</summary>
        internal const int MaxFrozenRecords = KnightIdentityArchive.MaxEntriesPerSnapshot;

        private static readonly long RetryBackoffTicks = 5L * TimeSpan.TicksPerSecond;

        /// <summary>一个被捕获的 root：只保存冻结值与必要引用（不持有 ObjectData）。</summary>
        private sealed class OwnerState
        {
            internal Knight Knight;
            internal long Lifetime;
            internal int GameObjectId;
            internal IntPtr GameObjectPointer;
            internal IntPtr KnightPointer;
            internal IntPtr WorldPointer; // 捕获时实测到当前 world 才写入；IntPtr.Zero = 未知
            internal bool Excluded;       // true = 明确 tagSquire 的已知排除（不写进快照，但要复核）
        }

        /// <summary>一批（= 一个 Load scope）的来源种子状态。</summary>
        private sealed class SeedBatch
        {
            internal long ScopeId;
            internal long TransactionScopeId; // 成功内层转移给父层；0才是整个事务已提交
            internal string ContextKey;   // 稳定上下文：与快照同一次原子写入里登记 epoch 映射
            internal string ScopeKey;
            internal string SnapshotHash;
            internal bool NewEpoch;       // 本代 epoch 尚未被上下文拥有（写路径需登记）
            internal Dictionary<string, IntPtr> Frozen;      // uniqueID → 冻结的 ObjectData.Pointer
            internal Dictionary<string, OwnerState> Owners;  // uniqueID → 捕获的真实 Knight root
            internal Dictionary<string, OwnerState> Excluded; // uniqueID → 明确 tagSquire 的已知排除
            internal string RejectReason;                    // 非 null：本批已整体拒绝
            internal bool Completed;
            internal bool Pending;
            internal bool Attempted;
            internal long LastAttemptTicks;
        }

        private enum FlushOutcome
        {
            Persisted,
            Waiting,
            Dropped,
        }

        private enum OwnerVerdict
        {
            Ready,
            Unknown,
            Changed,
        }

        private static readonly List<SeedBatch> Batches = new List<SeedBatch>(MaxBatches);

        /// <summary>仅诊断/测试：pending（已成功 Complete、等待 Flush 落盘）批次数。</summary>
        internal static int PendingCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Batches.Count; i++) if (Batches[i].Pending) count++;
                return count;
            }
        }

        /// <summary>仅诊断/测试：在管批次总数。</summary>
        internal static int BatchCount
        {
            get { return Batches.Count; }
        }

        /// <summary>仅诊断/测试：批次状态一行摘要（不含任何身份内容）。</summary>
        internal static string DescribeForTests()
        {
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < Batches.Count; i++)
            {
                SeedBatch batch = Batches[i];
                if (i > 0) builder.Append(" ; ");
                builder.Append("scope=").Append(batch.ScopeId.ToString(CultureInfo.InvariantCulture))
                    .Append(" frozen=").Append(batch.Frozen != null ? batch.Frozen.Count : 0)
                    .Append(" owners=").Append(batch.Owners != null ? batch.Owners.Count : 0)
                    .Append(" excluded=").Append(batch.Excluded != null ? batch.Excluded.Count : 0)
                    .Append(" pending=").Append(batch.Pending)
                    .Append(" completed=").Append(batch.Completed)
                    .Append(" reject=").Append(batch.RejectReason ?? "-");
            }
            return builder.Length == 0 ? "<none>" : builder.ToString();
        }

        // ------------------------------------------------------------------ Operator 接线入口

        /// <summary>
        /// 冻结原始岛快照里的精确 Knight 记录（uniqueID → ObjectData.Pointer）。任何重复 ID/非法 ID/零指针/超上限/
        /// 原生读取异常（含组件列表读不出来的未知记录）一律整批拒绝（不写、不删旧 sidecar），返回后不再触碰该 list。
        /// 重复 Begin 幂等：已存在本 scope 批次时直接返回，绝不重置已有捕获。
        /// </summary>
        internal static void Begin(KnightIdentityLoadBridge.LoadScope scope)
        {
            try
            {
                if (scope == null) return;
                if (FindBatch(scope.Id) != null) return; // 重复 Begin：幂等
                if (scope.Island == null) return;
                if (string.IsNullOrEmpty(scope.ScopeKey) || string.IsNullOrEmpty(scope.SnapshotHash)) return;
                if (scope.Receipts != null) return; // 已有精确快照：走既有绑定桥，不种
                if (string.IsNullOrEmpty(scope.ContextKey)) return; // 解析不出稳定上下文：不种
                if (!KnightIdentityRuntime.IsHostAuthority()) { KnightIdentityLog.Once("seed-client", null); return; }

                if (Batches.Count >= MaxBatches)
                {
                    RecycleStale();
                    if (Batches.Count >= MaxBatches)
                    {
                        KnightIdentityLog.Once("seed-batch-cap", null);
                        return;
                    }
                }

                Dictionary<string, IntPtr> frozen = FreezeRecords(scope.Island);
                if (frozen == null) return;      // 原因已记录（整批拒绝）
                if (frozen.Count == 0) return;   // 岛上没有骑士记录：不写空表

                Batches.Add(new SeedBatch
                {
                    ScopeId = scope.Id,
                    TransactionScopeId = scope.Id,
                    ContextKey = scope.ContextKey,
                    ScopeKey = scope.ScopeKey,
                    SnapshotHash = scope.SnapshotHash,
                    NewEpoch = scope.NewEpoch,
                    Frozen = frozen,
                    Owners = new Dictionary<string, OwnerState>(frozen.Count, StringComparer.Ordinal),
                    Excluded = new Dictionary<string, OwnerState>(StringComparer.Ordinal),
                });
            }
            catch (Exception e)
            {
                KnightIdentityLog.Once("seed-begin", e);
            }
        }

        /// <summary>
        /// TryCreateOrFind 后缀（在原收据早退之前）逐条捕获：只认本 scope 冻结的 uniqueID + 冻结 ObjectData.Pointer
        /// 与真实 root。只读；重复同 pair 幂等；记录指针不符或任何双向冲突立即整批拒绝。
        /// </summary>
        internal static void Capture(KnightIdentityLoadBridge.LoadScope scope, IslandSaveData.ObjectData data, Persistent owner)
        {
            try
            {
                SeedBatch batch = FindBatch(scope);
                if (batch == null || batch.Completed || batch.RejectReason != null) return;
                if (data == null || owner == null) return;

                string uniqueId;
                IntPtr recordPointer;
                try
                {
                    uniqueId = data.uniqueID;
                    recordPointer = data.Pointer;
                }
                catch (Exception e)
                {
                    KnightIdentityLog.Once("seed-capture-read", e);
                    RejectBatch(batch, "record-unreadable");
                    return;
                }

                if (!KnightIdentitySnapshot.IsValidUniqueId(uniqueId)) return;
                if (!batch.Frozen.TryGetValue(uniqueId, out IntPtr frozenPointer)) return; // 不是本快照冻结的记录
                if (frozenPointer != recordPointer)
                {
                    // 冻结 ID 却是别的记录实例（原生换了 ObjectData）：无法证明是本快照的那条 → 整批拒写
                    RejectBatch(batch, "record-mismatch");
                    return;
                }

                GameObject go;
                try
                {
                    go = owner.gameObject;
                }
                catch (Exception e)
                {
                    KnightIdentityLog.Once("seed-capture-root", e);
                    return; // 读不出来：不捕获（随后按不完整拒绝）
                }
                if (go == null) return;

                bool isKnight;
                bool isSquire;
                if (!TryCompareTag(go, "Knight", out isKnight)) return;
                if (!TryCompareTag(go, "Squire", out isSquire)) return;
                if (!isKnight && !isSquire) return; // 其他角色：不参与本快照
                bool excluded = isSquire;

                Knight knight = KnightIdentitySaveBridge.SafeGetComponent<Knight>(go);
                if (knight == null) return; // 排除条目也必须有真实 Knight 组件

                int gameObjectId = SafeInstanceId(go, 0);
                IntPtr gameObjectPointer = SafePointer(go);
                IntPtr knightPointer = SafePointer(knight);
                if (gameObjectId == 0 || gameObjectPointer == IntPtr.Zero || knightPointer == IntPtr.Zero) return;
                if (SafePointer(owner) == IntPtr.Zero) return;

                long lifetime = KnightIdentityRuntime.GetLifetime(knight);
                if (lifetime <= 0) return; // 未登记 life：不捕获（随后按不完整拒绝）

                if (FindCaptured(batch, uniqueId, out OwnerState existing))
                {
                    if (existing.Excluded == excluded
                        && existing.Lifetime == lifetime
                        && SameOwner(existing, gameObjectId, gameObjectPointer, knightPointer))
                    {
                        return; // 同一 pair 重复捕获：幂等
                    }
                    RejectBatch(batch, "owner-life-conflict"); // 同 id 换 owner/life 或角色改变
                    return;
                }

                if (FindCapturedOwner(batch, gameObjectId, gameObjectPointer, knightPointer, out _))
                {
                    RejectBatch(batch, "owner-two-ids"); // 一个 owner 两个 id
                    return;
                }

                if (batch.Owners.Count + batch.Excluded.Count >= MaxFrozenRecords)
                {
                    RejectBatch(batch, "capture-cap");
                    return;
                }

                OwnerState state = new OwnerState
                {
                    Knight = knight,
                    Lifetime = lifetime,
                    GameObjectId = gameObjectId,
                    GameObjectPointer = gameObjectPointer,
                    KnightPointer = knightPointer,
                    WorldPointer = FreezeWorldPointer(go, knight),
                    Excluded = excluded,
                };
                if (excluded) batch.Excluded.Add(uniqueId, state);
                else batch.Owners.Add(uniqueId, state);
            }
            catch (Exception e)
            {
                KnightIdentityLog.Once("seed-capture", e);
            }
        }

        /// <summary>
        /// TryPop finalizer 里（结束原 load scope 之后）按 __exception == null &amp;&amp; __result == true 调用。
        /// 只有成功且全部冻结记录都已映射（真实 Knight 或明确 tagSquire 的已知排除）才转 pending；重复调用幂等。
        /// </summary>
        internal static void Complete(KnightIdentityLoadBridge.LoadScope scope, bool succeeded)
        {
            try
            {
                if (scope == null) return;
                for (int i = 0; i < Batches.Count; i++)
                {
                    SeedBatch owned = Batches[i];
                    if (owned.TransactionScopeId != scope.Id) continue;
                    owned.TransactionScopeId = succeeded && scope.Previous != null ? scope.Previous.Id : 0;
                    if (!succeeded)
                    {
                        owned.Pending = false;
                        RejectBatch(owned, "native-failed");
                    }
                }
                SeedBatch batch = FindBatch(scope != null ? scope.Id : 0);
                if (batch == null || batch.Completed) return; // 重复 Complete：幂等
                batch.Completed = true;

                if (!succeeded)
                {
                    RejectBatch(batch, "native-failed");
                    return;
                }
                if (batch.RejectReason != null) return; // 已在 Capture 阶段记录原因
                if (batch.Owners.Count + batch.Excluded.Count != batch.Frozen.Count)
                {
                    // 原生 Decay/失败导致少对象：本轮保守不种（不写无法补齐的半张表），原因可辨。
                    RejectBatch(batch, "incomplete");
                    return;
                }
                if (batch.Owners.Count == 0)
                {
                    RejectBatch(batch, "no-knights"); // 全 Squire：绝不写空表
                    return;
                }

                batch.Pending = true;
            }
            catch (Exception e)
            {
                KnightIdentityLog.Once("seed-complete", e);
            }
        }

        /// <summary>
        /// 既有 5s IntegrityPass（PrimeExisting + ApplyKnightStyle 之后）与既有 Promote 的 PrimeExisting 之后调用。
        /// 只复核本批捕获的 root；全部就绪才整表 TryCreate + AppendSnapshot，并读回校验后才算持久化。
        /// </summary>
        internal static void Flush()
        {
            if (!KnightIdentityRuntime.CanFlushSeed) return;
            try
            {
                for (int i = Batches.Count - 1; i >= 0; i--)
                {
                    SeedBatch batch = Batches[i];
                    if (!batch.Pending || batch.TransactionScopeId != 0) continue;

                    // 退避只针对「真的尝试过后失败」的批次；未就绪（收据/层级）只是等待，不消耗退避窗口。
                    if (batch.Attempted && DateTime.UtcNow.Ticks - batch.LastAttemptTicks < RetryBackoffTicks) continue;
                    if (!KnightIdentityRuntime.IsHostAuthority()) continue; // 未就绪：等下次 Flush 事件

                    FlushOutcome outcome = TryFlush(batch);
                    if (outcome == FlushOutcome.Persisted)
                    {
                        Batches.RemoveAt(i); // 已持久化：回收（释放 owner 引用）
                    }
                    else if (outcome == FlushOutcome.Dropped)
                    {
                        KnightIdentityLog.Once("seed-dropped:" + batch.RejectReason, null);
                        Batches.RemoveAt(i);
                    }
                }
            }
            catch (Exception e)
            {
                KnightIdentityLog.Once("seed-flush", e);
            }
        }

        /// <summary>Operator 测试重置用：只清内存 scope/pending，不写 Unity、不写文件。</summary>
        internal static void Clear()
        {
            for (int i = 0; i < Batches.Count; i++)
            {
                SeedBatch batch = Batches[i];
                batch.Owners?.Clear();
                batch.Excluded?.Clear();
                batch.Frozen?.Clear();
            }
            Batches.Clear();
        }

        // ------------------------------------------------------------------ 冻结

        /// <summary>
        /// 冻结精确 Knight/KnightData 记录（与 Hermes/存档桥同一约定：name == "Knight" 且 type == "KnightData"）。
        /// 只有明确读到且不匹配才忽略；任何不可信输入（重复/非法/零指针/超上限/原生或组件读异常）返回 null 并
        /// 记录一次原因：整批拒绝，绝不清旧 sidecar。
        /// </summary>
        private static Dictionary<string, IntPtr> FreezeRecords(IslandSaveData island)
        {
            Il2CppSystem.Collections.Generic.List<IslandSaveData.ObjectData> records;
            int count;
            try
            {
                records = island.objects;
                if (records == null)
                {
                    KnightIdentityLog.Once("seed-reject:records-null", null);
                    return null;
                }
                count = records.Count;
            }
            catch (Exception e)
            {
                KnightIdentityLog.Once("seed-reject:records", e);
                return null;
            }

            Dictionary<string, IntPtr> frozen = new Dictionary<string, IntPtr>(StringComparer.Ordinal);
            for (int i = 0; i < count; i++)
            {
                IslandSaveData.ObjectData record;
                try
                {
                    record = records[i];
                }
                catch (Exception e)
                {
                    KnightIdentityLog.Once("seed-reject:record-read", e);
                    return null;
                }
                if (record == null) continue;

                if (!RecordIsKnight(record, out bool readable))
                {
                    if (!readable)
                    {
                        KnightIdentityLog.Once("seed-reject:record-read", null); // 未知记录绝不当作非 Knight
                        return null;
                    }
                    continue; // 明确读到且不匹配：合法忽略
                }

                string uniqueId;
                IntPtr pointer;
                try
                {
                    uniqueId = record.uniqueID;
                    pointer = record.Pointer;
                }
                catch (Exception e)
                {
                    KnightIdentityLog.Once("seed-reject:record-read", e);
                    return null;
                }

                if (!KnightIdentitySnapshot.IsValidUniqueId(uniqueId))
                {
                    KnightIdentityLog.Once("seed-reject:invalid-id", null);
                    return null;
                }
                if (pointer == IntPtr.Zero)
                {
                    KnightIdentityLog.Once("seed-reject:zero-pointer", null);
                    return null;
                }
                if (frozen.ContainsKey(uniqueId))
                {
                    KnightIdentityLog.Once("seed-reject:duplicate-id", null);
                    return null;
                }
                if (frozen.Count >= MaxFrozenRecords)
                {
                    KnightIdentityLog.Once("seed-reject:over-cap", null);
                    return null;
                }
                frozen.Add(uniqueId, pointer);
            }
            return frozen;
        }

        /// <summary>
        /// 三态判定：true/false = 明确读到（且是否匹配 Knight/KnightData）；readable == false = 组件列表读不出来，
        /// 属于未知记录，调用方必须整批拒绝（绝不把未知记录当作非 Knight 而漏掉半张表）。
        /// </summary>
        private static bool RecordIsKnight(IslandSaveData.ObjectData record, out bool readable)
        {
            readable = false;
            try
            {
                Il2CppSystem.Collections.Generic.List<IslandSaveData.ObjectData.ComponentData> components = record.componentData2;
                if (components == null)
                {
                    readable = true; // 明确没有组件列表：不是骑士记录
                    return false;
                }
                int count = components.Count;
                for (int i = 0; i < count; i++)
                {
                    IslandSaveData.ObjectData.ComponentData component = components[i];
                    if (component == null) continue;
                    if (string.Equals(component.name, "Knight", StringComparison.Ordinal)
                        && string.Equals(component.type, "KnightData", StringComparison.Ordinal))
                    {
                        readable = true;
                        return true;
                    }
                }
                readable = true;
                return false;
            }
            catch
            {
                readable = false; // 读异常：交给 FreezeRecords 整批拒绝
                return false;
            }
        }

        // ------------------------------------------------------------------ 落盘

        /// <summary>整批复核 + 一次 TryCreate/AppendSnapshot + 读回校验；任何不确定都只是等待，绝不写半张表。</summary>
        private static FlushOutcome TryFlush(SeedBatch batch)
        {
            if (KnightIdentityContexts.TryGetBinding(batch.ContextKey, out string epoch, out bool unresolved, out _)
                && (unresolved || !string.Equals(epoch, batch.ScopeKey, StringComparison.Ordinal)))
            {
                batch.RejectReason = "context-changed";
                return FlushOutcome.Dropped;
            }
            if (batch.Owners.Count + batch.Excluded.Count != batch.Frozen.Count) return FlushOutcome.Waiting; // 不应发生
            if (batch.Owners.Count == 0) return FlushOutcome.Waiting;                                          // 不写空表

            bool wait = false;

            // 先复核已知排除（Squire）：晋升/身份/life 变化一律丢整批（不能把新 Knight 漏在原快照之外）
            foreach (KeyValuePair<string, OwnerState> pair in batch.Excluded)
            {
                OwnerVerdict verdict = VerifyOwner(pair.Value, out _);
                if (verdict == OwnerVerdict.Changed)
                {
                    batch.RejectReason = "excluded-changed";
                    return FlushOutcome.Dropped;
                }
                if (verdict == OwnerVerdict.Unknown) wait = true;
            }

            List<KnightIdentitySnapshotEntry> entries = new List<KnightIdentitySnapshotEntry>(batch.Owners.Count);
            HashSet<Guid> identities = new HashSet<Guid>();

            foreach (KeyValuePair<string, OwnerState> pair in batch.Owners)
            {
                OwnerVerdict verdict = VerifyOwner(pair.Value, out KnightIdentityReceipt receipt);
                if (verdict == OwnerVerdict.Changed)
                {
                    batch.RejectReason = "owner-changed";
                    return FlushOutcome.Dropped; // 确定变化：丢该批，不重新认领
                }
                if (verdict == OwnerVerdict.Unknown)
                {
                    wait = true;
                    continue;
                }
                if (!receipt.IsValid || !identities.Add(receipt.Id))
                {
                    KnightIdentityLog.Once("seed-receipt-unusable", null);
                    wait = true;
                    continue;
                }
                entries.Add(new KnightIdentitySnapshotEntry(pair.Key, receipt));
            }

            if (wait) return FlushOutcome.Waiting;
            if (entries.Count != batch.Owners.Count) return FlushOutcome.Waiting;

            if (!KnightIdentitySnapshot.TryCreate(KnightIdentityFingerprint.KindNormalized, batch.SnapshotHash, DateTimeOffset.UtcNow, entries, out KnightIdentitySnapshot snapshot, out string error))
            {
                KnightIdentityLog.Once("seed-snapshot:" + error, null);
                return FlushOutcome.Waiting;
            }

            batch.Attempted = true;
            batch.LastAttemptTicks = DateTime.UtcNow.Ticks;

            // 唯一 I/O 入口；坏文件/未知版本/冲突一律保持保护；context 映射与快照同一次原子写入。
            KnightIdentitySidecar.AppendSnapshot(batch.ScopeKey, snapshot, batch.ContextKey, batch.NewEpoch);

            if (!VerifyPersisted(batch, snapshot))
            {
                KnightIdentityLog.Once("seed-persist-unverified", null);
                return FlushOutcome.Waiting; // 留待下次 Flush 事件（5s 退避）重试
            }

            KnightIdentityLog.Receipt("seed scope=" + ShortHash(batch.ScopeKey) + " hash=" + ShortHash(batch.SnapshotHash)
                + " entries=" + entries.Count.ToString(CultureInfo.InvariantCulture)
                + " excluded=" + batch.Excluded.Count.ToString(CultureInfo.InvariantCulture));
            return FlushOutcome.Persisted;
        }

        /// <summary>
        /// 复核一个捕获 root：确定变化 → Changed（丢批）；无法实测（inactive/层级未补齐/world 未就绪/读异常）→ Unknown（等待）；
        /// 否则 Ready（真实 Knight 还要给出当前收据）。排除条目（Squire）只复核身份/角色/life，不要收据。
        /// </summary>
        private static OwnerVerdict VerifyOwner(OwnerState owner, out KnightIdentityReceipt receipt)
        {
            receipt = default;
            Knight knight = owner.Knight;
            if (knight == null) return OwnerVerdict.Changed;

            GameObject go;
            try
            {
                go = knight.gameObject;
            }
            catch
            {
                return OwnerVerdict.Unknown; // 读异常不是确证销毁
            }
            if (go == null) return OwnerVerdict.Changed;

            if (!TryInstanceId(go, out int gameObjectId)) return OwnerVerdict.Unknown;
            if (gameObjectId != owner.GameObjectId) return OwnerVerdict.Changed;      // GO 已被替换

            if (!TryPointer(go, out IntPtr gameObjectPointer)) return OwnerVerdict.Unknown;
            if (gameObjectPointer != owner.GameObjectPointer) return OwnerVerdict.Changed; // GO 指针变了（instanceID 复用）

            if (!TryPointer(knight, out IntPtr knightPointer)) return OwnerVerdict.Unknown;
            if (knightPointer != owner.KnightPointer) return OwnerVerdict.Changed;    // Knight 组件换了

            bool active;
            try
            {
                active = go.activeInHierarchy;
            }
            catch
            {
                return OwnerVerdict.Unknown;
            }
            if (!active) return OwnerVerdict.Unknown; // inactive：等待，不是确证变化

            if (!TryCompareTag(go, "Knight", out bool isKnight)) return OwnerVerdict.Unknown;
            if (!TryCompareTag(go, "Squire", out bool isSquire)) return OwnerVerdict.Unknown;
            if (owner.Excluded)
            {
                if (isKnight) return OwnerVerdict.Changed;  // 已晋升：整批安全丢弃，不重新认领
                if (!isSquire) return OwnerVerdict.Changed; // 角色明确变了
            }
            else if (!isKnight)
            {
                return OwnerVerdict.Changed;
            }

            long lifetime = KnightIdentityRuntime.GetLifetime(knight);
            if (lifetime != owner.Lifetime) return OwnerVerdict.Changed; // 死亡/OnEnable 新 life/回收：不重新认领

            OwnerVerdict damageable = VerifyDamageable(knight);
            if (damageable != OwnerVerdict.Ready) return damageable;

            OwnerVerdict world = VerifyWorld(go, owner);
            if (world != OwnerVerdict.Ready) return world;

            if (owner.Excluded) return OwnerVerdict.Ready; // 排除条目不进快照：不需要收据

            if (!KnightIdentityRuntime.TryGetTrackedIdentity(knight, out long trackedLifetime, out KnightIdentityReceipt current))
            {
                return OwnerVerdict.Unknown; // 收据尚未就绪：等下一次 Flush 事件
            }
            if (trackedLifetime != owner.Lifetime) return OwnerVerdict.Changed;
            receipt = current;
            return OwnerVerdict.Ready;
        }

        /// <summary>Damageable 复核：读异常 → 未知（等待）；null 或死亡 → 确证变化。</summary>
        private static OwnerVerdict VerifyDamageable(Knight knight)
        {
            Damageable damageable;
            try
            {
                damageable = knight._damageable;
            }
            catch
            {
                return OwnerVerdict.Unknown;
            }
            if (damageable == null) return OwnerVerdict.Changed;

            bool dead;
            try
            {
                dead = damageable.isDead;
            }
            catch
            {
                return OwnerVerdict.Unknown;
            }
            return dead ? OwnerVerdict.Changed : OwnerVerdict.Ready;
        }

        /// <summary>world/scene/gameLayer 复核：场景明确不符 → 变化；world 缺失、层级未补齐或读异常 → 等待。</summary>
        private static OwnerVerdict VerifyWorld(GameObject go, OwnerState owner)
        {
            World world;
            GameObject worldGo;
            if (!TryGetWorldContext(out world, out worldGo)) return OwnerVerdict.Unknown;

            bool same;
            if (!TrySameScene(go, worldGo, out same)) return OwnerVerdict.Unknown;
            if (!same) return OwnerVerdict.Changed;

            try
            {
                Transform layer = world.gameLayer;
                Transform unitTransform = go.transform;
                if (layer == null || unitTransform == null) return OwnerVerdict.Unknown;
                if (!unitTransform.IsChildOf(layer)) return OwnerVerdict.Unknown; // 层级尚未补齐：等待
            }
            catch
            {
                return OwnerVerdict.Unknown;
            }

            if (!TryPointer(world, out IntPtr live)) return OwnerVerdict.Unknown;
            if (owner.WorldPointer != IntPtr.Zero && owner.WorldPointer != live) return OwnerVerdict.Changed;
            return OwnerVerdict.Ready;
        }

        /// <summary>AppendSnapshot 是 void：读回 archive 并确认同 scope/hash 的快照逐条（id + 收据）一致。</summary>
        private static bool VerifyPersisted(SeedBatch batch, KnightIdentitySnapshot snapshot)
        {
            try
            {
                string path = KnightIdentitySidecar.Path;
                if (string.IsNullOrEmpty(path)) return false;

                KnightIdentityArchiveStore.LoadResult loaded = KnightIdentityArchiveStore.Load(path);
                if (!loaded.IsUsable) return false;
                if (!loaded.Archive.TryGetSnapshot(batch.ScopeKey, batch.SnapshotHash, out KnightIdentitySnapshot readBack)) return false;
                return readBack.HasSameEntries(snapshot);
            }
            catch (Exception e)
            {
                KnightIdentityLog.Once("seed-readback", e);
                return false;
            }
        }

        // ------------------------------------------------------------------ 批次与 native 薄封装

        private static SeedBatch FindBatch(KnightIdentityLoadBridge.LoadScope scope)
        {
            return scope == null ? null : FindBatch(scope.Id);
        }

        private static SeedBatch FindBatch(long scopeId)
        {
            if (scopeId == 0) return null;
            for (int i = 0; i < Batches.Count; i++) if (Batches[i].ScopeId == scopeId) return Batches[i];
            return null;
        }

        /// <summary>回收不再属于活动 scope 链、且未 pending 的批次（Complete 未到/异常中断），避免占满额度。</summary>
        private static void RecycleStale()
        {
            for (int i = Batches.Count - 1; i >= 0; i--)
            {
                SeedBatch batch = Batches[i];
                if (batch.Pending) continue;
                if (IsScopeLive(batch.ScopeId)) continue;
                batch.Owners?.Clear();
                batch.Excluded?.Clear();
                batch.Frozen?.Clear();
                Batches.RemoveAt(i);
            }
        }

        private static bool IsScopeLive(long scopeId)
        {
            KnightIdentityLoadBridge.LoadScope scope = KnightIdentityLoadBridge.Current;
            while (scope != null)
            {
                if (scope.Id == scopeId) return true;
                scope = scope.Previous;
            }
            return false;
        }

        private static void RejectBatch(SeedBatch batch, string reason)
        {
            if (batch.RejectReason == null)
            {
                batch.RejectReason = reason;
                KnightIdentityLog.Once("seed-batch-rejected:" + reason, null); // 每批一次原因
            }
            batch.Owners?.Clear();   // 释放 owner 引用
            batch.Excluded?.Clear();
        }

        private static bool FindCaptured(SeedBatch batch, string uniqueId, out OwnerState state)
        {
            return batch.Owners.TryGetValue(uniqueId, out state) || batch.Excluded.TryGetValue(uniqueId, out state);
        }

        private static bool FindCapturedOwner(SeedBatch batch, int gameObjectId, IntPtr gameObjectPointer, IntPtr knightPointer, out OwnerState state)
        {
            foreach (KeyValuePair<string, OwnerState> pair in batch.Owners)
            {
                if (SameOwner(pair.Value, gameObjectId, gameObjectPointer, knightPointer)) { state = pair.Value; return true; }
            }
            foreach (KeyValuePair<string, OwnerState> pair in batch.Excluded)
            {
                if (SameOwner(pair.Value, gameObjectId, gameObjectPointer, knightPointer)) { state = pair.Value; return true; }
            }
            state = null;
            return false;
        }

        private static bool SameOwner(OwnerState owner, int gameObjectId, IntPtr gameObjectPointer, IntPtr knightPointer)
        {
            return owner.GameObjectId == gameObjectId
                && owner.GameObjectPointer == gameObjectPointer
                && owner.KnightPointer == knightPointer;
        }

        private static string ShortHash(string value)
        {
            return string.IsNullOrEmpty(value) || value.Length < 8 ? value : value.Substring(0, 8);
        }

        /// <summary>捕获期只要求同 scene（层级可能尚未挂到 world.gameLayer），实测到才冻结 world 指针。</summary>
        private static IntPtr FreezeWorldPointer(GameObject go, Knight knight)
        {
            try
            {
                if (knight == null) return IntPtr.Zero;
                if (!TryGetWorldContext(out World world, out GameObject worldGo)) return IntPtr.Zero;
                if (!TrySameScene(go, worldGo, out bool same) || !same) return IntPtr.Zero;
                return TryPointer(world, out IntPtr pointer) ? pointer : IntPtr.Zero;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        private static bool TryGetWorldContext(out World world, out GameObject worldGo)
        {
            world = null;
            worldGo = null;
            try
            {
                Managers managers = Managers.Inst;
                world = managers != null ? managers.world : null;
                if (world == null) return false;
                worldGo = world.gameObject;
                return worldGo != null;
            }
            catch
            {
                world = null;
                worldGo = null;
                return false;
            }
        }

        private static bool TrySameScene(GameObject a, GameObject b, out bool same)
        {
            same = false;
            try
            {
                if (a == null || b == null) return false;
                same = a.scene.handle == b.scene.handle;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryCompareTag(GameObject go, string tag, out bool matches)
        {
            matches = false;
            try
            {
                if (go == null) return false;
                matches = go.CompareTag(tag);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryInstanceId(GameObject go, out int instanceId)
        {
            instanceId = 0;
            try
            {
                if (go == null) return false;
                instanceId = go.GetInstanceID();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryPointer(UnityEngine.Object value, out IntPtr pointer)
        {
            pointer = IntPtr.Zero;
            try
            {
                if (value == null) return false;
                pointer = value.Pointer;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static int SafeInstanceId(GameObject go, int fallback)
        {
            return TryInstanceId(go, out int instanceId) ? instanceId : fallback;
        }

        private static IntPtr SafePointer(UnityEngine.Object value)
        {
            return TryPointer(value, out IntPtr pointer) ? pointer : IntPtr.Zero;
        }
    }
}
