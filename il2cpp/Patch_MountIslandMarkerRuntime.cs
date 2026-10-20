using System;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace KingdomEnhancedMod;

/// <summary>
/// 跨世界坐骑 marker 的共享单写者（#260 C）。仅保留产品实际使用的 fresh/InjectOnly
/// 写路径：SameOwner 复核、旧数组内容、值类型 entry 回写、cache invalidate 与 bool 返回语义。
///
/// 边界：只操作当前 reign 的 landData；不 GetIsland、不碰 objects、不注册 hook、不周期扫描；
/// 不全局 Clear、不对旧 parent 递归注销。旧空岛 recovery/补发/regrant 路径不在产品中接通。
/// </summary>
internal static class MountIslandMarkerRuntime
{
    private const string LogPrefix = "[MountIslandRecovery]";

    /// <summary>既有 fresh/InjectOnly 写路径：语义与 root 原 private TryWriteMarker 一致。</summary>
    internal static bool TryWriteMarker(
        CampaignSaveData campaign,
        int reignIndex,
        ulong landDataPointer,
        CrossWorldMountDefinition definition,
        int land,
        out string reason)
    {
        reason = string.Empty;
        try
        {
            if (!SameOwner(campaign, reignIndex, landDataPointer))
            {
                reason = "owner/container changed";
                return false;
            }
            if (definition == null)
            {
                reason = "definition missing";
                return false;
            }

            CampaignSaveData current = CampaignSaveData.current;
            CampaignSaveData.ReignInfo reign = current != null ? current.currentReign : null;
            Il2CppSystem.Collections.Generic.List<CampaignSaveData.LandMapData> landData =
                reign != null ? reign.landData : null;
            if (landData == null || land < 0 || land >= landData.Count)
            {
                reason = "landData unavailable";
                return false;
            }

            CampaignSaveData.LandMapData entry = landData[land];
            int[] next = CrossWorldMountPolicy.AppendMarker(
                CrossWorldMountRuntime.ToIntArray(entry.steedSpawns), definition.SteedTypeId);
            entry.steedSpawns = ToTyped(next);
            MountIslandLandMapList.Set(landData, land, entry);
            CrossWorldMountDependencies.InvalidateGrantCache();
            reason = "marker written";
            return true;
        }
        catch (Exception e)
        {
            reason = "marker write failed: " + e.GetType().Name + " " + e.Message;
            LogError(reason);
            return false;
        }
    }

    /// <summary>
    /// 稳定 owner 复核：raw selected slot == 捕获 campaign，CampaignSaveData.current 同指针，
    /// reign 整数一致，当前 landData 容器指针一致。
    /// </summary>
    private static bool SameOwner(CampaignSaveData campaign, int reignIndex, ulong landDataPointer)
    {
        try
        {
            if (campaign == null || reignIndex < 0 || landDataPointer == 0UL) return false;
            if (!ExtensionIslandRuntime.TryResolveRawOwner(out GlobalSaveData global, out _,
                    out CampaignSaveData owner)) return false;
            if (global == null) return false;
            if (owner == null || PointerOf(owner) != PointerOf(campaign)) return false;
            CampaignSaveData current = CampaignSaveData.current;
            if (current == null || PointerOf(current) != PointerOf(campaign)) return false;
            if (current.reign != reignIndex) return false;
            CampaignSaveData.ReignInfo reign = current.currentReign;
            if (reign == null || reign.landData == null) return false;
            if (CrossWorldMountDependencies.PointerOfList(reign.landData) != landDataPointer) return false;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static Il2CppStructArray<SteedType> ToTyped(int[] values)
    {
        int length = values != null ? values.Length : 0;
        var typed = new SteedType[length];
        for (int i = 0; i < length; i++) typed[i] = (SteedType)values[i];
        return typed;   // Il2CppStructArray<SteedType> 隐式转换
    }

    private static ulong PointerOf(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase value)
    {
        try { return value != null ? (ulong)value.Pointer.ToInt64() : 0UL; }
        catch (Exception) { return 0UL; }
    }

    private static void LogError(string message)
    {
        try { KingdomEnhancedPlugin.Instance?.LogSource.LogError(LogPrefix + " " + message); }
        catch (Exception) { }
    }
}

/// <summary>
/// LandMapData 是 native 值类型、CLR class wrapper。泛型 List 的参数封送以 CLR IsValueType
/// 判定，会把 box header 误作 payload；IList 的 native Object bridge 负责类型检查与解箱。
/// 仅服务本类型的 marker 写回/默认补槽，不修改全局泛型或其他容器。
/// </summary>
internal static class MountIslandLandMapList
{
    internal static void Set(
        Il2CppSystem.Collections.Generic.List<CampaignSaveData.LandMapData> list,
        int index, CampaignSaveData.LandMapData entry)
    {
        try
        {
            list.Cast<Il2CppSystem.Collections.IList>()[index] = entry;
        }
        finally
        {
            GC.KeepAlive(entry);
            GC.KeepAlive(list);
        }
    }

    internal static void Add(
        Il2CppSystem.Collections.Generic.List<CampaignSaveData.LandMapData> list,
        CampaignSaveData.LandMapData entry)
    {
        try
        {
            list.Cast<Il2CppSystem.Collections.IList>().Add(entry);
        }
        finally
        {
            GC.KeepAlive(entry);
            GC.KeepAlive(list);
        }
    }
}
