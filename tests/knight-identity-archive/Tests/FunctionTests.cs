using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using KingdomEnhancedMod;

namespace KnightIdentityArchiveTests
{
    /// <summary>指纹纯函数（长度前缀编码）+ 新招募均衡分配。</summary>
    internal static class FunctionTests
    {
        internal static void Run()
        {
            Console.WriteLine("Pure functions (fingerprint / balance)");

            Case.Run("function.fingerprintMatchesLengthPrefixedEncoding", () =>
            {
                byte[] expectedPayload = new byte[8 + 3 + 8 + 3];
                WriteUInt64(expectedPayload, 0, 3);
                expectedPayload[8] = (byte)'d';
                expectedPayload[9] = (byte)'e';
                expectedPayload[10] = (byte)'f';
                WriteUInt64(expectedPayload, 11, 3);
                expectedPayload[19] = (byte)'a';
                expectedPayload[20] = (byte)'b';
                expectedPayload[21] = (byte)'c';
                string expected;
                using (SHA256 sha = SHA256.Create())
                {
                    expected = Convert.ToHexString(sha.ComputeHash(expectedPayload)).ToLowerInvariant();
                }

                Check.Equal(expected, KnightIdentityFingerprint.Sha256("abc", "def"), "encoding must be len(context)||context||len(raw)||raw");
            });

            Case.Run("function.fingerprintIsStableHex64AndCollisionSafe", () =>
            {
                string first = KnightIdentityFingerprint.Sha256("{\"island\":1}", "campaign-1");
                Check.Equal(first, KnightIdentityFingerprint.Sha256("{\"island\":1}", "campaign-1"), "pure and stable");
                Check.Equal(KnightIdentityFingerprint.HexLength, first.Length, "64 chars");
                Check.True(KnightIdentityFingerprint.IsHex64(first), "hex64 accepted");
                Check.NotEqual(KnightIdentityFingerprint.Sha256("ab", "c"), KnightIdentityFingerprint.Sha256("a", "bc"), "length prefix prevents concatenation collisions");
                Check.NotEqual(KnightIdentityFingerprint.Sha256("same", "left"), KnightIdentityFingerprint.Sha256("same", "right"), "context participates in the hash");
                Check.NotEqual(KnightIdentityFingerprint.Sha256("left", "same"), KnightIdentityFingerprint.Sha256("right", "same"), "snapshot participates in the hash");
                Check.NotEqual(KnightIdentityFingerprint.Sha256(string.Empty, "k"), KnightIdentityFingerprint.Sha256("k", string.Empty), "argument order is significant");
                Check.Equal(first, KnightIdentityFingerprint.NormalizeHex64(first.ToUpperInvariant()), "uppercase normalizes to the same value");
                Check.False(KnightIdentityFingerprint.IsHex64(first.Substring(0, 63)), "63 chars rejected");
                Check.False(KnightIdentityFingerprint.IsHex64(first + "0"), "65 chars rejected");
                Check.False(KnightIdentityFingerprint.IsHex64("zz" + first.Substring(2)), "non hex rejected");
                Check.False(KnightIdentityFingerprint.IsHex64(null), "null rejected");
                Check.Throws<ArgumentNullException>(() => KnightIdentityFingerprint.Sha256(null, "c"), "null snapshot rejected");
                Check.Throws<ArgumentNullException>(() => KnightIdentityFingerprint.Sha256("s", null), "null context rejected");
            });

            Case.Run("function.balancePicksOnlyTheLeastAvailableStyle", () =>
            {
                int[] all = { 0, 1, 2, 3, 4 };
                Check.True(KnightIdentityBalance.ChooseLeast(new[] { 5, 0, 0, 0, 0 }, all, 0) >= 1, "style 0 with 5 knights is never picked");
                Check.True(KnightIdentityBalance.ChooseLeast(new[] { 5, 0, 0, 0, 0 }, all, 77) >= 1, "style 0 with 5 knights is never picked, any entropy");
                Check.Equal(0, KnightIdentityBalance.ChooseLeast(new[] { 0, 5, 5, 5, 5 }, all, 3), "only the sparse style is picked");

                int[] lopsided = { 7, 1, 1, 1, 1 };
                for (uint entropy = 0; entropy < 64; entropy++)
                {
                    int style = KnightIdentityBalance.ChooseLeast(lopsided, all, entropy);
                    Check.True(style >= 1 && style <= 4, "style 0 already has 7 and must not be picked");
                }

                int[] zeros = { 0, 0, 0, 0, 0 };
                int[] restricted = { 1, 2 };
                for (uint entropy = 0; entropy < 64; entropy++)
                {
                    int style = KnightIdentityBalance.ChooseLeast(zeros, restricted, entropy);
                    Check.True(style == 1 || style == 2, "unavailable styles are never picked");
                }
            });

            Case.Run("function.balanceTieBreaksDeterministicallyWithoutMutatingInputs", () =>
            {
                int[] counts = { 4, 4, 4, 4, 4 };
                int[] countsBefore = (int[])counts.Clone();
                List<int> available = new List<int> { 0, 1, 2, 3, 4 };
                int[] availableBefore = available.ToArray();

                HashSet<int> reached = new HashSet<int>();
                for (uint entropy = 0; entropy < 64; entropy++)
                {
                    int style = KnightIdentityBalance.ChooseLeast(counts, available, entropy);
                    Check.Equal(style, KnightIdentityBalance.ChooseLeast(counts, available, entropy), "same entropy, same result");
                    reached.Add(style);
                }
                Check.Equal(5, reached.Count, "entropy reaches every tied candidate");

                Check.True(counts.AsSpan().SequenceEqual(countsBefore), "counts are not mutated");
                Check.True(available.ToArray().AsSpan().SequenceEqual(availableBefore), "available is not mutated");
            });

            Case.Run("function.balanceRejectsInvalidInput", () =>
            {
                int[] all = { 0, 1, 2, 3, 4 };
                int[] counts = { 0, 0, 0, 0, 0 };
                Check.Throws<ArgumentNullException>(() => KnightIdentityBalance.ChooseLeast(null, all, 0), "null counts rejected");
                Check.Throws<ArgumentException>(() => KnightIdentityBalance.ChooseLeast(new int[4], all, 0), "counts length rejected");
                Check.Throws<ArgumentException>(() => KnightIdentityBalance.ChooseLeast(new[] { 0, 0, -1, 0, 0 }, all, 0), "negative count rejected");
                Check.Throws<ArgumentNullException>(() => KnightIdentityBalance.ChooseLeast(counts, null, 0), "null available rejected");
                Check.Throws<ArgumentException>(() => KnightIdentityBalance.ChooseLeast(counts, Array.Empty<int>(), 0), "empty available rejected");
                Check.Throws<ArgumentException>(() => KnightIdentityBalance.ChooseLeast(counts, new[] { 5 }, 0), "style above 4 rejected");
                Check.Throws<ArgumentException>(() => KnightIdentityBalance.ChooseLeast(counts, new[] { -1 }, 0), "negative style rejected");
                Check.Throws<ArgumentException>(() => KnightIdentityBalance.ChooseLeast(counts, new[] { 1, 1 }, 0), "duplicate style rejected");
            });

            Case.Run("function.balanceTieBreakIsUniformOverFullCycles", () =>
            {
                int[] all = { 0, 1, 2, 3, 4 };
                const int cycles = 25;

                // 4 路并列：styles 0..3 同为 4，style 4 是 9（永不该被选）
                int[] fourWay = { 4, 4, 4, 4, 9 };
                int[] four = new int[KnightIdentityReceipt.StyleCount];
                for (uint entropy = 0; entropy < 4 * cycles; entropy++) four[KnightIdentityBalance.ChooseLeast(fourWay, all, entropy)]++;
                for (int style = 0; style < 4; style++)
                {
                    Check.Equal(cycles, four[style], "4-way tie must hit style " + style + " exactly " + cycles + " times");
                }
                Check.Equal(0, four[4], "the non-minimum style is never picked");

                // 5 路并列：整周期里每个候选频次完全相同（旧的逐步 reservoir 版本在这里是有偏的）
                int[] fiveWay = { 2, 2, 2, 2, 2 };
                int[] five = new int[KnightIdentityReceipt.StyleCount];
                for (uint entropy = 0; entropy < 5 * cycles; entropy++) five[KnightIdentityBalance.ChooseLeast(fiveWay, all, entropy)]++;
                for (int style = 0; style < KnightIdentityReceipt.StyleCount; style++)
                {
                    Check.Equal(cycles, five[style], "5-way tie must hit style " + style + " exactly " + cycles + " times");
                }

                // 受限可用集里的并列同样均匀
                int[] restricted = { 2, 3, 4 };
                int[] limited = new int[KnightIdentityReceipt.StyleCount];
                for (uint entropy = 0; entropy < 3 * cycles; entropy++) limited[KnightIdentityBalance.ChooseLeast(new[] { 1, 1, 1, 1, 1 }, restricted, entropy)]++;
                for (int i = 0; i < restricted.Length; i++)
                {
                    Check.Equal(cycles, limited[restricted[i]], "restricted tie must hit style " + restricted[i] + " exactly " + cycles + " times");
                }
                Check.Equal(0, limited[0] + limited[1], "unavailable styles untouched");
            });

            Case.Run("function.balanceKeepsRecruitSequenceBalanced", () =>
            {
                int[] all = { 0, 1, 2, 3, 4 };
                int[] counts = new int[KnightIdentityReceipt.StyleCount];
                uint entropy = 12345u;
                for (int recruit = 0; recruit < 40; recruit++)
                {
                    entropy = entropy * 1664525u + 1013904223u;
                    int style = KnightIdentityBalance.ChooseLeast(counts, all, entropy);
                    Check.True(KnightIdentityReceipt.IsValidStyle(style), "picked style is in range");
                    counts[style]++;
                }

                int min = counts[0];
                int max = counts[0];
                int total = 0;
                for (int i = 0; i < counts.Length; i++)
                {
                    min = Math.Min(min, counts[i]);
                    max = Math.Max(max, counts[i]);
                    total += counts[i];
                }
                Check.Equal(40, total, "every recruit landed somewhere");
                Check.True(max - min <= 1, "40 recruits spread across 5 styles within 1 [min=" + min + " max=" + max + "]");

                int[] restricted = { 2, 3 };
                int[] limited = new int[KnightIdentityReceipt.StyleCount];
                for (int recruit = 0; recruit < 21; recruit++)
                {
                    entropy = entropy * 1664525u + 1013904223u;
                    int style = KnightIdentityBalance.ChooseLeast(limited, restricted, entropy);
                    Check.True(style == 2 || style == 3, "restricted availability respected");
                    limited[style]++;
                }
                Check.True(Math.Abs(limited[2] - limited[3]) <= 1, "restricted styles balanced [2=" + limited[2] + " 3=" + limited[3] + "]");
                Check.Equal(0, limited[0] + limited[1] + limited[4], "unavailable styles untouched");
            });
        }

        private static void WriteUInt64(byte[] destination, int offset, ulong value)
        {
            for (int i = 0; i < 8; i++) destination[offset + i] = (byte)(value >> (i * 8));
        }
    }
}
