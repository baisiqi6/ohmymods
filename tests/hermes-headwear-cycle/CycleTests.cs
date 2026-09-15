using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using KingdomEnhancedMod;

namespace HermesHeadwearCycleTests
{
    /// <summary>
    /// NET8 链接真实生产源（HERMES_CYCLE_TEST seam）的回归：稳定 30% 配额、跨重启/分批接续、
    /// 44 轮换、v1→v2 迁移、闭 schema 拒绝、原子写/竞态/锁/写失败/进程内并发。
    /// </summary>
    internal static class CycleTests
    {
        internal static void Run()
        {
            Case.Run("fresh start: 30% records credit only, assigns at mobs 4/7/10", FreshCreditThenFirstAssign);
            Case.Run("100 mobs at 30%: exactly 30 assignments, 3 per decade, offsets 4/7/10", HundredMobsAtThirtyPercent);
            Case.Run("restart and batching never shift the 30% positions", RestartAndBatchContinuity);
            Case.Run("100% assigns every mob in exact 0..43 order across two cycles", FullChanceExactCycles);
            Case.Run("0% (and negative) never advances, never writes, never creates", ZeroChanceNeverWrites);
            Case.Run("1%/25%/50% positions, mid-run config change, out-of-range clamp", OtherChancesAndClamp);
            Case.Run("v1 next=8 migrates on the first 100% call; old file byte-identical", LegacyMigrationAssign);
            Case.Run("v1 next=8 credit build-up migrates on first credit write", LegacyMigrationCreditBuildUp);
            Case.Run("existing v2 wins over v1; bad v1 beside valid v2 is ignored", V2PriorityOverLegacy);
            Case.Run("bad v2 never falls back to a valid v1, neither is overwritten", BadV2NeverFallsBack);
            Case.Run("bad v1 refuses: no v2 created, v1 byte-identical", BadLegacyRefused);
            Case.Run("bad/corrupt/oversized/unknown-schema v2 refused byte-identical", BadV2Refused);
            Case.Run("v2 bytes are the exact schema2 contract; bak holds previous state", ExactSchemaBytesAndBackup);
            Case.Run("non-host refuses and never writes", NonHostRefused);
            Case.Run("authority probe exception refuses", AuthorityThrowRefused);
            Case.Run("no injected path refuses (no arbitrary write surface)", NoPathRefused);
            Case.Run("denied read refuses; denied write consumes no credit and retries same code", DeniedReadWriteRetried);
            Case.Run("locked legacy v1 refuses instead of starting fresh at 0", LockedLegacyRefused);
            Case.Run("file appearing after a missing read is never overwritten", AppearingFileNeverOverwritten);
            Case.Run("file vanishing after an existing read fails instead of recreating", VanishingFileFails);
            Case.Run("external valid change adopted; corruption preserved; deletion restarts", ExternalStateHandling);
            Case.Run("44 concurrent 100% calls hand out each code exactly once", ConcurrentUniquePermutation);
            Case.Run("v2 path occupied by a directory refuses, leaves no temp", DirectoryBlocksWriteRefused);
            Case.Run("unwritable parent refused, user file untouched", UnwritableParentRefused);
            Case.Run("missing nested parent directory is created on first credit write", NestedDirectoryCreated);
            Case.Run("constants and parser bounds", Constants);
        }

        private static void FreshCreditThenFirstAssign()
        {
            using (Scope scope = Scope.Create())
            {
                Check.False(File.Exists(scope.Path), "no file before the first conversion");
                Check.Equal(-1, Fixture.Assign(30), "mob 1 records credit only");
                Check.True(File.Exists(scope.Path), "first credit write creates v2");
                Check.Equal(0, Fixture.Next(scope.Path), "cursor untouched at 0");
                Check.Equal(30, Fixture.Credit(scope.Path), "credit 30");
                Check.Equal(-1, Fixture.Assign(30), "mob 2 none, credit 60");
                Check.Equal(60, Fixture.Credit(scope.Path), "credit 60");
                Check.Equal(-1, Fixture.Assign(30), "mob 3 none, credit 90");
                Check.Equal(90, Fixture.Credit(scope.Path), "credit 90");
                Check.Equal(0, Fixture.Assign(30), "mob 4 assigns code 0");
                Check.Equal(1, Fixture.Next(scope.Path), "cursor advanced to 1");
                Check.Equal(20, Fixture.Credit(scope.Path), "credit 20 (stable 30% quota)");
            }
        }

        private static void HundredMobsAtThirtyPercent()
        {
            using (Scope scope = Scope.Create())
            {
                List<int> positions = new List<int>();
                List<int> choices = new List<int>();
                for (int n = 1; n <= 100; n++)
                {
                    int choice = Fixture.Assign(30);
                    if (choice != HermesHeadwearCycle.ChoiceNone)
                    {
                        positions.Add(n);
                        choices.Add(choice);
                    }
                }

                // 30 ≤ n：n=4 时 120、7 时 210、10 时 300 → 每十年固定偏移 4/7/10（人类独立推算）
                int[] expected =
                {
                    4, 7, 10, 14, 17, 20, 24, 27, 30, 34,
                    37, 40, 44, 47, 50, 54, 57, 60, 64, 67,
                    70, 74, 77, 80, 84, 87, 90, 94, 97, 100,
                };
                Check.Sequence(expected, positions, "assignment positions over 100 mobs");
                Check.Equal(30, positions.Count, "exactly 30 assignments in 100 mobs");
                int[] offsets = { 4, 7, 10 };
                for (int decade = 0; decade < 10; decade++)
                {
                    for (int k = 0; k < 3; k++)
                    {
                        Check.Equal(decade * 10 + offsets[k], positions[decade * 3 + k],
                            "3 per decade, offset " + offsets[k] + " in decade " + decade);
                    }
                }
                for (int i = 0; i < choices.Count; i++) Check.Equal(i, choices[i], "cursor order 0..29");
                Check.Equal(30, Fixture.Next(scope.Path), "cursor at 30 after 30 assignments");
                Check.Equal(0, Fixture.Credit(scope.Path), "credit exactly 0 after 100 mobs");
            }
        }

        private static void RestartAndBatchContinuity()
        {
            List<int> baseline = RunThirtyPercent(resetEvery: 0);
            Check.Equal(30, baseline.Count, "baseline assigns 30 times");
            foreach (int batch in new[] { 1, 3, 7 })
            {
                Check.Sequence(baseline, RunThirtyPercent(batch), "restart every " + batch + " mobs must not shift anything");
            }
        }

        /// <summary>100 只 30%，每 resetEvery 只模拟一次进程重启（0 = 从不）；返回发放位置。</summary>
        private static List<int> RunThirtyPercent(int resetEvery)
        {
            using (Scope scope = Scope.Create())
            {
                List<int> positions = new List<int>();
                for (int n = 1; n <= 100; n++)
                {
                    if (resetEvery > 0 && n % resetEvery == 0) HermesHeadwearCycle.ResetForTests();
                    if (Fixture.Assign(30) != HermesHeadwearCycle.ChoiceNone) positions.Add(n);
                }
                return positions;
            }
        }

        private static void FullChanceExactCycles()
        {
            using (Scope scope = Scope.Create())
            {
                HashSet<int> seen = new HashSet<int>();
                for (int i = 0; i < 2 * HermesHeadwearCycle.ChoiceCount; i++)
                {
                    int choice = Fixture.Assign(100);
                    Check.Equal(i % HermesHeadwearCycle.ChoiceCount, choice, "hit " + i + " order");
                    if (i < HermesHeadwearCycle.ChoiceCount) Check.True(seen.Add(choice), "first cycle unique");
                }
                Check.Equal(HermesHeadwearCycle.ChoiceCount, seen.Count, "all 44 codes in the first cycle");
                Check.Equal(0, Fixture.Next(scope.Path), "file back to 0 after two cycles");
                Check.Equal(0, Fixture.Credit(scope.Path), "100% leaves credit 0");
            }
        }

        private static void ZeroChanceNeverWrites()
        {
            using (Scope scope = Scope.Create())
            {
                for (int i = 0; i < 5; i++) Check.Equal(-1, Fixture.Assign(0), "0% yields none #" + i);
                Check.Equal(string.Empty, Fixture.Joined(Fixture.FilesIn(scope.Dir.Path)), "0% never creates a file");

                Fixture.Write(scope.Path, Fixture.DocV2(7, 55));
                byte[] before = Fixture.Read(scope.Path);
                Check.Equal(-1, Fixture.Assign(0), "0% with existing state yields none");
                Check.Equal(-1, Fixture.Assign(-9), "negative clamped to 0, no assignment");
                Check.Equal(-1, Fixture.Assign(int.MinValue), "int.MinValue clamped to 0");
                Check.True(Fixture.BytesEqual(before, Fixture.Read(scope.Path)), "0% writes nothing at all");
                Check.Equal("hermes-headwear-cycle.v2.json", Fixture.Joined(Fixture.FilesIn(scope.Dir.Path)),
                    "no bak/temp added by 0%");
            }
        }

        private static void OtherChancesAndClamp()
        {
            using (Scope scope = Scope.Create())
            {
                // 1%：恰好第 100 只发放
                int assignments = 0;
                for (int n = 1; n <= 100; n++)
                {
                    int choice = Fixture.Assign(1);
                    if (choice != HermesHeadwearCycle.ChoiceNone)
                    {
                        assignments++;
                        Check.Equal(100, n, "1% first (and only) assignment at mob 100");
                        Check.Equal(0, choice, "1% assigns code 0");
                    }
                }
                Check.Equal(1, assignments, "1% assigns exactly once per 100 mobs");
                Check.Equal(0, Fixture.Credit(scope.Path), "1% credit cleared");

                // 50%：每 2 只
                int expected = 1;
                for (int n = 1; n <= 6; n++)
                {
                    int want = (n % 2 == 0) ? expected++ : HermesHeadwearCycle.ChoiceNone;
                    Check.Equal(want, Fixture.Assign(50), "50% at mob " + n);
                }

                // 25%：每 4 只
                for (int n = 1; n <= 8; n++)
                {
                    int want = (n % 4 == 0) ? expected++ : HermesHeadwearCycle.ChoiceNone;
                    Check.Equal(want, Fixture.Assign(25), "25% at mob " + n);
                }
                Check.Equal(0, Fixture.Credit(scope.Path), "credit back to 0");
            }

            using (Scope scope = Scope.Create())
            {
                // 运行中改配置：credit 跨配置保留（30×3 = 90 → 50 补满）
                Check.Equal(-1, Fixture.Assign(30), "30% mob 1");
                Check.Equal(-1, Fixture.Assign(30), "30% mob 2");
                Check.Equal(-1, Fixture.Assign(30), "30% mob 3");
                Check.Equal(0, Fixture.Assign(50), "50% completes the quota and assigns 0");
                Check.Equal(40, Fixture.Credit(scope.Path), "leftover credit 40 kept across config change");
                Check.Equal(-1, Fixture.Assign(30), "credit 70");
                Check.Equal(1, Fixture.Assign(30), "credit 100 assigns 1");
                Check.Equal(0, Fixture.Credit(scope.Path), "credit 0");

                // 越界 clamp：>100 按 100（每只发放），<=0 按 0（不写）
                for (int i = 0; i < 3; i++) Check.Equal(2 + i, Fixture.Assign(150), "150 clamped to 100");
                Check.Equal(0, Fixture.Credit(scope.Path), "clamped 100 keeps credit 0");
                byte[] before = Fixture.Read(scope.Path);
                Check.Equal(-1, Fixture.Assign(-999), "negative clamped to 0");
                Check.True(Fixture.BytesEqual(before, Fixture.Read(scope.Path)), "clamped-to-0 writes nothing");
            }
        }

        private static void LegacyMigrationAssign()
        {
            using (Scope scope = Scope.Create())
            {
                Fixture.Write(scope.LegacyPath, Fixture.DocV1(8)); // 真实用户游标：next=8
                byte[] legacy = Fixture.Read(scope.LegacyPath);

                Check.Equal(8, Fixture.Assign(100), "first 100% call delivers legacy next 8");
                Check.True(Fixture.BytesEqual(Fixture.DocV2(9, 0), Fixture.Read(scope.Path)), "v2 migrated with next 9, credit 0");
                Check.True(Fixture.BytesEqual(legacy, Fixture.Read(scope.LegacyPath)), "v1 kept byte-identical");
                Check.Equal("hermes-headwear-cycle.v1.json|hermes-headwear-cycle.v2.json",
                    Fixture.Joined(Fixture.FilesIn(scope.Dir.Path)), "migration creates only v2, v1 retained, no bak/temp");
                Check.Equal(9, Fixture.Assign(100), "second call continues from v2");
                Check.Equal(10, Fixture.Next(scope.Path), "v2 advances");
                Check.True(Fixture.BytesEqual(legacy, Fixture.Read(scope.LegacyPath)), "v1 still byte-identical after later writes");
            }
        }

        private static void LegacyMigrationCreditBuildUp()
        {
            using (Scope scope = Scope.Create())
            {
                Fixture.Write(scope.LegacyPath, Fixture.DocV1(8));
                byte[] legacy = Fixture.Read(scope.LegacyPath);

                Check.Equal(-1, Fixture.Assign(30), "mob 1 records credit, no assignment");
                Check.True(Fixture.BytesEqual(Fixture.DocV2(8, 30), Fixture.Read(scope.Path)),
                    "migration on the first credit write keeps next 8 with credit 30");
                Check.Equal(-1, Fixture.Assign(30), "credit 60");
                Check.Equal(-1, Fixture.Assign(30), "credit 90");
                Check.Equal(8, Fixture.Assign(30), "4th mob assigns legacy code 8");
                Check.Equal(20, Fixture.Credit(scope.Path), "credit 20");
                Check.True(Fixture.BytesEqual(legacy, Fixture.Read(scope.LegacyPath)), "v1 untouched by credit-only writes");
            }
        }

        private static void V2PriorityOverLegacy()
        {
            using (Scope scope = Scope.Create())
            {
                Fixture.Write(scope.LegacyPath, Fixture.DocV1(0));
                Fixture.Write(scope.Path, Fixture.DocV2(41, 5));

                Check.Equal(41, Fixture.Assign(100), "v2 wins over v1");
                Check.Equal(42, Fixture.Next(scope.Path), "v2 advanced");
                Check.Equal(5, Fixture.Credit(scope.Path), "v2 credit kept");
                Check.Equal(0, Fixture.LegacyNext(scope.LegacyPath), "v1 not consumed while v2 exists");

                Fixture.Write(scope.LegacyPath, Encoding.UTF8.GetBytes("{ nope")); // 坏 v1 与合法 v2 并存
                Check.Equal(42, Fixture.Assign(100), "bad v1 beside valid v2 is ignored");
                Check.Equal(43, Fixture.Next(scope.Path), "v2 keeps advancing");
            }
        }

        private static void BadV2NeverFallsBack()
        {
            using (Scope scope = Scope.Create())
            {
                Fixture.Write(scope.LegacyPath, Fixture.DocV1(8));
                byte[] corrupt = Encoding.UTF8.GetBytes("{ not json");
                Fixture.Write(scope.Path, corrupt);

                for (int i = 0; i < 3; i++) Fixture.Refuse(100);
                Check.True(Fixture.BytesEqual(corrupt, Fixture.Read(scope.Path)), "bad v2 preserved byte-identical");
                Check.True(Fixture.BytesEqual(Fixture.DocV1(8), Fixture.Read(scope.LegacyPath)),
                    "no fallback to v1 while a v2 exists");
                Check.Equal("hermes-headwear-cycle.v1.json|hermes-headwear-cycle.v2.json",
                    Fixture.Joined(Fixture.FilesIn(scope.Dir.Path)), "no bak/temp added");
            }
        }

        private static void BadLegacyRefused()
        {
            string[] bad =
            {
                "{\"v\":1,",
                "[]",
                "{\"v\":2,\"kind\":\"hermes-headwear-cycle\",\"nextChoice\":3}",                     // 版本不符
                "{\"v\":1,\"kind\":\"knight-identity\",\"nextChoice\":3}",                          // kind
                "{\"v\":1,\"kind\":\"hermes-headwear-cycle\",\"nextChoice\":44}",                   // 越界
                "{\"v\":1,\"kind\":\"hermes-headwear-cycle\",\"nextChoice\":3,\"credit\":0}",       // schema1 无 credit 字段
                "{\"v\":1,\"kind\":\"hermes-headwear-cycle\"}",                                     // 缺字段
                "{\"v\":1,\"v\":1,\"kind\":\"hermes-headwear-cycle\",\"nextChoice\":3}",             // 重复字段
            };
            using (Scope scope = Scope.Create())
            {
                foreach (string text in bad)
                {
                    if (File.Exists(scope.LegacyPath)) File.Delete(scope.LegacyPath);
                    byte[] bytes = Encoding.UTF8.GetBytes(text);
                    Fixture.Write(scope.LegacyPath, bytes);
                    Fixture.Refuse(30);
                    Check.True(Fixture.BytesEqual(bytes, Fixture.Read(scope.LegacyPath)), "bad v1 untouched: " + text);
                    Check.False(File.Exists(scope.Path), "bad v1 must not create a v2: " + text);
                }

                Fixture.Write(scope.LegacyPath, Array.Empty<byte>());
                Fixture.Refuse(30);
                Check.Equal(0L, new FileInfo(scope.LegacyPath).Length, "empty v1 stays empty");
                Check.False(File.Exists(scope.Path), "empty v1 must not create a v2");

                byte[] huge = Encoding.UTF8.GetBytes(
                    "{\"v\":1,\"kind\":\"hermes-headwear-cycle\",\"nextChoice\":3}" + new string(' ', 6000));
                Fixture.Write(scope.LegacyPath, huge);
                Fixture.Refuse(30);
                Check.True(Fixture.BytesEqual(huge, Fixture.Read(scope.LegacyPath)), "oversized v1 untouched");
                Check.False(File.Exists(scope.Path), "oversized v1 must not create a v2");
            }
        }

        private static void BadV2Refused()
        {
            string[] bad =
            {
                "{\"v\":2,",
                "[]",
                "{\"v\":1,\"kind\":\"hermes-headwear-cycle\",\"nextChoice\":3,\"credit\":0}",       // v2 槽里的旧版本
                "{\"v\":3,\"kind\":\"hermes-headwear-cycle\",\"nextChoice\":3,\"credit\":0}",       // 未知版本
                "{\"v\":2,\"kind\":\"knight-identity\",\"nextChoice\":3,\"credit\":0}",             // kind
                "{\"v\":2,\"kind\":\"hermes-headwear-cycle\",\"nextChoice\":44,\"credit\":0}",      // next 越界
                "{\"v\":2,\"kind\":\"hermes-headwear-cycle\",\"nextChoice\":-1,\"credit\":0}",
                "{\"v\":2,\"kind\":\"hermes-headwear-cycle\",\"nextChoice\":\"3\",\"credit\":0}",
                "{\"v\":2,\"kind\":\"hermes-headwear-cycle\",\"nextChoice\":3,\"credit\":100}",     // credit 越界
                "{\"v\":2,\"kind\":\"hermes-headwear-cycle\",\"nextChoice\":3,\"credit\":-1}",
                "{\"v\":2,\"kind\":\"hermes-headwear-cycle\",\"nextChoice\":3,\"credit\":\"0\"}",
                "{\"v\":2,\"kind\":\"hermes-headwear-cycle\",\"nextChoice\":3,\"credit\":0.5}",
                "{\"v\":2,\"kind\":\"hermes-headwear-cycle\",\"nextChoice\":3,\"credit\":0,\"extra\":1}",   // 未知字段
                "{\"v\":2,\"v\":2,\"kind\":\"hermes-headwear-cycle\",\"nextChoice\":3,\"credit\":0}",       // 重复字段
                "{\"v\":2,\"kind\":\"hermes-headwear-cycle\",\"nextChoice\":3,\"credit\":0,\"credit\":1}",
                "{\"v\":2,\"kind\":\"hermes-headwear-cycle\",\"nextChoice\":3}",                            // 缺 credit
                "{\"v\":2,\"kind\":\"hermes-headwear-cycle\",\"credit\":0}",                                // 缺 next
                "{\"v\":2,\"kind\":\"hermes-headwear-cycle\",\"nextChoice\":3,\"credit\":0} trailing",      // 尾随内容
            };
            using (Scope scope = Scope.Create())
            {
                foreach (string text in bad)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(text);
                    Fixture.Write(scope.Path, bytes);
                    Fixture.Refuse(30);
                    Check.True(Fixture.BytesEqual(bytes, Fixture.Read(scope.Path)), "bad v2 untouched: " + text);
                }
                Check.Equal("hermes-headwear-cycle.v2.json", Fixture.Joined(Fixture.FilesIn(scope.Dir.Path)),
                    "no bak/temp added for bad v2");

                Fixture.Write(scope.Path, Array.Empty<byte>());
                Fixture.Refuse(30);
                Check.Equal(0L, new FileInfo(scope.Path).Length, "empty v2 stays empty");

                byte[] huge = Encoding.UTF8.GetBytes(
                    "{\"v\":2,\"kind\":\"hermes-headwear-cycle\",\"nextChoice\":3,\"credit\":0}" + new string(' ', 6000));
                Check.True(huge.Length > HermesHeadwearCycle.MaxFileBytes, "fixture must exceed the cap");
                Fixture.Write(scope.Path, huge);
                Fixture.Refuse(30);
                Check.True(Fixture.BytesEqual(huge, Fixture.Read(scope.Path)), "oversized v2 untouched");
            }
        }

        private static void ExactSchemaBytesAndBackup()
        {
            using (Scope scope = Scope.Create())
            {
                Check.Equal(-1, Fixture.Assign(30), "mob 1 records credit only");
                Check.Sequence(
                    new[] { "{\"v\":2,\"kind\":\"hermes-headwear-cycle\",\"nextChoice\":0,\"credit\":30}" },
                    new[] { Encoding.UTF8.GetString(Fixture.Read(scope.Path)) },
                    "credit-only document is the exact schema2 contract");
                Check.Equal("hermes-headwear-cycle.v2.json", Fixture.Joined(Fixture.FilesIn(scope.Dir.Path)),
                    "create writes no bak");
                Check.True(Fixture.Read(scope.Path).Length < HermesHeadwearCycle.MaxFileBytes, "far below the size cap");

                Check.Equal(0, Fixture.Assign(100), "credit 130 assigns 0");
                Check.Sequence(
                    new[] { "{\"v\":2,\"kind\":\"hermes-headwear-cycle\",\"nextChoice\":1,\"credit\":30}" },
                    new[] { Encoding.UTF8.GetString(Fixture.Read(scope.Path)) },
                    "assigned document keeps leftover credit");

                Check.Equal(1, Fixture.Assign(70), "credit 100 assigns 1");
                Check.Sequence(
                    new[] { "{\"v\":2,\"kind\":\"hermes-headwear-cycle\",\"nextChoice\":2,\"credit\":0}" },
                    new[] { Encoding.UTF8.GetString(Fixture.Read(scope.Path)) },
                    "final document is exact");
                Check.Equal("hermes-headwear-cycle.v2.json|hermes-headwear-cycle.v2.json.bak",
                    Fixture.Joined(Fixture.FilesIn(scope.Dir.Path)), "replace keeps main + bak");
                Check.Sequence(
                    new[] { "{\"v\":2,\"kind\":\"hermes-headwear-cycle\",\"nextChoice\":1,\"credit\":30}" },
                    new[] { Encoding.UTF8.GetString(Fixture.Read(scope.Path + ".bak")) },
                    "bak holds the previous state");
            }
        }

        private static void NonHostRefused()
        {
            using (Scope scope = Scope.Create(authority: false))
            {
                Fixture.Refuse(100);
                Fixture.Refuse(30);
                Check.Equal(string.Empty, Fixture.Joined(Fixture.FilesIn(scope.Dir.Path)), "client never writes");

                scope.SetAuthority(true);
                Check.Equal(0, Fixture.Assign(100), "host after promo starts fresh at 0");
                Check.Equal(1, Fixture.Next(scope.Path), "host file created");
            }
        }

        private static void AuthorityThrowRefused()
        {
            using (Scope scope = Scope.Create())
            {
                HermesHeadwearCycle.AuthorityOverride = () => throw new InvalidOperationException("probe failed");
                Fixture.Refuse(100);
                Check.Equal(string.Empty, Fixture.Joined(Fixture.FilesIn(scope.Dir.Path)), "no write on probe failure");
            }
        }

        private static void NoPathRefused()
        {
            using (Scope scope = Scope.Create())
            {
                scope.SetPath(null);
                Fixture.Refuse(30);
                Check.Equal(string.Empty, Fixture.Joined(Fixture.FilesIn(scope.Dir.Path)), "nothing written");
            }
        }

        private static void DeniedReadWriteRetried()
        {
            using (Scope scope = Scope.Create())
            {
                Fixture.Write(scope.Path, Fixture.DocV2(5, 90));
                byte[] persisted = Fixture.Read(scope.Path);

                using (new FileStream(scope.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    Fixture.Refuse(30); // 读被拒绝 ≠ 缺失：绝不当 0 起
                }
                Check.True(Fixture.BytesEqual(persisted, Fixture.Read(scope.Path)), "read denial leaves state byte-identical");

                using (new FileStream(scope.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    Fixture.Refuse(30); // 读成功、原子替换被共享拒绝：写失败，零进度
                }
                Check.True(Fixture.BytesEqual(persisted, Fixture.Read(scope.Path)), "write denial consumes no credit");
                Check.False(File.Exists(scope.Path + ".bak"), "failed replace leaves no bak");
                Check.Equal("hermes-headwear-cycle.v2.json", Fixture.Joined(Fixture.FilesIn(scope.Dir.Path)),
                    "failed write leaves no temp");

                Check.Equal(5, Fixture.Assign(30), "retry assigns the very same code 5");
                Check.Equal(6, Fixture.Next(scope.Path), "advanced to 6");
                Check.Equal(20, Fixture.Credit(scope.Path), "credit 20 after the retry");
            }
        }

        private static void LockedLegacyRefused()
        {
            using (Scope scope = Scope.Create())
            {
                Fixture.Write(scope.LegacyPath, Fixture.DocV1(5));
                using (new FileStream(scope.LegacyPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    Fixture.Refuse(100); // 旧 v1 读失败：绝不当 0 起、绝不建 v2
                }
                Check.False(File.Exists(scope.Path), "no v2 created from a denied legacy read");
                Check.True(Fixture.BytesEqual(Fixture.DocV1(5), Fixture.Read(scope.LegacyPath)), "v1 intact");

                Check.Equal(5, Fixture.Assign(100), "legacy adopted once readable");
                Check.Equal(6, Fixture.Next(scope.Path), "v2 next 6");
                Check.True(Fixture.BytesEqual(Fixture.DocV1(5), Fixture.Read(scope.LegacyPath)), "v1 still byte-identical");
            }
        }

        private static void AppearingFileNeverOverwritten()
        {
            using (Scope scope = Scope.Create())
            {
                int fired = 0;
                HermesHeadwearCycle.BeforeWriteHook = path =>
                {
                    if (fired++ != 0) return;
                    Fixture.Write(path, Fixture.DocV2(9, 0)); // 读时缺失，写前外部建立 v2
                };
                Fixture.Refuse(30);
                Check.True(Fixture.BytesEqual(Fixture.DocV2(9, 0), Fixture.Read(scope.Path)), "appearing v2 preserved");
                Check.Equal("hermes-headwear-cycle.v2.json", Fixture.Joined(Fixture.FilesIn(scope.Dir.Path)),
                    "no temp left after the refused create");

                // v1 在场（迁移路径）时同样：v2 出现即拒绝，外部新值优先于旧 v1
                HermesHeadwearCycle.BeforeWriteHook = null;
                File.Delete(scope.Path);
                Fixture.Write(scope.LegacyPath, Fixture.DocV1(8));
                fired = 0;
                HermesHeadwearCycle.BeforeWriteHook = path =>
                {
                    if (fired++ != 0) return;
                    Fixture.Write(path, Fixture.DocV2(40, 0));
                };
                Fixture.Refuse(100);
                Check.True(Fixture.BytesEqual(Fixture.DocV2(40, 0), Fixture.Read(scope.Path)), "appearing v2 beats legacy");
                Check.True(Fixture.BytesEqual(Fixture.DocV1(8), Fixture.Read(scope.LegacyPath)), "legacy untouched");

                HermesHeadwearCycle.BeforeWriteHook = null;
                Check.Equal(40, Fixture.Assign(100), "next call adopts the external v2");
            }
        }

        private static void VanishingFileFails()
        {
            using (Scope scope = Scope.Create())
            {
                Fixture.Write(scope.Path, Fixture.DocV2(3, 10));
                bool fired = false;
                HermesHeadwearCycle.BeforeWriteHook = path =>
                {
                    if (fired) return;
                    fired = true;
                    File.Delete(path);
                };
                Fixture.Refuse(30);
                Check.False(File.Exists(scope.Path), "vanished file is not recreated");
                Check.Equal(string.Empty, Fixture.Joined(Fixture.FilesIn(scope.Dir.Path)), "no temp left");

                HermesHeadwearCycle.BeforeWriteHook = null;
                Check.Equal(-1, Fixture.Assign(30), "next call starts fresh (file truly gone)");
                Check.Equal(30, Fixture.Credit(scope.Path), "fresh credit branch");
            }
        }

        private static void ExternalStateHandling()
        {
            using (Scope scope = Scope.Create())
            {
                Check.Equal(-1, Fixture.Assign(30), "mob 1 records credit");
                Fixture.Write(scope.Path, Fixture.DocV2(17, 40)); // 外部合法改动
                Check.Equal(17, Fixture.Assign(100), "external next delivered, not stale 0");
                Check.Equal(18, Fixture.Next(scope.Path), "advanced from the external value");
                Check.Equal(40, Fixture.Credit(scope.Path), "external credit adopted");

                byte[] broken = Encoding.UTF8.GetBytes("{\"v\":2,");
                Fixture.Write(scope.Path, broken);
                Fixture.Refuse(30);
                Check.True(Fixture.BytesEqual(broken, Fixture.Read(scope.Path)), "external corruption preserved");

                File.Delete(scope.Path);
                Check.Equal(0, Fixture.Assign(100), "deletion restarts at 0");
                Check.Equal(1, Fixture.Next(scope.Path), "file recreated with next=1");
            }
        }

        private static void ConcurrentUniquePermutation()
        {
            using (Scope scope = Scope.Create())
            {
                const int workers = 44;
                int[] results = new int[workers];
                bool[] ok = new bool[workers];
                Task[] tasks = new Task[workers];
                for (int i = 0; i < workers; i++)
                {
                    int index = i;
                    tasks[i] = Task.Run(() => { ok[index] = HermesHeadwearCycle.TryAssign(100, out results[index]); });
                }
                Task.WaitAll(tasks);

                HashSet<int> seen = new HashSet<int>();
                for (int i = 0; i < workers; i++)
                {
                    Check.True(ok[i], "concurrent call " + i + " succeeds");
                    Check.True(seen.Add(results[i]), "concurrent code " + results[i] + " handed out twice");
                }
                Check.Equal(HermesHeadwearCycle.ChoiceCount, seen.Count, "all codes handed out exactly once");
                Check.Equal(0, Fixture.Next(scope.Path), "file back to 0 after a full cycle");
                Check.Equal(0, Fixture.Credit(scope.Path), "credit 0");
                Check.Equal("hermes-headwear-cycle.v2.json|hermes-headwear-cycle.v2.json.bak",
                    Fixture.Joined(Fixture.FilesIn(scope.Dir.Path)), "only main + bak on disk");
            }
        }

        private static void DirectoryBlocksWriteRefused()
        {
            using (TempDir dir = new TempDir())
            using (Scope scope = Scope.Create())
            {
                string path = dir.File("hermes-headwear-cycle.v2.json");
                Directory.CreateDirectory(path); // 目标名被目录占用
                scope.SetPath(path);

                Fixture.Refuse(30);
                Check.True(Directory.Exists(path), "occupying directory untouched");
                Check.Equal(string.Empty, Fixture.Joined(Fixture.FilesIn(dir.Path)), "no temp left behind");
            }
        }

        private static void UnwritableParentRefused()
        {
            using (TempDir dir = new TempDir())
            using (Scope scope = Scope.Create())
            {
                string blocker = dir.File("blocker");
                File.WriteAllText(blocker, "user data");
                scope.SetPath(Path.Combine(blocker, "hermes-headwear-cycle.v2.json"));

                Fixture.Refuse(30);
                Check.Equal("user data", File.ReadAllText(blocker), "user file untouched");
                Check.Equal("blocker", Fixture.Joined(Fixture.FilesIn(dir.Path)), "no temp left behind");
            }
        }

        private static void NestedDirectoryCreated()
        {
            using (Scope scope = Scope.Create())
            {
                string nested = scope.Dir.File("nested/deep/hermes-headwear-cycle.v2.json");
                scope.SetPath(nested);
                Check.Equal(-1, Fixture.Assign(30), "first nested call records credit");
                Check.True(File.Exists(nested), "nested parent directory created on first write");
                Check.Equal(0, Fixture.Next(nested), "next 0");
                Check.Equal(30, Fixture.Credit(nested), "credit 30");
            }
        }

        private static void Constants()
        {
            Check.Equal(44, HermesHeadwearCycle.ChoiceCount, "44 fixed codes");
            Check.Equal(-1, HermesHeadwearCycle.ChoiceNone, "none is -1");
            Check.Equal(2, HermesHeadwearCycle.SchemaVersion, "schema2 current");
            Check.Equal(1, HermesHeadwearCycle.LegacySchemaVersion, "schema1 legacy");
            Check.Equal(100, HermesHeadwearCycle.CreditScale, "100 points per assignment");
            Check.Equal(4096L, HermesHeadwearCycle.MaxFileBytes, "4 KiB cap");

            Check.True(HermesHeadwearCycle.TryParseV2(Fixture.DocV2(0, 0), out int low, out int lowCredit, out _),
                "v2 minimum parses");
            Check.Equal(0, low, "next 0");
            Check.Equal(0, lowCredit, "credit 0");
            Check.True(HermesHeadwearCycle.TryParseV2(Fixture.DocV2(43, 99), out int high, out int highCredit, out _),
                "v2 maximum parses");
            Check.Equal(43, high, "next 43");
            Check.Equal(99, highCredit, "credit 99");
            Check.False(HermesHeadwearCycle.TryParseV1(Fixture.DocV2(1, 1), out _, out _), "v2 doc is not valid v1");
            Check.False(HermesHeadwearCycle.TryParseV2(Fixture.DocV1(1), out _, out _, out _), "v1 doc is not valid v2");

            Check.True(HermesHeadwearCycle.TryParseV1(Fixture.DocV1(0), out int legacyLow, out _), "v1 minimum parses");
            Check.Equal(0, legacyLow, "v1 next 0");
            Check.True(HermesHeadwearCycle.TryParseV1(Fixture.DocV1(43), out int legacyHigh, out _), "v1 maximum parses");
            Check.Equal(43, legacyHigh, "v1 next 43");
            Check.False(HermesHeadwearCycle.TryParseV1(Fixture.DocV1(44), out _, out _), "v1 next 44 refused");
        }
    }
}
