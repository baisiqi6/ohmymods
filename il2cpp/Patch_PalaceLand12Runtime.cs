// Private land12 (天朝物理岛) minimal runtime. SOURCE-only: Root wires the config dispatcher,
// the single file-capacity writer, the DefeatGreed dispatcher and the entry/reload handoff calls;
// this file exposes the land12 branch, the generation padding, the insurance gate and the
// prepare/gate/handoff APIs. No native acceptance claimed.
using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using KingdomEnhancedMod.PalaceSource;

namespace KingdomEnhancedMod;

/// <summary>
/// 私有物理 land12（天朝最小配置）最小运行时。职责与边界：
/// - <see cref="TryHandleConfigRequest"/>：Root 现有单一 config dispatcher 的 land12 分支。
///   仅 exact current Greek holder + 确证 scope（<see cref="ExtensionIslandRuntime.TryScope"/>，
///   普通 Greek 离线非挑战 owner 同一实例）+ <c>campaign.CurrentLand==12</c> 才接管；
///   接管后失败 result=null（fail-closed，不回 native 越界）。首次服务时绑定 G/C/H/reign/game
///   与 durable guid/slot（供随后 return handoff 复核）。owner 漂移只有携带单个 active Reload
///   交接 token（<see cref="BeginReloadHandoff"/>：新 Game、durable guid/slot/reign、trip 记录、
///   same exact holder 全部复核通过）才更新绑定并复用唯一 clone；无 token 的漂移仍 fail-closed。
///   进程内只存在一个 published clone，仅同 exact holder 复用；不同 holder 一律拒绝，不做二次分配。
/// - <see cref="PrepareForEntry"/>：Root 在 PalaceVisibleEntryScene 既有有界就绪窗的 actual Playing
///   交接（源岛）调用——收费前用现有 factory 准备唯一 clone（首次、cache 无 pending 时才调用一次
///   TryGetClone）；同 owner/同 holder 只复用；成功后绑定 G/C/H/reign/game + durable guid/slot；
///   失败不重建、不二次分配。不是 CanStart。
/// - <see cref="TryDescribeVerifiedReloadHandoff"/>：唯一 active token 的窄只读已验证事实出口（供
///   PalaceSaveBindingSource 单次替换 runtime epoch）；不交出可变 token，不造第二 ledger。
/// - <see cref="CanEnterPrepared"/>：收费前只读资格门——已准备标记、sticky/pendingEmpty、published
///   指针与 live clone 同 exact holder、当前 G/C/H/reign/game 与 durable guid/slot 全匹配；
///   零 Instantiate、零 ValidatePrivateConfig/GetCount、零写入；未知/未准备/交接进行中一律拒绝收费。
/// - <see cref="BeginReloadHandoff"/>/<see cref="CompleteReloadHandoff"/>/<see cref="AbortReloadHandoff"/>：
///   单次 native Reload 的 owner 交接 token（武装→完成/中止）；完成前不销毁 token、token 只能完成
///   一次、无重试；中止只停止服务，clone 保留。
/// - <see cref="InstallOnce"/>：Enabled 时注册 <c>Level.GenerateCurrentConfig(int)</c> 前缀（无属性，
///   禁用即不挂载）。仅 campaign/scene 同为 land12 且非保存中时补齐 currentReign.landData≥13；
///   前置门失败返回 false 阻止本次生成并记录，绝不让 native 在 12 槽容器上生成 land12。
///   生成门（Generation）约束不因交接放宽。
/// - <see cref="RequiredFileCapacity"/>：只报告需求（Enabled?13:12）；MAX 写入与
///   <c>UpdateFileProps</c> 仍由现有唯一 Extension writer 负责，本类不写。
/// - <see cref="ShouldBlockDefeatGreed"/>：land12 场景级保险门，供 Root 现有 DefeatGreed dispatcher 组合。
/// Enabled = <c>PALACE_LAND12_PRIVATE</c> 固定 profile 能力（DedicatedR7V1；不读环境/不比私有根）；
/// 否则所有入口零效果、零写入。指针只证明本次进程内实例同一性，不代表存档、到达或 future load 通过。
/// 日志只在实际 prepare/handoff/config 复用/到达时输出 currentH/publishedH/clonePtr/G/C/Game，不做 frame 扫描。
/// </summary>
internal static class PalaceLand12Runtime
{
    internal const int LandIndex = 12;

    /// <summary>landData 目标槽数：0..11 既有 + 索引 12。</summary>
    internal const int LandDataSlots = 13;

    private const string EnableEnv = "PALACE_LAND12_PRIVATE";

    /// <summary>
    /// 单次 native Reload 的 owner 交接 token。Begin 时绑定旧 game/managers/G/C/H/reign + durable
    /// guid/slot/biome/start + tripId/phase/record source/target；config 服务在新 owner 上的采纳、
    /// Complete 与唯一只读事实出口 <see cref="TryDescribeVerifiedReloadHandoff"/> 都只认这一个 token。
    /// </summary>
    private sealed class ReloadHandoffToken
    {
        internal string TripId = "";
        internal string Phase = "";
        internal int TargetLand = -1;
        internal int RecordSourceLand = -1;
        internal string CampaignGuid = "";
        internal int CampaignSlot = -1;
        internal int Reign = -1;
        internal int Biome = -1; // Begin 时 campaign.biomeIndex（durable）
        internal int Start = -1; // Begin 时 campaign.realStartDateTime（int）
        internal ulong OldGame, OldManagers, Global, Campaign, Holder, NewGame;
        internal bool OwnerAdopted;
    }

    private static readonly HashSet<string> LoggedKeys = new HashSet<string>(StringComparer.Ordinal);
    private static readonly PalacePrivateConfigCache ConfigCache = new PalacePrivateConfigCache();

    // owner 绑定（首次服务/prepare 或交接采纳写入）；无有效 token 的漂移一律 fail-closed。
    private static bool _ownerBound;
    private static ulong _boundGlobal, _boundCampaign, _boundHolder, _boundGame;
    private static int _boundReign = -1;
    private static string _boundGuid = "";
    private static int _boundSlot = -1;

    // 收费前准备快照（PrepareForEntry 成功或回程 Complete 复用成功才置位）。
    private static bool _entryPrepared;
    private static ulong _prepGlobal, _prepCampaign, _prepHolder, _prepGame;
    private static int _prepReign = -1;
    private static string _prepGuid = "";
    private static int _prepSlot = -1;

    // 唯一 active Reload 交接 token（无重试；成功完成或中止后为空）。
    private static ReloadHandoffToken _handoff;
    internal static bool HasActiveReloadHandoff => _handoff != null;

    // 日志去重：只按内容变化输出（事件驱动，不做 frame 扫描）。
    private static string _lastPrepareLog = "", _lastServeLog = "", _lastGateLog = "";

    private static bool _installed, _generationGateReady;
    internal static bool Ready => Enabled && _generationGateReady && ExtensionIslandRuntime.IsFilePropsPrepared();

    /// <summary>固定 DedicatedR7V1 profile 能力（纯查表，不读环境）；业务 save 绑定由 PalaceSaveBindingSource 在各门复核。</summary>
    internal static bool Enabled
    {
        get
        {
            try
            {
                return PalaceRuntimeProfile.Capability(EnableEnv);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// 容量需求（只报告）：Enabled 时取共享 <see cref="ExtensionIslandRuntime.RequiredFileCapacity"/>
    /// （产品文件表 14 ≥ land12 最低 13；LandDataSlots 仍是 landData 补槽下限）。不写 MAX、不调 UpdateFileProps。
    /// </summary>
    internal static int RequiredFileCapacity => Enabled ? ExtensionIslandRuntime.RequiredFileCapacity : 12;

    /// <summary>
    /// Enabled 时注册 <c>Level.GenerateCurrentConfig(int)</c> 前缀（手动挂载、无属性：禁用即无 hook）。
    /// 目标不可解析时只记录、不挂载（失败不拖垮插件），且不重试。
    /// </summary>
    internal static void InstallOnce(Harmony harmony)
    {
        if (_installed || !Enabled) return;
        _installed = true;
        try
        {
            MethodInfo target = AccessTools.Method(typeof(Level), "GenerateCurrentConfig", new[] { typeof(int) });
            if (target == null)
            {
                Log("ERROR Level.GenerateCurrentConfig(int) not resolvable; land12 generation gate NOT installed");
                return;
            }
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(PalaceLand12Runtime), nameof(GenerateCurrentConfigPrefix)));
            _generationGateReady = true;
            Log("land12 generation gate installed: Level.GenerateCurrentConfig(int) prefix");
        }
        catch (Exception e)
        {
            Log("ERROR land12 generation gate install failed: " + e.GetType().Name + " " + e.Message);
        }
    }

    /// <summary>
    /// 生成前缀：仅 land12 + 确证 Greek owner + campaign/scene 同为 12 时接手；非保存中且
    /// landData 补齐 ≥13 才放行原生。前置门（保存冲突 / 容器门失败 / 异常）返回 false 阻止本次生成。
    /// 其余 land、其他 world、owner 未确证、场景尚未落到 land12：原样透传（返回 true）。
    /// 该生成门约束不因 owner 交接放宽。
    /// </summary>
    private static bool GenerateCurrentConfigPrefix(Level __instance, int __0)
    {
        if (!Enabled) return true;
        try
        {
            if (!ExtensionIslandRuntime.TryScope(out CampaignSaveData campaign)) return true;
            if (campaign.CurrentLand != LandIndex) return true;
            Managers managers = Managers.Inst;
            LogOnce("generation-entry", "native GenerateCurrentConfig seed=" + __0 + "; campaignLand=" + campaign.CurrentLand
                + "; gameLand=" + (managers != null && managers.game != null ? managers.game.currentLand : -1));
            if (managers == null || managers.level == null || __instance == null
                || managers.level.Pointer != __instance.Pointer || managers.game == null || managers.game.currentLand != LandIndex)
            { Log("ERROR owned land12 generation scene/Level mismatch; blocking"); return false; }
            // 到这里才是本前缀负责的 land12 生成请求：容器未就绪就不准 native 生成。
            if (IslandSaveData.isSavingGame)
            {
                Log("ERROR land12 generation requested while island save in progress; blocking generation");
                return false;
            }
            // Fresh generation can bypass Pop entirely. Failure remains pending/unknown, never raw repair.
            if (!PalaceClockBootstrap.TryPrepareConfig(managers.game, out string clockPrepare))
                Log("clock early config pending: " + clockPrepare);
            if (TryEnsureLand12Slots(campaign)) return true;
            Log("ERROR land12 landData container gate failed; blocking generation (no 12-slot fallthrough)");
            return false;
        }
        catch (Exception e)
        {
            Log("ERROR land12 generation gate fault: " + e.GetType().Name + " " + e.Message + "; blocking generation");
            return false;
        }
    }

    /// <summary>
    /// Root 现有 config dispatcher 调用的 land12 分支。返回 true 表示已接管（调用方跳过原生）：
    /// index!=12 / holder 非当前实例 / holder 非 Greek / owner 未确证 / campaign 非 12 一律 false 透传；
    /// 归自己服务的场景下创建失败 result=null（fail-closed，不落回原生数组）。
    /// owner 漂移只有在 active Reload 交接 token 且新 Game、durable guid/slot/reign、trip 记录、
    /// same exact holder 全部复核通过时才更新绑定并复用唯一 clone；其余（含无 token）一律 fail-closed。
    /// </summary>
    internal static bool TryHandleConfigRequest(BiomeHolder holder, int configIndex, out LevelConfig result)
    {
        result = null;
        if (!Enabled) return false;
        if (configIndex != LandIndex) return false;
        if (!IsCurrentHolder(holder)) return false;
        try
        {
            if (holder.BiomeIndex != BiomeHolder.GreeceBiomeIndex) return false;
        }
        catch (Exception)
        {
            return false;
        }
        if (!ExtensionIslandRuntime.TryScope(out CampaignSaveData campaign)) return false;
        if (campaign.CurrentLand != LandIndex) return false;

        if (!Ready) { LogOnce("not-ready", "owned land12 generation/table gate not ready; config blocked"); return true; }
        ulong globalPtr = PalacePrivateConfigFactory.PointerOf(GlobalSaveData.loaded);
        ulong campaignPtr = PalacePrivateConfigFactory.PointerOf(campaign);
        ulong holderPtr = PalacePrivateConfigFactory.PointerOf(holder);
        ulong gamePtr = CurrentGamePointer();
        int reign = SafeReign(campaign);
        if (globalPtr == 0 || campaignPtr == 0 || holderPtr == 0 || gamePtr == 0 || reign < 0)
        { LogOnce("owner-unknown", "owned land12 identity unavailable; config blocked"); return true; }

        if (!_ownerBound)
        {
            BindOwner(globalPtr, campaignPtr, holderPtr, gamePtr, reign, campaign, "config-first-bind");
        }
        else if (globalPtr != _boundGlobal || campaignPtr != _boundCampaign
            || holderPtr != _boundHolder || reign != _boundReign || gamePtr != _boundGame)
        {
            if (!TryAdoptHandoffOwner(globalPtr, campaignPtr, holderPtr, gamePtr, campaign, out string adoptReason))
            {
                LogServe("config request refused: owner drift without valid handoff (" + adoptReason
                    + "); fail-closed, no rebuild", holderPtr, globalPtr, campaignPtr, gamePtr);
                return true;
            }
        }

        if (!TryGetReusableClone(holder, holderPtr, out LevelConfig clone, out string report))
        {
            LogServe("config request failed closed: " + report, holderPtr, globalPtr, campaignPtr, gamePtr);
            return true;
        }
        if (!FreshOwner(globalPtr, campaignPtr, holderPtr, reign))
        {
            LogServe("config request refused: owner changed during construction; clone retained but not served",
                holderPtr, globalPtr, campaignPtr, gamePtr);
            return true;
        }
        LogServe("served " + report, holderPtr, globalPtr, campaignPtr, gamePtr);
        result = clone;
        return true;
    }

    // ------------------------------------------------------------------ 收费前准备 / 资格门

    /// <summary>
    /// Root 在 PalaceVisibleEntryScene 既有有界就绪窗的 actual Playing 交接（源岛）调用：
    /// 收费前把唯一 clone 准备到 published，并用当前 G/C/H/reign/game + durable guid/slot 绑定。
    /// 仅私有 Greek 离线 owner scope + exact 当前 holder + Managers 当前 Game +
    /// <c>campaign.CurrentLand==game.currentLand</c> 时动作；首次/无 pending 才调用一次 TryGetClone，
    /// 同 owner/同 holder（含同 game）只复用；任何失败不重建、不二次分配。不是 CanStart。
    /// </summary>
    internal static bool PrepareForEntry(Game game, out string reason)
    {
        reason = "";
        if (!Enabled) { reason = "land12 私有模式未启用"; return false; }
        try
        {
            if (game == null) { reason = "Game 为空"; return false; }
            Managers managers = Managers.Inst;
            if (managers == null || managers.game == null || managers.game.Pointer != game.Pointer)
            { reason = "不是当前 Managers.Game"; return false; }
            if (!ExtensionIslandRuntime.TryScope(out CampaignSaveData campaign))
            { reason = "非私有 Greek 离线 owner scope"; return false; }
            BiomeHolder holder = BiomeHolder.Inst;
            if (holder == null || !IsCurrentHolder(holder)) { reason = "当前 holder 实例不可用"; return false; }
            if (holder.BiomeIndex != BiomeHolder.GreeceBiomeIndex) { reason = "当前 holder 非 Greek biome"; return false; }
            int land = SafeLand(game);
            if (campaign.CurrentLand != land)
            { reason = "campaign/Game 当前岛不一致(" + campaign.CurrentLand + "/" + land + ")"; return false; }

            ulong globalPtr = PalacePrivateConfigFactory.PointerOf(GlobalSaveData.loaded);
            ulong campaignPtr = PalacePrivateConfigFactory.PointerOf(campaign);
            ulong holderPtr = PalacePrivateConfigFactory.PointerOf(holder);
            ulong gamePtr = PalacePrivateConfigFactory.PointerOf(game);
            int reign = SafeReign(campaign);
            if (globalPtr == 0 || campaignPtr == 0 || holderPtr == 0 || gamePtr == 0 || reign < 0)
            { reason = "owner/game 指针不可读"; return false; }
            if (!PalaceTripContext.TryBinding(GlobalSaveData.loaded, campaign, out string guid, out int slot, out string bindDetail))
            { reason = "durable campaign 身份不可用：" + bindDetail; return false; }

            // 同 owner/同 holder/同 game 且已准备：只读复用，不再触发任何 cache 分配路径。
            if (_entryPrepared && globalPtr == _prepGlobal && campaignPtr == _prepCampaign
                && holderPtr == _prepHolder && gamePtr == _prepGame && reign == _prepReign
                && guid == _prepGuid && slot == _prepSlot)
            {
                if (!CloneReusableForHolder(holderPtr, out string reuseDetail))
                { reason = "已准备 clone 当前不可复用：" + reuseDetail; LogPrepare("prepare=reuse-refused; " + reason); return false; }
                reason = "复用已准备 clone";
                LogPrepare("prepare=reuse; " + IdentityLine(holderPtr, globalPtr, campaignPtr, gamePtr));
                return true;
            }

            // 漂移的 owner 只有经过 Reload 交接才能重新绑定；无绑定的首次准备直接建立绑定。
            if (_ownerBound && (globalPtr != _boundGlobal || campaignPtr != _boundCampaign
                || holderPtr != _boundHolder || reign != _boundReign || gamePtr != _boundGame))
            {
                reason = "owner 已漂移且无有效 Reload 交接；拒绝准备";
                LogPrepare("prepare=refused; " + reason + "; " + IdentityLine(holderPtr, globalPtr, campaignPtr, gamePtr));
                return false;
            }
            if (_ownerBound && _boundGuid.Length != 0 && (guid != _boundGuid || slot != _boundSlot))
            {
                reason = "durable guid/slot 与绑定不一致；拒绝准备";
                LogPrepare("prepare=refused; " + reason);
                return false;
            }

            if (!TryGetReusableClone(holder, holderPtr, out _, out string cacheReport))
            {
                reason = "配置准备失败（不重建）：" + cacheReport;
                LogPrepare("prepare=failed; " + reason + "; " + IdentityLine(holderPtr, globalPtr, campaignPtr, gamePtr));
                return false;
            }
            if (!FreshEntryOwner(globalPtr, campaignPtr, holderPtr, reign, gamePtr, land))
            {
                reason = "准备期间 owner/game 变化；不标记准备";
                LogPrepare("prepare=drift; " + reason + "; " + IdentityLine(holderPtr, globalPtr, campaignPtr, gamePtr));
                return false;
            }

            if (!_ownerBound)
            {
                BindOwner(globalPtr, campaignPtr, holderPtr, gamePtr, reign, campaign, "prepare");
            }
            else if (_boundGuid.Length == 0 && guid.Length != 0)
            {
                _boundGuid = guid;
                _boundSlot = slot;
                Log("owner durable identity backfilled from prepare; guid=" + guid + " slot=" + slot);
            }
            PalaceDestinationAdmission.NoteDestinationPrepare(campaign); // sidecar 磁盘 IO 只在源岛准备窗口执行
            PalaceDestinationAdmission.RunResourcePrecheck();            // 资源预检（有界 IO）只在准备窗口执行一次
            _entryPrepared = true;
            _prepGlobal = globalPtr;
            _prepCampaign = campaignPtr;
            _prepHolder = holderPtr;
            _prepGame = gamePtr;
            _prepReign = reign;
            _prepGuid = guid;
            _prepSlot = slot;
            reason = "已准备：" + cacheReport;
            LogPrepare("prepare=ok; " + IdentityLine(holderPtr, globalPtr, campaignPtr, gamePtr)
                + "; guid=" + guid + " slot=" + slot + "; " + cacheReport);
            return true;
        }
        catch (Exception e)
        {
            reason = "prepare 异常：" + e.GetType().Name;
            Log("ERROR entry prepare fault: " + e.GetType().Name + " " + e.Message);
            return false;
        }
    }

    /// <summary>
    /// 收费前配置资格门（Root 在 CanStart/支付路径调用）。只读检查：已准备成功标记、sticky=false、
    /// pending Empty、published 指针与 live clone 属于当前 exact holder、当前 G/C/H/reign/game 与
    /// durable guid/slot 全部匹配。零 Instantiate、零 ValidatePrivateConfig/GetCount、零写入；
    /// 未知/未准备/漂移/交接进行中一律拒绝收费（fail-closed）。
    /// </summary>
    internal static bool CanEnterPrepared(Game game, out string reason)
    {
        reason = "";
        if (!Enabled) { reason = "land12 私有模式未启用"; return false; }
        try
        {
            if (!_entryPrepared) { reason = "入口配置未准备（PrepareForEntry 未成功）"; LogGate("CanEnter=refused; " + reason); return false; }
            if (_handoff != null) { reason = "Reload 交接进行中，暂停收费"; LogGate("CanEnter=refused; " + reason); return false; }
            if (game == null) { reason = "Game 为空"; LogGate("CanEnter=refused; " + reason); return false; }
            Managers managers = Managers.Inst;
            if (managers == null || managers.game == null || managers.game.Pointer != game.Pointer)
            { reason = "不是当前 Managers.Game"; LogGate("CanEnter=refused; " + reason); return false; }
            if (!ExtensionIslandRuntime.TryScope(out CampaignSaveData campaign))
            { reason = "非私有 Greek 离线 owner scope"; LogGate("CanEnter=refused; " + reason); return false; }
            BiomeHolder holder = BiomeHolder.Inst;
            if (holder == null || !IsCurrentHolder(holder)) { reason = "当前 holder 实例不可用"; LogGate("CanEnter=refused; " + reason); return false; }
            if (holder.BiomeIndex != BiomeHolder.GreeceBiomeIndex) { reason = "当前 holder 非 Greek biome"; LogGate("CanEnter=refused; " + reason); return false; }
            int land = SafeLand(game);
            if (campaign.CurrentLand != land)
            { reason = "campaign/Game 当前岛不一致(" + campaign.CurrentLand + "/" + land + ")"; LogGate("CanEnter=refused; " + reason); return false; }

            ulong globalPtr = PalacePrivateConfigFactory.PointerOf(GlobalSaveData.loaded);
            ulong campaignPtr = PalacePrivateConfigFactory.PointerOf(campaign);
            ulong holderPtr = PalacePrivateConfigFactory.PointerOf(holder);
            ulong gamePtr = PalacePrivateConfigFactory.PointerOf(game);
            int reign = SafeReign(campaign);
            if (globalPtr == 0 || campaignPtr == 0 || holderPtr == 0 || gamePtr == 0 || reign < 0)
            { reason = "owner/game 指针不可读"; LogGate("CanEnter=refused; " + reason); return false; }
            if (globalPtr != _prepGlobal || campaignPtr != _prepCampaign || holderPtr != _prepHolder
                || gamePtr != _prepGame || reign != _prepReign)
            {
                reason = "owner/game/reign 与准备时不一致";
                LogGate("CanEnter=refused; " + reason, holderPtr, globalPtr, campaignPtr, gamePtr);
                return false;
            }
            if (!PalaceTripContext.TryBinding(GlobalSaveData.loaded, campaign, out string guid, out int slot, out string bindDetail))
            { reason = "durable campaign 身份不可用：" + bindDetail; LogGate("CanEnter=refused; " + reason); return false; }
            if (guid != _prepGuid || slot != _prepSlot)
            {
                reason = "durable guid/slot 与准备时不一致";
                LogGate("CanEnter=refused; " + reason, holderPtr, globalPtr, campaignPtr, gamePtr);
                return false;
            }
            if (!CloneReusableForHolder(holderPtr, out string liveDetail))
            {
                reason = "已准备 clone 不可用：" + liveDetail;
                LogGate("CanEnter=refused; " + reason, holderPtr, globalPtr, campaignPtr, gamePtr);
                return false;
            }
            reason = "已准备 clone 可用";
            LogGate("CanEnter=ok; " + IdentityLine(holderPtr, globalPtr, campaignPtr, gamePtr));
            return true;
        }
        catch (Exception e)
        {
            reason = "资格门异常：" + e.GetType().Name;
            LogGate("CanEnter=fault; " + reason);
            return false;
        }
    }

    // ------------------------------------------------------------------ native Reload 交接 token

    /// <summary>
    /// Root 在 source+global 保存 72、目标选定、紧邻唯一 <c>game.Reload</c> 前调用：武装唯一 active
    /// token，绑定旧 game/current G/C/H/reign + durable guid/slot + tripId/phase/record source/target。
    /// 无重试：已有 active token、记录/身份不一致、owner 未绑定或漂移一律拒绝（调用方 Fail，不自动重放）。
    /// </summary>
    internal static bool BeginReloadHandoff(Game sourceGame, string campaignGuid, int campaignSlot,
        string tripId, int targetLand, out string reason)
    {
        reason = "";
        if (!Enabled) { reason = "land12 私有模式未启用"; return false; }
        try
        {
            if (_handoff != null)
            { reason = "已有 active handoff token(trip=" + _handoff.TripId + ")；无重试"; return false; }
            if (sourceGame == null) { reason = "sourceGame 为空"; return false; }
            if (string.IsNullOrEmpty(tripId) || string.IsNullOrEmpty(campaignGuid) || campaignSlot < 0)
            { reason = "trip/campaign 参数无效"; return false; }
            if (targetLand < 0 || targetLand > LandIndex) { reason = "target 越界：" + targetLand; return false; }
            Managers managers = Managers.Inst;
            if (managers == null || managers.game == null || managers.game.Pointer != sourceGame.Pointer)
            { reason = "sourceGame 不是当前 Managers.Game"; return false; }
            if (IslandSaveData.isSavingGame) { reason = "仍在保存中"; return false; }
            if (!ExtensionIslandRuntime.TryScope(out CampaignSaveData campaign))
            { reason = "非私有 Greek 离线 owner scope"; return false; }
            BiomeHolder holder = BiomeHolder.Inst;
            if (holder == null || !IsCurrentHolder(holder)) { reason = "当前 holder 实例不可用"; return false; }
            if (holder.BiomeIndex != BiomeHolder.GreeceBiomeIndex) { reason = "当前 holder 非 Greek biome"; return false; }

            ulong globalPtr = PalacePrivateConfigFactory.PointerOf(GlobalSaveData.loaded);
            ulong campaignPtr = PalacePrivateConfigFactory.PointerOf(campaign);
            ulong holderPtr = PalacePrivateConfigFactory.PointerOf(holder);
            ulong gamePtr = PalacePrivateConfigFactory.PointerOf(sourceGame);
            ulong managersPtr = PalacePrivateConfigFactory.PointerOf(managers);
            int reign = SafeReign(campaign);
            if (globalPtr == 0 || campaignPtr == 0 || holderPtr == 0 || gamePtr == 0 || managersPtr == 0 || reign < 0)
            { reason = "owner 指针不可读"; return false; }
            if (!PalaceTripContext.TryBinding(GlobalSaveData.loaded, campaign, out string guid, out int slot, out string bindDetail))
            { reason = "durable campaign 身份不可用：" + bindDetail; return false; }
            if (guid != campaignGuid || slot != campaignSlot)
            { reason = "durable guid/slot 与参数不一致"; return false; }
            if (!_ownerBound) { reason = "owner 尚未绑定（config 未服务且未 prepare）"; return false; }
            if (globalPtr != _boundGlobal || campaignPtr != _boundCampaign
                || holderPtr != _boundHolder || reign != _boundReign || gamePtr != _boundGame)
            { reason = "source owner 与绑定不一致；需先完成交接"; return false; }
            if (_boundGuid.Length != 0 && (guid != _boundGuid || slot != _boundSlot))
            { reason = "durable guid/slot 与绑定不一致"; return false; }

            if (campaign.CurrentLand != targetLand)
            { reason = "campaign 尚未选定 target(" + campaign.CurrentLand + "/" + targetLand + ")"; return false; }
            if (PalaceTripContext.Read(GlobalSaveData.loaded, out PalaceTripRecord record, out string readDetail)
                != PalaceTripContext.ReadStatus.Ok)
            { reason = "行程记录不可用：" + readDetail; return false; }
            if (record.TripId != tripId || record.CampaignGuid != campaignGuid || record.CampaignSlot != campaignSlot)
            { reason = "行程记录 trip/campaign 不一致"; return false; }
            if (!PalaceTripContext.RecordMatches(campaign, guid, slot, record, out string matchDetail))
            { reason = "行程记录与 campaign 不一致：" + matchDetail; return false; }

            int gameLand = SafeLand(sourceGame);
            if (record.Phase == PalaceTripContext.PhasePalaceSelected)
            {
                if (targetLand != LandIndex) { reason = "进入方向 target 必须为 12"; return false; }
                if (gameLand != record.SourceLand || gameLand == LandIndex)
                { reason = "进入方向 source 与记录不一致(" + gameLand + "/" + record.SourceLand + ")"; return false; }
            }
            else if (record.Phase == PalaceTripContext.PhaseReturnSelected)
            {
                if (gameLand != LandIndex) { reason = "回程方向 source 不是 land12(" + gameLand + ")"; return false; }
                if (targetLand != record.SourceLand)
                { reason = "回程方向 target 与记录不一致(" + targetLand + "/" + record.SourceLand + ")"; return false; }
            }
            else
            { reason = "行程记录 phase 未知：" + record.Phase; return false; }

            _handoff = new ReloadHandoffToken
            {
                TripId = tripId,
                Phase = record.Phase,
                TargetLand = targetLand,
                RecordSourceLand = record.SourceLand,
                CampaignGuid = guid,
                CampaignSlot = slot,
                Reign = reign,
                Biome = campaign.biomeIndex,
                Start = campaign.realStartDateTime,
                OldGame = gamePtr,
                OldManagers = managersPtr,
                Global = globalPtr,
                Campaign = campaignPtr,
                Holder = holderPtr,
            };
            reason = "handoff token 已武装：target=" + targetLand + " phase=" + record.Phase;
            Log("handoff token armed; " + IdentityLine(holderPtr, globalPtr, campaignPtr, gamePtr)
                + "; trip=" + tripId + " target=" + targetLand + " phase=" + record.Phase
                + " source=" + record.SourceLand + " guid=" + guid + " slot=" + slot);
            return true;
        }
        catch (Exception e)
        {
            reason = "begin handoff 异常：" + e.GetType().Name;
            Log("ERROR begin handoff fault: " + e.GetType().Name + " " + e.Message);
            return false;
        }
    }

    /// <summary>
    /// Root 在 travel 已核 actualPlaying 到达后调用：用同一 token 的目标/记录/identity/newGame/holder
    /// 复核；回程（target 0..11）把绑定更新为新的当前 owner，并在唯一 clone 仍同 holder 复用时直接
    /// 标记可再次 Prepare/CanEnter；目标 land12 的 owner 由 config 服务按同 token 采纳，若服务路径未
    /// 采纳则在此按同一复核补齐绑定。成功才销毁 token（只能完成一次）；任何不满足保持 token 与 clone
    /// 原样（不销毁、不重建、不二次分配）。
    /// </summary>
    internal static bool CompleteReloadHandoff(Game game, string tripId, out string reason)
    {
        reason = "";
        if (!Enabled) { reason = "land12 私有模式未启用"; return false; }
        try
        {
            ReloadHandoffToken token = _handoff;
            if (token == null) { reason = "无 active handoff token（未武装/已完成/已中止）"; return false; }
            if (string.IsNullOrEmpty(tripId) || token.TripId != tripId) { reason = "tripId 不一致"; return false; }
            if (game == null) { reason = "Game 为空"; return false; }
            Managers managers = Managers.Inst;
            if (managers == null || managers.game == null || managers.game.Pointer != game.Pointer)
            { reason = "不是当前 Managers.Game"; return false; }
            ulong gamePtr = PalacePrivateConfigFactory.PointerOf(game);
            if (gamePtr == 0 || gamePtr == token.OldGame) { reason = "仍是旧 Game 实例（Reload 未完成）"; return false; }
            if (!ExtensionIslandRuntime.TryScope(out CampaignSaveData campaign))
            { reason = "非私有 Greek 离线 owner scope"; return false; }
            BiomeHolder holder = BiomeHolder.Inst;
            if (holder == null || !IsCurrentHolder(holder)) { reason = "当前 holder 实例不可用"; return false; }
            if (holder.BiomeIndex != BiomeHolder.GreeceBiomeIndex) { reason = "当前 holder 非 Greek biome"; return false; }
            int land = SafeLand(game);
            if (campaign.CurrentLand != land || land != token.TargetLand)
            { reason = "到达岛不一致(campaign=" + campaign.CurrentLand + ", game=" + land + ", target=" + token.TargetLand + ")"; return false; }

            ulong globalPtr = PalacePrivateConfigFactory.PointerOf(GlobalSaveData.loaded);
            ulong campaignPtr = PalacePrivateConfigFactory.PointerOf(campaign);
            ulong holderPtr = PalacePrivateConfigFactory.PointerOf(holder);
            int reign = SafeReign(campaign);
            if (globalPtr == 0 || campaignPtr == 0 || holderPtr == 0 || reign < 0)
            { reason = "owner 指针不可读"; return false; }
            if (!TryVerifyHandoffOwner(token, gamePtr, holderPtr, campaign, out string guid, out int slot, out string verifyDetail))
            {
                reason = "交接复核失败：" + verifyDetail;
                Log("handoff complete refused: " + reason + "; " + IdentityLine(holderPtr, globalPtr, campaignPtr, gamePtr)
                    + "; trip=" + token.TripId);
                return false;
            }

            token.NewGame = gamePtr;
            if (globalPtr != _boundGlobal || campaignPtr != _boundCampaign
                || holderPtr != _boundHolder || reign != _boundReign || gamePtr != _boundGame)
            {
                UpdateBinding(globalPtr, campaignPtr, holderPtr, gamePtr, reign, guid, slot);
                Log("handoff owner adopted at arrival (config service path did not bind); "
                    + IdentityLine(holderPtr, globalPtr, campaignPtr, gamePtr) + "; trip=" + token.TripId);
            }
            if (token.TargetLand < LandIndex)
            {
                if (TryMarkPreparedFromReusableClone(globalPtr, campaignPtr, holderPtr, gamePtr, reign, guid, slot, out string prepDetail))
                {
                    // 新 epoch 必须刷新 sidecar 分类：clone reuse 早返回不得沿用 fresh 的 none 缓存。
                    PalaceDestinationAdmission.NoteDestinationPrepare(campaign);
                    Log("handoff complete (return); entry re-prepared for next visit; " + prepDetail + "; trip=" + token.TripId);
                }
                else
                    Log("handoff complete (return); entry NOT prepared (" + prepDetail
                        + "); CanEnter stays refused until a valid prepare; trip=" + token.TripId);
            }
            else
            {
                Log("handoff complete (land12 arrival); " + IdentityLine(holderPtr, globalPtr, campaignPtr, gamePtr)
                    + "; configAdopted=" + token.OwnerAdopted + "; trip=" + token.TripId);
            }
            _handoff = null;
            reason = "handoff 完成：target=" + token.TargetLand;
            return true;
        }
        catch (Exception e)
        {
            reason = "complete handoff 异常：" + e.GetType().Name;
            Log("ERROR complete handoff fault: " + e.GetType().Name + " " + e.Message);
            return false;
        }
    }

    /// <summary>
    /// Root 在 travel 判 fail 时调用：只停止本 token 的服务（清 active token），绝不销毁 clone、
    /// 不重置 cache/绑定、不做第二分配。tripId 不匹配时忽略并保留 token 供诊断。
    /// </summary>
    internal static void AbortReloadHandoff(string tripId)
    {
        try
        {
            ReloadHandoffToken token = _handoff;
            if (token == null)
            {
                Log("handoff abort ignored: no active token (trip=" + (tripId ?? "?") + ")");
                return;
            }
            if (string.IsNullOrEmpty(tripId) || token.TripId != tripId)
            {
                Log("handoff abort ignored: tripId mismatch (active=" + token.TripId + ", got=" + (tripId ?? "?") + ")");
                return;
            }
            _handoff = null;
            Log("handoff aborted (travel failed): service stopped; clone retained; "
                + IdentityLine(token.Holder, token.Global, token.Campaign, token.OldGame) + "; trip=" + token.TripId);
        }
        catch (Exception e)
        {
            Log("handoff abort fault: " + e.GetType().Name + " " + e.Message);
        }
    }

    // ------------------------------------------------------------------ 交接复核/绑定辅助

    /// <summary>
    /// 交接 token 的只读身份复核：新 Game 实例、same exact holder、durable guid/slot/reign、
    /// 单键 trip 记录的 trip/phase/source 与 campaign 一致性。任何未知/异常一律 false。
    /// </summary>
    private static bool TryVerifyHandoffOwner(ReloadHandoffToken token, ulong gamePtr, ulong holderPtr,
        CampaignSaveData campaign, out string guid, out int slot, out string reason, bool requireLiveSession = true)
    {
        guid = "";
        slot = -1;
        reason = "";
        if (token == null) { reason = "no-active-handoff-token"; return false; }
        if (gamePtr == 0 || gamePtr == token.OldGame) { reason = "no-new-game-instance"; return false; }
        if (token.NewGame != 0 && gamePtr != token.NewGame) { reason = "second-new-game-instance"; return false; }
        if (holderPtr != token.Holder)
        { reason = "holder-differs:" + Hex(token.Holder) + " -> " + Hex(holderPtr); return false; }
        string bindDetail;
        bool bound = requireLiveSession
            ? PalaceTripContext.TryBinding(GlobalSaveData.loaded, campaign, out guid, out slot, out bindDetail)
            : TryReadSavedCampaignBinding(GlobalSaveData.loaded, campaign, out guid, out slot, out bindDetail);
        if (!bound) { reason = "durable-binding:" + bindDetail; return false; }
        if (guid != token.CampaignGuid || slot != token.CampaignSlot || SafeReign(campaign) != token.Reign)
        { reason = "durable-identity-drift"; return false; }
        if (PalaceTripContext.Read(GlobalSaveData.loaded, out PalaceTripRecord record, out string readDetail)
            != PalaceTripContext.ReadStatus.Ok)
        { reason = "trip-record-unreadable:" + readDetail; return false; }
        if (record.TripId != token.TripId || record.Phase != token.Phase || record.SourceLand != token.RecordSourceLand
            || record.CampaignGuid != token.CampaignGuid || record.CampaignSlot != token.CampaignSlot)
        { reason = "trip-record-drift"; return false; }
        if (!PalaceTripContext.RecordMatches(campaign, guid, slot, record, out string matchDetail))
        { reason = "record-campaign-mismatch:" + matchDetail; return false; }
        return true;
    }

    /// <summary>
    /// active ReloadHandoffToken 的窄只读已验证事实快照（拷贝；调用方不得持有 token 本体，也不得据此写入）。
    /// 只供 <see cref="PalaceSaveBindingSource.TryAdoptReloadBoundary"/> 单次替换 runtime epoch 使用；
    /// 不新增平行 token/ledger。
    /// </summary>
    internal sealed class PalaceReloadHandoffFacts
    {
        internal string TripId = "";
        internal string Phase = "";
        internal int TargetLand = -1;
        internal int RecordSourceLand = -1;
        internal string CampaignGuid = "";
        internal int CampaignSlot = -1;
        internal int Reign = -1;
        internal int Biome = -1;
        internal int Start = -1;
        internal ulong OldGame, OldManagers, OldGlobal, OldCampaign, OldHolder, NewGame;
        internal bool OwnerAdopted;
    }

    /// <summary>
    /// 唯一 active token 的只读事实出口：只拷贝 token 已冻结记录的已验证事实（无 token / 仍是旧 Game /
    /// 已记录 NewGame 且不是同一对象 → 拒绝）。不验证记录/绑定、不改状态、不做 IO；语义与文件系统复核
    /// 由调用方（binder）按合同 B 自行完成。
    /// </summary>
    internal static bool TryDescribeVerifiedReloadHandoff(ulong gamePtr,
        out PalaceReloadHandoffFacts facts, out string reason)
    {
        facts = null;
        reason = "";
        if (!Enabled) { reason = "land12 私有模式未启用"; return false; }
        try
        {
            ReloadHandoffToken token = _handoff;
            if (token == null) { reason = "no-active-handoff-token"; return false; }
            if (gamePtr == 0) { reason = "game-pointer-unavailable"; return false; }
            if (gamePtr == token.OldGame) { reason = "no-new-game-instance"; return false; }
            if (token.NewGame != 0 && gamePtr != token.NewGame) { reason = "second-new-game-instance"; return false; }
            facts = new PalaceReloadHandoffFacts
            {
                TripId = token.TripId,
                Phase = token.Phase,
                TargetLand = token.TargetLand,
                RecordSourceLand = token.RecordSourceLand,
                CampaignGuid = token.CampaignGuid,
                CampaignSlot = token.CampaignSlot,
                Reign = token.Reign,
                Biome = token.Biome,
                Start = token.Start,
                OldGame = token.OldGame,
                OldManagers = token.OldManagers,
                OldGlobal = token.Global,
                OldCampaign = token.Campaign,
                OldHolder = token.Holder,
                NewGame = token.NewGame,
                OwnerAdopted = token.OwnerAdopted,
            };
            reason = "verified-token:" + token.TripId;
            return true;
        }
        catch (Exception e) { reason = "handoff-facts-fault:" + e.GetType().Name; facts = null; return false; }
    }

    /// <summary>
    /// config 服务遇新 G/C 的唯一采纳口：token 已核 + <c>target==12</c> 才更新绑定并复用唯一 clone。
    /// 其余（无 token / 非进入目标 / holder 变化 / 身份或记录漂移）返回 false，由调用方 fail-closed。
    /// </summary>
    private static bool TryAdoptHandoffOwner(ulong globalPtr, ulong campaignPtr, ulong holderPtr,
        ulong gamePtr, CampaignSaveData campaign, out string reason)
    {
        ReloadHandoffToken token = _handoff;
        if (token == null) { reason = "no-active-handoff-token"; return false; }
        if (token.TargetLand != LandIndex) { reason = "token-target-not-land12:" + token.TargetLand; return false; }
        if (!TryVerifyHandoffOwner(token, gamePtr, holderPtr, campaign, out string guid, out int slot, out reason,
            requireLiveSession: false)) return false; // GetConfig precedes HeavyShield world/land staging.

        // 合同 B 顺序：先把 binder 的 runtime epoch 单次换到新实例（失败即整体 fail-closed，不先改一半），
        // 再提交 config owner 更新；旧 epoch 证据以 token + 旧 binder 为准（见 TryAdoptReloadBoundary）。
        Game currentGame = CurrentGameIfPointer(gamePtr);
        if (currentGame == null) { reason = "game-instance-unavailable"; return false; }
        if (!PalaceSaveBindingSource.TryAdoptReloadBoundary(currentGame, out string bindReason))
        { reason = "binder-adopt:" + bindReason; return false; }

        if (!PalaceClockBootstrap.TryPrepareConfig(currentGame, out string clockPrepare))
        { reason = "clock-config-prepare:" + clockPrepare; return false; }
        UpdateBinding(globalPtr, campaignPtr, holderPtr, gamePtr, SafeReign(campaign), guid, slot);
        token.NewGame = gamePtr;
        token.OwnerAdopted = true;
        Log("handoff owner adopted by config service; " + IdentityLine(holderPtr, globalPtr, campaignPtr, gamePtr)
            + "; trip=" + token.TripId + " guid=" + guid + " slot=" + slot + " reign=" + token.Reign);
        reason = "adopted";
        return true;
    }

    // Loading has not staged a world yet. Read the existing durable GUID map through its owner codec;
    // never force HeavyShield session readiness or write identity here. Playing Complete uses the live binding.
    internal static bool TryReadSavedCampaignBinding(GlobalSaveData global, CampaignSaveData campaign,
        out string guid, out int slot, out string reason)
    {
        guid=""; slot=-1; reason="";
        try
        {
            if (global==null || campaign==null || global.Pointer!=GlobalSaveData.loaded.Pointer
                || CampaignSaveData.current==null || campaign.Pointer!=CampaignSaveData.current.Pointer
                || global.GetCurrentCampaign()==null || global.GetCurrentCampaign().Pointer!=campaign.Pointer)
            { reason="saved-map-current-owner"; return false; }
            IntPtr gptr=global.Pointer, cptr=campaign.Pointer;
            slot=global.currentCampaign;
            if (global.prefs==null || global.prefs.contents==null
                || !global.prefs.contents.TryGetValue(HeavyShieldSaveSchema.Key, out string raw)
                || !HeavyShieldSaveCodec.TryParse(raw, out HeavyShieldSaveDocument doc, out reason))
            { reason="saved-guid-map:"+reason; return false; }
            int matches=0;
            foreach(var row in doc.Campaigns)
                if(row.Slot==slot) { matches++; guid=row.Guid; }
            if(matches!=1 || string.IsNullOrEmpty(guid)) { reason="saved-guid-slot-not-unique"; return false; }
            if (GlobalSaveData.loaded==null || GlobalSaveData.loaded.Pointer!=gptr
                || GlobalSaveData.loaded.currentCampaign!=slot || CampaignSaveData.current==null
                || CampaignSaveData.current.Pointer!=cptr || GlobalSaveData.loaded.GetCurrentCampaign()==null
                || GlobalSaveData.loaded.GetCurrentCampaign().Pointer!=cptr)
            { reason="saved-guid-owner-changed"; return false; }
            return true;
        }
        catch(Exception e){ reason="saved-guid-read:"+e.GetType().Name; guid=""; slot=-1; return false; }
    }

    private static void UpdateBinding(ulong globalPtr, ulong campaignPtr, ulong holderPtr,
        ulong gamePtr, int reign, string guid, int slot)
    {
        _ownerBound = true;
        _boundGlobal = globalPtr;
        _boundCampaign = campaignPtr;
        _boundHolder = holderPtr;
        _boundGame = gamePtr;
        _boundReign = reign;
        _boundGuid = guid;
        _boundSlot = slot;
    }

    /// <summary>
    /// 只读复用准备：唯一 clone 仍属当前 exact holder（published 非空且无 sticky/pending 责任）时，
    /// 用当前 G/C/H/reign/game + durable 身份标记"已准备"。零分配、零写入 cache。
    /// </summary>
    private static bool TryMarkPreparedFromReusableClone(ulong globalPtr, ulong campaignPtr, ulong holderPtr,
        ulong gamePtr, int reign, string guid, int slot, out string detail)
    {
        if (!CloneReusableForHolder(holderPtr, out string reuseReason))
        { detail = "clone 不可复用：" + reuseReason; return false; }
        _entryPrepared = true;
        _prepGlobal = globalPtr;
        _prepCampaign = campaignPtr;
        _prepHolder = holderPtr;
        _prepGame = gamePtr;
        _prepReign = reign;
        _prepGuid = guid;
        _prepSlot = slot;
        detail = IdentityLine(holderPtr, globalPtr, campaignPtr, gamePtr) + "; guid=" + guid + " slot=" + slot;
        return true;
    }

    /// <summary>记录 owner 绑定（首次服务/prepare）。durable 身份读取失败只记录，供 return handoff 复核。</summary>
    private static void BindOwner(ulong globalPtr, ulong campaignPtr, ulong holderPtr, ulong gamePtr,
        int reign, CampaignSaveData campaign, string tag)
    {
        _boundGlobal = globalPtr;
        _boundCampaign = campaignPtr;
        _boundHolder = holderPtr;
        _boundGame = gamePtr;
        _boundReign = reign;
        _boundGuid = "";
        _boundSlot = -1;
        if (PalaceTripContext.TryBinding(GlobalSaveData.loaded, campaign, out string guid, out int slot, out string bindDetail))
        {
            _boundGuid = guid;
            _boundSlot = slot;
        }
        else
        {
            Log("WARN owner bound without durable campaign identity (" + bindDetail + "); handoff will verify from record");
        }
        _ownerBound = true;
        Log("owner bound (" + tag + "); " + IdentityLine(holderPtr, globalPtr, campaignPtr, gamePtr)
            + "; reign=" + reign + " guid=" + (_boundGuid.Length == 0 ? "?" : _boundGuid) + " slot=" + _boundSlot);
    }

    // ------------------------------------------------------------------ 唯一 clone 取用

    /// <summary>
    /// 唯一 clone 的取用口：sticky / pending 非 Empty / 已发布 clone 属于其他 holder 时一律拒绝，
    /// 不进入 cache（避免把瞬时状态升级为 sticky，也杜绝第二次 Instantiate）；其余交给现有 TryGetClone
    /// （同 exact holder 只读复用；无 published 的首次才走一次有界构建）。
    /// </summary>
    private static bool TryGetReusableClone(BiomeHolder holder, ulong holderPtr, out LevelConfig clone, out string report)
    {
        clone = null;
        if (ConfigCache.IsStickyFailed) { report = "failed-sticky:" + ConfigCache.FailureReason; return false; }
        if (ConfigCache.PendingState != PalacePrivateConfigCache.PendingKind.Empty)
        { report = "pending-responsibility-occupied:" + ConfigCache.PendingState; return false; }
        LevelConfig published = ConfigCache.PublishedClone;
        if (!ReferenceEquals(published, null) && ConfigCache.PublishedHolderPointer != holderPtr)
        {
            report = "different-holder:published=" + Hex(ConfigCache.PublishedHolderPointer)
                + " current=" + Hex(holderPtr) + "; no second allocation";
            return false;
        }
        if (!ReferenceEquals(published, null) && published == null)
        { report = "published Unity asset was destroyed; no reuse or reallocation"; return false; }
        return ConfigCache.TryGetClone(holder, out clone, out report);
    }

    /// <summary>
    /// 只读 clone 可用性（CanEnter/复用标记/复述用）：无 sticky、pending Empty、published 非空且
    /// 指针非零、published holder == 当前 exact holder。不校验 clone 字段、不读 GetCount、零写入。
    /// </summary>
    private static bool CloneReusableForHolder(ulong holderPtr, out string reason)
    {
        if (ConfigCache.IsStickyFailed) { reason = "failed-sticky:" + ConfigCache.FailureReason; return false; }
        if (ConfigCache.PendingState != PalacePrivateConfigCache.PendingKind.Empty)
        { reason = "pending-responsibility-occupied:" + ConfigCache.PendingState; return false; }
        LevelConfig published = ConfigCache.PublishedClone;
        if (ReferenceEquals(published, null) || published == null || ConfigCache.PublishedPointer == 0
            || PalacePrivateConfigFactory.PointerOf(published) != ConfigCache.PublishedPointer)
        { reason = "no-published-clone"; return false; }
        if (ConfigCache.PublishedHolderPointer != holderPtr)
        { reason = "different-holder:published=" + Hex(ConfigCache.PublishedHolderPointer) + " current=" + Hex(holderPtr); return false; }
        reason = "";
        return true;
    }

    // Reader only: the existing owner and single published config certify this request.
    internal static bool TryMatchPublishedGenerationConfig(LevelConfig config, out string reason)
    {
        reason = "";
        if (!Enabled || !Ready || !_ownerBound)
        { reason = "generation-owner-not-ready"; return false; }
        try
        {
            if (config == null || IslandSaveData.isSavingGame || NetworkBigBoss.IsOnline
                || !ExtensionIslandRuntime.TryScope(out CampaignSaveData campaign)
                || campaign.CurrentLand != LandIndex)
            { reason = "generation-request-scope"; return false; }
            Managers managers = Managers.Inst;
            BiomeHolder holder = BiomeHolder.Inst;
            if (managers == null || managers.game == null || !IsCurrentHolder(holder)
                || holder.BiomeIndex != BiomeHolder.GreeceBiomeIndex)
            { reason = "generation-current-game-holder"; return false; }
            ulong globalPtr = PalacePrivateConfigFactory.PointerOf(GlobalSaveData.loaded);
            ulong campaignPtr = PalacePrivateConfigFactory.PointerOf(campaign);
            ulong gamePtr = PalacePrivateConfigFactory.PointerOf(managers.game);
            ulong holderPtr = PalacePrivateConfigFactory.PointerOf(holder);
            if (globalPtr == 0 || campaignPtr == 0 || gamePtr == 0 || holderPtr == 0
                || globalPtr != _boundGlobal || campaignPtr != _boundCampaign
                || gamePtr != _boundGame || holderPtr != _boundHolder
                || SafeReign(campaign) != _boundReign)
            { reason = "generation-bound-owner-drift"; return false; }
            if (!CloneReusableForHolder(holderPtr, out reason)) return false;
            ulong configPtr = PalacePrivateConfigFactory.PointerOf(config);
            if (configPtr == 0 || configPtr != ConfigCache.PublishedPointer
                || configPtr != PalacePrivateConfigFactory.PointerOf(ConfigCache.PublishedClone))
            { reason = "generation-not-the-published-config"; return false; }
            if (!PalaceTripContext.TryBinding(GlobalSaveData.loaded, campaign,
                    out string guid, out int slot, out string bindingReason)
                || string.IsNullOrEmpty(guid) || guid != _boundGuid || slot != _boundSlot)
            { reason = "generation-binding:" + bindingReason; return false; }
            return true;
        }
        catch (Exception e)
        { reason = "generation-config-read:" + e.GetType().Name; return false; }
    }

    // ------------------------------------------------------------------ 生成前容器补齐（不变）

    /// <summary>
    /// land12 生成前的唯一写入点：确证同一当前 campaign/owner + CurrentLand==12 + 场景 land12 +
    /// 非保存中 + reign 整数稳定后，保证 currentReign.landData ≥13 槽：
    /// - 已有短容器：**原地** append default <c>CampaignSaveData.LandMapData</c>（0..11 不动）；
    /// - 容器为 null：本地建 13 default 槽新 List，复核身份后用最新 reign wrapper 赋引用并
    ///   <c>campaign.currentReign = reign</c> 整体写回；
    /// - 已 ≥13：零写入。
    /// 不写 previousReigns/visited/carry/coins/markers；记录真实旧 Count、新 Count、owner/reign。
    /// </summary>
    private static bool TryEnsureLand12Slots(CampaignSaveData campaign)
    {
        try
        {
            if (campaign == null) return false;
            if (!ExtensionIslandRuntime.TryScope(out CampaignSaveData scoped)
                || PalacePrivateConfigFactory.PointerOf(scoped) != PalacePrivateConfigFactory.PointerOf(campaign))
            {
                LogOnce("container-scope", "land12 landData gate: campaign/owner not proven");
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
                Log("ERROR land12 landData gate: currentReign wrapper unavailable; refusing to overwrite");
                return false;
            }
            ulong campaignPtr = PalacePrivateConfigFactory.PointerOf(campaign);
            ulong globalPtr = PalacePrivateConfigFactory.PointerOf(GlobalSaveData.loaded);
            ulong holderPtr = PalacePrivateConfigFactory.PointerOf(BiomeHolder.Inst);
            if (!FreshOwner(globalPtr, campaignPtr, holderPtr, reignIndex)) return false;
            Il2CppSystem.Collections.Generic.List<CampaignSaveData.LandMapData> landData = reign.landData;
            int oldCount = landData != null ? landData.Count : -1;
            if (landData != null && oldCount >= LandDataSlots)
            {
                LogOnce("slots-ok-" + oldCount, "land12 landData already " + oldCount + " slots (campaign="
                    + Hex(campaignPtr) + ", reign=" + reignIndex + "); no write");
                return true;
            }

            if (landData != null)
            {
                while (landData.Count < LandDataSlots)
                    landData.Add(new CampaignSaveData.LandMapData());
                Log("land12 landData padded in place: " + oldCount + " -> " + landData.Count
                    + " slots (campaign=" + Hex(campaignPtr) + ", reign=" + reignIndex + ")");
            }
            else
            {
                var fresh = new Il2CppSystem.Collections.Generic.List<CampaignSaveData.LandMapData>(LandDataSlots);
                for (int i = 0; i < LandDataSlots; i++)
                    fresh.Add(new CampaignSaveData.LandMapData());
                if (!FreshOwner(globalPtr, campaignPtr, holderPtr, reignIndex))
                {
                    Log("ERROR land12 landData gate: ownership changed while building; not published");
                    return false;
                }
                CampaignSaveData.ReignInfo latest = campaign.currentReign;
                if (latest == null || latest.landData != null) return false;
                latest.landData = fresh;
                campaign.currentReign = latest;   // 值类型包装：必须整体写回当前 campaign
                Log("land12 landData container created: " + oldCount + " -> " + LandDataSlots
                    + " slots (campaign=" + Hex(campaignPtr) + ", reign=" + reignIndex + ")");
            }

            // 终核：从 campaign 重新读回，确认同一 owner/reign 且槽位满足。
            if (!FreshOwner(globalPtr, campaignPtr, holderPtr, reignIndex))
                return false;
            CampaignSaveData.ReignInfo verified = campaign.currentReign;
            if (verified == null || verified.landData == null || verified.landData.Count < LandDataSlots)
            {
                Log("ERROR land12 landData gate: readback failed (slots="
                    + (verified != null && verified.landData != null ? verified.landData.Count : -1) + ")");
                return false;
            }
            Log("land12 landData readback ok: " + verified.landData.Count + " slots (campaign="
                + Hex(campaignPtr) + ", reign=" + reignIndex + ")");
            return true;
        }
        catch (Exception e)
        {
            Log("ERROR land12 landData gate fault: " + e.GetType().Name + " " + e.Message);
            return false;
        }
    }

    /// <summary>
    /// land12 保险门：仅确证 Greek 普通离线 owner + 当前 campaign 与场景都在 land12 时返回 true，
    /// 保护测试配置不意外胜利；其余（含 foreign/unknown owner）返回 false 原样执行。
    /// </summary>
    internal static bool ShouldBlockDefeatGreed(Game game)
    {
        if (!Enabled) return false;
        try
        {
            if (!ExtensionIslandRuntime.TryScope(out CampaignSaveData campaign)) return false;
            if (campaign.CurrentLand != LandIndex) return false;
            bool scene = game != null && game.currentLand == LandIndex;
            if (scene) LogOnce("defeat-blocked", "Game.DefeatGreed blocked on land12 (campaign+scene 12)");
            return scene;
        }
        catch (Exception e)
        {
            LogOnce("defeat-" + e.GetType().Name, "land12 defeat gate scope read failed: " + e.Message);
            return false;
        }
    }

    // ------------------------------------------------------------------ 身份/日志

    /// <summary>调用者必须是全局当前 holder 的同一实例（指针身份）。</summary>
    private static bool IsCurrentHolder(BiomeHolder holder)
    {
        try
        {
            if (holder == null) return false;
            BiomeHolder current = BiomeHolder.Inst;
            if (current == null) return false;
            ulong pointer = PalacePrivateConfigFactory.PointerOf(holder);
            return pointer != 0 && pointer == PalacePrivateConfigFactory.PointerOf(current);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 身份新鲜性（原约束：land12 + reign 一致）。generation/land12 服务路径使用；
    /// 交接后调用方传入的是交接后的当前 owner 快照，比较对象始终是同一套参数。
    /// </summary>
    private static bool FreshOwner(ulong globalPtr, ulong campaignPtr, ulong holderPtr, int reign)
    {
        if (!Enabled || globalPtr == 0 || campaignPtr == 0 || holderPtr == 0 || reign < 0) return false;
        if (!ExtensionIslandRuntime.TryScope(out CampaignSaveData current)) return false;
        return PalacePrivateConfigFactory.PointerOf(GlobalSaveData.loaded) == globalPtr
            && PalacePrivateConfigFactory.PointerOf(current) == campaignPtr
            && PalacePrivateConfigFactory.PointerOf(BiomeHolder.Inst) == holderPtr
            && current.CurrentLand == LandIndex && SafeReign(current) == reign;
    }

    /// <summary>入口准备用的身份新鲜性：同一 G/C/H/reign/game 且 campaign 仍在准备时的岛（不限 land12）。</summary>
    private static bool FreshEntryOwner(ulong globalPtr, ulong campaignPtr, ulong holderPtr, int reign,
        ulong gamePtr, int requiredLand)
    {
        if (!Enabled || globalPtr == 0 || campaignPtr == 0 || holderPtr == 0 || gamePtr == 0 || reign < 0) return false;
        if (!ExtensionIslandRuntime.TryScope(out CampaignSaveData current)) return false;
        Managers managers = Managers.Inst;
        return PalacePrivateConfigFactory.PointerOf(GlobalSaveData.loaded) == globalPtr
            && PalacePrivateConfigFactory.PointerOf(current) == campaignPtr
            && PalacePrivateConfigFactory.PointerOf(BiomeHolder.Inst) == holderPtr
            && PalacePrivateConfigFactory.PointerOf(managers != null ? managers.game : null) == gamePtr
            && current.CurrentLand == requiredLand && SafeReign(current) == reign;
    }

    private static ulong CurrentGamePointer()
    {
        try
        {
            Managers managers = Managers.Inst;
            return managers != null ? PalacePrivateConfigFactory.PointerOf(managers.game) : 0UL;
        }
        catch (Exception)
        {
            return 0UL;
        }
    }

    /// <summary>当前 Managers.game 且 pointer 恰为 gamePtr 时返回实例，否则 null（不猜、不 fallback）。</summary>
    private static Game CurrentGameIfPointer(ulong gamePtr)
    {
        try
        {
            Managers managers = Managers.Inst;
            Game game = managers != null ? managers.game : null;
            return game != null && PalacePrivateConfigFactory.PointerOf(game) == gamePtr ? game : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static int SafeLand(Game game)
    {
        try { return game != null ? game.currentLand : -1; }
        catch (Exception) { return -1; }
    }

    private static int SafeReign(CampaignSaveData campaign)
    {
        try { return campaign != null ? campaign.reign : -1; }
        catch (Exception) { return -1; }
    }

    private static string IdentityLine(ulong holderPtr, ulong globalPtr, ulong campaignPtr, ulong gamePtr)
    {
        return "currentH=" + Hex(holderPtr)
            + " publishedH=" + Hex(ConfigCache.PublishedHolderPointer)
            + " clone=" + Hex(ConfigCache.PublishedPointer)
            + " G=" + Hex(globalPtr) + " C=" + Hex(campaignPtr) + " Game=" + Hex(gamePtr)
            + " pending=" + ConfigCache.PendingState;
    }

    private static string Hex(ulong value) => PalacePrivateConfigFactory.Hex(value);

    private static void LogPrepare(string message)
    {
        if (message == _lastPrepareLog) return;
        _lastPrepareLog = message;
        Log(message);
    }

    private static void LogServe(string message, ulong holderPtr, ulong globalPtr, ulong campaignPtr, ulong gamePtr)
    {
        string line = message + "; " + IdentityLine(holderPtr, globalPtr, campaignPtr, gamePtr);
        if (line == _lastServeLog) return;
        _lastServeLog = line;
        Log(line);
    }

    private static void LogGate(string message)
    {
        if (message == _lastGateLog) return;
        _lastGateLog = message;
        Log(message);
    }

    private static void LogGate(string message, ulong holderPtr, ulong globalPtr, ulong campaignPtr, ulong gamePtr)
        => LogGate(message + "; " + IdentityLine(holderPtr, globalPtr, campaignPtr, gamePtr));

    private static void Log(string message)
    {
        try { PalaceVisibleEntry.Log("[PalaceLand12] " + message); }
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
}
