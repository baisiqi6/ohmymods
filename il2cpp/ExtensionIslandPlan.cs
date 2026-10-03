using System;
using System.Collections.Generic;

namespace KingdomEnhancedMod;

/// <summary>
/// 附加希腊探索岛（物理 land11 / 地图 UI10）：纯计划逻辑（不引用 UnityEngine/interop，可离线单测）。
///
/// 物理索引合同（operator/integration-contract.md + native-lifecycle/native-terrain/native-travel CONTRACT）：
/// 原生物理 land0..10 保持不动（10 = MtOlympus Top，禁止覆盖），本 Mod 只新增物理 land11；
/// 地图 UI 为第 11 项（UI 索引 10），UI10 与物理 11 的翻译由 travel 子任务负责，本文件只固定常量。
///
/// 私有 LevelConfig 的地形合同（native-terrain/CONTRACT.md §2）：
/// - 8 道门 = 6 普通 Portal（原生 Distribute 左右各 3）+ 左真 Dock(BeachLeft) + 右真 Cliff(CliffRight)；
/// - 每侧端点各一次（EndLeft/EndRight），Cliff/Beach 类别各一次；
/// - 其余非白名单组一律归零，不继承所选 God 模板的神祇/任务/Olympus 配额。
/// </summary>
internal static class ExtensionIslandPlan
{
    /// <summary>新增附加岛的物理 land 索引（原 0..10 共 11 项之后的第一项）。</summary>
    internal const int LandIndex = 11;
    /// <summary>附加岛在地图 UI/详情数组中的索引（原 UI0..9 之后的第 11 项）。</summary>
    internal const int MapIndex = 10;
    /// <summary>岛文件寻址容量下限：物理 land0..11 共 12 项（严格只升不降）。</summary>
    internal const int MinimumFileCapacity = 12;

    // LevelBlockGroup 数值合同（actual interop 枚举实测，只读；本文件保持纯逻辑不引用 interop 类型）。
    internal const int GroupStartingArea = 1;
    internal const int GroupStartingAreaTight = 2;
    internal const int GroupForest = 3;
    internal const int GroupBridge = 4;
    internal const int GroupClearing = 5;
    internal const int GroupPortal = 6;
    internal const int GroupPlayerSpawnTitle = 7;
    internal const int GroupPlayerSpawn = 8;
    internal const int GroupChest = 14;
    internal const int GroupBeggarCamp = 15;
    internal const int GroupBeachLeft = 18;
    internal const int GroupBeachRight = 19;
    internal const int GroupCliffLeft = 20;
    internal const int GroupCliffRight = 21;
    internal const int GroupEndLeft = 30;
    internal const int GroupEndRight = 31;
    internal const int GroupCliff = 32;
    internal const int GroupBeach = 33;
    internal const int GroupCaveSection = 54;

    /// <summary>一条 LevelGroupCount 计划（group + 最小/最大放置数）。</summary>
    internal readonly struct GroupCount
    {
        internal readonly int Group;
        internal readonly int Min;
        internal readonly int Max;

        internal GroupCount(int group, int min, int max)
        {
            Group = group;
            Min = min;
            Max = max;
        }

        internal bool Equals(in GroupCount other)
            => Group == other.Group && Min == other.Min && Max == other.Max;
    }

    /// <summary>
    /// 由所选正常 God 模板（84672 Greece_Land_God_Artemis）的 groupCounts 逐组生成附加岛私有值：
    /// 白名单组保留模板区间，8 道门/端点组使用硬配额，其余（含全部神祇/任务/Olympus/活动组）归零。
    /// 纯函数：不修改模板列表，返回新列表（LevelGroupCount 的 deep clone 语义由调用方落实）。
    /// </summary>
    internal static List<GroupCount> PlanGroupCounts(IReadOnlyList<GroupCount> template)
    {
        var result = new List<GroupCount>(template?.Count ?? 0);
        if (template == null) return result;
        for (int i = 0; i < template.Count; i++) result.Add(PlanGroup(template[i]));
        return result;
    }

    /// <summary>单组计划：硬配额优先；白名单保留模板；其余归零。</summary>
    internal static GroupCount PlanGroup(in GroupCount template)
    {
        switch (template.Group)
        {
            case GroupPortal: return new GroupCount(GroupPortal, 6, 6);          // 原生 Distribute 左右各 3
            case GroupBeachLeft: return new GroupCount(GroupBeachLeft, 1, 1);    // 左真海岸（Dock）
            case GroupBeachRight: return new GroupCount(GroupBeachRight, 0, 0);  // 禁止右海岸
            case GroupCliffLeft: return new GroupCount(GroupCliffLeft, 0, 0);    // 禁止左洞口
            case GroupCliffRight: return new GroupCount(GroupCliffRight, 1, 1);  // 右外部洞口
            case GroupEndLeft: return new GroupCount(GroupEndLeft, 1, 1);
            case GroupEndRight: return new GroupCount(GroupEndRight, 1, 1);
            case GroupCliff: return new GroupCount(GroupCliff, 1, 1);
            case GroupBeach: return new GroupCount(GroupBeach, 1, 1);
            case GroupStartingAreaTight:
            case GroupForest:
            case GroupBridge:
            case GroupClearing:
            case GroupPlayerSpawn:
            case GroupChest:
            case GroupBeggarCamp:
                return template;                                                 // 普通基础区白名单：保留模板区间
            default:
                return new GroupCount(template.Group, 0, 0);                     // 其余全部归零（不继承神祇配额）
        }
    }

    /// <summary>
    /// 白名单组是否保留模板区间（供测试/日志核对；白名单来自 native-terrain §2 的普通基础区）。
    /// </summary>
    internal static bool IsWhitelistedGroup(int group)
        => group == GroupStartingAreaTight || group == GroupForest || group == GroupBridge
        || group == GroupClearing || group == GroupPlayerSpawn || group == GroupChest || group == GroupBeggarCamp;

    /// <summary>
    /// land11 的确定性接缝分配（新授予/重建共用，与付款/发现顺序无关）：
    /// 普通地形列表的合法内部接缝为 1..interiorCount（interiorCount = blockCount-1，不跨首尾端点），
    /// 第 k 个定义取 1 + floor(k * interiorCount / definitionCount)，按固定目录顺序均匀分散；
    /// 无随机、同 seed/同实际授予集重建一致。定义数超过接缝数时允许多定义共享同一接缝（由顺序保证稳定）。
    /// </summary>
    internal static int[] DistributeSeams(int interiorCount, int definitionCount)
    {
        var seams = new int[definitionCount < 0 ? 0 : definitionCount];
        if (interiorCount <= 0 || definitionCount <= 0) return seams;
        for (int k = 0; k < definitionCount; k++)
            seams[k] = 1 + (int)((long)k * interiorCount / definitionCount);
        return seams;
    }

    /// <summary>
    /// 可用性门（travel 桥）：scope 已确证（普通 Greek 离线 + owner 无误）且
    /// （Mod 总门 + CrossWorld 新授予开关）或当前就在 land11 或本战役访问过 land11。
    /// 开关关闭后不提供**新**导航，但当前/已访问岛保持恢复与返航。
    /// </summary>
    internal static bool AvailabilityGate(bool scopeProven, bool modAndFeatureEnabled,
        bool extensionCurrent, bool extensionVisited)
        => scopeProven && (modAndFeatureEnabled || extensionCurrent || extensionVisited);

    /// <summary>当前岛就是附加岛（恢复/返航判定）：scope 已确证且 campaign.CurrentLand==11（优先 campaign）。</summary>
    internal static bool CurrentGate(bool scopeProven, bool campaignCurrentIsExtension)
        => scopeProven && campaignCurrentIsExtension;
}
