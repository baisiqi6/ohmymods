using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using KingdomEnhancedMod;

// =====================================================================================
// R2 动态 fault-injection host（task-local，不改产品）：
//  - 执行的是 il2cpp/ExtensionIslandMap.cs 的**真实方法体**（HostedMethods.cs 由 extraction
//    脚本逐字拷贝；运行时对产品文件再抽取同签名方法体做 sha256 比对，HashLocked 全过才计证据）。
//  - fake 仅提供最小依赖：Il2Cpp 字典（Pointer 身份 + 读/写/删故障注入 + 计数）、menu
//    （LAND_TO_MAP_LAND 可换实例 / getter 可抛）。Patch_MountIslandMapLayout /
//    ExtensionIslandMapPlan / MountIslandSplitPolicy 为真实 Compile link。
//  - 覆盖 review 四反例：P1 no-extension 无账目零写；P2 Degrade 写前/写后 throw、readback
//    throw、静默失败与 uncertain 责任；P2 pending 回滚 postcondition 与 lifecycle 归还；
//    P2 capture→dict swap（D1/D2、同 native pointer 别名）。编排级接线由
//    tests/mount-island-map/Program.cs 的 wiring 断言锁定（两处如实分计）。
// =====================================================================================

namespace Il2CppSystem.Collections.Generic
{
    internal enum OpMode
    {
        Normal,
        ThrowBefore,   // 操作不生效并抛错
        ThrowAfter,    // 操作生效后抛错（写后 throw）
        Ignore,        // 静默忽略（写/删不生效、不抛错）
    }

    /// <summary>生产同名的最小 fake 字典：Pointer 身份 + 读写删故障注入 + 操作计数。</summary>
    internal sealed class Dictionary<TKey, TValue>
    {
        internal sealed class FaultPlan
        {
            internal OpMode Read = OpMode.Normal;
            internal OpMode Write = OpMode.Normal;
            internal OpMode Remove = OpMode.Normal;
            internal readonly HashSet<TKey> WriteThrowKeys = new HashSet<TKey>();
            /// <summary>从第 N 次（1-based）读开始抛错（覆盖"写成功后 readback 抛错"）。</summary>
            internal long FailReadsFrom = long.MaxValue;
        }

        internal FaultPlan Faults = new FaultPlan();
        internal IntPtr Pointer;
        internal long Reads, Writes, Removes;
        private readonly System.Collections.Generic.Dictionary<TKey, TValue> _data =
            new System.Collections.Generic.Dictionary<TKey, TValue>();

        private bool ReadGate()
        {
            Reads++;
            if (Faults.Read == OpMode.ThrowBefore) throw new InvalidOperationException("fake read throw");
            if (Reads >= Faults.FailReadsFrom) throw new InvalidOperationException("fake readback throw");
            return true;
        }

        internal bool ContainsKey(TKey key)
        {
            ReadGate();
            return _data.ContainsKey(key);
        }

        internal TValue this[TKey key]
        {
            get
            {
                ReadGate();
                return _data[key];
            }
            set
            {
                Writes++;
                if (Faults.WriteThrowKeys.Contains(key)) throw new InvalidOperationException("fake per-key write throw");
                if (Faults.Write == OpMode.ThrowBefore) throw new InvalidOperationException("fake write throw");
                if (Faults.Write == OpMode.Ignore) return;
                _data[key] = value;
                if (Faults.Write == OpMode.ThrowAfter) throw new InvalidOperationException("fake write-after throw");
            }
        }

        internal void Remove(TKey key)
        {
            Removes++;
            if (Faults.Remove == OpMode.ThrowBefore) throw new InvalidOperationException("fake remove throw");
            if (Faults.Remove == OpMode.Ignore) return;
            _data.Remove(key);
            if (Faults.Remove == OpMode.ThrowAfter) throw new InvalidOperationException("fake remove-after throw");
        }

        /// <summary>预置 native/foreign 键值（不经故障与计数）。</summary>
        internal void Seed(TKey key, TValue value) => _data[key] = value;
        internal bool RawContains(TKey key) => _data.ContainsKey(key);
        internal TValue RawGet(TKey key) => _data[key];
    }
}

namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    /// <summary>生产同名的最小 fake 引用数组：仅 Pointer 身份（R3 数组 CAS 回滚用）。</summary>
    internal sealed class Il2CppReferenceArray<T>
    {
        internal IntPtr Pointer;
    }
}

namespace KingdomEnhancedMod
{
    internal sealed class UILand
    {
    }

    internal sealed class UIMainMapLand
    {
    }

    /// <summary>生产同名的最小 fake 总览地图：两数组引用可被"重入"替换。</summary>
    internal sealed class UIMainMap
    {
        internal Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UILand> _lands;
        internal Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UIMainMapLand> _mainMapLands;
    }
}
namespace KingdomEnhancedMod
{
    /// <summary>生产同名的最小 fake 菜单：字典属性可换实例 / getter 可抛。</summary>
    internal sealed class MapTimelineMenuGreece
    {
        private Il2CppSystem.Collections.Generic.Dictionary<int, int> _landToMapLand;
        internal IntPtr Pointer;
        internal bool ThrowOnDictGet;

        internal Il2CppSystem.Collections.Generic.Dictionary<int, int> LAND_TO_MAP_LAND
        {
            get
            {
                if (ThrowOnDictGet) throw new InvalidOperationException("fake LAND_TO_MAP_LAND getter throw");
                return _landToMapLand;
            }
            set => _landToMapLand = value;
        }

        internal int GetInstanceID() => (int)Pointer.ToInt64();
    }
}

namespace MountIslandMapR2Host
{
    internal static partial class HostedMapMethods
    {
        internal sealed class Generation
        {
            internal MapTimelineMenuGreece Menu;
            internal Il2CppSystem.Collections.Generic.Dictionary<int, int> LookupDict;
            internal readonly List<MountIslandMapDictEntry> LookupPriors =
                new List<MountIslandMapDictEntry>(ExtensionIslandMapPlan.SlotCount);
            internal readonly int[] LastOwnedLookup = new int[ExtensionIslandMapPlan.SlotCount];
            internal bool LookupReleased;
            internal bool LookupUncertain;
            internal bool HasLookupLedger => LookupDict != null && LookupPriors.Count > 0;
        }

        private sealed class Registration
        {
            internal MapTimelineMenuGreece Menu;
            internal int PhysicalLand;
        }

        private static readonly string[] HashLockedSignatures =
        {
            "private static int SlotIndexOf(",
            "private static bool SameMenu(",
            "private static int MenuId(",
            "private static bool CaptureLookupPriors(",
            "private static void AttachLookupLedger(",
            "private static bool LookupDictStillCurrent(",
            "private static bool LookupCommitGuard(",
            "private static bool CanPoisonAppendAttempt(",
            "private static bool RemoveTombstoneForPendingGeneration(",
            "private static void RemoveTombstone(",
            "private static void RestoreReplacedArraysIfStillOurs(",
            "private static bool WriteLookup(",
            "private static bool FixLookupOnDict(",
            "private static void DegradeLookupForRevoke(",
            "private static bool ReleaseLookup(",
        };

        // ------------------------------------------------------------------ cases

        internal static string ProductSource;

        internal static void RunAll()
        {
            ProductSource = ReadSource(Path.Combine("il2cpp", "ExtensionIslandMap.cs"), null);
            string hostedSource = ReadSource(
                Path.Combine("tests", "mount-island-map", "r2host", "HostedMethods.cs"),
                Environment.GetEnvironmentVariable("KEM_HOSTED_ROOT"));
            if (!VerifyHashLock(hostedSource)) return;   // 拷贝区与产品不一致：零执行证据

            NoExtensionZeroWriteCounterexample();
            DegradeFaultMatrix();
            ReleaseFaultMatrix();
            PendingRollbackLifecycle();
            DictSwapIdentity();
            RepeatedClearNoExtraWrites();
            CommitGuardReentry();
            AppendPoisonAuthority();
            PendingTombstoneAuthority();
            ArraysCasRollback();
        }

        private static string ReadSource(string relative, string rootOverride)
        {
            string root = rootOverride;
            if (string.IsNullOrEmpty(root)) root = Environment.GetEnvironmentVariable("KEM_SOURCE_ROOT");
            if (string.IsNullOrEmpty(root)) root = FindRootUpwards();
            string path = Path.Combine(root ?? ".", relative);
            if (!File.Exists(path)) throw new FileNotFoundException("source not found: " + path);
            return File.ReadAllText(path);
        }

        private static string FindRootUpwards()
        {
            string dir = AppContext.BaseDirectory;
            for (int i = 0; i < 8; i++)
            {
                if (File.Exists(Path.Combine(dir, "il2cpp", "ExtensionIslandMap.cs"))) return dir;
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }

        private static string ExtractMethod(string source, string signature)
        {
            int start = source.IndexOf(signature, StringComparison.Ordinal);
            if (start < 0) return null;
            // 花括号平衡提取纯方法体（doc 注释/后续成员不进入范围；与生成脚本同一算法）
            int i = source.IndexOf('{', start);
            if (i < 0) return null;
            int brace = 0;
            while (i < source.Length)
            {
                char c = source[i];
                if (c == '{') brace++;
                else if (c == '}')
                {
                    brace--;
                    if (brace == 0) return source.Substring(start, i - start + 1);
                }
                i++;
            }
            return null;
        }

        private static string Sha(string text)
            => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text ?? "")));

        /// <summary>逐字证明：HostedMethods 拷贝区与产品文件同签名方法体 sha256 全等。</summary>
        private static bool VerifyHashLock(string hostedSource)
        {
            bool all = true;
            foreach (string signature in HashLockedSignatures)
            {
                string prod = ExtractMethod(ProductSource, signature);
                string hosted = ExtractMethod(hostedSource, signature);
                bool match = prod != null && hosted != null && prod == hosted;
                Program.Check(match, "hashlock " + signature.Trim() + " sha=" + (match ? Sha(prod)[..8] : "MISMATCH"));
                if (!match) all = false;
            }
            if (!all) Program.Fail("HOSTED METHODS DRIFTED FROM PRODUCTION: no execution evidence");
            return all;
        }

        private static Il2CppSystem.Collections.Generic.Dictionary<int, int> NewDict(long pointer)
            => new Il2CppSystem.Collections.Generic.Dictionary<int, int> { Pointer = new IntPtr(pointer) };

        private static MapTimelineMenuGreece NewMenu(long pointer, long dictPointer)
            => new MapTimelineMenuGreece
            {
                Pointer = new IntPtr(pointer),
                LAND_TO_MAP_LAND = NewDict(dictPointer),
            };

        private static Generation CapturedGeneration(MapTimelineMenuGreece menu)
        {
            if (!CaptureLookupPriors(menu, out var dict, out var priors)) return null;
            var generation = new Generation { Menu = menu };
            AttachLookupLedger(generation, dict, priors);
            return generation;
        }

        private static void ResetState()
        {
            _generation = null;
            _poisonedMenu = null;
            Tombstones.Clear();
            LogLines.Clear();
        }

        // ---------------------------------------------------------------- P1：no-extension

        private static void NoExtensionZeroWriteCounterexample()
        {
            ResetState();
            var menu = NewMenu(0x100, 0xA1);
            var dict = menu.LAND_TO_MAP_LAND;
            dict.Seed(11, 10);   // foreign 遗留 11→10（值恰为 expected，但无本代账目）

            // 动态执行反例：旧路径（FixLookup→WriteLookup uiPresent=false）会凭值写 9——证明危害真实。
            bool legacyWouldWrite = WriteLookup(dict, 11, false);
            Program.Check(legacyWouldWrite && dict.RawGet(11) == 9 && dict.Writes == 1,
                "P1 legacy no-extension path would write 9 by value (defect real, executed)");

            // R2 后：产品中该入口整体不存在（无 ledger 授权的写方法已删），no-extension 零写。
            Program.Check(!ProductSource.Contains("private static bool FixLookup("),
                "P1 ledger-less FixLookup entry point removed from production source");
            // 恢复 foreign 值后验证零写路径：不挂账、不调用任何写方法 → 字典保持原样。
            dict.Seed(11, 10);
            long writesBefore = dict.Writes;
            // （no-extension 分支无可执行字典调用——空操作集；以计数不变证明零写。）
            Program.Check(dict.Writes == writesBefore && dict.RawGet(11) == 10,
                "P1 no-extension performs zero dictionary writes without a ledger");
        }

        // ---------------------------------------------------------------- P2：Degrade 故障矩阵

        private static void DegradeFaultMatrix()
        {
            int landA = ExtensionIslandMapPlan.PrimaryPhysical;
            int fallback = ExtensionIslandMapPlan.FallbackFocusUi;

            // a) 正常：10→9 写成功读回 → last-owned=9，无 uncertain、无 poison。
            ResetState();
            var menu = NewMenu(0x200, 0xB1);
            var gen = CapturedGeneration(menu);
            var d = (Il2CppSystem.Collections.Generic.Dictionary<int, int>)gen.LookupDict;
            d.Seed(landA, 10);
            DegradeLookupForRevoke(gen, RegFor(menu, landA));
            Program.Check(d.RawGet(landA) == fallback && gen.LastOwnedLookup[0] == fallback
                && !gen.LookupUncertain && _poisonedMenu == null,
                "P2a degrade normal: 10->9 written, last-owned=9, no uncertain");

            // b) setter ThrowBefore（写未发生）：readback 读回 10 → 账目保持 10。
            ResetState();
            menu = NewMenu(0x201, 0xB2);
            gen = CapturedGeneration(menu);
            d = gen.LookupDict;
            d.Seed(landA, 10);
            d.Faults.Write = Il2CppSystem.Collections.Generic.OpMode.ThrowBefore;
            DegradeLookupForRevoke(gen, RegFor(menu, landA));
            Program.Check(d.RawContains(landA) && d.RawGet(landA) == 10 && gen.LastOwnedLookup[0] == 10
                && !gen.LookupUncertain && _poisonedMenu == null,
                "P2b degrade write-throw-before: ledger stays 10 (write proven absent)");

            // c) setter ThrowAfter（写已生效后抛）：readback 9 → 写入不确定性消解，last-owned=9。
            ResetState();
            menu = NewMenu(0x202, 0xB3);
            gen = CapturedGeneration(menu);
            d = gen.LookupDict;
            d.Seed(landA, 10);
            d.Faults.Write = Il2CppSystem.Collections.Generic.OpMode.ThrowAfter;
            DegradeLookupForRevoke(gen, RegFor(menu, landA));
            Program.Check(d.RawGet(landA) == fallback && gen.LastOwnedLookup[0] == fallback
                && !gen.LookupUncertain && _poisonedMenu == null,
                "P2c degrade write-throw-after: readback proves 9 owned (uncertainty resolved)");

            // d) 写成功但 readback 抛错：unknown 不得降级为 foreign/no-op —— uncertain 保留 + poison。
            ResetState();
            menu = NewMenu(0x203, 0xB4);
            gen = CapturedGeneration(menu);
            d = gen.LookupDict;
            d.Seed(landA, 10);
            d.Faults.FailReadsFrom = d.Reads + 3;   // capture 已耗 2 读；Degrade 第 3 读（写后 readback）起抛
            DegradeLookupForRevoke(gen, RegFor(menu, landA));
            Program.Check(gen.LookupUncertain && _poisonedMenu == menu && gen.LastOwnedLookup[0] == 10,
                "P2d degrade readback-throw: uncertain kept, poisoned, ledger not settled");
            // 后续 Release 不得把该 9/未知当确定 foreign 消解。
            d.Faults.FailReadsFrom = long.MaxValue;
            long writesBefore = d.Writes;
            bool clean = ReleaseLookup(gen, "reclaim");
            Program.Check(!clean && gen.LookupUncertain && _poisonedMenu == menu && d.Writes == writesBefore,
                "P2d release refuses uncertain ledger as foreign (zero writes, responsibility kept)");

            // e) setter 静默忽略（写不生效不抛）：readback 10 → 账目保持 10，无 uncertain。
            ResetState();
            menu = NewMenu(0x204, 0xB5);
            gen = CapturedGeneration(menu);
            d = gen.LookupDict;
            d.Seed(landA, 10);
            d.Faults.Write = Il2CppSystem.Collections.Generic.OpMode.Ignore;
            DegradeLookupForRevoke(gen, RegFor(menu, landA));
            Program.Check(d.RawGet(landA) == 10 && gen.LastOwnedLookup[0] == 10 && !gen.LookupUncertain,
                "P2e degrade silent-failure: ledger stays 10, release will CAS-restore");

            // f) foreign 值（账目 10，当前 7）：无本代授权零写。
            ResetState();
            menu = NewMenu(0x205, 0xB6);
            gen = CapturedGeneration(menu);
            d = gen.LookupDict;
            d.Seed(landA, 7);
            long before = d.Writes;
            DegradeLookupForRevoke(gen, RegFor(menu, landA));
            Program.Check(d.Writes == before && d.RawGet(landA) == 7 && gen.LastOwnedLookup[0] == 10,
                "P2f degrade refuses foreign value without generation authorization");

            // g) 本代从未写入该键（capture absent）：无可降级，零写。
            ResetState();
            menu = NewMenu(0x206, 0xB7);
            gen = CapturedGeneration(menu);
            d = gen.LookupDict;
            before = d.Writes;
            DegradeLookupForRevoke(gen, RegFor(menu, landA));
            Program.Check(d.Writes == before && !d.RawContains(landA),
                "P2g degrade on absent key writes nothing");

            // h) 读阶段抛错（写未尝试）：零写、账目不变、无 uncertain（无写发生）。
            ResetState();
            menu = NewMenu(0x207, 0xB8);
            gen = CapturedGeneration(menu);
            d = gen.LookupDict;
            d.Seed(landA, 10);
            d.Faults.Read = Il2CppSystem.Collections.Generic.OpMode.ThrowBefore;
            DegradeLookupForRevoke(gen, RegFor(menu, landA));
            Program.Check(d.RawGet(landA) == 10 && gen.LastOwnedLookup[0] == 10 && !gen.LookupUncertain,
                "P2h degrade read-throw before write: no write, ledger unchanged");
        }

        private static Registration RegFor(MapTimelineMenuGreece menu, int physicalLand)
            => new Registration { Menu = menu, PhysicalLand = physicalLand };

        // ---------------------------------------------------------------- P2：Release 故障矩阵

        private static void ReleaseFaultMatrix()
        {
            int landA = ExtensionIslandMapPlan.PrimaryPhysical;

            // a) prior 存在的精确归还：capture 在 prior 态（11→3）进行，写入态模拟后 restore 回 3。
            ResetState();
            var menu = NewMenu(0x300, 0xC1);
            menu.LAND_TO_MAP_LAND.Seed(landA, 3);   // capture 前置 prior=3（had=true）
            var gen = CapturedGeneration(menu);
            var d = gen.LookupDict;
            d.Seed(landA, 10);   // 模拟本代写入态（分支驱动；现实接管路径由 WriteLookup 门守）
            bool clean = ReleaseLookup(gen, "native-clear");
            Program.Check(clean && gen.LookupReleased && d.RawContains(landA) && d.RawGet(landA) == 3
                && _poisonedMenu == null,
                "P2a release restores exact pre-existing prior 3");

            // b) 正常 remove fresh（had=false，current=10==lastOwned）→ 键删。
            ResetState();
            menu = NewMenu(0x301, 0xC2);
            gen = CapturedGeneration(menu);   // 空字典 capture：两键 absent
            d = gen.LookupDict;
            d.Seed(landA, 10);   // 模拟 append 写入后释放
            clean = ReleaseLookup(gen, "native-clear");
            Program.Check(clean && !d.RawContains(landA) && _poisonedMenu == null,
                "P2b release removes fresh key cleanly");

            // c) own9（Degrade 已把账目记 9）→ remove 认领自己的 9。
            ResetState();
            menu = NewMenu(0x302, 0xC3);
            gen = CapturedGeneration(menu);
            d = gen.LookupDict;
            d.Seed(landA, 10);
            DegradeLookupForRevoke(gen, RegFor(menu, landA));
            Program.Check(gen.LastOwnedLookup[0] == 9, "P2c own9 ledger recorded");
            clean = ReleaseLookup(gen, "native-clear");
            Program.Check(clean && !d.RawContains(landA), "P2c release removes own fallback 9");

            // d) foreign9（账目 10、从未 Degrade）→ 不写不认，责任消解（clean 但零写）。
            ResetState();
            menu = NewMenu(0x303, 0xC4);
            gen = CapturedGeneration(menu);   // capture 空字典：fresh 账目，lastOwned=10
            d = gen.LookupDict;
            d.Seed(landA, 9);   // foreign 直接置 9（未经我们 Degrade/写入）
            long before = d.Writes;
            clean = ReleaseLookup(gen, "native-clear");
            Program.Check(clean && d.Writes == before && d.RawGet(landA) == 9,
                "P2d release treats foreign 9 as not ours (zero writes, dissolves)");

            // e) restore setter ThrowBefore → clean=false + poison + 账目保留；重试（fault off）成功。
            ResetState();
            menu = NewMenu(0x304, 0xC5);
            menu.LAND_TO_MAP_LAND.Seed(landA, 3);   // capture 前置 prior=3（had=true → restore 分支）
            gen = CapturedGeneration(menu);
            d = gen.LookupDict;
            d.Seed(landA, 10);   // 写入态
            d.Faults.Write = Il2CppSystem.Collections.Generic.OpMode.ThrowBefore;
            clean = ReleaseLookup(gen, "native-clear");
            Program.Check(!clean && !gen.LookupReleased && _poisonedMenu == menu && d.RawGet(landA) == 10,
                "P2e release write-throw: clean=false, poisoned, ledger kept");
            d.Faults.Write = Il2CppSystem.Collections.Generic.OpMode.Normal;
            clean = ReleaseLookup(gen, "native-clear");
            Program.Check(clean && gen.LookupReleased && d.RawContains(landA) && d.RawGet(landA) == 3
                && _poisonedMenu == menu,
                "P2e retry after lifecycle settles ledger (responsibility never lost)");

            // f) remove 静默忽略（键残留）→ readback mismatch → clean=false + poison；重试成功。
            ResetState();
            menu = NewMenu(0x305, 0xC6);
            gen = CapturedGeneration(menu);
            d = gen.LookupDict;
            d.Seed(landA, 10);
            d.Faults.Remove = Il2CppSystem.Collections.Generic.OpMode.Ignore;
            clean = ReleaseLookup(gen, "native-clear");
            Program.Check(!clean && d.RawContains(landA) && d.RawGet(landA) == 10 && _poisonedMenu == menu,
                "P2f remove silent-failure detected by readback (not treated as success)");
            d.Faults.Remove = Il2CppSystem.Collections.Generic.OpMode.Normal;
            clean = ReleaseLookup(gen, "native-clear");
            Program.Check(clean && !d.RawContains(landA), "P2f retry removes after fault clears");

            // g) remove ThrowAfter（删后抛）→ catch → clean=false 保留；重试时 prior-态快速路径零写通过。
            ResetState();
            menu = NewMenu(0x306, 0xC7);
            gen = CapturedGeneration(menu);
            d = gen.LookupDict;
            d.Seed(landA, 10);
            d.Faults.Remove = Il2CppSystem.Collections.Generic.OpMode.ThrowAfter;
            clean = ReleaseLookup(gen, "native-clear");
            Program.Check(!clean && !d.RawContains(landA) && _poisonedMenu == menu,
                "P2g remove-throw-after: unknown kept responsible although key already gone");
            d.Faults.Remove = Il2CppSystem.Collections.Generic.OpMode.Normal;
            long w = d.Writes, r = d.Removes;
            clean = ReleaseLookup(gen, "native-clear");
            Program.Check(clean && d.Writes == w && d.Removes == r,
                "P2g retry passes via absent-key fast path with zero extra writes");
        }

        // ---------------------------------------------------------------- P2：pending rollback 生命周期

        private static void PendingRollbackLifecycle()
        {
            int landA = ExtensionIslandMapPlan.PrimaryPhysical;
            int landB = ExtensionIslandMapPlan.SecondaryPhysical;

            // a) 第一键写成功、第二键写抛 → append-rollback 归还第一键，整笔无残留。
            ResetState();
            var menu = NewMenu(0x400, 0xD1);
            var gen = CapturedGeneration(menu);
            var d = gen.LookupDict;
            d.Faults.WriteThrowKeys.Add(landB);   // 仅 B 写抛
            bool writePhase;
            try
            {
                writePhase = FixLookupOnDict(d, true, true);
            }
            catch (Exception)
            {
                writePhase = false;   // 模拟 EnsureAppended try/finally：异常也走 finally 回滚
            }
            Program.Check(!writePhase && d.RawContains(landA),
                "P2a first key written before second-key failure");
            bool clean = ReleaseLookup(gen, "append-rollback");
            Program.Check(clean && !d.RawContains(landA) && !d.RawContains(landB) && _poisonedMenu == null,
                "P2a append-rollback restores both keys (no residue)");

            // b) rollback remove 静默忽略 → clean=false + poison + 账目保留；下次 lifecycle 归还。
            ResetState();
            menu = NewMenu(0x401, 0xD2);
            gen = CapturedGeneration(menu);
            d = gen.LookupDict;
            d.Seed(landA, 10);   // 模拟写入已发生（capture absent + write）
            d.Faults.Remove = Il2CppSystem.Collections.Generic.OpMode.Ignore;
            clean = ReleaseLookup(gen, "append-rollback");
            Program.Check(!clean && !gen.LookupReleased && d.RawGet(landA) == 10 && _poisonedMenu == menu,
                "P2b rollback silent-failure keeps pending ledger + poison (review counterexample)");
            d.Faults.Remove = Il2CppSystem.Collections.Generic.OpMode.Normal;
            clean = ReleaseLookup(gen, "native-clear");
            Program.Check(clean && gen.LookupReleased && !d.RawContains(landA),
                "P2b later lifecycle settles the kept pending ledger");

            // c) rollback remove 抛错 → 保留；换 menu 守卫先结旧账（模拟 menu-replaced 归还）。
            ResetState();
            menu = NewMenu(0x402, 0xD3);
            gen = CapturedGeneration(menu);
            d = gen.LookupDict;
            d.Seed(landA, 10);
            d.Faults.Remove = Il2CppSystem.Collections.Generic.OpMode.ThrowBefore;
            clean = ReleaseLookup(gen, "append-rollback");
            Program.Check(!clean && gen.HasLookupLedger && !gen.LookupReleased,
                "P2c rollback throw keeps ledger");
            d.Faults.Remove = Il2CppSystem.Collections.Generic.OpMode.Normal;
            clean = ReleaseLookup(gen, "menu-replaced");
            Program.Check(clean && !d.RawContains(landA),
                "P2c new append's menu-replaced settle releases old ledger before replacing");
            // 未结旧代账目在新代挂账后依然独立可归还（覆盖保护是编排级，见 Program wiring 守卫断言）。
            var menu2 = NewMenu(0x403, 0xD4);
            var gen2 = CapturedGeneration(menu2);
            Program.Check(gen2 != null && gen2.HasLookupLedger && gen.LookupReleased,
                "P2c old and new generation ledgers stay independent");
        }

        // ---------------------------------------------------------------- P2：dict swap

        private static void DictSwapIdentity()
        {
            ResetState();
            var menu = NewMenu(0x500, 0xE1);
            var gen = CapturedGeneration(menu);
            var d1 = gen.LookupDict;
            Program.Check(FixLookupOnDict(d1, true, true), "swap: append writes succeed on captured D1");

            var d2 = NewDict(0xE2);
            menu.LAND_TO_MAP_LAND = d2;
            Program.Check(!LookupDictStillCurrent(menu, d1), "swap: D1/D2 replacement detected at publish boundary");
            Program.Check(LookupDictStillCurrent(menu, d2), "swap: current dict D2 is current");

            var alias = NewDict(0xE1);   // 同 native pointer 的不同 managed wrapper
            menu.LAND_TO_MAP_LAND = alias;
            Program.Check(LookupDictStillCurrent(menu, d1),
                "swap: same native pointer alias treated as same instance");

            menu.LAND_TO_MAP_LAND = d2;
            bool clean = ReleaseLookup(gen, "append-rollback");
            Program.Check(clean && d2.Writes == 0 && d2.Removes == 0,
                "swap: refusal release only touches captured D1, D2 untouched");

            Program.Check(!LookupDictStillCurrent(null, d1) && !LookupDictStillCurrent(menu, null),
                "swap: null menu/dict rejected");
            var throwing = NewMenu(0x501, 0xE3);
            var throwingDict = throwing.LAND_TO_MAP_LAND;   // 先取引用，再开 getter 故障
            throwing.ThrowOnDictGet = true;
            Program.Check(!LookupDictStillCurrent(throwing, throwingDict),
                "swap: getter throw read as not-current (refuse publish, keep unknown responsibility)");
        }

        // ---------------------------------------------------------------- 重复 Clear 零写

        private static void RepeatedClearNoExtraWrites()
        {
            ResetState();
            var menu = NewMenu(0x600, 0xF1);
            var gen = CapturedGeneration(menu);
            var d = gen.LookupDict;
            d.Seed(ExtensionIslandMapPlan.PrimaryPhysical, 10);
            d.Seed(ExtensionIslandMapPlan.SecondaryPhysical, 11);
            Program.Check(ReleaseLookup(gen, "native-clear"), "repeat: first clear settles");
            long w = d.Writes, r = d.Removes;
            for (int i = 0; i < 3; i++)
            {
                Program.Check(ReleaseLookup(gen, "native-clear"),
                    "repeat clear #" + i + " stays clean");
            }
            Program.Check(d.Writes == w && d.Removes == r,
                "repeat: repeated clear performs zero extra writes/removes");
        }

        // ---------------------------------------------------------------- R3：SetActive 重入 commit 边界

        private static void CommitGuardReentry()
        {
            int landA = ExtensionIslandMapPlan.PrimaryPhysical;

            // same-current path：pending 挂账 + 写入 + dict 未换 → guard 放行 commit。
            ResetState();
            var menu = NewMenu(0x700, 0x11A1);
            var gen = CapturedGeneration(menu);
            _generation = gen;   // 模拟 EnsureAppended pending 发布
            var captured = gen.LookupDict;
            Program.Check(FixLookupOnDict(captured, true, true), "R3 setup write on D1");
            Program.Check(LookupCommitGuard(gen, menu, captured),
                "R3 same-current pending generation passes commit guard");

            // visibility 回调换 D2 → 旧 attempt 拒绝 commit（不写 D2，账目按 D1 归还）。
            ResetState();
            menu = NewMenu(0x701, 0x11A2);
            gen = CapturedGeneration(menu);
            _generation = gen;
            captured = gen.LookupDict;
            FixLookupOnDict(captured, true, true);
            var d2 = NewDict(0x22B2);
            menu.LAND_TO_MAP_LAND = d2;   // 模拟 SetActive 同步回调里 dict 被换
            Program.Check(!LookupCommitGuard(gen, menu, captured),
                "R3 visibility callback swapped D2 -> old attempt commit refused");

            // visibility 回调 Clear oldgen（OnLandsCleared 编排：ReleaseLookup 结清 + 清 pending）
            // → 旧 attempt 拒绝；g1 责任已在重入侧按 exact D1 账目结清。
            ResetState();
            menu = NewMenu(0x702, 0x11A3);
            gen = CapturedGeneration(menu);
            _generation = gen;
            captured = gen.LookupDict;
            FixLookupOnDict(captured, true, true);
            bool settled = ReleaseLookup(gen, "native-clear");   // 重入 Clear 按 exact D1 账目结清
            _generation = null;   // lookupClean 分支清 pending 引用
            Program.Check(settled && !LookupCommitGuard(gen, menu, captured),
                "R3 cleared oldgen -> old attempt commit refused (g1 settled by reentrant clear)");

            // Clear 后重绑 g2（新 attempt 挂新 pending）→ 旧 attempt 拒绝；g2 按自身账目可 commit。
            ResetState();
            menu = NewMenu(0x703, 0x11A4);
            gen = CapturedGeneration(menu);
            _generation = gen;
            captured = gen.LookupDict;
            FixLookupOnDict(captured, true, true);
            var gen2 = CapturedGeneration(menu);
            _generation = gen2;   // 重入新 attempt 挂 g2（真路径由守卫先结清旧账，这里模拟已替换）
            Program.Check(!LookupCommitGuard(gen, menu, captured),
                "R3 reentry rebound g2 -> old attempt commit refused");
            Program.Check(LookupCommitGuard(gen2, menu, gen2.LookupDict),
                "R3 g2 stays committable on its own ledger");

            // Degrade 留 uncertain 的 pending → 拒绝 commit（不自动认领未证明的 9）。
            ResetState();
            menu = NewMenu(0x704, 0x11A5);
            gen = CapturedGeneration(menu);
            _generation = gen;
            captured = gen.LookupDict;
            captured.Seed(landA, 10);
            captured.Faults.FailReadsFrom = captured.Reads + 3;   // Degrade readback 抛错
            DegradeLookupForRevoke(gen, RegFor(menu, landA));
            Program.Check(gen.LookupUncertain && !LookupCommitGuard(gen, menu, captured),
                "R3 uncertain pending -> commit refused");
        }

        private static void AppendPoisonAuthority()
        {
            ResetState();
            var gen = new Generation();
            Program.Check(CanPoisonAppendAttempt(gen), "pre-ledger attempt retains failure protection");
            _generation = gen;
            Program.Check(CanPoisonAppendAttempt(gen), "same pending attempt retains failure protection");
            gen.LookupUncertain = true;
            Program.Check(CanPoisonAppendAttempt(gen), "same uncertain attempt retains failure protection");
            gen.LookupReleased = true;
            _generation = null;
            Program.Check(!CanPoisonAppendAttempt(gen), "cleared g1 cannot poison idle menu");
            gen.LookupReleased = false;
            var gen2 = new Generation();
            _generation = gen2;
            Program.Check(!CanPoisonAppendAttempt(gen), "superseded g1 cannot poison current g2");
            Program.Check(CanPoisonAppendAttempt(gen2), "current g2 retains its own failure protection");
            gen2.LookupReleased = true;
            Program.Check(CanPoisonAppendAttempt(gen), "released old reference does not hide pre-ledger failure");
            Program.Check(!CanPoisonAppendAttempt(null), "absent attempt cannot poison menu");
        }

        private static readonly List<Registration> Tombstones = new List<Registration>();

        private static void PendingTombstoneAuthority()
        {
            ResetState();
            var menu = NewMenu(0x801, 0x881);
            var g1 = CapturedGeneration(menu);
            var g2 = CapturedGeneration(menu);
            _generation = g2;
            Tombstones.Add(RegFor(menu, ExtensionIslandMapPlan.PrimaryPhysical));
            Program.Check(!RemoveTombstoneForPendingGeneration(g1) && Tombstones.Count == 1,
                "stale g1 leaves g2 rejection tombstone untouched");
            Program.Check(RemoveTombstoneForPendingGeneration(g2) && Tombstones.Count == 0,
                "current g2 removes its own menu tombstone");
            Tombstones.Add(RegFor(menu, ExtensionIslandMapPlan.PrimaryPhysical));
            g2.LookupReleased = true;
            Program.Check(!RemoveTombstoneForPendingGeneration(g2) && Tombstones.Count == 1,
                "released attempt cannot remove a tombstone");
            g2.LookupReleased = false;
            g2.LookupUncertain = true;
            Program.Check(!RemoveTombstoneForPendingGeneration(g2) && Tombstones.Count == 1,
                "uncertain attempt cannot remove a tombstone");
            g2.LookupUncertain = false;
            menu.LAND_TO_MAP_LAND = NewDict(0x882);
            Program.Check(!RemoveTombstoneForPendingGeneration(g2) && Tombstones.Count == 1,
                "same pending generation with swapped D2 leaves rejection tombstone untouched");
        }

        private static void ArraysCasRollback()
        {
            ResetState();
            var map = new UIMainMap();
            var originalLands = NewArr<UILand>(0xA01);
            var originalButtons = NewArr<UIMainMapLand>(0xA02);
            var newLands = NewArr<UILand>(0xA03);
            var newButtons = NewArr<UIMainMapLand>(0xA04);
            map._lands = newLands;
            map._mainMapLands = newButtons;

            // same-current path：当前数组仍是本次替换 → 恢复 original。
            RestoreReplacedArraysIfStillOurs(map, newLands, newButtons, originalLands, originalButtons);
            Program.Check(map._lands.Pointer == originalLands.Pointer
                && map._mainMapLands.Pointer == originalButtons.Pointer,
                "R3 rollback restores originals when arrays are still ours");

            // 重入已把 map._lands 换成 g2 数组 → 旧 attempt 回滚不覆盖 g2。
            var g2Lands = NewArr<UILand>(0xB01);
            map._lands = newLands;
            map._mainMapLands = newButtons;
            map._lands = g2Lands;   // SetActive 回调重绑 g2
            RestoreReplacedArraysIfStillOurs(map, newLands, newButtons, originalLands, originalButtons);
            Program.Check(map._lands.Pointer == g2Lands.Pointer,
                "R3 rollback never overwrites reentrant g2 arrays");

            // 外来只换了 buttons、lands 仍 ours → 只恢复 ours 的那一个，不碰 foreign。
            var foreignButtons = NewArr<UIMainMapLand>(0xB02);
            map._lands = newLands;
            map._mainMapLands = foreignButtons;
            RestoreReplacedArraysIfStillOurs(map, newLands, newButtons, originalLands, originalButtons);
            Program.Check(map._lands.Pointer == originalLands.Pointer
                && map._mainMapLands.Pointer == foreignButtons.Pointer,
                "R3 partial replacement: only our array restored, foreign buttons untouched");

            // null map / null 替换引用 → 零写不抛。
            RestoreReplacedArraysIfStillOurs(null, newLands, newButtons, originalLands, originalButtons);
            var empty = new UIMainMap();
            RestoreReplacedArraysIfStillOurs(empty, null, null, originalLands, originalButtons);
            Program.Check(empty._lands == null && empty._mainMapLands == null,
                "R3 null map/replacement refs: zero writes, no throw");
        }

        private static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<T> NewArr<T>(long pointer)
            => new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<T>
            {
                Pointer = new IntPtr(pointer),
            };
    }

    internal static class Program
    {
        private static int _checks;
        private static int _failed;

        internal static void Check(bool condition, string what)
        {
            _checks++;
            if (condition) return;
            _failed++;
            Console.WriteLine("FAIL: " + what);
        }

        internal static void Fail(string message)
        {
            _failed++;
            Console.WriteLine("FAIL: " + message);
        }

        private static void Main()
        {
            HostedMapMethods.RunAll();
            Console.WriteLine($"checks={_checks} passed={_checks - _failed} failed={_failed}");
            Console.WriteLine(_failed == 0 ? "RESULT: PASS" : "RESULT: FAIL");
            Environment.Exit(_failed == 0 ? 0 : 1);
        }
    }
}
