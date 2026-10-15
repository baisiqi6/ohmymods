namespace KingdomEnhancedMod;

/// <summary>
/// 坐骑双岛目标分配与地图索引翻译（纯策略表：零 Unity/Il2Cpp/Harmony 依赖，不注册 hook、
/// 不读写存档、不含资源路径/定价/目录元数据）。
///
/// 用途边界（用户 2026-10-09 双岛方案，docs/project-harness/tasks/mount-islands-split-20261009/plan.md）：
/// - 本表只服务 new-grant（新授予投放）过滤：把尚未授予的稳定 definitionId 分配到 A/B 两岛。
///   既有 16 条定义的 InjectOnly 原址恢复仍由 root 现行 caller 负责，本表不参与，
///   也不改变旧存档的已授予分布。
/// - 分组按定义 Id 精确（Ordinal、大小写敏感）匹配，不按数组位置、访问次序或 SteedType 数值。
///   注意 norselands.wolf 的 SteedType 恰为 13，与 SecondaryLand 同值，绝不能按数值猜岛。
/// - 物理岛 ↔ UI 索引翻译只覆盖扩展岛 11→10、13→11；原生 10→UI9 保持原生逻辑，不经本表。
/// - land 13 尚未宣布 Native accepted：Native 寻址/容量审计（文件容量、landData 寻址、
///   全特征登记）完成前不得视为运行时已可用；本文件不授权生成/解锁，接线由 root 在审计后另行审查。
/// - 本文件当前不被任何 caller 引用（未接线），也不注册任何 Harmony hook。
/// </summary>
internal static class MountIslandSplitPolicy
{
    /// <summary>坐骑主岛（原物理 land11）：A 组（北境与樱林 8 条）的 new-grant 目标。</summary>
    internal const int PrimaryLand = 11;

    /// <summary>新增坐骑岛（物理 land13）：B 组（幽林与奇境 8 条）的 new-grant 目标。未宣布 Native accepted。</summary>
    internal const int SecondaryLand = 13;

    /// <summary>文件/landData 至少需可寻址 0..13，即容量 ≥ 14。</summary>
    internal const int RequiredFileCapacity = 14;

    /// <summary>主岛对应总览 UI 索引（物理 11 → UI 10）。</summary>
    internal const int PrimaryUi = 10;

    /// <summary>新增岛对应总览 UI 索引（物理 13 → UI 11）。</summary>
    internal const int SecondaryUi = 11;

    /// <summary>
    /// new-grant 目标岛：按稳定 definitionId 精确查表（大小写敏感）。
    /// 未知或 null 返回 -1（调用方不得据 -1 生成或投放任何东西）。
    /// </summary>
    internal static int GetTargetLand(string definitionId)
    {
        switch (definitionId)
        {
            // A 组：北境与樱林（8 条）→ PrimaryLand。
            case "norselands.gullinbursti":
            case "norselands.sleipnir":
            case "norselands.reindeer":
            case "norselands.catcart":
            case "norselands.kelpie":
            case "norselands.hrimfaxe":
            case "norselands.wolf": // SteedType=13：与 SecondaryLand 同值，必须按 Id 归主岛
            case "bamboo.kirin":
                return PrimaryLand;

            // B 组：幽林与奇境（8 条）→ SecondaryLand。
            case "woodlands.beetle":
            case "woodlands.golem":
            case "woodlands.gamigin":
            case "woodlands.mansion":
            case "swamp.eggsteed":
            case "deadlands.grave":
            case "santahouse.reindeer":
            case "anniversary.rainbowpony":
                return SecondaryLand;

            default:
                return -1;
        }
    }

    /// <summary>是否为扩展岛（仅 11/13）。原生 0..10 与宫廷 12 均不属于扩展岛。</summary>
    internal static bool IsExtensionLand(int land) =>
        land == PrimaryLand || land == SecondaryLand;

    /// <summary>
    /// 物理岛 → 总览 UI 索引：仅 11→10、13→11。
    /// 其余（含原生 10→9）返回 false 且 ui=-1，交原生逻辑处理。
    /// </summary>
    internal static bool TryGetMapIndex(int physical, out int ui)
    {
        switch (physical)
        {
            case PrimaryLand:
                ui = PrimaryUi;
                return true;
            case SecondaryLand:
                ui = SecondaryUi;
                return true;
            default:
                ui = -1;
                return false;
        }
    }

    /// <summary>
    /// 总览 UI 索引 → 物理岛：仅 10→11、11→13。
    /// 其余（含原生 UI9）返回 false 且 physical=-1。
    /// </summary>
    internal static bool TryGetPhysicalIndex(int ui, out int physical)
    {
        switch (ui)
        {
            case PrimaryUi:
                physical = PrimaryLand;
                return true;
            case SecondaryUi:
                physical = SecondaryLand;
                return true;
            default:
                physical = -1;
                return false;
        }
    }
}
