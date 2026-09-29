// 骑士历史职业配额恢复（knight-load-roundtrip-20260929）：完全卸载 MOD 玩过原版、无法逐人对应的旧档，
// 在「明确 known-mismatch + 有效历史来源 + host + 成功外层加载」时，按当前存活骑士人数 N 与历史职业
// 数量 H 重新分配特色职业配额——绝不新增骑士、不复活、不收费、不冒充逐人找回（新收据 = 新绑定 GUID）。
//
// 契约（与 KnightIdentityContext/Archive/LoadSeed/Runtime 严格配套）：
//  * 触发门全部满足才行动：本会话外层 load 以 known-mismatch 结束（精确恢复不可能）、sidecar 主档
//    Valid（不是 Missing，也不是从备份恢复出来的）、同 context 存在可证明的历史快照、host authority、
//    当前 world/对象/lifetime 完整（cohort 捕获期已复核）。损坏/只读/未知版本/新生成世界一律不做。
//  * 历史来源在 load 期冻结（cohort 建立时即以已读入的 archive 选定），之后不重新读盘、不漂移。
//    选择规则：同 context 全部 epoch 快照中取最高 revision，再取最新 SavedAtUtc；同优先级内容不一致
//    报冲突（不做任意选择）。最新快照为空 = 当时没有骑士：不做配额恢复（不回溯旧非空快照复活）。
//  * 人数：N == H 时精确保留各职业数量；不同时用 N*历史数量/H 的整数部分，再按余数从大到小补齐，
//    平局按 style ID。合计恒 = N，绝不发放 N 以外的数量。
//  * cohort 只含本次加载冻结且现场复核过的真实 tagKnight（明确 tagSquire 排除）；任何一名已有收据
//    （手动/其它来源）→ 整批取消，绝不覆盖、绝不混入第二套身份来源。面板 pending 存在时用户动作优先。
//  * 成功 = 只写运行时收据 + 把绑定改为所选历史 epoch 的 resolved 状态（ConfirmContext(false)）并挂
//    「下一次真实 Save 写新 revision」pending；恢复期零 I/O、不伪造面板日志。未保存退出 = 零持久化，
//    下次加载按同一历史来源确定性重算同样数量。
//  * 绝不新造 epoch；写入沿用所选历史 epoch（既有原子写/并发校验），revision 由 save 路径在落盘时
//    取「该 context 最大修订号 + 1」，保证新记录在后续 load 的冲突判定中严格胜出。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace KingdomEnhancedMod
{
    /// <summary>配额 cohort 的取用结果。</summary>
    internal enum KnightIdentityQuotaCohortStatus
    {
        None,     // 没有配额批次（非 known-mismatch 会话）
        Waiting,  // 批次在管，但 owner 现场尚未就绪（inactive/层级未补齐）：下次巡检重试
        Ready,    // cohort 完整且已整批 live 复核：由配额恢复消费
        Cancelled // 批次已丢弃（原因见 reason）：不再重试
    }

    /// <summary>一批 live 复核过的配额 cohort（冻结历史来源 + 有序 uniqueID/live owner）。</summary>
    internal sealed class KnightIdentityQuotaCohort
    {
        internal string ContextKey;
        internal string Epoch;             // 所选历史 epoch（写新 revision 的目标；绝不新造）
        internal int[] HistoricalCounts;   // 历史快照各 style 数量（长度 StyleCount，合计 = H）
        internal List<string> UniqueIds;   // uniqueID 升序（确定性派发顺序）
        internal List<Knight> Knights;     // 与 UniqueIds 同序
        internal List<long> Lifetimes;     // 与 UniqueIds 同序
        internal int ExcludedCount;        // 明确 tagSquire 的已知排除数
    }

    internal static class KnightIdentityQuotaRecovery
    {
        internal const string KindHistoricalQuota = "historical-quota";

        /// <summary>
        /// 纯策略：N 人按历史数量 h（长度 StyleCount）分配。N == H 精确复制；否则最大余数法，
        /// 平局按 style ID 升序。H == 0 或 N &lt;= 0 返回 null（不做配额恢复）。
        /// </summary>
        internal static int[] Allocate(int knights, IReadOnlyList<int> historicalCounts)
        {
            if (historicalCounts == null || historicalCounts.Count != KnightIdentityReceipt.StyleCount) return null;
            if (knights <= 0) return null;

            long total = 0;
            for (int i = 0; i < historicalCounts.Count; i++)
            {
                if (historicalCounts[i] < 0) return null;
                total += historicalCounts[i];
            }
            if (total == 0) return null;

            int[] quota = new int[KnightIdentityReceipt.StyleCount];
            if (total == knights)
            {
                for (int i = 0; i < historicalCounts.Count; i++) quota[i] = historicalCounts[i];
                return quota;
            }

            long[] remainders = new long[KnightIdentityReceipt.StyleCount];
            long assigned = 0;
            for (int i = 0; i < historicalCounts.Count; i++)
            {
                long scaled = (long)knights * historicalCounts[i];
                quota[i] = (int)(scaled / total);
                remainders[i] = scaled % total;
                assigned += quota[i];
            }

            long remaining = knights - assigned; // 0 <= remaining < StyleCount（Σ(rᵢ)/H 性质）
            while (remaining > 0)
            {
                // 最大余数、平局取最小 style ID；剩余席位数 < StyleCount，循环有界（不设顺序补齐兜底）。
                int best = 0;
                for (int i = 1; i < KnightIdentityReceipt.StyleCount; i++)
                {
                    if (remainders[i] > remainders[best]) best = i;
                }
                quota[best]++;
                remainders[best] = 0;
                remaining--;
            }
            return quota;
        }

        /// <summary>把配额展开成逐人 style 序列（style ID 升序填充）；输入非法/合计非正返回 null。</summary>
        internal static int[] BuildPlan(IReadOnlyList<int> quotaCounts, int cohortSize)
        {
            if (quotaCounts == null || quotaCounts.Count != KnightIdentityReceipt.StyleCount) return null;
            int total = 0;
            for (int i = 0; i < quotaCounts.Count; i++)
            {
                if (quotaCounts[i] < 0) return null;
                total += quotaCounts[i];
            }
            if (total != cohortSize || total <= 0) return null;

            int[] plan = new int[cohortSize];
            int cursor = 0;
            for (int style = 0; style < quotaCounts.Count; style++)
            {
                for (int n = 0; n < quotaCounts[style]; n++) plan[cursor++] = style;
            }
            return plan;
        }

        /// <summary>
        /// 历史来源选择（纯读，I/O 不在本方法内）：**只在 context 的有效 Active epoch 内**选择
        /// （最近确认的当前存档分支，不是创建时间排序）——最高 revision → 最新 SavedAtUtc →
        /// 同优先级内容必须一致，否则无有效来源。Active 缺失/不属于 context/archive 无该 scope/
        /// 无任何受支持快照/校验错误 → 一律拒绝（不回扫旧 epoch）；只有 Active 内的有效空快照是
        /// 「当时没有骑士」的零计数来源（交显式均匀分支）。
        /// 兼容性说明：Archive 支持的旧格式（KindLegacy，如 v1 全量指纹迁移记录）与 kind2 一样是
        /// 有效来源——kind 只决定指纹算法，不决定条目有效性；绝不把受支持的旧格式当坏档。
        /// </summary>
        internal static bool TrySelectSource(KnightIdentityArchive archive, string contextKey, out string epoch,
            out int[] historicalCounts, out string reason)
        {
            epoch = null;
            historicalCounts = null;
            reason = null;
            if (archive == null || contextKey == null)
            {
                reason = "no-archive";
                return false;
            }
            if (!archive.TryGetContext(contextKey, out KnightIdentityContext context) || context == null)
            {
                reason = "no-context";
                return false;
            }
            string active = context.Active;
            if (string.IsNullOrEmpty(active) || !context.Owns(active))
            {
                reason = "no-active-source"; // 无效 Active：绝不 fresh 均匀、不回扫旧 epoch
                return false;
            }
            // 不限 kind：legacy(v1) 与 kind2 都是受支持的有效 snapshot（kind 只影响指纹算法）。
            if (!archive.TryGetSnapshots(active, out IReadOnlyList<KnightIdentitySnapshot> snapshots) || snapshots.Count == 0)
            {
                reason = "no-active-source";
                return false;
            }

            KnightIdentitySnapshot best = null;
            int bestRevision = int.MinValue;
            DateTimeOffset bestSavedAt = DateTimeOffset.MinValue;
            bool conflict = false;

            for (int s = 0; s < snapshots.Count; s++)
            {
                KnightIdentitySnapshot snapshot = snapshots[s];
                if (snapshot == null) continue;
                if (!TryParseSavedAt(snapshot.SavedAtUtc, out DateTimeOffset savedAt))
                {
                    reason = "invalid-timestamp"; // Active 内校验错误：无有效来源
                    return false;
                }
                if (best == null || snapshot.Revision > bestRevision)
                {
                    best = snapshot;
                    bestRevision = snapshot.Revision;
                    bestSavedAt = savedAt;
                    conflict = false;
                    continue;
                }
                if (snapshot.Revision < bestRevision) continue;
                if (savedAt > bestSavedAt)
                {
                    best = snapshot;
                    bestSavedAt = savedAt;
                    conflict = false;
                    continue;
                }
                if (savedAt < bestSavedAt) continue;
                if (!best.HasSameContent(snapshot)) conflict = true; // 同 revision 同时间戳内容不一致
            }

            if (best == null)
            {
                reason = "no-active-source";
                return false;
            }
            if (conflict)
            {
                reason = "conflict";
                return false;
            }

            int[] counts = new int[KnightIdentityReceipt.StyleCount];
            IReadOnlyList<string> ids = best.OrderedUniqueIds;
            for (int i = 0; i < ids.Count; i++)
            {
                if (!best.TryGet(ids[i], out KnightIdentityReceipt receipt)) continue;
                if (!KnightIdentityReceipt.IsValidStyle(receipt.Style)) continue;
                counts[receipt.Style]++;
            }
            int total = 0;
            for (int i = 0; i < counts.Length; i++) total += counts[i];
            epoch = active;
            historicalCounts = counts;
            if (best.Count == 0 || total == 0) reason = "empty-latest"; // 有效最新空快照：显式均匀分支
            return true;
        }

        /// <summary>
        /// 显式均匀分支（empty-latest 的明确有效来源）：与既有首见策略同一纯规则（各可用 style 人数差 ≤1），
        /// 且是「cohort uniqueID 升序 + 可用池」的确定性纯函数（平局取最小 style ID），保证未保存退出后重放同一结果；
        /// 池不可用/为空/重复 → null（绝不发放不可用风格）。
        /// </summary>
        internal static int[] BuildUniformPlan(int cohortSize, IReadOnlyList<int> available)
        {
            if (cohortSize <= 0 || !IsUsablePool(available)) return null;
            int[] counts = new int[KnightIdentityReceipt.StyleCount];
            int[] plan = new int[cohortSize];
            for (int i = 0; i < cohortSize; i++)
            {
                int best = -1;
                for (int p = 0; p < available.Count; p++)
                {
                    int style = available[p];
                    if (best < 0) { best = style; continue; }
                    if (counts[style] < counts[best] || (counts[style] == counts[best] && style < best)) best = style;
                }
                plan[i] = best;
                counts[best]++;
            }
            return plan;
        }

        /// <summary>可用风格池预检（与首见批次同约束）：非空、全合法、无重复。</summary>
        internal static bool IsUsablePool(IReadOnlyList<int> available)
        {
            if (available == null || available.Count == 0) return false;
            for (int i = 0; i < available.Count; i++)
            {
                if (!KnightIdentityReceipt.IsValidStyle(available[i])) return false;
                for (int j = 0; j < i; j++) if (available[j] == available[i]) return false;
            }
            return true;
        }

        private static void Accept(ref KnightIdentitySnapshot best, ref string bestEpoch, ref int bestRevision,
            ref DateTimeOffset bestSavedAt, ref bool conflict, KnightIdentitySnapshot snapshot, string epoch)
        {
            // 切换来源时旧并列冲突判定作废（只对最终优先级集合判一致）
            best = snapshot;
            bestEpoch = epoch;
            bestRevision = snapshot.Revision;
            TryParseSavedAt(snapshot.SavedAtUtc, out bestSavedAt);
            conflict = false;
        }

        private static bool TryParseSavedAt(string value, out DateTimeOffset savedAt)
        {
            savedAt = DateTimeOffset.MinValue;
            return !string.IsNullOrEmpty(value)
                && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out savedAt);
        }

        /// <summary>
        /// 既有 5s IntegrityPass 调用：取一批整批 live 复核过的 cohort 并执行恢复（每会话至多一次）。
        /// 未就绪保持等待；用户 pending/部分已有收据/状态不符一律整批取消并记录一次原因。
        /// <paramref name="availableStyles"/> 是当前可用风格池（empty-latest 的显式均匀分支必须复用它，
        /// 资产缺失时绝不赋不可用风格而冒充原首见规则）。
        /// </summary>
        internal static void IntegrityPass(IReadOnlyList<int> availableStyles)
        {
            try
            {
                if (!KnightIdentityRuntime.IsHostAuthority()) return;
                KnightIdentityLoadSeed.TryTakeQuotaCohort(out KnightIdentityQuotaCohort cohort, out KnightIdentityQuotaCohortStatus status, out string reason);
                if (status == KnightIdentityQuotaCohortStatus.None || status == KnightIdentityQuotaCohortStatus.Waiting) return;
                if (status == KnightIdentityQuotaCohortStatus.Cancelled)
                {
                    KnightIdentityLog.Once("historical-quota-cancelled:" + (reason ?? "unknown"), null);
                    return;
                }
                Apply(cohort, availableStyles);
            }
            catch (Exception e)
            {
                KnightIdentityLog.Once("historical-quota", e);
            }
        }

        private static void Apply(KnightIdentityQuotaCohort cohort, IReadOnlyList<int> availableStyles)
        {
            if (cohort == null) return;
            if (KnightIdentityRuntime.InLoadContextNow)
            {
                KnightIdentityLog.Once("historical-quota-cancelled:load-context", null);
                return;
            }
            if (KnightIdentityRuntime.PanelRebaselineArmed || KnightIdentityRuntime.PanelRevisionArmed)
            {
                // 用户动作优先：手动应用/修订 pending 存在时绝不自动改职业。
                KnightIdentityLog.Once("historical-quota-cancelled:panel-pending", null);
                return;
            }
            if (!KnightIdentityContexts.TryGetBindingKind(cohort.ContextKey, out string kind)
                || !string.Equals(kind, "known-mismatch", StringComparison.Ordinal))
            {
                KnightIdentityLog.Once("historical-quota-cancelled:context-changed", null);
                return;
            }

            int knights = cohort.Knights.Count;
            int[] history = cohort.HistoricalCounts;
            bool fresh = true;
            for (int i = 0; i < history.Length; i++) if (history[i] > 0) { fresh = false; break; }

            int[] plan;
            string bindingKind;
            if (fresh)
            {
                // empty-latest：明确的「当时没有骑士」来源 → 原首见均匀策略（同纯规则、确定性、只用可用池）。
                plan = BuildUniformPlan(knights, availableStyles);
                if (plan == null)
                {
                    KnightIdentityLog.Once("historical-quota-cancelled:no-style-pool", null);
                    return;
                }
                bindingKind = "fresh-uniform";
            }
            else
            {
                int[] quota = Allocate(knights, history);
                plan = quota != null ? BuildPlan(quota, knights) : null;
                if (plan == null)
                {
                    KnightIdentityLog.Once("historical-quota-cancelled:no-quota", null);
                    return;
                }
                bindingKind = KindHistoricalQuota;
            }

            if (!KnightIdentityRuntime.TryApplyQuotaReceipts(cohort.Knights, cohort.Lifetimes, plan, out int applied) || applied != knights)
            {
                KnightIdentityLog.Once("historical-quota-cancelled:assign-failed", null);
                return;
            }

            // 成功：绑定改为所选有效来源 epoch 的 resolved 状态 + 挂一次性写 pending（恢复期零 I/O）。
            KnightIdentityContexts.RememberBinding(cohort.ContextKey, cohort.Epoch, false, false, bindingKind);
            KnightIdentityRuntime.ConfirmContext(false);
            KnightIdentityRuntime.ArmQuotaRevision(cohort.ContextKey, cohort.Epoch);
            KnightIdentityLog.Receipt((fresh ? "fresh-uniform: empty-latest" : "historical-quota:")
                + " knights=" + knights.ToString(CultureInfo.InvariantCulture)
                + " excluded=" + cohort.ExcludedCount.ToString(CultureInfo.InvariantCulture)
                + (fresh ? string.Empty : " old=" + Summary(history))
                + " new=" + Summary(CountStyles(plan))
                + " context=" + ShortHash(cohort.ContextKey));
        }

        private static int[] CountStyles(IReadOnlyList<int> plan)
        {
            int[] counts = new int[KnightIdentityReceipt.StyleCount];
            for (int i = 0; i < plan.Count; i++)
            {
                if (KnightIdentityReceipt.IsValidStyle(plan[i])) counts[plan[i]]++;
            }
            return counts;
        }

        private static string Summary(IReadOnlyList<int> counts)
        {
            StringBuilder builder = new StringBuilder(16);
            for (int i = 0; i < counts.Count; i++)
            {
                if (i > 0) builder.Append('/');
                builder.Append(counts[i].ToString(CultureInfo.InvariantCulture));
            }
            return builder.ToString();
        }

        private static string ShortHash(string value)
        {
            return string.IsNullOrEmpty(value) || value.Length < 8 ? value : value.Substring(0, 8);
        }
    }
}
