// 私有 land12（天朝12专用）净场策略：restore 边界的一次版本化迁移 + OWN 模板发布前净化 + 纯路径判定。
//
// 职责与边界（对齐 worker-island-content-r1 合同）：
// - Enabled = PALACE_ISLAND_OWNED_PRIVATE=1 + PalaceCourtArea.Enabled（后者已含
//   PALACE_COURT_AREA_PRIVATE / PALACE_WORLD_FULL_PRIVATE / PalaceLand12Runtime.Enabled 完整私有
//   env/root 链 + ModConfig.Enabled）。禁用时零 hook、零写入、零效果。
// - InstallOnce 手动挂载 IslandSaveData.TryPopObjectsToScene() Prefix（HarmonyPriority First，
//   先于同方法默认优先级的前缀与原方法）：仅对 exact campaign._islands[12]（typed __instance 同指针）
//   做一次 v1 版本化净场迁移。两阶段：先有界只读分类（<=8192 条）并审计 retained→removed 的
//   parentObject.linkedObjectID 与 componentData2[*].data 精确 uniqueID 引用；零冲突才新建
//   Il2Cpp List、按原顺序保留原 wrapper 引用，并对 island.objects 做一次赋值 + 回读核验。
//   只改此一个 List 字段；其余字段 / 0..11 岛 / 钱包 / 玩家 / 坐骑一概不动；保留记录不做默认值
//   或 link 补 null。空 List / nothing-to-remove / fresh 原样透传且不记假成功。
// - 身份键 = islandPtr + durableGUID + slot + reign（身份只读 PalaceTripContext.TryBinding，不新写）。
//   同键只 attempt 一次：成功记键跳过后续扫描；失败 sticky 记录并令本次 restore 返回 false，
//   不盲重试、不留部分发布。context 门（land/scope/CurrentLand/save 中）未过一律透传不阻断。
// - island.repopGrassHack==true：立即停止迁移并说明 grass 重铺 writer 未确认，绝不 silent set false，
//   原方法照常透传。
// - 原 TryPopObjectsToScene 成功路径必须继续执行；不逐条屏蔽 TryCreateOrFind 返回 null，
//   不读 Start.__instance，不写 nativeSave/备份（安全保存由 Root 保证）。
// - SanitizeOwnedBlock：Root 在 OWN 模板已与 borrowed 源分离、仍 inactive 时调用的发布前净化。
//   只删确证 OWN 的草/树/未建墙塔视觉源 GO；删除前枚举一次 GetComponentsInChildren<Persistent>(true)
//   并确证候选子树不含任何允许 Persistent / Tile / Ground / Player / BeggarCamp / Campfire（营地泊位），
//   有冲突即 abort 不猜；责任登记后 DestroyImmediate（inactive 模板的确定性销毁）；失败 sticky。
//   不改 borrowed prefab、不改 native 原始列表、不处理实际 world 对象、不每帧扫描、
//   不修改 TileData/GroundCollider；后续 BuildIndex/ComputeDimensions/geometry 核验由 Root 负责。
// - IsForbiddenPath / IsGroundTilePath：纯 Ordinal 完整匹配，供 Root 统一接入 terrain 与
//   managed allocation 最终入口；不按 name 或 StartsWith 推断。
// - SOURCE-only：本文件不声明 native 验收。interop 证据来自当前任务 interop 程序集反编译
//   （IslandSaveData.ObjectData 17 字段 / PersistentLink.linkedObjectID / repopGrassHack /
//   CurrentlySavingIsland / CampaignSaveData._islands+CurrentLand / Persistent.path）与
//   receipts/world-area-cold-r3-observed/global-v35-at-observation 存档路径分布（206 条禁路径、
//   retained→removed 引用为 0）。
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using HarmonyLib;
using KingdomEnhancedMod.PalaceSource;
using UnityEngine;

namespace KingdomEnhancedMod;

internal static class PalaceIslandContentPolicy
{
    internal const int LandIndex = PalaceLand12Runtime.LandIndex;
    private const string EnableEnv = "PALACE_ISLAND_OWNED_PRIVATE";
    private const string LogTag = "[IslandContent]";
    internal const int MigrationVersion = 1;

    // 有界扫描（观测存档 438 条 / componentData2 少量短 JSON；超界按完整性异常 fail-closed）。
    private const int MaxObjects = 8192;
    private const int MaxComponentEntriesPerObject = 64;
    private const int MaxComponentDataChars = 65536;
    private const int MaxTotalScanChars = 4194304;
    private const int MaxHierarchyDepth = 256;
    private const int MaxSanitizeEnumerations = 16384;
    private const int MaxSanitizeCandidates = 4096;

    // 精确视觉禁止路径（25 个，Ordinal 完整匹配）。历史视觉分布见world-area-cold-r3-observed。
    // Forest是原生逻辑区域；57fc实测该live实例无任何Renderer，不删除其状态职责。
    // 本分类不代表其他版本视觉或Native背景初始化已验证。历史旧表还包含Forest：
    // 138 Grass + 10 Tower0_greece + 8 Wall0 + 47 Trees(8 种) + 3 Forest = 206。
    private static readonly string[] ForbiddenPathList =
    {
        "Prefabs/Vegetation/Grass",
        "Prefabs/Buildings and Interactive/Wall0",
        "Prefabs/Buildings and Interactive/greece/Tower0_greece",
        "Prefabs/Vegetation/Greece/TreeBGPine_Greece",
        "Prefabs/Vegetation/Greece/TreeBGCypress_Greece",
        "Prefabs/Vegetation/Greece/TreeBGJuniper_Greece",
        "Prefabs/Vegetation/Greece/TreeBGOlive_Greece",
        "Prefabs/Vegetation/Greece/TreeCypress_Greece",
        "Prefabs/Vegetation/Greece/TreePine_Greece",
        "Prefabs/Vegetation/Greece/TreeWild_Pear_Greece",
        "Prefabs/Vegetation/Greece/TreeOlive_Greece",
        "Prefabs/Environment/greece/Quest_Pillars_Greece",
        "Prefabs/Environment/greece/BackdropNear_Greece",
        "Prefabs/Environment/greece/BackdropFar_Greece",
        "Prefabs/Environment/greece/Hill_Greece",
        "Prefabs/Environment/greece/Rocks_Greece",
        "Prefabs/Vegetation/Greece/Shrub_Greece",
        "Prefabs/Vegetation/Greece/ShrubBig_Greece",
        "Prefabs/Vegetation/Greece/ShrubBigBG_Greece",
        "Prefabs/Vegetation/Greece/Moss_Greece",
        "Prefabs/Vegetation/Greece/Boulder_Greece",
        "Prefabs/Vegetation/Greece/ForegroundStones_01_Greece",
        "Prefabs/Vegetation/Greece/ForegroundStones_02_Greece",
        "Prefabs/Vegetation/Greece/ForegroundStonesBirds_01_Greece",
        "Prefabs/Wildlife/Fireflies",
    };

    private static readonly HashSet<string> ForbiddenPaths = new(ForbiddenPathList, StringComparer.Ordinal);

    // 地面 Tile 模板路径（exact：Tile Big / Tile Forest / Tile Clearing）。
    private static readonly HashSet<string> GroundTilePathSet = new(StringComparer.Ordinal)
    {
        "Prefabs/Environment/Tile Big",
        "Prefabs/Environment/Tile Forest",
        "Prefabs/Environment/Tile Clearing",
        "Prefabs/Environment/Tile Forest Building",
    };

    private static bool _installed;
    private static string _migratedKey = "";
    private static string _stickyFailKey = "";
    private static string _stickyFailReason = "";
    private static bool _saveWindowLogged;

    // Sanitize 责任登记（进程内可观察；已删除 OWN 源 GO 指针累积，不重置）。
    private static bool _sanitizeSticky;
    private static string _sanitizeFailReason = "";
    private static readonly List<ulong> _sanitizeDeletedPtrs = new();
    private static int _sanitizeDeletedCount;

    /// <summary>env 门 + 完整私有 root/env 链（经 PalaceCourtArea.Enabled）全部满足才启用。</summary>
    internal static bool Enabled
    {
        get
        {
            try
            {
                return PalaceRuntimeProfile.Capability(EnableEnv)
                    && PalaceCourtArea.Enabled;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>精确禁止路径判定（pure；null/非完整匹配均 false，不推断）。</summary>
    internal static bool IsForbiddenPath(string path)
        => path != null && ForbiddenPaths.Contains(path);

    /// <summary>地面 Tile 模板路径判定（pure；exact Tile Big / Tile Forest / Tile Clearing）。</summary>
    internal static bool IsGroundTilePath(string path)
        => path != null && GroundTilePathSet.Contains(path);

    /// <summary>
    /// Enabled 时挂载 TryPopObjectsToScene Prefix（Priority.First）。目标不可解析时只记录、
    /// 不挂载、不抛出、不重试。
    /// </summary>
    internal static void InstallOnce(Harmony harmony)
    {
        // C：安装与动态 Enabled 分离——固定 profile 能力即安装；ModConfig/运行期门只约束动作。
        if (_installed || !PalaceRuntimeProfile.Capability(EnableEnv)) return;
        _installed = true;
        try
        {
            var target = AccessTools.Method(typeof(IslandSaveData), "TryPopObjectsToScene", Type.EmptyTypes);
            if (target == null)
            {
                Log("ERROR IslandSaveData.TryPopObjectsToScene() not resolvable; island content policy NOT installed");
                return;
            }
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(PalaceIslandContentPolicy), nameof(TryPopObjectsToScenePrefix)));
            Log("v" + MigrationVersion + " installed: IslandSaveData.TryPopObjectsToScene() prefix (Priority.First), forbidden paths=" + ForbiddenPathList.Length);
        }
        catch (Exception e)
        {
            Log("ERROR install fault (" + e.GetType().Name + "); island content policy NOT installed");
        }
    }

    /// <summary>
    /// restore 边界净场迁移（v1）。context 门未过 → 透传（true）；迁移失败 → sticky + 本次
    /// restore 返回 false；成功或 nothing-to-remove → true 让原方法继续。
    /// </summary>
    [HarmonyPriority(Priority.First)]
    private static bool TryPopObjectsToScenePrefix(IslandSaveData __instance)
    {
        string key = null;
        try
        {
            if (!Enabled) return true;
            if (__instance == null || __instance.Pointer == IntPtr.Zero) return true;
            if (__instance.land != LandIndex) return true;

            // 非save门：该岛正在序列化保存时绝不动 objects（本 hook 是 restore 路径，正常不并存）。
            IslandSaveData saving = IslandSaveData.CurrentlySavingIsland;
            if (saving != null && saving.Pointer == __instance.Pointer)
            {
                if (!_saveWindowLogged)
                {
                    _saveWindowLogged = true;
                    Log("SKIP land12 touched while it is the currently-saving island; no migration this pass");
                }
                return true;
            }

            // offline/owner/biome 门 + campaign 身份（只读，不新写身份）。
            if (!ExtensionIslandRuntime.TryScope(out CampaignSaveData campaign)) return true;
            if (campaign.CurrentLand != LandIndex) return true;
            Managers managers = Managers.Inst;
            if (managers == null || managers.game == null || managers.game.currentLand != LandIndex
                || managers.game.state != Game.State.Loading)
            {
                LogOnceKey("not-loading", "SKIP migration outside exact land12 Loading window");
                return true;
            }
            var islands = campaign._islands;
            if (islands == null || islands.Count <= LandIndex) return true;
            IslandSaveData island = islands[LandIndex];
            if (island == null || island.Pointer != __instance.Pointer) return true;   // exact island slot

            if (!PalaceTripContext.TryBinding(GlobalSaveData.loaded, campaign,
                out string campaignGuid, out int slot, out string bindReason))
            {
                // durable 身份不可读（fresh land12 常态）：无身份不迁移，透传，不阻断。
                LogOnceKey("bind-" + bindReason,
                    "SKIP migration: campaign binding unavailable (" + bindReason + "); passthrough");
                return true;
            }
            int reign = campaign.reign;
            key = Ptr(island) + ":" + campaignGuid + ":" + slot + ":" + reign;

            if (key == _migratedKey) return true;                     // 本进程同身份已迁移
            if (key == _stickyFailKey)
            {
                Log("BLOCK restore (sticky failure, no retry): " + _stickyFailReason + " key=" + key);
                return false;
            }

            var objects = island.objects;
            if (objects == null || objects.Count == 0) return true;   // 空 List 透传
            if (objects.Count > MaxObjects)
            {
                Fail(key, "objects-over-bound:" + objects.Count);
                return false;
            }

            if (island.repopGrassHack)
            {
                // 明确停止并说明：grass 重铺 writer 未确认；不动该标志，不迁移，透传原生行为。
                Fail(key, "repopGrassHack=true; grass repop writer unconfirmed, flag/list untouched");
                return false;
            }

            // ---- 阶段一：只读分类 + 引用审计（无冲突才允许发布）。
            int count = objects.Count;
            var retained = new List<IslandSaveData.ObjectData>(count);
            var removedIds = new HashSet<string>(StringComparer.Ordinal);
            var removeByPath = new Dictionary<string, int>(StringComparer.Ordinal);
            var seenIds = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < count; i++)
            {
                IslandSaveData.ObjectData od = objects[i];
                if (od == null) { Fail(key, "object-slot-null:" + i); return false; }
                string path = od.prefabPath;
                if (IsForbiddenPath(path))
                {
                    string uid = od.uniqueID;
                    if (!string.IsNullOrEmpty(uid))
                    {
                        if (seenIds.ContainsKey(uid))
                        { Fail(key, "duplicate-uniqueID:" + uid); return false; }
                        seenIds[uid] = i;
                        removedIds.Add(uid);
                    }
                    removeByPath.TryGetValue(path, out int c);
                    removeByPath[path] = c + 1;
                }
                else
                {
                    retained.Add(od);
                    string uid = od.uniqueID;
                    if (!string.IsNullOrEmpty(uid))
                    {
                        if (seenIds.ContainsKey(uid))
                        { Fail(key, "duplicate-uniqueID:" + uid); return false; }
                        seenIds[uid] = i;
                    }
                }
            }

            if (removedIds.Count == 0) return true;   // nothing-to-remove / fresh：透传，不记假成功

            // retained → removed 精确 uniqueID 引用审计（linkedObjectID + componentData2 JSON）。
            long scanned = 0;
            for (int i = 0; i < retained.Count; i++)
            {
                IslandSaveData.ObjectData od = retained[i];
                var link = od.parentObject;   // ValueType 装箱 wrapper；getter 恒非 null 引用
                if (link != null)
                {
                    string linkedId = link.linkedObjectID;
                    if (!string.IsNullOrEmpty(linkedId) && removedIds.Contains(linkedId))
                    { Fail(key, "retained-link-to-removed:" + linkedId + " at " + (od.prefabPath ?? "")); return false; }
                }
                var cds = od.componentData2;
                if (cds == null) continue;
                int entries = cds.Count;
                if (entries > MaxComponentEntriesPerObject)
                { Fail(key, "component-entries-over-bound:" + entries); return false; }
                for (int j = 0; j < entries; j++)
                {
                    IslandSaveData.ObjectData.ComponentData cd = cds[j];
                    if (cd == null) continue;
                    string data = cd.data;
                    if (string.IsNullOrEmpty(data)) continue;
                    if (data.Length > MaxComponentDataChars || scanned + data.Length > MaxTotalScanChars)
                    { Fail(key, "component-scan-over-budget:len=" + data.Length + " total=" + scanned); return false; }
                    scanned += data.Length;
                    if (ContainsRemovedReference(data, removedIds, out string referenced))
                    { Fail(key, "retained-json-ref-to-removed:" + referenced + " at " + (od.prefabPath ?? "")); return false; }
                }
            }

            // ---- 阶段二：新 List + 一次赋值 + 回读核验（只改 island.objects 这一个字段）。
            var rebuilt = new Il2CppSystem.Collections.Generic.List<IslandSaveData.ObjectData>();
            for (int i = 0; i < retained.Count; i++) rebuilt.Add(retained[i]);
            ulong oldListPtr = Ptr(objects);
            island.objects = rebuilt;   // 单次字段写（FieldWriteWbarrierStub）
            var readback = island.objects;
            if (readback == null || Ptr(readback) != Ptr(rebuilt) || readback.Count != retained.Count || Ptr(readback) == oldListPtr)
            {
                Fail(key, "publish-readback-mismatch retained=" + retained.Count
                    + " readback=" + (readback != null ? readback.Count : -1));
                return false;
            }

            _migratedKey = key;
            Log("v" + MigrationVersion + " migrated land12 objects " + count + " -> " + retained.Count
                + " (removed " + (count - retained.Count) + ") key=" + key
                + " remove-by-path={" + FormatCounts(removeByPath) + "} oldList=" + Hex(oldListPtr)
                + " ; native TryPop continues");
            return true;
        }
        catch (Exception e)
        {
            if (key != null)
            {
                Fail(key, "fault:" + e.GetType().Name);
                return false;
            }
            // 身份未知前的 context 读故障：透传（可能是瞬态），不 sticky。
            Log("WARN context read fault (passthrough): " + e.GetType().Name);
            return true;
        }
    }

    /// <summary>
    /// OWN 模板发布前净化（Root 在已与 borrowed 源分离的 inactive owner root 上调用）。
    /// 只删除确证 OWN 的禁路径源 GO；候选子树含任何允许 Persistent / Tile / Ground / Player /
    /// BeggarCamp / Campfire 即 abort；责任登记后 DestroyImmediate；失败 sticky 返回 false。
    /// </summary>
    internal static bool SanitizeOwnedBlock(LevelBlock owned, GameObject exactInactiveOwnerRoot, out string reason)
    {
        reason = "";
        try
        {
            if (!Enabled) { reason = "policy-disabled"; return false; }
            if (_sanitizeSticky) { reason = "sanitize-sticky:" + _sanitizeFailReason; return false; }
            if (owned == null || ReferenceEquals(owned, null) || exactInactiveOwnerRoot == null
                || ReferenceEquals(exactInactiveOwnerRoot, null))
            { reason = "input-null"; return false; }

            ulong rootPtr = Ptr(exactInactiveOwnerRoot);
            ulong ownedGoPtr = Ptr(owned != null ? owned.gameObject : null);
            if (rootPtr == 0 || ownedGoPtr == 0 || ownedGoPtr == rootPtr) { reason = "input-pointer"; return false; }
            if (exactInactiveOwnerRoot.activeInHierarchy) { reason = "owner-root-active-hierarchy"; return false; }
            if (owned.transform == null || !IsUnderRoot(owned.transform, rootPtr))
            { reason = "owned-not-under-owner-root"; return false; }

            // 单次 Persistent 枚举（含 inactive）。
            var entries = owned.gameObject.GetComponentsInChildren<Persistent>(true);
            if (entries == null || entries.Length > MaxSanitizeEnumerations)
            { reason = "persistent-enumeration-over-bound"; return SanitizeFail(reason); }

            var candidates = new Dictionary<ulong, GameObject>();
            var candidatePtrs = new HashSet<ulong>();
            var candidateLabels = new Dictionary<ulong, string>();
            for (int i = 0; i < entries.Length; i++)
            {
                Persistent entry = entries[i];
                if (entry == null || entry.gameObject == null || entry.transform == null)
                { reason = "persistent-entry-null:" + i; return SanitizeFail(reason); }
                if (!IsForbiddenPath(entry.path)) continue;
                ulong goPtr = Ptr(entry.gameObject);
                if (goPtr == 0) { reason = "candidate-pointer-zero:" + i; return SanitizeFail(reason); }
                if (!candidates.ContainsKey(goPtr))
                {
                    candidates[goPtr] = entry.gameObject;
                    candidatePtrs.Add(goPtr);
                    candidateLabels[goPtr] = entry.path;
                }
            }
            if (candidates.Count == 0) { reason = "no-forbidden-sources"; return true; }
            if (candidates.Count > MaxSanitizeCandidates)
            { reason = "candidates-over-bound:" + candidates.Count; return SanitizeFail(reason); }

            // 审计一：允许 Persistent 不得位于任何候选子树内（含同 GO：从自身起算）。
            for (int i = 0; i < entries.Length; i++)
            {
                Persistent entry = entries[i];
                if (IsForbiddenPath(entry.path)) continue;   // 被取消的源对象本身不审计
                if (TryFindAncestorPointer(entry.transform, candidatePtrs, out ulong hit))
                { reason = "allowed-persistent-under-forbidden:" + (entry.path ?? "") + "@" + Hex(hit); return SanitizeFail(reason); }
            }
            // 审计二：受保护组件类型（Tile / Ground / Player / 营地泊位）不得位于候选子树内。
            // interop 泛型调用固定在具体类型调用点（与仓内已证用法一致），helper 只做托管迭代。
            string found;
            if (AnyUnderCandidates(owned.gameObject.GetComponentsInChildren<Tile>(true), candidatePtrs, "Tile", out found)
                || AnyUnderCandidates(owned.gameObject.GetComponentsInChildren<Ground>(true), candidatePtrs, "Ground", out found)
                || AnyUnderCandidates(owned.gameObject.GetComponentsInChildren<Player>(true), candidatePtrs, "Player", out found)
                || AnyUnderCandidates(owned.gameObject.GetComponentsInChildren<BeggarCamp>(true), candidatePtrs, "BeggarCamp", out found)
                || AnyUnderCandidates(owned.gameObject.GetComponentsInChildren<Campfire>(true), candidatePtrs, "Campfire", out found))
            { reason = "protected-component-under-forbidden:" + found; return SanitizeFail(reason); }

            // 责任登记先行，再销毁（inactive 模板用 DestroyImmediate：同步完成，供 Root 随后
            // BuildIndex/ComputeDimensions 的 geometry 复核看到已删除状态）。
            var names = new StringBuilder();
            foreach (KeyValuePair<ulong, GameObject> kv in candidates)
            {
                _sanitizeDeletedPtrs.Add(kv.Key);
                if (names.Length > 0) names.Append(',');
                names.Append(candidateLabels[kv.Key]).Append('@').Append(Hex(kv.Key));
            }
            int destroyed = 0;
            foreach (KeyValuePair<ulong, GameObject> kv in candidates)
            {
                try { UnityEngine.Object.DestroyImmediate(kv.Value); destroyed++; }
                catch (Exception e)
                { reason = "destroy-fault:" + e.GetType().Name + "@" + Hex(kv.Key); return SanitizeFail(reason); }
            }
            _sanitizeDeletedCount += destroyed;
            Log("v" + MigrationVersion + " sanitized OWN block: destroyed " + destroyed + " forbidden source GOs ["
                + names + "] total=" + _sanitizeDeletedCount + " root=" + Hex(rootPtr)
                + " ; borrowed prefab untouched, native lists untouched");
            reason = "destroyed=" + destroyed;
            return true;
        }
        catch (Exception e)
        {
            reason = "fault:" + e.GetType().Name;
            return SanitizeFail(reason);
        }
    }

    // ------------------------------------------------------------------ 内部辅助

    private static void Fail(string key, string reason)
    {
        _stickyFailKey = key;
        _stickyFailReason = reason;
        Log("ERROR migration failed (sticky, restore blocked, no retry): " + reason + " key=" + key);
    }

    private static bool SanitizeFail(string reason)
    {
        _sanitizeSticky = true;
        _sanitizeFailReason = reason;
        Log("ERROR sanitize failed (sticky): " + reason);
        return false;
    }

    private static string FormatCounts(Dictionary<string, int> counts)
    {
        var keys = new List<string>(counts.Keys);
        keys.Sort(StringComparer.Ordinal);
        var text = new StringBuilder();
        for (int i = 0; i < keys.Count; i++)
        {
            if (i > 0) text.Append(';');
            text.Append(keys[i]).Append('=').Append(counts[keys[i]]);
        }
        return text.ToString();
    }

    internal static bool ContainsRemovedReference(string data, HashSet<string> removed, out string referenced)
    {
        using JsonDocument document = JsonDocument.Parse(data, new JsonDocumentOptions { MaxDepth = 64 });
        return Contains(document.RootElement, removed, out referenced);
        static bool Contains(JsonElement element, HashSet<string> ids, out string found)
        {
            found = "";
            if (element.ValueKind == JsonValueKind.String)
            {
                string value = element.GetString();
                if (value != null && ids.Contains(value)) { found = value; return true; }
            }
            else if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (ids.Contains(property.Name)) { found = property.Name; return true; }
                    if (Contains(property.Value,ids,out found)) return true;
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
                foreach (JsonElement child in element.EnumerateArray())
                    if (Contains(child,ids,out found)) return true;
            return false;
        }
    }

    private static bool IsUnderRoot(Transform transform, ulong rootPtr)
    {
        int depth = 0;
        for (Transform cur = transform; cur != null && depth < MaxHierarchyDepth; depth++)
        {
            if (Ptr(cur.gameObject) == rootPtr) return true;
            cur = cur.parent;
        }
        return false;
    }

    private static bool TryFindAncestorPointer(Transform transform, HashSet<ulong> targets, out ulong hit)
    {
        hit = 0;
        int depth = 0;
        for (Transform cur = transform; cur != null && depth < MaxHierarchyDepth; depth++)
        {
            ulong ptr = Ptr(cur.gameObject);
            if (ptr != 0 && targets.Contains(ptr)) { hit = ptr; return true; }
            cur = cur.parent;
        }
        return false;
    }

    /// <summary>受保护组件不得出现在候选子树内；枚举槽为 null 视为异常（不猜，直接拦）。</summary>
    private static bool AnyUnderCandidates<T>(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<T> comps,
        HashSet<ulong> candidatePtrs, string label, out string found)
        where T : Component
    {
        found = "";
        if (comps == null) return false;
        if (comps.Length > MaxSanitizeEnumerations) { found = label + ":enumeration-over-bound"; return true; }
        for (int i = 0; i < comps.Length; i++)
        {
            T comp = comps[i];
            if (comp == null || ReferenceEquals(comp, null) || comp.transform == null)
            { found = label + ":null-slot:" + i; return true; }
            if (TryFindAncestorPointer(comp.transform, candidatePtrs, out ulong hit))
            { found = label + "@" + Hex(hit); return true; }
        }
        return false;
    }

    private static readonly HashSet<string> _loggedKeys = new(StringComparer.Ordinal);

    private static void LogOnceKey(string key, string message)
    {
        try { if (_loggedKeys.Add(key)) Log(message); } catch (Exception) { }
    }

    private static ulong Ptr(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase value)
        => PalacePrivateConfigFactory.PointerOf(value);

    private static string Hex(ulong value) => PalacePrivateConfigFactory.Hex(value);

    private static void Log(string message)
    {
        try { PalaceVisibleEntry.Log(LogTag + " " + message); } catch (Exception) { }
    }
}
