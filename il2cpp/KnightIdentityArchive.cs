// 骑士身份附加存档（sidecar）核心：GUID + 固定风格(0..4) 的独立持久化与均衡分配。
// 契约：
//  * 不读写任何原生存档字段；原生 uniqueID 只作为“某一份精确原生岛快照”里的索引。
//  * v2：scope 是不透明 epoch（legacy 内嵌运行时 ticks，新的为随机 64-hex），contexts 记录
//    「稳定上下文（文件名 + campaign/challenge + land 的 SHA256）→ epoch 列表」；一个 scope 最多属于一个 context。
//  * snapshotHash 两种 kind：kind1 = 完整原生岛 JSON + scope 的 SHA256（legacy 配方）；
//    kind2 = 只剔除 3 个实测活时钟的时钟无关指纹（KnightIdentityFingerprint.Normalized）。
//    同 hash 不同 kind 视为冲突，绝不覆写。
//  * 只有 scope + hash 完全一致才允许 uniqueID→收据 恢复；任何失配一律拒绝，
//    绝不按位置/金币/名字/NetID 猜。旧档迁移由 KnightIdentityContexts 用两种精确 hash 做有界匹配。
//  * schemaVersion 只接受 1（legacy 只读兼容：快照一律 kind1、无 contexts）、2（前版：无修订号，仍按原封闭
//    schema 解析）与 4（当前：快照可携带用户修订号 rev；无修订号的旧文件读作 0，未产出修订时写回仍是 2，
//    字节级与旧行为一致）；其余版本（含未发布的 3）一律拒绝（不降级、不覆盖）；损坏主文件永不覆盖有效备份。
//  * 用户修订号（Revision）只由面板用户动作产出，写入时从重读的盘上取「该 context 全部 epoch 快照的最大值 + 1」；
//    自动路径写 0（不序列化该字段）或继承当前最大值（不递增）。快照身份 = (hash, 修订号) 二元组：
//    同二元组内容不同仍 RejectedConflict；同 hash 不同修订号允许共存（旧记录保留供审计/手工恢复）。
//  * root 未知字段原样保留；scope/snapshot/entry/context 里出现未知字段或重复字段即判 Corrupt（本 schema 是封闭的）。
//  * I/O 只发生在显式 Load / Save / RecoverMainFromBackup 调用：无后台线程、无 tick。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace KingdomEnhancedMod
{
    /// <summary>稳定身份收据：GUID 非空 + 固定风格 0..4。</summary>
    internal readonly struct KnightIdentityReceipt : IEquatable<KnightIdentityReceipt>
    {
        internal const int MinStyle = 0;
        internal const int MaxStyle = 4;
        internal const int StyleCount = MaxStyle - MinStyle + 1;

        internal readonly Guid Id;
        internal readonly int Style;

        internal KnightIdentityReceipt(Guid id, int style)
        {
            Id = id;
            Style = style;
        }

        internal static bool IsValidStyle(int style) { return style >= MinStyle && style <= MaxStyle; }

        internal bool IsValid { get { return Id != Guid.Empty && IsValidStyle(Style); } }

        public bool Equals(KnightIdentityReceipt other) { return Id == other.Id && Style == other.Style; }

        public override bool Equals(object obj) { return obj is KnightIdentityReceipt other && Equals(other); }

        public override int GetHashCode() { return unchecked((Id.GetHashCode() * 397) ^ Style); }

        public override string ToString()
        {
            return Id.ToString("N", CultureInfo.InvariantCulture) + "/" + Style.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>一份快照里的一条记录：native uniqueID（只作索引，最长 256） → 收据。</summary>
    internal readonly struct KnightIdentitySnapshotEntry
    {
        internal const int MaxNativeUniqueIdLength = 256;

        internal readonly string NativeUniqueId;
        internal readonly KnightIdentityReceipt Receipt;

        internal KnightIdentitySnapshotEntry(string nativeUniqueId, KnightIdentityReceipt receipt)
        {
            NativeUniqueId = nativeUniqueId;
            Receipt = receipt;
        }
    }

    /// <summary>一份成功记录的原生岛快照索引：hash 强绑定，条目 uniqueID 唯一、GUID 唯一。</summary>
    internal sealed class KnightIdentitySnapshot
    {
        private readonly Dictionary<string, KnightIdentityReceipt> _entries;
        private readonly string[] _orderedUniqueIds;

        internal readonly string Hash;
        internal readonly string SavedAtUtc;

        /// <summary>哈希函数（1 = legacy 全量 JSON，2 = 时钟无关指纹）；同 hash 不同 kind 视为不同快照。</summary>
        internal readonly int Kind;

        /// <summary>用户修订号：0 = 自动记录（不序列化）；&gt; 0 = 面板用户动作产出（快照身份的一部分）。</summary>
        internal readonly int Revision;

        private KnightIdentitySnapshot(int kind, string hash, string savedAtUtc, Dictionary<string, KnightIdentityReceipt> entries,
            string[] orderedUniqueIds, int revision)
        {
            Kind = kind;
            Hash = hash;
            SavedAtUtc = savedAtUtc;
            _entries = entries;
            _orderedUniqueIds = orderedUniqueIds;
            Revision = revision;
        }

        internal int Count { get { return _entries.Count; } }

        /// <summary>落盘顺序（uniqueID 序数升序），与插入顺序无关，保证字节稳定。</summary>
        internal IReadOnlyList<string> OrderedUniqueIds { get { return _orderedUniqueIds; } }

        internal bool TryGet(string nativeUniqueId, out KnightIdentityReceipt receipt)
        {
            receipt = default;
            return nativeUniqueId != null && _entries.TryGetValue(nativeUniqueId, out receipt);
        }

        /// <summary>
        /// hash + kind + 全部条目逐一相等（时间戳与修订号都是记录身份的一部分、不参与内容比较：
        /// 写回校验按 hash 查到的记录可能是继承修订号的副本）。
        /// </summary>
        internal bool HasSameEntries(KnightIdentitySnapshot other)
        {
            if (other == null || other._entries.Count != _entries.Count) return false;
            if (Kind != other.Kind) return false;
            if (!string.Equals(Hash, other.Hash, StringComparison.Ordinal)) return false;
            foreach (KeyValuePair<string, KnightIdentityReceipt> pair in _entries)
            {
                if (!other._entries.TryGetValue(pair.Key, out KnightIdentityReceipt receipt)) return false;
                if (!receipt.Equals(pair.Value)) return false;
            }
            return true;
        }

        /// <summary>hash + 时间戳 + 全部条目逐一相等。</summary>
        internal bool HasSameContent(KnightIdentitySnapshot other)
        {
            return HasSameEntries(other) && string.Equals(SavedAtUtc, other.SavedAtUtc, StringComparison.Ordinal);
        }

        /// <summary>时间戳按 UTC 归一化后存储的版本。</summary>
        internal static bool TryCreate(int kind, string hash, DateTimeOffset savedAtUtc, IReadOnlyList<KnightIdentitySnapshotEntry> entries,
            out KnightIdentitySnapshot snapshot, out string error)
        {
            return TryCreate(kind, hash, savedAtUtc, entries, 0, out snapshot, out error);
        }

        /// <summary>带用户修订号的版本（&gt; 0 时序列化该字段；0 = 自动记录）。</summary>
        internal static bool TryCreate(int kind, string hash, DateTimeOffset savedAtUtc, IReadOnlyList<KnightIdentitySnapshotEntry> entries,
            int revision, out KnightIdentitySnapshot snapshot, out string error)
        {
            return TryCreateCore(kind, hash, savedAtUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture), entries, revision,
                out snapshot, out error);
        }

        /// <summary>同内容（kind/hash/时间戳/全部条目）的副本，只替换修订号；非法修订号返回 false。</summary>
        internal bool TryCopyWithRevision(int revision, out KnightIdentitySnapshot copy, out string error)
        {
            error = null;
            copy = null;
            if (revision == Revision) { copy = this; return true; }
            if (revision < 0) { error = "revision must be non-negative"; return false; }
            KnightIdentitySnapshotEntry[] entries = new KnightIdentitySnapshotEntry[_orderedUniqueIds.Length];
            for (int i = 0; i < entries.Length; i++)
            {
                _entries.TryGetValue(_orderedUniqueIds[i], out KnightIdentityReceipt receipt);
                entries[i] = new KnightIdentitySnapshotEntry(_orderedUniqueIds[i], receipt);
            }
            return TryCreateCore(Kind, Hash, SavedAtUtc, entries, revision, out copy, out error);
        }

        /// <summary>
        /// 唯一校验入口（旧签名 = 自动记录修订号 0）。拒绝：kind 非 1/2、hash 非 64-hex、时间戳非 ISO-8601、
        /// 修订号为负、条目超 512、uniqueID 空/超 256、GUID 为空、风格越界、同快照内重复 uniqueID 或重复 GUID。
        /// </summary>
        internal static bool TryCreateCore(int kind, string hash, string savedAtUtc, IReadOnlyList<KnightIdentitySnapshotEntry> entries,
            out KnightIdentitySnapshot snapshot, out string error)
        {
            return TryCreateCore(kind, hash, savedAtUtc, entries, 0, out snapshot, out error);
        }

        /// <summary>唯一校验入口（显式用户修订号版本）。</summary>
        internal static bool TryCreateCore(int kind, string hash, string savedAtUtc, IReadOnlyList<KnightIdentitySnapshotEntry> entries,
            int revision, out KnightIdentitySnapshot snapshot, out string error)
        {
            snapshot = null;
            error = null;
            if (!KnightIdentityFingerprint.IsValidKind(kind)) return Fail("kind must be 1 or 2", out error);
            if (revision < 0) return Fail("revision must be non-negative", out error);
            string normalizedHash = KnightIdentityFingerprint.NormalizeHex64(hash);
            if (normalizedHash == null) return Fail("hash is not 64 hex chars", out error);
            if (savedAtUtc == null || !IsIsoTimestamp(savedAtUtc)) return Fail("savedAtUtc is not an ISO-8601 timestamp", out error);
            if (entries == null || entries.Count > KnightIdentityArchive.MaxEntriesPerSnapshot)
                return Fail("entry count must be 0.." + KnightIdentityArchive.MaxEntriesPerSnapshot, out error);

            Dictionary<string, KnightIdentityReceipt> map = new Dictionary<string, KnightIdentityReceipt>(entries.Count, StringComparer.Ordinal);
            HashSet<Guid> identities = new HashSet<Guid>();
            for (int i = 0; i < entries.Count; i++)
            {
                KnightIdentitySnapshotEntry entry = entries[i];
                if (!IsValidUniqueId(entry.NativeUniqueId))
                    return Fail("uniqueID must be 1.." + KnightIdentitySnapshotEntry.MaxNativeUniqueIdLength + " chars (index " + i + ")", out error);
                if (!entry.Receipt.IsValid)
                    return Fail("receipt needs non-empty GUID and style 0.." + KnightIdentityReceipt.MaxStyle + " (uniqueID " + entry.NativeUniqueId + ")", out error);
                if (map.ContainsKey(entry.NativeUniqueId)) return Fail("duplicate uniqueID " + entry.NativeUniqueId, out error);
                if (!identities.Add(entry.Receipt.Id)) return Fail("duplicate GUID " + entry.Receipt.Id.ToString("N", CultureInfo.InvariantCulture), out error);
                map.Add(entry.NativeUniqueId, entry.Receipt);
            }

            string[] ordered = new string[map.Count];
            map.Keys.CopyTo(ordered, 0);
            Array.Sort(ordered, StringComparer.Ordinal);
            snapshot = new KnightIdentitySnapshot(kind, normalizedHash, savedAtUtc, map, ordered, revision);
            return true;
        }

        internal static bool IsValidUniqueId(string value)
        {
            return !string.IsNullOrEmpty(value) && value.Length <= KnightIdentitySnapshotEntry.MaxNativeUniqueIdLength;
        }

        internal static bool IsIsoTimestamp(string value)
        {
            return !string.IsNullOrEmpty(value) && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
        }

        private static bool Fail(string message, out string error)
        {
            error = message;
            return false;
        }
    }

    /// <summary>
    /// 一个稳定岛上下文（存档文件名 + campaign/challenge + land）拥有的不透明 epoch 作用域列表。
    /// Active 恒为 Epochs[0]；epoch 作用域彼此不透明（legacy 内嵌运行时 ticks，新的为随机值），
    /// 一个 scope 最多属于一个 context。
    /// </summary>
    internal sealed class KnightIdentityContext
    {
        internal string Active;
        internal readonly List<string> Epochs = new List<string>();

        internal bool Owns(string scope)
        {
            for (int i = 0; i < Epochs.Count; i++)
            {
                if (string.Equals(Epochs[i], scope, StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }

    /// <summary>盘上状态：Unsupported（未知 schemaVersion）绝不能被当作 Missing。</summary>
    internal enum KnightIdentityArchiveStatus
    {
        Missing,
        Valid,
        Corrupt,
        UnsupportedVersion,
    }

    /// <summary>
    /// 附加存档纯模型。每 scope 保留最近 <see cref="MaxSnapshotsPerScope"/> 份成功记录快照，按 (hash, 修订号)
    /// 去重替换（幂等）；超容量一律拒绝 mutation，绝不淘汰别的 scope，只淘汰本 scope 最老历史。
    /// v2 起额外持久化「稳定上下文 → 不透明 epoch 作用域」映射（legacy v1 文件按只读兼容解析）；
    /// v4 起快照可携带用户修订号（面板用户动作；同 hash 的修订链允许共存，旧记录在淘汰前一直保留）。
    /// </summary>
    internal sealed class KnightIdentityArchive
    {
        /// <summary>当前 schema：快照可携带用户修订号 rev（无修订时写回仍用 <see cref="PreviousSchemaVersion"/>）。</summary>
        internal const int SchemaVersion = 4;

        /// <summary>前版 schema（v2）：没有修订号字段，仍按原封闭 schema 解析/写回。</summary>
        internal const int PreviousSchemaVersion = 2;

        internal const int LegacySchemaVersion = 1;
        internal const int MaxScopes = 128;
        internal const int MaxSnapshotsPerScope = 8;
        internal const int MaxEntriesPerSnapshot = 512;
        internal const int MaxTotalEntries = 32768;
        internal const int MaxContexts = 64;
        internal const int MaxEpochs = 8;

        /// <summary>root 对象字段总数上限（含未知字段）：防止恶意 root 拖垮解析。</summary>
        internal const int MaxRootFieldCount = 256;

        private const string SchemaVersionField = "schemaVersion";
        private const string ScopesField = "scopes";
        private const string ContextsField = "contexts";
        private const string ContextField = "context";
        private const string ActiveField = "active";
        private const string EpochsField = "epochs";
        private const string ScopeKeyField = "scopeKey";
        private const string SnapshotsField = "snapshots";
        private const string HashField = "hash";
        private const string KindField = "kind";
        private const string SavedAtField = "savedAtUtc";
        private const string RevisionField = "rev";
        private const string EntriesField = "entries";
        private const string UniqueIdField = "u";
        private const string IdField = "id";
        private const string StyleField = "style";

        private static readonly string[] RootFieldsV1 = { SchemaVersionField, ScopesField };
        private static readonly string[] RootFieldsV2 = { SchemaVersionField, ScopesField, ContextsField };
        private static readonly string[] ScopeFields = { ScopeKeyField, SnapshotsField };
        private static readonly string[] SnapshotFieldsV1 = { HashField, SavedAtField, EntriesField };
        private static readonly string[] SnapshotFieldsV2 = { HashField, KindField, SavedAtField, EntriesField };
        private static readonly string[] SnapshotFieldsV4 = { HashField, KindField, SavedAtField, RevisionField, EntriesField };
        private static readonly string[] EntryFields = { UniqueIdField, IdField, StyleField };
        private static readonly string[] ContextFields = { ContextField, ActiveField, EpochsField };

        private static readonly JsonDocumentOptions ParseOptions = new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = KnightIdentityArchiveStore.MaxJsonDepth,
        };

        private readonly Dictionary<string, ScopeNode> _scopes = new Dictionary<string, ScopeNode>(StringComparer.Ordinal);
        private readonly Dictionary<string, KnightIdentityContext> _contexts = new Dictionary<string, KnightIdentityContext>(StringComparer.Ordinal);
        private List<KeyValuePair<string, JsonElement>> _unknownRootFields;

        private KnightIdentityArchive() { }

        internal static KnightIdentityArchive CreateEmpty() { return new KnightIdentityArchive(); }

        internal int ScopeCount { get { return _scopes.Count; } }

        internal int ContextCount { get { return _contexts.Count; } }

        /// <summary>
        /// 稳定岛上下文键：存档文件名 + campaign/challenge + land 的 SHA256。
        /// 刻意不包含 realStartDateTime/NetID/instanceID 等每次读取都会重建的运行时值。
        /// </summary>
        internal static string ContextKey(string file, int campaign, int challenge, int land)
        {
            StringBuilder builder = new StringBuilder(192);
            AppendField(builder, "file", file);
            AppendField(builder, "campaign", campaign.ToString(CultureInfo.InvariantCulture));
            AppendField(builder, "challenge", challenge.ToString(CultureInfo.InvariantCulture));
            AppendField(builder, "land", land.ToString(CultureInfo.InvariantCulture));
            return KnightIdentityFingerprint.Sha256(builder.ToString(), "knight-context");
        }

        /// <summary>新世代的不透明 epoch 作用域：私有随机 64-hex，绝不由运行时状态派生。</summary>
        internal static string NewScope()
        {
            return Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        }

        private static void AppendField(StringBuilder builder, string name, string value)
        {
            if (value == null) value = string.Empty;
            builder.Append(name).Append('=').Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append('\n');
        }

        internal bool TryGetContext(string contextKey, out KnightIdentityContext context)
        {
            context = null;
            return contextKey != null && _contexts.TryGetValue(contextKey, out context) && context != null;
        }

        /// <summary>scope 是否被任何 context 拥有。</summary>
        internal bool ScopeClaimed(string scopeKey)
        {
            if (scopeKey == null) return false;
            foreach (KnightIdentityContext context in _contexts.Values)
            {
                if (context.Owns(scopeKey)) return true;
            }
            return false;
        }

        /// <summary>scope 是否被除 <paramref name="exceptContextKey"/> 之外的 context 拥有。</summary>
        internal bool ScopeClaimedBy(string scopeKey, string exceptContextKey)
        {
            if (scopeKey == null) return false;
            foreach (KeyValuePair<string, KnightIdentityContext> pair in _contexts)
            {
                if (string.Equals(pair.Key, exceptContextKey, StringComparison.Ordinal)) continue;
                if (pair.Value.Owns(scopeKey)) return true;
            }
            return false;
        }

        /// <summary>scope 里是否存在指定 kind 的快照。</summary>
        internal bool ScopeHasKind(string scopeKey, int kind)
        {
            string key = KnightIdentityFingerprint.NormalizeHex64(scopeKey);
            if (key == null || !_scopes.TryGetValue(key, out ScopeNode node)) return false;
            for (int i = 0; i < node.Snapshots.Count; i++)
            {
                if (node.Snapshots[i].Kind == kind) return true;
            }
            return false;
        }

        /// <summary>全部 scope 键的稳定升序副本（解析/迁移的确定性扫描顺序）。</summary>
        internal string[] OrderedScopeKeys()
        {
            string[] keys = new string[_scopes.Count];
            _scopes.Keys.CopyTo(keys, 0);
            Array.Sort(keys, StringComparer.Ordinal);
            return keys;
        }

        /// <summary>
        /// 注册或刷新 context → epoch。scope 必须已存在且未被别的 context 拥有；
        /// <paramref name="allowNewEpoch"/> 只为「已确认的匹配/新世代」放行新 epoch；
        /// 容量一律 fail closed（丢弃旧 epoch 会静默丢历史）。
        /// </summary>
        internal bool EnsureContext(string contextKey, string scopeKey, bool allowNewEpoch)
        {
            string key = KnightIdentityFingerprint.NormalizeHex64(contextKey);
            string scope = KnightIdentityFingerprint.NormalizeHex64(scopeKey);
            if (key == null || scope == null) return false;
            if (!_scopes.ContainsKey(scope)) return false;
            if (ScopeClaimedBy(scope, key)) return false;

            if (!_contexts.TryGetValue(key, out KnightIdentityContext context))
            {
                if (_contexts.Count >= MaxContexts) return false;
                context = new KnightIdentityContext();
                context.Epochs.Add(scope);
                context.Active = scope;
                _contexts.Add(key, context);
                return true;
            }
            if (string.Equals(context.Active, scope, StringComparison.Ordinal)) return true;
            int index = context.Epochs.IndexOf(scope);
            if (index >= 0)
            {
                // 本 context 的旧 epoch：active 回退到该代（保留更新的历史）。
                context.Epochs.RemoveAt(index);
                context.Epochs.Insert(0, scope);
                context.Active = scope;
                return true;
            }
            if (!allowNewEpoch) return false;
            if (context.Epochs.Count >= MaxEpochs) return false;
            context.Epochs.Insert(0, scope);
            context.Active = scope;
            return true;
        }

        /// <summary>全部 scope×快照 的条目总数（含历史）。</summary>
        internal int TotalEntryCount
        {
            get
            {
                int total = 0;
                foreach (ScopeNode node in _scopes.Values)
                {
                    for (int i = 0; i < node.Snapshots.Count; i++) total += node.Snapshots[i].Count;
                }
                return total;
            }
        }

        internal enum MutationStatus
        {
            Applied,
            Unchanged,
            RejectedInvalid,
            RejectedCapacity,

            /// <summary>同 scope 已有同 hash 但内容不同（或 kind 不同）的记录：绝不覆写，原记录保持不变。</summary>
            RejectedConflict,
        }

        /// <summary>
        /// 记录一份成功快照。同 scope 同 (hash, 修订号) 且内容相同 → 幂等（顶到最近）；同二元组但内容不同
        /// （kind 也算内容）→ <see cref="MutationStatus.RejectedConflict"/>（原记录不动）；同 hash 不同修订号
        /// （用户修订链）允许共存；满 8 份只淘汰本 scope 最老一份；
        /// scope 数或总条目超上限一律 <see cref="MutationStatus.RejectedCapacity"/>（不删别的 scope）。
        /// </summary>
        internal MutationStatus RecordSnapshot(string scopeKey, KnightIdentitySnapshot snapshot)
        {
            string key = KnightIdentityFingerprint.NormalizeHex64(scopeKey);
            if (key == null || snapshot == null) return MutationStatus.RejectedInvalid;

            if (!_scopes.TryGetValue(key, out ScopeNode node))
            {
                if (_scopes.Count >= MaxScopes) return MutationStatus.RejectedCapacity;
                if (TotalEntryCount + snapshot.Count > MaxTotalEntries) return MutationStatus.RejectedCapacity;
                _scopes.Add(key, new ScopeNode(new List<KnightIdentitySnapshot> { snapshot }));
                return MutationStatus.Applied;
            }

            List<KnightIdentitySnapshot> snapshots = node.Snapshots;
            int existing = -1;
            for (int i = 0; i < snapshots.Count; i++)
            {
                if (snapshots[i].Revision != snapshot.Revision) continue;
                if (string.Equals(snapshots[i].Hash, snapshot.Hash, StringComparison.Ordinal)) { existing = i; break; }
            }

            if (existing >= 0)
            {
                KnightIdentitySnapshot known = snapshots[existing];
                if (!known.HasSameEntries(snapshot)) return MutationStatus.RejectedConflict;
                if (existing == 0 && known.HasSameContent(snapshot)) return MutationStatus.Unchanged;
                snapshots.RemoveAt(existing);
                snapshots.Insert(0, snapshot);   // 同一份快照重新上报：只刷新顺序/时间戳，条目总数不变
                return MutationStatus.Applied;
            }

            int evicted = snapshots.Count >= MaxSnapshotsPerScope ? snapshots[snapshots.Count - 1].Count : 0;
            if (TotalEntryCount - evicted + snapshot.Count > MaxTotalEntries) return MutationStatus.RejectedCapacity;
            if (snapshots.Count >= MaxSnapshotsPerScope) snapshots.RemoveAt(snapshots.Count - 1);
            snapshots.Insert(0, snapshot);
            return MutationStatus.Applied;
        }

        /// <summary>
        /// 按 scopeKey + snapshotHash 取快照（任一不匹配返回 false）。同 hash 可存在多个用户修订：
        /// 本重载返回列表首条（最近写入）；需要精确修订号的调用方用
        /// <see cref="TryGetSnapshot(string, string, int, out KnightIdentitySnapshot)"/>，需要全部修订的调用方
        /// 枚举 <see cref="TryGetSnapshots"/>。
        /// </summary>
        internal bool TryGetSnapshot(string scopeKey, string snapshotHash, out KnightIdentitySnapshot snapshot)
        {
            snapshot = null;
            string key = KnightIdentityFingerprint.NormalizeHex64(scopeKey);
            string hash = KnightIdentityFingerprint.NormalizeHex64(snapshotHash);
            if (key == null || hash == null || !_scopes.TryGetValue(key, out ScopeNode node)) return false;
            for (int i = 0; i < node.Snapshots.Count; i++)
            {
                if (string.Equals(node.Snapshots[i].Hash, hash, StringComparison.Ordinal)) { snapshot = node.Snapshots[i]; return true; }
            }
            return false;
        }

        /// <summary>按 scopeKey + snapshotHash + 修订号（唯一键）精确取快照；任一不匹配返回 false。</summary>
        internal bool TryGetSnapshot(string scopeKey, string snapshotHash, int revision, out KnightIdentitySnapshot snapshot)
        {
            snapshot = null;
            string key = KnightIdentityFingerprint.NormalizeHex64(scopeKey);
            string hash = KnightIdentityFingerprint.NormalizeHex64(snapshotHash);
            if (key == null || hash == null || !_scopes.TryGetValue(key, out ScopeNode node)) return false;
            for (int i = 0; i < node.Snapshots.Count; i++)
            {
                KnightIdentitySnapshot candidate = node.Snapshots[i];
                if (candidate.Revision != revision) continue;
                if (string.Equals(candidate.Hash, hash, StringComparison.Ordinal)) { snapshot = candidate; return true; }
            }
            return false;
        }

        /// <summary>
        /// 该 scope 里快照的最大用户修订号（无记录 = 0）。自动写入继承本值（不递增）：同 scope 同 hash 的
        /// 自动重报落在用户修订同一键上（幂等或 RejectedConflict），绝不抬升到会与别的 epoch 并列的层级。
        /// </summary>
        internal int MaxScopeRevision(string scopeKey)
        {
            string key = KnightIdentityFingerprint.NormalizeHex64(scopeKey);
            if (key == null || !_scopes.TryGetValue(key, out ScopeNode node)) return 0;
            int max = 0;
            for (int i = 0; i < node.Snapshots.Count; i++)
            {
                int revision = node.Snapshots[i].Revision;
                if (revision > max) max = revision;
            }
            return max;
        }

        /// <summary>
        /// 该上下文全部 epoch 快照的最大用户修订号（无上下文/无记录 = 0）。
        /// 用户写入取 +1（跨 epoch 单调递增，修订才能压过旧 epoch 的记录）。
        /// </summary>
        internal int MaxRevision(string contextKey)
        {
            string key = KnightIdentityFingerprint.NormalizeHex64(contextKey);
            if (key == null || !_contexts.TryGetValue(key, out KnightIdentityContext context)) return 0;
            int max = 0;
            for (int e = 0; e < context.Epochs.Count; e++)
            {
                if (!_scopes.TryGetValue(context.Epochs[e], out ScopeNode node)) continue;
                for (int i = 0; i < node.Snapshots.Count; i++)
                {
                    int revision = node.Snapshots[i].Revision;
                    if (revision > max) max = revision;
                }
            }
            return max;
        }

        /// <summary>恢复入口：只有 scope + hash 完全一致且 uniqueID 存在时才给回收据。</summary>
        internal bool TryRestore(string scopeKey, string snapshotHash, string nativeUniqueId, out KnightIdentityReceipt receipt)
        {
            receipt = default;
            return KnightIdentitySnapshot.IsValidUniqueId(nativeUniqueId)
                && TryGetSnapshot(scopeKey, snapshotHash, out KnightIdentitySnapshot snapshot)
                && snapshot.TryGet(nativeUniqueId, out receipt);
        }

        /// <summary>某 scope 的快照（新→旧）；不存在返回 false。</summary>
        internal bool TryGetSnapshots(string scopeKey, out IReadOnlyList<KnightIdentitySnapshot> snapshots)
        {
            snapshots = null;
            string key = KnightIdentityFingerprint.NormalizeHex64(scopeKey);
            if (key == null || !_scopes.TryGetValue(key, out ScopeNode node)) return false;
            snapshots = node.Snapshots;
            return true;
        }

        // ------------------------------------------------------------------ 落盘（确定性字节）

        /// <summary>确定性 UTF-8 JSON：scope/context 按 key 升序、entry 按 uniqueID 升序、root 未知字段原样追加。</summary>
        internal byte[] SerializeToUtf8()
        {
            using (MemoryStream stream = new MemoryStream())
            {
                using (Utf8JsonWriter writer = new Utf8JsonWriter(stream))
                {
                    writer.WriteStartObject();
                    // 只有真的存在用户修订号时才写 v4：无修订的存档仍是 v2 字节级形状（旧构建可读）。
                    writer.WriteNumber(SchemaVersionField, UsesRevisionSchema() ? SchemaVersion : PreviousSchemaVersion);
                    writer.WriteStartArray(ScopesField);
                    string[] keys = new string[_scopes.Count];
                    _scopes.Keys.CopyTo(keys, 0);
                    Array.Sort(keys, StringComparer.Ordinal);
                    for (int i = 0; i < keys.Length; i++)
                    {
                        ScopeNode node = _scopes[keys[i]];
                        writer.WriteStartObject();
                        writer.WriteString(ScopeKeyField, keys[i]);
                        writer.WriteStartArray(SnapshotsField);
                        for (int s = 0; s < node.Snapshots.Count; s++) WriteSnapshot(writer, node.Snapshots[s]);
                        writer.WriteEndArray();
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                    writer.WriteStartArray(ContextsField);
                    string[] contextKeys = new string[_contexts.Count];
                    _contexts.Keys.CopyTo(contextKeys, 0);
                    Array.Sort(contextKeys, StringComparer.Ordinal);
                    for (int i = 0; i < contextKeys.Length; i++)
                    {
                        KnightIdentityContext context = _contexts[contextKeys[i]];
                        writer.WriteStartObject();
                        writer.WriteString(ContextField, contextKeys[i]);
                        writer.WriteString(ActiveField, context.Active);
                        writer.WriteStartArray(EpochsField);
                        for (int e = 0; e < context.Epochs.Count; e++) writer.WriteStringValue(context.Epochs[e]);
                        writer.WriteEndArray();
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                    WriteExtras(writer, _unknownRootFields);
                    writer.WriteEndObject();
                }
                return stream.ToArray();
            }
        }

        /// <summary>是否存在任何携带用户修订号的快照（决定序列化 v4 还是 v2）。</summary>
        private bool UsesRevisionSchema()
        {
            foreach (ScopeNode node in _scopes.Values)
            {
                for (int i = 0; i < node.Snapshots.Count; i++)
                {
                    if (node.Snapshots[i].Revision > 0) return true;
                }
            }
            return false;
        }

        private static void WriteSnapshot(Utf8JsonWriter writer, KnightIdentitySnapshot snapshot)
        {
            writer.WriteStartObject();
            writer.WriteString(HashField, snapshot.Hash);
            writer.WriteNumber(KindField, snapshot.Kind);
            writer.WriteString(SavedAtField, snapshot.SavedAtUtc);
            if (snapshot.Revision > 0) writer.WriteNumber(RevisionField, snapshot.Revision); // rev=0 不序列化（字节兼容）
            writer.WriteStartArray(EntriesField);
            IReadOnlyList<string> ids = snapshot.OrderedUniqueIds;
            for (int i = 0; i < ids.Count; i++)
            {
                snapshot.TryGet(ids[i], out KnightIdentityReceipt receipt);
                writer.WriteStartObject();
                writer.WriteString(UniqueIdField, ids[i]);
                writer.WriteString(IdField, receipt.Id.ToString("N", CultureInfo.InvariantCulture));
                writer.WriteNumber(StyleField, receipt.Style);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        private static void WriteExtras(Utf8JsonWriter writer, List<KeyValuePair<string, JsonElement>> extras)
        {
            if (extras == null) return;
            for (int i = 0; i < extras.Count; i++)
            {
                writer.WritePropertyName(extras[i].Key);
                extras[i].Value.WriteTo(writer);
            }
        }

        // ------------------------------------------------------------------ 解析

        /// <summary>解析一份 UTF-8 JSON。未知 schemaVersion 返回 UnsupportedVersion（永不降级）。</summary>
        internal static KnightIdentityArchiveStatus Parse(byte[] utf8, out KnightIdentityArchive archive, out string error)
        {
            archive = null;
            error = null;
            if (utf8 == null) { error = "bytes are null"; return KnightIdentityArchiveStatus.Corrupt; }
            if (utf8.Length == 0) { error = "bytes are empty"; return KnightIdentityArchiveStatus.Corrupt; }
            if (utf8.Length > KnightIdentityArchiveStore.MaxFileBytes)
            {
                error = "input is " + utf8.Length.ToString(CultureInfo.InvariantCulture) + " bytes, above " + KnightIdentityArchiveStore.MaxFileBytes.ToString(CultureInfo.InvariantCulture);
                return KnightIdentityArchiveStatus.Corrupt;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(utf8, ParseOptions);
            }
            catch (JsonException e)
            {
                error = "invalid json: " + e.Message;
                return KnightIdentityArchiveStatus.Corrupt;
            }

            using (document)
            {
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object) { error = "root is not an object"; return KnightIdentityArchiveStatus.Corrupt; }

                // 先独立判定版本：未知版本的文件体不按本 schema 解释，否则“更高版本”会因字段不认识而被误判
                // 成 Corrupt，而 Corrupt 拒绝普通 Save 覆盖。v1（legacy）快照一律 kind=1；v2 无修订号字段；
                // v4 允许可选 rev（缺省 0）。3 从未发布，仍按未知版本拒绝。
                int version = -1;
                foreach (JsonProperty property in root.EnumerateObject())
                {
                    if (!property.NameEquals(SchemaVersionField)) continue;
                    if (version != -1) { error = "duplicate root field " + SchemaVersionField; return KnightIdentityArchiveStatus.Corrupt; }
                    if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt32(out version))
                    {
                        error = SchemaVersionField + " is not an integer";
                        return KnightIdentityArchiveStatus.Corrupt;
                    }
                    if (version != SchemaVersion && version != PreviousSchemaVersion && version != LegacySchemaVersion)
                    {
                        error = "unsupported schemaVersion " + version.ToString(CultureInfo.InvariantCulture);
                        return KnightIdentityArchiveStatus.UnsupportedVersion;
                    }
                }
                if (version == -1) { error = "missing " + SchemaVersionField; return KnightIdentityArchiveStatus.Corrupt; }
                bool legacy = version == LegacySchemaVersion;
                bool revisioned = version == SchemaVersion;

                KnightIdentityArchive result = CreateEmpty();
                if (!TryReadFields(root, legacy ? RootFieldsV1 : RootFieldsV2, "root", MaxRootFieldCount, out JsonElement[] fields, out result._unknownRootFields, out error))
                    return KnightIdentityArchiveStatus.Corrupt;
                if (fields[1].ValueKind != JsonValueKind.Undefined && !TryReadScopes(fields[1], legacy, revisioned, result, out error))
                    return KnightIdentityArchiveStatus.Corrupt;
                if (result.TotalEntryCount > MaxTotalEntries)
                {
                    error = "more than " + MaxTotalEntries + " entries";
                    return KnightIdentityArchiveStatus.Corrupt;
                }
                if (!legacy)
                {
                    if (fields[2].ValueKind == JsonValueKind.Undefined) { error = "missing " + ContextsField; return KnightIdentityArchiveStatus.Corrupt; }
                    if (!TryReadContexts(fields[2], result, out error)) return KnightIdentityArchiveStatus.Corrupt;
                }

                archive = result;
                return KnightIdentityArchiveStatus.Valid;
            }
        }

        private static bool TryReadScopes(JsonElement value, bool legacy, bool revisioned, KnightIdentityArchive result, out string error)
        {
            error = null;
            if (value.ValueKind != JsonValueKind.Array) return Fail("scopes is not an array", out error);
            if (value.GetArrayLength() > MaxScopes) return Fail("more than " + MaxScopes + " scopes", out error);
            foreach (JsonElement element in value.EnumerateArray())
            {
                if (!TryReadScope(element, legacy, revisioned, result, out error)) return false;
            }
            return true;
        }

        private static bool TryReadScope(JsonElement element, bool legacy, bool revisioned, KnightIdentityArchive result, out string error)
        {
            if (!TryReadClosedFields(element, ScopeFields, "scope", out JsonElement[] fields, out error)) return false;
            string scopeKey = fields[0].ValueKind == JsonValueKind.String ? KnightIdentityFingerprint.NormalizeHex64(fields[0].GetString()) : null;
            if (scopeKey == null) return Fail("scopeKey is not 64 hex chars", out error);
            if (fields[1].ValueKind == JsonValueKind.Undefined) return Fail("scope is missing " + SnapshotsField, out error);
            if (!TryReadSnapshots(fields[1], legacy, revisioned, out List<KnightIdentitySnapshot> snapshots, out error)) return false;
            if (result._scopes.ContainsKey(scopeKey)) return Fail("duplicate scopeKey " + scopeKey, out error);
            result._scopes.Add(scopeKey, new ScopeNode(snapshots));
            return true;
        }

        private static bool TryReadSnapshots(JsonElement value, bool legacy, bool revisioned, out List<KnightIdentitySnapshot> snapshots, out string error)
        {
            snapshots = null;
            error = null;
            if (value.ValueKind != JsonValueKind.Array) return Fail("snapshots is not an array", out error);
            if (value.GetArrayLength() > MaxSnapshotsPerScope) return Fail("more than " + MaxSnapshotsPerScope + " snapshots in one scope", out error);

            List<KnightIdentitySnapshot> list = new List<KnightIdentitySnapshot>(value.GetArrayLength());
            HashSet<string> identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement element in value.EnumerateArray())
            {
                if (!TryReadSnapshot(element, legacy, revisioned, out KnightIdentitySnapshot snapshot, out error)) return false;
                // 身份 = (hash, 修订号)：同 hash 的多个用户修订各占一条；同二元组重复才是 Corrupt。
                if (!identities.Add(snapshot.Hash + ":" + snapshot.Revision.ToString(CultureInfo.InvariantCulture)))
                    return Fail("duplicate snapshot hash+revision in one scope", out error);
                list.Add(snapshot);
            }
            snapshots = list;
            return true;
        }

        private static bool TryReadSnapshot(JsonElement element, bool legacy, bool revisioned, out KnightIdentitySnapshot snapshot, out string error)
        {
            snapshot = null;
            string[] known = legacy ? SnapshotFieldsV1 : revisioned ? SnapshotFieldsV4 : SnapshotFieldsV2;
            if (!TryReadClosedFields(element, known, "snapshot", out JsonElement[] fields, out error)) return false;
            if (fields[0].ValueKind != JsonValueKind.String) return Fail("snapshot " + HashField + " is not a string", out error);

            int kind = KnightIdentityFingerprint.KindLegacy;
            int savedAtIndex = 1;
            int entriesIndex = 2;
            int revision = 0;
            if (!legacy)
            {
                if (fields[1].ValueKind != JsonValueKind.Number || !fields[1].TryGetInt32(out kind))
                    return Fail("snapshot " + KindField + " is not an integer", out error);
                savedAtIndex = 2;
                entriesIndex = 3;
                if (revisioned)
                {
                    entriesIndex = 4;
                    // rev 可选：缺省（未序列化）就是自动记录 0。
                    if (fields[3].ValueKind == JsonValueKind.Number)
                    {
                        if (!fields[3].TryGetInt32(out revision)) return Fail("snapshot " + RevisionField + " is not an integer", out error);
                    }
                    else if (fields[3].ValueKind != JsonValueKind.Undefined)
                    {
                        return Fail("snapshot " + RevisionField + " is not an integer", out error);
                    }
                }
            }
            if (fields[savedAtIndex].ValueKind != JsonValueKind.String) return Fail("snapshot " + SavedAtField + " is not a string", out error);

            IReadOnlyList<KnightIdentitySnapshotEntry> entries = Array.Empty<KnightIdentitySnapshotEntry>();
            if (fields[entriesIndex].ValueKind != JsonValueKind.Undefined && !TryReadEntries(fields[entriesIndex], out entries, out error)) return false;
            return KnightIdentitySnapshot.TryCreateCore(kind, fields[0].GetString(), fields[savedAtIndex].GetString(), entries, revision, out snapshot, out error);
        }

        /// <summary>
        /// v2/v4 contexts：稳定上下文 → epoch 列表。active 恒为 epochs[0]；每个 epoch 必须已存在且只属于一个 context。
        /// </summary>
        private static bool TryReadContexts(JsonElement value, KnightIdentityArchive result, out string error)
        {
            error = null;
            if (value.ValueKind != JsonValueKind.Array) return Fail("contexts is not an array", out error);
            if (value.GetArrayLength() > MaxContexts) return Fail("more than " + MaxContexts + " contexts", out error);
            foreach (JsonElement element in value.EnumerateArray())
            {
                if (!TryReadClosedFields(element, ContextFields, "context", out JsonElement[] fields, out error)) return false;
                string contextKey = fields[0].ValueKind == JsonValueKind.String ? KnightIdentityFingerprint.NormalizeHex64(fields[0].GetString()) : null;
                if (contextKey == null) return Fail("context is not 64 hex chars", out error);
                if (result._contexts.ContainsKey(contextKey)) return Fail("duplicate context " + contextKey, out error);
                if (fields[2].ValueKind != JsonValueKind.Array) return Fail("context " + EpochsField + " is not an array", out error);
                if (fields[2].GetArrayLength() < 1 || fields[2].GetArrayLength() > MaxEpochs)
                    return Fail("context epoch count must be 1.." + MaxEpochs, out error);

                KnightIdentityContext context = new KnightIdentityContext();
                foreach (JsonElement epochValue in fields[2].EnumerateArray())
                {
                    string epoch = epochValue.ValueKind == JsonValueKind.String ? KnightIdentityFingerprint.NormalizeHex64(epochValue.GetString()) : null;
                    if (epoch == null) return Fail("context epoch is not 64 hex chars", out error);
                    if (context.Owns(epoch)) return Fail("duplicate context epoch " + epoch, out error);
                    if (!result._scopes.ContainsKey(epoch)) return Fail("context epoch has no scope " + epoch, out error);
                    if (result.ScopeClaimed(epoch)) return Fail("epoch owned by two contexts: " + epoch, out error);
                    context.Epochs.Add(epoch);
                }
                if (fields[1].ValueKind != JsonValueKind.String) return Fail("context " + ActiveField + " is not a string", out error);
                context.Active = KnightIdentityFingerprint.NormalizeHex64(fields[1].GetString());
                if (context.Active == null || !string.Equals(context.Active, context.Epochs[0], StringComparison.Ordinal))
                    return Fail("active epoch must be the newest context epoch", out error);
                result._contexts.Add(contextKey, context);
            }
            return true;
        }

        private static bool TryReadEntries(JsonElement value, out IReadOnlyList<KnightIdentitySnapshotEntry> entries, out string error)
        {
            entries = null;
            error = null;
            if (value.ValueKind != JsonValueKind.Array) return Fail("entries is not an array", out error);
            if (value.GetArrayLength() > MaxEntriesPerSnapshot) return Fail("more than " + MaxEntriesPerSnapshot + " entries in one snapshot", out error);

            List<KnightIdentitySnapshotEntry> list = new List<KnightIdentitySnapshotEntry>(value.GetArrayLength());
            foreach (JsonElement element in value.EnumerateArray())
            {
                if (!TryReadClosedFields(element, EntryFields, "entry", out JsonElement[] fields, out error)) return false;
                if (fields[0].ValueKind != JsonValueKind.String) return Fail("entry " + UniqueIdField + " is not a string", out error);
                if (fields[1].ValueKind != JsonValueKind.String || !Guid.TryParse(fields[1].GetString(), out Guid id))
                    return Fail("entry " + IdField + " is not a GUID", out error);
                if (fields[2].ValueKind != JsonValueKind.Number || !fields[2].TryGetInt32(out int style))
                    return Fail("entry " + StyleField + " is not an integer", out error);
                list.Add(new KnightIdentitySnapshotEntry(fields[0].GetString(), new KnightIdentityReceipt(id, style)));
            }
            entries = list;
            return true;
        }

        /// <summary>
        /// 读一个对象：已知字段去重收集，未知字段进入 <paramref name="extras"/>（调用方决定保留还是拒绝）。
        /// 未知字段名用 HashSet 去重（不随字段数退化），字段总数超过 <paramref name="maxFields"/> 即失败，
        /// 因此恶意对象不会让解析退化成 O(n²) 或吃掉无界内存。缺失字段在返回数组里是 Undefined。
        /// </summary>
        private static bool TryReadFields(JsonElement element, string[] known, string where, int maxFields,
            out JsonElement[] fields, out List<KeyValuePair<string, JsonElement>> extras, out string error)
        {
            fields = null;
            extras = null;
            error = null;
            if (element.ValueKind != JsonValueKind.Object) return Fail(where + " is not an object", out error);

            JsonElement[] found = new JsonElement[known.Length];
            HashSet<string> extraNames = null;
            int seen = 0;
            int count = 0;
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (++count > maxFields) return Fail(where + " has more than " + maxFields + " fields", out error);
                int index = -1;
                for (int i = 0; i < known.Length; i++)
                {
                    if (string.Equals(known[i], property.Name, StringComparison.Ordinal)) { index = i; break; }
                }
                if (index < 0)
                {
                    if (extraNames == null)
                    {
                        extraNames = new HashSet<string>(StringComparer.Ordinal);
                        extras = new List<KeyValuePair<string, JsonElement>>();
                    }
                    if (!extraNames.Add(property.Name)) return Fail(where + " has duplicate field '" + property.Name + "'", out error);
                    extras.Add(new KeyValuePair<string, JsonElement>(property.Name, property.Value.Clone()));
                    continue;
                }
                int bit = 1 << index;
                if ((seen & bit) != 0) return Fail(where + " has duplicate field '" + property.Name + "'", out error);
                seen |= bit;
                found[index] = property.Value;
            }
            fields = found;
            return true;
        }

        /// <summary>封闭 schema 的对象读取：出现任何未知字段即 Corrupt（最多容忍 1 个以便报出字段名）。</summary>
        private static bool TryReadClosedFields(JsonElement element, string[] known, string where, out JsonElement[] fields, out string error)
        {
            if (!TryReadFields(element, known, where, known.Length + 1, out fields, out List<KeyValuePair<string, JsonElement>> extras, out error)) return false;
            return extras == null || Fail(where + " has unknown field '" + extras[0].Key + "'", out error);
        }

        private static bool Fail(string message, out string error)
        {
            error = message;
            return false;
        }

        private sealed class ScopeNode
        {
            /// <summary>新 → 旧。</summary>
            internal readonly List<KnightIdentitySnapshot> Snapshots;

            internal ScopeNode(List<KnightIdentitySnapshot> snapshots)
            {
                Snapshots = snapshots;
            }
        }
    }

    /// <summary>原子落盘/读取。只有显式调用才 I/O；原生存档一概不碰。</summary>
    internal static class KnightIdentityArchiveStore
    {
        internal const long MaxFileBytes = 16L * 1024 * 1024;
        internal const int MaxJsonDepth = 32;

        internal enum SaveStatus
        {
            Created,
            Replaced,
            Unchanged,
            RefusedUnknownVersion,
            RefusedCorruptMain,
            Failed,
        }

        internal sealed class LoadResult
        {
            internal readonly KnightIdentityArchiveStatus Status;
            internal readonly KnightIdentityArchive Archive;
            internal readonly bool RecoveredBackup;
            internal readonly string Detail;

            internal LoadResult(KnightIdentityArchiveStatus status, KnightIdentityArchive archive, bool recoveredBackup, string detail)
            {
                Status = status;
                Archive = archive;
                RecoveredBackup = recoveredBackup;
                Detail = detail;
            }

            internal bool IsUsable { get { return Status == KnightIdentityArchiveStatus.Valid && Archive != null; } }
        }

        internal sealed class SaveResult
        {
            internal readonly SaveStatus Status;
            internal readonly string Detail;

            /// <summary>是否发生过 IO 失败后的重试（结果也可能是成功；供调用方落一次性诊断日志）。</summary>
            internal readonly bool Retried;

            internal SaveResult(SaveStatus status, string detail, bool retried = false)
            {
                Status = status;
                Detail = detail;
                Retried = retried;
            }

            internal bool Ok
            {
                get { return Status != SaveStatus.Failed && Status != SaveStatus.RefusedUnknownVersion && Status != SaveStatus.RefusedCorruptMain; }
            }
        }

        internal static string BackupPath(string path) { return path + ".bak"; }

        /// <summary>主文件优先；主文件损坏时读有效备份并置 RecoveredBackup。未知主版本不降级读备份。</summary>
        internal static LoadResult Load(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("path is required", nameof(path));

            KnightIdentityArchiveStatus mainStatus = Inspect(path, out KnightIdentityArchive mainArchive, out _, out string mainDetail);
            if (mainStatus == KnightIdentityArchiveStatus.Valid) return new LoadResult(mainStatus, mainArchive, false, null);
            if (mainStatus == KnightIdentityArchiveStatus.UnsupportedVersion) return new LoadResult(mainStatus, null, false, mainDetail);

            KnightIdentityArchiveStatus backupStatus = Inspect(BackupPath(path), out KnightIdentityArchive backupArchive, out _, out string backupDetail);
            if (backupStatus == KnightIdentityArchiveStatus.Valid) return new LoadResult(KnightIdentityArchiveStatus.Valid, backupArchive, true, "main: " + mainDetail);
            if (mainStatus == KnightIdentityArchiveStatus.Missing && backupStatus == KnightIdentityArchiveStatus.Missing)
                return new LoadResult(KnightIdentityArchiveStatus.Missing, null, false, mainDetail);
            if (backupStatus == KnightIdentityArchiveStatus.UnsupportedVersion)
                return new LoadResult(KnightIdentityArchiveStatus.UnsupportedVersion, null, false, backupDetail);
            return new LoadResult(KnightIdentityArchiveStatus.Corrupt, null, false, "main: " + mainDetail + "; backup: " + backupDetail);
        }

        /// <summary>
        /// 原子保存：同目录 temp → 完整写 + Flush(true) → File.Replace（保留上一份有效 bak）或 Move。
        /// 拒绝写的情形（一律不产生任何字节变化）：序列化结果超 16 MiB；主文件版本未知；**备份版本未知**
        /// （覆盖/遮蔽它都会毁掉更高版本的数据）；主文件损坏（只能由显式 RecoverMainFromBackup 修复）。
        /// 主文件缺失 → 只新建主文件，绝不碰已有备份。内容与现文件逐字节相同 → Unchanged 不写盘。
        /// </summary>
        internal static SaveResult Save(string path, KnightIdentityArchive archive)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("path is required", nameof(path));
            if (archive == null) throw new ArgumentNullException(nameof(archive));

            byte[] bytes = archive.SerializeToUtf8();
            // v1未知字段可能与v2新增contexts重名，或升级后超过字段上限。拒写而不丢失未知数据。
            if (KnightIdentityArchive.Parse(bytes, out _, out string serializedError) != KnightIdentityArchiveStatus.Valid)
                return new SaveResult(SaveStatus.Failed, "serialized archive is not readable: " + serializedError);
            if (bytes.Length > MaxFileBytes)
            {
                return new SaveResult(SaveStatus.Failed,
                    "serialized archive is " + bytes.Length.ToString(CultureInfo.InvariantCulture) + " bytes, above " + MaxFileBytes.ToString(CultureInfo.InvariantCulture));
            }

            string backupPath = BackupPath(path);
            KnightIdentityArchiveStatus mainStatus = Inspect(path, out _, out byte[] mainBytes, out string mainDetail);
            if (mainStatus == KnightIdentityArchiveStatus.UnsupportedVersion) return new SaveResult(SaveStatus.RefusedUnknownVersion, mainDetail);

            KnightIdentityArchiveStatus backupStatus = Inspect(backupPath, out _, out _, out string backupDetail);
            if (backupStatus == KnightIdentityArchiveStatus.UnsupportedVersion)
                return new SaveResult(SaveStatus.RefusedUnknownVersion, backupDetail);

            if (mainStatus == KnightIdentityArchiveStatus.Corrupt)
                return new SaveResult(SaveStatus.RefusedCorruptMain, mainDetail + (backupStatus == KnightIdentityArchiveStatus.Valid ? "; valid backup kept at " + backupPath : null));
            if (mainStatus == KnightIdentityArchiveStatus.Valid && mainBytes != null && mainBytes.AsSpan().SequenceEqual(bytes))
                return new SaveResult(SaveStatus.Unchanged, null);

            bool keepBackup = mainStatus == KnightIdentityArchiveStatus.Valid;
            SaveResult written = WriteAtomically(path, bytes, keepBackup ? backupPath : null, RetryPolicy.Save, null);
            if (written.Status == SaveStatus.Created && mainStatus != KnightIdentityArchiveStatus.Missing)
                written = new SaveResult(SaveStatus.Created, mainDetail, written.Retried);
            return written;
        }

        /// <summary>
        /// 显式修复：主文件损坏而备份有效时用备份内容原子重建主文件（备份本身不动）。Load 从不自动调用它。
        /// </summary>
        internal static SaveResult RecoverMainFromBackup(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("path is required", nameof(path));

            KnightIdentityArchiveStatus mainStatus = Inspect(path, out _, out _, out string mainDetail);
            if (mainStatus == KnightIdentityArchiveStatus.Valid) return new SaveResult(SaveStatus.Unchanged, "main is valid");
            if (mainStatus == KnightIdentityArchiveStatus.UnsupportedVersion) return new SaveResult(SaveStatus.RefusedUnknownVersion, mainDetail);
            if (!File.Exists(path)) return new SaveResult(SaveStatus.Unchanged, "main is missing; Save creates it from the loaded archive");

            if (Inspect(BackupPath(path), out _, out byte[] backupBytes, out string backupDetail) != KnightIdentityArchiveStatus.Valid)
                return new SaveResult(SaveStatus.Failed, "backup is not usable: " + backupDetail);
            return WriteAtomically(path, backupBytes, null, RetryPolicy.RecoverFromBackup, backupBytes);
        }

        /// <summary>写失败（IO 类）后的一次重试延迟：真实时钟，只重试一次。</summary>
        internal const int RetryDelayMs = 250;

        /// <summary>
        /// retry 前复核的调用方前提（不是跨进程 CAS：只保证等待窗口内不覆盖新出现的受保护文件、不写入陈旧来源）。
        /// </summary>
        private enum RetryPolicy
        {
            /// <summary>
            /// Save：拒绝未知 schema 主/备文件与损坏主文件。受检备份恒为 <see cref="BackupPath"/>（与本次是否轮换
            /// 备份无关）：主文件缺失时的新建同样不得遮蔽等待窗口内出现的更高版本备份。
            /// </summary>
            Save,

            /// <summary>
            /// RecoverMainFromBackup：主文件未知 schema、或已不再需要修复（有效/缺失）→ 拒绝；来源备份必须仍
            /// Valid 且与本次捕获字节逐字节一致，否则拒绝——绝不把陈旧来源写成低版本主档去遮蔽更高版本数据。
            /// 主文件损坏本身不是拒绝条件（修复目标）。
            /// </summary>
            RecoverFromBackup,
        }

        /// <summary>
        /// 同目录 temp → 完整写 + Flush(true) → File.Replace（可选保留 bak）或 Move；失败保持旧文件。
        /// IO 类失败在真实时钟延迟后重试一次，重试前按 <paramref name="policy"/> 重新 Inspect 复核调用方前提：
        /// 未知 schema / 损坏主文件 / 不再可修的恢复前提 / 变化的来源备份都不会被这次的陈旧 bytes 覆盖。
        /// 保护性拒绝同样带 Retried 标记，由调用方落日志。
        /// </summary>
        private static SaveResult WriteAtomically(string path, byte[] bytes, string backupPath, RetryPolicy policy, byte[] sourceBytes)
        {
            SaveResult first = WriteAtomicallyOnce(path, bytes, backupPath, out bool retryable);
            if (first.Status != SaveStatus.Failed || !retryable) return first; // 保护性拒写在 Save/Recover 层，不会到这里
            SleepRetryDelay();
            SaveResult refused = CheckRetryProtection(path, policy, sourceBytes);
            if (refused != null) return new SaveResult(refused.Status, refused.Detail, retried: true);
            SaveResult second = WriteAtomicallyOnce(path, bytes, backupPath, out _);
            return new SaveResult(second.Status, second.Detail, retried: true);
        }

        /// <summary>retry 前的调用方前提复核；前提仍成立返回 null。</summary>
        private static SaveResult CheckRetryProtection(string path, RetryPolicy policy, byte[] sourceBytes)
        {
            string inspectedBackup = BackupPath(path); // 受检备份与「本次是否轮换备份」的参数分离
            KnightIdentityArchiveStatus mainStatus = Inspect(path, out _, out _, out string mainDetail);
            KnightIdentityArchiveStatus backupStatus = Inspect(inspectedBackup, out _, out byte[] backupBytes, out string backupDetail);

            if (mainStatus == KnightIdentityArchiveStatus.UnsupportedVersion)
                return new SaveResult(SaveStatus.RefusedUnknownVersion, "retry re-check: " + mainDetail);
            if (backupStatus == KnightIdentityArchiveStatus.UnsupportedVersion)
                return new SaveResult(SaveStatus.RefusedUnknownVersion, "retry re-check: " + backupDetail);

            if (policy == RetryPolicy.Save)
            {
                if (mainStatus == KnightIdentityArchiveStatus.Corrupt)
                    return new SaveResult(SaveStatus.RefusedCorruptMain, "retry re-check: " + mainDetail);
                return null;
            }

            // RecoverFromBackup：主文件必须仍是「存在且损坏」的修复目标，来源必须仍是这次捕获的那一份。
            if (mainStatus == KnightIdentityArchiveStatus.Valid || mainStatus == KnightIdentityArchiveStatus.Missing)
                return new SaveResult(SaveStatus.Failed, "retry re-check: main no longer needs a repair (" + mainStatus + ")");
            if (backupStatus != KnightIdentityArchiveStatus.Valid)
                return new SaveResult(SaveStatus.Failed, "retry re-check: source backup is not usable: " + backupDetail);
            if (sourceBytes == null || backupBytes == null || !backupBytes.AsSpan().SequenceEqual(sourceBytes))
                return new SaveResult(SaveStatus.Failed, "retry re-check: source backup changed during the wait");
            return null;
        }

        private static void SleepRetryDelay()
        {
            try { Thread.Sleep(RetryDelayMs); } catch (ThreadInterruptedException) { }
        }

        /// <summary>单次原子写；ioFailure 只在捕获 IO 异常时为真（用于决定是否重试）。</summary>
        private static SaveResult WriteAtomicallyOnce(string path, byte[] bytes, string backupPath, out bool ioFailure)
        {
            ioFailure = false;
            string tempPath = null;
            try
            {
                // GetFullPath 会抛（非法路径/超长路径），因此放在异常边界内一起降级成 Failed。
                string directory = Path.GetDirectoryName(Path.GetFullPath(path));
                if (string.IsNullOrEmpty(directory)) return new SaveResult(SaveStatus.Failed, "path has no directory");
                tempPath = Path.Combine(directory, Path.GetFileName(path) + ".tmp-" + Guid.NewGuid().ToString("N"));

                Directory.CreateDirectory(directory);
                using (FileStream stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(path))
                {
                    File.Replace(tempPath, path, backupPath, true);
                    return new SaveResult(SaveStatus.Replaced, null);
                }
                File.Move(tempPath, path);
                return new SaveResult(SaveStatus.Created, null);
            }
            catch (Exception e) when (IsIoFailure(e))
            {
                ioFailure = true;
                return new SaveResult(SaveStatus.Failed, e.GetType().Name + ": " + e.Message);
            }
            finally
            {
                // 只清自己那一个 exact temp；绝不扫描或删除目录内其他文件。
                if (tempPath != null)
                {
                    try
                    {
                        if (File.Exists(tempPath)) File.Delete(tempPath);
                    }
                    catch (Exception e) when (IsIoFailure(e))
                    {
                    }
                }
            }
        }

        /// <summary>
        /// 单个 FileStream 上一次性定长读取：不再 FileInfo 后再 ReadAllBytes，读取上限是「实际读到的字节」。
        /// 文件在读过程中变短 → Corrupt（绝不冒充 Missing）；不存在 → Missing；其余 I/O 失败 → Corrupt。
        /// </summary>
        private static KnightIdentityArchiveStatus Inspect(string path, out KnightIdentityArchive archive, out byte[] raw, out string detail)
        {
            archive = null;
            raw = null;
            detail = null;
            try
            {
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.SequentialScan))
                {
                    long length = stream.Length;
                    if (length == 0) { detail = path + " is empty"; return KnightIdentityArchiveStatus.Corrupt; }
                    if (length > MaxFileBytes)
                    {
                        detail = path + " exceeds " + MaxFileBytes.ToString(CultureInfo.InvariantCulture) + " bytes";
                        return KnightIdentityArchiveStatus.Corrupt;
                    }

                    byte[] bytes = new byte[(int)length];
                    int read = 0;
                    while (read < bytes.Length)
                    {
                        int chunk = stream.Read(bytes, read, bytes.Length - read);
                        if (chunk <= 0) break;
                        read += chunk;
                    }
                    if (read != bytes.Length || stream.ReadByte() != -1 || stream.Length != bytes.Length)
                    {
                        detail = path + " shrank while being read (" + read + " of " + bytes.Length + " bytes)";
                        return KnightIdentityArchiveStatus.Corrupt;
                    }

                    KnightIdentityArchiveStatus status = KnightIdentityArchive.Parse(bytes, out archive, out string error);
                    detail = status == KnightIdentityArchiveStatus.Valid ? null : path + ": " + error;
                    if (status == KnightIdentityArchiveStatus.Valid) raw = bytes;
                    return status;
                }
            }
            catch (FileNotFoundException)
            {
                detail = path + " is missing";
                return KnightIdentityArchiveStatus.Missing;
            }
            catch (DirectoryNotFoundException)
            {
                detail = path + " is missing";
                return KnightIdentityArchiveStatus.Missing;
            }
            catch (Exception e) when (IsIoFailure(e))
            {
                detail = path + " is unreadable: " + e.GetType().Name + ": " + e.Message;
                return KnightIdentityArchiveStatus.Corrupt;
            }
        }

        private static bool IsIoFailure(Exception e)
        {
            return e is IOException
                || e is UnauthorizedAccessException
                || e is NotSupportedException
                || e is ArgumentException
                || e is System.Security.SecurityException;
        }
    }

    /// <summary>指纹纯函数：SHA256(长度前缀(context) ‖ 长度前缀(rawSnapshot))，小写 64 hex。</summary>
    internal static class KnightIdentityFingerprint
    {
        internal const int HexLength = 64;

        /// <summary>hash kind 1：legacy 全量岛 JSON（含全部顶层时钟）。</summary>
        internal const int KindLegacy = 1;

        /// <summary>hash kind 2：排除 3 个已实测时钟的时钟无关指纹（与 HeroRecruitmentFingerprint 同一实测结论）。</summary>
        internal const int KindNormalized = 2;

        internal static bool IsValidKind(int kind) { return kind == KindLegacy || kind == KindNormalized; }

        /// <summary>kind2 指纹的域分隔前缀：保证 kind1/kind2 的盐空间可分。</summary>
        private const string NormalizedContextPrefix = "knight-normalized-v2\n";

        internal static string Sha256(string rawSnapshot, string context)
        {
            if (rawSnapshot == null) throw new ArgumentNullException(nameof(rawSnapshot));
            if (context == null) throw new ArgumentNullException(nameof(context));
            return Sha256Bytes(Encoding.UTF8.GetBytes(rawSnapshot), context);
        }

        /// <summary>已编码 payload 的 legacy 指纹（迁移扫描每个 scope 复用同一份字节，避免逐 scope 复制）。</summary>
        internal static string Sha256Bytes(byte[] snapshotBytes, string context)
        {
            if (snapshotBytes == null) throw new ArgumentNullException(nameof(snapshotBytes));
            if (context == null) throw new ArgumentNullException(nameof(context));
            return Sha256Core(Encoding.UTF8.GetBytes(context), snapshotBytes);
        }

        /// <summary>
        /// 时钟无关指纹：只剔除 playTimeDays / lastPlayedTimeDays / _islandTimePlayed 三个顶层时钟
        /// （实测：这三个活时钟可能在 IslandSaveData.Save 与 global 落盘之间前进；其余字段全部参与）。
        /// 非法 JSON（非对象 / 重复顶层字段）抛异常，调用方自行降级。
        /// </summary>
        internal static string Normalized(string rawSnapshot, string context)
        {
            if (rawSnapshot == null) throw new ArgumentNullException(nameof(rawSnapshot));
            if (context == null) throw new ArgumentNullException(nameof(context));
            return NormalizedFromPayload(NormalizedPayload(rawSnapshot), context);
        }

        /// <summary>过滤后的 payload 字节（每个 rawJson 只解析/重写一次，供多 scope 复用）。</summary>
        internal static byte[] NormalizedPayload(string rawSnapshot)
        {
            if (rawSnapshot == null) throw new ArgumentNullException(nameof(rawSnapshot));
            using (JsonDocument document = JsonDocument.Parse(rawSnapshot))
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object) throw new FormatException("island object required");
                using (MemoryStream stream = new MemoryStream())
                {
                    HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
                    using (Utf8JsonWriter writer = new Utf8JsonWriter(stream))
                    {
                        writer.WriteStartObject();
                        foreach (JsonProperty property in document.RootElement.EnumerateObject())
                        {
                            if (!names.Add(property.Name)) throw new FormatException("duplicate island property");
                            if (property.Name is "playTimeDays" or "lastPlayedTimeDays" or "_islandTimePlayed") continue;
                            property.WriteTo(writer);
                        }
                        writer.WriteEndObject();
                    }
                    return stream.ToArray();
                }
            }
        }

        /// <summary>已过滤 payload 的时钟无关指纹（迁移扫描每个 scope 复用，不重复解析 JSON）。</summary>
        internal static string NormalizedFromPayload(byte[] normalizedPayload, string context)
        {
            if (normalizedPayload == null) throw new ArgumentNullException(nameof(normalizedPayload));
            if (context == null) throw new ArgumentNullException(nameof(context));
            return Sha256Core(Encoding.UTF8.GetBytes(NormalizedContextPrefix + context), normalizedPayload);
        }

        private static string Sha256Core(byte[] contextBytes, byte[] snapshotBytes)
        {
            using (IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                Span<byte> length = stackalloc byte[8];
                WriteUInt64(length, (ulong)contextBytes.Length);
                hash.AppendData(length);
                hash.AppendData(contextBytes);
                WriteUInt64(length, (ulong)snapshotBytes.Length);
                hash.AppendData(length);
                hash.AppendData(snapshotBytes);
                return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            }
        }

        internal static bool IsHex64(string value) { return NormalizeHex64(value) != null; }

        /// <summary>64 位十六进制归一化为小写；不合法返回 null。</summary>
        internal static string NormalizeHex64(string value)
        {
            if (value == null || value.Length != HexLength) return null;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) return null;
            }
            return value.ToLowerInvariant();
        }

        private static void WriteUInt64(Span<byte> destination, ulong value)
        {
            for (int i = 0; i < 8; i++) destination[i] = (byte)(value >> (i * 8));
        }
    }

    /// <summary>
    /// 新招募的风格分配：只在 available 内取 counts 最少的风格，同数用调用方私有 entropy 确定性 tie-break。
    /// 不改输入、不用 Unity random。已有 GUID/风格与当前数量、资源缺失无关，永不被本函数改动。
    /// </summary>
    internal static class KnightIdentityBalance
    {
        /// <summary>
        /// 返回必属于 available 的风格码。非法输入抛 ArgumentException：
        /// counts 为 null / 长度非 5 / 含负数；available 为 null / 空 / 含越界码或重复码。
        /// </summary>
        internal static int ChooseLeast(int[] counts, IReadOnlyList<int> available, uint entropy)
        {
            if (counts == null) throw new ArgumentNullException(nameof(counts));
            if (counts.Length != KnightIdentityReceipt.StyleCount)
                throw new ArgumentException("counts must have " + KnightIdentityReceipt.StyleCount + " entries", nameof(counts));
            if (available == null) throw new ArgumentNullException(nameof(available));
            if (available.Count == 0) throw new ArgumentException("available must not be empty", nameof(available));
            for (int i = 0; i < counts.Length; i++)
            {
                if (counts[i] < 0) throw new ArgumentException("counts must be non-negative", nameof(counts));
            }

            int bestCount = int.MaxValue;
            int ties = 0;
            for (int i = 0; i < available.Count; i++)
            {
                int style = available[i];
                if (!KnightIdentityReceipt.IsValidStyle(style))
                    throw new ArgumentException("available contains style outside 0.." + KnightIdentityReceipt.MaxStyle, nameof(available));
                for (int j = 0; j < i; j++)
                {
                    if (available[j] == style) throw new ArgumentException("available contains duplicate style " + style.ToString(CultureInfo.InvariantCulture), nameof(available));
                }
                int count = counts[style];
                if (count < bestCount) { bestCount = count; ties = 1; }
                else if (count == bestCount) { ties++; }
            }

            // 并列只在“最少”集合里按 entropy 取第 N 个：一次取模选一个下标，整周期频次完全均匀。
            int pick = (int)(entropy % (uint)ties);
            for (int i = 0; i < available.Count; i++)
            {
                if (counts[available[i]] != bestCount) continue;
                if (pick == 0) return available[i];
                pick--;
            }
            return available[0];
        }
    }
}
