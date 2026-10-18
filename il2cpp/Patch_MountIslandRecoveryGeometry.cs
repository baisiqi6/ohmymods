namespace KingdomEnhancedMod;

/// <summary>
/// 旧空坐骑岛恢复的离线派生资源/几何/地面白名单（纯数据，无 Unity/Il2Cpp/Harmony 依赖）。
///
/// 来源（只读离线解析当前游戏包，不复制任何游戏图片/二进制）：
/// - geometry-envelopes.json SHA-256 c46b486120a6df0181f8e7894e9583c546063064d18c91e5e48f5cc0cc7a4d5f
///   （16 设施、30 含 biome controller、74 clips、1082 sprites、62 位移 binding；真实 sprite
///   rect/pivot/PPU + transform 树 + 全部 controller/biome 替换 + cubic 极值/constant/dense 轨迹）。
///   53 次 controller 指向不存在 descendant 的引用（Mansion 4、King Grave 49）保留在源表
///   unmatchedBindings；本表不声称所有动画路径匹配，运行期仍以真实 clone 层级/bounds 复核。
/// - surface-exclusions.json SHA-256 d7fa15952d477d1b723f57ee61a32d6493b0874f46b79a877496754f674054db
///   （25 条 native component 类型 + Persistent.path 键的源 block 相对排除区间）。
/// - tile-surface-inventory.json（当前 1127 Tile 的 noGround/path/本地表面 y-z 分布）。
///
/// 数值语义：
/// - <see cref="FacilityRow.ReservedHalfWidth"/> 是相对设施 root X 的额外保守半宽，已包含
///   sourceFacilityLocalX 与源 block Tile 宽，覆盖真实动画包络；运行期 clone 实际
///   SpriteRenderer/Collider2D 水平 bounds 不得超出。
/// - <see cref="FacilityRow.EnvelopeMinX"/> / EnvelopeMaxX 是派生动画包络的水平范围，只用于诊断/
///   交叉核对；预留半宽才是放置拒绝条件。
/// - 源 block 相对排除区间通过运行期结构真实 Transform 映射到 world，再与实际 renderer/collider
///   bounds 合并。未知 path/无法读范围必须 fail-closed，不能猜。
/// </summary>
internal static class MountIslandRecoveryGeometry
{
    internal const string GeometrySourceSha256 = "c46b486120a6df0181f8e7894e9583c546063064d18c91e5e48f5cc0cc7a4d5f";
    internal const string SurfaceSourceSha256 = "d7fa15952d477d1b723f57ee61a32d6493b0874f46b79a877496754f674054db";

    internal readonly struct FacilityRow
    {
        internal readonly string DefinitionId;
        internal readonly string FacilityName;
        internal readonly string FacilityPersistentPath;
        internal readonly float SourceFacilityLocalX;
        internal readonly float ReservedHalfWidth;
        internal readonly float EnvelopeMinX;
        internal readonly float EnvelopeMaxX;

        internal FacilityRow(string definitionId, string facilityName, string facilityPersistentPath,
            float sourceFacilityLocalX, float reservedHalfWidth, float envelopeMinX, float envelopeMaxX)
        {
            DefinitionId = definitionId;
            FacilityName = facilityName;
            FacilityPersistentPath = facilityPersistentPath;
            SourceFacilityLocalX = sourceFacilityLocalX;
            ReservedHalfWidth = reservedHalfWidth;
            EnvelopeMinX = envelopeMinX;
            EnvelopeMaxX = envelopeMaxX;
        }
    }

    internal enum StructureKind
    {
        Bridge = 0,
        Beach = 1,
        CliffPortal = 2,
        BombablePortal = 3,
    }

    internal readonly struct StructureExclusion
    {
        internal readonly StructureKind Kind;
        internal readonly string PersistentPath;
        internal readonly float SourceMinX;
        internal readonly float SourceMaxX;

        internal StructureExclusion(StructureKind kind, string persistentPath, float sourceMinX, float sourceMaxX)
        {
            Kind = kind;
            PersistentPath = persistentPath;
            SourceMinX = sourceMinX;
            SourceMaxX = sourceMaxX;
        }
    }

    /// <summary>
    /// 合格地面正名单。Cave Tile Big / Tile Nothing / Tile Blank / Olympus 深层 / raised 等均不在内。
    /// Tile Clearing 当前未列入：没有与 Big/Forest 同等的表面 y/z/变换证据，宁少不多。
    /// </summary>
    internal static readonly string[] EligibleTilePaths =
    {
        "Prefabs/Environment/Tile Big",
        "Prefabs/Environment/Tile Forest",
    };

    internal static readonly FacilityRow[] Facilities =
    {
        Row("norselands.gullinbursti", "BoarAltar_norselands", "Prefabs/Environment/MountAreas/BoarAltar_norselands", 0f, 6f, -4.78125f, 3.28125f),
        Row("norselands.sleipnir", "SleipnirTree_norselands", "Prefabs/Environment/MountAreas/SleipnirTree_norselands", 0f, 10f, -5.4375f, 5.4375f),
        Row("norselands.reindeer", "Reindeer_area_norselands", "Prefabs/Environment/MountAreas/Reindeer_area_norselands", 0f, 10f, -8.0625f, 8f),
        Row("norselands.catcart", "CatCartArch_norselands", "Prefabs/Environment/MountAreas/CatCartArch_norselands", 0f, 8f, -6.5675001f, 5.3125f),
        Row("norselands.kelpie", "KelpiePool_norselands", "Prefabs/Environment/MountAreas/KelpiePool_norselands", 0f, 10f, -9.546875f, 9.956875f),
        Row("norselands.hrimfaxe", "Horse_DayNight_area_norselands", "Prefabs/Environment/MountAreas/Horse_DayNight_area_norselands", 0f, 6f, -4.6875f, 4.6875f),
        Row("norselands.wolf", "WolfRock_norselands", "Prefabs/Environment/MountAreas/WolfRock_norselands", 0f, 14f, -7.5939625f, 7.40625f),
        Row("woodlands.beetle", "BeetleHouse", "Prefabs/Environment/MountAreas/BeetleHouse", 2.1700001f, 10.17f, -9.90625f, 9.90625f),
        Row("woodlands.golem", "GolemHouse", "Prefabs/Environment/MountAreas/GolemHouse", 1.39f, 10.61f, -7.8125f, 8.3425f),
        Row("woodlands.gamigin", "GamiginHouse", "Prefabs/Environment/MountAreas/GamiginHouse", 4f, 10f, -7.75f, 7.75f),
        Row("swamp.eggsteed", "Egg", "Prefabs/Environment/MountAreas/Egg", 2.3299999f, 10.33f, -1.40625f, 1.5862503f),
        Row("woodlands.mansion", "Mansion", "Prefabs/Environment/MountAreas/Mansion", 4.98f, 12.98f, -0.5f, 0.5f),
        Row("deadlands.grave", "King Grave", "Prefabs/Environment/MountAreas/King Grave", 3.9400024f, 10.059998f, -8f, 8f),
        Row("santahouse.reindeer", "Santa House", "Prefabs/Environment/MountAreas/Santa House", -2f, 14f, 0f, 0f),
        Row("anniversary.rainbowpony", "Steed_BirthdayParty_Abandoned", "Prefabs/Environment/MountAreas/seasonal/anniversary/Steed_BirthdayParty_Abandoned", -1f, 7f, -2f, 4.125f),
        Row("bamboo.kirin", "BlossomTree", "Prefabs/Environment/MountAreas/BlossomTree", 2.28125f, 10.28125f, -8.0625f, 8f),
    };

    /// <summary>
    /// 25 条 typed structure 源 block 相对排除区间。同一 path 可能同时有 CliffPortalState 与
    /// BombablePortal，两行都必须匹配（按 kind + path 精确键）。
    /// </summary>
    internal static readonly StructureExclusion[] StructureExclusions =
    {
        Exclusion(StructureKind.Bridge, "Prefabs/Environment/Bridge Forest Portal", -4f, 8f),
        Exclusion(StructureKind.CliffPortal, "Prefabs/Enemies/Cliff Portal Deactivated Anniversary Left", -12.40999985f, 3.590000153f),
        Exclusion(StructureKind.Bridge, "Prefabs/Environment/norselands/Bridge Long_Norselands", -4f, 16f),
        Exclusion(StructureKind.Bridge, "Prefabs/Environment/Bridge Very Short", -4f, 8f),
        Exclusion(StructureKind.Beach, "Prefabs/Buildings and Interactive/greece/Beach_greece", -8.03125f, 16.6875f),
        Exclusion(StructureKind.Beach, "Prefabs/Buildings and Interactive/greece/MtOlympus/Shipyard_greece", -30.71875f, 5.28125f),
        Exclusion(StructureKind.Beach, "Prefabs/Buildings and Interactive/Beach", -4.0625f, 11.96875f),
        Exclusion(StructureKind.Beach, "Prefabs/Buildings and Interactive/greece/Quests/Hermes_Dock", -4f, 12f),
        Exclusion(StructureKind.CliffPortal, "Prefabs/Enemies/Cliff Portal Deactivated Anniversary", -12.94999695f, 2.050003052f),
        Exclusion(StructureKind.Bridge, "Prefabs/Environment/Bridge", -4f, 12f),
        Exclusion(StructureKind.Beach, "Prefabs/Buildings and Interactive/norselands/Beach_Norselands", -4.0625f, 11.96875f),
        Exclusion(StructureKind.Bridge, "Prefabs/Environment/norselands/Bridge Medium_Norselands", -4f, 14f),
        Exclusion(StructureKind.CliffPortal, "Prefabs/Enemies/SkullCliffPortal", -20.94999981f, 14.05000019f),
        Exclusion(StructureKind.BombablePortal, "Prefabs/Enemies/SkullCliffPortal", -20.94999981f, 14.05000019f),
        Exclusion(StructureKind.Bridge, "Prefabs/Environment/norselands/Bridge Short_Norselands", -4f, 10f),
        Exclusion(StructureKind.Bridge, "Prefabs/Environment/Bridge Short", -4f, 10f),
        Exclusion(StructureKind.CliffPortal, "Prefabs/Enemies/Cliff Portal", -20.94999981f, 14.05000019f),
        Exclusion(StructureKind.BombablePortal, "Prefabs/Enemies/Cliff Portal", -20.94999981f, 14.05000019f),
        Exclusion(StructureKind.Bridge, "Prefabs/Environment/norselands/Bridge_Norselands", -4f, 12f),
        Exclusion(StructureKind.Bridge, "Prefabs/Environment/Bridge Double", -4f, 20f),
        Exclusion(StructureKind.Bridge, "Prefabs/Environment/norselands/Bridge Double_Norselands", -4f, 20f),
        Exclusion(StructureKind.Bridge, "Prefabs/Environment/Bridge Long", -4f, 16f),
        Exclusion(StructureKind.Bridge, "Prefabs/Environment/Bridge Two River", -4f, 22f),
        Exclusion(StructureKind.Bridge, "Prefabs/Environment/Bridge Medium", -4f, 14f),
        Exclusion(StructureKind.Bridge, "Prefabs/Environment/norselands/Bridge Very Short_Norselands", -4f, 8f),
    };

    internal static bool TryGetFacility(string definitionId, out FacilityRow row)
    {
        for (int i = 0; i < Facilities.Length; i++)
        {
            if (string.Equals(Facilities[i].DefinitionId, definitionId, System.StringComparison.Ordinal))
            {
                row = Facilities[i];
                return true;
            }
        }
        row = default;
        return false;
    }

    internal static bool TryGetFacilityByPath(string persistentPath, out FacilityRow row)
    {
        for (int i = 0; i < Facilities.Length; i++)
        {
            if (string.Equals(Facilities[i].FacilityPersistentPath, persistentPath, System.StringComparison.Ordinal))
            {
                row = Facilities[i];
                return true;
            }
        }
        row = default;
        return false;
    }

    internal static bool IsEligibleTilePath(string persistentPath)
    {
        if (string.IsNullOrEmpty(persistentPath)) return false;
        for (int i = 0; i < EligibleTilePaths.Length; i++)
        {
            if (string.Equals(EligibleTilePaths[i], persistentPath, System.StringComparison.Ordinal)) return true;
        }
        return false;
    }

    internal static bool IsStructurePath(string persistentPath)
    {
        if (string.IsNullOrEmpty(persistentPath)) return false;
        for (int i = 0; i < StructureExclusions.Length; i++)
        {
            if (string.Equals(StructureExclusions[i].PersistentPath, persistentPath, System.StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    internal static bool TryGetStructureExclusion(StructureKind kind, string persistentPath, out StructureExclusion row)
    {
        for (int i = 0; i < StructureExclusions.Length; i++)
        {
            if (StructureExclusions[i].Kind == kind
                && string.Equals(StructureExclusions[i].PersistentPath, persistentPath, System.StringComparison.Ordinal))
            {
                row = StructureExclusions[i];
                return true;
            }
        }
        row = default;
        return false;
    }

    private static FacilityRow Row(string definitionId, string facilityName, string facilityPersistentPath,
        float sourceFacilityLocalX, float reservedHalfWidth, float envelopeMinX, float envelopeMaxX)
        => new FacilityRow(definitionId, facilityName, facilityPersistentPath, sourceFacilityLocalX,
            reservedHalfWidth, envelopeMinX, envelopeMaxX);

    private static StructureExclusion Exclusion(StructureKind kind, string persistentPath, float minX, float maxX)
        => new StructureExclusion(kind, persistentPath, minX, maxX);
}
