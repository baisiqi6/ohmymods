// 自动生成：逐字拷贝 il2cpp/ExtensionIslandMap.cs 的字典生命周期方法体。
// 再生成命令（repo 根）：python3 tests/mount-island-map/r2host/extract-hosted.py
// 运行时由 R2Program 按 sha256 对产品文件再抽取比对（HashLocked 区，提取用同样的
// 花括号平衡算法）——不一致即全部 FAIL，不产生执行证据。不要手改本文件。
using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using KingdomEnhancedMod;

namespace MountIslandMapR2Host
{
    internal static partial class HostedMapMethods
    {
        /// <summary>产品同名字段的 host 挂点（poison/pending 状态由逐字方法体维护）。</summary>
        internal static MapTimelineMenuGreece _poisonedMenu;
        /// <summary>产品同名字段的 host 挂点（pending generation 引用，LookupCommitGuard 用）。</summary>
        internal static Generation _generation;
        internal static readonly List<string> LogLines = new List<string>(8);

private static int SlotIndexOf(int physicalLand)
        {
            if (physicalLand == ExtensionIslandMapPlan.PrimaryPhysical) return 0;
            if (physicalLand == ExtensionIslandMapPlan.SecondaryPhysical) return 1;
            return -1;
        }

private static bool SameMenu(MapTimelineMenuGreece a, MapTimelineMenuGreece b)
        {
            if (a == null || b == null) return false;
            try { return a.Pointer == b.Pointer; } catch (Exception) { return false; }
        }

private static int MenuId(MapTimelineMenuGreece menu)
        {
            try { return menu != null ? menu.GetInstanceID() : 0; }
            catch (Exception) { return 0; }
        }

private static bool CaptureLookupPriors(MapTimelineMenuGreece menu,
            out Il2CppSystem.Collections.Generic.Dictionary<int, int> dict,
            out List<MountIslandMapDictEntry> priors)
        {
            dict = null;
            priors = null;
            try
            {
                if (menu == null) return false;
                dict = menu.LAND_TO_MAP_LAND;
                if (dict == null) return false;
                priors = new List<MountIslandMapDictEntry>(ExtensionIslandMapPlan.SlotCount);
                int[] lands = { ExtensionIslandMapPlan.PrimaryPhysical, ExtensionIslandMapPlan.SecondaryPhysical };
                int[] uis = { ExtensionIslandMapPlan.PrimaryUi, ExtensionIslandMapPlan.SecondaryUi };
                for (int i = 0; i < lands.Length; i++)
                {
                    bool had = dict.ContainsKey(lands[i]);
                    int prior = had ? dict[lands[i]] : 0;
                    priors.Add(MountIslandMapDictTransaction.Capture(lands[i], had, prior, uis[i]));
                }
                return true;
            }
            catch (Exception e)
            {
                LogOnce("lookup-capture", "LAND_TO_MAP_LAND capture failed: " + e.GetType().Name + " " + e.Message);
                return false;
            }
        }

private static void AttachLookupLedger(Generation generation,
            Il2CppSystem.Collections.Generic.Dictionary<int, int> dict, List<MountIslandMapDictEntry> priors)
        {
            if (generation == null || dict == null || priors == null) return;
            generation.LookupDict = dict;
            generation.LookupPriors.AddRange(priors);
            for (int i = 0; i < priors.Count; i++)
            {
                int idx = SlotIndexOf(priors[i].Physical);
                if (idx >= 0)
                {
                    generation.LastOwnedLookup[idx] =
                        ExtensionIslandMapPlan.LookupUiForPhysical(priors[i].Physical, true);
                }
            }
        }

private static bool LookupDictStillCurrent(MapTimelineMenuGreece menu,
            Il2CppSystem.Collections.Generic.Dictionary<int, int> captured)
        {
            if (menu == null || captured == null) return false;
            try
            {
                Il2CppSystem.Collections.Generic.Dictionary<int, int> current = menu.LAND_TO_MAP_LAND;
                return current != null && current.Pointer == captured.Pointer;
            }
            catch (Exception)
            {
                return false;
            }
        }

private static bool LookupCommitGuard(Generation generation, MapTimelineMenuGreece menu,
            Il2CppSystem.Collections.Generic.Dictionary<int, int> capturedDict)
        {
            if (!LookupDictStillCurrent(menu, capturedDict)) return false;
            return ReferenceEquals(_generation, generation)
                && !generation.LookupReleased
                && !generation.LookupUncertain;
        }

private static bool CanPoisonAppendAttempt(Generation generation)
        {
            return generation != null && !generation.LookupReleased
                && (_generation == null || _generation.LookupReleased || ReferenceEquals(_generation, generation));
        }

private static bool RemoveTombstoneForPendingGeneration(Generation generation)
        {
            if (!ReferenceEquals(_generation, generation) || generation.LookupReleased || generation.LookupUncertain)
                return false;
            if (!LookupCommitGuard(generation, generation.Menu, generation.LookupDict)) return false;
            RemoveTombstone(generation.Menu);
            return true;
        }

private static void RemoveTombstone(MapTimelineMenuGreece menu)
        {
            if (menu == null) return;
            for (int i = 0; i < Tombstones.Count; i++)
            {
                Registration keeper = Tombstones[i];
                if (keeper == null || keeper.Menu == null || SameMenu(keeper.Menu, menu))
                {
                    Tombstones.RemoveAt(i--);
                }
            }
        }

private static void RestoreReplacedArraysIfStillOurs(UIMainMap map,
            Il2CppReferenceArray<UILand> newLands, Il2CppReferenceArray<UIMainMapLand> newButtons,
            Il2CppReferenceArray<UILand> originalLands, Il2CppReferenceArray<UIMainMapLand> originalButtons)
        {
            if (map == null) return;
            try
            {
                if (newLands != null && map._lands != null && map._lands.Pointer == newLands.Pointer)
                {
                    map._lands = originalLands;
                }
            }
            catch (Exception) { }
            try
            {
                if (newButtons != null && map._mainMapLands != null && map._mainMapLands.Pointer == newButtons.Pointer)
                {
                    map._mainMapLands = originalButtons;
                }
            }
            catch (Exception) { }
        }

private static bool WriteLookup(Il2CppSystem.Collections.Generic.Dictionary<int, int> dict,
            int physicalLand, bool uiPresent)
        {
            int value = ExtensionIslandMapPlan.LookupUiForPhysical(physicalLand, uiPresent);
            if (value < 0) return false;   // 非扩展键：绝不写（native 0..10/12 交还原生）
            bool present = dict.ContainsKey(physicalLand);
            if (!uiPresent)
            {
                if (!present) return true;   // 无需降级写入（从未登记过）
                // 降级只清**我们 owned 的期望映射**（11→10 / 13→11）；foreign/未知值绝不覆盖。
                int expected = ExtensionIslandMapPlan.LookupUiForPhysical(physicalLand, true);
                int current = dict[physicalLand];
                if (!MountIslandMapDictTransaction.OwnsExpectedValue(current, expected)) return true;
                dict[physicalLand] = value;
                return dict[physicalLand] == value;
            }
            if (present)
            {
                int expected = ExtensionIslandMapPlan.LookupUiForPhysical(physicalLand, true);
                int current = dict[physicalLand];
                if (current != value && !MountIslandMapDictTransaction.OwnsExpectedValue(current, expected))
                {
                    // 已有 11/13 键不是我们的 owned 期望映射（foreign/未知状态）：不可接管，拒绝 append。
                    LogOnce("lookup-foreign-" + physicalLand, "LAND_TO_MAP_LAND key " + physicalLand +
                        " holds non-owned value " + current + "; extension append refused (no takeover)");
                    return false;
                }
            }
            dict[physicalLand] = value;
            return dict[physicalLand] == value;   // 读回校验
        }

private static bool FixLookupOnDict(Il2CppSystem.Collections.Generic.Dictionary<int, int> dict,
            bool primaryUiPresent, bool secondaryUiPresent)
        {
            if (dict == null) return false;
            if (!WriteLookup(dict, ExtensionIslandMapPlan.PrimaryPhysical, primaryUiPresent)) return false;
            return WriteLookup(dict, ExtensionIslandMapPlan.SecondaryPhysical, secondaryUiPresent);
        }

private static void DegradeLookupForRevoke(Generation generation, Registration slot)
        {
            if (generation == null || generation.LookupDict == null || slot == null) return;
            int idx = SlotIndexOf(slot.PhysicalLand);
            if (idx < 0) return;
            int key = slot.PhysicalLand;
            int current;
            try
            {
                if (!generation.LookupDict.ContainsKey(key)) return;   // 本代从未写入该键：无可降级
                current = generation.LookupDict[key];
                if (current == ExtensionIslandMapPlan.FallbackFocusUi) return;   // 已降级幂等（账目已同步）
                if (!MountIslandMapDictTransaction.ShouldDegradeRevoke(current, generation.LastOwnedLookup[idx]))
                {
                    return;   // foreign/漂移值：无本代授权不凭值猜 owned
                }
            }
            catch (Exception e)
            {
                LogOnce("lookup-degrade-read-" + key, "LAND_TO_MAP_LAND degrade read failed (no write attempted, land "
                    + key + "): " + e.GetType().Name + " " + e.Message);
                return;   // 读阶段失败：本次无写发生，账目保持原值
            }
            // 写阶段：pending owned 转换（current → 9）。任何异常都由独立 readback 消解或保留 uncertain。
            try { generation.LookupDict[key] = ExtensionIslandMapPlan.FallbackFocusUi; }
            catch (Exception e)
            {
                LogOnce("lookup-degrade-write-" + key, "LAND_TO_MAP_LAND degrade setter threw (resolving by readback, land "
                    + key + "): " + e.GetType().Name + " " + e.Message);
            }
            int after;
            bool afterKnown;
            try
            {
                after = generation.LookupDict[key];
                afterKnown = true;
            }
            catch (Exception e)
            {
                after = 0;
                afterKnown = false;
                LogOnce("lookup-degrade-readback-" + key, "LAND_TO_MAP_LAND degrade readback failed (uncertain, land "
                    + key + "): " + e.GetType().Name + " " + e.Message);
            }
            if (afterKnown && after == ExtensionIslandMapPlan.FallbackFocusUi)
            {
                generation.LastOwnedLookup[idx] = ExtensionIslandMapPlan.FallbackFocusUi;   // 转换已证明：9 认领本代
                return;
            }
            if (afterKnown && after == current)
            {
                return;   // 写未生效（静默忽略）：账目保持原 owned 值，责任原值等整代归还
            }
            // readback 失败或值漂移到非预期：写入状态不可证明——保留 uncertain 责任并 poison，
            // Release 不得把该键 CAS 失配当确定 foreign 消解（不自动认领未验证的 9）。
            generation.LookupUncertain = true;
            if (MountIslandMapDictTransaction.RequiresPoisonOnRollbackFailure(true)) _poisonedMenu = generation.Menu;
        }

private static bool ReleaseLookup(Generation generation, string reason)
        {
            if (generation == null || !generation.HasLookupLedger) return true;
            if (generation.LookupReleased) return true;   // 重复释放不再写字典
            if (generation.LookupUncertain)
            {
                if (MountIslandMapDictTransaction.RequiresPoisonOnRollbackFailure(true)) _poisonedMenu = generation.Menu;
                LogOnce("lookup-release-uncertain-" + reason + "-" + MenuId(generation.Menu),
                    "LAND_TO_MAP_LAND release refused: degrade ownership unproven, ledger kept and menu poisoned (menu "
                    + MenuId(generation.Menu) + ")");
                return false;
            }
            Il2CppSystem.Collections.Generic.Dictionary<int, int> dict = generation.LookupDict;
            bool clean = true;
            string failure = "";
            for (int i = 0; i < generation.LookupPriors.Count; i++)
            {
                MountIslandMapDictEntry entry = generation.LookupPriors[i];
                int idx = SlotIndexOf(entry.Physical);
                if (idx < 0) continue;
                try
                {
                    bool present = dict.ContainsKey(entry.Physical);
                    int current = present ? dict[entry.Physical] : 0;
                    if (present && current == entry.PriorValue)
                    {
                        continue;   // 已处于 prior 状态（写入从未发生/已被归还）：零写通过
                    }
                    int lastOwned = generation.LastOwnedLookup[idx];
                    if (MountIslandMapDictTransaction.ShouldRestoreRelease(entry, lastOwned, present, current))
                    {
                        dict[entry.Physical] = entry.PriorValue;
                    }
                    else if (MountIslandMapDictTransaction.ShouldRemoveRelease(entry, lastOwned, present, current))
                    {
                        dict.Remove(entry.Physical);
                    }
                    else
                    {
                        continue;   // foreign 改值/已删键：不接管，责任消解
                    }
                    bool afterPresent = dict.ContainsKey(entry.Physical);
                    int afterValue = afterPresent ? dict[entry.Physical] : 0;
                    if (!MountIslandMapDictTransaction.ReleaseReadbackMatches(entry, afterPresent, afterValue))
                    {
                        clean = false;   // setter 静默失败/读回未知：责任未消解
                        failure = "readback key " + entry.Physical;
                    }
                }
                catch (Exception e)
                {
                    clean = false;   // 读写抛错：unknown，责任保留
                    failure = e.GetType().Name + " key " + entry.Physical;
                }
            }
            if (clean)
            {
                generation.LookupReleased = true;
                LogOnce("lookup-release-" + reason + "-" + MenuId(generation.Menu),
                    "LAND_TO_MAP_LAND ledger released (" + reason + ", menu " + MenuId(generation.Menu)
                    + ", dict 0x" + dict.Pointer.ToString("X") + "): keys back to first-capture priors");
            }
            else
            {
                if (MountIslandMapDictTransaction.RequiresPoisonOnRollbackFailure(true)) _poisonedMenu = generation.Menu;
                LogOnce("lookup-release-fail-" + reason + "-" + MenuId(generation.Menu),
                    "LAND_TO_MAP_LAND release kept responsible and menu poisoned (" + reason + ", menu "
                    + MenuId(generation.Menu) + ", " + failure + ")");
            }
            return clean;
        }

        private static void LogOnce(string key, string message)
        {
            LogLines.Add("once[" + key + "] " + message);
        }

        private static void Log(string message)
        {
            LogLines.Add("log " + message);
        }
    }
}
