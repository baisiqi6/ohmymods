// Native fresh palace court area: dedicated private44u branch, legacy48u branch unchanged.
// Dedicated scope owns one inactive draft, validates actual nonshared layout/zero ledger before Last publication,
// and publishes schema2 only after exact native CloneInto, 11 tiles and authoritative44u collider readback.
// No new hook/periodic correction; normal/legacy paths retain their former behavior.
//
// 职责与边界（对齐 worker-court-area 合同）：
// - Enabled = PALACE_COURT_AREA_PRIVATE=1 + PalaceLand12Runtime.Enabled（BepInEx root / fresh-observation
//   save root 完全匹配）+ ModConfig.Enabled（总开关）。禁用时零 hook、零写入、零效果。
// - InstallOnce 手动挂载（无 attribute 自动注册）：Level.GenerateInternal(LevelConfig,int)
//   Prefix/Postfix/Finalizer 建立"本次 fresh land12 生成"scope；LevelLayout.GetBlocks Postfix
//   Priority.Last（在既有 PatchWorld_Level_GetBlocks 之后执行）每 scope 一次注入；LevelBlock.CloneInto
//   Postfix 只观测自有 clone 的原生克隆跨度；PatchWorld_TowerSpots/WallSpots.Schedule(World) Prefix
//   在确证私有 land12 离线 scope 返回 false 阻止新增 KEM 塔/墙位（不清理已建对象）。
// - 候选只取本次 layout.blocks 的 Clearing Large_Blocks / Clearing_Blocks / Clearing Small_Blocks
//   精确名模板（group Clearing/None、无 absoluteCenter/cameraMarker/dangerSource），复用 MapWidthTerrain
//   的完整结构核验（GameLayer 直接子对象仅 Tile/Grass、Persistent.prefabPath、组件名多重集、
//   Tile 连贯覆盖且 floor(span)==GetWidth）。不使用被占用的 StartingArea；同键出现第二个不同模板引用
//   即整键歧义拒绝。宽度必须整除 48 且 <=48，取最大者；无纯净候选时记失败日志，绝不伪造 Ground。
// - 自有模板：先建唯一 inactive owner root（DontDestroyOnLoad，立即登记指针），再
//   Object.Instantiate(template.gameObject, parent, false)（子对象保持 activeSelf、层级不激活）；
//   Instantiate 返回后立即记录 clone 指针并核验与模板指针不相等；仅自有 clone 写
//   HideFlags.DontUnloadUnusedAsset 并清空全部地形装饰/结构 scatter 布尔（保留 ground/snow/天气引擎/
//   材质/Tile/Persistent/Grass/路径）；模板字段快照前后必须一致；BuildIndex+ComputeDimensions 重算后
//   GetWidth 必须仍等于模板宽度；不做任何 Tile Stretch/Resize/collider 修改。每个 exact holder 只缓存
//   一份自有模板，不同 holder 拒绝二次分配；失败保留缓存、不再重试。借用原生对象只读，不改不删不移不藏。
// - 发布：保留原生返回列表的顺序与引用，在最后一个原生块之前插入恰好合计 48 宽的自有 clone 引用
//   （要求原列表 >1 块、插入点严格在首末块之间）；layout.blocks / 共享计数 / 随机源一概不动，
//   新 List 替换 ref __result 是本函数最后一个成功动作。冷读已存 land12 不经过 GenerateInternal，
//   天然零注入。
// - Close：以实际 level._levelEdges、记录到的原生 CloneInto 跨度（posX+GetLeft() 起、GetWidth() 宽）
//   验证数量=计划、跨度连续、合计恰 48，全部通过才存当前 Level 指针与 span；失败不虚构 span。
// - TryGetSpan：本进程 fresh span 只认 exact level + 当前私有 land12；冷进程读严格单 JSON sidecar
//   （Paths.ConfigPath/KingdomEnhancedMod/ModSave/palace-court-area.v1.json，闭 schema、有限值、宽 48、
//   存档 nativeEdges 必须等于当前 level._levelEdges，身份取 PalaceTripContext.TryBinding），并做一次
//   有界只读活体 Tile 覆盖/结构 scatter 复核；不救援、不写 RuntimeTiles、不触碰玩家位置。fresh 元数据
//   只在 TryBinding 就绪后持久化一次（原子 temp->move + 读回复核；CourtPlaying 时刻可补延迟持久化），
//   不写金币/nativeSave/历史身份记录。
// - SOURCE-only：本文件不声明 native 验收、node acceptance 或 NPC 行为；interop 签名证据来自
//   receipts/world-levelblock-interop.cs 与 world-tile-interop.cs 及仓内已证用法。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BepInEx;
using HarmonyLib;
using Il2CppInterop.Runtime;
using KingdomEnhancedMod.PalaceSource;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>冷进程 span 元数据（闭 schema v1；public get/set 供 strict System.Text.Json 读写）。</summary>
internal sealed class PalaceCourtAreaMetadata
{
    [JsonPropertyName("v")] public int V { get; set; }
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("campaignGuid")] public string CampaignGuid { get; set; } = "";
    [JsonPropertyName("campaignSlot")] public int CampaignSlot { get; set; } = -1;
    [JsonPropertyName("reign")] public int Reign { get; set; } = -1;
    [JsonPropertyName("nativeEdgesMin")] public int NativeEdgesMin { get; set; }
    [JsonPropertyName("nativeEdgesMax")] public int NativeEdgesMax { get; set; }
    [JsonPropertyName("left")] public double Left { get; set; }
    [JsonPropertyName("right")] public double Right { get; set; }
    [JsonPropertyName("blockWidth")] public int BlockWidth { get; set; }
    [JsonPropertyName("addedBlocks")] public int AddedBlocks { get; set; }
}

/// <summary>
/// 私有 land12 宫廷区（fresh 全固定美术的净空地面）。Root 已在 KingdomEnhancedPlugin.Init 预挂
/// <see cref="InstallOnce"/>；ArrivalPlacement / CourtVisuals / WorldLayout 通过
/// <see cref="TryGetSpan"/> 取 span 中点做宫廷中心与布局边界。
/// </summary>
internal static class PalaceCourtArea
{
    internal const int LandIndex = PalaceLand12Runtime.LandIndex;

    /// <summary>本区新增宽度预算（恰好 48；模板宽度必须整除它）。</summary>
    internal const int CourtAddedWidth = 48;
    private const string EnableEnv = "PALACE_COURT_AREA_PRIVATE";
    private const string OwnerRootName = "KEM_PalaceCourtArea_Owner";
    private const string SidecarFile = "palace-court-area.v1.json";
    private const string MetadataKind = "palace-court-area";
    private const int MetadataVersion = 1;
    private const int MaxJsonChars = 2048;
    private const float CoverageTolerance = 0.001f;

    private const string ClearingLargeBlockName = "Clearing Large_Blocks";
    private const string ClearingBlockName = "Clearing_Blocks";
    private const string ClearingSmallBlockName = "Clearing Small_Blocks";

    // template-boundary.md 实测（同 MapWidthTerrain）：可复制地形对象的确切组件组合与原生持久化路径。
    private const string TransformName = "Transform";
    private const string SpriteRendererName = "SpriteRenderer";
    private const string RandomizeSpriteName = "RandomizeSprite";
    private const string TileName = "Tile";
    private const string GrassName = "Grass";
    private const string PersistentName = "Persistent";
    private const string BiomeSpriteSwapperName = "BiomeSpriteSwapper";
    private const string FixedTransformName = "FixedTransform";
    private const string GapsName = "GAPS";
    private const string PoolStamperName = "PoolStamper";

    private const string TileForestPath = "Prefabs/Environment/Tile Forest";
    private const string TileClearingPath = "Prefabs/Environment/Tile Clearing";
    private const string GrassPath = "Prefabs/Vegetation/Grass";

    private static readonly string[] TileComponentNames =
    {
        TransformName, SpriteRendererName, RandomizeSpriteName, TileName,
        PersistentName, BiomeSpriteSwapperName, FixedTransformName,
    };

    private static readonly string[] GrassComponentNames =
    {
        TransformName, SpriteRendererName, GrassName, GapsName,
        PersistentName, FixedTransformName, BiomeSpriteSwapperName, PoolStamperName,
    };

    private static readonly string[] MetadataExpectedKeys =
    {
        "v", "kind", "campaignGuid", "campaignSlot", "reign", "nativeEdgesMin",
        "nativeEdgesMax", "left", "right", "blockWidth", "addedBlocks",
    };

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = false,
    };

    private static readonly HashSet<string> ContentLayerNames = BuildContentLayerNames();
    private static readonly HashSet<string> LoggedKeys = new HashSet<string>(StringComparer.Ordinal);

    private enum TerrainObjectKind { None, Tile, Grass }

    private sealed class GenerationScope
    {
        internal bool Active;
        internal bool Closed;
        internal bool Consumed;
        internal bool Published;
        internal ulong LevelPtr;
        internal bool Dedicated;
        internal ulong ConfigPtr, GlobalPtr, CampaignPtr, GamePtr, HolderPtr, LayoutPtr;
        internal LevelConfig Config;
        internal int Reign, Seed;
        internal string CampaignGuid = "";
        internal int CampaignSlot = -1;
        internal ulong DedicatedOwnedPtr;
        internal string Failure = "";
        internal int PlannedAdds;
        internal int TemplateWidth;
        internal int TemplateTiles;
        internal int TemplateGrass;
        internal readonly List<CourtSpan> Spans = new();
    }

    private sealed class CandidateGroup
    {
        internal string Key;
        internal string Name;
        internal int Width;
        internal int Tiles;
        internal int Grass;
        internal LevelBlock Representative;
        internal bool Conflict;
    }

    private struct CourtSpan
    {
        internal float Left;
        internal float Right;
    }

    private static readonly List<GenerationScope> Scopes = new();

    // This bounded fresh producer makes at most one allocation per process. Success remains
    // strongly owned for native CloneInto; failure cleanup is authoritative at this independent root.
    private static bool _dedicatedAllocationAttempted, _dedicatedCleanupAttempted;
    private static string _dedicatedAllocationFailure = "";
    private static GameObject _dedicatedOwnerRoot;
    private static GameObject _dedicatedBeachLeft, _dedicatedBeachRight;
    private static LevelBlock _dedicatedOwned;
    private static ulong _dedicatedOwnerPtr, _dedicatedHolderPtr;
    private static GenerationScope _dedicatedSpanScope, _dedicatedAllocationScope;

    // 唯一自有模板缓存（每 exact holder 一份；不同 holder 拒绝；失败 sticky 不重试）。
    private static bool _allocationSticky;
    private static string _allocationFailure = "";
    private static GameObject _ownerRoot;   // CLR wrapper 引用保持（与 DDOL 双保险）
    private static ulong _ownerRootPtr;
    private static LevelBlock _ownedClone;
    private static ulong _ownedClonePtr;      // LevelBlock 组件指针（CloneInto 观测门用）
    private static ulong _ownedCloneGoPtr;
    private static ulong _ownedTemplatePtr;
    private static ulong _ownedHolderPtr;
    private static int _ownedTemplateWidth;
    private static string _ownedTemplateKey = "";
    private static string _ownedTemplateName = "";
    // Dedicated recipe owns the original support blocks too. Allocation is one attempt per holder;
    // no authored source block is modified, and a failed partial allocation is retained.
    private static bool _supportAllocationAttempted, _supportAllocationReady;
    private static ulong _supportHolderPtr;
    private static string _supportAllocationFailure = "";
    private static readonly List<GameObject> SupportOwnedRoots = new();
    private static readonly List<LevelBlock> SupportOwnedBlocks = new();
    private static readonly List<ulong> SupportSourcePointers = new();

    // 本进程已验证 span（exact level 指针 + 范围）。
    private static bool _spanValid;
    private static ulong _spanLevelPtr;
    private static float _spanLeft, _spanRight;
    private static ulong _attemptedGenerationLevelPtr;
    private static bool _freshCoverageValidated;
    private static int _spanGeneration;

    // 元数据持久化：每个已验证 generation 至多成功一次。
    private static int _persistedGeneration = -1;

    // KEM 塔/墙位阻断计数（仅诊断）。
    private static int _blockedTowerCount, _blockedWallCount;

    private static bool _installed;

    /// <summary>env + land12 私有 root + MOD 总开关全部满足才启用；否则所有入口零效果。</summary>
    internal static bool Enabled
    {
        get
        {
            try
            {
                return ModConfig.Enabled != null && ModConfig.Enabled.Value
                    && PalaceLand12Runtime.Enabled
                    && PalaceRuntimeProfile.Capability(EnableEnv)
                    && PalaceRuntimeProfile.Capability("PALACE_WORLD_FULL_PRIVATE");
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>被阻断的 KEM 新增塔位调度次数（只读诊断）。</summary>
    internal static int BlockedTowerSchedules => _blockedTowerCount;

    /// <summary>被阻断的 KEM 新增墙位调度次数（只读诊断）。</summary>
    internal static int BlockedWallSchedules => _blockedWallCount;

    /// <summary>
    /// Enabled 时手动挂载全部 hook（禁用即零 hook）。目标不可解析时只记录、不挂载、不重试。
    /// </summary>
    internal static void InstallOnce(Harmony harmony)
    {
        if (_installed || !PalaceRuntimeProfile.Capability(EnableEnv)
            || !PalaceRuntimeProfile.Capability("PALACE_WORLD_FULL_PRIVATE")) return;
        _installed = true;
        try
        {
            MethodInfo generate = AccessTools.Method(typeof(Level), "GenerateInternal",
                new[] { typeof(LevelConfig), typeof(int) });
            if (generate == null)
            {
                Log("ERROR Level.GenerateInternal(LevelConfig,int) not resolvable; court area NOT installed");
                return;
            }
            harmony.Patch(generate,
                prefix: new HarmonyMethod(typeof(PalaceCourtArea), nameof(GenerateInternalPrefix)),
                postfix: new HarmonyMethod(typeof(PalaceCourtArea), nameof(GenerateInternalPostfix)),
                finalizer: new HarmonyMethod(typeof(PalaceCourtArea), nameof(GenerateInternalFinalizer)));

            MethodInfo getBlocks = AccessTools.Method(typeof(LevelLayout), "GetBlocks");
            if (getBlocks == null)
            {
                Log("ERROR LevelLayout.GetBlocks not resolvable; court area NOT installed");
                return;
            }
            // Priority.Last：本 postfix 必须在既有 PatchWorld_Level_GetBlocks（默认优先级）之后执行，
            // 与仓内已证约定一致（postfix Priority.Last 最后运行、写入最终生效）。
            var latePostfix = new HarmonyMethod(typeof(PalaceCourtArea), nameof(LevelLayoutGetBlocksPostfix))
            {
                priority = Priority.Last,
            };
            harmony.Patch(getBlocks, postfix: latePostfix);

            MethodInfo cloneInto = AccessTools.Method(typeof(LevelBlock), "CloneInto",
                new[] { typeof(Level), typeof(float) });
            if (cloneInto == null)
            {
                Log("ERROR LevelBlock.CloneInto(Level,float) not resolvable; court area NOT installed");
                return;
            }
            harmony.Patch(cloneInto,
                postfix: new HarmonyMethod(typeof(PalaceCourtArea), nameof(CloneIntoPostfix)));

            // Existing managed allocation entries call our scope policy directly; no managed/native detour ambiguity.
            Log("installed: GenerateInternal scope + GetBlocks postfix(last) + CloneInto observe + "
                + "managed tower/wall schedule gate; private land12 only");
        }
        catch (Exception e)
        {
            Log("ERROR install failed: " + e.GetType().Name + " " + e.Message);
        }
    }

    // ------------------------------------------------------------------ 生成 scope

    /// <summary>
    /// 仅确证本次 fresh land12 生成才打开 scope：exact 当前 Managers.level、已发布私有 LevelConfig 名
    /// （Greece_Land_Palace_P12_Service）、campaign/Game 同在 12、普通 Greek 离线 owner。
    /// 其余 land / 其他 world / 联机 / 未发布配置：scope 不激活，原生路径原样透传。
    /// </summary>
    private static void GenerateInternalPrefix(Level __instance, LevelConfig __0, int __1,
        out GenerationScope __state)
    {
        GenerationScope prior = CurrentScope();
        __state = new GenerationScope();
        Scopes.Add(__state);
        if (!Enabled) return;
        if (prior != null && prior.Dedicated && prior.Active && !prior.Closed)
        {
            __state.Dedicated = true;
            throw DedicatedFault(__state, "nested-GenerateInternal");
        }
        if (DedicatedWholeEnabled)
        {
            // Request intent is the actual current private12 campaign and current Level.
            // Config name is a qualification checked by the published-config owner, not a bypass selector.
            Managers requestManagers = Managers.Inst;
            ulong requestLevel = PointerOf(__instance);
            bool currentPrivateRequest = requestLevel != 0 && requestManagers != null
                && PointerOf(requestManagers.level) == requestLevel
                && ExtensionIslandRuntime.TryScope(out CampaignSaveData requestCampaign)
                && requestCampaign.CurrentLand == LandIndex;
            bool namedPrivateRequest = false;
            try
            {
                namedPrivateRequest = __0 != null
                    && string.Equals(__0.name, PalacePrivateConfigPlan.PrivateConfigName, StringComparison.Ordinal);
            }
            catch (Exception e)
            {
                if (currentPrivateRequest)
                {
                    __state.Dedicated = true;
                    throw DedicatedFault(__state, "request-config-name-read:" + e.GetType().Name);
                }
            }
            if (currentPrivateRequest || namedPrivateRequest)
            {
                __state.Dedicated = true;
                OpenDedicatedScope(__instance, __0, __1, __state);
                return;
            }
        }
        try
        {
            if (__instance == null || __0 == null) return;
            string configName;
            try { configName = __0.name; }
            catch (Exception) { return; }
            if (!string.Equals(configName, PalacePrivateConfigPlan.PrivateConfigName, StringComparison.Ordinal))
                return;
            if (NetworkBigBoss.IsOnline) return;
            if (!ExtensionIslandRuntime.TryScope(out CampaignSaveData campaign)) return;
            if (campaign.CurrentLand != LandIndex) return;
            Managers managers = Managers.Inst;
            if (managers == null || managers.level == null || managers.level.Pointer != __instance.Pointer)
                return;
            if (managers.game == null || managers.game.currentLand != LandIndex) return;
            _attemptedGenerationLevelPtr = PointerOf(__instance);
            _spanValid = false;
            _spanLevelPtr = 0;
            _freshCoverageValidated = false;
            __state.Active = true;
            __state.LevelPtr = PointerOf(__instance);
            LogOnce("scope-open", "fresh land12 generation scope opened: level=" + Hex(__state.LevelPtr)
                + " config=" + configName + " seed=" + __1);
        }
        catch (Exception e)
        {
            Log("ERROR scope open fault: " + e.GetType().Name + " " + e.Message);
        }
    }

    private static void GenerateInternalPostfix(Level __instance, GenerationScope __state)
        => CloseScope(__instance, __state);

    /// <summary>原生生成异常：只归还 scope；异常原样返回，绝不吞掉（同 PatchWorld_Level 语义）。</summary>
    private static Exception GenerateInternalFinalizer(Exception __exception, GenerationScope __state)
    {
        try
        {
            if (__state != null && __state.Dedicated && __exception != null)
            {
                _spanValid = false;
                CleanupDedicatedOwner(__state);
            }
            if (__state != null && !__state.Closed)
            {
                __state.Closed = true;
                RemoveScope(__state);
                if (__state.Active)
                    Log("generation threw inside court scope; scope restored, court span withheld"
                        + (__exception != null ? ": " + __exception.GetType().Name : ""));
            }
        }
        catch (Exception)
        {
            // 清理路径异常不吞原生异常。
        }
        return __exception;
    }

    // ------------------------------------------------------------------ GetBlocks 注入

    /// <summary>
    /// 只在激活 scope 内、每 scope 首次调用处理一次；scope 外/未激活/已消费立即返回，
    /// 不读写返回列表。冷读已存 land12 不经 GenerateInternal，天然零注入。
    /// </summary>
    private static void LevelLayoutGetBlocksPostfix(LevelLayout __instance,
        ref Il2CppSystem.Collections.Generic.List<LevelBlock> __result)
    {
        GenerationScope scope = CurrentScope();
        if (scope != null && scope.Dedicated)
        {
            LockDedicatedLayout(scope, __instance);
            if (scope.Consumed) throw DedicatedFault(scope, "duplicate-GetBlocks");
            scope.Consumed = true;
            try { ApplyDedicatedDraft(__instance, ref __result, scope); }
            catch
            {
                CleanupDedicatedOwner(scope);
                throw;
            }
            return;
        }
        if (scope == null || !scope.Active || scope.Consumed) return;
        scope.Consumed = true;
        try
        {
            ApplyCourtArea(__instance, ref __result, scope);
        }
        catch (Exception e)
        {
            scope.Failure = "exception:" + e.GetType().Name;
            Log("ERROR court injection fault (list untouched unless publish already completed): "
                + e.GetType().Name + " " + e.Message);
        }
    }

    // Dedicated scope identity is captured from actual published owners, not inferred from names.
    private static void OpenDedicatedScope(Level level, LevelConfig config, int seed, GenerationScope scope)
    {
        scope.Config = config;
        scope.ConfigPtr = PointerOf(config);
        scope.LevelPtr = PointerOf(level);
        scope.Seed = seed; // Actual GenerateInternal argument; never invented or reseeded.
        Managers requestManagers = Managers.Inst;
        int actualGameLand = requestManagers != null && requestManagers.game != null
            ? requestManagers.game.currentLand : -1;
        Log("dedicated request: level=" + Hex(scope.LevelPtr) + " config=" + Hex(scope.ConfigPtr)
            + " actualGameLand=" + actualGameLand + " seed=" + seed);
        if (!PalaceLand12Runtime.Ready || IslandSaveData.isSavingGame)
            throw DedicatedFault(scope, "generation-not-ready-or-saving");
        if (!PalaceLand12Runtime.TryMatchPublishedGenerationConfig(config, out string publishedReason))
            throw DedicatedFault(scope, "published-config:" + publishedReason);
        if (!string.Equals(SafeName(config), PalacePrivateConfigPlan.PrivateConfigName, StringComparison.Ordinal))
            throw DedicatedFault(scope, "published-config-name-drift");
        if (!CurrentPrivateLand12(level)
            || !ExtensionIslandRuntime.TryScope(out CampaignSaveData campaign))
            throw DedicatedFault(scope, "private12-owner-unavailable:actualGameLand=" + actualGameLand);
        Managers managers = Managers.Inst;
        GlobalSaveData global = GlobalSaveData.loaded;
        scope.GlobalPtr = PointerOf(global);
        scope.CampaignPtr = PointerOf(campaign);
        scope.GamePtr = PointerOf(managers != null ? managers.game : null);
        scope.HolderPtr = PointerOf(BiomeHolder.Inst);
        scope.Reign = campaign.reign;
        if (!PalaceTripContext.TryBinding(global, campaign, out string guid, out int slot, out string bindingReason))
            throw DedicatedFault(scope, "binding:" + bindingReason);
        scope.CampaignGuid = guid;
        scope.CampaignSlot = slot;
        scope.Active = true;
        RequireDedicatedOwner(scope);
        if (_dedicatedAllocationAttempted)
            throw DedicatedFault(scope, "second-fresh-allocation-refused:" + _dedicatedAllocationFailure);
        _attemptedGenerationLevelPtr = scope.LevelPtr;
        _spanValid = false;
        _spanLevelPtr = 0;
        _freshCoverageValidated = false;
        _dedicatedSpanScope = null;
        Log("dedicated scope opened: level=" + Hex(scope.LevelPtr) + " config=" + Hex(scope.ConfigPtr)
            + " campaign=" + Hex(scope.CampaignPtr) + " game=" + Hex(scope.GamePtr)
            + " holder=" + Hex(scope.HolderPtr) + " reign=" + scope.Reign + " seed=" + scope.Seed
            + " guid=" + scope.CampaignGuid + " slot=" + scope.CampaignSlot);
    }

    private static void RequireDedicatedOwner(GenerationScope scope)
    {
        if (scope == null || !scope.Dedicated || !scope.Active || !DedicatedWholeEnabled
            || !PalaceLand12Runtime.Ready || NetworkBigBoss.IsOnline || IslandSaveData.isSavingGame)
            throw DedicatedFault(scope, "inactive-or-not-ready");
        Managers managers = Managers.Inst;
        if (managers == null || PointerOf(managers.level) != scope.LevelPtr
            || managers.game == null || PointerOf(managers.game) != scope.GamePtr
            || managers.game.currentLand != LandIndex || PointerOf(BiomeHolder.Inst) != scope.HolderPtr
            || scope.LevelPtr == 0 || scope.GamePtr == 0 || scope.HolderPtr == 0
            || scope.GlobalPtr == 0 || scope.CampaignPtr == 0 || scope.ConfigPtr == 0
            || PointerOf(scope.Config) != scope.ConfigPtr)
            throw DedicatedFault(scope, "level-config-game-holder-drift");
        if (!PalaceLand12Runtime.TryMatchPublishedGenerationConfig(scope.Config, out string configReason))
            throw DedicatedFault(scope, "published-config:" + configReason);
        if (!string.Equals(SafeName(scope.Config), PalacePrivateConfigPlan.PrivateConfigName, StringComparison.Ordinal))
            throw DedicatedFault(scope, "published-config-name-drift");
        if (!ExtensionIslandRuntime.TryScope(out CampaignSaveData campaign)
            || PointerOf(campaign) != scope.CampaignPtr || campaign.CurrentLand != LandIndex
            || campaign.reign != scope.Reign || PointerOf(GlobalSaveData.loaded) != scope.GlobalPtr)
            throw DedicatedFault(scope, "global-campaign-reign-drift");
        if (!PalaceTripContext.TryBinding(GlobalSaveData.loaded, campaign,
                out string guid, out int slot, out string bindingReason)
            || guid != scope.CampaignGuid || slot != scope.CampaignSlot)
            throw DedicatedFault(scope, "guid-slot-drift:" + bindingReason);
    }

    private static void LockDedicatedLayout(GenerationScope scope, LevelLayout layout)
    {
        RequireDedicatedOwner(scope);
        ulong ptr = PointerOf(layout);
        if (ptr == 0) throw DedicatedFault(scope, "layout-null");
        if (scope.LayoutPtr == 0) scope.LayoutPtr = ptr;
        else if (scope.LayoutPtr != ptr) throw DedicatedFault(scope, "layout-pointer-drift");
    }

    // Called by the existing general MapWidth writer before it allocates/expands anything.
    // This does not consume GetBlocks publication; that remains our Priority.Last postfix.
    internal static bool SuppressGeneralMapWidth(LevelLayout layout)
    {
        GenerationScope scope = CurrentScope();
        if (scope == null || !scope.Dedicated) return false;
        LockDedicatedLayout(scope, layout);
        if (scope.Consumed) throw DedicatedFault(scope, "duplicate-MapWidth-GetBlocks");
        return true;
    }

    private static void ApplyDedicatedDraft(LevelLayout layout,
        ref Il2CppSystem.Collections.Generic.List<LevelBlock> result, GenerationScope scope)
    {
        RequireDedicatedOwner(scope);
        if (PointerOf(layout) != scope.LayoutPtr || result == null)
            throw DedicatedFault(scope, "layout-or-original-result");
        var available = layout.blocks;
        if (available == null || available.Count < 1 || available.Count > 256)
            throw DedicatedFault(scope, "source-candidate-budget:layout=" + Hex(PointerOf(layout))
                + ";available=" + (available == null ? "null" : available.Count.ToString(CultureInfo.InvariantCulture))
                + ";originalResultCount=" + result.Count.ToString(CultureInfo.InvariantCulture));
        int count = available.Count;
        LevelBlock camp = null, spawn = null;
        for (int i = 0; i < count; i++)
        {
            LevelBlock block = available[i];
            if (block == null) throw DedicatedFault(scope, "source-null:" + i);
            string name = block.name;
            if (name == "StartingAreaTight_Greece_Blocks")
            {
                if (camp != null) throw DedicatedFault(scope, "camp-source-ambiguous");
                camp = block;
            }
            else if (name == "PlayerSpawn_Blocks_Greece")
            {
                if (spawn != null) throw DedicatedFault(scope, "spawn-source-ambiguous");
                spawn = block;
            }
        }
        if (available.Count != count || camp == null || spawn == null)
            throw DedicatedFault(scope, "source-membership-or-count-drift");
        RequireDedicatedOwner(scope);
        if (_dedicatedAllocationAttempted)
            throw DedicatedFault(scope, "allocation-already-attempted:" + _dedicatedAllocationFailure);
        _dedicatedAllocationAttempted = true;
        _dedicatedAllocationScope = scope;
        _dedicatedHolderPtr = scope.HolderPtr;
        _dedicatedOwnerRoot = new GameObject("KEM_PalaceDedicated44_Owner");
        _dedicatedOwnerPtr = PointerOf(_dedicatedOwnerRoot);
        if (_dedicatedOwnerPtr == 0) throw DedicatedFault(scope, "owner-allocation-null");
        _dedicatedOwnerRoot.SetActive(false);
        UnityEngine.Object.DontDestroyOnLoad(_dedicatedOwnerRoot);
        if (_dedicatedOwnerRoot.activeSelf || _dedicatedOwnerRoot.activeInHierarchy)
            throw DedicatedFault(scope, "owner-not-inactive");
        if (!PalaceDedicatedDraftProducer.TryPrepare(_dedicatedOwnerRoot, layout, camp, spawn,
                out var staged, out _dedicatedOwned,
                out _dedicatedBeachLeft, out _dedicatedBeachRight, out string preparationReason))
            throw DedicatedFault(scope, "prepare:" + preparationReason);
        ulong ownedPtr = PointerOf(_dedicatedOwned);
        if (ownedPtr == 0 || staged == null || staged.Count != 1 || PointerOf(staged[0]) != ownedPtr
            || !_dedicatedOwned._dimensionsComputed || _dedicatedOwned._width != 44
            || _dedicatedOwned._left != -22f || _dedicatedOwned._right != 22f
            || PointerOf(_dedicatedOwned.transform.parent) != PointerOf(_dedicatedOwnerRoot.transform)
            || _dedicatedOwnerRoot.activeSelf || _dedicatedOwnerRoot.activeInHierarchy)
            throw DedicatedFault(scope, "staged-draft-readback");
        RequireDedicatedOwner(scope);
        if (PointerOf(layout) != scope.LayoutPtr || _dedicatedHolderPtr != scope.HolderPtr)
            throw DedicatedFault(scope, "publish-layout-or-holder-drift");
        scope.PlannedAdds = 1;
        scope.TemplateWidth = 44;
        scope.DedicatedOwnedPtr = ownedPtr;
        scope.Spans.Clear();
        string summary = "dedicated prepared actual layout=" + Hex(scope.LayoutPtr)
            + " owned=" + Hex(ownedPtr) + " finalCount=1 cachedWidth44 left-22 right22; ledger0 verified";
        scope.Published = true;
        result = staged; // Sole successful result publication; no stock fallback or layout-ledger writes.
        Log(summary);
    }

    private static InvalidOperationException DedicatedFault(GenerationScope scope, string reason)
    {
        if (scope != null) scope.Failure = reason;
        _dedicatedAllocationFailure = reason;
        Log("ERROR dedicated attempt stopped: " + reason);
        return new InvalidOperationException("Palace dedicated44: " + reason);
    }

    private static void CleanupDedicatedOwner(GenerationScope scope)
    {
        if (!ReferenceEquals(scope, _dedicatedAllocationScope) || _dedicatedCleanupAttempted) return;
        _dedicatedCleanupAttempted = true;
        try
        {
            // These are only the two exclusive handles returned by this exact allocation.
            // Snapshot both identities before destruction; a read fault quarantines without guessing.
            ulong leftPtr = ReferenceEquals(_dedicatedBeachLeft, null) ? 0 : PointerOf(_dedicatedBeachLeft);
            ulong rightPtr = ReferenceEquals(_dedicatedBeachRight, null) ? 0 : PointerOf(_dedicatedBeachRight);
            bool leftDestroyed = TryDestroyDedicatedEndpoint(_dedicatedBeachLeft, leftPtr);
            bool rightDestroyed = rightPtr != 0 && rightPtr == leftPtr
                ? leftDestroyed : TryDestroyDedicatedEndpoint(_dedicatedBeachRight, rightPtr);
            if (!leftDestroyed || !rightDestroyed)
            {
                _dedicatedAllocationFailure = "cleanup-quarantined:endpoint-unconfirmed";
                Log("ERROR dedicated endpoint cleanup unconfirmed; owner and handles retained, no retry");
                return;
            }
            // Destroy the parent only after both children are confirmed dead, avoiding implicit retries.
            if (!ReferenceEquals(_dedicatedOwnerRoot, null) && _dedicatedOwnerRoot != null)
            {
                if (PointerOf(_dedicatedOwnerRoot) != _dedicatedOwnerPtr
                    || _dedicatedOwnerRoot.activeSelf || _dedicatedOwnerRoot.activeInHierarchy)
                {
                    _dedicatedAllocationFailure = "cleanup-quarantined:owner-identity-or-activity";
                    Log("ERROR dedicated owner cleanup qualification failed; references retained");
                    return;
                }
                UnityEngine.Object.DestroyImmediate(_dedicatedOwnerRoot, false);
                if (_dedicatedOwnerRoot != null)
                {
                    _dedicatedAllocationFailure = "cleanup-quarantined:owner-destruction-unconfirmed";
                    Log("ERROR dedicated owner retained in quarantine; no second cleanup write");
                    return;
                }
            }
            _dedicatedBeachLeft = null;
            _dedicatedBeachRight = null;
            _dedicatedOwnerRoot = null;
            _dedicatedOwned = null;
            _dedicatedOwnerPtr = 0;
            Log("dedicated endpoint and inactive owner destruction confirmed; allocation remains sticky");
        }
        catch (Exception e)
        {
            _dedicatedAllocationFailure = "cleanup-quarantined:" + e.GetType().Name;
            Log("ERROR dedicated cleanup unconfirmed; strong references retained, no retry");
        }
    }

    private static bool TryDestroyDedicatedEndpoint(GameObject endpoint, ulong expectedPtr)
    {
        try
        {
            if (ReferenceEquals(endpoint, null) || endpoint == null) return true;
            if (expectedPtr == 0 || PointerOf(endpoint) != expectedPtr) return false;
            UnityEngine.Object.DestroyImmediate(endpoint, false);
            return endpoint == null;
        }
        catch (Exception e)
        {
            Log("ERROR dedicated endpoint destruction unconfirmed: " + e.GetType().Name);
            return false;
        }
    }

    private static void CloseDedicatedScope(Level level, GenerationScope scope)
    {
        RequireDedicatedOwner(scope);
        if (PointerOf(level) != scope.LevelPtr || !scope.Published || !scope.Consumed
            || scope.Spans.Count != 1 || scope.Spans[0].Left != -22f || scope.Spans[0].Right != 22f)
            throw DedicatedFault(scope, "close-native-clone-span");
        IntRange edges = level._levelEdges;
        if (edges.min != -22 || edges.max != 22)
            throw DedicatedFault(scope, "close-native-edges:" + edges.min + "," + edges.max);
        if (!TryDescribeGround(level, -22f, 22f, out string groundReason))
            throw DedicatedFault(scope, "close-ground-deferred:" + groundReason);
        if (!TryLiveTileCoverage(-22f, 22f, true, out string tileReason))
            throw DedicatedFault(scope, "close-eleven-tiles-deferred:" + tileReason);
        RequireDedicatedOwner(scope);
        _spanValid = true;
        _spanLevelPtr = scope.LevelPtr;
        _spanLeft = -22f;
        _spanRight = 22f;
        _freshCoverageValidated = true;
        _dedicatedSpanScope = scope;
        _spanGeneration++;
        Log("dedicated fresh geometry verified: single44u native span, edges[-22,22], "
            + groundReason + "; " + tileReason + "; source-only wiring until actual Native evidence");
        TryPersistMetadata(level);
    }

    private static void PersistDedicatedMetadata(Level level)
    {
        if (!_spanValid || _dedicatedSpanScope == null || _persistedGeneration == _spanGeneration) return;
        GenerationScope scope = _dedicatedSpanScope;
        RequireDedicatedOwner(scope);
        if (PointerOf(level) != scope.LevelPtr || _spanLevelPtr != scope.LevelPtr
            || !_freshCoverageValidated || _spanLeft != -22f || _spanRight != 22f)
            throw DedicatedFault(scope, "metadata-unverified-span");
        IntRange edges = level._levelEdges;
        var meta = new PalaceCourtAreaMetadata
        {
            V = 2, Kind = "palace-dedicated-island", CampaignGuid = scope.CampaignGuid,
            CampaignSlot = scope.CampaignSlot, Reign = scope.Reign,
            NativeEdgesMin = edges.min, NativeEdgesMax = edges.max,
            Left = -22, Right = 22, BlockWidth = 4, AddedBlocks = 11,
        };
        if (!ValidateMetadata(meta, out string invalid))
            throw DedicatedFault(scope, "metadata-invalid:" + invalid);
        if (!TryWriteSidecarAtomic(meta, out string writeReason, out string readBack))
            throw DedicatedFault(scope, "metadata-write:" + writeReason);
        RequireDedicatedOwner(scope);
        _persistedGeneration = _spanGeneration;
        Log("dedicated v2 metadata persisted: guid=" + scope.CampaignGuid + " slot=" + scope.CampaignSlot
            + " reign=" + scope.Reign + "; atomic/readback " + readBack.Length + " chars");
    }

    private static void ApplyCourtArea(LevelLayout layout,
        ref Il2CppSystem.Collections.Generic.List<LevelBlock> result, GenerationScope scope)
    {
        Il2CppSystem.Collections.Generic.List<LevelBlock> original = result;
        if (original == null) { FailScope(scope, "no-result"); return; }
        if (layout == null) { FailScope(scope, "no-layout"); return; }
        int count = original.Count;
        if (count < 2) { FailScope(scope, "original-blocks=" + count + " (need >1 to insert strictly inside)"); return; }

        ulong holderPtr = PointerOf(BiomeHolder.Inst);
        if (holderPtr == 0) { FailScope(scope, "holder-unavailable"); return; }

        if (!TrySelectClearingTemplate(layout, scope, out LevelBlock template, out int templateWidth, out string templateKey, out string selectReason))
        { FailScope(scope, "no-pure-candidate: " + selectReason); return; }

        if (!TryUseOwnedClone(template, templateWidth, templateKey, holderPtr, out string ownedReason))
        { FailScope(scope, "owned-clone: " + ownedReason); return; }

        if (PalaceIslandContentPolicy.Enabled
            && !TryPrepareOwnedSupportBlocks(original, holderPtr, out string supportReason))
        { FailScope(scope, "dedicated-supports: " + supportReason); return; }

        int added = CourtAddedWidth / templateWidth;

        // 发布前构造完整新列表（保留原引用与相对顺序；插入点在最后一个原生块之前，严格在首末块之间）。
        var inflated = new Il2CppSystem.Collections.Generic.List<LevelBlock>(count + added);
        for (int i = 0; i < count - 1; i++)
            inflated.Add(PalaceIslandContentPolicy.Enabled ? SupportOwnedBlocks[i] : original[i]);
        for (int i = 0; i < added; i++) inflated.Add(_ownedClone);
        inflated.Add(PalaceIslandContentPolicy.Enabled ? SupportOwnedBlocks[count - 1] : original[count - 1]);

        scope.PlannedAdds = added;
        scope.TemplateWidth = templateWidth;
        scope.Spans.Clear();
        string summary = "court planned: layout=" + Hex(PointerOf(layout))
            + " template=" + _ownedTemplateName + " key=" + templateKey
            + " width=" + templateWidth
            + " added=" + added + "x" + templateWidth + " total=" + CourtAddedWidth
            + " original=" + count + " inflated=" + inflated.Count
            + " clone=" + Hex(_ownedClonePtr) + " holder=" + Hex(holderPtr);
        scope.Published = true;
        result = inflated;   // 唯一发布动作（此后仅自身隔离日志）
        Log(summary);
    }

    private static void FailScope(GenerationScope scope, string reason)
    {
        scope.Failure = reason;
        Log("court unsupported reason=" + reason + "; original list kept, no fake ground");
    }

    // ------------------------------------------------------------------ 候选选择（MapWidth 同级核验）

    /// <summary>
    /// 只从本次 layout.blocks 收集 Clearing 精确名候选；命名/组别/标记/结构全部核验通过才入选；
    /// 宽度必须整除 48 且 <=48；同键第二个不同模板引用即整键歧义拒绝；取最大宽度（平手按名字序）。
    /// </summary>
    private static bool TrySelectClearingTemplate(LevelLayout layout, GenerationScope scope,
        out LevelBlock template, out int width, out string key, out string reason)
    {
        template = null;
        width = 0;
        key = null;
        reason = "";
        Il2CppSystem.Collections.Generic.List<LevelBlock> available = layout.blocks;
        if (available == null) { reason = "layout-blocks-null"; return false; }

        var groups = new List<CandidateGroup>();
        var byKey = new Dictionary<string, int>(StringComparer.Ordinal);
        int total = available.Count;
        for (int i = 0; i < total; i++)
        {
            LevelBlock block = available[i];
            if (!ClassifyClearing(block, out int blockWidth, out int tiles, out int grass)) continue;
            string name = SafeName(block);
            string blockKey = name + "|" + ((int)LevelBlockGroup.Clearing).ToString(CultureInfo.InvariantCulture)
                + "|" + blockWidth.ToString(CultureInfo.InvariantCulture);
            if (byKey.TryGetValue(blockKey, out int at))
            {
                if (!ReferenceEquals(groups[at].Representative, block)) groups[at].Conflict = true;
                continue;
            }
            byKey.Add(blockKey, groups.Count);
            groups.Add(new CandidateGroup
            {
                Key = blockKey,
                Name = name,
                Width = blockWidth,
                Representative = block,
                Tiles = tiles,
                Grass = grass,
            });
        }

        CandidateGroup best = null;
        for (int i = 0; i < groups.Count; i++)
        {
            CandidateGroup group = groups[i];
            if (group.Conflict)
            {
                LogOnce("ambiguous-" + group.Key,
                    "candidate key rejected (ambiguous duplicate template): " + group.Key);
                continue;
            }
            if (best == null
                || group.Width > best.Width
                || (group.Width == best.Width && string.CompareOrdinal(group.Name, best.Name) < 0))
                best = group;
        }
        if (best == null) { reason = "candidates=" + groups.Count + " of " + total + " layout blocks"; return false; }

        template = best.Representative;
        width = best.Width;
        key = best.Key;
        _ownedTemplateName = best.Name;
        scope.TemplateTiles = best.Tiles;
        scope.TemplateGrass = best.Grass;
        return true;
    }

    /// <summary>精确名 + 组别 + 标记 + 完整可复制地形内容 + 整除预算共同核验；任一不符即不入候选。</summary>
    private static bool ClassifyClearing(LevelBlock block, out int width, out int tileCount, out int grassCount)
    {
        width = 0;
        tileCount = 0;
        grassCount = 0;
        if (block == null) return false;
        string name = SafeName(block);
        if (!string.Equals(name, ClearingLargeBlockName, StringComparison.Ordinal)
            && !string.Equals(name, ClearingBlockName, StringComparison.Ordinal)
            && !string.Equals(name, ClearingSmallBlockName, StringComparison.Ordinal)) return false;
        if (block.groupOne != LevelBlockGroup.Clearing
            || block.groupTwo != LevelBlockGroup.None
            || block.groupThree != LevelBlockGroup.None) return false;
        if (block.absoluteCenter || block.cameraBlockMarker != null || block.isDangerSource) return false;
        int nativeWidth = block.GetWidth();
        if (nativeWidth <= 0 || nativeWidth > CourtAddedWidth) return false;
        if (CourtAddedWidth % nativeWidth != 0) return false;   // 同模板整数次铺满恰好 48
        if (!VerifyTerrainContent(block, nativeWidth, out tileCount, out grassCount)) return false;
        width = nativeWidth;
        return true;
    }

    /// <summary>
    /// 按原生 CloneInto 的可复制集合核验地形内容（同 MapWidthTerrain.VerifyTerrainContent）：
    /// 激活内容层容器的每个激活直接子对象都必须是确切 Tile 或 Grass 地形对象（含组件组合、
    /// Persistent 有效路径、Grass 池戳），不得有子孙；Tile 正宽、noGround=false、按左缘排序
    /// 连贯覆盖且 floor(span)==原生 GetWidth。
    /// </summary>
    private static bool VerifyTerrainContent(LevelBlock block, int blockWidth, out int tileCount, out int grassCount)
    {
        tileCount = 0;
        grassCount = 0;
        Transform root = block.transform;
        if (root == null) return false;

        var spans = new List<CourtSpan>();
        int layerRoots = 0;
        int childCount = root.childCount;
        for (int i = 0; i < childCount; i++)
        {
            Transform layer = root.GetChild(i);
            if (layer == null) return false;
            GameObject layerObject = layer.gameObject;
            if (layerObject == null || !layerObject.activeSelf) continue;
            if (!ContentLayerNames.Contains(layerObject.name)) continue;
            layerRoots++;
            int innerCount = layer.childCount;
            for (int c = 0; c < innerCount; c++)
            {
                Transform child = layer.GetChild(c);
                if (child == null) return false;
                GameObject childObject = child.gameObject;
                if (childObject == null || !childObject.activeSelf) continue;
                TerrainObjectKind objectKind = MatchTerrainObject(childObject);
                if (objectKind == TerrainObjectKind.None) return false;
                if (objectKind == TerrainObjectKind.Tile)
                {
                    Tile tile = childObject.GetComponent<Tile>();
                    if (tile == null || tile.noGround) return false;
                    float left = tile.GetLeft();
                    float right = tile.GetRight();
                    if (!float.IsFinite(left) || !float.IsFinite(right) || right <= left) return false;
                    spans.Add(new CourtSpan { Left = left, Right = right });
                    tileCount++;
                }
                else grassCount++;
            }
        }
        if (layerRoots == 0 || spans.Count == 0) return false;

        spans.Sort((a, b) => a.Left != b.Left ? a.Left.CompareTo(b.Left) : a.Right.CompareTo(b.Right));
        float covered = spans[0].Right;
        for (int i = 1; i < spans.Count; i++)
        {
            if (spans[i].Left > covered + CoverageTolerance) return false;   // 缝隙：未连贯覆盖
            if (spans[i].Right > covered) covered = spans[i].Right;
        }
        float spanStart = spans[0].Left;
        float spanEnd = spans[spans.Count - 1].Right;
        if (!(spanEnd - spanStart > 0f)) return false;
        return (int)Math.Floor((double)(spanEnd - spanStart)) == blockWidth;
    }

    /// <summary>
    /// 单个可复制地形对象的确切准入（同 MapWidthTerrain.MatchTerrainObject）：无子孙；组件组合与
    /// 已证 Tile/Grass 组合完全一致；Persistent 启用且路径对应原生预制；Grass 的 PoolStamper 指向 Grass 池。
    /// </summary>
    private static TerrainObjectKind MatchTerrainObject(GameObject subject)
    {
        Transform transform = subject.transform;
        if (transform == null || transform.childCount != 0) return TerrainObjectKind.None;

        var components = subject.GetComponents<Component>();
        if (components == null || components.Length == 0) return TerrainObjectKind.None;
        var names = new string[components.Length];
        Persistent persistent = null;
        PoolStamper stamper = null;
        for (int i = 0; i < components.Length; i++)
        {
            Component component = components[i];
            if (component == null) return TerrainObjectKind.None;
            string className = IL2CPP.il2cpp_class_get_name_(component.ObjectClass);
            names[i] = className;
            if (string.Equals(className, PersistentName, StringComparison.Ordinal))
                persistent = component.TryCast<Persistent>();
            else if (string.Equals(className, PoolStamperName, StringComparison.Ordinal))
                stamper = component.TryCast<PoolStamper>();
        }

        bool tileShape = SameNameMultiset(names, TileComponentNames);
        bool grassShape = !tileShape && SameNameMultiset(names, GrassComponentNames);
        if (tileShape == grassShape) return TerrainObjectKind.None;
        if (persistent == null || !persistent.persistObject) return TerrainObjectKind.None;

        string path = persistent.path;
        if (tileShape)
        {
            return path == TileForestPath || path == TileClearingPath
                ? TerrainObjectKind.Tile
                : TerrainObjectKind.None;
        }
        if (stamper == null || stamper.targetPool != PoolStamper.Pool.Grass) return TerrainObjectKind.None;
        return path == GrassPath ? TerrainObjectKind.Grass : TerrainObjectKind.None;
    }

    // ------------------------------------------------------------------ 自有模板分配 / 复用

    /// <summary>
    /// 唯一自有模板取用口：sticky 失败 / 不同 holder / 已缓存 clone 失效或选择漂移一律拒绝；
    /// 同 exact holder 只读复用；首次才走一次有界分配。
    /// </summary>
    private static bool TryUseOwnedClone(LevelBlock template, int templateWidth, string templateKey,
        ulong holderPtr, out string reason)
    {
        reason = "";
        if (_allocationSticky)
        {
            reason = "allocation-sticky:" + _allocationFailure + "; no retry";
            return false;
        }
        if (_ownedClonePtr != 0)
        {
            if (_ownedHolderPtr != holderPtr)
            {
                reason = "different-holder: owned=" + Hex(_ownedHolderPtr)
                    + " current=" + Hex(holderPtr) + "; no second allocation";
                return false;
            }
            if (ReferenceEquals(_ownedClone, null) || _ownedClone == null)
            {
                _allocationSticky = true;
                _allocationFailure = "owned-clone-destroyed";
                reason = "owned clone destroyed; retained cache blocks further allocation";
                return false;
            }
            if (PointerOf(_ownedClone) != _ownedClonePtr
                || (_ownedCloneGoPtr != 0 && PointerOf(_ownedClone.gameObject) != _ownedCloneGoPtr))
            {
                _allocationSticky = true;
                _allocationFailure = "owned-clone-pointer-drift";
                reason = _allocationFailure;
                return false;
            }
            if (_ownedTemplateWidth != templateWidth || _ownedTemplateKey != templateKey)
            {
                reason = "template selection drifted from cached owned clone ("
                    + _ownedTemplateKey + " -> " + templateKey + ")";
                return false;
            }
            int liveWidth = _ownedClone.GetWidth();
            if (liveWidth != _ownedTemplateWidth)
            {
                _allocationSticky = true;
                _allocationFailure = "owned-width-drift:" + liveWidth;
                reason = _allocationFailure;
                return false;
            }
            return true;   // 只读复用，不再触发任何分配路径
        }
        return TryAllocateOwnedClone(template, templateWidth, templateKey, holderPtr, out reason);
    }

    /// <summary>
    /// 一次有界分配：模板快照 -> 建 owner root（立即登记）-> Instantiate（返回后立即记录指针并核验
    /// 与模板不相等）-> 自有对象上的 HideFlag/scatter 清除/维度重算 -> 模板快照复核 -> 发布缓存。
    /// 任一步失败：sticky、保留已建对象、不再重试；借用模板零写入。
    /// </summary>
    private static bool TryAllocateOwnedClone(LevelBlock template, int templateWidth, string templateKey,
        ulong holderPtr, out string reason)
    {
        reason = "";
        ulong templatePtr = PointerOf(template);
        ulong templateGoPtr = PointerOf(template != null ? template.gameObject : null);
        if (templatePtr == 0 || templateGoPtr == 0)
        {
            _allocationSticky = true;
            _allocationFailure = "template-pointer-zero";
            reason = _allocationFailure;
            return false;
        }

        if (!TrySnapshotScatter(template, out string templateBefore, out int templateTiles))
        {
            _allocationSticky = true;
            _allocationFailure = "template-snapshot-failed";
            reason = _allocationFailure + " (no allocation made)";
            return false;
        }

        // Reserve before ANY native allocation/identification; an exception leaves one retained responsibility.
        _allocationSticky = true;
        _allocationFailure = "owned-allocation-in-flight";
        // 唯一 owner root：先创建并立即登记，再做任何可能失败的 Instantiate。
        GameObject ownerRoot = new GameObject(OwnerRootName);
        _ownerRoot = ownerRoot;
        _ownerRootPtr = PointerOf(ownerRoot);
        try { UnityEngine.Object.DontDestroyOnLoad(ownerRoot); }
        catch (Exception e)
        {
            _allocationFailure = "owner-lifetime-failed:" + e.GetType().Name;
            reason = _allocationFailure;
            return false;
        }
        ownerRoot.SetActive(false);   // 子对象保持 activeSelf，层级不激活

        GameObject cloneGo;
        try
        {
            cloneGo = UnityEngine.Object.Instantiate(template.gameObject, ownerRoot.transform, false);
        }
        catch (Exception e)
        {
            _allocationSticky = true;
            _allocationFailure = "instantiate-exception:" + e.GetType().Name;
            reason = _allocationFailure + " root=" + Hex(_ownerRootPtr);
            return false;
        }
        if (ReferenceEquals(cloneGo, null) || cloneGo == null)
        {
            _allocationSticky = true;
            _allocationFailure = "instantiate-null";
            reason = _allocationFailure + " root=" + Hex(_ownerRootPtr);
            return false;
        }

        // 立即记录 clone 指针（先于任何可能失败的字段写入）。
        ulong cloneGoPtr = PointerOf(cloneGo);
        LevelBlock clone = cloneGo.GetComponent<LevelBlock>();
        ulong cloneBlockPtr = PointerOf(clone);
        if (clone == null || cloneGoPtr == 0 || cloneBlockPtr == 0)
        {
            _allocationSticky = true;
            _allocationFailure = "clone-levelblock-missing";
            reason = _allocationFailure + " go=" + Hex(cloneGoPtr);
            return false;
        }
        if (cloneGoPtr == templateGoPtr || cloneBlockPtr == templatePtr)
        {
            _allocationSticky = true;
            _allocationFailure = "clone-alias-of-template:" + Hex(cloneGoPtr);
            reason = _allocationFailure;
            return false;
        }

        int strippedTiles = 0, clearedFlags = 0;
        try
        {
            HideFlags flagsBefore = cloneGo.hideFlags;
            cloneGo.hideFlags = flagsBefore | HideFlags.DontUnloadUnusedAsset;
            if ((cloneGo.hideFlags & HideFlags.DontUnloadUnusedAsset) == 0)
                throw new InvalidOperationException("hide-flag-readback");

            var ownedTiles = new List<Tile>();
            if (!TryCollectContentTiles(cloneGo.transform, ownedTiles)) throw new InvalidOperationException("own Tile enumeration failed");
            foreach (Tile tile in ownedTiles)
            {
                var originalOverrides = tile.versionOverrides;
                var filteredOverrides = new Il2CppSystem.Collections.Generic.List<TileOverrideData>();
                for (int i = 0; originalOverrides != null && i < originalOverrides.Count; i++)
                {
                    var row = originalOverrides[i];
                    if (row != null && !(row.toggleName ?? "").StartsWith("scatter", StringComparison.Ordinal)) filteredOverrides.Add(row);
                }
                tile.versionOverrides = filteredOverrides;
            }
            if (!TryStripScatterFlags(cloneGo, out strippedTiles, out clearedFlags))
                throw new InvalidOperationException("scatter-strip-failed");
            if (PalaceIslandContentPolicy.Enabled
                && !PalaceIslandContentPolicy.SanitizeOwnedBlock(clone, ownerRoot, out string sanitizeReason))
                throw new InvalidOperationException("own-source-sanitize: " + sanitizeReason);

            if (!TrySnapshotScatter(template, out string templateAfter, out int afterTiles)
                || afterTiles != templateTiles || templateAfter != templateBefore)
                throw new InvalidOperationException("template-flags-changed");

            // 维度重算（inactive 层级下 Awake 未跑）：原生 BuildIndex+ComputeDimensions 后 GetWidth
            // 必须仍等于模板宽度 —— 本区不做任何 Stretch/Resize/collider 修改。
            clone.BuildIndex();
            clone.ComputeDimensions();
            int cloneWidth = clone.GetWidth();
            if (cloneWidth != templateWidth)
                throw new InvalidOperationException("clone-width:" + cloneWidth + "/" + templateWidth);

            _ownedClone = clone;
            _ownedClonePtr = cloneBlockPtr;
            _ownedCloneGoPtr = cloneGoPtr;
            _ownedTemplatePtr = templatePtr;
            _ownedHolderPtr = holderPtr;
            _ownedTemplateWidth = templateWidth;
            _ownedTemplateKey = templateKey;
            _allocationSticky = false;
            _allocationFailure = "";
        }
        catch (Exception e)
        {
            _allocationSticky = true;
            _allocationFailure = "owned-clone-init:" + e.Message;
            reason = _allocationFailure + " (clone retained under owner root " + Hex(_ownerRootPtr)
                + ", borrowed template untouched, no retry)";
            Log("ERROR owned clone init failed: " + e.Message + "; cloneGO=" + Hex(cloneGoPtr)
                + " root=" + Hex(_ownerRootPtr));
            return false;
        }

        Log("owned clone allocated: template=" + _ownedTemplateName + " templatePtr=" + Hex(templatePtr)
            + " cloneGO=" + Hex(cloneGoPtr) + " cloneBlock=" + Hex(cloneBlockPtr)
            + " root=" + Hex(_ownerRootPtr) + " width=" + templateWidth
            + " tiles=" + strippedTiles + " scatterFlagsCleared=" + clearedFlags
            + " templateSnapshot=unchanged(" + templateTiles + " tiles)");
        reason = "allocated";
        return true;
    }

    private static bool TryPrepareOwnedSupportBlocks(
        Il2CppSystem.Collections.Generic.List<LevelBlock> original, ulong holderPtr, out string reason)
    {
        reason = "";
        if (_supportAllocationAttempted)
        {
            if (!_supportAllocationReady || _supportHolderPtr != holderPtr
                || original.Count != SupportSourcePointers.Count)
            { reason = _supportAllocationFailure.Length > 0 ? _supportAllocationFailure : "support-owner-drift"; return false; }
            for (int i = 0; i < original.Count; i++)
                if (PointerOf(original[i]) != SupportSourcePointers[i] || SupportOwnedBlocks[i] == null)
                { reason = "support-template-drift"; return false; }
            return true;
        }
        _supportAllocationAttempted = true;
        _supportHolderPtr = holderPtr;
        _supportAllocationFailure = "support-allocation-in-flight";
        try
        {
            if (_ownerRoot == null || _ownerRoot.activeInHierarchy || original.Count > 16)
                throw new InvalidOperationException("inactive support owner or bounded source recipe unavailable");
            for (int i = 0; i < original.Count; i++)
            {
                LevelBlock source = original[i];
                if (source == null || source.gameObject == null
                    || !TrySnapshotScatter(source, out string before, out int sourceTiles) || sourceTiles <= 0)
                    throw new InvalidOperationException("source-support-snapshot:" + i);
                int width = source.GetWidth();
                float left = source.GetLeft();
                string sourceContentBefore = SupportContentFingerprint(source);
                GameObject ownGo = UnityEngine.Object.Instantiate(source.gameObject, _ownerRoot.transform, false);
                SupportOwnedRoots.Add(ownGo); // register before classification or initialization
                if (ownGo == null || ownGo.Pointer == source.gameObject.Pointer)
                    throw new InvalidOperationException("support-clone-alias-or-null:" + i);
                LevelBlock own = ownGo.GetComponent<LevelBlock>();
                if (own == null || own.Pointer == source.Pointer)
                    throw new InvalidOperationException("support-component-alias-or-null:" + i);
                SupportOwnedBlocks.Add(own);
                SupportSourcePointers.Add(PointerOf(source));
                ownGo.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                if ((ownGo.hideFlags & HideFlags.DontUnloadUnusedAsset) == 0)
                    throw new InvalidOperationException("support-lifetime-readback:" + i);
                var ownTiles = new List<Tile>();
                if (!TryCollectContentTiles(ownGo.transform, ownTiles) || ownTiles.Count != sourceTiles)
                    throw new InvalidOperationException("support-tile-enumeration:" + i);
                foreach (Tile tile in ownTiles)
                {
                    var old = tile.versionOverrides;
                    var kept = new Il2CppSystem.Collections.Generic.List<TileOverrideData>();
                    for (int j = 0; old != null && j < old.Count; j++)
                        if (old[j] != null && !(old[j].toggleName ?? "").StartsWith("scatter", StringComparison.Ordinal))
                            kept.Add(old[j]);
                    tile.versionOverrides = kept;
                }
                if (!TryStripScatterFlags(ownGo, out _, out _)
                    || !PalaceIslandContentPolicy.SanitizeOwnedBlock(own, _ownerRoot, out string sanitized))
                    throw new InvalidOperationException("support-source-sanitize:" + i);
                own.BuildIndex();
                own.ComputeDimensions();
                if (own.GetWidth() != width || own.GetLeft() != left)
                    throw new InvalidOperationException("support-geometry-changed:" + i);
                if (!TrySnapshotScatter(source, out string after, out int afterTiles)
                    || before != after || sourceTiles != afterTiles
                    || SupportContentFingerprint(source) != sourceContentBefore)
                    throw new InvalidOperationException("borrowed-support-changed:" + i);
                Log("dedicated support block=" + SafeName(source) + " width=" + width
                    + " source=" + Hex(PointerOf(source)) + " own=" + Hex(PointerOf(own))
                    + " sourceSnapshot=unchanged; ground/spawn/camp/wharf support retained");
            }
            _supportAllocationReady = true;
            _supportAllocationFailure = "";
            return true;
        }
        catch (Exception e)
        {
            _supportAllocationFailure = "support-init:" + e.Message;
            reason = _supportAllocationFailure;
            Log("ERROR dedicated recipe stopped; partial owned supports retained: " + reason);
            return false;
        }
    }

    private static string SupportContentFingerprint(LevelBlock source)
    {
        var entries = source.gameObject.GetComponentsInChildren<Persistent>(true);
        var text = new StringBuilder();
        for (int i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            if (entry == null || entry.gameObject == null) throw new InvalidOperationException("support persistent missing");
            text.Append(entry.Pointer).Append(':').Append(entry.path).Append(':')
                .Append(entry.gameObject.activeSelf).Append(';');
        }
        return text.ToString();
    }

    /// <summary>收集 block 内容层的激活直接子 Tile（Grass 等非 Tile 内容不在此处理）；迭代顺序确定。</summary>
    private static bool TryCollectContentTiles(Transform root, List<Tile> tiles)
    {
        tiles.Clear();
        int childCount = root.childCount;
        for (int i = 0; i < childCount; i++)
        {
            Transform layer = root.GetChild(i);
            if (layer == null) return false;
            GameObject layerObject = layer.gameObject;
            if (layerObject == null || !layerObject.activeSelf) continue;
            if (!ContentLayerNames.Contains(layerObject.name)) continue;
            int innerCount = layer.childCount;
            for (int c = 0; c < innerCount; c++)
            {
                Transform child = layer.GetChild(c);
                if (child == null) return false;
                GameObject childObject = child.gameObject;
                if (childObject == null || !childObject.activeSelf) continue;
                Tile tile = childObject.GetComponent<Tile>();
                if (tile == null) continue;
                tiles.Add(tile);
            }
        }
        return true;
    }

    /// <summary>模板/自有 clone 的地形字段快照（33 个 scatter 布尔 + noGround/noSnow，按确定顺序）。</summary>
    private static bool TrySnapshotScatter(LevelBlock block, out string snapshot, out int tiles)
    {
        snapshot = null;
        tiles = 0;
        try
        {
            if (block == null || block.transform == null) return false;
            var collected = new List<Tile>();
            if (!TryCollectContentTiles(block.transform, collected)) return false;
            var builder = new StringBuilder(collected.Count * 40);
            for (int i = 0; i < collected.Count; i++)
            {
                if (i > 0) builder.Append(';');
                AppendTileFlags(builder, collected[i]);
            }
            snapshot = builder.ToString();
            tiles = collected.Count;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void AppendTileFlags(StringBuilder builder, Tile tile)
    {
        AppendBool(builder, tile.noGround);
        AppendBool(builder, tile.noSnow);
        AppendBool(builder, tile.scatterTree);
        AppendBool(builder, tile.scatterShrub);
        AppendBool(builder, tile.scatterShrubBirds);
        AppendBool(builder, tile.scatterFireflies);
        AppendBool(builder, tile.scatterShrubBig);
        AppendBool(builder, tile.scatterShrubBigBG);
        AppendBool(builder, tile.scatterGlowingTree);
        AppendBool(builder, tile.scatterTreeBG);
        AppendBool(builder, tile.scatterTreeCurtain);
        AppendBool(builder, tile.scatterTreeCurtainMid);
        AppendBool(builder, tile.scatterTreeCurtainFar);
        AppendBool(builder, tile.scatterMoss);
        AppendBool(builder, tile.scatterWaterReed);
        AppendBool(builder, tile.scatterWaterStatue);
        AppendBool(builder, tile.scatterRoots);
        AppendBool(builder, tile.scatterHill);
        AppendBool(builder, tile.scatterRocks);
        AppendBool(builder, tile.scatterWall);
        AppendBool(builder, tile.scatterFarmhouse);
        AppendBool(builder, tile.scatterTower);
        AppendBool(builder, tile.scatterHermesShade);
        AppendBool(builder, tile.scatterEggMound);
        AppendBool(builder, tile.scatterBackdropNear);
        AppendBool(builder, tile.scatterBackdropFar);
        AppendBool(builder, tile.scatterExtremeForegroundItems);
        AppendBool(builder, tile.scatterHumanoidTrees);
        AppendBool(builder, tile.scatterLightBeamBG);
        AppendBool(builder, tile.scatterForegroundCloudLayer1);
        AppendBool(builder, tile.scatterForegroundCloudLayer2);
        AppendBool(builder, tile.scatterForegroundCloudLayer3);
        AppendBool(builder, tile.scatterForegroundCloudLayer4);
        AppendBool(builder, tile.scatterForegroundCloudLayer5);
        AppendBool(builder, tile.scatterForegroundCloudLayer6);
        var overrides = tile.versionOverrides;
        builder.Append("#overrides:").Append(overrides != null ? overrides.Count : -1);
        for (int i = 0; overrides != null && i < overrides.Count; i++)
        {
            var row = overrides[i];
            if (row == null) builder.Append(";null");
            else builder.Append(';').Append(row.version.ToString()).Append(':').Append(row.toggleName).Append('=').Append(row.enable ? '1' : '0');
        }
    }

    private static void AppendBool(StringBuilder builder, bool value)
        => builder.Append(value ? '1' : '0');

    /// <summary>
    /// 仅对自有 clone 的内容层 Tile 清空全部地形装饰/结构 scatter 布尔（receipts/world-tile-interop.cs
    /// 的 33 个 scatter 字段）；保留 ground(noGround)、snow/天气(noSnow/snow)、材质、Tile/Persistent、
    /// Grass 与路径。返回触及的 Tile 数与实际由 true 清为 false 的字段数。
    /// </summary>
    private static bool TryStripScatterFlags(GameObject cloneRoot, out int tiles, out int clearedFlags)
    {
        tiles = 0;
        clearedFlags = 0;
        var collected = new List<Tile>();
        if (!TryCollectContentTiles(cloneRoot.transform, collected)) return false;
        for (int i = 0; i < collected.Count; i++)
        {
            Tile tile = collected[i];
            clearedFlags += ClearAllScatter(tile);
            tiles++;
        }
        return tiles > 0;
    }

    private static int ClearAllScatter(Tile tile)
    {
        int changed = 0;
        if (tile.scatterTree) { tile.scatterTree = false; changed++; }
        if (tile.scatterShrub) { tile.scatterShrub = false; changed++; }
        if (tile.scatterShrubBirds) { tile.scatterShrubBirds = false; changed++; }
        if (tile.scatterFireflies) { tile.scatterFireflies = false; changed++; }
        if (tile.scatterShrubBig) { tile.scatterShrubBig = false; changed++; }
        if (tile.scatterShrubBigBG) { tile.scatterShrubBigBG = false; changed++; }
        if (tile.scatterGlowingTree) { tile.scatterGlowingTree = false; changed++; }
        if (tile.scatterTreeBG) { tile.scatterTreeBG = false; changed++; }
        if (tile.scatterTreeCurtain) { tile.scatterTreeCurtain = false; changed++; }
        if (tile.scatterTreeCurtainMid) { tile.scatterTreeCurtainMid = false; changed++; }
        if (tile.scatterTreeCurtainFar) { tile.scatterTreeCurtainFar = false; changed++; }
        if (tile.scatterMoss) { tile.scatterMoss = false; changed++; }
        if (tile.scatterWaterReed) { tile.scatterWaterReed = false; changed++; }
        if (tile.scatterWaterStatue) { tile.scatterWaterStatue = false; changed++; }
        if (tile.scatterRoots) { tile.scatterRoots = false; changed++; }
        if (tile.scatterHill) { tile.scatterHill = false; changed++; }
        if (tile.scatterRocks) { tile.scatterRocks = false; changed++; }
        if (tile.scatterWall) { tile.scatterWall = false; changed++; }
        if (tile.scatterFarmhouse) { tile.scatterFarmhouse = false; changed++; }
        if (tile.scatterTower) { tile.scatterTower = false; changed++; }
        if (tile.scatterHermesShade) { tile.scatterHermesShade = false; changed++; }
        if (tile.scatterEggMound) { tile.scatterEggMound = false; changed++; }
        if (tile.scatterBackdropNear) { tile.scatterBackdropNear = false; changed++; }
        if (tile.scatterBackdropFar) { tile.scatterBackdropFar = false; changed++; }
        if (tile.scatterExtremeForegroundItems) { tile.scatterExtremeForegroundItems = false; changed++; }
        if (tile.scatterHumanoidTrees) { tile.scatterHumanoidTrees = false; changed++; }
        if (tile.scatterLightBeamBG) { tile.scatterLightBeamBG = false; changed++; }
        if (tile.scatterForegroundCloudLayer1) { tile.scatterForegroundCloudLayer1 = false; changed++; }
        if (tile.scatterForegroundCloudLayer2) { tile.scatterForegroundCloudLayer2 = false; changed++; }
        if (tile.scatterForegroundCloudLayer3) { tile.scatterForegroundCloudLayer3 = false; changed++; }
        if (tile.scatterForegroundCloudLayer4) { tile.scatterForegroundCloudLayer4 = false; changed++; }
        if (tile.scatterForegroundCloudLayer5) { tile.scatterForegroundCloudLayer5 = false; changed++; }
        if (tile.scatterForegroundCloudLayer6) { tile.scatterForegroundCloudLayer6 = false; changed++; }
        return changed;
    }

    /// <summary>活体复核用：任一 scatter 布尔仍为 true 即结构散布未清。</summary>
    private static bool AnyScatterSet(Tile tile)
        => tile.scatterTree || tile.scatterShrub || tile.scatterShrubBirds || tile.scatterFireflies
        || tile.scatterShrubBig || tile.scatterShrubBigBG || tile.scatterGlowingTree || tile.scatterTreeBG
        || tile.scatterTreeCurtain || tile.scatterTreeCurtainMid || tile.scatterTreeCurtainFar
        || tile.scatterMoss || tile.scatterWaterReed || tile.scatterWaterStatue
        || tile.scatterRoots || tile.scatterHill || tile.scatterRocks
        || tile.scatterWall || tile.scatterFarmhouse || tile.scatterTower
        || tile.scatterHermesShade || tile.scatterEggMound
        || tile.scatterBackdropNear || tile.scatterBackdropFar || tile.scatterExtremeForegroundItems
        || tile.scatterHumanoidTrees || tile.scatterLightBeamBG
        || tile.scatterForegroundCloudLayer1 || tile.scatterForegroundCloudLayer2
        || tile.scatterForegroundCloudLayer3 || tile.scatterForegroundCloudLayer4
        || tile.scatterForegroundCloudLayer5 || tile.scatterForegroundCloudLayer6;

    // ------------------------------------------------------------------ CloneInto 观测

    /// <summary>
    /// 只读观测：激活 scope 内且 __instance 是 exact 自有 clone 指针才记录
    /// 原生克隆跨度（posX+GetLeft() 起、GetWidth() 宽）。零写入、零抑制、零复制。
    /// </summary>
    private static void CloneIntoPostfix(LevelBlock __instance, Level __0, float __1)
    {
        GenerationScope dedicated = CurrentScope();
        if (dedicated != null && dedicated.Dedicated)
        {
            RequireDedicatedOwner(dedicated);
            if (!dedicated.Published || __instance == null
                || PointerOf(__instance) != dedicated.DedicatedOwnedPtr
                || PointerOf(__0) != dedicated.LevelPtr)
                throw DedicatedFault(dedicated, "CloneInto-owner-or-unpublished");
            if (dedicated.Spans.Count != 0)
                throw DedicatedFault(dedicated, "duplicate-CloneInto");
            if (!__instance._dimensionsComputed || __instance._width != 44
                || __instance._left != -22f || __instance._right != 22f
                || !float.IsFinite(__1) || __1 != 0f)
                throw DedicatedFault(dedicated, "CloneInto-cached44-or-position");
            dedicated.Spans.Add(new CourtSpan { Left = __1 + __instance._left, Right = __1 + __instance._right });
            Log("dedicated native CloneInto observed: level=" + Hex(dedicated.LevelPtr)
                + " owned=" + Hex(dedicated.DedicatedOwnedPtr) + " span=[-22,22]");
            return;
        }
        try
        {
            GenerationScope scope = CurrentScope();
            if (scope == null || !scope.Active || !scope.Published) return;
            if (__instance == null || _ownedClonePtr == 0 || PointerOf(__instance) != _ownedClonePtr) return;
            float left = __1 + __instance.GetLeft();
            float width = __instance.GetWidth();
            if (!float.IsFinite(left) || !float.IsFinite(width) || width <= 0f) return;
            scope.Spans.Add(new CourtSpan { Left = left, Right = left + width });
        }
        catch (Exception e)
        {
            Log("ERROR clone-into observe fault: " + e.GetType().Name);
        }
    }

    // ------------------------------------------------------------------ Close 验证 / span 存储

    /// <summary>
    /// 生成收尾验证：记录跨度数量=计划、按左缘连续、合计恰 48、span 落在实际 level._levelEdges 内；
    /// 全部通过才存 exact Level 指针与范围；失败不虚构 span。地面 collider 在此刻尽力读取并记录，
    /// 最终判定统一延迟到 <see cref="TryGetSpan"/>（服务点始终做 ground scene/ClosestPoint 校验）。
    /// </summary>
    private static void CloseScope(Level level, GenerationScope scope)
    {
        if (scope == null || scope.Closed) return;
        scope.Closed = true;
        RemoveScope(scope);
        if (!scope.Active) return;
        if (scope.Dedicated)
        {
            CloseDedicatedScope(level, scope);
            return;
        }
        try
        {
            if (level == null || PointerOf(level) != scope.LevelPtr)
            {
                Log("court close: level pointer mismatch; span withheld");
                return;
            }
            if (!scope.Published)
            {
                Log("court close: no injection this scope"
                    + (scope.Failure.Length > 0 ? " (reason=" + scope.Failure + ")" : "")
                    + (scope.Consumed ? "" : "; GetBlocks not observed"));
                return;
            }

            List<CourtSpan> spans = scope.Spans;
            if (spans.Count != scope.PlannedAdds)
            {
                Log("ERROR court close: native cloned " + spans.Count + " of " + scope.PlannedAdds
                    + " owned blocks; span withheld");
                return;
            }
            spans.Sort((a, b) => a.Left != b.Left ? a.Left.CompareTo(b.Left) : a.Right.CompareTo(b.Right));
            for (int i = 1; i < spans.Count; i++)
            {
                if (Math.Abs(spans[i].Left - spans[i - 1].Right) > CoverageTolerance)
                {
                    Log("ERROR court close: cloned spans not contiguous at " + F(spans[i].Left) + "; span withheld");
                    return;
                }
            }
            float left = spans[0].Left;
            float right = spans[spans.Count - 1].Right;
            float total = 0f;
            for (int i = 0; i < spans.Count; i++) total += spans[i].Right - spans[i].Left;
            if (Math.Abs(total - CourtAddedWidth) > CoverageTolerance || Math.Abs((right - left) - CourtAddedWidth) > CoverageTolerance)
            {
                Log("ERROR court close: added width " + F(total) + " != " + CourtAddedWidth + "; span withheld");
                return;
            }

            IntRange edges = level._levelEdges;
            if (!(left >= edges.min - 0.5f && right <= edges.max + 0.5f))
            {
                Log("ERROR court close: span [" + F(left) + "," + F(right) + "] outside native edges ["
                    + edges.min + "," + edges.max + "]; span withheld");
                return;
            }

            bool groundReady = TryDescribeGround(level, left, right, out string groundDetail);
            if (!groundReady)
            {
                LogOnce("close-ground-deferred", "court close: ground collider not final yet ("
                    + groundDetail + "); ground is verified at every span read instead");
            }

            _spanValid = true;
            _spanLevelPtr = scope.LevelPtr;
            _spanLeft = left;
            _spanRight = right;
            _spanGeneration++;

            Log("court verified: span=[" + F(left) + "," + F(right) + "] width=" + F(right - left)
                + " blocks=" + spans.Count + "x" + scope.TemplateWidth
                + " edges=[" + edges.min + "," + edges.max + "]"
                + " ground=" + (groundReady ? groundDetail : "deferred")
                + " level=" + Hex(scope.LevelPtr) + " clone=" + Hex(_ownedClonePtr));
            TryPersistMetadata(level);
        }
        catch (Exception e)
        {
            _spanValid = false;
            Log("ERROR court close fault: " + e.GetType().Name + " " + e.Message);
        }
    }

    // ------------------------------------------------------------------ span 读取（Root 契约入口）

    /// <summary>
    /// 取宫廷区 span。本进程 fresh span 只认 exact 当前 level（指针一致）+ 当前私有 land12；
    /// 冷进程读严格单 JSON sidecar（闭 schema v1、宽恰 48、身份 = PalaceTripContext.TryBinding、
    /// 存档 nativeEdges 必须等于当前 level._levelEdges），并做一次有界只读活体 Tile 覆盖复核
    /// （覆盖连续且全部结构 scatter 为 false）。两条路径都做 ground scene/ClosestPoint 校验。
    /// 只读：不救援、不写 RuntimeTiles、不改玩家位置（保留冷读存档玩家位置）。
    /// </summary>
    internal static bool TryGetSpan(Level level, out float left, out float right, out string reason)
    {
        left = 0f;
        right = 0f;
        reason = "";
        if (!Enabled) { reason = "court area private mode disabled"; return false; }
        try
        {
            if (level == null) { reason = "level unavailable"; return false; }
            if (!CurrentPrivateLand12(level)) { reason = "not the current private land12 level"; return false; }

            if (_spanValid && PointerOf(level) == _spanLevelPtr)
            {
                if (DedicatedWholeEnabled)
                {
                    if (_dedicatedSpanScope == null)
                    { reason = "dedicated fresh identity missing"; return false; }
                    RequireDedicatedOwner(_dedicatedSpanScope);
                }
                if (!TryDescribeGround(level, _spanLeft, _spanRight, out string freshGround))
                {
                    reason = "fresh span ground: " + freshGround;
                    return false;
                }
                if (!_freshCoverageValidated)
                {
                    if (!TryLiveTileCoverage(_spanLeft, _spanRight, true, out string freshTiles))
                    { reason = "fresh tiles: " + freshTiles; return false; }
                    _freshCoverageValidated = true;
                    Log("fresh generated tiles verified: " + freshTiles);
                }
                left = _spanLeft;
                right = _spanRight;
                reason = "fresh-in-memory";
                TryPersistMetadata(level);   // CourtPlaying 时刻的延迟持久化（事件驱动，每代至多一次）
                return true;
            }

            if (_attemptedGenerationLevelPtr == PointerOf(level))
            { reason = "this Level generation did not publish a verified court span"; return false; }
            // 冷进程（新进程 / Reload 后无 fresh 记录）：严格单 JSON sidecar。
            if (!ExtensionIslandRuntime.TryScope(out CampaignSaveData campaign))
            {
                reason = "private Greek offline owner scope unavailable";
                return false;
            }
            if (!PalaceTripContext.TryBinding(GlobalSaveData.loaded, campaign,
                out string guid, out int slot, out string bind))
            {
                reason = "campaign identity unavailable: " + bind;
                return false;
            }
            CourtAreaMetadataRead meta = ReadSidecar(out string readReason);
            if (meta == null) { reason = "sidecar: " + readReason; return false; }
            if (meta.CampaignGuid != guid || meta.CampaignSlot != slot
                || (DedicatedWholeEnabled && meta.Reign != campaign.reign))
            {
                reason = "sidecar identity mismatch (stored slot=" + meta.CampaignSlot
                    + " current=" + slot + ")";
                return false;
            }
            // Level._levelEdges is generation scratch: observed cold restore leaves it (0,0).
            // The current native island save owns the persisted edges; ground/Tile coverage below proves their live restoration.
            var islands = campaign._islands;
            if (islands == null || islands.Count <= LandIndex || islands[LandIndex] == null)
            { reason = "current saved island geometry unavailable"; return false; }
            IntRange edges = islands[LandIndex]._levelEdges;
            if (meta.NativeEdgesMin != edges.min || meta.NativeEdgesMax != edges.max)
            {
                reason = "sidecar edges [" + meta.NativeEdgesMin + "," + meta.NativeEdgesMax
                    + "] != current [" + edges.min + "," + edges.max + "]";
                return false;
            }
            float coldLeft = (float)meta.Left;
            float coldRight = (float)meta.Right;
            if (!TryDescribeGround(level, coldLeft, coldRight, out string coldGround))
            {
                reason = "cold ground: " + coldGround;
                return false;
            }
            if (!TryLiveTileCoverage(coldLeft, coldRight, false, out string tileDetail))
            {
                reason = "cold tiles: " + tileDetail;
                return false;
            }
            left = coldLeft;
            right = coldRight;
            reason = "sidecar-ok";
            LogOnce("span-served-cold", "court span served from sidecar: [" + F(left) + "," + F(right)
                + "] " + tileDetail + "; player position untouched");
            return true;
        }
        catch (Exception e)
        {
            reason = "fault:" + e.GetType().Name;
            return false;
        }
    }

    private static bool CurrentPrivateLand12(Level level)
    {
        try
        {
            Managers managers = Managers.Inst;
            if (managers == null || managers.level == null || managers.level.Pointer != level.Pointer)
                return false;
            if (NetworkBigBoss.IsOnline) return false;
            if (!ExtensionIslandRuntime.TryScope(out CampaignSaveData campaign)) return false;
            if (campaign.CurrentLand != LandIndex) return false;
            return managers.game != null && managers.game.currentLand == LandIndex;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 当前 level 的地面 scene 校验：GroundCollider 存在/启用/非触发/在激活层级、与 gameLayer 同 scene、
    /// bounds 覆盖 span、ClosestPoint 有限。失败给出可读 detail（fail-closed，不猜）。
    /// </summary>
    private static bool TryDescribeGround(Level level, float left, float right, out string detail)
    {
        detail = "";
        try
        {
            BoxCollider2D collider = level != null ? level.GroundCollider : null;
            if (collider == null) { detail = "collider-missing"; return false; }
            if (!collider.enabled || collider.isTrigger || collider.gameObject == null
                || !collider.gameObject.activeInHierarchy)
            { detail = "collider-not-active"; return false; }
            Managers managers = Managers.Inst;
            Transform layer = managers != null && managers.world != null ? managers.world.gameLayer : null;
            if (layer != null && collider.gameObject.scene.handle != layer.gameObject.scene.handle)
            { detail = "collider-scene-mismatch"; return false; }
            if (DedicatedWholeEnabled)
            {
                Transform gt = collider.transform;
                // Physics2D bounds include contact skin; the authored BoxCollider shape proves44u directly.
                if (gt == null || gt.position.x != 0f || gt.lossyScale.x != 1f || gt.lossyScale.y != 1f
                    || gt.rotation != Quaternion.identity || collider.offset.x != 0f || collider.offset.y != .4375f
                    || collider.size.x != 44f || collider.size.y != .875f)
                { detail = "schema2 authored collider differs from44u recipe"; return false; }
            }
            Bounds bounds = collider.bounds;
            if (!float.IsFinite(bounds.min.x) || !float.IsFinite(bounds.max.x))
            { detail = "bounds-nonfinite"; return false; }
            if (!(bounds.min.x <= left + 0.5f && bounds.max.x >= right - 0.5f))
            {
                detail = "bounds=[" + F(bounds.min.x) + "," + F(bounds.max.x) + "] span-not-covered";
                return false;
            }
            Vector2 closest = collider.ClosestPoint(new Vector2((left + right) * 0.5f, bounds.max.y + 2f));
            if (!float.IsFinite(closest.x) || !float.IsFinite(closest.y))
            { detail = "closestpoint-nonfinite"; return false; }
            detail = "bounds=[" + F(bounds.min.x) + "," + F(bounds.max.x) + "] closest=("
                + F(closest.x) + "," + F(closest.y) + ")";
            return true;
        }
        catch (Exception e)
        {
            detail = "fault:" + e.GetType().Name;
            return false;
        }
    }

    /// <summary>
    /// 一次有界只读活体复核：遍历 world.gameLayer 直接子对象，取严格位于 span 内部的 Tile，
    /// 验证覆盖连续且全部 scatter 为 false。不写任何 RuntimeTiles、不救援。
    /// </summary>
    private static bool TryLiveTileCoverage(float left, float right, bool freshGeneration, out string detail)
    {
        detail = "";
        try
        {
            Managers managers = Managers.Inst;
            Transform layer = managers != null && managers.world != null ? managers.world.gameLayer : null;
            if (layer == null) { detail = "gameLayer-missing"; return false; }
            float offset = layer.position.x;
            var spans = new List<CourtSpan>();
            int actualTiles = 0;
            int childCount = layer.childCount;
            for (int i = 0; i < childCount; i++)
            {
                Transform child = layer.GetChild(i);
                if (child == null) continue;
                GameObject go = child.gameObject;
                if (go == null || !go.activeInHierarchy) continue;
                Tile tile = go.GetComponent<Tile>();
                if (tile == null) continue;
                actualTiles++;
                float tileLeft = tile.GetLeft() + offset;
                float tileRight = tile.GetRight() + offset;
                if (!float.IsFinite(tileLeft) || !float.IsFinite(tileRight) || tileRight <= tileLeft) continue;
                if (!(tileRight > left + 0.05f) || !(tileLeft < right - 0.05f)) continue;   // 只取严格在 span 内的 tile
                if (freshGeneration && AnyScatterSet(tile))
                { detail = "fresh scatter configuration was rewritten at " + F(tileLeft); return false; }
                // Scatter flags are generation configuration, not persisted TileData (observed native schema).
                // Cold restore proves the saved ground geometry here; WorldLayout separately checks actual occupants.
                spans.Add(new CourtSpan { Left = tileLeft, Right = tileRight });
            }
            if (DedicatedWholeEnabled && (actualTiles != 11 || spans.Count != 11))
            { detail = "schema2 expected exactly11 live tiles, all within full island; actual=" + actualTiles + " inside=" + spans.Count; return false; }
            if (spans.Count == 0) { detail = "no live tiles inside span"; return false; }
            spans.Sort((a, b) => a.Left != b.Left ? a.Left.CompareTo(b.Left) : a.Right.CompareTo(b.Right));
            if (DedicatedWholeEnabled)
                for (int i = 0; i < 11; i++)
                    if (spans[i].Left != left + i * 4f || spans[i].Right != left + (i + 1) * 4f)
                    { detail = "schema2 tile segment" + i + " differs from contiguous4u recipe"; return false; }
            if (spans[0].Left > left + 0.5f || spans[spans.Count - 1].Right < right - 0.5f)
            {
                detail = "live tiles [" + F(spans[0].Left) + "," + F(spans[spans.Count - 1].Right)
                    + "] do not cover span";
                return false;
            }
            float covered = spans[0].Right;
            for (int i = 1; i < spans.Count; i++)
            {
                if (spans[i].Left > covered + 0.5f)
                {
                    detail = "coverage gap at " + F(spans[i].Left);
                    return false;
                }
                if (spans[i].Right > covered) covered = spans[i].Right;
            }
            if (covered < right - 0.5f) { detail = "coverage short of right edge"; return false; }
            detail = "tiles=" + spans.Count + " covered=[" + F(spans[0].Left) + "," + F(covered) + "]";
            return true;
        }
        catch (Exception e)
        {
            detail = "fault:" + e.GetType().Name;
            return false;
        }
    }

    // ------------------------------------------------------------------ sidecar 元数据

    /// <summary>
    /// fresh 元数据持久化：仅当本进程 fresh span 已验证、当前确证私有 land12、durable 身份就绪时，
    /// 把生成派生的 span 原子写盘并读回复核；每个已验证 generation 至多成功一次。
    /// 身份未就绪时不写盘（延迟到 CourtPlaying 时刻的事件驱动调用），不做未知身份写盘。
    /// </summary>
    private static void TryPersistMetadata(Level level)
    {
        if (DedicatedWholeEnabled)
        {
            PersistDedicatedMetadata(level);
            return;
        }
        try
        {
            if (!_spanValid) return;
            if (_persistedGeneration == _spanGeneration) return;
            if (!CurrentPrivateLand12(level)) return;
            if (!ExtensionIslandRuntime.TryScope(out CampaignSaveData campaign)) return;
            if (campaign.CurrentLand != LandIndex) return;
            if (!PalaceTripContext.TryBinding(GlobalSaveData.loaded, campaign,
                out string guid, out int slot, out string bind))
            {
                LogOnce("persist-deferred", "court metadata persist deferred (identity not ready): "
                    + bind + "; span still served in memory");
                return;
            }
            int reign;
            try { reign = campaign.reign; }
            catch (Exception) { return; }
            if (_ownedTemplateWidth <= 0 || CourtAddedWidth % _ownedTemplateWidth != 0) return;

            IntRange edges = level._levelEdges;
            var meta = new PalaceCourtAreaMetadata
            {
                V = MetadataVersion,
                Kind = MetadataKind,
                CampaignGuid = guid,
                CampaignSlot = slot,
                Reign = reign,
                NativeEdgesMin = edges.min,
                NativeEdgesMax = edges.max,
                Left = _spanLeft,
                Right = _spanRight,
                BlockWidth = _ownedTemplateWidth,
                AddedBlocks = CourtAddedWidth / _ownedTemplateWidth,
            };
            if (!ValidateMetadata(meta, out string invalid))
            {
                Log("ERROR court metadata invalid before write: " + invalid);
                return;
            }
            if (!TryWriteSidecarAtomic(meta, out string writeReason, out string readBack))
            {
                Log("ERROR court metadata persist failed: " + writeReason);
                return;
            }
            _persistedGeneration = _spanGeneration;
            Log("metadata persisted (atomic temp->move, readback ok): guid=" + guid + " slot=" + slot
                + " reign=" + reign + " edges=[" + edges.min + "," + edges.max + "] span=["
                + F(_spanLeft) + "," + F(_spanRight) + "] readback=" + readBack.Length + " chars");
        }
        catch (Exception e)
        {
            Log("ERROR court metadata persist fault: " + e.GetType().Name + " " + e.Message);
        }
    }

    private static string SidecarPath()
    {
        try
        {
            return Path.Combine(BepInEx.Paths.ConfigPath, "KingdomEnhancedMod", "ModSave", DedicatedWholeEnabled ? "palace-whole-island.v2.json" : SidecarFile);
        }
        catch (Exception)
        {
            return null;
        }
    }

    // Narrow read-only exposure for pre-payment destination admission（PalaceDestinationAdmission）：
    // 严格沿用同一闭 schema 严格解析 + ValidateMetadata；只读，不写、不迁移、不删除 sidecar。
    internal static CourtAreaMetadataRead ReadSidecar(out string reason)
    {
        reason = "";
        string path = SidecarPath();
        if (path == null) { reason = "config-path-unavailable"; return null; }
        if (!File.Exists(path)) { reason = "missing"; return null; }
        string json = File.ReadAllText(path);
        if (string.IsNullOrEmpty(json) || json.Length > MaxJsonChars)
        {
            reason = "size:" + (json != null ? json.Length : -1);
            return null;
        }
        if (!TryParseMetadataStrict(json, out PalaceCourtAreaMetadata meta, out string parseReason))
        {
            reason = parseReason;
            return null;
        }
        return CourtAreaMetadataRead.From(meta);
    }

    private static bool TryWriteSidecarAtomic(PalaceCourtAreaMetadata meta, out string reason, out string readBack)
    {
        reason = "";
        readBack = null;
        string path = SidecarPath();
        if (path == null) { reason = "config-path-unavailable"; return false; }
        string json = JsonSerializer.Serialize(meta, JsonOptions);
        if (json.Length > MaxJsonChars) { reason = "json-too-long:" + json.Length; return false; }
        string temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path, true);   // 同目录原子 rename（.NET 8 overwrite）
        readBack = File.ReadAllText(path);
        if (!string.Equals(readBack, json, StringComparison.Ordinal))
        {
            reason = "readback-mismatch";
            return false;
        }
        if (!TryParseMetadataStrict(readBack, out PalaceCourtAreaMetadata parsed, out string parseReason)
            || !MetadataEquals(meta, parsed))
        {
            reason = "readback-invalid:" + parseReason;
            return false;
        }
        return true;
    }

    /// <summary>闭 schema 严格解析：非对象 / 未知键 / 重复键 / 键数不符 / 反序列化失败 / 校验失败一律拒绝。</summary>
    private static bool TryParseMetadataStrict(string json, out PalaceCourtAreaMetadata meta, out string reason)
    {
        meta = null;
        reason = "";
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { reason = "not-object"; return false; }
            int count = 0;
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in root.EnumerateObject())
            {
                count++;
                if (!names.Add(property.Name)) { reason = "duplicate-key:" + property.Name; return false; }
                bool known = false;
                for (int i = 0; i < MetadataExpectedKeys.Length; i++)
                    if (MetadataExpectedKeys[i] == property.Name) { known = true; break; }
                if (!known) { reason = "unknown-key:" + property.Name; return false; }
            }
            if (count != MetadataExpectedKeys.Length) { reason = "key-count:" + count; return false; }
            PalaceCourtAreaMetadata parsed = JsonSerializer.Deserialize<PalaceCourtAreaMetadata>(json, JsonOptions);
            if (parsed == null) { reason = "deserialize-null"; return false; }
            if (!ValidateMetadata(parsed, out reason)) return false;
            meta = parsed;
            return true;
        }
        catch (Exception e)
        {
            reason = "parse-fault:" + e.GetType().Name;
            return false;
        }
    }

    private static bool ValidateMetadata(PalaceCourtAreaMetadata m, out string reason)
    {
        reason = "";
        if (m == null) { reason = "null"; return false; }
        int version = DedicatedWholeEnabled ? 2 : MetadataVersion;
        string kind = DedicatedWholeEnabled ? "palace-dedicated-island" : MetadataKind;
        int expectedWidth = DedicatedWholeEnabled ? 44 : CourtAddedWidth;
        if (m.V != version) { reason = "version:" + m.V; return false; }
        if (!string.Equals(m.Kind, kind, StringComparison.Ordinal)) { reason = "kind"; return false; }
        if (!ValidGuidD(m.CampaignGuid)) { reason = "campaignGuid"; return false; }
        if (m.CampaignSlot < 0) { reason = "campaignSlot:" + m.CampaignSlot; return false; }
        if (m.Reign < 0) { reason = "reign:" + m.Reign; return false; }
        if (m.NativeEdgesMin > m.NativeEdgesMax) { reason = "edges:" + m.NativeEdgesMin + ">" + m.NativeEdgesMax; return false; }
        if (!double.IsFinite(m.Left) || !double.IsFinite(m.Right)) { reason = "range-nonfinite"; return false; }
        if (!(m.Left < m.Right)) { reason = "range-order"; return false; }
        if (DedicatedWholeEnabled && (m.NativeEdgesMin != -22 || m.NativeEdgesMax != 22
            || m.Left != -22 || m.Right != 22 || m.BlockWidth != 4 || m.AddedBlocks != 11))
        { reason = "schema2 fixed target recipe differs"; return false; }
        if (Math.Abs((m.Right - m.Left) - expectedWidth) > 0.000001)
        { reason = "width:" + (m.Right - m.Left).ToString("F3", CultureInfo.InvariantCulture); return false; }
        if (m.BlockWidth <= 0 || m.BlockWidth > expectedWidth || expectedWidth % m.BlockWidth != 0)
        { reason = "blockWidth:" + m.BlockWidth; return false; }
        if (m.AddedBlocks != expectedWidth / m.BlockWidth)
        { reason = "addedBlocks:" + m.AddedBlocks; return false; }
        return true;
    }

    private static bool MetadataEquals(PalaceCourtAreaMetadata a, PalaceCourtAreaMetadata b)
    {
        return a.V == b.V
            && string.Equals(a.Kind, b.Kind, StringComparison.Ordinal)
            && string.Equals(a.CampaignGuid, b.CampaignGuid, StringComparison.Ordinal)
            && a.CampaignSlot == b.CampaignSlot
            && a.Reign == b.Reign
            && a.NativeEdgesMin == b.NativeEdgesMin
            && a.NativeEdgesMax == b.NativeEdgesMax
            && a.Left == b.Left
            && a.Right == b.Right
            && a.BlockWidth == b.BlockWidth
            && a.AddedBlocks == b.AddedBlocks;
    }

    private static bool ValidGuidD(string text)
        => !string.IsNullOrEmpty(text) && text.Length == 36
            && Guid.TryParseExact(text, "D", out Guid parsed) && parsed != Guid.Empty;

    /// <summary>sidecar 读结果（值拷贝，避免调用方持有反序列化实例的可变引用）。</summary>
    internal sealed class CourtAreaMetadataRead
    {
        internal string CampaignGuid;
        internal int CampaignSlot;
        internal int Reign;
        internal int NativeEdgesMin;
        internal int NativeEdgesMax;
        internal double Left;
        internal double Right;

        internal static CourtAreaMetadataRead From(PalaceCourtAreaMetadata meta)
            => new CourtAreaMetadataRead
            {
                CampaignGuid = meta.CampaignGuid,
                CampaignSlot = meta.CampaignSlot,
                Reign = meta.Reign,
                NativeEdgesMin = meta.NativeEdgesMin,
                NativeEdgesMax = meta.NativeEdgesMax,
                Left = meta.Left,
                Right = meta.Right,
            };
    }

    // ------------------------------------------------------------------ KEM 塔/墙位阻断

    /// <summary>
    /// 在既有 managed 分配入口阻断私有 land12 的 KEM 新增塔位：仅 Enabled + exact 当前
    /// Managers.world + 普通 Greek 离线 owner + campaign/Game 同在 12 时返回 false（跳过本次
    /// Schedule）；其余（含 source8、联机、未启用）原样放行。不清理/删除任何已创建对象；
    /// 冷读已存 land12 同样跳过新增点。
    /// </summary>
    private static bool TowerSchedulePrefix(World __0)
        => AllowManagedSpotSchedule(__0, "tower");

    private static bool WallSchedulePrefix(World __0)
        => AllowManagedSpotSchedule(__0, "wall");

    internal static bool AllowManagedSpotSchedule(World world, string kind)
    {
        if (!Enabled) return true;
        try
        {
            if (world == null) return true;
            Managers managers = Managers.Inst;
            if (managers == null || managers.world == null
                || PointerOf(managers.world) != PointerOf(world)) return true;
            if (NetworkBigBoss.IsOnline) return true;
            if (!ExtensionIslandRuntime.TryScope(out CampaignSaveData campaign)) return true;
            if (campaign.CurrentLand != LandIndex) return true;
            if (managers.game == null || managers.game.currentLand != LandIndex) return true;
            // 确证私有 land12 离线 scope：阻止本次新增 KEM 塔/墙位（已建对象不动）。
            if (kind == "tower") _blockedTowerCount++;
            else _blockedWallCount++;
            LogOnce("blocked-" + kind, "blocked new KEM " + kind
                + " spot growth on private land12 (tower=" + _blockedTowerCount
                + " wall=" + _blockedWallCount + "); existing objects untouched");
            return false;
        }
        catch (Exception e)
        {
            Log("WARN managed spot gate read fault (" + kind + "): " + e.GetType().Name);
            return true;
        }
    }

    // ------------------------------------------------------------------ 通用辅助

    private static GenerationScope CurrentScope()
        => Scopes.Count > 0 ? Scopes[Scopes.Count - 1] : null;

    private static void RemoveScope(GenerationScope scope)
    {
        for (int i = Scopes.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(Scopes[i], scope))
            {
                Scopes.RemoveAt(i);
                return;
            }
        }
    }

    private static bool SameNameMultiset(string[] actual, string[] expected)
    {
        if (actual.Length != expected.Length) return false;
        for (int i = 0; i < expected.Length; i++)
        {
            if (CountName(actual, expected[i]) != CountName(expected, expected[i])) return false;
        }
        return true;
    }

    private static int CountName(string[] names, string name)
    {
        int count = 0;
        for (int i = 0; i < names.Length; i++)
        {
            if (string.Equals(names[i], name, StringComparison.Ordinal)) count++;
        }
        return count;
    }

    private static HashSet<string> BuildContentLayerNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (string name in Enum.GetNames(typeof(ContentLayers))) names.Add(name);
        return names;
    }

    private static ulong PointerOf(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase value)
        => PalacePrivateConfigFactory.PointerOf(value);

    private static string Hex(ulong value) => PalacePrivateConfigFactory.Hex(value);

    private static string SafeName(UnityEngine.Object subject)
    {
        try { return subject != null ? (subject.name ?? "") : ""; }
        catch (Exception) { return ""; }
    }

    private static string F(float value) => value.ToString("F3", CultureInfo.InvariantCulture);

    private static void Log(string message)
    {
        try { PalaceVisibleEntry.Log("[PalaceCourtArea] " + message); }
        catch (Exception) { }
    }

    private static void LogOnce(string key, string message)
    {
        try
        {
            if (!LoggedKeys.Add(key)) return;
            Log(message);
        }
        catch (Exception) { }
    }
    // Schema2 dedicated scope selects the single44u draft; existing cold metadata/geometry remain strict.
    internal static bool DedicatedWholeEnabled => Enabled
        && PalaceRuntimeProfile.Capability("PALACE_DEDICATED_ISLAND_PRIVATE");
    internal static float RenderAxis(float left, float right) => DedicatedWholeEnabled
        ? (left + right) * .5f + (645f - 1448f * .5f) / 32f : (left + right) * .5f;


}
