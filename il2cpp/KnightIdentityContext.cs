// 骑士稳定上下文解析：对一个（文件名 + campaign/challenge + land）稳定上下文，判定当前加载/保存的
// 原生岛 JSON 属于哪一代 epoch 作用域、可恢复哪些收据、以及本代是否允许写新快照。
//
// 契约（与 KnightIdentityArchive/KnightIdentityRuntime/LoadSeed 严格配套）：
//  * 纯函数 + 会话内只读绑定缓存；本类不做任何 I/O、不写 sidecar、不碰 Unity 类型（只吃 rawJson 字符串）。
//  * 匹配只用两种精确哈希：kind1 = 全量 JSON（legacy 快照的唯一配方），kind2 = 只剔除 3 个实测时钟的
//    时钟无关指纹；任何「按位置/名字/金币/NetID 猜」都是禁止的。
//  * 匹配范围：本上下文自己的 epoch（含 legacy 归并进来的），再加上未被任何上下文拥有的 scope；
//    已被别的上下文拥有的 scope 绝不作为迁移来源，一个 scope 也绝不迁进第二个上下文。
//  * 多个精确匹配：全部一致 → 确定性取一个（kind2 优先，其次按 scope 序数）；有任何不一致 → 若其中存在
//    「用户修订号 &gt; 0 且严格唯一最高」的匹配（面板用户动作对内容的最终裁定），以它为准（跳过一致性判定）；
//    否则 unresolved。
//  * 同 scope 同 hash 的多个修订号各算一条独立匹配（旧记录不因修订被顶掉）。
//  * unresolved（冲突 / 上下文存在但当前快照对不上 / 仍有未归属历史而全新上下文）：
//    不恢复、不建 epoch、不写种子、绝不把历史人群当新档重种；sidecar 原样保留。
//  * 真正无任何历史的新上下文才允许新建不透明 epoch 并写入（kind2 快照），由既有 Save/LoadSeed 写路径落盘。
//  * 唯一的例外放行：本上下文已有 epoch 但当前内容对不上，且该上下文所有 epoch 的历史条目为空 —— 这在
//    骑士模型里不会由生产写路径产生（快照至少 1 条），因此等价于 fail closed。

using System;
using System.Collections.Generic;
using System.Text;

namespace KingdomEnhancedMod
{
    /// <summary>一次上下文解析结果；Epoch == null 表示 unresolved（不可恢复、不可写）。</summary>
    internal sealed class KnightIdentityResolution
    {
        internal string ContextKey;
        internal string Kind = "none";       // 有界诊断标签
        internal string Epoch;               // 恢复/写入使用的 scope；null = 未证明
        internal string MatchHash;           // 精确命中的存储 hash（kind 由 MatchKind 决定）
        internal int MatchKind;              // 0 = 无精确命中
        internal bool Unresolved;            // fail closed：不恢复、不写、不重种
        internal bool NewEpoch;              // epoch 尚未被本上下文拥有（写路径需 EnsureContext 放行新 epoch）
        internal string FreshHash;           // 无精确命中且允许写入时，kind2 快照应使用的 hash
        internal KnightIdentitySnapshot Matched;                     // 精确命中快照本体
        internal Dictionary<string, KnightIdentityReceipt> Receipts; // 精确命中快照的 uniqueID → 收据
    }

    internal static class KnightIdentityContexts
    {
        private sealed class Match
        {
            internal string Scope;
            internal KnightIdentitySnapshot Snapshot;
            internal int Kind;
        }

        /// <summary>本会话的 load→save 绑定：某上下文本次加载解析出的 epoch、unresolved 状态与解析 Kind。</summary>
        private sealed class Binding
        {
            internal string Epoch;
            internal bool Unresolved;
            internal bool NewEpoch;
            internal string Kind;   // 本次装载解析的诊断标签（吸收态再基线化的触发锚点）
        }

        private static readonly Dictionary<string, Binding> Bindings = new Dictionary<string, Binding>(StringComparer.Ordinal);

        /// <summary>
        /// 解析一个稳定上下文的当前原始岛 JSON。rawJson 非法（null / 本地 JSON 异常）→ unresolved。
        /// 绝不修改 archive。
        /// </summary>
        internal static KnightIdentityResolution Resolve(KnightIdentityArchive archive, string contextKey, string rawJson)
        {
            KnightIdentityResolution result = new KnightIdentityResolution { ContextKey = contextKey };
            if (archive == null || contextKey == null || !KnightIdentityFingerprint.IsHex64(contextKey) || string.IsNullOrEmpty(rawJson))
            {
                result.Kind = "invalid";
                result.Unresolved = true;
                return result;
            }

            bool known = archive.TryGetContext(contextKey, out KnightIdentityContext context);
            List<Match> matches = Collect(archive, contextKey, context, rawJson);

            if (matches.Count > 1 && !Agree(matches))
            {
                // 用户修订优先：面板重派写出的 (hash, rev+1) 记录即用户对内容的最终裁定，跳过 Agree 冲突判定；
                // 无「严格唯一最高修订」时维持既有 conflict fail-closed。
                Match revised = HighestUniqueRevision(matches);
                if (revised == null)
                {
                    result.Kind = "conflict";
                    result.Unresolved = true;
                    return result;
                }
                Adopt(result, context, revised, "revision");
                return result;
            }
            if (matches.Count > 0)
            {
                Adopt(result, context, Pick(matches), null);
                return result;
            }

            if (!known)
            {
                if (HasUnclaimedHistory(archive))
                {
                    // 未归属的 legacy 历史可能是本岛漂移前的记录：绝不当作无历史重种。
                    result.Kind = "legacy-pending";
                    result.Unresolved = true;
                    return result;
                }
                result.Kind = "fresh";
                result.Epoch = KnightIdentityArchive.NewScope();
                result.NewEpoch = true;
                if (!TryFreshHash(result, rawJson)) return result;
                return result;
            }

            if (context.Epochs.Exists(epoch => EpochHasEntries(archive, epoch)))
            {
                // 已知上下文但这一份快照从未记录过：unresolved，LoadSeed 不得覆盖已知历史。
                result.Kind = "known-mismatch";
                result.Unresolved = true;
                return result;
            }
            result.Kind = "fresh-known";
            result.Epoch = context.Active;
            if (!TryFreshHash(result, rawJson)) return result;
            return result;
        }

        private static bool TryFreshHash(KnightIdentityResolution result, string rawJson)
        {
            try
            {
                result.FreshHash = KnightIdentityFingerprint.Normalized(rawJson, result.Epoch);
                return true;
            }
            catch (Exception)
            {
                result.Kind = "unusable-json";
                result.Epoch = null;
                result.NewEpoch = false;
                result.FreshHash = null;
                result.Unresolved = true;
                return false;
            }
        }

        // ------------------------------------------------------------------ 会话绑定（save 路径复用 load 的判定）

        /// <summary>记住本次加载的解析结果；容量满时拒绝新增（save 路径会退回独立解析）。</summary>
        internal static void RememberBinding(string contextKey, string epoch, bool unresolved, bool newEpoch, string kind = null)
        {
            if (contextKey == null) return;
            if (!Bindings.ContainsKey(contextKey) && Bindings.Count >= KnightIdentityArchive.MaxContexts) return;
            Bindings[contextKey] = new Binding { Epoch = epoch, Unresolved = unresolved, NewEpoch = newEpoch, Kind = kind };
        }

        /// <summary>本次装载解析诊断标签（如 known-mismatch）；无绑定或无标签返回 false。</summary>
        internal static bool TryGetBindingKind(string contextKey, out string kind)
        {
            kind = null;
            if (contextKey == null || !Bindings.TryGetValue(contextKey, out Binding binding) || binding == null) return false;
            kind = binding.Kind;
            return kind != null;
        }

        internal static bool TryGetBinding(string contextKey, out string epoch, out bool unresolved, out bool newEpoch)
        {
            epoch = null;
            unresolved = true;
            newEpoch = false;
            if (contextKey == null || !Bindings.TryGetValue(contextKey, out Binding binding) || binding == null) return false;
            epoch = binding.Epoch;
            unresolved = binding.Unresolved;
            newEpoch = binding.NewEpoch;
            return true;
        }

        internal static void ResetForTests()
        {
            Bindings.Clear();
        }

        // ------------------------------------------------------------------ 内部

        private static List<Match> Collect(KnightIdentityArchive archive, string contextKey, KnightIdentityContext context, string rawJson)
        {
            List<Match> result = new List<Match>();
            List<string> scopes = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            byte[] rawBytes = Encoding.UTF8.GetBytes(rawJson); // 每个 scope 复用，避免逐 scope 复制整份岛 JSON
            byte[] normalizedPayload = null;
            bool payloadBuilt = false;

            if (context != null)
            {
                for (int i = 0; i < context.Epochs.Count; i++)
                {
                    if (seen.Add(context.Epochs[i])) scopes.Add(context.Epochs[i]);
                }
            }
            string[] keys = archive.OrderedScopeKeys();
            for (int i = 0; i < keys.Length; i++)
            {
                if (archive.ScopeClaimedBy(keys[i], contextKey)) continue;
                if (seen.Add(keys[i])) scopes.Add(keys[i]);
            }

            for (int i = 0; i < scopes.Count; i++)
            {
                string scope = scopes[i];
                if (rawBytes != null && archive.ScopeHasKind(scope, KnightIdentityFingerprint.KindLegacy))
                {
                    string hash = KnightIdentityFingerprint.Sha256Bytes(rawBytes, scope);
                    AddMatches(archive, scope, hash, KnightIdentityFingerprint.KindLegacy, result);
                }
                if (archive.ScopeHasKind(scope, KnightIdentityFingerprint.KindNormalized))
                {
                    if (!payloadBuilt)
                    {
                        payloadBuilt = true;
                        try
                        {
                            normalizedPayload = KnightIdentityFingerprint.NormalizedPayload(rawJson);
                        }
                        catch (Exception)
                        {
                            normalizedPayload = null; // 不可用的 native JSON 不可能命中 kind2 快照
                        }
                    }
                    if (normalizedPayload == null) continue;
                    string hash = KnightIdentityFingerprint.NormalizedFromPayload(normalizedPayload, scope);
                    AddMatches(archive, scope, hash, KnightIdentityFingerprint.KindNormalized, result);
                }
            }
            return result;
        }

        /// <summary>
        /// 该 scope 内所有与 hash + kind 精确匹配的快照：同 hash 的每个用户修订号各成一条独立匹配
        /// （绝不因同 hash 只取一条而漏掉修订记录）。
        /// </summary>
        private static void AddMatches(KnightIdentityArchive archive, string scope, string hash, int kind, List<Match> matches)
        {
            if (!archive.TryGetSnapshots(scope, out IReadOnlyList<KnightIdentitySnapshot> snapshots)) return;
            for (int i = 0; i < snapshots.Count; i++)
            {
                KnightIdentitySnapshot snapshot = snapshots[i];
                if (snapshot.Kind != kind) continue;
                if (!string.Equals(snapshot.Hash, hash, StringComparison.Ordinal)) continue;
                matches.Add(new Match { Scope = scope, Snapshot = snapshot, Kind = kind });
            }
        }

        private static bool Agree(List<Match> matches)
        {
            KnightIdentitySnapshot first = matches[0].Snapshot;
            for (int i = 1; i < matches.Count; i++)
            {
                if (!SameEntries(first, matches[i].Snapshot)) return false;
            }
            return true;
        }

        /// <summary>把选中的匹配写进解析结果（Kind 标签默认按命中 kind；revision 优先时用调用方标签）。</summary>
        private static void Adopt(KnightIdentityResolution result, KnightIdentityContext context, Match match, string kindOverride)
        {
            result.Kind = kindOverride ?? (match.Kind == KnightIdentityFingerprint.KindNormalized ? "exact" : "legacy");
            result.Epoch = match.Scope;
            result.MatchHash = match.Snapshot.Hash;
            result.MatchKind = match.Kind;
            result.NewEpoch = context == null || !context.Owns(match.Scope);
            result.Matched = match.Snapshot;
            result.Receipts = SnapshotEntries(match.Snapshot);
        }

        /// <summary>
        /// 用户修订优先：匹配里存在「修订号 &gt; 0 且严格唯一最高」的那一条 → 返回它（跳过 Agree 冲突判定）；
        /// 否则返回 null（维持既有 conflict fail-closed）。自动记录（修订号 0）永不获得该优先权。
        /// </summary>
        private static Match HighestUniqueRevision(List<Match> matches)
        {
            int bestIndex = -1, bestRevision = 0;
            bool unique = false;
            for (int i = 0; i < matches.Count; i++)
            {
                int revision = matches[i].Snapshot.Revision;
                if (revision <= 0) continue;
                if (revision > bestRevision)
                {
                    bestRevision = revision;
                    bestIndex = i;
                    unique = true;
                }
                else if (revision == bestRevision)
                {
                    unique = false;
                }
            }
            return bestIndex < 0 || !unique ? null : matches[bestIndex];
        }

        private static bool SameEntries(KnightIdentitySnapshot left, KnightIdentitySnapshot right)
        {
            if (left.Count != right.Count) return false;
            IReadOnlyList<string> ids = left.OrderedUniqueIds;
            for (int i = 0; i < ids.Count; i++)
            {
                if (!left.TryGet(ids[i], out KnightIdentityReceipt a) || !right.TryGet(ids[i], out KnightIdentityReceipt b) || !a.Equals(b))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>确定性选择：kind2（时钟无关指纹）优先，其次按 scope 序数；绝不「最新/最先写入即胜」。</summary>
        private static Match Pick(List<Match> matches)
        {
            Match best = matches[0];
            for (int i = 1; i < matches.Count; i++)
            {
                Match candidate = matches[i];
                bool candidateAuthoritative = candidate.Kind == KnightIdentityFingerprint.KindNormalized;
                bool bestAuthoritative = best.Kind == KnightIdentityFingerprint.KindNormalized;
                if (candidateAuthoritative != bestAuthoritative)
                {
                    if (candidateAuthoritative) best = candidate;
                    continue;
                }
                if (string.CompareOrdinal(candidate.Scope, best.Scope) < 0) best = candidate;
            }
            return best;
        }

        private static Dictionary<string, KnightIdentityReceipt> SnapshotEntries(KnightIdentitySnapshot snapshot)
        {
            IReadOnlyList<string> ids = snapshot.OrderedUniqueIds;
            Dictionary<string, KnightIdentityReceipt> receipts = new Dictionary<string, KnightIdentityReceipt>(ids.Count, StringComparer.Ordinal);
            for (int i = 0; i < ids.Count; i++)
            {
                if (snapshot.TryGet(ids[i], out KnightIdentityReceipt receipt)) receipts[ids[i]] = receipt;
            }
            return receipts;
        }

        /// <summary>存在「未被任何上下文拥有且带条目的」历史 scope。空条目快照不阻塞（同 Hero 语义的保守版）。</summary>
        private static bool HasUnclaimedHistory(KnightIdentityArchive archive)
        {
            string[] keys = archive.OrderedScopeKeys();
            for (int i = 0; i < keys.Length; i++)
            {
                if (archive.ScopeClaimed(keys[i])) continue;
                if (!archive.TryGetSnapshots(keys[i], out IReadOnlyList<KnightIdentitySnapshot> snapshots)) continue;
                for (int s = 0; s < snapshots.Count; s++)
                {
                    if (snapshots[s].Count > 0) return true;
                }
            }
            return false;
        }

        private static bool EpochHasEntries(KnightIdentityArchive archive, string scopeKey)
        {
            if (!archive.TryGetSnapshots(scopeKey, out IReadOnlyList<KnightIdentitySnapshot> snapshots)) return false;
            for (int i = 0; i < snapshots.Count; i++)
            {
                if (snapshots[i].Count > 0) return true;
            }
            return false;
        }
    }
}
