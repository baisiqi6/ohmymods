// 测试替身：Runtime 边界的等价语义（冻结契约 3 个 API）。
// 语义按契约建模：只 client/当前 life/有效 GUID+style；同 life 重复幂等；
// 本 life 已有别的 GUID 一律安全拒绝（不随意覆写）。

using System;
using System.Collections.Generic;

namespace KingdomEnhancedMod
{
    internal static class KnightIdentityRuntime
    {
        private sealed class Entry
        {
            internal KnightIdentityReceipt Receipt;
            internal bool HasReceipt;
            internal long Lifetime = 1L;
        }

        private static readonly Dictionary<Knight, Entry> Entries = new Dictionary<Knight, Entry>();

        internal static int ApplyCalls;
        internal static int ApplyAccepted;
        internal static int ApplyRejected;
        internal static bool ThrowOnApply;
        internal static bool ThrowOnGet;

        /// <summary>测试注入：在应用瞬间改动对象状态（模拟“接收与应用之间换了 life”）。</summary>
        internal static Action<Knight> BeforeApply;

        /// <summary>测试注入：Runtime 侧判定拒绝（冲突 GUID / 非权威 world 等），返回 true 表示拒绝。</summary>
        internal static Func<Knight, KnightIdentityReceipt, bool> RejectApply;

        // ------------------------------------------------------------------ 测试工具（非契约）

        internal static void Reset()
        {
            Entries.Clear();
            ApplyCalls = 0;
            ApplyAccepted = 0;
            ApplyRejected = 0;
            ThrowOnApply = false;
            ThrowOnGet = false;
            BeforeApply = null;
            RejectApply = null;
        }

        internal static void Seed(Knight knight, Guid id, int style)
        {
            Entry entry = Get(knight);
            entry.Receipt = new KnightIdentityReceipt(id, style);
            entry.HasReceipt = true;
        }

        internal static void ClearReceipt(Knight knight)
        {
            Entry entry = Get(knight);
            entry.HasReceipt = false;
            entry.Receipt = default(KnightIdentityReceipt);
        }

        internal static void SetLifetime(Knight knight, long lifetime)
        {
            Get(knight).Lifetime = lifetime;
        }

        internal static long CurrentReceiptId(Knight knight, out Guid id, out int style)
        {
            Entry entry = Get(knight);
            id = entry.HasReceipt ? entry.Receipt.Id : Guid.Empty;
            style = entry.HasReceipt ? entry.Receipt.Style : -1;
            return entry.Lifetime;
        }

        // ------------------------------------------------------------------ 冻结契约

        internal static bool TryGetReceipt(Knight knight, out KnightIdentityReceipt receipt)
        {
            if (ThrowOnGet) throw new InvalidOperationException("mock runtime TryGetReceipt failure");
            receipt = default(KnightIdentityReceipt);
            if (knight == null) return false;
            if (!Entries.TryGetValue(knight, out Entry entry) || !entry.HasReceipt) return false;
            receipt = entry.Receipt;
            return true;
        }

        internal static long GetLifetime(Knight knight)
        {
            if (knight == null) return -1L;
            return Entries.TryGetValue(knight, out Entry entry) ? entry.Lifetime : 1L;
        }

        internal static bool ApplyHostReceipt(Knight knight, long expectedLocalLifetime, KnightIdentityReceipt receipt)
        {
            ApplyCalls++;
            if (ThrowOnApply) throw new InvalidOperationException("mock runtime ApplyHostReceipt failure");
            Action<Knight> hook = BeforeApply;
            if (hook != null) hook(knight);
            Func<Knight, KnightIdentityReceipt, bool> reject = RejectApply;
            if (reject != null && reject(knight, receipt))
            {
                ApplyRejected++;
                return false;
            }
            if (knight == null || !receipt.IsValid)
            {
                ApplyRejected++;
                return false;
            }

            Entry entry = Get(knight);
            if (entry.Lifetime != expectedLocalLifetime)
            {
                ApplyRejected++; // 当前 life 不符（复用了/已换代）：安全拒绝
                return false;
            }
            if (entry.HasReceipt)
            {
                if (entry.Receipt.Id == receipt.Id)
                {
                    ApplyAccepted++; // 同 life 重复：幂等
                    return true;
                }
                ApplyRejected++; // 本 life 已有别的 GUID：不覆写
                return false;
            }

            entry.Receipt = receipt;
            entry.HasReceipt = true;
            ApplyAccepted++;
            return true;
        }

        private static Entry Get(Knight knight)
        {
            if (!Entries.TryGetValue(knight, out Entry entry))
            {
                entry = new Entry();
                Entries[knight] = entry;
            }
            return entry;
        }
    }
}
