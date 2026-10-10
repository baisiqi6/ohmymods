using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 附加希腊探索岛（物理 land11）：native 静态度量与私有 LevelConfig 的窄桥 + 洞口入口隔离。
///
/// 职责（operator/integration-contract.md、native-terrain/CAVE-GUARD-CONTRACT.md）：
/// - <see cref="IsAvailableForCurrentCampaign"/> / <see cref="IsCurrentExtension"/>：travel 子任务调用的
///   可用性与"当前就在附加岛"判定。scope 只认普通 Greek 离线且 owner（<c>GlobalSaveData.loaded.
///   GetCurrentCampaign()</c> 与 <c>CampaignSaveData.current</c> 同一实例）确证，无 slot0 猜测、
///   无未知 owner fallback；不同 campaign/世界立即重判不沿用旧结论。
/// - <see cref="EnsureFileCapacity"/> / <see cref="EnsureReady"/>：就绪门。**容量值与文件表确认分离**：
///   MAX>=12 只是容量；只有一次原生 <c>IslandSaveData.UpdateFileProps()</c> 完整返回（postfix）才标记
///   table-ready；异常/刷新开始即清 ready，后续可有界重试真实重建。失败时不可开放新岛旅行。
/// - <see cref="TryHandleConfigRequest"/>：<c>BiomeHolder.GetConfigFromIndex</c> 前缀判定体。
///   必须先过 **exact current-holder 实例门**（错误 holder 原样透传）；仅"确证 scope + 当前
///   campaign.CurrentLand==11"时接管并返回同一私有 config；该场景下创建失败→null 屏蔽（不落回数组
///   越界），scope 外（联机/挑战/其他 world/campaign 非 11）一律原生透传。
/// - <see cref="TryPadLandDataToExtension"/>：首次 land11 生成前，只对确证 owner/reign 的当前容器把
///   landData 补到 ≥12 个 default 槽（不触碰既有 items/previousReigns/visited/markers/付款）。
/// - 洞口入口隔离：只对"land11 生成/加载窗口内创建"的那座右 Cliff 实例登记并拦截四个入口
///   （BombablePortal.Start / ActivatePortal / SidedCaveData.CanBuildBomb / CliffPortalState.ChangeState
///   的非 None 目标）+ 两个直接造弹提交门（PayableBombPurchase/PayableForge.SpawnBomb）；
///   Portal/Damageable/Persistent/PortalData/Crumble/3 天重建链完全不触碰。
/// - <see cref="ShouldBlockDefeatGreed"/>：land11 场景级完成保险门。
///
/// 实机验收边界见 REPORT.md；本类只保证窄作用域、fail-closed、单调扩容与实例绑定。
/// </summary>
internal static class ExtensionIslandRuntime
{
    internal const int LandIndex = ExtensionIslandPlan.LandIndex;
    internal const int MapIndex = ExtensionIslandPlan.MapIndex;

    /// <summary>次级扩展岛（物理 land13 / UI11；issue-200 双岛，见 MountIslandSplitPolicy）。</summary>
    internal const int SecondaryLandIndex = MountIslandSplitPolicy.SecondaryLand;
    internal const int SecondaryMapIndex = MountIslandSplitPolicy.SecondaryUi;

    /// <summary>
    /// 扩展岛文件寻址容量下限（**单 writer**，本类独占）：0..13 共 14 槽。
    /// land11 的 12 槽下限与双岛 policy 的 14 槽下限取大，严格单调不降；
    /// 13 未宣布 Native accepted 前容量仍按 14 准备（纸面容量不等于表 ready）。
    /// </summary>
    internal const int RequiredFileCapacity =
        ExtensionIslandPlan.MinimumFileCapacity > MountIslandSplitPolicy.RequiredFileCapacity
            ? ExtensionIslandPlan.MinimumFileCapacity
            : MountIslandSplitPolicy.RequiredFileCapacity;

    internal const string LogPrefix = "[ExtensionIsland]";
    /// <summary>私有 config 模板（native-terrain §2：普通 God 岛 84672 作为希腊基础字段来源）。</summary>
    internal const string TemplateAssetName = "Greece_Land_God_Artemis";
    private static string PrivateConfigName(int land) => "Greece_Land_Extension_L" + land;
    /// <summary>Greek 普通地形场景名（native-terrain §8：Level.GenerateCurrentConfig 写入的 BlocksSceneName）。</summary>
    private const string GreekBlocksSceneName = "blocks_greece";

    /// <summary>每扩展岛一份私有 config 缓存（0 = land11、1 = land13；身份三元组一致才复用）。</summary>
    private struct ConfigSlot
    {
        internal LevelConfig Config;
        internal ulong BiomePointer;
        internal ulong TemplatePointer;
        internal ulong CampaignPointer;
    }

    private static readonly ConfigSlot[] ConfigSlots = new ConfigSlot[2];

    private static int ConfigSlotIndex(int land) => land == LandIndex ? 0 : 1;

    /// <summary>
    /// fresh 票据门专用（readonly，**绝不创建 config / 不按名回落 / 不把 native 原模板当私有 config**）：
    /// <paramref name="config"/> 是否就是本 land 的私有缓存实例。全部条件：
    /// land ∈ {11,13}；Greek biome 且 holder 与当前实例同指针；raw owner（
    /// <see cref="TryResolveRawOwner"/>，无 GetCurrentCampaign fallback）确证且 <c>owner.CurrentLand == land</c>；
    /// 私有 <see cref="ConfigSlot"/> 的 Config 指针精确等于入参（alias wrapper 同 ptr 可接受），且
    /// 缓存身份三元组 BiomePointer/CampaignPointer 与当前 live 身份精确一致。任一读取失败/未知 → false。
    /// </summary>
    internal static bool IsOwnedConfig(LevelConfig config, int land)
    {
        try
        {
            if (config == null) return false;
            if (!MountIslandSplitPolicy.IsExtensionLand(land)) return false;
            BiomeHolder holder = BiomeHolder.Inst;
            if (holder == null || !IsCurrentHolder(holder)) return false;
            if (holder.BiomeIndex != BiomeHolder.GreeceBiomeIndex) return false;
            if (!TryResolveRawOwner(out _, out _, out CampaignSaveData owner)) return false;
            if (owner == null || owner.CurrentLand != land) return false;
            ConfigSlot cached = ConfigSlots[ConfigSlotIndex(land)];
            if (cached.Config == null) return false;
            if (PointerOf(cached.Config) != PointerOf(config)) return false;
            if (cached.BiomePointer != PointerOf(holder)) return false;
            if (cached.CampaignPointer != PointerOf(owner)) return false;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // 文件表准备状态由纯状态机 MountIslandFilePropsRefresh 独占（prefix/postfix/finalizer 各一次调用，
    // 本类不再保留私有 ready 字段，避免两处状态）。语义：ready = 本轮 refresh 唯一收尾确认
    // && completed == 当前 MAX && completed >= 14。

    // 洞口隔离：land11 生成/加载窗口内创建的实例指针（context 变化即清，防指针复用误拦）。
    private const int MaxTrackedCaves = 64;
    private static readonly HashSet<ulong> RestrictedCaves = new HashSet<ulong>();
    private static readonly HashSet<ulong> RestrictedPortals = new HashSet<ulong>();

    private static readonly HashSet<string> LoggedKeys = new HashSet<string>(StringComparer.Ordinal);

    // ------------------------------------------------------------------ travel 桥

    /// <summary>
    /// 可用性（新导航授权）。旧 no-arg = 主岛 land11 合同，保持所有现行 caller 语义不变。
    /// </summary>
    internal static bool IsAvailableForCurrentCampaign() => IsAvailableForCurrentCampaign(LandIndex);

    /// <summary>
    /// per-land 语义（root 二审修正）：**展示/恢复可发现性**与**实际旅行提交资格**分开。
    /// - current/visited/生成历史正证岛保可发现，不因买完/活动过期/开关 OFF 裁撤；
    /// - visited 未知 / raw slot 读取失败 → 展示也关闭（fail-closed）；
    /// - 旅行门（<see cref="CanTravelToLand"/> / <see cref="EnsureReady(int)"/>）：恢复身份正证但 raw
    ///   存档槽缺失/待核 → false（"存档槽待核，无法出航重建"），绝不 autoCreate、绝不用 GetIsland 补槽；
    /// - 首次路径要求 feature 开启且存在 eligible 新目标；A/B 共用同一门；原生 0..10/宫廷 12 不接。
    /// </summary>
    internal static bool IsAvailableForCurrentCampaign(int land)
    {
        EvaluateLand(land, out MountIslandAvailabilityOutcome outcome, out _, out string diagnostic);
        if (diagnostic != null)
            LogOnce("availability-" + land + "-" + outcome, "land " + land + ": "
                + MountIslandAvailabilityPolicy.Describe(outcome, land) + " [" + diagnostic + "]");
        return MountIslandAvailabilityPolicy.IsAvailable(outcome);
    }

    /// <summary>
    /// **实际旅行提交资格**（Map travel gate / EnsureReady 使用）：recovery 正证但 raw 存档槽缺失时 false。
    /// </summary>
    internal static bool CanTravelToLand(int land)
    {
        EvaluateLand(land, out _, out MountIslandTravelOutcome travel, out string diagnostic);
        if (!MountIslandAvailabilityPolicy.IsTravelAllowed(travel) || diagnostic != null)
            LogOnce("travel-" + land + "-" + travel, "land " + land + ": "
                + MountIslandAvailabilityPolicy.DescribeTravel(travel, land)
                + (diagnostic != null ? " [" + diagnostic + "]" : string.Empty));
        return MountIslandAvailabilityPolicy.IsTravelAllowed(travel);
    }

    /// <summary>只读诊断（展示 + 旅行）：供 map/日志读取判定原因，不改变任何状态。</summary>
    internal static string DescribeAvailability(int land)
    {
        EvaluateLand(land, out MountIslandAvailabilityOutcome outcome, out MountIslandTravelOutcome travel, out string diagnostic);
        string text = MountIslandAvailabilityPolicy.Describe(outcome, land)
            + " available=" + (MountIslandAvailabilityPolicy.IsAvailable(outcome) ? "true" : "false")
            + "; " + MountIslandAvailabilityPolicy.DescribeTravel(travel, land)
            + " canTravel=" + (MountIslandAvailabilityPolicy.IsTravelAllowed(travel) ? "true" : "false");
        return diagnostic != null ? text + " [" + diagnostic + "]" : text;
    }

    /// <summary>同一次真实读取同时产出展示判定与旅行判定（避免两套读取漂移）。</summary>
    private static void EvaluateLand(int land, out MountIslandAvailabilityOutcome availability,
        out MountIslandTravelOutcome travel, out string diagnostic)
    {
        availability = MountIslandAvailabilityOutcome.NotExtensionLand;
        travel = MountIslandTravelOutcome.NotExtensionLand;
        diagnostic = null;
        try
        {
            if (!MountIslandSplitPolicy.IsExtensionLand(land)) return;
            if (!TryScope(out CampaignSaveData campaign))
            {
                availability = MountIslandAvailabilityOutcome.ScopeUnproven;
                travel = MountIslandTravelOutcome.ScopeUnproven;
                return;
            }

            bool current = campaign.CurrentLand == land;
            bool visitedKnown = true;
            bool visited = false;
            try
            {
                Il2CppSystem.Collections.Generic.List<int> islands = campaign.visitedIslands;
                visited = islands != null && islands.Contains(land);
            }
            catch (Exception e)
            {
                visitedKnown = false;
                diagnostic = "visitedIslands read failed (visit state unknown): " + e.Message;
            }

            // 本岛 marker（landData[land].steedSpawns）是生成历史正证之一；读取失败 → unknown（closed）。
            bool markerReadOk = TryReadIslandMarkers(campaign, land, out bool markerKnown, out bool hasMarkers);
            if (!markerReadOk)
                diagnostic = Append(diagnostic, "island marker array read failed (generation state unknown)");

            // raw slot 正证历史（只读；不 GetIsland）；markers 证据必须一并 known，否则整体 unknown。
            bool slotPresent = false;
            bool generatedHistory = false;
            bool generatedKnown = markerReadOk && MountIslandGrantAuthority.TryReadIslandGenerationHistory(
                campaign, land, markerKnown, hasMarkers, out slotPresent, out generatedHistory);
            if (markerReadOk && !generatedKnown)
                diagnostic = Append(diagnostic, "raw island slot read failed (generation state unknown)");

            bool feature = BaseEnabled() && GrantSwitchEnabled();
            bool hasEligible = false;
            if (feature && !current && visitedKnown && !visited && generatedKnown && !generatedHistory)
            {
                if (land == SecondaryLandIndex)
                    hasEligible = HasEligibleSecondaryNewTarget(campaign);   // B：排除全部已授予 B + 活动门
                else
                    hasEligible = true;   // land11 保持现行首次开放语义（root 未要求改；报告已注明）
            }

            availability = MountIslandAvailabilityPolicy.Decide(
                land, scopeProven: true, current: current, visitedKnown: visitedKnown, visited: visited,
                generatedEvidenceKnown: generatedKnown, generatedHistory: generatedHistory,
                featureEnabled: feature, hasEligibleNewTarget: hasEligible);
            travel = MountIslandAvailabilityPolicy.DecideTravel(
                land, scopeProven: true, current: current, visitedKnown: visitedKnown, visited: visited,
                generatedEvidenceKnown: generatedKnown, generatedHistory: generatedHistory,
                slotPresent: slotPresent, featureEnabled: feature, hasEligibleNewTarget: hasEligible);

            if (travel == MountIslandTravelOutcome.RecoveryDataMissing)
                diagnostic = Append(diagnostic, "存档槽待核，无法出航重建 (no auto-create)");
            else if (availability == MountIslandAvailabilityOutcome.VisitedRecovery && !slotPresent)
                diagnostic = Append(diagnostic, "visited land " + land + " but raw island slot missing");
        }
        catch (Exception e)
        {
            diagnostic = "availability read failed: " + e.GetType().Name + " " + e.Message;
            availability = MountIslandAvailabilityOutcome.UnknownEvidence;
            travel = MountIslandTravelOutcome.UnknownEvidence;
        }
    }

    /// <summary>本岛 landData[land].steedSpawns 读取（marker 证据的一部分；异常 → false/unknown）。</summary>
    private static bool TryReadIslandMarkers(CampaignSaveData campaign, int land, out bool known, out bool hasMarkers)
    {
        known = false;
        hasMarkers = false;
        try
        {
            CampaignSaveData.ReignInfo reign = campaign.currentReign;
            Il2CppSystem.Collections.Generic.List<CampaignSaveData.LandMapData> landData = reign != null ? reign.landData : null;
            if (landData == null) return false;
            if (land < 0 || land >= landData.Count) { known = true; return true; }   // 无槽 = 已知阴性
            CampaignSaveData.LandMapData entry = landData[land];
            if (entry == null) { known = true; return true; }
            Il2CppStructArray<SteedType> spawns = entry.steedSpawns;
            hasMarkers = spawns != null && spawns.Length > 0;
            known = true;
            return true;
        }
        catch (Exception e)
        {
            LogOnce("markers-" + land + "-" + e.GetType().Name,
                "island marker read failed (land " + land + "): " + e.Message);
            return false;
        }
    }

    private static string Append(string existing, string addition)
        => existing == null ? addition : existing + "; " + addition;

    /// <summary>
    /// B 首次开放资格：同一次 campaign 权威快照（raw _islands 只读；捕获前后身份 + 单元指纹稳定；
    /// eligibility marker 扫描后再次 <c>MatchesCurrent</c>）下，至少一条 B 定义"未授予（任意 land
    /// marker 或任意 raw slot receipt 都没有）且活动门 eligible"。快照/读取 unknown → false（closed）；
    /// marker 读故障 → closed；没有 eligible 的 B 不开放空新岛。
    /// </summary>
    private static bool HasEligibleSecondaryNewTarget(CampaignSaveData campaign)
    {
        try
        {
            CampaignSaveData.ReignInfo reign = campaign.currentReign;
            if (reign == null) return false;
            Il2CppSystem.Collections.Generic.List<CampaignSaveData.LandMapData> landData = reign.landData;
            if (landData == null) return false;
            if (!MountIslandGrantAuthority.TryCapture(campaign, reign, landData,
                    out MountIslandGrantAuthority.Snapshot snapshot) || snapshot == null)
            {
                LogOnce("b-eligible-snapshot", "secondary eligibility snapshot unavailable; first-time travel stays closed");
                return false;
            }

            var candidates = new System.Collections.Generic.List<MountIslandNewGrantCandidate>(8);
            CrossWorldMountDefinition[] definitions = CrossWorldMountCatalog.Definitions;
            for (int i = 0; i < definitions.Length; i++)
            {
                CrossWorldMountDefinition definition = definitions[i];
                if (MountIslandSplitPolicy.GetTargetLand(definition.Id) != SecondaryLandIndex) continue;
                if (!TryMarkerOnAnyLand(landData, definition, out bool markerFound))
                {
                    LogOnce("b-eligible-marker-" + definition.Id,
                        "secondary eligibility marker scan failed; first-time travel stays closed");
                    return false;   // 读故障 closed，不静默阴性
                }
                bool grantedAnywhere = snapshot.ReceiptInCampaign(definition) || markerFound;
                bool seasonEligible = CrossWorldMountRuntime.IsNewGrantSeasonEligible(definition);
                candidates.Add(new MountIslandNewGrantCandidate(definition.Id, grantedAnywhere, seasonEligible));
            }

            // eligibility marker 扫描完成后重核：身份 + 单元指纹（并发替换/换容器 → closed）。
            if (!snapshot.MatchesCurrent(campaign))
            {
                LogOnce("b-eligible-stale",
                    "campaign snapshot changed during eligibility scan; first-time travel stays closed");
                return false;
            }

            bool eligible = MountIslandNewGrantEligibility.HasEligibleTarget(candidates);
            if (!eligible)
                LogOnce("b-eligible-none", "no eligible secondary new target (all granted or season-ineligible); land "
                    + SecondaryLandIndex + " new travel stays closed");
            return eligible;
        }
        catch (Exception e)
        {
            LogOnce("b-eligible-" + e.GetType().Name,
                "secondary eligibility read failed; first-time travel stays closed: " + e.Message);
            return false;
        }
    }

    /// <summary>
    /// 当前 reign 任意 land 的 steedSpawns 是否已含本定义（旧岛 marker）。
    /// 返回 false = 读取故障（unknown，调用方 closed）；返回 true 时 <paramref name="found"/> 有效。
    /// </summary>
    private static bool TryMarkerOnAnyLand(
        Il2CppSystem.Collections.Generic.List<CampaignSaveData.LandMapData> landData,
        CrossWorldMountDefinition definition, out bool found)
    {
        found = false;
        try
        {
            int count = landData.Count;
            for (int i = 0; i < count; i++)
            {
                CampaignSaveData.LandMapData entry = landData[i];
                if (entry == null) continue;
                if (CrossWorldMountPolicy.Contains(CrossWorldMountRuntime.ToIntArray(entry.steedSpawns), definition.SteedTypeId))
                {
                    found = true;
                    return true;
                }
            }
            return true;
        }
        catch (Exception e)
        {
            LogOnce("markers-any-" + definition.Id + "-" + e.GetType().Name,
                "marker scan read failed: " + e.Message);
            return false;
        }
    }

    /// <summary>
    /// 当前岛就是扩展岛（11/13；与功能开关无关，保证恢复/返航）：普通 Greek 离线 + owner 确证 +
    /// campaign.CurrentLand ∈ {11,13}（读档时 Game.currentLand 尚未同步，必须优先 campaign）。
    /// </summary>
    internal static bool IsCurrentExtension()
    {
        try
        {
            if (!TryScope(out CampaignSaveData campaign)) return false;
            return ExtensionIslandPlan.CurrentGate(true, MountIslandSplitPolicy.IsExtensionLand(campaign.CurrentLand));
        }
        catch (Exception e)
        {
            LogOnce("current-" + e.GetType().Name, "current extension read failed: " + e.Message);
            return false;
        }
    }

    // ------------------------------------------------------------------ 文件表容量 / 准备状态

    /// <summary>
    /// table-ready = 纯状态机确认（本轮提交容量 ≥ 14）且**当前 MAX == completed**：
    /// MAX 值本身、旧一轮确认、别人后来抬高的 postMAX、容量漂移后都不算准备完成。
    /// </summary>
    internal static bool IsFilePropsPrepared()
    {
        try { return MountIslandFilePropsRefresh.IsPrepared(IslandSaveData.MAX_BIOME_ISLANDS); }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// UpdateFileProps 前缀：建立本 refresh 身份并（桥内读取后）冻结**提升后 submitted MAX**、清 ready；
    /// 任何读取/设置异常 → NotePrefixFailed（本 refresh 永不 ready）。
    /// </summary>
    internal static void OnFilePropsRefreshBegin(out MountIslandFilePropsRefresh.RefreshCall call)
    {
        MountIslandFilePropsRefresh.Begin(out call);
        try
        {
            int current = IslandSaveData.MAX_BIOME_ISLANDS;
            if (current < MountIslandFilePropsRefresh.RequiredCapacity)
            {
                IslandSaveData.MAX_BIOME_ISLANDS = MountIslandFilePropsRefresh.RequiredCapacity;
                Log("file-props refresh: island capacity raised " + current + " -> "
                    + MountIslandFilePropsRefresh.RequiredCapacity);
            }
            MountIslandFilePropsRefresh.NoteSubmittedMax(call, IslandSaveData.MAX_BIOME_ISLANDS);
        }
        catch (Exception e)
        {
            MountIslandFilePropsRefresh.NotePrefixFailed(call);
            LogError("file-props refresh prefix read/set failed; this refresh stays not-ready (fail-closed): "
                + e.GetType().Name + " " + e.Message);
        }
    }

    /// <summary>postfix：按 binder 注入的 <paramref name="runOriginal"/> 记录执行证据（false = 被 skip）。</summary>
    internal static void OnFilePropsRefreshOriginalRun(
        MountIslandFilePropsRefresh.RefreshCall call, bool runOriginal)
    {
        MountIslandFilePropsRefresh.NoteOriginalRun(call, runOriginal);
    }

    /// <summary>
    /// finalizer 唯一收尾：读取当前 MAX（读取失败按 failed）后确认；复核 finalizer 的 readonly
    /// __runOriginal。确认失败按原因记日志（嵌套会单独记录），ready 保持 false。
    /// </summary>
    internal static void OnFilePropsRefreshFinished(
        MountIslandFilePropsRefresh.RefreshCall call, bool failed, bool runOriginalAtFinalizer)
    {
        int currentMax = -1;
        try { currentMax = IslandSaveData.MAX_BIOME_ISLANDS; }
        catch (Exception e)
        {
            failed = true;
            LogOnce("refresh-max-read-" + e.GetType().Name,
                "finalizer MAX read failed; refresh stays not-ready: " + e.Message);
        }
        MountIslandRefreshResult result = MountIslandFilePropsRefresh.Confirm(call, failed, currentMax, runOriginalAtFinalizer);
        switch (result)
        {
            case MountIslandRefreshResult.Confirmed:
                Log("file-props refresh confirmed: completed=submitted=" + currentMax + " (ready)");
                break;
            case MountIslandRefreshResult.NestedObserved:
                LogError("nested UpdateFileProps refresh interleaved; outer refresh not confirmed (ready stays false)");
                break;
            case MountIslandRefreshResult.ChainSkipped:
                LogOnce("refresh-chain-skipped", "original UpdateFileProps was skipped; refresh not confirmed");
                break;
            case MountIslandRefreshResult.FailedOrAborted:
                LogOnce("refresh-failed", "file-props refresh aborted/failed; table stays not-ready");
                break;
            case MountIslandRefreshResult.CapacityMismatch:
                LogOnce("refresh-capacity-mismatch-" + currentMax, "file-props refresh not confirmed: submitted/current MAX mismatch (current="
                    + currentMax + ")");
                break;
            default:
                break;   // NotOwner/None：嵌套/陈旧/重复收尾，不写 ready（外层另有日志）
        }
    }

    /// <summary>__state 缺失（prefix 链未到本类/被中断）：本次 attempt 未被本类拥有 → 失效 ready。</summary>
    internal static void OnFilePropsRefreshUnowned()
    {
        MountIslandFilePropsRefresh.NoteUnownedAttempt();
        LogOnce("refresh-unowned", "UpdateFileProps attempt not owned by this patch (prefix chain interrupted); ready invalidated");
    }

    /// <summary>
    /// 文件寻址就绪门：已确认（completed ≥ 14 且当前 MAX == completed）→ true 零副作用；
    /// 未确认且无进行中刷新 → 调用一次真实 UpdateFileProps（身份/冻结/唯一确认由三个钩子维护）。
    /// 进行中刷新（含原生内部重入）返回 false；异常保持未准备并显式日志。
    ///   ※ 不再在此重复抬 MAX：单调提升在 prefix 冻结提交值之前执行，避免两处写同一状态。
    /// </summary>
    internal static bool EnsureFileCapacity()
    {
        try
        {
            if (IsFilePropsPrepared()) return true;
            if (MountIslandFilePropsRefresh.InProgress) return false;
            try
            {
                IslandSaveData.UpdateFileProps();     // prefix 冻结 submitted；finalizer 唯一确认
            }
            catch (Exception e)
            {
                LogError("island file capacity bootstrap call failed (ready stays false): "
                    + e.GetType().Name + " " + e.Message);
                return false;
            }
            return IsFilePropsPrepared();
        }
        catch (Exception e)
        {
            LogError("island file capacity bootstrap failed: " + e.GetType().Name + " " + e.Message);
            return false;
        }
    }

    /// <summary>
    /// travel 调用的就绪门（旧 no-arg = 主岛 land11，保持现行 caller 语义）。
    /// </summary>
    internal static bool EnsureReady() => EnsureReady(LandIndex);

    /// <summary>
    /// per-land 就绪门：**实际旅行资格**（CanTravelToLand 同一判定，含"恢复正证但存档槽缺失→拒绝"）
    /// + 文件表确认（≥14 单 writer）+ 进度守卫安装 + **该 land 的**私有 config 可创建且读回正确。
    /// 不要求当前岛就是该 land（地图阶段在旧岛打开）。任一失败即不得开放该岛旅行；
    /// 非扩展岛/未知 land 一律 false（不做原生路由）。
    /// </summary>
    internal static bool EnsureReady(int land)
    {
        if (!MountIslandSplitPolicy.IsExtensionLand(land)) return false;
        EvaluateLand(land, out _, out MountIslandTravelOutcome travel, out string diagnostic);
        if (!MountIslandAvailabilityPolicy.IsTravelAllowed(travel))
        {
            LogOnce("ensure-travel-" + land + "-" + travel, "land " + land + ": "
                + MountIslandAvailabilityPolicy.DescribeTravel(travel, land)
                + (diagnostic != null ? " [" + diagnostic + "]" : string.Empty));
            return false;
        }
        if (!EnsureFileCapacity()) return false;
        if (!ProgressionGuardReady()) return false;
        try
        {
            if (!TryScope(out CampaignSaveData campaign)) return false;
            return TryEnsureConfig(campaign, BiomeHolder.Inst, land, out _);
        }
        catch (Exception e)
        {
            LogError("ensure ready failed (land " + land + "): " + e.GetType().Name + " " + e.Message);
            return false;
        }
    }

    // ------------------------------------------------------------------ GetConfigFromIndex 前缀判定体

    /// <summary>
    /// 返回 true 表示本 prefix 已接管（调用方必须跳过原生）。接管条件全部满足：
    /// 1) configIndex 是扩展岛（11/13，见 MountIslandSplitPolicy）；2) 调用者就是当前
    /// <c>BiomeHolder.Inst</c> 同一实例（错误 holder 透传）；3) Greek biome；4) 确证 scope
    /// （普通 Greek 离线 + owner 无误）且 <c>campaign.CurrentLand == configIndex</c>（只服务
    /// 当前所在扩展岛自己的 config，11/13 不互相混用）。
    /// 该场景下创建失败→result=null 屏蔽（fail-closed，不落回原生数组）；其余情况一律 false 透传。
    /// </summary>
    internal static bool TryHandleConfigRequest(BiomeHolder holder, int configIndex, out LevelConfig result)
    {
        result = null;
        if (!MountIslandSplitPolicy.IsExtensionLand(configIndex)) return false;
        if (!IsCurrentHolder(holder)) return false;      // exact holder 实例门：错误 holder 不改原
        if (holder.BiomeIndex != BiomeHolder.GreeceBiomeIndex) return false;   // 其他 world 透传
        if (!TryScope(out CampaignSaveData campaign)) return false;            // 联机/挑战/无 owner：透传
        if (campaign.CurrentLand != configIndex) return false;                 // 非当前 campaign 该岛：透传
        if (!ProgressionGuardReady())
        {
            LogOnce("config-guard-blocked-" + configIndex, "own current campaign land " + configIndex
                + " but native progression guard not installed; config serving blocked (fail-closed)");
            return true;   // 不返回可生成的 config，也不落回原生数组
        }
        try
        {
            if (TryEnsureConfig(campaign, holder, configIndex, out LevelConfig config))
            {
                result = config;
                LogOnce("config-served-" + configIndex, "private extension config served for land " + configIndex);
                return true;
            }
            LogOnce("config-blocked-" + configIndex, "own current campaign land " + configIndex
                + " config failed to build; navigation blocked (no native array fallthrough)");
            return true;
        }
        catch (Exception e)
        {
            LogOnce("config-error-" + configIndex + "-" + e.GetType().Name,
                "extension config request failed closed: " + e.Message);
            return true;
        }
    }

    /// <summary>调用者必须是全局当前 holder 的同一实例（指针身份）。</summary>
    private static bool IsCurrentHolder(BiomeHolder holder)
    {
        try
        {
            if (holder == null) return false;
            BiomeHolder current = BiomeHolder.Inst;
            if (current == null) return false;
            return PointerOf(holder) == PointerOf(current);
        }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// 原生精确进度防护（progression-worker 单文件 raw ulong native detour）的安装/就绪门：
    /// <c>EnsureInstalled()</c> 幂等、失败锁存；失败/未就绪时不得开放新岛 UI
    /// （<see cref="EnsureReady"/> false）或返回可生成的 land11 config（fail-closed null）。
    /// 原 0..10 / 未知 owner / 其他 world 路径不经过本门（保持原生透传）。
    /// </summary>
    private static bool ProgressionGuardReady()
    {
        try { return ExtensionIslandProgressionGuard.EnsureInstalled(); }
        catch (Exception e)
        {
            LogError("progression guard install/readiness failed: " + e.GetType().Name + " " + e.Message);
            return false;
        }
    }

    // ------------------------------------------------------------------ landData 补槽（首次 land11）

    /// <summary>
    /// 首次扩展岛生成前置容器门（FIRST-GENERATION-CONTRACT）：独立于 SameOwner，先确证“同一当前
    /// campaign + CurrentLand==land + 场景 land==land + 非保存中 + reign 整数稳定”，再保证 currentReign
    /// 的 landData 至少有 <see cref="RequiredFileCapacity"/>（14）槽：
    /// - 已有短容器：**原地** append default LandMapData（保留既有项与容器 Pointer）；
    /// - 容器为 null：本地建 14 default 槽的新 List，复核身份后用**最新** reign wrapper 赋引用并
    ///   `campaign.currentReign = reign` 整体写回（值类型包装语义，不能只改临时 wrapper）；
    /// - 已 ≥14：零写入。
    /// 只补 default 槽：不更新 visited/价格/支付/资源，不改 previousReigns/MaxIslands/secured。
    /// </summary>
    internal static bool TryEnsureExtensionLandDataSlots(CampaignSaveData campaign, int land)
    {
        try
        {
            if (campaign == null || !MountIslandSplitPolicy.IsExtensionLand(land)) return true;   // 只服务扩展岛
            if (!TryScope(out CampaignSaveData scoped) || PointerOf(scoped) != PointerOf(campaign))
            {
                LogOnce("container-scope", "landData container gate: campaign/owner not proven");
                return false;
            }
            if (campaign.CurrentLand != land) return false;
            Managers managers = Managers.Inst;
            if (managers == null || managers.game == null || managers.game.currentLand != land) return false;
            if (IslandSaveData.isSavingGame) return false;
            int reignIndex = SafeReign(campaign);
            if (reignIndex < 0) return false;

            CampaignSaveData.ReignInfo reign = campaign.currentReign;
            if (reign == null)
            {
                LogError("landData container gate: currentReign wrapper unavailable; refusing to overwrite");
                return false;
            }
            Il2CppSystem.Collections.Generic.List<CampaignSaveData.LandMapData> landData = reign.landData;
            if (landData != null && landData.Count >= RequiredFileCapacity) return true;

            if (landData != null)
            {
                while (landData.Count < RequiredFileCapacity)
                    landData.Add(new CampaignSaveData.LandMapData());
                Log("landData container padded in place to " + landData.Count + " slots for land " + land);
            }
            else
            {
                var fresh = new Il2CppSystem.Collections.Generic.List<CampaignSaveData.LandMapData>(
                    RequiredFileCapacity);
                for (int i = 0; i < RequiredFileCapacity; i++)
                    fresh.Add(new CampaignSaveData.LandMapData());
                if (PointerOf(scoped) != PointerOf(campaign) || campaign.CurrentLand != land
                    || SafeReign(campaign) != reignIndex)
                {
                    LogError("landData container gate: ownership changed while building; not published");
                    return false;
                }
                reign.landData = fresh;
                campaign.currentReign = reign;   // 值类型包装：必须整体写回当前 campaign
                Log("landData container created with " + RequiredFileCapacity
                    + " default slots for land " + land);
            }

            // 终核：从 campaign 重新读回，确认同一容器且槽位数满足。
            if (PointerOf(scoped) != PointerOf(campaign) || campaign.CurrentLand != land
                || SafeReign(campaign) != reignIndex)
                return false;
            CampaignSaveData.ReignInfo verified = campaign.currentReign;
            if (verified == null || verified.landData == null
                || verified.landData.Count < RequiredFileCapacity)
            {
                LogError("landData container gate: readback failed (slots="
                    + (verified != null && verified.landData != null ? verified.landData.Count : -1) + ")");
                return false;
            }
            return true;
        }
        catch (Exception e)
        {
            LogError("landData container gate failed: " + e.GetType().Name + " " + e.Message);
            return false;
        }
    }

    private static int SafeReign(CampaignSaveData campaign)
    {
        try { return campaign != null ? campaign.reign : -1; }
        catch (Exception) { return -1; }
    }

    // ------------------------------------------------------------------ 洞口入口隔离

    /// <summary>
    /// 受限上下文：确证 scope + campaign 与当前场景都在**同一扩展岛**（land 11/13；生成/加载/游玩窗口）。
    /// 宫廷 12 与原生 0..10 不进入本窗口（不吞 12、不改原岛）。
    /// </summary>
    internal static bool IsRestrictedCaveContext()
    {
        try
        {
            if (!TryScope(out CampaignSaveData campaign)) return false;
            int land = campaign.CurrentLand;
            if (!MountIslandSplitPolicy.IsExtensionLand(land)) return false;
            Managers managers = Managers.Inst;
            return managers != null && managers.game != null && managers.game.currentLand == land;
        }
        catch (Exception) { return false; }
    }

    /// <summary>context 不合格时清登记（指针复用防护）；合格时保留本次窗口的实例。</summary>
    private static bool RestrictedContextOrClear()
    {
        if (IsRestrictedCaveContext()) return true;
        ClearRestrictedCaves();
        return false;
    }

    private static void ClearRestrictedCaves()
    {
        if (RestrictedCaves.Count == 0 && RestrictedPortals.Count == 0) return;
        RestrictedCaves.Clear();
        RestrictedPortals.Clear();
        Log("restricted cave registry cleared (context change)");
    }

    /// <summary>Game.SetCurrentLand 后置：任何换岛都清登记（新窗口在实例创建时重新登记）。</summary>
    internal static void OnCurrentLandChanged()
    {
        try { ClearRestrictedCaves(); }
        catch (Exception) { }
    }

    /// <summary>CliffPortalState.OnEnable 后置：land11 窗口内创建的实例登记为该岛的受限洞口。</summary>
    internal static void NoteCaveStateEnabled(CliffPortalState state)
    {
        if (state == null) return;
        if (!IsRestrictedCaveContext()) return;
        try
        {
            Register(RestrictedCaves, PointerOf(state), "cliff state");
            // 异常恢复状态：只报告，不重写用户数据（后续 ChangeState 非 None 会被拒）。
            CliffPortalState.State current = state.CurrentState;
            if (current != CliffPortalState.State.None)
                LogError("restricted cave enabled in non-None state " + current
                    + "; refusing to advance (no data rewrite)");
        }
        catch (Exception e)
        {
            LogOnce("cave-state-reg-" + e.GetType().Name, "cave state registration failed: " + e.Message);
        }
    }

    /// <summary>BombablePortal.Awake 后置：登记该入口实例（Start/ActivatePortal 目标）及其 portalState。</summary>
    internal static void NoteBombablePortalAwake(BombablePortal portal)
    {
        if (portal == null) return;
        if (!IsRestrictedCaveContext()) return;
        try
        {
            Register(RestrictedPortals, PointerOf(portal), "bombable portal");
            CliffPortalState state = portal.portalState;
            if (state != null) Register(RestrictedCaves, PointerOf(state), "cliff state");
        }
        catch (Exception e)
        {
            LogOnce("cave-portal-reg-" + e.GetType().Name, "cave portal registration failed: " + e.Message);
        }
    }

    private static void Register(HashSet<ulong> set, ulong pointer, string kind)
    {
        if (pointer == 0UL) return;
        if (set.Count >= MaxTrackedCaves && !set.Contains(pointer))
        {
            LogError("cave guard registry overflow (" + kind + "); clearing");
            SetClear(set);
        }
        if (set.Add(pointer)) Log("restricted cave " + kind + " registered (land " + LandIndex + ")");
    }

    private static void SetClear(HashSet<ulong> set)
    {
        if (ReferenceEquals(set, RestrictedCaves)) { RestrictedCaves.Clear(); RestrictedPortals.Clear(); }
        else set.Clear();
    }

    /// <summary>BombablePortal.Start：land11 受限实例跳过（无洞内 BossHill 时不初始化），其他原样。</summary>
    internal static bool ShouldSkipBombablePortalStart(BombablePortal portal)
    {
        if (portal == null) return false;
        if (!RestrictedContextOrClear()) return false;
        return IsRestrictedPortal(portal);
    }

    /// <summary>BombablePortal.ActivatePortal：同上，阻止 Haglet 入洞启动。</summary>
    internal static bool ShouldSkipBombablePortalActivate(BombablePortal portal) => ShouldSkipBombablePortalStart(portal);

    /// <summary>SidedCaveData.get_CanBuildBomb：受限洞口返回 false（不给出付费资格）；其余原样。</summary>
    internal static bool ShouldBlockCanBuildBomb(SidedCaveData data)
    {
        if (data == null) return false;
        if (!RestrictedContextOrClear()) return false;
        try
        {
            CliffPortalState state = data.state;
            if (state != null && RestrictedCaves.Contains(PointerOf(state))) return true;
            object cavePortal = data.cavePortal;
            if (cavePortal is BombablePortal portal && IsRestrictedPortal(portal)) return true;
        }
        catch (Exception e)
        {
            LogOnce("cave-canbuild-" + e.GetType().Name, "cave CanBuildBomb scope read failed: " + e.Message);
        }
        return false;
    }

    /// <summary>
    /// CliffPortalState.ChangeState：受限实例只允许 None（正常恢复），任何非 None 目标在副作用前拒绝。
    /// 未登记实例（含仍在卸载窗口的原岛实例）一律透传。
    /// </summary>
    internal static bool ShouldBlockCaveStateChange(CliffPortalState state, CliffPortalState.State newState)
    {
        if (state == null) return false;
        if (newState == CliffPortalState.State.None) return false;   // None 恢复链原样
        if (!RestrictedContextOrClear()) return false;
        try
        {
            if (!RestrictedCaves.Contains(PointerOf(state))) return false;
            LogOnce("cave-state-blocked", "restricted cave ChangeState(" + newState + ") blocked (land " + LandIndex + ")");
            return true;
        }
        catch (Exception) { return false; }
    }

    /// <summary>两个直接造弹提交门：land11 当前窗口直接调用 SpawnBomb 也不产生炸弹；其他原样。</summary>
    internal static bool ShouldBlockBombSpawn()
    {
        if (!RestrictedContextOrClear()) return false;
        LogOnce("cave-bomb-blocked", "direct SpawnBomb blocked on extension land " + LandIndex);
        return true;
    }

    private static bool IsRestrictedPortal(BombablePortal portal)
    {
        try
        {
            if (RestrictedPortals.Contains(PointerOf(portal))) return true;
            CliffPortalState state = portal.portalState;
            return state != null && RestrictedCaves.Contains(PointerOf(state));
        }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// Level.GetLevelBlocks 后置（native-terrain §8）：land11 生成窗口内核对 Greek 候选确有
    /// Portal(6)/BeachLeft(18)/CliffRight(21) 三块；缺失即中止本次生成，不产出残缺地形。
    /// </summary>
    internal static void VerifyExtensionCandidates(Level level, Il2CppSystem.Collections.Generic.List<LevelBlock> blocks)
    {
        try
        {
            if (!IsRestrictedCaveContext()) return;
            if (level == null || !string.Equals(level.BlocksSceneName, GreekBlocksSceneName, StringComparison.Ordinal)) return;
            int count = blocks != null ? blocks.Count : 0;
            bool portal = false, beachLeft = false, cliffRight = false;
            for (int i = 0; i < count; i++)
            {
                LevelBlock block = blocks[i];
                if (block == null) continue;
                int groupOne = (int)block.groupOne;
                if (groupOne == ExtensionIslandPlan.GroupPortal) portal = true;
                else if (groupOne == ExtensionIslandPlan.GroupBeachLeft) beachLeft = true;
                else if (groupOne == ExtensionIslandPlan.GroupCliffRight) cliffRight = true;
            }
            if (portal && beachLeft && cliffRight)
            {
                LogOnce("candidates-" + count, "extension block candidates verified: portal+beachLeft+cliffRight among "
                    + count + " blocks");
                return;
            }
            LogError("extension block candidates incomplete (portal=" + portal + " beachLeft=" + beachLeft
                + " cliffRight=" + cliffRight + ", blocks=" + count + "); aborting this generation");
            throw new InvalidOperationException("KEM extension island: required Greek block candidates missing");
        }
        catch (InvalidOperationException) { throw; }
        catch (Exception e)
        {
            LogOnce("candidates-" + e.GetType().Name, "candidate verification failed: " + e.Message);
        }
    }

    // ------------------------------------------------------------------ DefeatGreed 保险门

    /// <summary>
    /// 扩展岛的完成保险门：只对确证 Greek 普通离线 + **当前场景**是扩展岛（11/13，<c>Game.currentLand</c>，
    /// 与 CliffPortalState/CrownStatue 调用点同源）返回 true；0..10/宫廷12/其他世界/挑战全返回 false（原样）。
    /// 不改 Boat/CanPay/SailAway，不触碰存档。
    /// </summary>
    internal static bool ShouldBlockDefeatGreed(Game game)
    {
        try
        {
            if (!TryScope(out _)) return false;
            int current = game != null ? game.currentLand : -1;
            bool extension = MountIslandSplitPolicy.IsExtensionLand(current);
            if (extension) LogOnce("defeat-blocked-" + current, "Game.DefeatGreed blocked on extension land " + current);
            return extension;
        }
        catch (Exception e)
        {
            LogOnce("defeat-" + e.GetType().Name, "defeat greed scope read failed: " + e.Message);
            return false;
        }
    }

    // ------------------------------------------------------------------ scope

    /// <summary>
    /// raw owner 解析（root R2 共用 helper；不依赖 GetCurrentCampaign 的 slot0 fallback）：
    /// Global.loaded 非空、<c>currentCampaign</c> 是 0..campaigns.Count-1 的 exact slot、
    /// <c>campaigns[selected]</c> 非空且 == <c>CampaignSaveData.current</c>。任何未知/异常 false。
    /// </summary>
    internal static bool TryResolveRawOwner(out GlobalSaveData global, out int selectedIndex, out CampaignSaveData owner)
    {
        global = null;
        selectedIndex = -1;
        owner = null;
        try
        {
            GlobalSaveData loaded = GlobalSaveData.loaded;
            if (loaded == null) return false;
            int selected = loaded.currentCampaign;
            if (selected < 0) return false;                       // 菜单/无选中：不接受 fallback
            Il2CppSystem.Collections.Generic.List<CampaignSaveData> campaigns = loaded.campaigns;
            if (campaigns == null || selected >= campaigns.Count) return false;
            CampaignSaveData byIndex = campaigns[selected];
            if (byIndex == null) return false;
            CampaignSaveData current = CampaignSaveData.current;
            if (current == null || PointerOf(byIndex) != PointerOf(current)) return false;
            global = loaded;
            selectedIndex = selected;
            owner = byIndex;
            return true;
        }
        catch (Exception e)
        {
            LogOnce("raw-owner-" + e.GetType().Name, "raw owner resolve failed: " + e.Message);
            return false;
        }
    }

    /// <summary>
    /// 普通 Greek 离线 + owner 确证（root R2）：Global 非空 + <c>InChallenge</c> 为 false（挑战资格只信
    /// InChallenge，不猜 regular id 0 是哪个字段）+ 非联机 + Greek biome + <see cref="TryResolveRawOwner"/>
    /// （exact raw selected slot；不用 GetCurrentCampaign fallback）。任何未知/异常一律 false（fail-closed）。
    /// </summary>
    internal static bool TryScope(out CampaignSaveData campaign)
    {
        campaign = null;
        try
        {
            if (!TryResolveRawOwner(out GlobalSaveData global, out _, out CampaignSaveData owner)) return false;
            if (global.InChallenge) return false;
            if (NetworkBigBoss.IsOnline) return false;
            BiomeHolder biome = BiomeHolder.Inst;
            if (biome == null || biome.BiomeIndex != BiomeHolder.GreeceBiomeIndex) return false;
            campaign = owner;
            return true;
        }
        catch (Exception e)
        {
            LogOnce("scope-" + e.GetType().Name, "campaign scope read failed: " + e.Message);
            return false;
        }
    }

    // ------------------------------------------------------------------ 私有 config

    private static bool TryEnsureConfig(CampaignSaveData campaign, BiomeHolder holder, int land, out LevelConfig config)
    {
        config = null;
        try
        {
            if (!MountIslandSplitPolicy.IsExtensionLand(land)) return false;
            BiomeData biomeData = holder != null ? holder.curBiomeData : null;
            if (biomeData == null) return false;
            Il2CppReferenceArray<LevelConfig> configs = biomeData.levelConfigs;
            if (configs == null || configs.Length <= 1) return false;
            if (!TryFindTemplateIndex(configs, out int templateIndex))
            {
                LogOnce("template-missing-" + land, "normal Greek template config not found in levelConfigs (land "
                    + land + ")");
                return false;
            }
            LevelConfig template = configs[templateIndex];
            if (template == null) return false;

            int slotIndex = ConfigSlotIndex(land);
            ulong biomePointer = PointerOf(biomeData);
            ulong templatePointer = PointerOf(template);
            ulong campaignPointer = PointerOf(campaign);
            ConfigSlot slot = ConfigSlots[slotIndex];
            if (slot.Config != null
                && slot.BiomePointer == biomePointer
                && slot.TemplatePointer == templatePointer
                && slot.CampaignPointer == campaignPointer)
            {
                config = slot.Config;
                return true;
            }

            LevelConfig built = BuildPrivateConfig(template, land);
            if (built == null)
            {
                ClearConfigCache(land);
                return false;
            }
            slot.Config = built;
            slot.BiomePointer = biomePointer;
            slot.TemplatePointer = templatePointer;
            slot.CampaignPointer = campaignPointer;
            ConfigSlots[slotIndex] = slot;
            config = built;
            return true;
        }
        catch (Exception e)
        {
            LogError("private config ensure failed (land " + land + "): " + e.GetType().Name + " " + e.Message);
            ClearConfigCache(land);
            return false;
        }
    }

    /// <summary>模板定位：优先资源全名（84672 Greece_Land_God_Artemis），退化按 GodIslandArtemis 任务类型。</summary>
    private static bool TryFindTemplateIndex(Il2CppReferenceArray<LevelConfig> configs, out int index)
    {
        index = -1;
        for (int i = 0; i < configs.Length; i++)
        {
            LevelConfig candidate = configs[i];
            if (candidate == null) continue;
            try
            {
                if (string.Equals(candidate.name, TemplateAssetName, StringComparison.Ordinal))
                {
                    index = i;
                    return true;
                }
            }
            catch (Exception) { }
        }
        for (int i = 0; i < configs.Length; i++)
        {
            LevelConfig candidate = configs[i];
            if (candidate == null) continue;
            try
            {
                if (candidate.questType == QuestType.GodIslandArtemis)
                {
                    index = i;
                    return true;
                }
            }
            catch (Exception) { }
        }
        return false;
    }

    /// <summary>
    /// 创建私有 config（每扩展岛一份）：<c>Object.Instantiate</c> 复制模板字段后逐项落实合同并读回验证。
    /// 不复用/不修改共享模板；返回 null 即失败（调用方 fail-closed）。同一 Artemis 模板 deep clone、
    /// minWidth=500、groupCounts 合同与 8 道门配额逐岛一致（不改模板、不为 B 走半宽特例）。
    /// </summary>
    internal static LevelConfig BuildPrivateConfig(LevelConfig template, int land)
    {
        if (!MountIslandSplitPolicy.IsExtensionLand(land)) return null;
        LevelConfig clone = UnityEngine.Object.Instantiate(template);
        if (clone == null)
        {
            LogError("private config instantiate returned null");
            return null;
        }

        clone.name = PrivateConfigName(land);
        clone.minLevelWidth = 500;
        clone.questType = QuestType.None;
        clone.islandMonumentID = -1;
        clone.sharedBlocksConfigs = new Il2CppReferenceArray<SharedBlocksConfig>(0);
        clone.caveless = true;
        clone.twoCliffs = false;
        clone.randomizeCliffSide = false;
        clone.groupCounts = BuildGroupCountList(template.groupCounts);
        clone._counts = null;   // 清掉可能随 Instantiate 复制来的缓存：GetCount 首次按新 groupCounts 重建

        if (!ValidatePrivateConfig(clone)) return null;
        Log("private config ready: name=" + clone.name + " minWidth=" + clone.minLevelWidth
            + " questType=" + clone.questType + " groups=" + (clone.groupCounts != null ? clone.groupCounts.Count : 0));
        return clone;
    }

    private static Il2CppSystem.Collections.Generic.List<LevelGroupCount> BuildGroupCountList(
        Il2CppSystem.Collections.Generic.List<LevelGroupCount> template)
    {
        var planned = new List<ExtensionIslandPlan.GroupCount>(template != null ? template.Count : 0);
        if (template != null)
        {
            for (int i = 0; i < template.Count; i++)
            {
                LevelGroupCount entry = template[i];
                if (entry == null) continue;
                planned.Add(new ExtensionIslandPlan.GroupCount((int)entry.group, entry.count.min, entry.count.max));
            }
        }

        List<ExtensionIslandPlan.GroupCount> final = ExtensionIslandPlan.PlanGroupCounts(planned);
        var result = new Il2CppSystem.Collections.Generic.List<LevelGroupCount>(final.Count);
        for (int i = 0; i < final.Count; i++)
        {
            var entry = new LevelGroupCount();
            entry.group = (LevelBlockGroup)final[i].Group;
            entry.count = new IntRange(final[i].Min, final[i].Max);
            result.Add(entry);
        }
        return result;
    }

    /// <summary>合同读回验证：身份字段 + 8 道门配额 + 洞内终段/白名单修正都按计划生效。</summary>
    private static bool ValidatePrivateConfig(LevelConfig clone)
    {
        if (clone.questType != QuestType.None) { LogError("private config questType != None"); return false; }
        if (clone.islandMonumentID != -1) { LogError("private config islandMonumentID != -1"); return false; }
        if (clone.minLevelWidth != 500) { LogError("private config minLevelWidth != 500"); return false; }
        if (clone.sharedBlocksConfigs != null && clone.sharedBlocksConfigs.Length != 0)
        {
            LogError("private config sharedBlocksConfigs not empty");
            return false;
        }
        if (clone.groupCounts == null || clone.groupCounts.Count == 0)
        {
            LogError("private config groupCounts empty");
            return false;
        }
        if (!VerifyCount(clone, LevelBlockGroup.Portal, 6, 6)) return false;
        if (!VerifyCount(clone, LevelBlockGroup.BeachLeft, 1, 1)) return false;
        if (!VerifyCount(clone, LevelBlockGroup.BeachRight, 0, 0)) return false;
        if (!VerifyCount(clone, LevelBlockGroup.CliffLeft, 0, 0)) return false;
        if (!VerifyCount(clone, LevelBlockGroup.CliffRight, 1, 1)) return false;
        if (!VerifyCount(clone, LevelBlockGroup.EndLeft, 1, 1)) return false;
        if (!VerifyCount(clone, LevelBlockGroup.EndRight, 1, 1)) return false;
        if (!VerifyCount(clone, LevelBlockGroup.Cliff, 1, 1)) return false;
        if (!VerifyCount(clone, LevelBlockGroup.Beach, 1, 1)) return false;
        if (!VerifyCount(clone, LevelBlockGroup.CaveSection, 0, 0)) return false;
        return true;
    }

    private static bool VerifyCount(LevelConfig config, LevelBlockGroup group, int min, int max)
    {
        try
        {
            IntRange count = config.GetCount(group);
            if (count.min == min && count.max == max) return true;
            LogError("private config group " + group + " = [" + count.min + "," + count.max
                + "] expected [" + min + "," + max + "]");
            return false;
        }
        catch (Exception e)
        {
            LogError("private config GetCount(" + group + ") failed: " + e.GetType().Name + " " + e.Message);
            return false;
        }
    }

    private static void ClearConfigCache(int land)
    {
        int slotIndex = ConfigSlotIndex(land);
        ConfigSlots[slotIndex].Config = null;
        ConfigSlots[slotIndex].BiomePointer = 0UL;
        ConfigSlots[slotIndex].TemplatePointer = 0UL;
        ConfigSlots[slotIndex].CampaignPointer = 0UL;
    }

    // ------------------------------------------------------------------ 基础工具

    private static bool BaseEnabled()
    {
        try { return ModConfig.Enabled != null && ModConfig.Enabled.Value; }
        catch (Exception) { return false; }
    }

    private static bool GrantSwitchEnabled()
    {
        try { return ModConfig.CrossWorldMountsEnabled != null && ModConfig.CrossWorldMountsEnabled.Value; }
        catch (Exception) { return false; }
    }

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
