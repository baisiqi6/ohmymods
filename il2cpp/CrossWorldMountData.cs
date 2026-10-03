using System;
using System.Collections.Generic;

namespace KingdomEnhancedMod;

/// <summary>
/// 跨世界坐骑：共用主干的数据与判定（纯逻辑，不引用 UnityEngine/interop，可离线单测）。
///
/// 结构（用户 2026-10-02 确认"一套共用接入 + 小型定义表"）：
/// - <see cref="CrossWorldMountCatalog"/>：按"获取场景"登记的原生定义数据（稳定 ID、原生
///   block/prefab 路径与对象名、LevelBlock 分组、祭坛回执关键字）。新增坐骑只加数据行；
///   价格/能力/持久化/联网全部继续由原组件执行，这里不复制魔法数字、不重写技能。
/// - <see cref="CrossWorldMountPolicy"/>：岛级判定（每定义独立），老岛不追插。
/// - <see cref="CrossWorldMountGrantState"/>：一次生成的授予事务（owner/Level 由 Runtime 绑定）。
/// - <see cref="MountMapPlan"/> + <see cref="MountMapLayoutPlan"/>：同源地图条目与"全部可达"布局。
/// </summary>
internal enum CrossWorldMountDecision
{
    /// <summary>不动关卡：未知状态、该定义已在他岛授予、或该岛并非新生成岛。</summary>
    Skip = 0,
    /// <summary>该岛已记录本定义（reign.landData 标记或岛内存档回执）：每次生成都补回地块。</summary>
    InjectOnly = 1,
    /// <summary>新生成岛且本战役该定义尚未授予：注入地块并在放置证实后写 native 标记。</summary>
    GrantAndInject = 2,
}

/// <summary>
/// 同一获取链的进阶/伴随 SteedType（原生保存可记录为这些类型）。定义行的 P1 形态与
/// 这些别名共享一条获取链，查询/恢复必须一并注册，否则升级后的存档读档会解析失败。
/// 路径来自 <c>SteedX.GetPrefabPath</c> 原生字面量（worker/out/steed-prefab-literals.txt）。
/// </summary>
internal readonly struct CrossWorldMountSteedAlias
{
    internal readonly int SteedTypeId;
    internal readonly string PrefabPath;

    internal CrossWorldMountSteedAlias(int steedTypeId, string prefabPath)
    {
        SteedTypeId = steedTypeId;
        PrefabPath = prefabPath;
    }
}

/// <summary>
/// 一条"跨世界坐骑获取场景"定义（数据行）。所有字段都是原生资源/元数据的直接引用，
/// 不做推断：路径与对象名来自离线解码，groupOne/Two 来自真实 LevelBlock 组件。
/// </summary>
internal sealed class CrossWorldMountDefinition
{
    /// <summary>稳定定义 ID（日志/回执/测试用；不落存档）。</summary>
    internal readonly string Id;
    /// <summary>原生 <c>SteedType</c> 值（landData.steedSpawns 写入用的唯一数值）。</summary>
    internal readonly int SteedTypeId;
    /// <summary>原生获取地块资源路径（Resources.Load，小写形状）。</summary>
    internal readonly string BlockResourcePath;
    /// <summary>原生地块 GameObject 名（LevelBlock 所在对象，身份校验用）。</summary>
    internal readonly string BlockObjectName;
    /// <summary>原生 LevelBlock 分组（身份校验用）。</summary>
    internal readonly int GroupOne;
    internal readonly int GroupTwo;
    /// <summary>原生祭坛/clone 对象名（岛内存档回执与生成后核实用）。</summary>
    internal readonly string ReceiptObjectName;
    /// <summary>原生 SteedSpawn 设施名（operator-spawn-catalog 实测；证明地块内获取点身份）。</summary>
    internal readonly string FacilityName;
    /// <summary>原生坐骑 prefab 资源路径（SteedX.GetPrefabPath 字面量；跨 biome 依赖注册用）。</summary>
    internal readonly string SteedPrefabPath;
    /// <summary>同一获取链的进阶类型（P2 等）：读档恢复需要一并注册；无则为空数组。</summary>
    internal readonly CrossWorldMountSteedAlias[] Aliases;
    /// <summary>
    /// 原生 <c>BiomeObjectPools</c> 集合名（技能同步池的来源世界，如 "norselands"）：
    /// 该定义的坐骑技能引用的 Spit 预制体只在这些集合里有原生同步池定义。空 = 无需补池。
    /// </summary>
    internal readonly string PoolCollection;
    /// <summary>
    /// legacy 岛槽位元数据（历史"固定分布表"残留）：issue-98 新契约后不再门控授予
    /// （新授予只在附加岛 land11，见 <see cref="CrossWorldMountPolicy.ShouldGrantDefinition"/>）；
    /// 保留字段只为与旧定义行/文档对照，不参与任何判定。
    /// </summary>
    internal readonly int IslandSlot;
    /// <summary>
    /// 变体覆盖（Kirin/Wolf 两例）：该 SteedType 在希腊原生查询表 <c>objectSteedTypePairs</c>
    /// 中**已存在指向别的世界变体的精确映射**（type6=Unicorn、type13=GreekWolf P1），
    /// 本定义不得写入共享表，也不能把原生"不同实例"当冲突拒绝；就绪判定只看本 prefab 资源
    /// 自身（只读解析），查询/生成适配统一在"已授予 scope"内经控制点做结果替换。
    /// </summary>
    internal readonly bool VariantOverride;
    /// <summary>
    /// 新授予的活动资格（0 = 普通，无活动门）：非 0 时只在"当前原生活动挑战 id 等于本值
    /// 且活动处于激活态"时允许**新授予**（GrantAndInject）。已经授予的岛（marker/设施回执）
    /// 走 InjectOnly 重建、已付款/已拥有恢复、依赖注册与查询均不经过本门。
    /// </summary>
    internal readonly int SeasonalChallengeId;

    internal CrossWorldMountDefinition(
        string id, int steedTypeId, string blockResourcePath, string blockObjectName,
        int groupOne, int groupTwo, string receiptObjectName, string facilityName,
        string steedPrefabPath, int islandSlot,
        CrossWorldMountSteedAlias[] aliases = null, string poolCollection = null,
        bool variantOverride = false, int seasonalChallengeId = 0)
    {
        FacilityName = facilityName;
        SteedPrefabPath = steedPrefabPath;
        Aliases = aliases ?? Array.Empty<CrossWorldMountSteedAlias>();
        PoolCollection = poolCollection;
        IslandSlot = islandSlot;
        VariantOverride = variantOverride;
        SeasonalChallengeId = seasonalChallengeId;
        Id = id;
        SteedTypeId = steedTypeId;
        BlockResourcePath = blockResourcePath;
        BlockObjectName = blockObjectName;
        GroupOne = groupOne;
        GroupTwo = groupTwo;
        ReceiptObjectName = receiptObjectName;
    }

    /// <summary>
    /// 存档文本（prefabPath/name/hierarchyPath）是否精确指向本定义的获取设施：
    /// 取最后一段路径、去掉 Unity "(Clone)" 后缀后按序数大小写不敏感全等比较——
    /// 不用 substring（避免 trade_routes 变体/同名后缀误判成归属证据）。
    /// </summary>
    internal bool ReceiptMatches(string value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        int slash = value.LastIndexOf('/');
        string leaf = slash >= 0 ? value.Substring(slash + 1) : value;
        const string cloneSuffix = "(Clone)";
        if (leaf.EndsWith(cloneSuffix, StringComparison.Ordinal)) leaf = leaf.Substring(0, leaf.Length - cloneSuffix.Length);
        return string.Equals(leaf.Trim(), ReceiptObjectName, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// 定义表。只登记"有完整原生获取场景证据"的定义：地块路径/对象名/分组来自 operator 只读实测
/// （operator-block-catalog.json），SteedSpawn 设施与 SteedType 来自真实引用解出
/// （runtime-r4/evidence-block-facilities.json），prefab 路径来自 <c>SteedX.GetPrefabPath</c> 字面量。
/// 新增坐骑只加数据行；技能/池依赖由 <c>CrossWorldMountDependencies</c> 沿定义闭包自动解析。
/// </summary>
internal static class CrossWorldMountCatalog
{
    /// <summary>
    /// 已登记定义（全部来自 operator 只读实测：block 路径/GO/分组来自 operator-block-catalog.json，
    /// SteedSpawn 设施与 SteedType 来自 operator-spawn-catalog.json，prefab 路径来自
    /// SteedX.GetPrefabPath 字面量与 steed-resource-paths.json 的实测资源名）。
    /// 岛槽位是显式固定分布表（与玩家访问顺序无关）。
    /// 共 16 条：14 条走"缺失键登记"（写入希腊共享表），2 条为变体覆盖
    /// （norselands.wolf type13/17、bamboo.kirin type6 —— 希腊原生表已有同类型别的世界变体，
    /// 只做已授予 scope 内的查询/生成结果适配，不写共享表、不覆盖原生值）。
    /// 新授予活动门：仅 anniversary.rainbowpony 携带 SeasonalChallengeId=10（原生 Anniversary 活动
    /// 激活才允许新授予，见 <c>CrossWorldMountRuntime.SeasonalGrantAllowed</c>）；其余 15 条为普通
    /// 新授予。santahouse.reindeer 是用户 2026-10-02 授权的新增 Greek 普通获取点，不代表当前 native
    /// 已证无条件活动入口。
    /// </summary>
    internal static readonly CrossWorldMountDefinition[] Definitions =
    {
        // 北欧（Norselands）：设施→类型由 runtime-r4/evidence-block-facilities.json 真实引用解出。
        Def("norselands.gullinbursti", 21, "prefabs/blockprefabs/norselands/steed gullingursti_norselands_blocks",
            "Steed Gullingursti_Norselands_Blocks", 76, 35, "BoarAltar_norselands", "Prefabs/Steeds/Gullinbursti", 2),
        Def("norselands.sleipnir", 22, "prefabs/blockprefabs/norselands/steed sleipnir_norselands_blocks",
            "Steed Sleipnir_Norselands_Blocks", 77, 35, "SleipnirTree_norselands", "Prefabs/Steeds/Sleipnir", 3,
            poolCollection: "norselands"),
        Def("norselands.reindeer", 23, "prefabs/blockprefabs/norselands/steed reindeer_norselands_blocks",
            "Steed Reindeer_Norselands_Blocks", 83, 35, "Reindeer_area_norselands", "Prefabs/Steeds/Reindeer_Norselands", 4,
            aliases: new[] { Alias(28, "Prefabs/Steeds/Reindeer_norselands P2") }),
        // 地块名为 Freya，引用解出的设施是 CatCartArch_norselands（即猫车 type24），不是 Freya 坐骑。
        Def("norselands.catcart", 24, "prefabs/blockprefabs/norselands/steed freya_norselands_blocks",
            "Steed Freya_Norselands_Blocks", 79, 35, "CatCartArch_norselands", "Prefabs/Steeds/Catcart_norselands", 5),
        Def("norselands.kelpie", 25, "prefabs/blockprefabs/norselands/steed kelpie_norselands_blocks",
            "Steed Kelpie_Norselands_Blocks", 80, 35, "KelpiePool_norselands", "Prefabs/Steeds/Kelpie", 6,
            aliases: new[] { Alias(27, "Prefabs/Steeds/Kelpie P2") }, poolCollection: "norselands"),
        Def("norselands.hrimfaxe", 26, "prefabs/blockprefabs/norselands/steed hrimfaxe_norselands_blocks",
            "Steed Hrimfaxe_Norselands_Blocks", 78, 35, "Horse_DayNight_area_norselands", "Prefabs/Steeds/Horse DayNight_norselands", 2),
        // 普通北欧第六岛狼（Norselands_Land_SIX group81 min1/max1 → WolfRock_norselands → Steed112476/
        // type13，3宝石+8币由原组件执行；证据 wolf-factcheck/evidence-summary.json）。
        // 与 Dire/Greek 的 type13 变体（112477，仅 WolfAttack）不同：本 prefab 带 BuffUnits97747
        // （10秒 DamageInvulnerability、冷却30、手动攻击触发、局部 x±5/y0..3 Citizens/Tools），
        // 全部原样保留。希腊表 type13/type17 已有原生映射 → 变体覆盖，不写共享表。
        // 旧资源 "Wolf Norselands P1"(95552) 不是当前普通北欧第六岛所用，不登记。
        Def("norselands.wolf", 13, "prefabs/blockprefabs/norselands/steed wolf_norselands_blocks",
            "Steed Wolf_Norselands_Blocks", 81, 35, "WolfRock_norselands", "Prefabs/Steeds/Wolf_norselands", 6,
            aliases: new[] { Alias(17, "Prefabs/Steeds/Wolf Norselands P2") }, variantOverride: true),
        // 其他世界（希腊 BiomeSpecificAssets 无这些类型）。
        Def("woodlands.beetle", 14, "prefabs/blockprefabs/steed beetle_blocks",
            "Steed Beetle_Blocks", 64, 35, "BeetleHouse", "Prefabs/Steeds/Beetle", 3,
            poolCollection: "deadlands"),
        Def("woodlands.golem", 15, "prefabs/blockprefabs/steed golem_blocks",
            "Steed Golem_Blocks", 65, 35, "GolemHouse", "Prefabs/Steeds/Golem", 4,
            poolCollection: "deadlands"),
        Def("woodlands.gamigin", 16, "prefabs/blockprefabs/steed gamigin_blocks",
            "Steed Gamigin_Blocks", 66, 35, "GamiginHouse", "Prefabs/Steeds/Gamigin", 5),
        // 三条路径修正为原生 GetPrefabPath 字面量（原 Swamp Egg/Mansion/Warhorse 资源不存在，
        // 见 astra-review/runtime-r4/report.md §2 与 operator-verification/steed-resource-paths.json）。
        Def("swamp.eggsteed", 2, "prefabs/blockprefabs/steed swamp_blocks",
            "Steed Swamp_Blocks", 48, 35, "Egg", "Prefabs/Steeds/Lizard", 6),
        Def("woodlands.mansion", 4, "prefabs/blockprefabs/steed mansion_blocks",
            "Steed Mansion_Blocks", 39, 0, "Mansion", "Prefabs/Steeds/Spookyhorse", 2),
        Def("deadlands.grave", 7, "prefabs/blockprefabs/steed grave_blocks",
            "Steed Grave_Blocks", 25, 35, "King Grave", "Prefabs/Steeds/Warhorse P1", 3,
            aliases: new[] { Alias(19, "Prefabs/Steeds/Warhorse P2") }),
        // Santa/type3：SantaHouse_Blocks 的活动 SteedSpawn 指向 89244/Reindeer（block-hierarchy.json
        // 实测 steeds=[89244]）。用户 2026-10-02 明确授权：这是**本 Mod 新增的 Greek 普通特色获取点
        // 规则**（不是当前 native 已证的无条件活动入口），保留整块原生场景内部解锁/价格；不猜测
        // 12 月或其他外层日期资格。因此数据行为普通新授予（SeasonalChallengeId=0），不设活动门。
        Def("santahouse.reindeer", 3, "prefabs/blockprefabs/santahouse_blocks",
            "SantaHouse_Blocks", 40, 35, "Santa House", "Prefabs/Steeds/Reindeer", 5),
        // Birthday/type38：多阶段原生链（PayableUpgrade + CerberusPuzzle → nextPrefab →
        // Steed_BirthdayParty_Activated 的 SteedSpawn117609 → 112486/Rainbow Pony）。整块注入，
        // 不直接生成最终坐骑；谜题/升级条件保留原生组件执行。
        // 新授予复用原生活动资格（Anniversary id10）：ChallengeHolder.Inst.ChallengeDataForID(10)
        // 有效 + id==10 + isSeasonalEvent + SeasonalEventManager.IsEventActive(data)（见
        // CrossWorldMountRuntime.SeasonalGrantAllowed）。已授予/已付款恢复不受活动到期影响；
        // 固定岛生成后活动晚激活不补发、不重生成、不改旧存档。
        Def("anniversary.rainbowpony", 38, "prefabs/blockprefabs/seasonal/anniversary/steed birthday_blocks",
            "Steed Birthday_Blocks", 153, 35, "Steed_BirthdayParty_Abandoned", "Prefabs/Steeds/Rainbow Pony", 6,
            seasonalChallengeId: 10),
        // 樱花树（Bamboo 世界的 type6 物种，用户 canonical：共享物种只投一个获取点，选 Kirin 保留
        // 外世界特色）。普通非季节块 prefabs/blockprefabs/steed sakura_blocks / GO Steed Sakura_Blocks /
        // LevelBlock95765 group24，子层 BlossomTree(GO25083) 直接挂 SteedSpawn95695：steeds=[Unicorn87019]、
        // 4宝石。原生 Bamboo 靠 Unicorn→Kirin 的 biome swap 兑现该物种；Greek 无此 swap，
        // 且设施恢复不保存 steeds 数组 → 由依赖层的已授予 scope 在 SpawnSteed 生成点统一选择 Kirin。
        // 希腊表 type6 已有 Unicorn 精确键 → 变体覆盖，不写共享表。技能（DropCoinsSteedAbility
        // 45秒/3币）与外观资源全部原样保留。
        Def("bamboo.kirin", 6, "prefabs/blockprefabs/steed sakura_blocks",
            "Steed Sakura_Blocks", 24, 35, "BlossomTree", "Prefabs/Steeds/Kirin", 4,
            variantOverride: true),
    };

    private static CrossWorldMountDefinition Def(string id, int steedTypeId, string blockPath, string blockObject,
        int groupOne, int groupTwo, string facility, string steedPrefabPath, int islandSlot,
        CrossWorldMountSteedAlias[] aliases = null, string poolCollection = null, bool variantOverride = false,
        int seasonalChallengeId = 0)
        => new CrossWorldMountDefinition(id, steedTypeId, blockPath, blockObject, groupOne, groupTwo,
            facility, facility, steedPrefabPath, islandSlot, aliases, poolCollection, variantOverride,
            seasonalChallengeId);

    private static CrossWorldMountSteedAlias Alias(int steedTypeId, string prefabPath)
        => new CrossWorldMountSteedAlias(steedTypeId, prefabPath);

    internal static CrossWorldMountDefinition BySteedType(int steedTypeId)
    {
        for (int i = 0; i < Definitions.Length; i++)
            if (Definitions[i].SteedTypeId == steedTypeId) return Definitions[i];
        return null;
    }

    /// <summary>该 landData.steedSpawns 数组是否已含任一定义（用于按岛计数与分配）。</summary>
    internal static bool ArrayContainsAny(int[] steedSpawns)
    {
        if (steedSpawns == null) return false;
        for (int i = 0; i < steedSpawns.Length; i++)
            if (BySteedType(steedSpawns[i]) != null) return true;
        return false;
    }
}

/// <summary>当前岛的原生信号快照（由 Runtime 从 CampaignSaveData/IslandSaveData 读取）。</summary>
internal readonly struct CrossWorldMountIslandState
{
    internal readonly bool LandValid;
    /// <summary>landData[land].steedSpawns 已含"本定义"的坐骑值。</summary>
    internal readonly bool MarkerOnIsland;
    /// <summary>本 reign 任一 land 的 steedSpawns 已含"本定义"的坐骑值。</summary>
    internal readonly bool MarkerInCampaign;
    internal readonly bool IslandVisited;
    /// <summary>本岛存档 objects 中存在本定义的获取设施回执（原生持久化，标记被覆盖时的兜底）。</summary>
    internal readonly bool ReceiptOnIsland;
    internal readonly double LastPlayedDays;
    /// <summary>本岛已登记的定义数（用于多定义的稳定分布）。</summary>
    internal readonly int GrantedDefinitionCount;

    internal CrossWorldMountIslandState(
        bool landValid, bool markerOnIsland, bool markerInCampaign, bool islandVisited,
        bool receiptOnIsland, double lastPlayedDays, int grantedDefinitionCount)
    {
        LandValid = landValid;
        MarkerOnIsland = markerOnIsland;
        MarkerInCampaign = markerInCampaign;
        IslandVisited = islandVisited;
        ReceiptOnIsland = receiptOnIsland;
        LastPlayedDays = lastPlayedDays;
        GrantedDefinitionCount = grantedDefinitionCount;
    }
}

internal static class CrossWorldMountPolicy
{
    /// <summary>
    /// 授予/注入判定（每定义一次，顺序即优先级）：
    /// 1. land 无效 → Skip（fail-closed）；
    /// 2. 本岛已有本定义标记或原生回执 → InjectOnly（读档重建时补回地块）；
    /// 3. 本战役已在其他岛授予本定义 → Skip（一战役一次，原生记录驱动）；
    /// 4. 该岛已访问过 / lastPlayedTimeDays &gt; 0 → Skip（老岛不追插/不重排）；
    /// 5. 其余（新生成岛且该定义从未授予）→ GrantAndInject。
    /// </summary>
    internal static CrossWorldMountDecision Decide(in CrossWorldMountIslandState state)
    {
        if (!state.LandValid) return CrossWorldMountDecision.Skip;
        if (state.MarkerOnIsland || state.ReceiptOnIsland) return CrossWorldMountDecision.InjectOnly;
        if (state.MarkerInCampaign) return CrossWorldMountDecision.Skip;
        if (state.IslandVisited) return CrossWorldMountDecision.Skip;
        if (state.LastPlayedDays > 0d) return CrossWorldMountDecision.Skip;
        return CrossWorldMountDecision.GrantAndInject;
    }

    /// <summary>
    /// 新授予分布（issue-98 新契约）：16 定义的新获取入口集中到附加岛（物理 land11），
    /// 与玩家访问顺序无关；定义行的 <c>IslandSlot</c> 只保留为 legacy 元数据（旧岛 marker/receipt
    /// 的 InjectOnly 恢复不经过本门，不迁移/不复制/不删除旧记录）。非 land11 一律不授予。
    /// </summary>
    internal static bool ShouldGrantDefinition(in CrossWorldMountDefinition definition, int land)
        => land == ExtensionIslandPlan.LandIndex;

    internal static bool Contains(int[] values, int wanted)
    {
        if (values == null) return false;
        for (int i = 0; i < values.Length; i++)
            if (values[i] == wanted) return true;
        return false;
    }

    /// <summary>在数组尾部追加（已存在则原样返回；null 视为空）。</summary>
    internal static int[] AppendMarker(int[] steedSpawns, int steedTypeId)
    {
        if (Contains(steedSpawns, steedTypeId)) return steedSpawns;
        int length = steedSpawns != null ? steedSpawns.Length : 0;
        var result = new int[length + 1];
        for (int i = 0; i < length; i++) result[i] = steedSpawns[i];
        result[length] = steedTypeId;
        return result;
    }
}

/// <summary>授予提交结果：只有"生成成功且地块确认进入本次目标 Level"才允许写 native 标记。</summary>
internal enum CrossWorldMountGrantOutcome
{
    None = 0,
    /// <summary>放置已验证：写 landData.steedSpawns 标记。</summary>
    CommitMarker = 1,
    /// <summary>生成失败/未证实放置：绝不写标记。</summary>
    AbortNoMarker = 2,
}

/// <summary>
/// 一次生成的授予事务（纯逻辑）：Runtime 在 GetBlocks 发布列表时 Begin，CloneInto 观察到
/// 目标实例进入某个 Level 时 NotePlaced(levelPointer)，生成正常结束 Complete(targetLevelPointer)
/// ——只有"观察到的 Level == 本次目标 Level"才提交。生成异常 Abort（自己只终止自己）。
/// </summary>
internal struct CrossWorldMountGrantState
{
    private bool _active;
    private int _land;
    private bool _placed;
    private ulong _placedLevelPointer;
    private bool _hasPlacedLevel;

    internal bool Pending => _active;
    internal int Land => _land;

    internal void Begin(int land)
    {
        _active = true;
        _land = land;
        _placed = false;
        _hasPlacedLevel = false;
        _placedLevelPointer = 0UL;
    }

    /// <summary>CloneInto 观察到本次注入实例被克隆进 <paramref name="levelPointer"/>。</summary>
    internal void NotePlaced(ulong levelPointer)
    {
        if (!_active) return;
        _placed = true;
        _placedLevelPointer = levelPointer;
        _hasPlacedLevel = true;
    }

    /// <summary>
    /// 生成正常结束：只有观察到"本实例进入本次目标 Level"才提交标记。
    /// <paramref name="targetLevelPointer"/> 为本次 GenerateInternal 的 Level 实例指针。
    /// </summary>
    internal CrossWorldMountGrantOutcome Complete(ulong targetLevelPointer, out int land)
    {
        if (!_active)
        {
            land = -1;
            return CrossWorldMountGrantOutcome.None;
        }
        land = _land;
        bool placedInTarget = _placed && _hasPlacedLevel && _placedLevelPointer == targetLevelPointer;
        _active = false;
        _placed = false;
        _hasPlacedLevel = false;
        return placedInTarget ? CrossWorldMountGrantOutcome.CommitMarker : CrossWorldMountGrantOutcome.AbortNoMarker;
    }

    /// <summary>生成异常/会话归还：只清自己，绝不写标记。</summary>
    internal void Abort()
    {
        _active = false;
        _placed = false;
        _hasPlacedLevel = false;
    }
}

/// <summary>地图详情条目类型（与 DynamicIcon 的原生子集对应：Steed=0 / Hermit=1）。</summary>
internal enum MountMapEntryKind
{
    Steed = 0,
    Hermit = 1,
}

/// <summary>一条真实的地图详情条目（来自 LandMapData，不含任何理论/规划数据）。</summary>
internal readonly struct MountMapEntry
{
    internal readonly MountMapEntryKind Kind;
    internal readonly int TypeId;
    internal readonly int SourceIndex;

    internal MountMapEntry(MountMapEntryKind kind, int typeId, int sourceIndex)
    {
        Kind = kind;
        TypeId = typeId;
        SourceIndex = sourceIndex;
    }
}

/// <summary>地图详情行规划（纯函数）：真实数组 → 行列表 + 原生槽位溢出数。</summary>
internal static class MountMapPlan
{
    /// <summary>原生希腊岛详情面板最多显示 2 个坐骑槽（Steed1/Steed2，Quest 面板实测；God 面板 1 个）。</summary>
    internal const int NativeSteedSlots = 2;
    /// <summary>原生希腊岛详情面板 1 个隐士槽（Hermit）。</summary>
    internal const int NativeHermitSlots = 1;

    internal static bool TryBuild(int[] steedTypes, int[] hermitTypes,
        List<MountMapEntry> into, out int extraBeyondNative)
    {
        extraBeyondNative = 0;
        if (into == null) return false;
        into.Clear();
        int steeds = 0;
        if (steedTypes != null)
        {
            for (int i = 0; i < steedTypes.Length; i++)
            {
                into.Add(new MountMapEntry(MountMapEntryKind.Steed, steedTypes[i], i));
                steeds++;
            }
        }
        int hermits = 0;
        if (hermitTypes != null)
        {
            for (int i = 0; i < hermitTypes.Length; i++)
            {
                into.Add(new MountMapEntry(MountMapEntryKind.Hermit, hermitTypes[i], i));
                hermits++;
            }
        }
        if (steeds > NativeSteedSlots) extraBeyondNative += steeds - NativeSteedSlots;
        if (hermits > NativeHermitSlots) extraBeyondNative += hermits - NativeHermitSlots;
        return into.Count > 0;
    }

    internal static bool NeedsExtendedList(List<MountMapEntry> entries, Func<MountMapEntryKind, int, bool> hasNativeIcon)
    {
        if (entries == null || entries.Count == 0) return false;
        int steeds = 0, hermits = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e.Kind == MountMapEntryKind.Steed) steeds++;
            else hermits++;
            if (hasNativeIcon != null && !hasNativeIcon(e.Kind, e.TypeId)) return true;
        }
        return steeds > NativeSteedSlots || hermits > NativeHermitSlots;
    }
}

/// <summary>矩形（纯数据；面板局部坐标，原点=左下角，单位=UI 单位）。</summary>
internal readonly struct MountMapRect
{
    internal readonly float XMin;
    internal readonly float YMin;
    internal readonly float XMax;
    internal readonly float YMax;

    internal MountMapRect(float xMin, float yMin, float xMax, float yMax)
    {
        XMin = xMin;
        YMin = yMin;
        XMax = xMax;
        YMax = yMax;
    }

    internal bool Intersects(in MountMapRect other)
        => XMin < other.XMax && other.XMin < XMax && YMin < other.YMax && other.YMin < YMax;
}

/// <summary>
/// 详情列表布局（纯函数，含几何证明可离线复算）：
/// 1) <see cref="PickFreeRows"/>：在面板高度内、给定横跨范围 [xMin,xMax] 上找"自上而下连续的
///    无遮挡行带"——被岛图/原生图标矩形挡住的 16px 行一律不用（因此列表不覆盖原生图标区）；
/// 2) <see cref="PageCount"/> / <see cref="Plan"/>：全部条目按页可达（无"其余N项"截断行），
///    每页列数按可用宽度取 1 或 2。
/// </summary>
internal static class MountMapLayoutPlan
{
    internal const float RowHeight = 16f;
    /// <summary>每页最多行数（详情区域内；超出走分页，不提高硬上限替代可达性）。</summary>
    internal const int MaxRowsPerPage = 4;
    internal const int MaxColumns = 2;
    /// <summary>无可用行带时的兜底：面板顶部 1 行（仅用于岛图占满整板的少数面板，仍避开图标区）。</summary>
    internal const int FallbackRows = 1;

    /// <summary>
    /// 自上而下连续的可用行数：行 i 占 y ∈ [top - (i+1)*rowHeight, top - i*rowHeight]，
    /// 只统计与任一遮挡矩形（岛图/原生图标/按钮）不相交的行，遇到第一行被挡即停止。
    /// </summary>
    internal static int PickFreeRows(float panelTop, float panelBottom, float xMin, float xMax,
        IReadOnlyList<MountMapRect> occupied, int maxRows)
    {
        if (panelTop <= panelBottom || xMax <= xMin || maxRows <= 0) return 0;
        int free = 0;
        float top = panelTop;
        while (free < maxRows && top - RowHeight >= panelBottom)
        {
            var band = new MountMapRect(xMin, top - RowHeight, xMax, top);
            bool blocked = false;
            if (occupied != null)
            {
                for (int i = 0; i < occupied.Count; i++)
                {
                    if (band.Intersects(occupied[i])) { blocked = true; break; }
                }
            }
            if (blocked) break;
            free++;
            top -= RowHeight;
        }
        return free;
    }

    internal static int PageCount(int entryCount, int rowsPerPage, int columns)
    {
        int perPage = rowsPerPage * columns;
        if (entryCount <= 0 || perPage <= 0) return 0;
        return (entryCount + perPage - 1) / perPage;
    }

    /// <summary>行主序：第 i 条 = row i/columns、column i%columns（每页内）。</summary>
    internal static int ColumnOf(int index, int columns) => columns <= 0 ? 0 : index % columns;
    internal static int RowOf(int index, int columns) => columns <= 0 ? 0 : index / columns;

    /// <summary>给定可用行数与列数的每页容量（列数按页宽是否 ≥ 2 个最小单元格决定）。</summary>
    internal static int ColumnsFor(float pageWidth, float minCellWidth)
    {
        if (pageWidth >= minCellWidth * 2f) return MaxColumns;
        return 1;
    }
}
