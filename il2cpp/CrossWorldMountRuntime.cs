using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 跨世界坐骑运行时（共用主干，issue-98）。
///
/// 单一注入点：<c>LevelLayout.GetBlocks</c> 后缀，在某座"确定的希腊新生成岛"的布局列表里插入
/// 定义表登记的原生获取地块实例（<see cref="CrossWorldMountCatalog"/>）。地块本体/祭坛/SteedSpawn
/// 全部是原生资产，因此购买、解锁、坐骑生成、技能、换骑、出海、保存/读档均走原生链；本补丁不改写
/// 价格/技能/CanPay，不做逐帧扫描；技能同步池经 <see cref="CrossWorldMountDependencies"/> 沿定义
/// 闭包接入（原生池定义 + 现有可靠注册路径）。
///
/// 主链发布（审查必改）：同一 frame 的全部注入先累计、后**一次性发布最终列表**；所有待提交项都
/// 对应发布列表中的真实实例。依赖未就绪的定义不得进入授予（重建不受限）。
///
/// 事务绑定（审查必改）：每次 <c>Level.GenerateInternal</c> 建立自己的 frame（owner=
/// campaign/reign/land、目标 Level、消费与提交状态），GetBlocks/CloneInto/关闭全部只作用于本 frame：
/// - 标记只在"本次注入实例被 CloneInto 进**本次目标 Level**"时提交；
/// - 未观察到放置 → 不写标记（fail-closed，不用名称扫描冒充成功）；
/// - 嵌套生成/内层异常只终止自己，不清外层；
/// - 写标记用 frame 捕获的 reign（若 campaign/reign 已切换则放弃并记日志）。
///
/// 开关语义：<c>CrossWorldMountsEnabled</c> 只控制**新授予**；已确认授予的岛（native 标记或
/// 岛内存档回执）在任何开关状态下都继续补回地块，保证既有布局不丢块、位置不漂移。
/// 联机/挑战岛整体跳过（单侧生成会布局分叉）。
/// </summary>
internal static class CrossWorldMountRuntime
{
    private const string LogPrefix = "[CrossWorldMount]";
    private const string HolderName = "KEM_CrossWorldMountBlocks";

    // 稳定顺序契约：本 postfix 固定在所有 postfix 之后运行（Priority.Last），因此地图倍率
    // (MapWidthScope) 的基线永远不含本地块；本地块是倍率规划完成后的纯追加（见 REPORT.md §顺序）。
    private static readonly HashSet<string> LoggedKeys = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// 一次 GenerateInternal 的事务 frame（owner/目标 Level/本 frame 的授予）。
    /// owner 身份不用 <c>ReignInfo</c> 包装指针：ReignInfo 是值类型，<c>get_currentReign</c>
    /// 每次 <c>il2cpp_value_box</c> 产生新包装（见 review/probes/interop/run.log），
    /// 改用稳定三元组：Campaign 指针 + <c>campaign.reign</c> 整数 + 该 Reign 的 landData 容器指针。
    /// </summary>
    internal sealed class Frame
    {
        internal readonly List<PendingGrant> Pending = new List<PendingGrant>(2);
        internal CampaignSaveData Campaign;          // owner 快照（GetBlocks 时捕获）
        internal int ReignIndex = -1;                // campaign.reign 快照（-1 = 读取失败，提交拒绝）
        internal ulong LandDataPointer;              // 该 Reign 的 landData 容器指针快照
        internal bool Consumed;
        internal bool Closed;
        internal ulong LevelPointer;
        // issue-200 fresh 观察票据（private proposal）：Open(Level, LevelConfig, seed) 在 GetBlocks 前
        // 捕获的目标身份 + 单次转交的候选票据（null = Unknown，newgrant 关闭）。
        internal ulong TargetLevelPointer;
        internal LevelConfig Config;
        internal ulong ConfigPointer;
        internal int Seed;
        internal MountIslandFrameTicket FreshTicket;
        internal int Land = -1;                      // 本 frame 的目标 land（Apply 捕获；commit batch 使用）
    }

    internal sealed class PendingGrant
    {
        internal CrossWorldMountDefinition Definition;
        internal int Land;
        internal LevelBlock Template;
        internal CrossWorldMountGrantState Grant;
        internal bool Authorized;   // true = 本次授予由 fresh 票据门放行（commit 前须 binding 复核）
    }

    private static readonly List<Frame> Frames = new List<Frame>(2);

    private static GameObject _holder;
    private static readonly Dictionary<string, LevelBlock> Templates = new Dictionary<string, LevelBlock>(StringComparer.Ordinal);

    // ------------------------------------------------------------------ scope

    /// <summary>
    /// 旧兼容入口（无可信目标身份）：不认领 fresh 票据，frame 恒 Unknown，批准前语义不变。
    /// </summary>
    internal static Frame Open()
    {
        return Open(null, null, 0);
    }

    /// <summary>
    /// root 统一 Prefix（__instance/__0/__1）入口：在 GetBlocks **之前**捕获目标 Level/config/seed
    /// 并单次转交 fresh 观察票据（<see cref="MountIslandFreshObservation.ClaimForFrame"/>）。
    /// config 为空（旧兼容路径）时不认领票据。
    /// </summary>
    internal static Frame Open(Level target, LevelConfig config, int seed)
    {
        var frame = new Frame();
        try
        {
            frame.TargetLevelPointer = PointerOf(target);
            frame.Config = config;
            frame.ConfigPointer = PointerOf(config);
            frame.Seed = seed;
            if (config != null)
                frame.FreshTicket = MountIslandFreshObservation.ClaimForFrame(frame);
            Frames.Add(frame);
        }
        catch (Exception e) { LogError("open scope: " + e.Message); }
        return frame;
    }

    /// <summary>GenerateInternal 正常返回：结算本 frame 的待提交授予（只提交进入本次 Level 的实例）。</summary>
    internal static void Close(Frame frame, Level level)
    {
        try
        {
            Detach(frame);
            if (frame == null || frame.Closed) return;
            frame.Closed = true;
            frame.LevelPointer = PointerOf(level);
            if (frame.Pending.Count == 0)
            {
                MountIslandFreshObservation.OnFrameClosed(frame);
                return;
            }

            // Pass 1：placement 结果（不写 marker；own marker 写入必须晚于 batch fresh 复核）。
            var placed = new List<PendingGrant>(frame.Pending.Count);
            for (int i = 0; i < frame.Pending.Count; i++)
            {
                PendingGrant grant = frame.Pending[i];
                CrossWorldMountGrantOutcome outcome = grant.Grant.Complete(frame.LevelPointer, out int land);
                if (outcome != CrossWorldMountGrantOutcome.CommitMarker)
                {
                    LogError("grant aborted: placed block not observed in target level (def="
                        + grant.Definition.Id + " land=" + grant.Land + "); native marker NOT written");
                    continue;
                }
                if (land != grant.Land)
                {
                    LogError("grant aborted: placement reported wrong land (def=" + grant.Definition.Id
                        + " expected=" + grant.Land + " actual=" + land + "); native marker NOT written");
                    continue;
                }
                placed.Add(grant);
            }

            // Pass 2：**一次** batch fresh 复核（此刻 island history 尚未包含本帧 own marker；外部新增
            // 非 own marker 会在此阻止整批 authorized 提交）。
            bool batchVerified = MountIslandFreshObservation.BeginCommitBatch(frame, out string batchReason);
            var session = new MountIslandCommitSession(batchVerified, batchReason);

            // Pass 3：逐条 binding（owner/level/config/slot 指针，**不读 island history**）+ 写 own expected marker。
            for (int i = 0; i < placed.Count; i++)
            {
                PendingGrant grant = placed[i];
                var batchGrant = new MountIslandBatchGrant
                {
                    DefinitionId = grant.Definition.Id,
                    Land = grant.Land,
                    SteedTypeId = grant.Definition.SteedTypeId,
                    Authorized = grant.Authorized,
                    Placed = true,
                };
                bool bindingOk = false;
                string bindingReason = "not authorized";
                if (grant.Authorized)
                    bindingOk = MountIslandFreshObservation.IsCommitBindingAuthorized(frame, grant.Land, out bindingReason);
                if (!session.PlanGrant(batchGrant, bindingOk, frame.Land, out MountIslandMarkerWrite write))
                {
                    grant.Grant.Abort();
                    LogError("grant aborted without marker (def=" + grant.Definition.Id + " land=" + grant.Land
                        + "): batchVerified=" + batchVerified + " binding=" + bindingOk
                        + " batchReason=" + batchReason + " bindingReason=" + bindingReason + "; native marker NOT written");
                    continue;
                }
                if (TryWriteMarker(frame, grant.Definition, write.Land))
                    Log("native marker written after placement verified: def=" + grant.Definition.Id + " land=" + write.Land);
                else
                    LogError("native marker write failed after placement verified (def="
                        + grant.Definition.Id + " land=" + write.Land + ")");
            }

            // Pass 4：批量后 owner/land 终核（只记录；已写入的 marker 不回溯撤销）。
            if (!MountIslandFreshObservation.EndCommitBatch(frame, out string finalReason))
                LogError("owner/land changed during marker batch: " + finalReason);

            frame.Pending.Clear();
            MountIslandFreshObservation.OnFrameClosed(frame);
        }
        catch (Exception e) { LogError("close scope: " + e.Message); }
    }

    /// <summary>GenerateInternal 抛异常：只终止本 frame（正常完成的内层 frame 不受影响）。</summary>
    internal static void Abort(Frame frame)
    {
        try
        {
            Detach(frame);
            if (frame == null || frame.Closed) return;
            frame.Closed = true;
            for (int i = 0; i < frame.Pending.Count; i++)
            {
                PendingGrant grant = frame.Pending[i];
                grant.Grant.Abort();
                LogError("generation threw; grant aborted without marker (def="
                    + grant.Definition.Id + " land=" + grant.Land + ")");
            }
            frame.Pending.Clear();
            MountIslandFreshObservation.OnFrameClosed(frame);
        }
        catch (Exception e) { LogError("abort scope: " + e.Message); }
    }

    private static void Detach(Frame frame)
    {
        for (int i = Frames.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(Frames[i], frame)) { Frames.RemoveAt(i); return; }
        }
    }

    private static Frame CurrentFrame()
    {
        return Frames.Count > 0 ? Frames[Frames.Count - 1] : null;
    }

    // ------------------------------------------------------------------ entry

    /// <summary>LevelLayout.GetBlocks 后缀入口（唯一注入点）。</summary>
    internal static void TryApply(ref Il2CppSystem.Collections.Generic.List<LevelBlock> result)
    {
        try
        {
            Frame frame = CurrentFrame();
            if (frame == null || frame.Consumed || frame.Closed) return;
            frame.Consumed = true;
            Apply(frame, ref result);
        }
        catch (Exception e)
        {
            LogError("apply failed: " + e.GetType().Name + " " + e.Message);
        }
    }

    /// <summary>
    /// 原生 <c>LevelBlock.CloneInto(Level, float)</c> 后缀：本 frame 注入的实例真正被克隆进
    /// 某个 Level 时记录该 Level 指针；提交时要求与本次生成的目标 Level 一致。
    /// </summary>
    internal static void NoteBlockPlaced(LevelBlock block, Level level)
    {
        try
        {
            if (block == null || level == null) return;
            Frame frame = CurrentFrame();
            if (frame == null || frame.Pending.Count == 0) return;
            ulong levelPointer = PointerOf(level);
            for (int i = 0; i < frame.Pending.Count; i++)
            {
                PendingGrant grant = frame.Pending[i];
                LevelBlock template = grant.Template;
                if (template != null && block.Pointer == template.Pointer)
                {
                    if (grant.Authorized && !MountIslandFreshObservation.IsPlacementAuthorized(frame, level, out string recheckReason))
                    {
                        LogError("placement not recorded: fresh ticket invalidated (def=" + grant.Definition.Id
                            + " land=" + grant.Land + "): " + recheckReason);
                        continue;
                    }
                    grant.Grant.NotePlaced(levelPointer);
                }
            }
        }
        catch (Exception) { }
    }

    private static ulong PointerOf(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase value)
    {
        try { return value != null ? (ulong)value.Pointer.ToInt64() : 0UL; }
        catch (Exception) { return 0UL; }
    }

    internal static bool GrantEnabled()
    {
        try
        {
            return ModConfig.Enabled != null && ModConfig.Enabled.Value
                && ModConfig.CrossWorldMountsEnabled != null && ModConfig.CrossWorldMountsEnabled.Value;
        }
        catch (Exception) { return false; }
    }

    private static bool BaseEnabled()
    {
        try { return ModConfig.Enabled != null && ModConfig.Enabled.Value; }
        catch (Exception) { return false; }
    }

    private static bool InGenerationContext()
    {
        try
        {
            BiomeHolder biome = BiomeHolder.Inst;
            if (biome == null || biome.BiomeIndex != BiomeHolder.GreeceBiomeIndex) return false;
            if (NetworkBigBoss.IsOnline) return false;
            GlobalSaveData global = GlobalSaveData.loaded;
            if (global != null && global.InChallenge) return false;
            if (IslandSaveData.isSavingGame) return false;
            return true;
        }
        catch (Exception e)
        {
            LogOnce("context-" + e.GetType().Name, "context read failed: " + e.Message);
            return false;
        }
    }

    /// <summary>
    /// 单次布局的注入累计：所有定义先各自解析模板/接缝，最后一次性构造并发布**唯一**的最终列表
    /// （修复早期"按值改局部 result"导致列表从未真正发布的问题）。待提交项与发布列表一一对应：
    /// 只有成功进入发布列表的定义才建立 PendingGrant；异常中断发生在发布前时，本帧全部放弃，
    /// 不留下"已待提交但未发布"的半状态。
    /// </summary>
    private struct Insertion
    {
        internal LevelBlock Block;
        internal int Seam;
        internal int Order;
        /// <summary>true = 本项属于本帧 newgrant 计划（发布前终核失效时仅撤销这些项）。</summary>
        internal bool Granted;
    }

    private static void Apply(Frame frame, ref Il2CppSystem.Collections.Generic.List<LevelBlock> result)
    {
        if (!BaseEnabled()) return;
        if (result == null || result.Count < 2)
        {
            LogOnce("skip-shape", "skip: block list too small");
            return;
        }
        if (!InGenerationContext()) return;

        // 希腊世界的外来 SteedType 查询/恢复 + 技能同步池接入（幂等；缺项在初始化点重试）。
        CrossWorldMountDependencies.EnsureAll();

        // owner 快照：后续写标记只认这一份 reign/campaign，campaign 切换即放弃。
        CampaignSaveData campaign;
        try { campaign = CampaignSaveData.current; } catch (Exception) { return; }
        if (campaign == null) return;
        CampaignSaveData.ReignInfo reign;
        int land;
        try
        {
            Managers managers = Managers.Inst;
            land = managers != null && managers.game != null ? managers.game.currentLand : campaign.currentLand;
            reign = campaign.currentReign;
        }
        catch (Exception e)
        {
            LogOnce("owner-" + e.GetType().Name, "owner read failed: " + e.Message);
            return;
        }
        if (reign == null || land < 0) return;

        // 扩展岛（11/13）：在捕获容器/owner 门之前建立当前确证 reign 的 14 槽容器
        // （FIRST-GENERATION-CONTRACT；独立于 SameOwner，含 landData==null 的值类型整体写回与
        // 短容器原地补齐）。其他 land 保持原逻辑。
        if (MountIslandSplitPolicy.IsExtensionLand(land)
            && !ExtensionIslandRuntime.TryEnsureExtensionLandDataSlots(campaign, land))
        {
            LogOnce("landdata-ensure-" + land,
                "skip generation: extension landData container not established (fail-closed)");
            return;
        }

        // 容器门可能刚写回新引用：从当前 campaign 重新取最新 reign wrapper/landData。
        CampaignSaveData.ReignInfo currentReign = campaign.currentReign;
        if (currentReign == null) return;
        Il2CppSystem.Collections.Generic.List<CampaignSaveData.LandMapData> landData = currentReign.landData;
        if (landData == null)
        {
            if (MountIslandSplitPolicy.IsExtensionLand(land))
                LogOnce("landdata-null-" + land, "skip generation: campaign landData missing");
            return;
        }

        frame.Campaign = campaign;
        frame.ReignIndex = SafeReign(campaign);
        frame.LandDataPointer = CrossWorldMountDependencies.PointerOfList(landData);
        frame.Land = land;

        // new-grant 权威快照：本战役全岛 receipt（raw _islands 只读，不 GetIsland）+ 当前 reign marker。
        // 快照失败/身份不符 = campaign 证据 unknown：本岛已证的 InjectOnly 恢复照常，newgrant 关闭。
        MountIslandGrantAuthority.Snapshot authority = null;
        if (MountIslandGrantAuthority.TryCapture(campaign, currentReign, landData, out authority)
            && authority != null
            && !authority.MatchesOwner(campaign, frame.ReignIndex, frame.LandDataPointer))
        {
            LogOnce("authority-owner-" + land, "campaign grant snapshot owner mismatch; new grants closed this pass");
            authority = null;
        }

        // 审查补充：同一 owner 门从发布前闭合——身份未确证（无准确 Global owner / reign 或
        // landData 不可读）时不进入任何注入或 receipt 修补，原 result 保持且 Pending 空，
        // 不会留下"注入了块但 Close 拒 marker"的无标记布局；提交前 SameOwner 终检仍保留
        // （覆盖捕获后到 Close 之间 owner 变化的场景）。
        if (!SameOwner(frame))
        {
            LogOnce("early-owner-" + land,
                "skip generation: owner identity not proven (fail-closed, no blocks published)");
            return;
        }

        if (land >= landData.Count)
        {
            if (MountIslandSplitPolicy.IsExtensionLand(land))
                LogOnce("landdata-short-" + land, "skip generation: campaign landData slots="
                    + landData.Count + " (need " + (land + 1) + "); padding did not establish the slot");
            return;
        }

        bool grantAllowed = GrantEnabled();
        bool distributed = MountIslandSplitPolicy.IsExtensionLand(land);   // A11/B13 共用同一分散接缝规则
        bool freshChecked = false;       // fresh 票据门按需（首个 GrantAndInject）判定一次
        bool freshGrant = false;
        string freshReason = null;
        var insertions = new List<Insertion>(2);

        for (int index = 0; index < CrossWorldMountCatalog.Definitions.Length; index++)
        {
            CrossWorldMountDefinition definition = CrossWorldMountCatalog.Definitions[index];
            if (!TryReadIslandState(campaign, reign, landData, land, definition, authority, out CrossWorldMountIslandState state))
            {
                LogOnce("skip-state-" + definition.Id, "skip def=" + definition.Id + ": island state unreadable (fail-closed)");
                continue;
            }

            CrossWorldMountDecision decision = CrossWorldMountPolicy.Decide(state);
            if (decision == CrossWorldMountDecision.InjectOnly)
            {
                // 开关只阻止"新分配"；已授予的岛继续履行重建责任（布局块不丢失）。
                if (QueueInjection(frame, definition, land, result, insertions, granted: false, distributed, out int seam))
                {
                    LogOnce("rebuild-" + definition.Id + "-" + land,
                        "inject(rebuild) def=" + definition.Id + " land=" + land
                        + (distributed ? " (seam assigned by catalog order)" : " seam=" + seam));
                }
                if (state.ReceiptOnIsland && !state.MarkerOnIsland)
                    RestoreMarkerFromReceipt(frame, definition, land);
                continue;
            }
            if (decision != CrossWorldMountDecision.GrantAndInject) continue;
            if (!grantAllowed)
            {
                LogOnce("skip-gate-" + definition.Id + "-" + land,
                    "skip def=" + definition.Id + " land=" + land + " (feature OFF: no new grants)");
                continue;
            }
            // 双岛 membership（仅 new-grant 过滤；InjectOnly/season/deps 语义不变）：稳定 Id → A11/B13，
            // 未知 Id 一律不授予（不按数值/顺序猜岛；norselands.wolf 的 SteedType=13 与 land13 同值也由 Id 决定）。
            int targetLand = MountIslandSplitPolicy.GetTargetLand(definition.Id);
            if (targetLand != land)
            {
                LogOnce("skip-land-" + definition.Id + "-" + land,
                    "skip def=" + definition.Id + " land=" + land
                    + " (new-grant target=" + (targetLand < 0 ? "unknown" : targetLand.ToString()) + ")");
                continue;
            }
            // 活动资格（只作用于真正的新授予）：缺 holder/data、id 不符、非季节活动、异常或活动
            // 非激活一律不新授予；InjectOnly 重建/已付款恢复/依赖注册/查询不经过本门。
            if (!SeasonalGrantAllowed(definition))
            {
                LogOnce("skip-season-" + definition.Id + "-" + land,
                    "skip def=" + definition.Id + " land=" + land + " (seasonal grant not active: challenge "
                    + definition.SeasonalChallengeId + ")");
                continue;
            }
            // 依赖未就绪（查询映射/技能池缺失）不得进入授予；重建路径不受此门影响。
            if (!CrossWorldMountDependencies.IsDefinitionReady(definition))
            {
                LogOnce("skip-deps-" + definition.Id + "-" + land,
                    "skip def=" + definition.Id + " land=" + land + " (dependencies not ready: "
                    + CrossWorldMountDependencies.FirstNotReadyReason() + ")");
                continue;
            }
            // fresh 观察票据门（A11/B13 新授予统一门，替代原常 Unknown 的 native 资格门占位）：
            // 只有本 frame 持有匹配的 native 缺记录候选票据才放行；未知/过期/身份漂移一律 closed。
            // InjectOnly 重建路径不受此门影响（原位顺序不变）。
            if (!freshChecked)
            {
                freshChecked = true;
                freshGrant = MountIslandFreshObservation.IsGrantAuthorized(frame, land, out freshReason);
            }
            if (!freshGrant)
            {
                LogOnce("skip-fresh-" + definition.Id + "-" + land,
                    "skip def=" + definition.Id + " land=" + land + " (fresh ticket gate closed: " + freshReason + ")");
                continue;
            }
            if (!QueueInjection(frame, definition, land, result, insertions, granted: true, distributed, out int grantSeam)) continue;
            frame.Pending[frame.Pending.Count - 1].Authorized = true;
            Log("grant+inject def=" + definition.Id + " land=" + land
                + (distributed ? " (seam assigned by catalog order)" : " seam=" + grantSeam)
                + " (marker pending placement)");
        }

        // R3：newgrant 真正发布前**一次**终核（authority 事实 + raw owner；finite 重核，不逐定义扫描）。
        // 失效 → 撤销本帧全部 newgrant 计划/clones 与 Pending，仅按已证 InjectOnly 项重建最终 result
        // （不 already-queued 就发布；无剩余项时原样保留调用方列表指针）。
        bool authorityCurrent = authority != null && authority.MatchesCurrent(campaign);
        bool ownerCurrent = SameOwner(frame);
        if (!MountIslandGrantFacts.AllowsGrantPublish(authorityCurrent, ownerCurrent)
            && HasGrantPlans(insertions))
        {
            RevokeGrantPlans(frame, insertions,
                authorityCurrent ? "raw owner changed before publish" : "authority facts changed before publish");
        }

        // 扩展岛（A11/B13）：全部待注入地块按固定目录顺序确定性分散到合法内部接缝；
        // 旧岛保留原 ChooseSeam（首 Clearing）接缝次序不漂移；恢复集按实际授予集 count/order 不重排。
        if (distributed && insertions.Count > 0) AssignDistributedSeams(land, result.Count, insertions);

        Publish(ref result, insertions);
    }

    /// <summary>
    /// 附加岛的确定性接缝分配：<paramref name="insertions"/> 已按固定目录顺序登记，按
    /// <see cref="ExtensionIslandPlan.DistributeSeams"/> 均匀分散到普通地形列表的合法内部接缝
    /// [1, blockCount-1]（不跨首尾端点）。同 seed/同实际授予集重建一致，与付款/发现顺序无关；
    /// 不运行时 Random。
    /// </summary>
    private static void AssignDistributedSeams(int land, int blockCount, List<Insertion> insertions)
    {
        int[] seams = ExtensionIslandPlan.DistributeSeams(blockCount - 1, insertions.Count);
        for (int i = 0; i < insertions.Count && i < seams.Length; i++)
        {
            Insertion item = insertions[i];
            item.Seam = seams[i];
            insertions[i] = item;
        }
        Log("land " + land + " distributed seams: blockCount=" + blockCount
            + " definitions=" + insertions.Count + " seams=[" + string.Join(",", seams) + "]");
    }

    /// <summary>
    /// 新授予活动资格（仅 GrantAndInject/首可用资格评估调用）：<c>SeasonalChallengeId==0</c> 直接允许
    /// （普通获取定义）。非 0 时要求原生 <c>ChallengeHolder.Inst</c> 与其
    /// <c>ChallengeDataForID(id)</c> 有效、返回数据 <c>id</c> 等于该 id、<c>isSeasonalEvent</c>
    /// 为真，且 <c>SeasonalEventManager.IsEventActive(data)</c> 为真（actual 静态方法；正常模式
    /// 由服务器活动与 id 匹配决定，不比较本机日期，不设置 mock）。缺 holder/data、异常、非活动
    /// 一律 false；已授予/回执恢复、依赖注册与坐骑查询不经过本门。
    /// （availability 侧首可用资格评估复用同一门，不复制第二套活动判定。）
    /// </summary>
    internal static bool IsNewGrantSeasonEligible(CrossWorldMountDefinition definition)
        => SeasonalGrantAllowed(definition);

    private static bool SeasonalGrantAllowed(CrossWorldMountDefinition definition)
    {
        try
        {
            if (definition.SeasonalChallengeId <= 0) return true;
            ChallengeHolder holder = ChallengeHolder.Inst;
            if (holder == null) return false;
            ChallengeData data = holder.ChallengeDataForID(definition.SeasonalChallengeId);
            if (data == null) return false;
            if (data.id != definition.SeasonalChallengeId) return false;
            if (!data.isSeasonalEvent) return false;
            return SeasonalEventManager.IsEventActive(data);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 解析模板与接缝并登记累计（不动传入列表、不写标记之外的状态）；模板/接缝失败即拒绝本定义。
    /// granted=true 时同时建立 PendingGrant（其模板就是最终发布列表中的那一个实例）。
    /// <paramref name="distributed"/>（附加岛 land11）：登记时不定接缝，由
    /// <see cref="AssignDistributedSeams"/> 在全部定义判定完成后按固定目录顺序统一分配；
    /// 旧岛（false）沿用原 ChooseSeam 首 Clearing 次序。
    /// </summary>
    private static bool QueueInjection(Frame frame, CrossWorldMountDefinition definition, int land,
        Il2CppSystem.Collections.Generic.List<LevelBlock> result, List<Insertion> insertions,
        bool granted, bool distributed, out int seam)
    {
        seam = -1;
        if (!TryGetTemplate(definition, out LevelBlock block)) return false;

        int count = result.Count;
        if (distributed)
        {
            insertions.Add(new Insertion { Block = block, Seam = -1, Order = insertions.Count, Granted = granted });
        }
        else
        {
            seam = ChooseSeam(result, count);
            if (seam <= 0 || seam >= count)
            {
                LogOnce("skip-seam-" + definition.Id + "-" + count, "skip def=" + definition.Id + ": no valid seam (count=" + count + ")");
                seam = -1;
                return false;
            }
            insertions.Add(new Insertion { Block = block, Seam = seam, Order = insertions.Count, Granted = granted });
        }

        if (granted)
        {
            var pending = new PendingGrant
            {
                Definition = definition,
                Land = land,
                Template = block,
            };
            pending.Grant.Begin(land);
            frame.Pending.Add(pending);
        }
        return true;
    }

    /// <summary>
    /// 唯一发布点：按接缝（同接缝按定义顺序）构造最终列表并写回。无任何累计时原样保留调用方列表。
    /// </summary>
    private static void Publish(ref Il2CppSystem.Collections.Generic.List<LevelBlock> result, List<Insertion> insertions)
    {
        if (insertions == null || insertions.Count == 0) return;

        // 接缝排序（同级按登记顺序），保证单遍重建稳定。
        insertions.Sort(static (a, b) => a.Seam != b.Seam ? a.Seam.CompareTo(b.Seam) : a.Order.CompareTo(b.Order));

        int count = result.Count;
        var published = new Il2CppSystem.Collections.Generic.List<LevelBlock>(count + insertions.Count);
        int cursor = 0;
        for (int j = 0; j < count; j++)
        {
            while (cursor < insertions.Count && insertions[cursor].Seam == j)
            {
                published.Add(insertions[cursor].Block);
                cursor++;
            }
            published.Add(result[j]);
        }
        // 防御：接缝只可能落在 [1, count-1]，但任何残留都按登记顺序追加，绝不丢块。
        while (cursor < insertions.Count)
        {
            published.Add(insertions[cursor].Block);
            cursor++;
        }
        result = published;
    }

    /// <summary>
    /// 发布前终核失效：只撤销本帧 newgrant 项（待发布 clones/计划）与对应 Pending（Abort，不写标记），
    /// 保留已证 InjectOnly 项；result 在 Publish 时按剩余项重建（无剩余则原样保留调用方列表指针）。
    /// </summary>
    private static void RevokeGrantPlans(Frame frame, List<Insertion> insertions, string reason)
    {
        int removed = 0;
        for (int i = insertions.Count - 1; i >= 0; i--)
        {
            if (!insertions[i].Granted) continue;
            insertions.RemoveAt(i);
            removed++;
        }
        int aborted = frame.Pending.Count;
        for (int i = 0; i < frame.Pending.Count; i++) frame.Pending[i].Grant.Abort();
        frame.Pending.Clear();
        LogError("newgrant plans revoked before publish (" + reason + "): removed=" + removed
            + " queued clones, aborted=" + aborted + " pending; publishing InjectOnly-only result");
    }

    private static bool HasGrantPlans(List<Insertion> insertions)
    {
        for (int i = 0; i < insertions.Count; i++)
        {
            if (insertions[i].Granted) return true;
        }
        return false;
    }

    private static void RestoreMarkerFromReceipt(Frame frame, CrossWorldMountDefinition definition, int land)
    {
        // 岛内存档已有原生获取设施回执（真实证据）：按同一 owner 补写标记（自愈，非预测）。
        if (TryWriteMarker(frame, definition, land))
            Log("native marker restored from island receipt: def=" + definition.Id + " land=" + land);
        else
            LogError("native marker restore failed (def=" + definition.Id + " land=" + land + ")");
    }

    /// <summary>合法接缝：首块与末块之间，优先首个 Clearing 之后，其次 Clearing Small/Forest，再次倒数第二。</summary>
    private static int ChooseSeam(Il2CppSystem.Collections.Generic.List<LevelBlock> blocks, int count)
    {
        int fallback = -1;
        for (int i = 0; i < count - 1; i++)
        {
            LevelBlock b = blocks[i];
            if (b == null || b.gameObject == null) continue;
            string name = b.name;
            if (name == "Clearing_Blocks") return i + 1;
            if ((name == "Clearing Small_Blocks" || name == "Forest_Blocks") && fallback < 0) fallback = i + 1;
        }
        if (fallback > 0) return fallback;
        return count - 1;
    }

    // ------------------------------------------------------------------ template

    private static bool TryGetTemplate(CrossWorldMountDefinition definition, out LevelBlock block)
    {
        block = null;
        if (Templates.TryGetValue(definition.Id, out LevelBlock cached) && cached != null)
        {
            block = cached;
            return true;
        }

        try
        {
            GameObject prefab = Resources.Load<GameObject>(definition.BlockResourcePath);
            if (prefab == null)
            {
                LogOnce("tmpl-missing-" + definition.Id, "template resource missing: " + definition.BlockResourcePath);
                return false;
            }

            // 身份校验必须在任何改名之前（原 prefab 实例名 == 定义名）。
            if (!string.Equals(prefab.name, definition.BlockObjectName, StringComparison.Ordinal))
            {
                LogOnce("tmpl-name-" + definition.Id, "template identity mismatch (resource name): " + prefab.name
                    + " != " + definition.BlockObjectName);
                return false;
            }

            if (_holder == null)
            {
                _holder = new GameObject(HolderName);
                UnityEngine.Object.DontDestroyOnLoad(_holder);
                _holder.SetActive(false);
            }

            GameObject instance = UnityEngine.Object.Instantiate(prefab, _holder.transform);
            if (instance == null)
            {
                LogOnce("tmpl-inst-" + definition.Id, "template instantiate failed");
                return false;
            }
            instance.SetActive(false);

            LevelBlock candidate = instance.GetComponent<LevelBlock>();
            if (candidate == null)
            {
                LogOnce("tmpl-block-" + definition.Id, "template has no LevelBlock");
                return false;
            }

            // 私有实例身份仍按定义校验（group 来自真实组件）；此后不再依赖名字。
            if (candidate.groupOne != (LevelBlockGroup)definition.GroupOne
                || candidate.groupTwo != (LevelBlockGroup)definition.GroupTwo)
            {
                LogOnce("tmpl-groups-" + definition.Id, "template identity mismatch (groups): g1=" + candidate.groupOne + " g2=" + candidate.groupTwo);
                return false;
            }

            try { candidate.ComputeDimensions(); } catch (Exception e) { LogOnce("dims-" + e.GetType().Name, "ComputeDimensions: " + e.Message); }
            try { candidate.BuildIndex(); } catch (Exception e) { LogOnce("index-" + e.GetType().Name, "BuildIndex: " + e.Message); }
            if (candidate.GetWidth() <= 0)
            {
                LogOnce("tmpl-width-" + definition.Id, "template width invalid: " + candidate.GetWidth());
                return false;
            }

            Templates[definition.Id] = candidate;
            Log("template ready: def=" + definition.Id + " width=" + SafeWidth(candidate));
            block = candidate;
            return true;
        }
        catch (Exception e)
        {
            LogOnce("tmpl-load-" + definition.Id, "template load failed: " + e.GetType().Name + " " + e.Message);
            return false;
        }
    }

    private static int SafeWidth(LevelBlock block)
    {
        try { return block != null ? block.GetWidth() : -1; }
        catch (Exception) { return -1; }
    }

    // ------------------------------------------------------------------ island state (native save data)

    /// <summary>
    /// 每定义的本岛/本战役信号读取。receipt 走 raw _islands 槽只读（不 GetIsland）：
    /// 读取异常 → state 不可读（fail-closed，跳过本定义）；null slot/无 objects = 无 record（阴性证据）。
    /// campaignEvidenceReadable=false 表示 campaign 级快照缺失/不可读 → Decide 拒新授予但不影响本岛恢复。
    /// </summary>
    private static bool TryReadIslandState(
        CampaignSaveData campaign, CampaignSaveData.ReignInfo reign,
        Il2CppSystem.Collections.Generic.List<CampaignSaveData.LandMapData> landData, int land,
        CrossWorldMountDefinition definition, MountIslandGrantAuthority.Snapshot authority,
        out CrossWorldMountIslandState state)
    {
        state = default;
        try
        {
            CampaignSaveData.LandMapData entry = landData[land];
            if (entry == null) return false;
            int[] spawns = ToIntArray(entry.steedSpawns);
            bool markerOnIsland = CrossWorldMountPolicy.Contains(spawns, definition.SteedTypeId);

            bool markerInCampaign = false;
            int landCount = landData.Count;
            for (int i = 0; i < landCount; i++)
            {
                CampaignSaveData.LandMapData other = landData[i];
                if (other == null) continue;
                if (CrossWorldMountPolicy.Contains(ToIntArray(other.steedSpawns), definition.SteedTypeId))
                {
                    markerInCampaign = true;
                    break;
                }
            }

            bool visited = false;
            Il2CppSystem.Collections.Generic.List<int> visitedIslands = campaign.visitedIslands;
            if (visitedIslands != null) visited = visitedIslands.Contains(land);

            double playedDays = entry.lastPlayedTimeDays;
            if (double.IsNaN(playedDays) || double.IsInfinity(playedDays) || playedDays < 0d) playedDays = double.MaxValue;

            bool receiptReadable = MountIslandGrantAuthority.TryReadIslandReceipt(
                campaign, land, definition, out bool receiptOnIsland);
            bool receiptInCampaign = authority != null && authority.ReceiptInCampaign(definition);

            state = new CrossWorldMountIslandState(
                landValid: true,
                markerOnIsland: markerOnIsland,
                markerInCampaign: markerInCampaign,
                islandVisited: visited,
                receiptOnIsland: receiptOnIsland,
                lastPlayedDays: playedDays,
                grantedDefinitionCount: CountIslandDefinitions(spawns),
                receiptInCampaign: receiptInCampaign,
                campaignEvidenceReadable: receiptReadable && authority != null);
            return true;
        }
        catch (Exception e)
        {
            LogOnce("state-" + definition.Id + "-" + e.GetType().Name, "island state read failed: " + e.Message);
            return false;
        }
    }

    private static int CountIslandDefinitions(int[] steedSpawns)
    {
        if (steedSpawns == null) return 0;
        int count = 0;
        for (int i = 0; i < CrossWorldMountCatalog.Definitions.Length; i++)
        {
            if (CrossWorldMountPolicy.Contains(steedSpawns, CrossWorldMountCatalog.Definitions[i].SteedTypeId)) count++;
        }
        return count;
    }

    /// <summary>
    /// 岛内存档里是否已有本定义获取设施的原生持久化回执（查询覆盖的已授予兜底共用）。
    /// 现为 raw <c>_islands</c> 槽只读（不调用 GetIsland：其懒补建/IO 副作用未核）；
    /// null slot/无 objects = 无回执；读取异常按 unknown 记录并按缺失处理（查询兜底的既有语义）。
    /// </summary>
    internal static bool IslandHasReceipt(CampaignSaveData campaign, int land, CrossWorldMountDefinition definition)
    {
        try
        {
            if (MountIslandGrantAuthority.TryReadIslandReceipt(campaign, land, definition, out bool found))
                return found;
            LogOnce("receipt-slot-" + definition.Id + "-" + land,
                "island receipt slot read failed (unknown; query fallback treats as absent)");
            return false;
        }
        catch (Exception e)
        {
            LogOnce("receipt-" + definition.Id + "-" + e.GetType().Name, "island receipt scan failed: " + e.Message);
            return false;
        }
    }

    /// <summary>
    /// 把定义坐骑值写入 owner 当前的 landData.steedSpawns：稳定身份终检通过后直接读
    /// <c>CampaignSaveData.current.currentReign.landData</c>（与捕获的容器指针一致），
    /// 保留 List 值类型 entry 回写（interop 值类型元素读取为副本）。
    /// </summary>
    private static bool TryWriteMarker(Frame frame, CrossWorldMountDefinition definition, int land)
    {
        try
        {
            if (frame == null) return false;
            if (!SameOwner(frame)) return false;
            CampaignSaveData current = CampaignSaveData.current;
            CampaignSaveData.ReignInfo reign = current != null ? current.currentReign : null;
            if (reign == null) return false;
            Il2CppSystem.Collections.Generic.List<CampaignSaveData.LandMapData> landData = reign.landData;
            if (landData == null || land < 0 || land >= landData.Count) return false;

            CampaignSaveData.LandMapData entry = landData[land];
            int[] next = CrossWorldMountPolicy.AppendMarker(ToIntArray(entry.steedSpawns), definition.SteedTypeId);
            var typed = new SteedType[next.Length];
            for (int i = 0; i < next.Length; i++) typed[i] = (SteedType)next[i];
            entry.steedSpawns = typed;   // Il2CppStructArray<SteedType> 隐式转换
            landData[land] = entry;      // List<T> 值类型元素：必须回写（interop 值类型读为副本）
            CrossWorldMountDependencies.InvalidateGrantCache();   // 查询覆盖的 receipt 兜底缓存立即失效
            return true;
        }
        catch (Exception e)
        {
            LogError("marker write failed: " + e.GetType().Name + " " + e.Message);
            return false;
        }
    }

    /// <summary>
    /// owner 复核（稳定身份，写入/发布前终检；root R2 转用 raw owner helper，不用 GetCurrentCampaign fallback）：
    /// 1) raw selected slot（Global.loaded / currentCampaign / campaigns[index] 与 CampaignSaveData.current
    ///    同一实例）== 捕获的 Campaign；
    /// 2) <c>current.reign</c> 整数与捕获一致（切王朝即失效）；
    /// 3) 当前 Reign 的 landData 容器指针与捕获一致（换容器即失效）。
    /// </summary>
    private static bool SameOwner(Frame frame)
    {
        try
        {
            if (frame == null || frame.Campaign == null || frame.ReignIndex < 0 || frame.LandDataPointer == 0UL)
                return false;
            if (!ExtensionIslandRuntime.TryResolveRawOwner(out _, out _, out CampaignSaveData owner))
                return false;
            if (owner == null || PointerOf(owner) != PointerOf(frame.Campaign)) return false;
            CampaignSaveData current = CampaignSaveData.current;
            if (current == null) return false;
            if (current.Pointer != frame.Campaign.Pointer) return false;
            if (current.reign != frame.ReignIndex) return false;
            CampaignSaveData.ReignInfo reign = current.currentReign;
            if (reign == null || reign.landData == null) return false;
            if (CrossWorldMountDependencies.PointerOfList(reign.landData) != frame.LandDataPointer) return false;
            return true;
        }
        catch (Exception e)
        {
            LogOnce("owner-verify-" + e.GetType().Name, "owner verify failed: " + e.Message);
            return false;
        }
    }

    /// <summary>读 <c>campaign.reign</c>（失败返回 -1；提交侧据此拒绝写标记）。</summary>
    private static int SafeReign(CampaignSaveData campaign)
    {
        try { return campaign != null ? campaign.reign : -1; }
        catch (Exception) { return -1; }
    }

    /// <summary>landData.steedSpawns → int[]（marker 判定共用；interop 值类型数组读取）。</summary>
    internal static int[] ToIntArray(Il2CppStructArray<SteedType> source)
    {
        if (source == null) return Array.Empty<int>();
        int count = source.Length;
        var result = new int[count];
        for (int i = 0; i < count; i++) result[i] = (int)source[i];
        return result;
    }

    // ------------------------------------------------------------------ logging

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
