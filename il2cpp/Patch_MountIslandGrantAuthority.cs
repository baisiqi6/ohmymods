using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace KingdomEnhancedMod;

/// <summary>
/// 跨世界坐骑 new-grant 的**权威证据快照**（issue-200 双岛 private proposal）。
///
/// 边界（root 审查前不得进入 tracked 主线）：
/// - 只读 raw <c>CampaignSaveData._islands</c> 与 current reign landData；**不调用 GetIsland**、
///   不写 intent key、不注册 hook、不做周期扫描（生成入口/菜单 eligibility 入口 finite batch）。
/// - **保存并重核影响 newgrant 授权的事实**（root R1/R2）：
///   · 16 定义全 campaign receipt bool（record 三个 string 各读一次后按 16 定义匹配）；
///   · 逐 `_islands` 槽 ptr/count 指纹；
///   · 逐 landData entry 的 steedSpawns 实际 int 序列（非仅 ptr/length）。
///   `MatchesCurrent` 重算上述全部事实并逐项比较：同 objects list/count 但换 record 或改 receipt 字符串、
///   同 marker ptr/length 但改元素 → 一律 false。
/// - **owner scope**：扫描前/后均重核 Global.loaded 指针 + raw selected index（
///   ExtensionIslandRuntime.TryResolveRawOwner，不依赖 GetCurrentCampaign 的 fallback）+ 捕获时的
///   Campaign.CurrentLand + campaign/reign/landData/_islands 四容器身份；菜单/切档/换容器 → false。
/// - 语义区分：null slot / 无 objects = 无 record（证据缺失，非异常）；读取异常 = unknown
///   （拒 newgrant；本岛已证的 InjectOnly 恢复不受影响，且不静默阴性）。
/// </summary>
internal sealed class MountIslandGrantAuthority
{
    internal sealed class Snapshot
    {
        private readonly ulong _globalPointer;
        private readonly int _selectedIndex;
        private readonly int _land;
        private readonly ulong _campaignPointer;
        private readonly int _reignIndex;
        private readonly ulong _landDataPointer;
        private readonly ulong _islandsPointer;
        private readonly Dictionary<string, bool> _receiptByDefinition;
        private readonly MountIslandCellFingerprint[] _cells;
        private readonly MountIslandLandFingerprint[] _lands;
        private readonly int[][] _landMarkers;

        internal Snapshot(ulong globalPointer, int selectedIndex, int land,
            ulong campaignPointer, int reignIndex, ulong landDataPointer, ulong islandsPointer,
            Dictionary<string, bool> receiptByDefinition,
            MountIslandCellFingerprint[] cells, MountIslandLandFingerprint[] lands, int[][] landMarkers)
        {
            _globalPointer = globalPointer;
            _selectedIndex = selectedIndex;
            _land = land;
            _campaignPointer = campaignPointer;
            _reignIndex = reignIndex;
            _landDataPointer = landDataPointer;
            _islandsPointer = islandsPointer;
            _receiptByDefinition = receiptByDefinition;
            _cells = cells;
            _lands = lands;
            _landMarkers = landMarkers;
        }

        /// <summary>本战役任一 _islands 槽的 objects 中存在本定义的获取设施回执（捕获时事实）。</summary>
        internal bool ReceiptInCampaign(CrossWorldMountDefinition definition)
            => definition != null && _receiptByDefinition.TryGetValue(definition.Id, out bool found) && found;

        /// <summary>
        /// 快照与**当前实时**状态一致：owner scope（Global.loaded/raw selected/CurrentLand/四容器）
        /// + receipt 事实集合 + 逐槽指纹 + 逐 land marker int 序列。任一读取异常/变化 → false。
        /// </summary>
        internal bool MatchesCurrent(CampaignSaveData campaign)
        {
            try
            {
                if (campaign == null) return false;
                if (!ExtensionIslandRuntime.TryResolveRawOwner(out GlobalSaveData global, out int selected, out CampaignSaveData owner))
                    return false;
                if (PointerOf(global) != _globalPointer || selected != _selectedIndex) return false;
                if (owner == null || PointerOf(owner) != _campaignPointer) return false;
                if (PointerOf(campaign) != _campaignPointer) return false;
                if (campaign.CurrentLand != _land) return false;
                CampaignSaveData live = CampaignSaveData.current;
                if (live == null || PointerOf(live) != _campaignPointer) return false;

                CampaignSaveData.ReignInfo reign = campaign.currentReign;
                Il2CppSystem.Collections.Generic.List<CampaignSaveData.LandMapData> landData =
                    reign != null ? reign.landData : null;
                Il2CppSystem.Collections.Generic.List<IslandSaveData> islands = campaign._islands;
                if (landData == null || islands == null) return false;
                if (!MountIslandSnapshotIdentity.Matches(
                        _campaignPointer, _reignIndex, _landDataPointer, _islandsPointer,
                        PointerOf(campaign), SafeReign(campaign),
                        CrossWorldMountDependencies.PointerOfList(landData), PointerOf(islands)))
                    return false;

                // R1：重算授权事实（receipt bool 集合 + 单元指纹 + marker 元素内容）。
                Dictionary<string, bool> receiptsAfter = ScanReceiptFacts(islands);
                if (receiptsAfter == null) return false;
                if (!MountIslandGrantFacts.SameReceiptFacts(_receiptByDefinition, receiptsAfter)) return false;
                if (!MountIslandCellStability.Matches(_cells, CaptureCells(islands), _lands, CaptureLands(landData))) return false;
                int[][] markersAfter = CaptureMarkerContents(landData);
                if (markersAfter == null || markersAfter.Length != _landMarkers.Length) return false;
                for (int i = 0; i < markersAfter.Length; i++)
                {
                    if (!MountIslandGrantFacts.SameIntSequence(_landMarkers[i], markersAfter[i])) return false;
                }
                return true;
            }
            catch (Exception e)
            {
                LogOnce("owner-verify-" + e.GetType().Name, "snapshot owner verify failed: " + e.Message);
                return false;
            }
        }

        /// <summary>owner scope + 调用方容器必须是快照捕获的同一容器。</summary>
        internal bool MatchesOwner(CampaignSaveData campaign, int reignIndex, ulong landDataPointer)
        {
            try
            {
                if (campaign == null || _reignIndex < 0 || reignIndex != _reignIndex) return false;
                return _landDataPointer == landDataPointer && MatchesCurrent(campaign);
            }
            catch (Exception) { return false; }
        }
    }

    private const string LogPrefix = "[MountIslandAuthority]";
    private static readonly HashSet<string> LoggedKeys = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// 采集一次 campaign 级快照：捕获前记录 owner scope（Global/selected/land）+ 四容器身份 +
    /// receipt 事实 + 单元指纹 + marker 内容；**再**重核全部；任何异常/并发变化 → false（unknown）。
    /// </summary>
    internal static bool TryCapture(
        CampaignSaveData campaign,
        CampaignSaveData.ReignInfo reign,
        Il2CppSystem.Collections.Generic.List<CampaignSaveData.LandMapData> landData,
        out Snapshot snapshot)
    {
        snapshot = null;
        try
        {
            if (campaign == null || reign == null || landData == null) return false;
            Il2CppSystem.Collections.Generic.List<IslandSaveData> islands = campaign._islands;
            if (islands == null)
            {
                LogOnce("islands-null", "campaign _islands list unavailable; campaign evidence unknown (new grants closed)");
                return false;
            }
            if (!ExtensionIslandRuntime.TryResolveRawOwner(out GlobalSaveData global, out int selected, out CampaignSaveData owner))
            {
                LogOnce("owner-unresolved", "raw owner selection not provable; campaign evidence unknown (new grants closed)");
                return false;
            }

            ulong globalPointer = PointerOf(global);
            int land = campaign.CurrentLand;
            ulong campaignPointer = PointerOf(campaign);
            int reignIndex = SafeReign(campaign);
            ulong landDataPointer = CrossWorldMountDependencies.PointerOfList(landData);
            ulong islandsPointer = PointerOf(islands);
            if (globalPointer == 0UL || selected < 0 || land < 0 || campaignPointer == 0UL
                || reignIndex < 0 || landDataPointer == 0UL || islandsPointer == 0UL
                || owner == null || PointerOf(owner) != campaignPointer)
            {
                LogOnce("identity-missing", "snapshot identity incomplete before scan; campaign evidence unknown");
                return false;
            }

            Dictionary<string, bool> receiptFacts = ScanReceiptFacts(islands);
            if (receiptFacts == null) return false;
            MountIslandCellFingerprint[] cellsBefore = CaptureCells(islands);
            MountIslandLandFingerprint[] landsBefore = CaptureLands(landData);
            int[][] markersBefore = CaptureMarkerContents(landData);
            if (markersBefore == null) return false;

            // 扫描后重核全部：owner scope + 容器身份 + receipt 事实 + 指纹 + marker 内容。
            CampaignSaveData.ReignInfo reignNow = campaign.currentReign;
            Il2CppSystem.Collections.Generic.List<CampaignSaveData.LandMapData> landDataNow =
                reignNow != null ? reignNow.landData : null;
            Il2CppSystem.Collections.Generic.List<IslandSaveData> islandsNow = campaign._islands;
            if (landDataNow == null || islandsNow == null) return false;
            if (!ExtensionIslandRuntime.TryResolveRawOwner(out GlobalSaveData globalNow, out int selectedNow, out CampaignSaveData ownerNow)) return false;
            Dictionary<string, bool> receiptsAfter = ScanReceiptFacts(islandsNow);
            int[][] markersAfter = CaptureMarkerContents(landDataNow);
            if (receiptsAfter == null || markersAfter == null) return false;

            bool stable = MountIslandGrantFacts.AllowsGrantPublish(
                    MountIslandSnapshotIdentity.MatchesOwnerScope(
                        globalPointer, selected, land, campaignPointer, reignIndex, landDataPointer, islandsPointer,
                        PointerOf(globalNow), selectedNow, campaign.CurrentLand,
                        PointerOf(campaign), SafeReign(campaign),
                        CrossWorldMountDependencies.PointerOfList(landDataNow), PointerOf(islandsNow)),
                    ownerNow != null && PointerOf(ownerNow) == campaignPointer);
            if (!stable
                || !MountIslandGrantFacts.SameReceiptFacts(receiptFacts, receiptsAfter)
                || !MountIslandCellStability.Matches(cellsBefore, CaptureCells(islandsNow), landsBefore, CaptureLands(landDataNow))
                || markersAfter.Length != markersBefore.Length)
            {
                LogOnce("identity-shift", "campaign identity/facts changed during scan; snapshot rejected (new grants closed)");
                return false;
            }
            for (int i = 0; i < markersAfter.Length; i++)
            {
                if (!MountIslandGrantFacts.SameIntSequence(markersBefore[i], markersAfter[i]))
                {
                    LogOnce("identity-shift-marker-" + i, "marker content changed during scan; snapshot rejected");
                    return false;
                }
            }

            snapshot = new Snapshot(globalPointer, selected, land, campaignPointer, reignIndex, landDataPointer,
                islandsPointer, receiptFacts, cellsBefore, landsBefore, markersBefore);
            LogOnce("captured-" + islands.Count, "campaign grant snapshot captured across " + islands.Count
                + " island slots (" + receiptFacts.Count + " definitions)");
            return true;
        }
        catch (Exception e)
        {
            LogOnce("capture-" + e.GetType().Name, "campaign grant snapshot read failed: "
                + e.GetType().Name + " " + e.Message);
            snapshot = null;
            return false;
        }
    }

    /// <summary>
    /// 16 定义 campaign-wide receipt 事实扫描：每条 record 的三个 string **各读一次**，再按 16 定义匹配
    /// （避免逐定义反复 interop 读取）。异常 → null（unknown）。
    /// </summary>
    private static Dictionary<string, bool> ScanReceiptFacts(Il2CppSystem.Collections.Generic.List<IslandSaveData> islands)
    {
        var found = new Dictionary<string, bool>(StringComparer.Ordinal);
        CrossWorldMountDefinition[] definitions = CrossWorldMountCatalog.Definitions;
        for (int d = 0; d < definitions.Length; d++) found[definitions[d].Id] = false;

        int slots = islands.Count;
        for (int i = 0; i < slots; i++)
        {
            IslandSaveData island = islands[i];
            if (island == null) continue;              // null slot：无 record（非异常）
            Il2CppSystem.Collections.Generic.List<IslandSaveData.ObjectData> objects = island.objects;
            if (objects == null) continue;             // 无 objects record：证据缺失（非异常）
            int count = objects.Count;
            for (int j = 0; j < count; j++)
            {
                IslandSaveData.ObjectData record = objects[j];
                if (record == null) continue;          // 非 nullish 读取失败会抛异常 → 整体 unknown
                string prefabPath = record.prefabPath; // 三个 string 各读一次
                string name = record.name;
                string hierarchyPath = record.hierarchyPath;
                for (int d = 0; d < definitions.Length; d++)
                {
                    CrossWorldMountDefinition definition = definitions[d];
                    if (found[definition.Id]) continue;
                    if (MatchesReceipt(definition, prefabPath, name, hierarchyPath)) found[definition.Id] = true;
                }
            }
        }
        return found;
    }

    private static MountIslandCellFingerprint[] CaptureCells(
        Il2CppSystem.Collections.Generic.List<IslandSaveData> islands)
    {
        int slots = islands.Count;
        var cells = new MountIslandCellFingerprint[slots];
        for (int i = 0; i < slots; i++)
        {
            IslandSaveData island = islands[i];
            if (island == null)
            {
                cells[i] = new MountIslandCellFingerprint(0UL, 0UL, -1);
                continue;
            }
            Il2CppSystem.Collections.Generic.List<IslandSaveData.ObjectData> objects = island.objects;
            cells[i] = new MountIslandCellFingerprint(
                PointerOf(island), objects != null ? PointerOf(objects) : 0UL, objects != null ? objects.Count : -1);
        }
        return cells;
    }

    private static MountIslandLandFingerprint[] CaptureLands(
        Il2CppSystem.Collections.Generic.List<CampaignSaveData.LandMapData> landData)
    {
        int count = landData.Count;
        var lands = new MountIslandLandFingerprint[count];
        for (int i = 0; i < count; i++)
        {
            CampaignSaveData.LandMapData entry = landData[i];
            if (entry == null)
            {
                lands[i] = new MountIslandLandFingerprint(0UL, -1);
                continue;
            }
            Il2CppStructArray<SteedType> marker = entry.steedSpawns;   // 绑数组引用（entry wrapper 不作证据）
            lands[i] = new MountIslandLandFingerprint(
                marker != null ? PointerOf(marker) : 0UL, marker != null ? marker.Length : -1);
        }
        return lands;
    }

    /// <summary>逐 landData entry 的 steedSpawns 实际 int 序列（元素内容，非仅 ptr/length）。</summary>
    private static int[][] CaptureMarkerContents(
        Il2CppSystem.Collections.Generic.List<CampaignSaveData.LandMapData> landData)
    {
        int count = landData.Count;
        var contents = new int[count][];
        for (int i = 0; i < count; i++)
        {
            CampaignSaveData.LandMapData entry = landData[i];
            contents[i] = entry == null ? null : CrossWorldMountRuntime.ToIntArray(entry.steedSpawns);
        }
        return contents;
    }

    /// <summary>
    /// 单岛 receipt 读取（raw _islands 只读，不 GetIsland）：返回 false = 读取异常/容器不可读（unknown）；
    /// 返回 true 时 <paramref name="found"/> 为"该槽 objects 中确有本定义回执"；null slot/无 objects 为无回执。
    /// </summary>
    internal static bool TryReadIslandReceipt(
        CampaignSaveData campaign, int land, CrossWorldMountDefinition definition, out bool found)
    {
        found = false;
        try
        {
            if (campaign == null || definition == null || land < 0) return false;
            Il2CppSystem.Collections.Generic.List<IslandSaveData> islands = campaign._islands;
            if (islands == null) return false;
            if (land >= islands.Count) return true;    // 无 slot = 无 record
            IslandSaveData island = islands[land];
            if (island == null) return true;           // null slot = 无 record
            Il2CppSystem.Collections.Generic.List<IslandSaveData.ObjectData> objects = island.objects;
            if (objects == null) return true;          // 无 objects record
            int count = objects.Count;
            for (int i = 0; i < count; i++)
            {
                IslandSaveData.ObjectData record = objects[i];
                if (record == null) continue;
                if (MatchesReceipt(definition, record.prefabPath, record.name, record.hierarchyPath))
                {
                    found = true;
                    return true;
                }
            }
            return true;
        }
        catch (Exception e)
        {
            LogOnce("slot-" + land + "-" + e.GetType().Name, "island receipt slot read failed (land " + land
                + "): " + e.GetType().Name + " " + e.Message);
            return false;
        }
    }

    /// <summary>
    /// raw <c>_islands</c> 槽的**生成历史正证**读取（只读，不 GetIsland）：
    /// - <paramref name="slotPresent"/>：槽存在且对象非 null；
    /// - 正证：objects 非空 **或** savedWithRevisions.Count &gt; 0 **或** 有限且 &gt; 0 的
    ///   playTimeDays/lastPlayedTimeDays **或** 本岛已有 markers（调用方读自 landData）；
    /// - null slot / 无 objects / 零值 = **不是正证**（也不能反推"从未生成"）；
    /// - NaN/Infinite/负 double 或任何读取异常 = **unknown**（返回 false；不做 0 假设、不静默阴性）。
    ///   （不扩展 lastPlayedReign 等未证字段；升级需真实责任证据。）
    /// </summary>
    internal static bool TryReadIslandGenerationHistory(
        CampaignSaveData campaign, int land, bool markersKnown, bool hasMarkers,
        out bool slotPresent, out bool generatedHistory)
    {
        slotPresent = false;
        generatedHistory = false;
        try
        {
            if (campaign == null || land < 0 || !markersKnown) return false;
            Il2CppSystem.Collections.Generic.List<IslandSaveData> islands = campaign._islands;
            if (islands == null) return false;
            if (land >= islands.Count) { generatedHistory = hasMarkers; return true; }   // 无槽：markers 可单独作正证
            IslandSaveData island = islands[land];
            if (island == null) { generatedHistory = hasMarkers; return true; }          // null slot 同上
            slotPresent = true;

            Il2CppSystem.Collections.Generic.List<IslandSaveData.ObjectData> objects = island.objects;
            if (objects != null && objects.Count > 0) { generatedHistory = true; return true; }

            Il2CppSystem.Collections.Generic.List<int> revisions = island.savedWithRevisions;
            if (revisions != null && revisions.Count > 0) { generatedHistory = true; return true; }

            double play = island.playTimeDays;
            if (double.IsNaN(play) || double.IsInfinity(play) || play < 0d) return false;   // 读错 → unknown
            double last = island.lastPlayedTimeDays;
            if (double.IsNaN(last) || double.IsInfinity(last) || last < 0d) return false;
            generatedHistory = play > 0d || last > 0d || hasMarkers;
            return true;
        }
        catch (Exception e)
        {
            LogOnce("history-" + land + "-" + e.GetType().Name,
                "island generation history read failed (land " + land + "): " + e.Message);
            return false;
        }
    }

    /// <summary>
    /// ObjectData 回执匹配 body（语义同原 IslandHasReceipt 抽取：prefabPath/name/hierarchyPath 三段任一
    /// 精确指向本定义设施）。字符串由调用方各读一次后传入（避免逐定义重复 interop 读取）。
    /// </summary>
    internal static bool MatchesReceipt(CrossWorldMountDefinition definition,
        string prefabPath, string name, string hierarchyPath)
    {
        if (definition == null) return false;
        return definition.ReceiptMatches(prefabPath)
            || definition.ReceiptMatches(name)
            || definition.ReceiptMatches(hierarchyPath);
    }

    private static ulong PointerOf(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase value)
    {
        try { return value != null ? (ulong)value.Pointer.ToInt64() : 0UL; }
        catch (Exception) { return 0UL; }
    }

    private static int SafeReign(CampaignSaveData campaign)
    {
        try { return campaign != null ? campaign.reign : -1; }
        catch (Exception) { return -1; }
    }

    private static void LogOnce(string key, string message)
    {
        try
        {
            if (!LoggedKeys.Add(key)) return;
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(LogPrefix + " " + message);
        }
        catch (Exception) { }
    }
}
