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

    internal const string LogPrefix = "[ExtensionIsland]";
    /// <summary>私有 config 模板（native-terrain §2：普通 God 岛 84672 作为希腊基础字段来源）。</summary>
    internal const string TemplateAssetName = "Greece_Land_God_Artemis";
    private const string PrivateConfigName = "Greece_Land_Extension_L11";
    /// <summary>Greek 普通地形场景名（native-terrain §8：Level.GenerateCurrentConfig 写入的 BlocksSceneName）。</summary>
    private const string GreekBlocksSceneName = "blocks_greece";

    private static LevelConfig _config;
    private static ulong _configBiomePointer;
    private static ulong _configTemplatePointer;
    private static ulong _configCampaignPointer;

    // 文件表准备状态：容量值（MAX_BIOME_ISLANDS）与"表已成功重建"必须分离。
    private static bool _filePropsPrepared;
    private static int _filePropsPreparedCapacity;
    private static bool _filePropsRefreshInProgress;

    // 洞口隔离：land11 生成/加载窗口内创建的实例指针（context 变化即清，防指针复用误拦）。
    private const int MaxTrackedCaves = 64;
    private static readonly HashSet<ulong> RestrictedCaves = new HashSet<ulong>();
    private static readonly HashSet<ulong> RestrictedPortals = new HashSet<ulong>();

    private static readonly HashSet<string> LoggedKeys = new HashSet<string>(StringComparer.Ordinal);

    // ------------------------------------------------------------------ travel 桥

    /// <summary>
    /// 可用性（新导航授权）：普通 Greek 离线 + owner 确证后，
    /// （Mod 总门 && CrossWorld 新授予开关）或 campaign.CurrentLand==11 或 visitedIslands.Contains(11)。
    /// 开关关闭后不再提供新导航，但当前/已访问岛保持可用（恢复/返航）。
    /// </summary>
    internal static bool IsAvailableForCurrentCampaign()
    {
        try
        {
            if (!TryScope(out CampaignSaveData campaign)) return false;
            bool current = campaign.CurrentLand == LandIndex;
            bool visited = false;
            try
            {
                Il2CppSystem.Collections.Generic.List<int> islands = campaign.visitedIslands;
                visited = islands != null && islands.Contains(LandIndex);
            }
            catch (Exception e) { LogOnce("visited-" + e.GetType().Name, "visitedIslands read failed: " + e.Message); }
            return ExtensionIslandPlan.AvailabilityGate(true, BaseEnabled() && GrantSwitchEnabled(), current, visited);
        }
        catch (Exception e)
        {
            LogOnce("available-" + e.GetType().Name, "availability read failed: " + e.Message);
            return false;
        }
    }

    /// <summary>
    /// 当前岛就是附加岛（与功能开关无关，保证恢复/返航）：普通 Greek 离线 + owner 确证 +
    /// campaign.CurrentLand==11（读档时 Game.currentLand 尚未同步，必须优先 campaign）。
    /// </summary>
    internal static bool IsCurrentExtension()
    {
        try
        {
            if (!TryScope(out CampaignSaveData campaign)) return false;
            return ExtensionIslandPlan.CurrentGate(true, campaign.CurrentLand == LandIndex);
        }
        catch (Exception e)
        {
            LogOnce("current-" + e.GetType().Name, "current extension read failed: " + e.Message);
            return false;
        }
    }

    // ------------------------------------------------------------------ 文件表容量 / 准备状态

    /// <summary>table-ready = 一次原生 UpdateFileProps 完整返回且容量 ≥ 12；MAX 值本身不算准备完成。</summary>
    internal static bool IsFilePropsPrepared()
    {
        try { return _filePropsPrepared && IslandSaveData.MAX_BIOME_ISLANDS >= ExtensionIslandPlan.MinimumFileCapacity; }
        catch (Exception) { return false; }
    }

    /// <summary>UpdateFileProps 前缀：标记刷新开始并清 ready；顺带把 MAX 单调抬到下限（不递归、不调原生）。</summary>
    internal static void OnFilePropsRefreshBegin()
    {
        try
        {
            _filePropsRefreshInProgress = true;
            _filePropsPrepared = false;   // 本轮确认前不得把旧确认当现役
            int current = IslandSaveData.MAX_BIOME_ISLANDS;
            if (current < ExtensionIslandPlan.MinimumFileCapacity)
            {
                IslandSaveData.MAX_BIOME_ISLANDS = ExtensionIslandPlan.MinimumFileCapacity;
                Log("file-props refresh: island capacity raised " + current + " -> "
                    + ExtensionIslandPlan.MinimumFileCapacity);
            }
        }
        catch (Exception e)
        {
            LogOnce("refresh-begin-" + e.GetType().Name, "file-props refresh adapter failed: " + e.Message);
        }
    }

    /// <summary>postfix/finalizer 收尾：只有本轮未失败才确认 table-ready；失败保持未准备。</summary>
    internal static void OnFilePropsRefreshFinished(bool failed)
    {
        try
        {
            _filePropsRefreshInProgress = false;
            if (failed)
            {
                _filePropsPrepared = false;
                LogOnce("refresh-failed", "file-props refresh failed; table stays not-ready (retry allowed)");
                return;
            }
            _filePropsPreparedCapacity = IslandSaveData.MAX_BIOME_ISLANDS;
            _filePropsPrepared = _filePropsPreparedCapacity >= ExtensionIslandPlan.MinimumFileCapacity;
        }
        catch (Exception e)
        {
            _filePropsPrepared = false;
            LogOnce("refresh-end-" + e.GetType().Name, "file-props finish adapter failed: " + e.Message);
        }
    }

    /// <summary>
    /// 文件寻址就绪门：已确认（容量≥12 且表成功重建）→ true 零副作用；
    /// 未确认且无进行中刷新 → 有界地提升容量并调用一次真实 UpdateFileProps（成功由 postfix 确认）。
    /// 进行中刷新返回 false（有界，不并发重建）；异常保持未准备并显式日志。
    /// </summary>
    internal static bool EnsureFileCapacity()
    {
        try
        {
            if (IsFilePropsPrepared()) return true;
            if (_filePropsRefreshInProgress) return false;
            try
            {
                _filePropsRefreshInProgress = true;   // 防原生重建内部重入触发二次重建
                int current = IslandSaveData.MAX_BIOME_ISLANDS;
                if (current < ExtensionIslandPlan.MinimumFileCapacity)
                    IslandSaveData.MAX_BIOME_ISLANDS = ExtensionIslandPlan.MinimumFileCapacity;
                IslandSaveData.UpdateFileProps();     // prefix/postfix/finalizer 维护 prepared
            }
            finally { _filePropsRefreshInProgress = false; }
            return IsFilePropsPrepared();
        }
        catch (Exception e)
        {
            LogError("island file capacity bootstrap failed: " + e.GetType().Name + " " + e.Message);
            return false;
        }
    }

    /// <summary>
    /// travel 调用的就绪门：文件表确认 + 当前 Greek 世界的私有 config 可创建且读回正确。
    /// 不要求当前岛就是 land11（地图阶段在旧岛打开）。失败即不得开放新岛旅行。
    /// </summary>
    internal static bool EnsureReady()
    {
        if (!EnsureFileCapacity()) return false;
        if (!ProgressionGuardReady()) return false;
        try
        {
            if (!TryScope(out CampaignSaveData campaign)) return false;
            return TryEnsureConfig(campaign, BiomeHolder.Inst, out _);
        }
        catch (Exception e)
        {
            LogError("ensure ready failed: " + e.GetType().Name + " " + e.Message);
            return false;
        }
    }

    // ------------------------------------------------------------------ GetConfigFromIndex 前缀判定体

    /// <summary>
    /// 返回 true 表示本 prefix 已接管（调用方必须跳过原生）。接管条件全部满足：
    /// 1) configIndex==LandIndex；2) 调用者就是当前 <c>BiomeHolder.Inst</c> 同一实例（错误 holder 透传）；
    /// 3) Greek biome；4) 确证 scope（普通 Greek 离线 + owner 无误）且 campaign.CurrentLand==11。
    /// 该场景下创建失败→result=null 屏蔽（fail-closed，不落回数组越界）；其余情况一律 false 透传。
    /// </summary>
    internal static bool TryHandleConfigRequest(BiomeHolder holder, int configIndex, out LevelConfig result)
    {
        result = null;
        if (configIndex != LandIndex) return false;
        if (!IsCurrentHolder(holder)) return false;      // exact holder 实例门：错误 holder 不改原
        if (holder.BiomeIndex != BiomeHolder.GreeceBiomeIndex) return false;   // 其他 world 透传
        if (!TryScope(out CampaignSaveData campaign)) return false;            // 联机/挑战/无 owner：透传
        if (campaign.CurrentLand != LandIndex) return false;                   // 非当前 campaign11：透传
        if (!ProgressionGuardReady())
        {
            LogOnce("config-guard-blocked", "own current campaign land " + LandIndex
                + " but native progression guard not installed; config serving blocked (fail-closed)");
            return true;   // 不返回可生成的 11 config，也不落回原生数组
        }
        try
        {
            if (TryEnsureConfig(campaign, holder, out LevelConfig config))
            {
                result = config;
                LogOnce("config-served", "private extension config served for land " + LandIndex);
                return true;
            }
            LogOnce("config-blocked", "own current campaign land " + LandIndex
                + " config failed to build; navigation blocked (no native array fallthrough)");
            return true;
        }
        catch (Exception e)
        {
            LogOnce("config-error-" + e.GetType().Name, "extension config request failed closed: " + e.Message);
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
    /// 首次 land11 生成前置容器门（FIRST-GENERATION-CONTRACT）：独立于 SameOwner，先确证“同一当前
    /// campaign + CurrentLand==11 + 场景 land11 + 非保存中 + reign 整数稳定”，再保证 currentReign
    /// 的 landData 至少有 12 槽：
    /// - 已有短容器：**原地** append default LandMapData（保留既有项与容器 Pointer）；
    /// - 容器为 null：本地建 12 default 槽的新 List，复核身份后用**最新** reign wrapper 赋引用并
    ///   `campaign.currentReign = reign` 整体写回（值类型包装语义，不能只改临时 wrapper）；
    /// - 已 ≥12：零写入。
    /// 只补 default 槽：不更新 visited/价格/支付/资源，不改 previousReigns/MaxIslands/secured。
    /// </summary>
    internal static bool TryEnsureExtensionLandDataSlots(CampaignSaveData campaign, int land)
    {
        try
        {
            if (campaign == null || land != LandIndex) return true;   // 只服务 land11
            if (!TryScope(out CampaignSaveData scoped) || PointerOf(scoped) != PointerOf(campaign))
            {
                LogOnce("container-scope", "landData container gate: campaign/owner not proven");
                return false;
            }
            if (campaign.CurrentLand != LandIndex) return false;
            Managers managers = Managers.Inst;
            if (managers == null || managers.game == null || managers.game.currentLand != LandIndex) return false;
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
            if (landData != null && landData.Count >= ExtensionIslandPlan.MinimumFileCapacity) return true;

            if (landData != null)
            {
                while (landData.Count < ExtensionIslandPlan.MinimumFileCapacity)
                    landData.Add(new CampaignSaveData.LandMapData());
                Log("landData container padded in place to " + landData.Count + " slots for land " + LandIndex);
            }
            else
            {
                var fresh = new Il2CppSystem.Collections.Generic.List<CampaignSaveData.LandMapData>(
                    ExtensionIslandPlan.MinimumFileCapacity);
                for (int i = 0; i < ExtensionIslandPlan.MinimumFileCapacity; i++)
                    fresh.Add(new CampaignSaveData.LandMapData());
                if (PointerOf(scoped) != PointerOf(campaign) || campaign.CurrentLand != LandIndex
                    || SafeReign(campaign) != reignIndex)
                {
                    LogError("landData container gate: ownership changed while building; not published");
                    return false;
                }
                reign.landData = fresh;
                campaign.currentReign = reign;   // 值类型包装：必须整体写回当前 campaign
                Log("landData container created with " + ExtensionIslandPlan.MinimumFileCapacity
                    + " default slots for land " + LandIndex);
            }

            // 终核：从 campaign 重新读回，确认同一容器且槽位数满足。
            if (PointerOf(scoped) != PointerOf(campaign) || campaign.CurrentLand != LandIndex
                || SafeReign(campaign) != reignIndex)
                return false;
            CampaignSaveData.ReignInfo verified = campaign.currentReign;
            if (verified == null || verified.landData == null
                || verified.landData.Count < ExtensionIslandPlan.MinimumFileCapacity)
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

    /// <summary>受限上下文：确证 scope + campaign 与当前场景都在 land11（生成/加载/游玩窗口）。</summary>
    internal static bool IsRestrictedCaveContext()
    {
        try
        {
            if (!TryScope(out CampaignSaveData campaign)) return false;
            if (campaign.CurrentLand != LandIndex) return false;
            Managers managers = Managers.Inst;
            return managers != null && managers.game != null && managers.game.currentLand == LandIndex;
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
    /// 附加岛的完成保险门：只对确证 Greek 普通离线 + **当前场景** land11（<c>Game.currentLand</c>，
    /// 与 CliffPortalState/CrownStatue 调用点同源）返回 true；0..10/其他世界/挑战全返回 false（原样）。
    /// 不改 Boat/CanPay/SailAway，不触碰存档。
    /// </summary>
    internal static bool ShouldBlockDefeatGreed(Game game)
    {
        try
        {
            if (!TryScope(out _)) return false;
            bool extension = game != null && game.currentLand == LandIndex;
            if (extension) LogOnce("defeat-blocked", "Game.DefeatGreed blocked on extension land " + LandIndex);
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
    /// 普通 Greek 离线 + owner 确证：Global.loaded 非空、非挑战、非联机、Greek biome、
    /// <c>CampaignSaveData.current</c> 非空且与 <c>GetCurrentCampaign()</c> 同一实例（菜单/无选中
    /// 时原生会回退 slot0，必须拒绝）。任何未知/异常一律 false（fail-closed）。
    /// </summary>
    internal static bool TryScope(out CampaignSaveData campaign)
    {
        campaign = null;
        try
        {
            GlobalSaveData global = GlobalSaveData.loaded;
            if (global == null || global.InChallenge) return false;
            if (NetworkBigBoss.IsOnline) return false;
            BiomeHolder biome = BiomeHolder.Inst;
            if (biome == null || biome.BiomeIndex != BiomeHolder.GreeceBiomeIndex) return false;
            CampaignSaveData current = CampaignSaveData.current;
            if (current == null) return false;
            CampaignSaveData owner = global.GetCurrentCampaign();
            if (owner == null) return false;
            if (PointerOf(owner) != PointerOf(current)) return false;
            campaign = current;
            return true;
        }
        catch (Exception e)
        {
            LogOnce("scope-" + e.GetType().Name, "campaign scope read failed: " + e.Message);
            return false;
        }
    }

    // ------------------------------------------------------------------ 私有 config

    private static bool TryEnsureConfig(CampaignSaveData campaign, BiomeHolder holder, out LevelConfig config)
    {
        config = null;
        try
        {
            BiomeData biomeData = holder != null ? holder.curBiomeData : null;
            if (biomeData == null) return false;
            Il2CppReferenceArray<LevelConfig> configs = biomeData.levelConfigs;
            if (configs == null || configs.Length <= 1) return false;
            if (!TryFindTemplateIndex(configs, out int templateIndex))
            {
                LogOnce("template-missing", "normal Greek template config not found in levelConfigs");
                return false;
            }
            LevelConfig template = configs[templateIndex];
            if (template == null) return false;

            ulong biomePointer = PointerOf(biomeData);
            ulong templatePointer = PointerOf(template);
            ulong campaignPointer = PointerOf(campaign);
            if (_config != null
                && _configBiomePointer == biomePointer
                && _configTemplatePointer == templatePointer
                && _configCampaignPointer == campaignPointer)
            {
                config = _config;
                return true;
            }

            LevelConfig built = BuildPrivateConfig(template);
            if (built == null)
            {
                ClearConfigCache();
                return false;
            }
            _config = built;
            _configBiomePointer = biomePointer;
            _configTemplatePointer = templatePointer;
            _configCampaignPointer = campaignPointer;
            config = built;
            return true;
        }
        catch (Exception e)
        {
            LogError("private config ensure failed: " + e.GetType().Name + " " + e.Message);
            ClearConfigCache();
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
    /// 创建私有 config：<c>Object.Instantiate</c> 复制模板字段后逐项落实合同并读回验证。
    /// 不复用/不修改共享模板；返回 null 即失败（调用方 fail-closed）。
    /// </summary>
    internal static LevelConfig BuildPrivateConfig(LevelConfig template)
    {
        LevelConfig clone = UnityEngine.Object.Instantiate(template);
        if (clone == null)
        {
            LogError("private config instantiate returned null");
            return null;
        }

        clone.name = PrivateConfigName;
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

    private static void ClearConfigCache()
    {
        _config = null;
        _configBiomePointer = 0UL;
        _configTemplatePointer = 0UL;
        _configCampaignPointer = 0UL;
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
