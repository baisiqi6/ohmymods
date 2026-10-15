using System;
using System.Collections.Generic;

namespace KingdomEnhancedMod;

/// <summary>扩展岛**展示/恢复可发现性**判定结果（布尔授权 = <see cref="MountIslandAvailabilityPolicy.IsAvailable"/>）。</summary>
internal enum MountIslandAvailabilityOutcome
{
    NotExtensionLand = 0,
    ScopeUnproven = 1,
    /// <summary>关键证据未知（visited 读取失败 / raw slot 读取失败）→ 不放行（fail-closed）。</summary>
    UnknownEvidence = 2,
    /// <summary>当前岛正证恢复（current==land）。</summary>
    CurrentRecovery = 3,
    /// <summary>visited 正证恢复（不因买完/活动过期/开关 OFF 裁撤）。</summary>
    VisitedRecovery = 4,
    /// <summary>生成历史正证恢复（objects 非空 / played &gt; 0 等）：同样不随开关/活动裁撤。</summary>
    ExistingRecovery = 5,
    /// <summary>开关开启且同 campaign 快照下仍有 eligible 新目标 → 首次开放。</summary>
    FirstTimeEligible = 6,
    /// <summary>开关开启但已无 eligible 新目标（全已授予/活动不eligible）→ 不开放空新岛。</summary>
    NoEligibleRemaining = 7,
}

/// <summary>**实际旅行提交资格**判定结果（布尔授权 = <see cref="MountIslandAvailabilityPolicy.IsTravelAllowed"/>）。</summary>
internal enum MountIslandTravelOutcome
{
    NotExtensionLand = 0,
    ScopeUnproven = 1,
    /// <summary>visited/生成状态读取失败 → 不可出航（fail-closed）。</summary>
    UnknownEvidence = 2,
    /// <summary>当前就在该岛且存档槽在位。</summary>
    CurrentRecovery = 3,
    /// <summary>visited 且存档槽在位：可出航重建。</summary>
    VisitedRebuildable = 4,
    /// <summary>生成历史正证且存档槽在位：可出航重建。</summary>
    ExistingRebuildable = 5,
    /// <summary>开关开启且有 eligible 新目标：首次出航。</summary>
    FirstTimeEligible = 6,
    /// <summary>恢复身份正证（current/visited/生成历史）但 raw 存档槽缺失/待核：
    /// 展示可发现、**不可出航重建**（fail-closed；绝不用 GetIsland 补槽让本门变 true）。</summary>
    RecoveryDataMissing = 7,
    /// <summary>开关关闭或无 eligible 新目标：不出航。</summary>
    NoEligibleRemaining = 8,
}

/// <summary>
/// 纯决策（无 interop）：扩展岛展示可用性 + 实际旅行资格 + new-grant 候选资格 + 快照单元稳定性。
/// 输入全部由 production 从真实信号读取；未知一律 fail-closed。
/// 语义（root 二审修正）：
/// - 展示可用性与旅行提交资格**分开**：visited/current/生成正证岛保可发现，但 raw 存档槽缺失时
///   旅行门 false（RecoveryDataMissing），给出"存档槽待核，无法出航重建"诊断；
/// - current 正证恢复优先（visited 读取失败也保展示）；visited 未知/raw slot 读取失败 → 展示也关闭；
/// - 首次路径要求 feature 开启且存在 eligible 新目标（无 eligible 不开放空新岛）；
/// - `EnsureReady`（容量/config 服务就绪）不构成旅行放行；A/B 共用同一旅行门；原生 0..10/宫廷 12 不接。
/// </summary>
internal static class MountIslandAvailabilityPolicy
{
    internal static bool IsAvailable(MountIslandAvailabilityOutcome outcome)
        => outcome == MountIslandAvailabilityOutcome.CurrentRecovery
        || outcome == MountIslandAvailabilityOutcome.VisitedRecovery
        || outcome == MountIslandAvailabilityOutcome.ExistingRecovery
        || outcome == MountIslandAvailabilityOutcome.FirstTimeEligible;

    internal static bool IsTravelAllowed(MountIslandTravelOutcome outcome)
        => outcome == MountIslandTravelOutcome.CurrentRecovery
        || outcome == MountIslandTravelOutcome.VisitedRebuildable
        || outcome == MountIslandTravelOutcome.ExistingRebuildable
        || outcome == MountIslandTravelOutcome.FirstTimeEligible;

    /// <summary>展示/恢复可发现性（不含旅行提交资格）。</summary>
    internal static MountIslandAvailabilityOutcome Decide(
        int land, bool scopeProven, bool current, bool visitedKnown, bool visited,
        bool generatedEvidenceKnown, bool generatedHistory, bool featureEnabled, bool hasEligibleNewTarget)
    {
        if (!MountIslandSplitPolicy.IsExtensionLand(land)) return MountIslandAvailabilityOutcome.NotExtensionLand;
        if (!scopeProven) return MountIslandAvailabilityOutcome.ScopeUnproven;
        if (current) return MountIslandAvailabilityOutcome.CurrentRecovery;          // 正证恢复优先
        if (!visitedKnown) return MountIslandAvailabilityOutcome.UnknownEvidence;    // 未知不得经 feature 放行
        if (visited) return MountIslandAvailabilityOutcome.VisitedRecovery;
        if (!generatedEvidenceKnown) return MountIslandAvailabilityOutcome.UnknownEvidence;
        if (generatedHistory) return MountIslandAvailabilityOutcome.ExistingRecovery;
        if (featureEnabled && hasEligibleNewTarget) return MountIslandAvailabilityOutcome.FirstTimeEligible;
        return MountIslandAvailabilityOutcome.NoEligibleRemaining;
    }

    /// <summary>
    /// 实际旅行提交资格（Map 的 travel gate / EnsureReady 使用）：
    /// recovery 身份正证但 <paramref name="slotPresent"/> 为 false → RecoveryDataMissing（false）。
    /// </summary>
    internal static MountIslandTravelOutcome DecideTravel(
        int land, bool scopeProven, bool current, bool visitedKnown, bool visited,
        bool generatedEvidenceKnown, bool generatedHistory, bool slotPresent,
        bool featureEnabled, bool hasEligibleNewTarget)
    {
        if (!MountIslandSplitPolicy.IsExtensionLand(land)) return MountIslandTravelOutcome.NotExtensionLand;
        if (!scopeProven) return MountIslandTravelOutcome.ScopeUnproven;
        if (!visitedKnown || !generatedEvidenceKnown) return MountIslandTravelOutcome.UnknownEvidence;
        if (current)
        {
            return slotPresent
                ? MountIslandTravelOutcome.CurrentRecovery
                : MountIslandTravelOutcome.RecoveryDataMissing;
        }
        if (visited)
        {
            return slotPresent
                ? MountIslandTravelOutcome.VisitedRebuildable
                : MountIslandTravelOutcome.RecoveryDataMissing;
        }
        if (generatedHistory)
        {
            return slotPresent
                ? MountIslandTravelOutcome.ExistingRebuildable
                : MountIslandTravelOutcome.RecoveryDataMissing;
        }
        if (featureEnabled && hasEligibleNewTarget) return MountIslandTravelOutcome.FirstTimeEligible;
        return MountIslandTravelOutcome.NoEligibleRemaining;
    }

    internal static string Describe(MountIslandAvailabilityOutcome outcome, int land)
    {
        switch (outcome)
        {
            case MountIslandAvailabilityOutcome.NotExtensionLand: return "land " + land + " is not an extension land";
            case MountIslandAvailabilityOutcome.ScopeUnproven: return "campaign scope not proven";
            case MountIslandAvailabilityOutcome.UnknownEvidence: return "evidence unknown (visit/slot read failed); stays closed";
            case MountIslandAvailabilityOutcome.CurrentRecovery: return "current land " + land + " (recovery)";
            case MountIslandAvailabilityOutcome.VisitedRecovery: return "visited land " + land + " (recovery)";
            case MountIslandAvailabilityOutcome.ExistingRecovery: return "generation history on land " + land + " (recovery)";
            case MountIslandAvailabilityOutcome.FirstTimeEligible: return "first-time travel eligible for land " + land;
            default: return "no eligible new target for land " + land + " (closed)";
        }
    }

    internal static string DescribeTravel(MountIslandTravelOutcome outcome, int land)
    {
        switch (outcome)
        {
            case MountIslandTravelOutcome.NotExtensionLand: return "land " + land + " is not an extension land";
            case MountIslandTravelOutcome.ScopeUnproven: return "campaign scope not proven";
            case MountIslandTravelOutcome.UnknownEvidence: return "recovery data unknown (visit/slot read failed); travel closed";
            case MountIslandTravelOutcome.CurrentRecovery: return "current land " + land + " (recovery ok)";
            case MountIslandTravelOutcome.VisitedRebuildable: return "visited land " + land + " rebuildable";
            case MountIslandTravelOutcome.ExistingRebuildable: return "generation history on land " + land + " rebuildable";
            case MountIslandTravelOutcome.FirstTimeEligible: return "first-time travel to land " + land;
            case MountIslandTravelOutcome.RecoveryDataMissing:
                return "land " + land + " recovery identity proven but 存档槽待核，无法出航重建 (travel closed; no auto-create)";
            default: return "no eligible new target for land " + land + " (travel closed)";
        }
    }
}

/// <summary>单条 new-grant 候选（纯数据）：已授予（旧岛 marker 或任意 raw slot receipt）与活动资格。</summary>
internal readonly struct MountIslandNewGrantCandidate
{
    internal readonly string Id;
    internal readonly bool GrantedAnywhere;
    internal readonly bool SeasonEligible;

    internal MountIslandNewGrantCandidate(string id, bool grantedAnywhere, bool seasonEligible)
    {
        Id = id;
        GrantedAnywhere = grantedAnywhere;
        SeasonEligible = seasonEligible;
    }
}

/// <summary>new-grant 资格计数（纯）：至少一条"未授予 且 活动/普通资格成立"才允许首次开放。</summary>
internal static class MountIslandNewGrantEligibility
{
    internal static bool HasEligibleTarget(IReadOnlyList<MountIslandNewGrantCandidate> candidates)
    {
        if (candidates == null) return false;
        for (int i = 0; i < candidates.Count; i++)
        {
            MountIslandNewGrantCandidate candidate = candidates[i];
            if (!candidate.GrantedAnywhere && candidate.SeasonEligible) return true;
        }
        return false;
    }
}

/// <summary>
/// 快照身份匹配（纯比较，interop 读取在调用方）：捕获**前**记录四元组，捕获**后**重核
/// campaign/reign/landData/raw _islands 四容器身份；任一不一致或零值 → 不可信（unknown，拒 newgrant）。
/// </summary>
internal static class MountIslandSnapshotIdentity
{
    internal static bool Matches(
        ulong campaignBefore, int reignBefore, ulong landDataBefore, ulong islandsBefore,
        ulong campaignAfter, int reignAfter, ulong landDataAfter, ulong islandsAfter)
        => campaignBefore != 0UL && campaignBefore == campaignAfter
        && reignBefore >= 0 && reignBefore == reignAfter
        && landDataBefore != 0UL && landDataBefore == landDataAfter
        && islandsBefore != 0UL && islandsBefore == islandsAfter;

    /// <summary>
    /// **owner scope** 匹配（root R2）：Global.loaded 指针 + raw selected index + 捕获时的
    /// Campaign.CurrentLand + 四容器身份必须全部一致；selected 为负/未读/不一致一律 false。
    /// </summary>
    internal static bool MatchesOwnerScope(
        ulong globalBefore, int selectedBefore, int landBefore,
        ulong campaignBefore, int reignBefore, ulong landDataBefore, ulong islandsBefore,
        ulong globalAfter, int selectedAfter, int landAfter,
        ulong campaignAfter, int reignAfter, ulong landDataAfter, ulong islandsAfter)
        => globalBefore != 0UL && globalBefore == globalAfter
        && selectedBefore >= 0 && selectedBefore == selectedAfter
        && landBefore >= 0 && landBefore == landAfter
        && Matches(campaignBefore, reignBefore, landDataBefore, islandsBefore,
            campaignAfter, reignAfter, landDataAfter, islandsAfter);
}

/// <summary>
/// newgrant 授权**事实**的稳定性比较（纯；root R1：不深 hash record，只比较影响授权的 payload）：
/// - <see cref="SameReceiptFacts"/>：16 定义全 campaign receipt bool 集合（键集 + 值全等）；
/// - <see cref="SameIntSequence"/>：landData 各 entry 的 steedSpawns 实际 int 序列（非仅 ptr/length）；
/// - <see cref="AllowsGrantPublish"/>：发布前终核门（authority 内容 + raw owner 都当前才允许
///   newgrant 计划随 result 发布；否则本帧只按 InjectOnly 重建）。
/// </summary>
internal static class MountIslandGrantFacts
{
    internal static bool SameReceiptFacts(
        IReadOnlyDictionary<string, bool> before, IReadOnlyDictionary<string, bool> after)
    {
        if (before == null || after == null) return false;
        if (before.Count != after.Count) return false;
        foreach (KeyValuePair<string, bool> pair in before)
        {
            if (!after.TryGetValue(pair.Key, out bool value)) return false;
            if (value != pair.Value) return false;
        }
        return true;
    }

    internal static bool SameIntSequence(int[] before, int[] after)
    {
        if (before == null || after == null) return before == after;
        if (before.Length != after.Length) return false;
        for (int i = 0; i < before.Length; i++)
        {
            if (before[i] != after[i]) return false;
        }
        return true;
    }

    internal static bool AllowsGrantPublish(bool authorityCurrent, bool rawOwnerSame)
        => authorityCurrent && rawOwnerSame;
}

/// <summary>一个 raw _islands 槽的稳定性指纹（island 对象 / objects 列表指针 / count）。</summary>
internal readonly struct MountIslandCellFingerprint
{
    internal readonly ulong IslandPointer;
    internal readonly ulong ObjectsPointer;
    internal readonly int ObjectsCount;

    internal MountIslandCellFingerprint(ulong islandPointer, ulong objectsPointer, int objectsCount)
    {
        IslandPointer = islandPointer;
        ObjectsPointer = objectsPointer;
        ObjectsCount = objectsCount;
    }
}

/// <summary>
/// 一个 landData entry 的稳定性指纹（只绑 marker 数组引用 + 长度；entry 为值类型，每次读取的
/// boxed wrapper 指针不同，不可作为证据）。
/// </summary>
internal readonly struct MountIslandLandFingerprint
{
    internal readonly ulong MarkerArrayPointer;
    internal readonly int MarkerLength;

    internal MountIslandLandFingerprint(ulong markerArrayPointer, int markerLength)
    {
        MarkerArrayPointer = markerArrayPointer;
        MarkerLength = markerLength;
    }
}

/// <summary>
/// 快照单元稳定性比较（纯）：扫描前捕获的逐槽/逐 landData entry 指纹与扫描后（或 eligibility
/// 扫描后）重读值逐项相等才可信；长度不同、任一变化、零值指针 → false（closed）。
/// </summary>
internal static class MountIslandCellStability
{
    internal static bool Matches(
        IReadOnlyList<MountIslandCellFingerprint> cellsBefore,
        IReadOnlyList<MountIslandCellFingerprint> cellsAfter,
        IReadOnlyList<MountIslandLandFingerprint> landsBefore,
        IReadOnlyList<MountIslandLandFingerprint> landsAfter)
    {
        if (cellsBefore == null || cellsAfter == null || landsBefore == null || landsAfter == null) return false;
        if (cellsBefore.Count != cellsAfter.Count || landsBefore.Count != landsAfter.Count) return false;
        for (int i = 0; i < cellsBefore.Count; i++)
        {
            MountIslandCellFingerprint before = cellsBefore[i];
            MountIslandCellFingerprint after = cellsAfter[i];
            if (before.IslandPointer != after.IslandPointer) return false;
            if (before.ObjectsPointer != after.ObjectsPointer) return false;
            if (before.ObjectsCount != after.ObjectsCount) return false;
        }
        for (int i = 0; i < landsBefore.Count; i++)
        {
            MountIslandLandFingerprint before = landsBefore[i];
            MountIslandLandFingerprint after = landsAfter[i];
            if (before.MarkerArrayPointer != after.MarkerArrayPointer) return false;
            if (before.MarkerLength != after.MarkerLength) return false;
        }
        return true;
    }
}
