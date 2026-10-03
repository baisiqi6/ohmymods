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
    }

    internal sealed class PendingGrant
    {
        internal CrossWorldMountDefinition Definition;
        internal int Land;
        internal LevelBlock Template;
        internal CrossWorldMountGrantState Grant;
    }

    private static readonly List<Frame> Frames = new List<Frame>(2);

    private static GameObject _holder;
    private static readonly Dictionary<string, LevelBlock> Templates = new Dictionary<string, LevelBlock>(StringComparer.Ordinal);

    // ------------------------------------------------------------------ scope

    internal static Frame Open()
    {
        var frame = new Frame();
        try { Frames.Add(frame); }
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
            if (frame.Pending.Count == 0) return;

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
                if (TryWriteMarker(frame, grant.Definition, land))
                    Log("native marker written after placement verified: def=" + grant.Definition.Id + " land=" + land);
                else
                    LogError("native marker write failed after placement verified (def="
                        + grant.Definition.Id + " land=" + land + ")");
            }
            frame.Pending.Clear();
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
                    grant.Grant.NotePlaced(levelPointer);
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

        // land11：在捕获容器/owner 门之前建立当前确证 reign 的 12 槽容器
        // （FIRST-GENERATION-CONTRACT；独立于 SameOwner，含 landData==null 的值类型整体写回与
        // 短容器原地补齐）。其他 land 保持原逻辑。
        if (land == ExtensionIslandRuntime.LandIndex
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
            if (land == ExtensionIslandRuntime.LandIndex)
                LogOnce("landdata-null-" + land, "skip generation: campaign landData missing");
            return;
        }

        frame.Campaign = campaign;
        frame.ReignIndex = SafeReign(campaign);
        frame.LandDataPointer = CrossWorldMountDependencies.PointerOfList(landData);

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
            if (land == ExtensionIslandRuntime.LandIndex)
                LogOnce("landdata-short-" + land, "skip generation: campaign landData slots="
                    + landData.Count + " (need " + (land + 1) + "); padding did not establish the slot");
            return;
        }

        bool grantAllowed = GrantEnabled();
        bool distributed = land == ExtensionIslandRuntime.LandIndex;
        var insertions = new List<Insertion>(2);

        for (int index = 0; index < CrossWorldMountCatalog.Definitions.Length; index++)
        {
            CrossWorldMountDefinition definition = CrossWorldMountCatalog.Definitions[index];
            if (!TryReadIslandState(campaign, reign, landData, land, definition, out CrossWorldMountIslandState state))
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
            if (!CrossWorldMountPolicy.ShouldGrantDefinition(definition, land))
            {
                LogOnce("skip-land-" + definition.Id + "-" + land,
                    "skip def=" + definition.Id + " land=" + land
                    + " (new grants only on extension land " + ExtensionIslandRuntime.LandIndex + ")");
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
            if (!QueueInjection(frame, definition, land, result, insertions, granted: true, distributed, out int grantSeam)) continue;
            Log("grant+inject def=" + definition.Id + " land=" + land
                + (distributed ? " (seam assigned by catalog order)" : " seam=" + grantSeam)
                + " (marker pending placement)");
        }

        // 附加岛（land11）：全部待注入地块按固定目录顺序确定性分散到合法内部接缝；
        // 旧岛保留原 ChooseSeam（首 Clearing）接缝次序不漂移。
        if (distributed && insertions.Count > 0) AssignDistributedSeams(result.Count, insertions);

        Publish(ref result, insertions);
    }

    /// <summary>
    /// 附加岛的确定性接缝分配：<paramref name="insertions"/> 已按固定目录顺序登记，按
    /// <see cref="ExtensionIslandPlan.DistributeSeams"/> 均匀分散到普通地形列表的合法内部接缝
    /// [1, blockCount-1]（不跨首尾端点）。同 seed/同实际授予集重建一致，与付款/发现顺序无关；
    /// 不运行时 Random。
    /// </summary>
    private static void AssignDistributedSeams(int blockCount, List<Insertion> insertions)
    {
        int[] seams = ExtensionIslandPlan.DistributeSeams(blockCount - 1, insertions.Count);
        for (int i = 0; i < insertions.Count && i < seams.Length; i++)
        {
            Insertion item = insertions[i];
            item.Seam = seams[i];
            insertions[i] = item;
        }
        Log("land " + ExtensionIslandRuntime.LandIndex + " distributed seams: blockCount=" + blockCount
            + " definitions=" + insertions.Count + " seams=[" + string.Join(",", seams) + "]");
    }

    /// <summary>
    /// 新授予的活动资格（仅 GrantAndInject 分支调用）：<c>SeasonalChallengeId==0</c> 直接允许
    /// （普通获取定义）。非 0 时要求原生 <c>ChallengeHolder.Inst</c> 与其
    /// <c>ChallengeDataForID(id)</c> 有效、返回数据 <c>id</c> 等于该 id、<c>isSeasonalEvent</c>
    /// 为真，且 <c>SeasonalEventManager.IsEventActive(data)</c> 为真（actual 静态方法；正常模式
    /// 由服务器活动与 id 匹配决定，不比较本机日期，不设置 mock）。缺 holder/data、异常、非活动
    /// 一律 false；已授予/回执恢复、依赖注册与坐骑查询不经过本门。
    /// </summary>
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
            insertions.Add(new Insertion { Block = block, Seam = -1, Order = insertions.Count });
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
            insertions.Add(new Insertion { Block = block, Seam = seam, Order = insertions.Count });
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

    private static bool TryReadIslandState(
        CampaignSaveData campaign, CampaignSaveData.ReignInfo reign,
        Il2CppSystem.Collections.Generic.List<CampaignSaveData.LandMapData> landData, int land,
        CrossWorldMountDefinition definition, out CrossWorldMountIslandState state)
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

            state = new CrossWorldMountIslandState(
                landValid: true,
                markerOnIsland: markerOnIsland,
                markerInCampaign: markerInCampaign,
                islandVisited: visited,
                receiptOnIsland: IslandHasReceipt(campaign, land, definition),
                lastPlayedDays: playedDays,
                grantedDefinitionCount: CountIslandDefinitions(spawns));
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

    /// <summary>岛内存档里是否已有本定义获取设施的原生持久化回执（查询覆盖的已授予兜底共用）。</summary>
    internal static bool IslandHasReceipt(CampaignSaveData campaign, int land, CrossWorldMountDefinition definition)
    {
        try
        {
            IslandSaveData island = campaign.GetIsland(land);
            if (island == null) return false;
            Il2CppSystem.Collections.Generic.List<IslandSaveData.ObjectData> objects = island.objects;
            if (objects == null) return false;
            int count = objects.Count;
            for (int i = 0; i < count; i++)
            {
                IslandSaveData.ObjectData record = objects[i];
                if (record == null) continue;
                if (definition.ReceiptMatches(record.prefabPath)
                    || definition.ReceiptMatches(record.name)
                    || definition.ReceiptMatches(record.hierarchyPath))
                    return true;
            }
        }
        catch (Exception e)
        {
            LogOnce("receipt-" + definition.Id + "-" + e.GetType().Name, "island receipt scan failed: " + e.Message);
        }
        return false;
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
    /// owner 复核（稳定身份，写入前终检）：
    /// 1) current 与捕获的 Campaign 同一实例（指针）；
    /// 2) <c>current.reign</c> 整数与捕获一致（切王朝即失效）；
    /// 3) 当前 Reign 的 landData 容器指针与捕获一致（换容器即失效；**不用** boxed ReignInfo 指针——
    ///    get_currentReign 每次 il2cpp_value_box 新包装）；
    /// 4) Global 当前选择证明：<c>GetCurrentCampaign()</c> 非空且与 current 同一实例——
    ///    菜单/无选中（currentCampaign==-1）时原生 current 会回退 GetCampaign(0)，此时拒绝写入。
    /// </summary>
    private static bool SameOwner(Frame frame)
    {
        try
        {
            if (frame == null || frame.Campaign == null || frame.ReignIndex < 0 || frame.LandDataPointer == 0UL)
                return false;
            CampaignSaveData current = CampaignSaveData.current;
            if (current == null) return false;
            if (current.Pointer != frame.Campaign.Pointer) return false;
            if (current.reign != frame.ReignIndex) return false;
            CampaignSaveData.ReignInfo reign = current.currentReign;
            if (reign == null || reign.landData == null) return false;
            if (CrossWorldMountDependencies.PointerOfList(reign.landData) != frame.LandDataPointer) return false;
            GlobalSaveData global = GlobalSaveData.loaded;
            if (global == null) return false;
            CampaignSaveData owner = global.GetCurrentCampaign();
            if (owner == null) return false;
            return PointerOf(owner) == PointerOf(current);
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

    private static int[] ToIntArray(Il2CppStructArray<SteedType> source)
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
