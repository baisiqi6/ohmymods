using System;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace KingdomEnhancedMod;

/// <summary>
/// 跨世界坐骑 marker 的共享单写者（#260 C）。
///
/// 既有 <c>CrossWorldMountRuntime.TryWriteMarker(Frame, definition, land)</c> 由 root 以最小 diff
/// 委托到 <see cref="TryWriteMarker"/>：保留 SameOwner 复核、旧数组内容、值类型 entry 回写、
/// cache invalidate 与 bool 返回语义。恢复事务使用 <see cref="TryCommitMarkers"/>：提交前重核
/// raw owner/容器/完整 preimage，只写目标 land，setter 异常后以 readback 区分
/// committed/not committed/unknown；cache invalidation 失败不能把已写入误报成未提交。
///
/// 边界：只操作当前 reign 的 landData；不 GetIsland、不碰 objects、不注册 hook、不周期扫描；
/// 不全局 Clear、不对旧 parent 递归注销。
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
            landData[land] = entry;
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
    /// 恢复事务的一次提交。preimage 必须覆盖整个 landData（逐 land 的 int 序列，null entry 保持 null）；
    /// additions 只追加到目标 land，去重且保留旧顺序。返回 Unknown 时调用方必须保留实体，不重试。
    /// </summary>
    internal static MountIslandMarkerCommitOutcome TryCommitMarkers(
        ulong expectedGlobalPointer,
        CampaignSaveData campaign,
        int reignIndex,
        ulong landDataPointer,
        int land,
        int[][] preimage,
        int[] additions,
        out int[][] postimage,
        out string reason)
    {
        postimage = null;
        reason = string.Empty;
        string invalidationError = null;
        string readError = null;

        Func<int[][]> read = () =>
        {
            try
            {
                if (!SameOwner(campaign, reignIndex, landDataPointer, expectedGlobalPointer)) return null;
                CampaignSaveData current = CampaignSaveData.current;
                CampaignSaveData.ReignInfo reign = current != null ? current.currentReign : null;
                Il2CppSystem.Collections.Generic.List<CampaignSaveData.LandMapData> landData =
                    reign != null ? reign.landData : null;
                if (landData == null || preimage == null || landData.Count != preimage.Length) return null;
                var result = new int[landData.Count][];
                for (int i = 0; i < landData.Count; i++)
                {
                    CampaignSaveData.LandMapData entry = landData[i];
                    result[i] = entry == null ? null : CrossWorldMountRuntime.ToIntArray(entry.steedSpawns);
                }
                return result;
            }
            catch (Exception e)
            {
                readError = "marker read failed: " + e.GetType().Name;
                return null;
            }
        };

        Action<int, int[]> write = (targetLand, values) =>
        {
            if (!SameOwner(campaign, reignIndex, landDataPointer, expectedGlobalPointer))
                throw new InvalidOperationException("owner/container changed before marker write");
            CampaignSaveData current = CampaignSaveData.current;
            CampaignSaveData.ReignInfo reign = current != null ? current.currentReign : null;
            Il2CppSystem.Collections.Generic.List<CampaignSaveData.LandMapData> landData =
                reign != null ? reign.landData : null;
            if (landData == null || targetLand < 0 || targetLand >= landData.Count)
                throw new InvalidOperationException("landData unavailable before marker write");

            CampaignSaveData.LandMapData entry = landData[targetLand];
            entry.steedSpawns = ToTyped(values);
            landData[targetLand] = entry;   // List<T> 值类型元素必须回写
            try
            {
                CrossWorldMountDependencies.InvalidateGrantCache();
            }
            catch (Exception e)
            {
                invalidationError = e.GetType().Name + " " + e.Message;
            }
        };

        MountIslandMarkerCommitOutcome outcome = MountIslandMarkerBatch.Commit(
            preimage, land, additions, read, write, out postimage, out string commitReason);
        reason = !string.IsNullOrEmpty(commitReason) ? commitReason : (readError ?? string.Empty);

        if (!string.IsNullOrEmpty(invalidationError))
        {
            // readback 已决定 committed/not committed/unknown；invalidate 失败只降级查询缓存，
            // 不能把已提交误报成未提交。
            LogError("marker cache invalidate failed after commit path: " + invalidationError);
        }
        if (outcome == MountIslandMarkerCommitOutcome.Unknown)
            LogError("marker commit unknown; retaining entities for diagnostics: " + reason);
        return outcome;
    }

    /// <summary>整个 landData 的 marker 快照（scope 已由调用方捕获；读取失败返回 false）。</summary>
    internal static bool TryReadAllMarkers(
        ulong expectedGlobalPointer,
        CampaignSaveData campaign,
        int reignIndex,
        ulong landDataPointer,
        out int[][] markers,
        out string reason)
    {
        markers = null;
        reason = string.Empty;
        try
        {
            if (!SameOwner(campaign, reignIndex, landDataPointer, expectedGlobalPointer))
            {
                reason = "owner/container changed";
                return false;
            }
            CampaignSaveData current = CampaignSaveData.current;
            CampaignSaveData.ReignInfo reign = current != null ? current.currentReign : null;
            Il2CppSystem.Collections.Generic.List<CampaignSaveData.LandMapData> landData =
                reign != null ? reign.landData : null;
            if (landData == null)
            {
                reason = "landData unavailable";
                return false;
            }
            markers = new int[landData.Count][];
            for (int i = 0; i < landData.Count; i++)
            {
                CampaignSaveData.LandMapData entry = landData[i];
                markers[i] = entry == null ? null : CrossWorldMountRuntime.ToIntArray(entry.steedSpawns);
            }
            return true;
        }
        catch (Exception e)
        {
            reason = "marker read failed: " + e.GetType().Name;
            return false;
        }
    }

    /// <summary>
    /// 稳定 owner 复核（与 root 原 SameOwner 等价）：raw selected slot == 捕获 campaign，
    /// CampaignSaveData.current 同指针，reign 整数一致，当前 landData 容器指针一致。
    /// </summary>
    internal static bool SameOwner(CampaignSaveData campaign, int reignIndex, ulong landDataPointer)
        => SameOwner(campaign, reignIndex, landDataPointer, 0UL);

    internal static bool SameOwner(CampaignSaveData campaign, int reignIndex, ulong landDataPointer,
        ulong expectedGlobalPointer)
    {
        try
        {
            if (campaign == null || reignIndex < 0 || landDataPointer == 0UL) return false;
            if (!ExtensionIslandRuntime.TryResolveRawOwner(out GlobalSaveData global, out _,
                    out CampaignSaveData owner)) return false;
            if (global == null) return false;
            if (expectedGlobalPointer != 0UL && PointerOf(global) != expectedGlobalPointer) return false;
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
