using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KingdomEnhancedMod;

/// <summary>
/// 旧空坐骑岛恢复（issue #260 C）：同次读档证明 + 增量原生实体恢复。
///
/// 生命周期（当前 2.4 ARM 证据）：
/// - <c>IslandSaveData.TryPopObjectsToScene</c> Prefix 在任何新调用开始时撤销旧 token，只在
///   完整证明普通 Greek 离线 raw owner、物理 land 11/13、真实旧档（playTimeDays 有限 &gt; 0、
///   objects 非空）、Level/seed/GameLayer/holder、markers 与其他岛 receipt 可读且扫描稳定后，
///   捕获单槽快照。
/// - 只有同次 original 正常执行、bool true、且原生已把 objects 清空时，Postfix 才发布 ready。
/// - <c>Managers.OnLevelLoaded(fromSave=true)</c> 正常 Postfix **先取走并清 token**，再复核同一
///   scope/owner/Level/seed/holder/GameLayer/同 scene、全 land markers 与其他槽 receipt 未漂移。
///   网络/挑战/非 Greek/原岛/宫廷/新档/feature off 不进入。
///
/// 恢复只补“旧档本应存在但丢失”的设施：per-definition 状态判定由纯 policy 完成；新建用私有
/// inactive 模板 holder + 独占 inactive staging，调用原生 <c>LevelBlock.CloneObject</c>；全批
/// inactive 创建 → 逐 root false → reparent 准确 GameLayer → 校验 → 同步激活 → 一次 marker
/// 数组提交。无 Update 轮询、无重生成、无 terrain/seed/boundary 写入；marker 未提交才清理本批
/// owned roots（逐 child Persistent DontPersistInstance(false,null) 后 destroy）。
/// </summary>
internal static class MountIslandRecoveryRuntime
{
    private const string LogPrefix = "[MountIslandRecovery]";
    private const string StagingName = "KEM_MountIslandRecoveryStaging";
    private static readonly MountIslandRecoveryTokenSlot Tokens = new MountIslandRecoveryTokenSlot();
    private static readonly HashSet<string> LoggedKeys = new HashSet<string>(StringComparer.Ordinal);

    private sealed class Capture
    {
        internal IslandSaveData Island;
        internal MountIslandRecoveryScope Scope;
        internal MountIslandRecoveryCell[] Cells;
        internal int[][] MarkerPreimage;
        internal bool[] ReceiptThis;
        internal bool[] ReceiptOther;
        internal bool[] ReceiptUnknown;
        internal bool CampaignEvidenceReadable;
        internal int SavedSeed;
        internal int SavedLevelMin;
        internal int SavedLevelMax;
        internal ulong ObjectsPointer;
        internal int ObjectsCount;
    }

    private readonly struct ReceiptFacts
    {
        internal readonly bool[] This;
        internal readonly bool[] Other;
        internal readonly bool[] Unknown;
        internal readonly bool Readable;

        internal ReceiptFacts(bool[] target, bool[] other, bool[] unknown, bool readable)
        {
            This = target;
            Other = other;
            Unknown = unknown;
            Readable = readable;
        }
    }

    private struct LiveInfo
    {
        internal MountIslandRecoveryLiveState State;
        internal bool Complete;
    }

    // ------------------------------------------------------------------ Harmony entry points

    internal static MountIslandRecoveryToken BeforePop(IslandSaveData island, bool runOriginal)
    {
        // 任何新 TryPop 都先撤旧 token（单槽，不累积、不跨 load 复用）。
        Tokens.Clear();
        if (!runOriginal) return null;
        try
        {
            if (!TryBuildContext(island, out Capture capture, out string reason))
            {
                LogOnce("skip-" + reason, "recovery token not captured: " + reason);
                return null;
            }
            MountIslandRecoveryToken token = Tokens.Begin(capture);
            Log("token pending: land=" + capture.Scope.Land + " objects=" + capture.ObjectsCount
                + " defs=" + CrossWorldMountCatalog.Definitions.Length);
            return token;
        }
        catch (Exception e)
        {
            LogError("before pop failed: " + e.GetType().Name + " " + e.Message);
            Tokens.Clear();
            return null;
        }
    }

    internal static void AfterPop(IslandSaveData island, bool result, bool runOriginal,
        MountIslandRecoveryToken token)
    {
        if (token == null) return;
        try
        {
            if (!MountIslandRecoveryPolicy.IsOriginalCompletion(runOriginal, result, false))
            {
                Tokens.Cancel(token);
                return;
            }
            Capture capture = token.Payload as Capture;
            if (capture == null || island == null || PointerOf(island) != capture.Scope.IslandPointer)
            {
                Tokens.Cancel(token);
                return;
            }
            if (island.objects != null)
            {
                LogError("pop returned true but objects were not cleared; token withdrawn");
                Tokens.Cancel(token);
                return;
            }
            if (!TryReadScope(capture.Scope.Land, island, out MountIslandRecoveryScope now,
                    out _, out _, out _, out string reason)
                || !MountIslandRecoveryScope.SameLoadExceptSeed(capture.Scope, now)
                || !MountIslandRecoveryScope.IsAllowedSeedTransition(
                    capture.Scope.Seed, now.Seed, capture.SavedSeed)
                || !TryMatchSavedLevelEdges(capture, out reason))
            {
                LogOnce("post-" + reason, "post-pop scope/seed rejected; token withdrawn: " + reason);
                Tokens.Cancel(token);
                return;
            }
            if (now.Seed != capture.Scope.Seed)
                Log("seed handoff applied: pre=" + capture.Scope.Seed + " saved=" + capture.SavedSeed
                    + " post=" + now.Seed);

            // 冻结 post 确认 scope（含合法 handoff 后 seed）；后续 consume/activation/commit 全等比较。
            capture.Scope = now;
            if (!Tokens.PublishReady(token))
                return;   // 已被新 TryPop/消费替代
        }
        catch (Exception e)
        {
            Tokens.Cancel(token);
            LogError("after pop failed: " + e.GetType().Name + " " + e.Message);
        }
    }

    internal static void FinalizePop(Exception exception, bool result, bool runOriginal,
        MountIslandRecoveryToken token)
    {
        if (!MountIslandRecoveryPolicy.IsOriginalCompletion(runOriginal, result, exception != null))
            Tokens.Cancel(token);
    }

    internal static void AfterLevelLoaded(Managers managers, bool fromSave, bool runOriginal)
    {
        // 先取走并清 token；之后的任何验证失败都不能再消费同一 token。
        MountIslandRecoveryToken token = Tokens.Take();
        if (token == null || !token.Ready) return;
        if (!MountIslandRecoveryPolicy.IsOriginalCompletion(runOriginal, true, false)) return;
        if (!fromSave) return;
        try
        {
            Managers current = Managers.Inst;
            if (managers == null || current == null || PointerOf(current) != PointerOf(managers))
                return;
            Capture capture = token.Payload as Capture;
            if (capture == null) return;
            TryRecover(capture);
        }
        catch (Exception e)
        {
            LogError("recovery failed: " + e.GetType().Name + " " + e.Message);
        }
    }

    // ------------------------------------------------------------------ capture / evidence

    private static bool TryBuildContext(IslandSaveData island, out Capture capture, out string reason)
    {
        capture = null;
        reason = string.Empty;
        if (island == null) { reason = "island null"; return false; }
        if (!CrossWorldMountRuntime.GrantEnabled()) { reason = "feature off"; return false; }
        if (IslandSaveData.isSavingGame) { reason = "saving"; return false; }
        if (PointerOf(IslandSaveData._currentlySavingIsland) != 0UL
            && PointerOf(IslandSaveData._currentlySavingIsland) == PointerOf(island))
        {
            reason = "island is being saved";
            return false;
        }
        if (!ExtensionIslandRuntime.TryScope(out CampaignSaveData campaign)) { reason = "not offline Greek campaign"; return false; }
        if (!ExtensionIslandRuntime.TryResolveRawOwner(out GlobalSaveData global, out int selected,
                out CampaignSaveData owner)
            || global == null || owner == null || PointerOf(owner) != PointerOf(campaign))
        {
            reason = "raw owner unresolved";
            return false;
        }

        int land = island.land;
        if (!MountIslandSplitPolicy.IsExtensionLand(land)) { reason = "not extension land"; return false; }
        if (campaign.CurrentLand != land) { reason = "campaign current land mismatch"; return false; }

        Il2CppSystem.Collections.Generic.List<IslandSaveData> islands = campaign._islands;
        if (islands == null || land < 0 || land >= islands.Count)
        {
            reason = "island slot unavailable";
            return false;
        }
        if (PointerOf(islands[land]) != PointerOf(island))
        {
            reason = "island slot identity mismatch";
            return false;
        }

        double playTime = island.playTimeDays;
        if (double.IsNaN(playTime) || double.IsInfinity(playTime) || playTime <= 0d)
        {
            reason = "island has no positive playTimeDays";
            return false;
        }
        Il2CppSystem.Collections.Generic.List<IslandSaveData.ObjectData> objects = island.objects;
        if (objects == null || objects.Count <= 0) { reason = "island objects empty/null"; return false; }
        if (!TryReadSavedWorldSeed(island, out int savedSeed, out reason)) return false;

        if (!TryReadScope(ScopeReadPhase.BeforeNativePop, selected, land, island,
                out MountIslandRecoveryScope scope, out _, out _, out _, out reason))
            return false;
        if (scope.GlobalPointer != PointerOf(global))
        {
            reason = "Global.loaded changed before capture";
            return false;
        }

        if (!TryReadSavedLevelEdges(island, out int savedLevelMin, out int savedLevelMax, out reason))
            return false;

        if (!MountIslandMarkerRuntime.TryReadAllMarkers(
                scope.GlobalPointer, campaign, scope.ReignIndex, scope.LandDataPointer,
                out int[][] markers, out reason))
            return false;

        if (!TryScanReceiptFacts(campaign, land, out ReceiptFacts facts, out reason)) return false;
        MountIslandRecoveryCell[] cells = ReadCells(islands);
        ulong objectsPointer = PointerOf(objects);
        int objectsCount = objects.Count;

        // 保存边界是此次旧档的输入；在原有 scope 围栏内双读，不能借生成期 Level cache 回退。
        if (!TryReadSavedLevelEdges(island, out int savedLevelMinAgain, out int savedLevelMaxAgain, out reason))
            return false;
        if (savedLevelMinAgain != savedLevelMin || savedLevelMaxAgain != savedLevelMax)
        {
            reason = "saved LevelEdges changed during evidence scan";
            return false;
        }

        // 扫描前后稳定：owner/容器/Level/seed/holder/GameLayer + markers + receipts + objects。
        if (!TryReadScope(ScopeReadPhase.BeforeNativePop, selected, land, island,
                out MountIslandRecoveryScope again, out _, out _, out _, out reason)
            || !MountIslandRecoveryScope.SameLoad(scope, again))
        {
            reason = "scope changed during evidence scan";
            return false;
        }
        Il2CppSystem.Collections.Generic.List<IslandSaveData> islandsNow = campaign._islands;
        Il2CppSystem.Collections.Generic.List<IslandSaveData.ObjectData> objectsNow = island.objects;
        if (islandsNow == null || objectsNow == null
            || PointerOf(islandsNow) != PointerOf(islands)
            || PointerOf(objectsNow) != objectsPointer
            || objectsNow.Count != objectsCount)
        {
            reason = "island objects changed during evidence scan";
            return false;
        }
        if (!MountIslandMarkerRuntime.TryReadAllMarkers(
                scope.GlobalPointer, campaign, scope.ReignIndex, scope.LandDataPointer,
                out int[][] markersAgain, out reason)
            || !SameMarkers(markers, markersAgain))
        {
            reason = "markers changed during evidence scan";
            return false;
        }
        if (!TryScanReceiptFacts(campaign, land, out ReceiptFacts factsAgain, out reason)
            || !SameReceipts(facts, factsAgain))
        {
            reason = "receipts changed during evidence scan";
            return false;
        }
        if (!SameCells(cells, ReadCells(campaign._islands)))
        {
            reason = "island cells changed during evidence scan";
            return false;
        }
        if (!TryReadSavedWorldSeed(island, out int savedSeedAgain, out reason)
            || savedSeedAgain != savedSeed)
        {
            reason = "saved World seed changed during evidence scan";
            return false;
        }

        capture = new Capture
        {
            Island = island,
            Scope = scope,
            Cells = cells,
            MarkerPreimage = markers,
            ReceiptThis = facts.This,
            ReceiptOther = facts.Other,
            ReceiptUnknown = facts.Unknown,
            CampaignEvidenceReadable = facts.Readable,
            SavedSeed = savedSeed,
            SavedLevelMin = savedLevelMin,
            SavedLevelMax = savedLevelMax,
            ObjectsPointer = objectsPointer,
            ObjectsCount = objectsCount,
        };
        return true;
    }

    // 原生 Level cache（Level.GetLayer 的字典）只在 TryPop original 的 ClearLevel→ValidateLayers
    // 之后存在；Prefix 读取只能跳过该 cache 关系校验，post-pop 必须使用 FULL。
    private enum ScopeReadPhase
    {
        // Zero must be FULL so default(ScopeReadPhase) can never bypass the post-producer
        // cache relation check. Before is explicit and nonzero.
        AfterNativePop = 0,
        BeforeNativePop = 1,
    }

    /// <summary>读当前 scope 身份；默认 FULL（AfterNativePop）。</summary>
    private static bool TryReadScope(int land, IslandSaveData island,
        out MountIslandRecoveryScope scope, out Level level, out Transform gameLayer, out World world,
        out string reason)
        => TryReadScope(ScopeReadPhase.AfterNativePop, -1, land, island,
            out scope, out level, out gameLayer, out world, out reason);

    /// <summary>读当前 scope 身份；默认 FULL（AfterNativePop）。</summary>
    private static bool TryReadScope(int selectedExpected, int land, IslandSaveData island,
        out MountIslandRecoveryScope scope, out Level level, out Transform gameLayer, out World world,
        out string reason)
        => TryReadScope(ScopeReadPhase.AfterNativePop, selectedExpected, land, island,
            out scope, out level, out gameLayer, out world, out reason);

    private static bool TryReadScope(ScopeReadPhase phase, int selectedExpected, int land,
        IslandSaveData island,
        out MountIslandRecoveryScope scope, out Level level, out Transform gameLayer, out World world,
        out string reason)
    {
        scope = default;
        level = null;
        gameLayer = null;
        world = null;
        reason = string.Empty;
        try
        {
            if (phase != ScopeReadPhase.BeforeNativePop && phase != ScopeReadPhase.AfterNativePop)
            {
                reason = "invalid scope phase";
                return false;
            }
            if (island == null) { reason = "island null"; return false; }
            CampaignSaveData campaign = CampaignSaveData.current;
            if (campaign == null) { reason = "campaign null"; return false; }
            if (!ExtensionIslandRuntime.TryResolveRawOwner(out GlobalSaveData global, out int selected,
                    out CampaignSaveData owner)
                || global == null || owner == null || PointerOf(owner) != PointerOf(campaign))
            {
                reason = "raw owner unresolved";
                return false;
            }
            ulong globalPointer = PointerOf(global);
            if (globalPointer == 0UL) { reason = "Global.loaded pointer missing"; return false; }
            if (selectedExpected >= 0 && selected != selectedExpected)
            {
                reason = "selected campaign slot changed";
                return false;
            }
            if (campaign.CurrentLand != land || island.land != land)
            {
                reason = "land identity changed";
                return false;
            }
            CampaignSaveData.ReignInfo reign = campaign.currentReign;
            Il2CppSystem.Collections.Generic.List<CampaignSaveData.LandMapData> landData =
                reign != null ? reign.landData : null;
            if (landData == null || land < 0 || land >= landData.Count)
            {
                reason = "landData unavailable";
                return false;
            }
            Il2CppSystem.Collections.Generic.List<IslandSaveData> islands = campaign._islands;
            if (islands == null || land >= islands.Count || PointerOf(islands[land]) != PointerOf(island))
            {
                reason = "island slot changed";
                return false;
            }

            Managers managers = Managers.Inst;
            if (managers == null) { reason = "Managers unavailable"; return false; }
            level = managers.level;
            world = managers.world;
            if (level == null || level.gameObject == null || world == null)
            {
                reason = "Level/World unavailable";
                return false;
            }
            gameLayer = world.gameLayer;
            if (gameLayer == null || gameLayer.gameObject == null)
            {
                reason = "GameLayer unavailable";
                return false;
            }
            if (phase == ScopeReadPhase.AfterNativePop)
            {
                GameObject layerByContent = level.GetLayer(ContentLayers.GameLayer);
                if (layerByContent == null
                    || PointerOf(layerByContent) != PointerOf(gameLayer.gameObject))
                {
                    reason = "Level GameLayer != World.gameLayer";
                    return false;
                }
            }
            Scene levelScene = level.gameObject.scene;
            Scene layerScene = gameLayer.gameObject.scene;
            if (!levelScene.IsValid() || !levelScene.isLoaded || !layerScene.IsValid()
                || levelScene.handle != layerScene.handle)
            {
                reason = "Level/GameLayer scene mismatch";
                return false;
            }
            Quaternion rotation = gameLayer.rotation;
            if (Math.Abs(rotation.x) > 1e-4f || Math.Abs(rotation.y) > 1e-4f
                || Math.Abs(rotation.z) > 1e-4f || Math.Abs(rotation.w - 1f) > 1e-4f)
            {
                reason = "GameLayer rotation is not identity";
                return false;
            }
            Vector3 lossy = gameLayer.lossyScale;
            if (Math.Abs(lossy.x - 1f) > 1e-4f || Math.Abs(lossy.y - 1f) > 1e-4f
                || Math.Abs(lossy.z - 1f) > 1e-4f)
            {
                reason = "GameLayer scale is not identity";
                return false;
            }

            BiomeHolder holder = BiomeHolder.Inst;
            if (holder == null || holder.BiomeIndex != BiomeHolder.GreeceBiomeIndex)
            {
                reason = "Greek biome holder unavailable";
                return false;
            }
            int reignIndex = campaign.reign;
            if (reignIndex < 0) { reason = "campaign reign invalid"; return false; }
            scope = new MountIslandRecoveryScope(
                globalPointer, PointerOf(campaign), selected, reignIndex,
                CrossWorldMountDependencies.PointerOfList(landData),
                PointerOf(islands), land, PointerOf(level), levelScene.handle, levelScene.name,
                level.cachedCurrentLevelSeed,
                PointerOf(gameLayer.gameObject), layerScene.handle,
                PointerOf(holder), holder.BiomeIndex, PointerOf(island), island.land);
            return true;
        }
        catch (Exception e)
        {
            string message;
            try
            {
                message = e.Message ?? string.Empty;
                if (message.Length > 512) message = message.Substring(0, 512);
            }
            catch (Exception)
            {
                message = "<unavailable>";
            }
            reason = "scope read failed: " + e.GetType().Name + " phase=" + phase
                + " message=" + message;
            return false;
        }
    }

    // IslandSaveData owns the saved geometry; Level._levelEdges is only a generation cache.
    // Always read one value snapshot, and keep raw integer equality separate from float validity.
    private static bool TryReadSavedLevelEdges(IslandSaveData island,
        out int min, out int max, out string reason)
    {
        min = max = 0;
        reason = string.Empty;
        try
        {
            if (island == null) { reason = "saved LevelEdges island missing"; return false; }
            IntRange edges = island.LevelEdges;
            float minFloat = edges.min;
            float maxFloat = edges.max;
            if (edges.min >= edges.max || !float.IsFinite(minFloat) || !float.IsFinite(maxFloat)
                || maxFloat - minFloat <= 1e-3f)
            {
                reason = "saved LevelEdges invalid";
                return false;
            }
            min = edges.min;
            max = edges.max;
            return true;
        }
        catch (Exception e)
        {
            reason = "saved LevelEdges read failed: " + e.GetType().Name;
            return false;
        }
    }

    private static bool TryMatchSavedLevelEdges(Capture capture, out string reason)
    {
        reason = string.Empty;
        if (capture == null) { reason = "saved LevelEdges capture missing"; return false; }
        if (!TryReadSavedLevelEdges(capture.Island, out int min, out int max, out reason))
            return false;
        if (min != capture.SavedLevelMin || max != capture.SavedLevelMax)
        {
            reason = "saved LevelEdges changed after capture";
            return false;
        }
        return true;
    }

    /// <summary>
    /// 从同次 island.objects 的唯一 World 组件保存数据读取目标 seed。当前 2.4 存档的
    /// componentData2 为 name="World"、type="WorldData"，data JSON 使用 "cachedSeed"；
    /// 仅接受唯一顶层 cachedSeed 整数；未知键形状、重复、缺字段或非法 JSON 一律拒绝。不使用 Level.cachedCurrentLevelSeed 作为回退，也不写存档。
    /// </summary>
    private static bool TryReadSavedWorldSeed(IslandSaveData island, out int seed, out string reason)
    {
        seed = 0;
        reason = string.Empty;
        try
        {
            Il2CppSystem.Collections.Generic.List<IslandSaveData.ObjectData> objects = island.objects;
            if (objects == null) { reason = "island objects unavailable for World seed"; return false; }
            bool found = false;
            for (int i = 0; i < objects.Count; i++)
            {
                IslandSaveData.ObjectData record = objects[i];
                if (record == null) continue;
                Il2CppSystem.Collections.Generic.List<IslandSaveData.ObjectData.ComponentData> components =
                    record.componentData2;
                if (components == null) continue;
                for (int c = 0; c < components.Count; c++)
                {
                    IslandSaveData.ObjectData.ComponentData component = components[c];
                    if (component == null) continue;
                    string type = component.type;
                    if (!string.Equals(type, "WorldData", StringComparison.Ordinal)) continue;
                    if (found) { reason = "duplicate WorldData component"; return false; }
                    string recordName = record.name;
                    string hierarchyPath = record.hierarchyPath;
                    string prefabPath = record.prefabPath;
                    if (!string.Equals(recordName, "World", StringComparison.Ordinal)
                        || !string.Equals(hierarchyPath, "Managers/", StringComparison.Ordinal)
                        || !string.IsNullOrEmpty(prefabPath))
                    {
                        reason = "WorldData source record mismatch";
                        return false;
                    }
                    string name = component.name;
                    if (!string.Equals(name, "World", StringComparison.Ordinal))
                    {
                        reason = "WorldData component name is not World";
                        return false;
                    }
                    if (!TryParseWorldSeed(component.data, out seed, out reason)) return false;
                    found = true;
                }
            }
            if (!found) { reason = "WorldData component missing"; return false; }
            return true;
        }
        catch (Exception e)
        {
            reason = "World seed read failed: " + e.GetType().Name;
            seed = 0;
            return false;
        }
    }

    private static bool TryParseWorldSeed(string data, out int seed, out string reason)
        => MountIslandRecoverySeedData.TryParse(data, out seed, out reason);

    private static bool TryScanReceiptFacts(CampaignSaveData campaign, int land,
        out ReceiptFacts facts, out string reason)
    {
        facts = default;
        reason = string.Empty;
        CrossWorldMountDefinition[] definitions = CrossWorldMountCatalog.Definitions;
        var thisReceipt = new bool[definitions.Length];
        var otherReceipt = new bool[definitions.Length];
        var unknown = new bool[definitions.Length];
        bool sawError = false;
        try
        {
            Il2CppSystem.Collections.Generic.List<IslandSaveData> islands = campaign._islands;
            if (islands == null) { reason = "_islands unavailable"; return false; }
            int slots = islands.Count;
            for (int slot = 0; slot < slots; slot++)
            {
                IslandSaveData island = islands[slot];
                if (island == null) continue;
                Il2CppSystem.Collections.Generic.List<IslandSaveData.ObjectData> objects = island.objects;
                if (objects == null) continue;
                int count = objects.Count;
                for (int i = 0; i < count; i++)
                {
                    IslandSaveData.ObjectData record = objects[i];
                    if (record == null) continue;
                    try
                    {
                        string prefabPath = record.prefabPath;
                        string name = record.name;
                        string hierarchyPath = record.hierarchyPath;
                        for (int d = 0; d < definitions.Length; d++)
                        {
                            if (!MountIslandGrantAuthority.MatchesReceipt(
                                    definitions[d], prefabPath, name, hierarchyPath)) continue;
                            if (slot == land) thisReceipt[d] = true;
                            else otherReceipt[d] = true;
                        }
                    }
                    catch (Exception)
                    {
                        sawError = true;
                        break;   // 该槽剩余记录不可证明；其他槽继续
                    }
                }
            }
            for (int d = 0; d < definitions.Length; d++)
                unknown[d] = sawError && !thisReceipt[d] && !otherReceipt[d];
            facts = new ReceiptFacts(thisReceipt, otherReceipt, unknown, !sawError);
            return true;
        }
        catch (Exception e)
        {
            reason = "receipt scan failed: " + e.GetType().Name;
            return false;
        }
    }

    private static MountIslandRecoveryCell[] ReadCells(
        Il2CppSystem.Collections.Generic.List<IslandSaveData> islands)
    {
        if (islands == null) return Array.Empty<MountIslandRecoveryCell>();
        int count = islands.Count;
        var cells = new MountIslandRecoveryCell[count];
        for (int i = 0; i < count; i++)
        {
            IslandSaveData island = islands[i];
            if (island == null)
            {
                cells[i] = new MountIslandRecoveryCell(0UL, 0UL, -1);
                continue;
            }
            Il2CppSystem.Collections.Generic.List<IslandSaveData.ObjectData> objects = island.objects;
            cells[i] = new MountIslandRecoveryCell(
                PointerOf(island), objects != null ? PointerOf(objects) : 0UL,
                objects != null ? objects.Count : -1);
        }
        return cells;
    }

    private static bool SameReceipts(in ReceiptFacts a, in ReceiptFacts b)
    {
        if (a.Readable != b.Readable
            || a.This == null || a.Other == null || a.Unknown == null
            || b.This == null || b.Other == null || b.Unknown == null
            || a.This.Length != b.This.Length
            || a.Other.Length != b.Other.Length
            || a.Unknown.Length != b.Unknown.Length) return false;
        for (int i = 0; i < a.This.Length; i++)
        {
            if (a.This[i] != b.This[i] || a.Other[i] != b.Other[i] || a.Unknown[i] != b.Unknown[i])
                return false;
        }
        return true;
    }

    private static bool SameMarkers(int[][] a, int[][] b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (!MountIslandMarkerBatch.SameSequence(a[i], b[i])) return false;
        }
        return true;
    }

    private static bool SameCells(MountIslandRecoveryCell[] a, MountIslandRecoveryCell[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i].IslandPointer != b[i].IslandPointer
                || a[i].ObjectsPointer != b[i].ObjectsPointer
                || a[i].ObjectsCount != b[i].ObjectsCount) return false;
        }
        return true;
    }

    // ------------------------------------------------------------------ recovery

    private static void TryRecover(Capture capture)
    {
        if (capture == null) return;
        if (!CrossWorldMountRuntime.GrantEnabled()) return;
        if (IslandSaveData.isSavingGame) return;
        CampaignSaveData campaign;
        try
        {
            if (!ExtensionIslandRuntime.TryScope(out campaign)) return;
        }
        catch (Exception e)
        {
            LogError("scope recheck failed: " + e.GetType().Name);
            return;
        }
        if (!ExtensionIslandRuntime.TryResolveRawOwner(out GlobalSaveData global, out _,
                out CampaignSaveData owner)
            || global == null || PointerOf(global) != capture.Scope.GlobalPointer
            || owner == null || PointerOf(owner) != capture.Scope.CampaignPointer)
            return;

        if (!TryReadScope(capture.Scope.SelectedIndex, capture.Scope.Land, capture.Island,
                out MountIslandRecoveryScope now, out Level level, out Transform gameLayer, out World world,
                out string reason))
        {
            LogError("consume scope rejected: " + reason);
            return;
        }
        if (!MountIslandRecoveryScope.SameLoad(capture.Scope, now))
        {
            LogError("consume scope changed (owner/Level/seed/holder/GameLayer)");
            return;
        }
        if (capture.Island.objects != null)
        {
            LogError("consume rejected: objects were not cleared by this pop");
            return;
        }

        if (!MountIslandMarkerRuntime.TryReadAllMarkers(
                capture.Scope.GlobalPointer, campaign, capture.Scope.ReignIndex, capture.Scope.LandDataPointer,
                out int[][] markersNow, out reason)
            || !SameMarkers(capture.MarkerPreimage, markersNow))
        {
            LogError("consume rejected: markers drifted: " + reason);
            return;
        }

        Il2CppSystem.Collections.Generic.List<IslandSaveData> islands = campaign._islands;
        if (islands == null || islands.Count != capture.Cells.Length)
        {
            LogError("consume rejected: _islands container/count changed");
            return;
        }
        for (int i = 0; i < islands.Count; i++)
        {
            IslandSaveData slot = islands[i];
            ulong pointer = PointerOf(slot);
            if (i == capture.Scope.Land)
            {
                if (pointer != capture.Cells[i].IslandPointer || (slot != null && slot.objects != null))
                {
                    LogError("consume rejected: target island slot/objects changed");
                    return;
                }
                continue;
            }
            Il2CppSystem.Collections.Generic.List<IslandSaveData.ObjectData> objects =
                slot != null ? slot.objects : null;
            ulong objectsPointer = objects != null ? PointerOf(objects) : 0UL;
            int objectsCount = objects != null ? objects.Count : -1;
            if (pointer != capture.Cells[i].IslandPointer
                || objectsPointer != capture.Cells[i].ObjectsPointer
                || objectsCount != capture.Cells[i].ObjectsCount)
            {
                LogError("consume rejected: other island slot changed at " + i);
                return;
            }
        }

        if (!TryScanReceiptFacts(campaign, capture.Scope.Land, out ReceiptFacts receiptsNow, out reason))
        {
            LogError("consume rejected: receipt scan failed: " + reason);
            return;
        }
        if (!receiptsNow.Readable || !SameOtherReceipts(capture, receiptsNow))
        {
            LogError("consume rejected: other-island receipts drifted/unknown");
            return;
        }

        if (!TryMatchSavedLevelEdges(capture, out reason))
        {
            LogError("consume rejected: " + reason);
            return;
        }

        // 原生依赖复用：只在进入恢复时 EnsureAll；IsDefinitionReady 逐定义。
        try { CrossWorldMountDependencies.EnsureAll(); }
        catch (Exception e) { LogError("EnsureAll failed: " + e.GetType().Name + " " + e.Message); }

        if (!TryScanLive(gameLayer, capture.Scope.Land, out LiveInfo[] live, out reason))
        {
            LogError("consume rejected: live scan failed: " + reason);
            return;
        }

        CrossWorldMountDefinition[] definitions = CrossWorldMountCatalog.Definitions;
        var evidence = new List<MountIslandRecoveryEvidence>(definitions.Length);
        for (int d = 0; d < definitions.Length; d++)
        {
            CrossWorldMountDefinition definition = definitions[d];
            bool markerOnIsland = MarkerContains(capture.MarkerPreimage, capture.Scope.Land, definition.SteedTypeId);
            bool otherMarker = false;
            for (int land = 0; land < capture.MarkerPreimage.Length; land++)
            {
                if (land != capture.Scope.Land
                    && MarkerContains(capture.MarkerPreimage, land, definition.SteedTypeId))
                {
                    otherMarker = true;
                    break;
                }
            }
            bool dependencyReady = false;
            try { dependencyReady = CrossWorldMountDependencies.IsDefinitionReady(definition); }
            catch (Exception) { dependencyReady = false; }
            bool seasonal = false;
            try { seasonal = CrossWorldMountRuntime.IsNewGrantSeasonEligible(definition); }
            catch (Exception) { seasonal = false; }

            evidence.Add(new MountIslandRecoveryEvidence(
                definition.Id, definition.SteedTypeId,
                targetLand: MountIslandSplitPolicy.GetTargetLand(definition.Id) == capture.Scope.Land,
                markerOnIsland: markerOnIsland,
                receiptOnIsland: d < capture.ReceiptThis.Length && capture.ReceiptThis[d],
                grantedOtherIsland: (d < capture.ReceiptOther.Length && capture.ReceiptOther[d]) || otherMarker,
                live: d < live.Length ? live[d].State : MountIslandRecoveryLiveState.Unknown,
                liveComplete: d < live.Length && live[d].Complete,
                dependencyReady: dependencyReady,
                seasonalEligible: seasonal,
                campaignEvidenceReadable: capture.CampaignEvidenceReadable,
                receiptUnknown: d < capture.ReceiptUnknown.Length && capture.ReceiptUnknown[d]));
        }

        MountIslandRecoveryPlan plan = MountIslandRecoveryPolicy.Build(evidence);
        Log("plan: creates=" + plan.Creates.Count + " markerOnly=" + plan.MarkerOnly.Count
            + " rejects=" + plan.Diagnostics.Count + " land=" + capture.Scope.Land);
        for (int i = 0; i < plan.Diagnostics.Count; i++) LogError("definition rejected: " + plan.Diagnostics[i]);

        // 模板失败只拒该定义臂；其余 create 继续作为同一批预检。
        for (int i = plan.Creates.Count - 1; i >= 0; i--)
        {
            MountIslandRecoveryArm arm = plan.Creates[i];
            if (MountIslandRecoveryTemplates.TryGet(arm.DefinitionId, level, out _, out string templateReason))
                continue;
            LogError("template rejected: " + arm.DefinitionId + ": " + templateReason);
            plan.Creates.RemoveAt(i);
        }

        MountIslandRecoveryPlacement[] placements = Array.Empty<MountIslandRecoveryPlacement>();
        if (plan.Creates.Count > 0)
        {
            try
            {
                if (!TryBuildPlacementModel(level, gameLayer, world,
                        capture.SavedLevelMin, capture.SavedLevelMax,
                        out PlacementModel model, out reason))
                {
                    LogError("create batch stopped: " + reason);
                    plan.Creates.Clear();
                }
                else if (!TryPlanPlacements(plan, model, out placements, out reason))
                {
                    LogError("create batch stopped: " + reason);
                    plan.Creates.Clear();
                }
            }
            catch (Exception e)
            {
                LogError("create batch stopped: placement threw " + e.GetType().Name);
                plan.Creates.Clear();
                placements = Array.Empty<MountIslandRecoveryPlacement>();
            }
        }

        var host = new RecoveryHost(capture, level, gameLayer);
        MountIslandRecoveryExecutionResult result;
        try
        {
            result = MountIslandRecoveryTransaction.Run(plan, placements, host);
        }
        finally
        {
            host.Dispose();
        }

        if (result.Outcome == MountIslandRecoveryExecutionOutcome.Committed)
            Log("recovery committed: created=" + result.CreatedCount + " markers="
                + MountIslandRecoveryTransaction.BuildMarkerTypes(plan).Length);
        else if (result.Outcome == MountIslandRecoveryExecutionOutcome.Noop)
            Log("recovery no-op");
        else if (result.Outcome == MountIslandRecoveryExecutionOutcome.MarkerUnknown)
            LogError("recovery marker commit unknown; retained " + result.CreatedCount
                + " new roots for diagnostics: " + result.Reason);
        else
            LogError("recovery " + result.Outcome + ": " + result.Reason);
    }

    private static bool SameOtherReceipts(Capture capture, in ReceiptFacts now)
    {
        if (now.Other == null || now.Unknown == null
            || capture.ReceiptOther == null || capture.ReceiptUnknown == null
            || now.Other.Length != capture.ReceiptOther.Length
            || now.Unknown.Length != capture.ReceiptUnknown.Length) return false;
        for (int i = 0; i < now.Other.Length; i++)
        {
            if (now.Other[i] != capture.ReceiptOther[i]) return false;
            if (now.Unknown[i] != capture.ReceiptUnknown[i]) return false;
        }
        return true;
    }

    private static bool MarkerContains(int[][] markers, int land, int steedTypeId)
        => markers != null && land >= 0 && land < markers.Length
            && CrossWorldMountPolicy.Contains(markers[land], steedTypeId);

    // ------------------------------------------------------------------ live enumeration

    private static bool TryScanLive(Transform gameLayer, int land, out LiveInfo[] infos, out string reason)
    {
        infos = null;
        reason = string.Empty;
        CrossWorldMountDefinition[] definitions = CrossWorldMountCatalog.Definitions;
        try
        {
            Persistent[] all = gameLayer.GetComponentsInChildren<Persistent>(true);
            var counts = new int[definitions.Length];
            var roots = new Transform[definitions.Length];
            for (int i = 0; i < all.Length; i++)
            {
                Persistent persistent = all[i];
                if (persistent == null || persistent.gameObject == null) continue;
                string path;
                try { path = persistent.path; }
                catch (Exception e) { reason = "persistent path read failed: " + e.GetType().Name; return false; }
                if (string.IsNullOrEmpty(path)) continue;
                if (!MountIslandRecoveryGeometry.TryGetFacilityByPath(path, out MountIslandRecoveryGeometry.FacilityRow row))
                    continue;
                int index = FindDefinitionIndex(definitions, row.DefinitionId);
                if (index < 0) continue;
                counts[index]++;
                if (roots[index] == null) roots[index] = persistent.transform;
            }

            infos = new LiveInfo[definitions.Length];
            int sceneHandle = gameLayer.gameObject.scene.handle;
            for (int d = 0; d < definitions.Length; d++)
            {
                if (counts[d] <= 0)
                {
                    infos[d] = new LiveInfo { State = MountIslandRecoveryLiveState.Missing, Complete = false };
                    continue;
                }
                if (counts[d] > 1)
                {
                    infos[d] = new LiveInfo { State = MountIslandRecoveryLiveState.Duplicate, Complete = false };
                    continue;
                }
                bool complete = IsFacilityComplete(roots[d], definitions[d].Id, sceneHandle, out _);
                infos[d] = new LiveInfo
                {
                    State = MountIslandRecoveryLiveState.Single,
                    Complete = complete,
                };
            }
            return true;
        }
        catch (Exception e)
        {
            reason = "live enumeration failed: " + e.GetType().Name;
            infos = null;
            return false;
        }
    }

    private static int FindDefinitionIndex(CrossWorldMountDefinition[] definitions, string id)
    {
        for (int i = 0; i < definitions.Length; i++)
        {
            if (string.Equals(definitions[i].Id, id, StringComparison.Ordinal)) return i;
        }
        return -1;
    }

    private static bool IsFacilityComplete(Transform root, string definitionId, int sceneHandle, out string reason)
    {
        reason = string.Empty;
        if (root == null || root.gameObject == null) { reason = "root null"; return false; }
        if (root.gameObject.scene.handle != sceneHandle) { reason = "scene mismatch"; return false; }
        if (root.GetComponentsInChildren<PrefabPlaceholder>(true).Length > 0)
        {
            reason = "PrefabPlaceholder in live facility";
            return false;
        }
        if (root.GetComponentsInChildren<SpriteRenderer>(true).Length == 0)
        {
            reason = "no SpriteRenderer";
            return false;
        }
        if (string.Equals(definitionId, "anniversary.rainbowpony", StringComparison.Ordinal))
        {
            if (root.GetComponentInChildren<PayableUpgrade>(true) == null
                || root.GetComponentInChildren<CerberusPuzzleController>(true) == null)
            {
                reason = "birthday puzzle chain incomplete";
                return false;
            }
        }
        else if (root.GetComponentInChildren<SteedSpawn>(true) == null)
        {
            reason = "SteedSpawn missing";
            return false;
        }
        return true;
    }

    // ------------------------------------------------------------------ placement model

    private sealed class PlacementModel
    {
        internal readonly List<MountIslandRecoveryInterval> Tiles = new List<MountIslandRecoveryInterval>();
        internal readonly List<MountIslandRecoveryInterval> Exclusions = new List<MountIslandRecoveryInterval>();
        internal float LevelMin;
        internal float LevelMax;
    }

    private static bool TryBuildPlacementModel(Level level, Transform gameLayer, World world,
        int savedLevelMin, int savedLevelMax,
        out PlacementModel model, out string reason)
    {
        model = null;
        reason = string.Empty;
        try
        {
            var result = new PlacementModel
            {
                LevelMin = savedLevelMin,
                LevelMax = savedLevelMax,
            };
            if (!float.IsFinite(result.LevelMin) || !float.IsFinite(result.LevelMax)
                || result.LevelMax - result.LevelMin <= 1e-3f)
            {
                reason = "LevelEdges invalid";
                return false;
            }

            Tile[] tiles = gameLayer.GetComponentsInChildren<Tile>(true);
            for (int i = 0; i < tiles.Length; i++)
            {
                Tile tile = tiles[i];
                if (tile == null || tile.gameObject == null) continue;
                if (!TryReadPath(tile.gameObject, out string path, out reason)) return false;
                bool noGround = tile.noGround;
                if (!noGround && !MountIslandRecoveryGeometry.IsEligibleTilePath(path)) continue;
                if (noGround)
                {
                    // noGround=true 一律禁止；优先用实际 renderer/collider 世界 bounds（对 raised/洞穴
                    // 等非表面 Tile 也成立），无法读 bounds 才回落到本地表面区间校验。
                    if (TryCollectBounds(tile.gameObject, out float minX, out float maxX))
                    {
                        result.Exclusions.Add(new MountIslandRecoveryInterval(minX, maxX));
                        continue;
                    }
                }
                if (!TryTileInterval(tile, out MountIslandRecoveryInterval interval, out reason)) return false;
                if (noGround) result.Exclusions.Add(interval);
                else result.Tiles.Add(interval);
            }
            if (result.Tiles.Count == 0)
            {
                reason = "no eligible Tile Big/Forest coverage";
                return false;
            }

            Bridge[] bridges = gameLayer.GetComponentsInChildren<Bridge>(true);
            for (int i = 0; i < bridges.Length; i++)
            {
                if (bridges[i] == null) continue;
                if (!AddStructure(level, gameLayer, MountIslandRecoveryGeometry.StructureKind.Bridge,
                        bridges[i], result.Exclusions, out reason)) return false;
            }
            Il2CppSystem.Collections.Generic.HashSet<Bridge> worldBridges = world.bridges;
            if (worldBridges != null)
            {
                foreach (Bridge bridge in worldBridges)
                {
                    if (bridge == null) continue;
                    if (!AddStructure(level, gameLayer, MountIslandRecoveryGeometry.StructureKind.Bridge,
                            bridge, result.Exclusions, out reason)) return false;
                }
            }
            Beach[] beaches = gameLayer.GetComponentsInChildren<Beach>(true);
            for (int i = 0; i < beaches.Length; i++)
            {
                if (beaches[i] == null) continue;
                if (!AddStructure(level, gameLayer, MountIslandRecoveryGeometry.StructureKind.Beach,
                        beaches[i], result.Exclusions, out reason)) return false;
            }
            CliffPortalState[] cliffs = gameLayer.GetComponentsInChildren<CliffPortalState>(true);
            for (int i = 0; i < cliffs.Length; i++)
            {
                if (cliffs[i] == null) continue;
                if (!AddStructure(level, gameLayer, MountIslandRecoveryGeometry.StructureKind.CliffPortal,
                        cliffs[i], result.Exclusions, out reason)) return false;
            }
            BombablePortal[] bombs = gameLayer.GetComponentsInChildren<BombablePortal>(true);
            for (int i = 0; i < bombs.Length; i++)
            {
                if (bombs[i] == null) continue;
                if (!AddStructure(level, gameLayer, MountIslandRecoveryGeometry.StructureKind.BombablePortal,
                        bombs[i], result.Exclusions, out reason)) return false;
            }

            if (!CollectOccupancy(level, gameLayer, result.Exclusions, out reason)) return false;
            model = result;
            return true;
        }
        catch (Exception e)
        {
            reason = "placement model failed: " + e.GetType().Name + " " + e.Message;
            model = null;
            return false;
        }
    }

    private static bool TryReadPath(GameObject go, out string path, out string reason)
    {
        path = null;
        reason = string.Empty;
        try
        {
            Persistent persistent = go.GetComponent<Persistent>();
            path = persistent != null ? persistent.path : null;
            return true;
        }
        catch (Exception e)
        {
            reason = "Persistent.path read failed: " + e.GetType().Name;
            return false;
        }
    }

    private static bool TryTileInterval(Tile tile, out MountIslandRecoveryInterval interval, out string reason)
    {
        interval = default;
        reason = string.Empty;
        try
        {
            Transform tr = tile.transform;
            Transform parent = tr.parent;
            if (parent == null) { reason = "tile has no parent"; return false; }
            Vector3 local = tr.localPosition;
            Quaternion rotation = tr.localRotation;
            Vector3 scale = tr.localScale;
            if (!float.IsFinite(local.y) || Math.Abs(local.y) > 1e-3f)
            {
                reason = "tile surface local y != 0";
                return false;
            }
            if (!float.IsFinite(local.z)
                || (Math.Abs(local.z) > 1e-3f && Math.Abs(local.z - 0.01f) > 1e-3f))
            {
                reason = "tile surface local z not 0/0.01";
                return false;
            }
            if (Math.Abs(rotation.x) > 1e-4f || Math.Abs(rotation.y) > 1e-4f
                || Math.Abs(rotation.z) > 1e-4f || Math.Abs(rotation.w - 1f) > 1e-4f
                || !float.IsFinite(scale.x) || !float.IsFinite(scale.y) || scale.x <= 0f || scale.y <= 0f)
            {
                reason = "tile transform not identity/positive";
                return false;
            }
            Quaternion parentRotation = parent.rotation;
            Vector3 parentScale = parent.lossyScale;
            if (Math.Abs(parentRotation.x) > 1e-4f || Math.Abs(parentRotation.y) > 1e-4f
                || Math.Abs(parentRotation.z) > 1e-4f || Math.Abs(parentRotation.w - 1f) > 1e-4f
                || Math.Abs(parentScale.x - 1f) > 1e-4f || Math.Abs(parentScale.y - 1f) > 1e-4f
                || Math.Abs(parentScale.z - 1f) > 1e-4f)
            {
                reason = "tile parent not identity";
                return false;
            }
            float left = tile.GetLeft();
            float right = tile.GetRight();
            if (!float.IsFinite(left) || !float.IsFinite(right) || right - left <= 1e-4f)
            {
                reason = "tile interval invalid";
                return false;
            }
            float worldLeft = parent.TransformPoint(new Vector3(left, 0f, 0f)).x;
            float worldRight = parent.TransformPoint(new Vector3(right, 0f, 0f)).x;
            if (!float.IsFinite(worldLeft) || !float.IsFinite(worldRight) || worldRight <= worldLeft)
            {
                reason = "tile world interval invalid";
                return false;
            }
            interval = new MountIslandRecoveryInterval(worldLeft, worldRight);
            return true;
        }
        catch (Exception e)
        {
            reason = "tile interval failed: " + e.GetType().Name;
            return false;
        }
    }

    private static bool AddStructure(Level level, Transform gameLayer,
        MountIslandRecoveryGeometry.StructureKind kind, Component component,
        List<MountIslandRecoveryInterval> exclusions, out string reason)
    {
        reason = string.Empty;
        try
        {
            Transform tr = component.transform;
            if (!TryFindStructurePath(tr, gameLayer, out string path, out reason)) return false;
            if (!MountIslandRecoveryGeometry.TryGetStructureExclusion(kind, path, out var row))
            {
                reason = "unknown " + kind + " native path: " + path;
                return false;
            }
            Vector3 a = tr.TransformPoint(new Vector3(row.SourceMinX, 0f, 0f));
            Vector3 b = tr.TransformPoint(new Vector3(row.SourceMaxX, 0f, 0f));
            if (!float.IsFinite(a.x) || !float.IsFinite(b.x))
            {
                reason = kind + " interval unreadable";
                return false;
            }
            exclusions.Add(new MountIslandRecoveryInterval(Math.Min(a.x, b.x), Math.Max(a.x, b.x)));
            if (TryCollectBounds(component.gameObject, out float minX, out float maxX))
                exclusions.Add(new MountIslandRecoveryInterval(minX, maxX));
            return true;
        }
        catch (Exception e)
        {
            reason = kind + " exclusion failed: " + e.GetType().Name;
            return false;
        }
    }

    private static bool TryFindStructurePath(Transform tr, Transform gameLayer, out string path, out string reason)
    {
        path = null;
        reason = string.Empty;
        Transform cursor = tr;
        while (cursor != null)
        {
            Persistent persistent = cursor.GetComponent<Persistent>();
            if (persistent != null)
            {
                try { path = persistent.path; }
                catch (Exception e) { reason = "structure path read failed: " + e.GetType().Name; return false; }
                if (!string.IsNullOrEmpty(path)) return true;
            }
            if (cursor == gameLayer) break;
            cursor = cursor.parent;
        }
        reason = "typed structure without Persistent.path";
        return false;
    }

    private static bool CollectOccupancy(Level level, Transform parent,
        List<MountIslandRecoveryInterval> exclusions, out string reason)
    {
        reason = string.Empty;
        int count = parent.childCount;
        for (int i = 0; i < count; i++)
        {
            Transform child = parent.GetChild(i);
            if (child == null || child.gameObject == null) continue;
            try
            {
                Persistent persistent = child.GetComponent<Persistent>();
                string path = persistent != null ? persistent.path : null;
                if (!string.IsNullOrEmpty(path)
                    && (MountIslandRecoveryGeometry.IsEligibleTilePath(path)
                        || MountIslandRecoveryGeometry.IsStructurePath(path)))
                {
                    continue;   // tile/typed structure 已按原生区间处理，不能再叠整棵艺术 bounds
                }

                // 建筑/设施/树有持久化或原生占位组件；以这些对象 root 的真实 bounds 为占地。
                // 没有标记的纯装饰 sprite 不定义地面占用，不参与整岛排除。
                bool levelBlock = child.GetComponent<LevelBlock>() != null;
                if (levelBlock)
                {
                    if (!CollectOccupancy(level, child, exclusions, out reason)) return false;
                    continue;
                }
                Payable payable = child.GetComponent<Payable>();
                bool marked = persistent != null || payable != null
                    || child.GetComponent<ForestItem>() != null
                    || child.GetComponent<ScatteredObject>() != null;
                if (marked)
                {
                    bool hasBounds = TryCollectBounds(child.gameObject, out float minX, out float maxX);
                    if (hasBounds)
                        exclusions.Add(new MountIslandRecoveryInterval(minX, maxX));
                    if (payable != null && payable.gameObject != null)
                    {
                        Rect overlap = level.GetOverlapRegion(payable.gameObject);
                        if (float.IsFinite(overlap.xMin) && float.IsFinite(overlap.xMax))
                            exclusions.Add(new MountIslandRecoveryInterval(overlap.xMin, overlap.xMax));
                    }
                    if (marked && hasBounds) continue;   // 对象整棵子树已被 bounds 覆盖
                }
                else if (!TryCollectDirectColliderBounds(child.gameObject, exclusions, out reason)) return false;
                if (!CollectOccupancy(level, child, exclusions, out reason)) return false;
            }
            catch (Exception e)
            {
                reason = "occupancy scan failed: " + e.GetType().Name;
                return false;
            }
        }
        return true;
    }

    // Unmarked art is not occupancy; each direct solid collider still protects its own interval.
    private static bool TryCollectDirectColliderBounds(GameObject root,
        List<MountIslandRecoveryInterval> exclusions, out string reason)
    {
        reason = string.Empty;
        try
        {
            Collider2D[] colliders = root.GetComponents<Collider2D>();
            if (colliders == null)
            {
                reason = "occupancy direct colliders unreadable";
                return false;
            }
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider2D collider = colliders[i];
                if (collider == null || collider.gameObject == null)
                {
                    reason = "occupancy direct collider unreadable";
                    return false;
                }
                if (!collider.gameObject.activeInHierarchy || !collider.enabled || collider.isTrigger) continue;
                Bounds bounds = collider.bounds;
                if (!float.IsFinite(bounds.min.x) || !float.IsFinite(bounds.max.x)
                    || bounds.max.x < bounds.min.x)
                {
                    reason = "occupancy direct collider bounds invalid";
                    return false;
                }
                exclusions.Add(new MountIslandRecoveryInterval(bounds.min.x, bounds.max.x));
            }
            return true;
        }
        catch (Exception e)
        {
            reason = "occupancy direct collider read failed: " + e.GetType().Name;
            return false;
        }
    }

    private static bool TryPlanPlacements(MountIslandRecoveryPlan plan, PlacementModel model,
        out MountIslandRecoveryPlacement[] placements, out string reason)
    {
        placements = Array.Empty<MountIslandRecoveryPlacement>();
        reason = string.Empty;
        var requests = new List<MountIslandRecoveryPlacementRequest>(plan.Creates.Count);
        for (int i = 0; i < plan.Creates.Count; i++)
        {
            MountIslandRecoveryArm arm = plan.Creates[i];
            if (!MountIslandRecoveryGeometry.TryGetFacility(arm.DefinitionId, out var row))
            {
                reason = "geometry row missing for " + arm.DefinitionId;
                return false;
            }
            requests.Add(new MountIslandRecoveryPlacementRequest(arm.DefinitionId, row.ReservedHalfWidth));
        }

        float ppu;
        try { ppu = Dejiggler.pixelsPerUnit; }
        catch (Exception e) { reason = "Dejiggler pixelsPerUnit read failed: " + e.GetType().Name; return false; }
        if (!float.IsFinite(ppu) || ppu <= 0f)
        {
            reason = "Dejiggler pixelsPerUnit invalid";
            return false;
        }

        return MountIslandRecoveryPlacementPolicy.TryPlan(
            requests, model.Tiles, model.Exclusions, model.LevelMin, model.LevelMax,
            x => Dejiggler.RoundToPixels(x),
            (x, half) => ProbePlacement(x, half, out _),
            out placements, out reason);
    }

    private static bool ProbePlacement(float x, float halfWidth, out string reason)
    {
        reason = string.Empty;
        if (!CoinCourierGround.TryStandable(x - halfWidth, out float left)
            || !CoinCourierGround.TryStandable(x, out float center)
            || !CoinCourierGround.TryStandable(x + halfWidth, out float right))
        {
            reason = "ground/obstacle probe failed at " + x;
            return false;
        }
        const float heightTolerance = 0.05f;
        if (Math.Abs(left - center) > heightTolerance || Math.Abs(center - right) > heightTolerance)
        {
            reason = "probe heights differ at " + x;
            return false;
        }
        return true;
    }

    // ------------------------------------------------------------------ bounds helpers

    private static bool TryCollectBounds(GameObject root, out float minX, out float maxX)
    {
        minX = 0f;
        maxX = 0f;
        bool any = false;
        SpriteRenderer[] sprites = root.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < sprites.Length; i++)
        {
            SpriteRenderer sprite = sprites[i];
            if (sprite == null || !sprite.enabled || !sprite.gameObject.activeInHierarchy) continue;
            Bounds bounds = sprite.bounds;
            if (!float.IsFinite(bounds.min.x) || !float.IsFinite(bounds.max.x)
                || bounds.max.x < bounds.min.x) continue;
            minX = any ? Math.Min(minX, bounds.min.x) : bounds.min.x;
            maxX = any ? Math.Max(maxX, bounds.max.x) : bounds.max.x;
            any = true;
        }
        Collider2D[] colliders = root.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider2D collider = colliders[i];
            if (collider == null || collider.isTrigger
                || !collider.enabled || !collider.gameObject.activeInHierarchy) continue;
            Bounds bounds = collider.bounds;
            if (!float.IsFinite(bounds.min.x) || !float.IsFinite(bounds.max.x)
                || bounds.max.x < bounds.min.x) continue;
            minX = any ? Math.Min(minX, bounds.min.x) : bounds.min.x;
            maxX = any ? Math.Max(maxX, bounds.max.x) : bounds.max.x;
            any = true;
        }
        return any;
    }

    // ------------------------------------------------------------------ clone host

    private sealed class CloneRecord
    {
        internal MountIslandRecoveryArm Arm;
        internal Transform Root;
        internal Vector3 ExpectedPosition;
        internal float RootX;
        internal float HalfWidth;
    }

    private sealed class RecoveryHost : IMountIslandRecoveryHost
    {
        private readonly Capture _capture;
        private readonly Level _level;
        private readonly Transform _layer;
        private readonly List<CloneRecord> _records = new List<CloneRecord>();
        private GameObject _staging;

        internal RecoveryHost(Capture capture, Level level, Transform layer)
        {
            _capture = capture;
            _level = level;
            _layer = layer;
        }

        internal void Dispose()
        {
            if (_staging != null)
            {
                // 事务异常逃逸时的兜底：只清仍挂在私有 staging 下的未提交 roots；
                // 已 reparent 到 GameLayer 的 committed/unknown roots 保持不动。
                for (int i = 0; i < _records.Count; i++)
                {
                    Transform root = _records[i].Root;
                    if (root == null || root.parent == null) continue;
                    if (PointerOf(root.parent.gameObject) == PointerOf(_staging))
                        DestroyOwnedRoot(root);
                }
                _records.Clear();
                UnityEngine.Object.Destroy(_staging);
                _staging = null;
            }
        }

        public bool TryCreateInactive(MountIslandRecoveryArm arm, float rootX, out int handle, out string reason)
        {
            handle = -1;
            reason = string.Empty;
            try
            {
                if (!MountIslandRecoveryTemplates.TryGet(arm.DefinitionId, _level,
                        out MountIslandRecoveryTemplates.Entry template, out reason))
                    return false;
                if (!EnsureStaging(out reason)) return false;

                float posX = rootX - _staging.transform.position.x - template.Facility.localPosition.x;
                Transform clone = template.Block.CloneObject(posX, _staging, template.Facility);
                if (clone == null || clone.gameObject == null)
                {
                    reason = "CloneObject returned null";
                    return false;
                }
                Vector3 expected = Dejiggler.RoundToPixels(
                    _staging.transform.position + template.Facility.localPosition + new Vector3(posX, 0f, 0f));

                // 先锁定 inactive，再核位置/父节点/身份。CloneObject 自己的 SetActive(true) 在
                // inactive staging 下仍为 inactiveInHierarchy。
                clone.gameObject.SetActive(false);
                var record = new CloneRecord
                {
                    Arm = arm,
                    Root = clone,
                    ExpectedPosition = expected,
                    RootX = rootX,
                    HalfWidth = template.Row.ReservedHalfWidth,
                };
                _records.Add(record);
                handle = _records.Count - 1;

                if (clone.parent == null || PointerOf(clone.parent.gameObject) != PointerOf(_staging))
                {
                    reason = "clone not parented to staging";
                    return false;
                }
                if (!SamePosition(clone.position, expected, 0.003f))
                {
                    reason = "clone position != native snap";
                    return false;
                }
                if (!TryReadRootPersistent(clone, arm.DefinitionId, out _, out reason)) return false;
                if (!IsFacilityComplete(clone, arm.DefinitionId, _layer.gameObject.scene.handle, out reason))
                    return false;
                return true;
            }
            catch (Exception e)
            {
                reason = "clone failed: " + e.GetType().Name + " " + e.Message;
                return false;
            }
        }

        public bool TryValidateCreatedBatch(out string reason)
        {
            reason = string.Empty;
            if (!RecheckScope(out reason)) return false;
            for (int i = 0; i < _records.Count; i++)
            {
                CloneRecord record = _records[i];
                if (record.Root == null || record.Root.gameObject == null)
                {
                    reason = "created root destroyed";
                    return false;
                }
                if (record.Root.parent == null || PointerOf(record.Root.parent.gameObject) != PointerOf(_staging))
                {
                    reason = "created root not under staging";
                    return false;
                }
                if (record.Root.gameObject.activeSelf
                    || record.Root.gameObject.activeInHierarchy)
                {
                    reason = "created root active before activation";
                    return false;
                }
                if (!SamePosition(record.Root.position, record.ExpectedPosition, 0.003f))
                {
                    reason = "created root position drifted";
                    return false;
                }
                if (!TryReadRootPersistent(record.Root, record.Arm.DefinitionId, out _, out reason)) return false;
                if (!IsFacilityComplete(record.Root, record.Arm.DefinitionId,
                        _layer.gameObject.scene.handle, out reason)) return false;
            }
            return true;
        }

        public bool TryActivateCreatedBatch(out string reason)
        {
            reason = string.Empty;
            // 全批先 reparent（仍 inactive），再统一激活。
            for (int i = 0; i < _records.Count; i++)
            {
                CloneRecord record = _records[i];
                record.Root.SetParent(_layer, true);
                if (record.Root.parent == null || PointerOf(record.Root.parent.gameObject) != PointerOf(_layer.gameObject))
                {
                    reason = "reparent failed";
                    return false;
                }
                if (!SamePosition(record.Root.position, record.ExpectedPosition, 0.003f))
                {
                    reason = "world position changed on reparent";
                    return false;
                }
                if (record.Root.gameObject.scene.handle != _layer.gameObject.scene.handle)
                {
                    reason = "reparented root scene mismatch";
                    return false;
                }
                if (record.Root.gameObject.activeSelf || record.Root.gameObject.activeInHierarchy)
                {
                    reason = "root activated before batch activation";
                    return false;
                }
            }
            for (int i = 0; i < _records.Count; i++)
            {
                _records[i].Root.gameObject.SetActive(true);
            }
            for (int i = 0; i < _records.Count; i++)
            {
                CloneRecord record = _records[i];
                if (!record.Root.gameObject.activeInHierarchy)
                {
                    reason = "root not active after activation";
                    return false;
                }
                if (!SamePosition(record.Root.position, record.ExpectedPosition, 0.003f))
                {
                    reason = "post-activation position drifted";
                    return false;
                }
                if (!TryReadRootPersistent(record.Root, record.Arm.DefinitionId, out _, out reason)) return false;
                if (!IsFacilityComplete(record.Root, record.Arm.DefinitionId,
                        _layer.gameObject.scene.handle, out reason)) return false;
                if (!TryCollectBounds(record.Root.gameObject, out float minX, out float maxX))
                {
                    reason = "created root has no readable bounds";
                    return false;
                }
                const float margin = 0.01f;
                if (minX < record.RootX - record.HalfWidth - margin
                    || maxX > record.RootX + record.HalfWidth + margin)
                {
                    reason = "created root bounds exceed reserved half width";
                    return false;
                }
            }
            if (!RecheckScope(out reason)) return false;
            return true;
        }

        public void DiscardCreatedBatch(string reason)
        {
            for (int i = _records.Count - 1; i >= 0; i--)
                DestroyOwnedRoot(_records[i].Root);
            _records.Clear();
            if (!string.IsNullOrEmpty(reason)) Log("created batch discarded: " + reason);
        }

        private static void DestroyOwnedRoot(Transform root)
        {
            if (root == null || root.gameObject == null) return;
            try { root.gameObject.SetActive(false); } catch (Exception) { }
            try
            {
                Persistent[] persistents = root.GetComponentsInChildren<Persistent>(true);
                for (int p = 0; p < persistents.Length; p++)
                {
                    if (persistents[p] == null) continue;
                    try { persistents[p].DontPersistInstance(false, null); }
                    catch (Exception e)
                    {
                        LogError("DontPersistInstance failed during cleanup: " + e.GetType().Name);
                    }
                }
            }
            catch (Exception e)
            {
                LogError("owned Persistent scan failed during cleanup: " + e.GetType().Name);
            }
            try { UnityEngine.Object.Destroy(root.gameObject); }
            catch (Exception e) { LogError("destroy failed during cleanup: " + e.GetType().Name); }
        }

        public MountIslandMarkerCommitOutcome CommitMarkers(int[] markerTypes, out string reason)
        {
            reason = string.Empty;
            try
            {
                if (!RecheckScope(out reason))
                    return MountIslandMarkerCommitOutcome.NotCommitted;
                if (!ExtensionIslandRuntime.TryResolveRawOwner(out GlobalSaveData global,
                        out _, out CampaignSaveData owner)
                    || global == null || PointerOf(global) != _capture.Scope.GlobalPointer
                    || owner == null || PointerOf(owner) != _capture.Scope.CampaignPointer)
                {
                    reason = "captured raw Global/owner no longer selected";
                    return MountIslandMarkerCommitOutcome.NotCommitted;
                }
                return MountIslandMarkerRuntime.TryCommitMarkers(
                    _capture.Scope.GlobalPointer,
                    owner,
                    _capture.Scope.ReignIndex,
                    _capture.Scope.LandDataPointer,
                    _capture.Scope.Land,
                    _capture.MarkerPreimage,
                    markerTypes,
                    out _,
                    out reason);
            }
            catch (Exception e)
            {
                reason = "marker commit failed before write: " + e.GetType().Name;
                return MountIslandMarkerCommitOutcome.NotCommitted;
            }
        }

        private bool RecheckScope(out string reason)
        {
            reason = string.Empty;
            if (_capture.Island == null || _capture.Island.objects != null)
            {
                reason = "target island objects not cleared";
                return false;
            }
            if (!TryReadScope(_capture.Scope.SelectedIndex, _capture.Scope.Land, _capture.Island,
                    out MountIslandRecoveryScope now, out _, out _, out _, out reason))
                return false;
            if (!MountIslandRecoveryScope.SameLoad(_capture.Scope, now))
            {
                reason = "load scope changed after post confirmation";
                return false;
            }
            return TryMatchSavedLevelEdges(_capture, out reason);
        }

        private bool EnsureStaging(out string reason)
        {
            reason = string.Empty;
            if (_staging != null) return true;
            try
            {
                _staging = new GameObject(StagingName);
                _staging.SetActive(false);
                Scene target = _layer.gameObject.scene;
                if (_staging.scene.handle != target.handle)
                    SceneManager.MoveGameObjectToScene(_staging, target);
                Transform staging = _staging.transform;
                staging.SetParent(null, true);
                staging.position = _layer.position;
                staging.rotation = _layer.rotation;
                staging.localScale = _layer.lossyScale;
                if (!SamePosition(staging.position, _layer.position, 1e-5f)
                    || !SameRotation(staging.rotation, _layer.rotation)
                    || !SameScale(staging.lossyScale, _layer.lossyScale))
                {
                    reason = "staging transform mismatch";
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                reason = "staging create failed: " + e.GetType().Name + " " + e.Message;
                return false;
            }
        }
    }

    private static bool TryReadRootPersistent(Transform root, string definitionId, out Persistent persistent,
        out string reason)
    {
        persistent = null;
        reason = string.Empty;
        try
        {
            persistent = root.GetComponent<Persistent>();
            if (persistent == null)
            {
                reason = "root has no Persistent";
                return false;
            }
            if (!MountIslandRecoveryGeometry.TryGetFacility(definitionId, out var row))
            {
                reason = "geometry row missing";
                return false;
            }
            if (!string.Equals(persistent.path, row.FacilityPersistentPath, StringComparison.Ordinal))
            {
                reason = "root Persistent.path mismatch";
                return false;
            }
            return true;
        }
        catch (Exception e)
        {
            reason = "root Persistent read failed: " + e.GetType().Name;
            return false;
        }
    }

    private static bool SamePosition(Vector3 a, Vector3 b, float tolerance)
    {
        return float.IsFinite(a.x) && float.IsFinite(a.y) && float.IsFinite(a.z)
            && Math.Abs(a.x - b.x) <= tolerance
            && Math.Abs(a.y - b.y) <= tolerance
            && Math.Abs(a.z - b.z) <= tolerance;
    }

    private static bool SameRotation(Quaternion a, Quaternion b)
    {
        return Math.Abs(a.x - b.x) <= 1e-5f && Math.Abs(a.y - b.y) <= 1e-5f
            && Math.Abs(a.z - b.z) <= 1e-5f && Math.Abs(a.w - b.w) <= 1e-5f;
    }

    private static bool SameScale(Vector3 a, Vector3 b)
    {
        return float.IsFinite(a.x) && float.IsFinite(a.y) && float.IsFinite(a.z)
            && Math.Abs(a.x - b.x) <= 1e-5f
            && Math.Abs(a.y - b.y) <= 1e-5f
            && Math.Abs(a.z - b.z) <= 1e-5f;
    }

    // ------------------------------------------------------------------ logging / pointers

    private static ulong PointerOf(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase value)
    {
        try { return value != null ? (ulong)value.Pointer.ToInt64() : 0UL; }
        catch (Exception) { return 0UL; }
    }

    private static void Log(string message)
    {
        try { KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(LogPrefix + " " + message); }
        catch (Exception) { }
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

    private static void LogError(string message)
    {
        try { KingdomEnhancedPlugin.Instance?.LogSource.LogError(LogPrefix + " " + message); }
        catch (Exception) { }
    }
}

/// <summary>
/// C 专用独占 inactive 模板 holder：不读取/不修改 <c>CrossWorldMountRuntime.Templates</c>，
/// 不修改原资源；每定义只保留一个私有 block 实例，源设施 root.activeSelf=false。
/// </summary>
internal static class MountIslandRecoveryTemplates
{
    private const string LogPrefix = "[MountIslandRecovery]";
    private const string HolderName = "KEM_MountIslandRecoveryTemplates";
    private static readonly Dictionary<string, Entry> Cache = new Dictionary<string, Entry>(StringComparer.Ordinal);
    private static GameObject _holder;

    internal sealed class Entry
    {
        internal LevelBlock Block;
        internal Transform Facility;
        internal string GameLayerName;
        internal MountIslandRecoveryGeometry.FacilityRow Row;
    }

    internal static bool TryGet(string definitionId, Level level, out Entry entry, out string reason)
    {
        entry = null;
        reason = string.Empty;
        if (!TryResolveGameLayerName(level, out string gameLayerName, out reason)) return false;
        if (Cache.TryGetValue(definitionId, out Entry cached))
        {
            if (IsValid(cached) && string.Equals(cached.GameLayerName, gameLayerName, StringComparison.Ordinal))
            {
                entry = cached;
                return true;
            }
            if (cached != null && cached.Block != null)
                Destroy(cached.Block.gameObject);
            Cache.Remove(definitionId);
        }
        try
        {
            CrossWorldMountDefinition definition = FindDefinition(definitionId);
            if (definition == null) { reason = "definition not in catalog"; return false; }
            if (!MountIslandRecoveryGeometry.TryGetFacility(definitionId, out var row))
            {
                reason = "geometry row missing";
                return false;
            }
            if (_holder == null)
            {
                _holder = new GameObject(HolderName);
                UnityEngine.Object.DontDestroyOnLoad(_holder);
                _holder.SetActive(false);
            }
            GameObject prefab = Resources.Load<GameObject>(definition.BlockResourcePath);
            if (prefab == null) { reason = "block resource missing"; return false; }
            if (!string.Equals(prefab.name, definition.BlockObjectName, StringComparison.Ordinal))
            {
                reason = "block resource name mismatch";
                return false;
            }
            GameObject instance = UnityEngine.Object.Instantiate(prefab, _holder.transform);
            if (instance == null) { reason = "block instantiate failed"; return false; }
            instance.SetActive(false);
            LevelBlock block = instance.GetComponent<LevelBlock>();
            if (block == null) { reason = "template has no LevelBlock"; Destroy(instance); return false; }
            if (block.groupOne != (LevelBlockGroup)definition.GroupOne
                || block.groupTwo != (LevelBlockGroup)definition.GroupTwo)
            {
                reason = "template group mismatch";
                Destroy(instance);
                return false;
            }
            try { block.ComputeDimensions(); } catch (Exception) { }
            try { block.BuildIndex(); } catch (Exception) { }
            if (block.GetWidth() <= 0) { reason = "template width invalid"; Destroy(instance); return false; }

            Persistent[] persistents = instance.GetComponentsInChildren<Persistent>(true);
            Transform facility = null;
            int matches = 0;
            for (int i = 0; i < persistents.Length; i++)
            {
                Persistent persistent = persistents[i];
                if (persistent == null) continue;
                string path;
                try { path = persistent.path; }
                catch (Exception e)
                {
                    reason = "template Persistent.path read failed: " + e.GetType().Name;
                    Destroy(instance);
                    return false;
                }
                if (!string.Equals(path, row.FacilityPersistentPath, StringComparison.Ordinal)) continue;
                matches++;
                facility = persistent.transform;
            }
            if (matches != 1 || facility == null)
            {
                reason = "template facility path count " + matches;
                Destroy(instance);
                return false;
            }
            int layerCandidates = 0;
            Transform templateLayer = null;
            for (int childIndex = 0; childIndex < instance.transform.childCount; childIndex++)
            {
                Transform child = instance.transform.GetChild(childIndex);
                if (child == null || child.gameObject == null) continue;
                if (!string.Equals(child.gameObject.name, gameLayerName, StringComparison.Ordinal)) continue;
                layerCandidates++;
                templateLayer = child;
            }
            bool directChild = facility.parent != null && templateLayer != null
                && PointerOf(facility.parent.gameObject) == PointerOf(templateLayer.gameObject);
            if (!MountIslandRecoveryPolicy.IsTemplateFacilityParentValid(
                    true, layerCandidates, directChild))
            {
                reason = "facility is not direct child of unique template GameLayer";
                Destroy(instance);
                return false;
            }
            if (block.cameraBlockMarker != null
                && (PointerOf(block.cameraBlockMarker) == PointerOf(facility.gameObject)
                    || facility.IsChildOf(block.cameraBlockMarker.transform)))
            {
                reason = "facility is cameraBlockMarker";
                Destroy(instance);
                return false;
            }
            if (facility.GetComponentsInChildren<PrefabPlaceholder>(true).Length > 0)
            {
                reason = "template facility contains PrefabPlaceholder";
                Destroy(instance);
                return false;
            }
            facility.gameObject.SetActive(false);
            if (facility.gameObject.activeSelf)
            {
                reason = "template facility source is active";
                Destroy(instance);
                return false;
            }
            entry = new Entry
            {
                Block = block,
                Facility = facility,
                GameLayerName = gameLayerName,
                Row = row,
            };
            Cache[definitionId] = entry;
            return true;
        }
        catch (Exception e)
        {
            reason = "template load failed: " + e.GetType().Name + " " + e.Message;
            entry = null;
            return false;
        }
    }

    private static bool TryResolveGameLayerName(Level level, out string name, out string reason)
    {
        name = null;
        reason = string.Empty;
        try
        {
            if (level == null) { reason = "Level unavailable for template layer"; return false; }
            GameObject layer = level.GetLayer(ContentLayers.GameLayer);
            if (layer == null) { reason = "ContentLayers.GameLayer missing"; return false; }
            name = layer.name;
            if (string.IsNullOrEmpty(name)) { reason = "GameLayer name empty"; return false; }
            return true;
        }
        catch (Exception e)
        {
            reason = "GameLayer resolve failed: " + e.GetType().Name;
            return false;
        }
    }

    private static bool IsValid(Entry entry)
    {
        try
        {
            return entry != null && entry.Block != null && entry.Facility != null
                && entry.Facility.gameObject != null && !entry.Facility.gameObject.activeSelf
                && entry.Row.DefinitionId != null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static CrossWorldMountDefinition FindDefinition(string definitionId)
    {
        CrossWorldMountDefinition[] definitions = CrossWorldMountCatalog.Definitions;
        for (int i = 0; i < definitions.Length; i++)
        {
            if (string.Equals(definitions[i].Id, definitionId, StringComparison.Ordinal)) return definitions[i];
        }
        return null;
    }

    private static void Destroy(GameObject instance)
    {
        try { if (instance != null) UnityEngine.Object.Destroy(instance); }
        catch (Exception e) { LogError("template cleanup failed: " + e.GetType().Name); }
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

[HarmonyPatch(typeof(IslandSaveData), nameof(IslandSaveData.TryPopObjectsToScene))]
internal static class IslandSaveData_TryPopObjectsToScene_MountIslandRecovery_Patch
{
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static void Prefix(IslandSaveData __instance, bool __runOriginal,
        out MountIslandRecoveryToken __state)
    {
        __state = MountIslandRecoveryRuntime.BeforePop(__instance, __runOriginal);
    }

    [HarmonyPostfix]
    private static void Postfix(IslandSaveData __instance, bool __result, bool __runOriginal,
        MountIslandRecoveryToken __state)
    {
        MountIslandRecoveryRuntime.AfterPop(__instance, __result, __runOriginal, __state);
    }

    [HarmonyFinalizer]
    private static Exception Finalizer(Exception __exception, bool __result, bool __runOriginal,
        MountIslandRecoveryToken __state)
    {
        MountIslandRecoveryRuntime.FinalizePop(__exception, __result, __runOriginal, __state);
        return __exception;
    }
}

[HarmonyPatch(typeof(Managers), nameof(Managers.OnLevelLoaded))]
internal static class Managers_OnLevelLoaded_MountIslandRecovery_Patch
{
    [HarmonyPostfix]
    private static void Postfix(Managers __instance, bool fromSave, bool __runOriginal)
    {
        MountIslandRecoveryRuntime.AfterLevelLoaded(__instance, fromSave, __runOriginal);
    }
}
