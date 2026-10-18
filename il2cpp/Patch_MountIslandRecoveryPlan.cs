using System;
using System.Collections.Generic;
using System.Text.Json;

namespace KingdomEnhancedMod;

/// <summary>
/// 旧空坐骑岛恢复的纯策略/事务编排（无 Unity/Il2Cpp/Harmony 依赖，可离线单测）。
///
/// 边界：
/// - 资格按定义独立分类；scope（owner/slot/Level/seed/容器）异常由调用方整批中止，
///   本文件只处理已进入同一 scope 的每定义证据。
/// - 新建只在本地 marker/receipt/live 全无、他岛无 grant、split 目标匹配、活动门与依赖
///   ready 时成立；live 重复/缺损/unknown 只拒该定义臂，不伪造阴性。
/// - 放置先做全组区间规划（合格 Tile 并集 - 所有禁区），全组任一设施无空间即停 create
///   batch；marker-only 不依赖空间，但整次事务只在 create batch 成功后才提交 marker。
/// - marker 一次构造目标 land 的完整数组、一次回写；preimage 与 postimage 的判定由纯函数
///   完成，调用方负责把 Native setter 异常转成 readback 证据。
/// </summary>
internal enum MountIslandRecoveryLiveState
{
    Unknown = 0,
    Missing = 1,
    Single = 2,
    Duplicate = 3,
}

internal enum MountIslandRecoveryDecision
{
    Skip = 0,
    MarkerOnly = 1,
    Create = 2,
    Reject = 3,
}

internal readonly struct MountIslandRecoveryEvidence
{
    internal readonly string DefinitionId;
    internal readonly int SteedTypeId;
    internal readonly bool TargetLand;
    internal readonly bool MarkerOnIsland;
    /// <summary>读档前 raw _islands[land].objects 中已有本定义真实回执（不是 live 观测）。</summary>
    internal readonly bool ReceiptOnIsland;
    /// <summary>其他岛的真实 marker 或 receipt（本岛不参与）。</summary>
    internal readonly bool GrantedOtherIsland;
    internal readonly MountIslandRecoveryLiveState Live;
    /// <summary>Single 时身份/完整性/同 scene 校验通过。</summary>
    internal readonly bool LiveComplete;
    internal readonly bool DependencyReady;
    internal readonly bool SeasonalEligible;
    /// <summary>campaign 级 receipt 证据完整可读；false 时不得新建。</summary>
    internal readonly bool CampaignEvidenceReadable;
    /// <summary>本定义 receipt 读取异常（unknown，不能当 0）。</summary>
    internal readonly bool ReceiptUnknown;

    internal MountIslandRecoveryEvidence(string definitionId, int steedTypeId, bool targetLand,
        bool markerOnIsland, bool receiptOnIsland, bool grantedOtherIsland,
        MountIslandRecoveryLiveState live, bool liveComplete,
        bool dependencyReady, bool seasonalEligible,
        bool campaignEvidenceReadable, bool receiptUnknown)
    {
        DefinitionId = definitionId;
        SteedTypeId = steedTypeId;
        TargetLand = targetLand;
        MarkerOnIsland = markerOnIsland;
        ReceiptOnIsland = receiptOnIsland;
        GrantedOtherIsland = grantedOtherIsland;
        Live = live;
        LiveComplete = liveComplete;
        DependencyReady = dependencyReady;
        SeasonalEligible = seasonalEligible;
        CampaignEvidenceReadable = campaignEvidenceReadable;
        ReceiptUnknown = receiptUnknown;
    }
}

internal readonly struct MountIslandRecoveryArm
{
    internal readonly string DefinitionId;
    internal readonly int SteedTypeId;
    internal readonly MountIslandRecoveryDecision Decision;
    internal readonly string Reason;

    internal MountIslandRecoveryArm(string definitionId, int steedTypeId,
        MountIslandRecoveryDecision decision, string reason)
    {
        DefinitionId = definitionId;
        SteedTypeId = steedTypeId;
        Decision = decision;
        Reason = reason;
    }
}

internal sealed class MountIslandRecoveryPlan
{
    internal readonly List<MountIslandRecoveryArm> Arms = new List<MountIslandRecoveryArm>();
    internal readonly List<MountIslandRecoveryArm> Creates = new List<MountIslandRecoveryArm>();
    internal readonly List<MountIslandRecoveryArm> MarkerOnly = new List<MountIslandRecoveryArm>();
    internal readonly List<string> Diagnostics = new List<string>();

    internal bool HasMarkerWork => Creates.Count > 0 || MarkerOnly.Count > 0;

    internal void Add(MountIslandRecoveryArm arm)
    {
        Arms.Add(arm);
        if (arm.Decision == MountIslandRecoveryDecision.Create) Creates.Add(arm);
        else if (arm.Decision == MountIslandRecoveryDecision.MarkerOnly) MarkerOnly.Add(arm);
        else if (arm.Decision == MountIslandRecoveryDecision.Reject)
            Diagnostics.Add(arm.DefinitionId + ": " + arm.Reason);
    }
}

internal static class MountIslandRecoveryPolicy
{
    /// <summary>
    /// 逐定义分类。顺序即优先级：
    /// 1) live unknown/duplicate/缺损 → Reject（只拒该臂）；
    /// 2) live 完整：已有 marker → Skip（零写），marker 缺 → MarkerOnly；
    /// 3) live 缺且本岛 marker/receipt 已存在 → Skip（不重建，可能已消费/隐藏）；
    /// 4) 他岛已 grant → Skip；
    /// 5) campaign receipt unknown / 不可读 → Reject（不得新授予）；
    /// 6) split 不匹配、依赖未 ready、活动不 eligible → Skip（不新授予）；
    /// 7) 其余 → Create。
    /// </summary>
    internal static MountIslandRecoveryPlan Build(IReadOnlyList<MountIslandRecoveryEvidence> evidence)
    {
        var plan = new MountIslandRecoveryPlan();
        if (evidence == null) return plan;
        for (int i = 0; i < evidence.Count; i++)
        {
            MountIslandRecoveryEvidence item = evidence[i];
            if (string.IsNullOrEmpty(item.DefinitionId))
            {
                plan.Diagnostics.Add("empty definition id");
                continue;
            }

            if (item.Live == MountIslandRecoveryLiveState.Unknown)
            {
                plan.Add(Arm(item, MountIslandRecoveryDecision.Reject, "live evidence unknown"));
                continue;
            }
            if (item.Live == MountIslandRecoveryLiveState.Duplicate)
            {
                plan.Add(Arm(item, MountIslandRecoveryDecision.Reject, "duplicate live facility"));
                continue;
            }
            if (item.Live == MountIslandRecoveryLiveState.Single)
            {
                if (!item.LiveComplete)
                {
                    plan.Add(Arm(item, MountIslandRecoveryDecision.Reject, "incomplete live facility"));
                    continue;
                }
                if (item.MarkerOnIsland)
                {
                    plan.Add(Arm(item, MountIslandRecoveryDecision.Skip, "live and marker present"));
                    continue;
                }
                plan.Add(Arm(item, MountIslandRecoveryDecision.MarkerOnly, "live present; marker missing"));
                continue;
            }

            // Live == Missing.
            if (item.MarkerOnIsland || item.ReceiptOnIsland)
            {
                plan.Add(Arm(item, MountIslandRecoveryDecision.Skip,
                    "marker/receipt present but live missing; not rebuilt"));
                continue;
            }
            if (item.GrantedOtherIsland)
            {
                plan.Add(Arm(item, MountIslandRecoveryDecision.Skip, "granted on another island"));
                continue;
            }
            if (!item.CampaignEvidenceReadable || item.ReceiptUnknown)
            {
                plan.Add(Arm(item, MountIslandRecoveryDecision.Reject,
                    item.ReceiptUnknown ? "receipt evidence unknown" : "campaign receipt evidence unreadable"));
                continue;
            }
            if (!item.TargetLand)
            {
                plan.Add(Arm(item, MountIslandRecoveryDecision.Skip, "not this split target"));
                continue;
            }
            if (!item.DependencyReady)
            {
                plan.Add(Arm(item, MountIslandRecoveryDecision.Skip, "native dependency not ready"));
                continue;
            }
            if (!item.SeasonalEligible)
            {
                plan.Add(Arm(item, MountIslandRecoveryDecision.Skip, "seasonal new-grant gate closed"));
                continue;
            }
            plan.Add(Arm(item, MountIslandRecoveryDecision.Create, "no local evidence; new grant allowed"));
        }
        return plan;
    }

    private static MountIslandRecoveryArm Arm(in MountIslandRecoveryEvidence item,
        MountIslandRecoveryDecision decision, string reason)
        => new MountIslandRecoveryArm(item.DefinitionId, item.SteedTypeId, decision, reason);

    /// <summary>Harmony 原方法完成判定：skipped/false/throw 均不得 ready/consume。</summary>
    internal static bool IsOriginalCompletion(bool runOriginal, bool result, bool threw)
        => runOriginal && result && !threw;

    /// <summary>
    /// 私有模板 facility 父节点判定：必须解析到唯一准确 GameLayer（ContentLayers.GameLayer），
    /// 且 facility 是该层直接子节点。缺失/非唯一/错层/任意更深 descendant 全部拒绝。
    /// </summary>
    internal static bool IsTemplateFacilityParentValid(bool gameLayerResolved, int layerCandidates,
        bool facilityIsDirectChild)
        => gameLayerResolved && layerCandidates == 1 && facilityIsDirectChild;
}

/// <summary>闭区间 [Min, Max]，world X；只做水平放置。</summary>
internal readonly struct MountIslandRecoveryInterval
{
    internal readonly float Min;
    internal readonly float Max;

    internal MountIslandRecoveryInterval(float min, float max)
    {
        Min = min;
        Max = max;
    }

    internal float Width => Max - Min;
}

internal readonly struct MountIslandRecoveryPlacementRequest
{
    internal readonly string DefinitionId;
    internal readonly float HalfWidth;

    internal MountIslandRecoveryPlacementRequest(string definitionId, float halfWidth)
    {
        DefinitionId = definitionId;
        HalfWidth = halfWidth;
    }
}

internal readonly struct MountIslandRecoveryPlacement
{
    internal readonly string DefinitionId;
    internal readonly float RootX;
    internal readonly float HalfWidth;

    internal MountIslandRecoveryPlacement(string definitionId, float rootX, float halfWidth)
    {
        DefinitionId = definitionId;
        RootX = rootX;
        HalfWidth = halfWidth;
    }
}

internal static class MountIslandRecoveryPlacementPolicy
{
    internal const float Epsilon = 1e-4f;
    private const int MaxSnapGuard = 8192;

    /// <summary>
    /// 全组从左到右放置：合格 Tile 并集 ∩ LevelEdges，再扣全部禁区；每个设施整个 reserved
    /// 区间必须落在同一段连续可用区间内。snapX 必须是生产同源 Native snap（测试用 fake）。
    /// probeCenter 补高度/Obstacles 证据，不能替代连续覆盖。任一设施失败 → 全组无 placement。
    /// </summary>
    internal static bool TryPlan(
        IReadOnlyList<MountIslandRecoveryPlacementRequest> requests,
        IReadOnlyList<MountIslandRecoveryInterval> tileCoverage,
        IReadOnlyList<MountIslandRecoveryInterval> exclusions,
        float levelMin,
        float levelMax,
        Func<float, float> snapX,
        Func<float, float, bool> probeCenter,
        out MountIslandRecoveryPlacement[] placements,
        out string reason)
    {
        placements = Array.Empty<MountIslandRecoveryPlacement>();
        reason = string.Empty;
        if (requests == null || snapX == null || probeCenter == null)
        {
            reason = "placement arguments missing";
            return false;
        }
        if (!float.IsFinite(levelMin) || !float.IsFinite(levelMax) || levelMax - levelMin <= Epsilon)
        {
            reason = "level edges invalid";
            return false;
        }

        List<MountIslandRecoveryInterval> free = Normalize(tileCoverage);
        free = Intersect(free, new MountIslandRecoveryInterval(levelMin, levelMax));
        free = Subtract(free, Normalize(exclusions));
        if (free.Count == 0)
        {
            reason = "no eligible surface after exclusions";
            return false;
        }

        var result = new List<MountIslandRecoveryPlacement>(requests.Count);
        for (int i = 0; i < requests.Count; i++)
        {
            MountIslandRecoveryPlacementRequest request = requests[i];
            if (string.IsNullOrEmpty(request.DefinitionId)
                || !float.IsFinite(request.HalfWidth) || request.HalfWidth <= 0f)
            {
                reason = "invalid placement request";
                return false;
            }

            bool placed = false;
            float chosen = 0f;
            for (int f = 0; f < free.Count && !placed; f++)
            {
                MountIslandRecoveryInterval interval = free[f];
                if (interval.Width + Epsilon < request.HalfWidth * 2f) continue;

                float x = interval.Min + request.HalfWidth;
                for (int guard = 0; guard < MaxSnapGuard; guard++)
                {
                    float snapped = snapX(x);
                    if (!float.IsFinite(snapped)) break;
                    float left = snapped - request.HalfWidth;
                    float right = snapped + request.HalfWidth;
                    if (left >= interval.Min - Epsilon && right <= interval.Max + Epsilon)
                    {
                        if (probeCenter(snapped, request.HalfWidth))
                        {
                            chosen = snapped;
                            placed = true;
                        }
                        else
                        {
                            x = snapped + request.HalfWidth + 0.25f;
                        }
                        if (placed) break;
                        if (x > interval.Max) break;
                        continue;
                    }
                    if (left < interval.Min - Epsilon)
                    {
                        float needed = interval.Min + request.HalfWidth;
                        if (x < needed - Epsilon) x = needed;
                        else x += 0.25f;   // snap 向下取整时保证前进，避免固定点循环
                        if (x + request.HalfWidth > interval.Max + Epsilon) break;
                        continue;
                    }
                    break; // 已越过本段右边界
                }
            }

            if (!placed)
            {
                reason = "no space for " + request.DefinitionId;
                return false;
            }

            result.Add(new MountIslandRecoveryPlacement(request.DefinitionId, chosen, request.HalfWidth));
            free = Subtract(free, new[]
            {
                new MountIslandRecoveryInterval(chosen - request.HalfWidth, chosen + request.HalfWidth),
            });
        }

        placements = result.ToArray();
        return true;
    }

    internal static List<MountIslandRecoveryInterval> Normalize(
        IReadOnlyList<MountIslandRecoveryInterval> intervals)
    {
        var result = new List<MountIslandRecoveryInterval>();
        if (intervals == null) return result;
        var sorted = new List<MountIslandRecoveryInterval>(intervals.Count);
        for (int i = 0; i < intervals.Count; i++)
        {
            MountIslandRecoveryInterval interval = intervals[i];
            if (!float.IsFinite(interval.Min) || !float.IsFinite(interval.Max)) continue;
            if (interval.Max - interval.Min <= Epsilon) continue;
            sorted.Add(new MountIslandRecoveryInterval(
                Math.Min(interval.Min, interval.Max), Math.Max(interval.Min, interval.Max)));
        }
        sorted.Sort((a, b) => a.Min.CompareTo(b.Min));
        for (int i = 0; i < sorted.Count; i++)
        {
            MountIslandRecoveryInterval next = sorted[i];
            if (result.Count == 0 || next.Min > result[result.Count - 1].Max + Epsilon)
            {
                result.Add(next);
                continue;
            }
            MountIslandRecoveryInterval last = result[result.Count - 1];
            if (next.Max > last.Max)
                result[result.Count - 1] = new MountIslandRecoveryInterval(last.Min, next.Max);
        }
        return result;
    }

    internal static List<MountIslandRecoveryInterval> Intersect(
        IReadOnlyList<MountIslandRecoveryInterval> intervals, MountIslandRecoveryInterval clip)
    {
        var result = new List<MountIslandRecoveryInterval>();
        if (intervals == null) return result;
        for (int i = 0; i < intervals.Count; i++)
        {
            MountIslandRecoveryInterval current = intervals[i];
            float min = Math.Max(current.Min, clip.Min);
            float max = Math.Min(current.Max, clip.Max);
            if (max - min > Epsilon) result.Add(new MountIslandRecoveryInterval(min, max));
        }
        return result;
    }

    internal static List<MountIslandRecoveryInterval> Subtract(
        IReadOnlyList<MountIslandRecoveryInterval> source, IReadOnlyList<MountIslandRecoveryInterval> cuts)
    {
        var result = new List<MountIslandRecoveryInterval>();
        if (source == null) return result;
        for (int i = 0; i < source.Count; i++)
        {
            MountIslandRecoveryInterval current = source[i];
            var pieces = new List<MountIslandRecoveryInterval> { current };
            if (cuts != null)
            {
                for (int c = 0; c < cuts.Count; c++)
                {
                    MountIslandRecoveryInterval cut = cuts[c];
                    var next = new List<MountIslandRecoveryInterval>(pieces.Count + 1);
                    for (int p = 0; p < pieces.Count; p++)
                    {
                        MountIslandRecoveryInterval piece = pieces[p];
                        if (cut.Max <= piece.Min + Epsilon || cut.Min >= piece.Max - Epsilon)
                        {
                            next.Add(piece);
                            continue;
                        }
                        if (cut.Min > piece.Min + Epsilon)
                            next.Add(new MountIslandRecoveryInterval(piece.Min, Math.Min(cut.Min, piece.Max)));
                        if (cut.Max < piece.Max - Epsilon)
                            next.Add(new MountIslandRecoveryInterval(Math.Max(cut.Max, piece.Min), piece.Max));
                    }
                    pieces = next;
                }
            }
            result.AddRange(pieces);
        }
        return Normalize(result);
    }
}

internal enum MountIslandMarkerCommitOutcome
{
    NotCommitted = 0,
    Committed = 1,
    Unknown = 2,
}

/// <summary>
/// marker 数组策略与“setter 后 readback”判定。preimage/postimage 都是整个 landData 的
/// 逐 land 快照（null = 该 entry 无数组）；expected 只改目标 land，其他 land 原样保留。
/// </summary>
internal static class MountIslandMarkerBatch
{
    internal static int[] AppendAll(int[] existing, IReadOnlyList<int> additions)
    {
        var result = new List<int>(existing != null ? existing.Length + (additions?.Count ?? 0) : (additions?.Count ?? 0));
        if (existing != null) result.AddRange(existing);
        if (additions != null)
        {
            for (int i = 0; i < additions.Count; i++)
            {
                int value = additions[i];
                if (!result.Contains(value)) result.Add(value);
            }
        }
        return result.ToArray();
    }

    internal static bool SameSequence(int[] left, int[] right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left == null || right == null) return false;
        if (left.Length != right.Length) return false;
        for (int i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i]) return false;
        }
        return true;
    }

    internal static int[][] ClonePreimage(int[][] preimage)
    {
        if (preimage == null) return null;
        var clone = new int[preimage.Length][];
        for (int i = 0; i < preimage.Length; i++)
            clone[i] = preimage[i] != null ? (int[])preimage[i].Clone() : null;
        return clone;
    }

    private static bool SameAll(int[][] left, int[][] right)
    {
        if (left == null || right == null || left.Length != right.Length) return false;
        for (int i = 0; i < left.Length; i++)
        {
            if (!SameSequence(left[i], right[i])) return false;
        }
        return true;
    }

    /// <summary>
    /// 单次提交：read 返回整个 landData 的当前 marker 快照（读取失败返回 null）；write 只写
    /// 目标 land 并负责 owner 终检。任何 setter 异常都进入 readback 判定，不以异常本身断言
    /// committed/not committed。返回 Unknown 时调用方必须保留实体与证据，不盲目重试。
    /// </summary>
    internal static MountIslandMarkerCommitOutcome Commit(
        int[][] preimage,
        int land,
        IReadOnlyList<int> additions,
        Func<int[][]> read,
        Action<int, int[]> write,
        out int[][] postimage,
        out string reason)
    {
        postimage = null;
        reason = string.Empty;
        if (preimage == null || land < 0 || land >= preimage.Length)
        {
            reason = "marker preimage/land invalid";
            return MountIslandMarkerCommitOutcome.NotCommitted;
        }
        if (read == null || write == null)
        {
            reason = "marker read/write missing";
            return MountIslandMarkerCommitOutcome.NotCommitted;
        }

        int[][] current;
        try { current = read(); }
        catch (Exception e) { current = null; reason = "marker pre-read threw " + e.GetType().Name; }
        if (current == null || !SameAll(preimage, current))
        {
            reason = string.IsNullOrEmpty(reason) ? "marker preimage changed" : reason;
            return MountIslandMarkerCommitOutcome.NotCommitted;
        }

        int[][] expected = ClonePreimage(preimage);
        expected[land] = AppendAll(expected[land], additions);
        if (SameAll(expected, preimage))
        {
            postimage = current;
            reason = "markers already complete";
            return MountIslandMarkerCommitOutcome.Committed;
        }

        Exception writeError = null;
        try { write(land, expected[land]); }
        catch (Exception e) { writeError = e; }

        int[][] after;
        try { after = read(); }
        catch (Exception e)
        {
            after = null;
            if (writeError == null) reason = "marker readback threw " + e.GetType().Name;
        }
        if (after == null)
        {
            reason = string.IsNullOrEmpty(reason) ? "marker readback unavailable" : reason;
            return MountIslandMarkerCommitOutcome.Unknown;
        }

        if (SameAll(expected, after))
        {
            postimage = after;
            reason = writeError == null
                ? "markers committed"
                : "markers committed (setter threw " + writeError.GetType().Name + "; readback = postimage)";
            return MountIslandMarkerCommitOutcome.Committed;
        }
        if (SameAll(preimage, after))
        {
            postimage = after;
            reason = writeError == null
                ? "marker write did not change data"
                : "markers not committed (setter threw " + writeError.GetType().Name + "; readback = preimage)";
            return MountIslandMarkerCommitOutcome.NotCommitted;
        }
        postimage = after;
        reason = "marker readback matches neither preimage nor postimage";
        return MountIslandMarkerCommitOutcome.Unknown;
    }
}

/// <summary>raw _islands 槽的指纹（null slot / 无 objects 用 0/-1 表示）。</summary>
internal readonly struct MountIslandRecoveryCell
{
    internal readonly ulong IslandPointer;
    internal readonly ulong ObjectsPointer;
    internal readonly int ObjectsCount;

    internal MountIslandRecoveryCell(ulong islandPointer, ulong objectsPointer, int objectsCount)
    {
        IslandPointer = islandPointer;
        ObjectsPointer = objectsPointer;
        ObjectsCount = objectsCount;
    }
}

/// <summary>
/// TryPop 单槽 token：Begin 总是替换旧 token；旧 postfix/旧消费不能再 ready 或取用。
/// Payload 由运行期放入（最多一个已持有的 IslandSaveData 引用），纯测试可放任意对象。
/// </summary>
internal sealed class MountIslandRecoveryToken
{
    internal readonly long Stamp;
    internal readonly object Payload;
    internal bool Ready;

    internal MountIslandRecoveryToken(long stamp, object payload)
    {
        Stamp = stamp;
        Payload = payload;
    }
}

internal sealed class MountIslandRecoveryTokenSlot
{
    private long _nextStamp;
    private MountIslandRecoveryToken _current;

    internal MountIslandRecoveryToken Current => _current;

    internal MountIslandRecoveryToken Begin(object payload)
    {
        _nextStamp++;
        _current = new MountIslandRecoveryToken(_nextStamp, payload);
        return _current;
    }

    internal void Clear() { _current = null; }

    internal bool PublishReady(MountIslandRecoveryToken token)
    {
        if (token == null || !ReferenceEquals(_current, token)) return false;
        token.Ready = true;
        return true;
    }

    internal void Cancel(MountIslandRecoveryToken token)
    {
        if (token != null && ReferenceEquals(_current, token)) _current = null;
    }

    /// <summary>先取走并清槽；消费方之后的任何验证失败都不能再拿回同一 token。</summary>
    internal MountIslandRecoveryToken Take()
    {
        MountIslandRecoveryToken token = _current;
        _current = null;
        return token;
    }
}

/// <summary>捕获时与消费时必须完全一致的 scope 身份（不含会被原生 pop 清空的 objects）。</summary>
internal readonly struct MountIslandRecoveryScope
{
    internal readonly ulong GlobalPointer;
    internal readonly ulong CampaignPointer;
    internal readonly int SelectedIndex;
    internal readonly int ReignIndex;
    internal readonly ulong LandDataPointer;
    internal readonly ulong IslandsPointer;
    internal readonly int Land;
    internal readonly ulong LevelPointer;
    internal readonly int LevelSceneHandle;
    internal readonly string LevelSceneName;
    internal readonly int Seed;
    internal readonly ulong GameLayerPointer;
    internal readonly int GameLayerSceneHandle;
    internal readonly ulong HolderPointer;
    internal readonly int HolderBiomeIndex;
    internal readonly ulong IslandPointer;
    internal readonly int IslandLand;

    internal MountIslandRecoveryScope(ulong globalPointer, ulong campaignPointer, int selectedIndex, int reignIndex,
        ulong landDataPointer, ulong islandsPointer, int land, ulong levelPointer,
        int levelSceneHandle, string levelSceneName, int seed,
        ulong gameLayerPointer, int gameLayerSceneHandle,
        ulong holderPointer, int holderBiomeIndex, ulong islandPointer, int islandLand)
    {
        GlobalPointer = globalPointer;
        CampaignPointer = campaignPointer;
        SelectedIndex = selectedIndex;
        ReignIndex = reignIndex;
        LandDataPointer = landDataPointer;
        IslandsPointer = islandsPointer;
        Land = land;
        LevelPointer = levelPointer;
        LevelSceneHandle = levelSceneHandle;
        LevelSceneName = levelSceneName;
        Seed = seed;
        GameLayerPointer = gameLayerPointer;
        GameLayerSceneHandle = gameLayerSceneHandle;
        HolderPointer = holderPointer;
        HolderBiomeIndex = holderBiomeIndex;
        IslandPointer = islandPointer;
        IslandLand = islandLand;
    }

    internal static bool SameLoad(in MountIslandRecoveryScope a, in MountIslandRecoveryScope b)
        => SameLoadExceptSeed(a, b) && a.Seed == b.Seed;

    /// <summary>除 seed 外的完整 load 身份；用于允许一次合法的 WorldData→cached seed handoff。</summary>
    internal static bool SameLoadExceptSeed(in MountIslandRecoveryScope a, in MountIslandRecoveryScope b)
    {
        return a.GlobalPointer == b.GlobalPointer
            && a.CampaignPointer == b.CampaignPointer
            && a.SelectedIndex == b.SelectedIndex
            && a.ReignIndex == b.ReignIndex
            && a.LandDataPointer == b.LandDataPointer
            && a.IslandsPointer == b.IslandsPointer
            && a.Land == b.Land
            && a.LevelPointer == b.LevelPointer
            && a.LevelSceneHandle == b.LevelSceneHandle
            && string.Equals(a.LevelSceneName, b.LevelSceneName, StringComparison.Ordinal)
            && a.GameLayerPointer == b.GameLayerPointer
            && a.GameLayerSceneHandle == b.GameLayerSceneHandle
            && a.HolderPointer == b.HolderPointer
            && a.HolderBiomeIndex == b.HolderBiomeIndex
            && a.IslandPointer == b.IslandPointer
            && a.IslandLand == b.IslandLand;
    }

    /// <summary>
    /// 当前 ARM World.Persistent.ApplyData 会从保存 WorldData 读取目标 seed 并调用
    /// Level.OverwriteCachedLevelSeed。post 必须等于保存目标 seed（含 pre==post==savedTarget）；
    /// pre==post!=savedTarget 也拒绝，因为那说明原生合法 handoff 没有发生。savedTarget 必须来自
    /// 数据侧唯一准确 World 记录，不能用 cached getter 或 legacy 键代替。
    /// </summary>
    internal static bool IsAllowedSeedTransition(int preSeed, int postSeed, int savedTargetSeed)
        => postSeed == savedTargetSeed;

    /// <summary>
    /// 原生成功 pop 在返回前把 island.objects 写成 null（当前 ARM 0x88eed0..d8）。消费只允许
    /// 这一种预期清空；非 null（含空 list）说明不是同一次成功 pop 的终态，必须中止。
    /// </summary>
    internal static bool ConsumedObjectsClear(bool currentObjectsNull) => currentObjectsNull;
}

/// <summary>
/// 保存 WorldData.data 的 seed 字段解析（纯函数）。当前 2.4 序列化键为 cachedSeed；
/// 仅接受唯一顶层 cachedSeed 整数；legacy seed、重复、缺失或非法 JSON 一律 unknown。
/// </summary>
internal static class MountIslandRecoverySeedData
{
    internal static bool TryParse(string data, out int seed, out string reason)
    {
        seed = 0;
        reason = string.Empty;
        if (string.IsNullOrEmpty(data)) { reason = "WorldData JSON empty"; return false; }
        try
        {
            using (JsonDocument document = JsonDocument.Parse(data))
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    reason = "WorldData JSON root is not object";
                    return false;
                }
                int matches = 0;
                JsonElement value = default;
                foreach (JsonProperty property in document.RootElement.EnumerateObject())
                {
                    if (property.NameEquals("seed"))
                    {
                        reason = "legacy seed key is not accepted";
                        return false;
                    }
                    if (!property.NameEquals("cachedSeed")) continue;
                    matches++;
                    value = property.Value;
                }
                if (matches != 1)
                {
                    reason = matches == 0 ? "top-level cachedSeed missing" : "top-level cachedSeed duplicated";
                    return false;
                }
                if (value.ValueKind != JsonValueKind.Number)
                {
                    reason = "cachedSeed is not a JSON number";
                    return false;
                }
                if (!value.TryGetInt32(out seed))
                {
                    seed = 0;
                    reason = "cachedSeed is not int32";
                    return false;
                }
                return true;
            }
        }
        catch (JsonException)
        {
            seed = 0;
            reason = "WorldData JSON invalid/truncated";
            return false;
        }
        catch (Exception e)
        {
            seed = 0;
            reason = "WorldData JSON unknown: " + e.GetType().Name;
            return false;
        }
    }
}

internal enum MountIslandRecoveryExecutionOutcome
{
    Noop = 0,
    Committed = 1,
    CreateFailed = 2,
    ValidateFailed = 3,
    ActivateFailed = 4,
    MarkerNotCommitted = 5,
    MarkerUnknown = 6,
    InvalidPlan = 7,
}

internal sealed class MountIslandRecoveryExecutionResult
{
    internal readonly MountIslandRecoveryExecutionOutcome Outcome;
    internal readonly string Reason;
    internal readonly int CreatedCount;
    internal readonly MountIslandMarkerCommitOutcome MarkerOutcome;

    internal MountIslandRecoveryExecutionResult(MountIslandRecoveryExecutionOutcome outcome,
        string reason, int createdCount, MountIslandMarkerCommitOutcome markerOutcome)
    {
        Outcome = outcome;
        Reason = reason;
        CreatedCount = createdCount;
        MarkerOutcome = markerOutcome;
    }
}

/// <summary>
/// 运行期事务的最小 side-effect 接口：生产实现负责 Unity 克隆/校验/激活/清理与 Native marker
/// 提交；host 测试用 fake 驱动同一生产编排。
/// </summary>
internal interface IMountIslandRecoveryHost
{
    bool TryCreateInactive(MountIslandRecoveryArm arm, float rootX, out int handle, out string reason);
    bool TryValidateCreatedBatch(out string reason);
    bool TryActivateCreatedBatch(out string reason);
    void DiscardCreatedBatch(string reason);
    MountIslandMarkerCommitOutcome CommitMarkers(int[] markerTypes, out string reason);
}

internal static class MountIslandRecoveryTransaction
{
    /// <summary>
    /// 全组计划 → 只创建 owned inactive roots → 校验 → 激活 → 一次 marker 提交。
    /// create 阶段任一失败即清本批已创建 roots、不写 marker；marker NotCommitted 时清本批；
    /// Unknown 时保留实体与证据，不清理、不重试。
    /// </summary>
    internal static MountIslandRecoveryExecutionResult Run(
        MountIslandRecoveryPlan plan,
        IReadOnlyList<MountIslandRecoveryPlacement> placements,
        IMountIslandRecoveryHost host)
    {
        if (plan == null || host == null)
            return new MountIslandRecoveryExecutionResult(MountIslandRecoveryExecutionOutcome.InvalidPlan,
                "plan/host missing", 0, MountIslandMarkerCommitOutcome.NotCommitted);

        int created = 0;
        if (plan.Creates.Count > 0)
        {
            for (int i = 0; i < plan.Creates.Count; i++)
            {
                MountIslandRecoveryArm arm = plan.Creates[i];
                if (!TryFindPlacement(placements, arm.DefinitionId, out MountIslandRecoveryPlacement placement))
                {
                    SafeDiscard(host, "placement missing");
                    return new MountIslandRecoveryExecutionResult(MountIslandRecoveryExecutionOutcome.InvalidPlan,
                        "placement missing for " + arm.DefinitionId, created, MountIslandMarkerCommitOutcome.NotCommitted);
                }
                bool createdOk;
                string createReason;
                try { createdOk = host.TryCreateInactive(arm, placement.RootX, out _, out createReason); }
                catch (Exception e) { createdOk = false; createReason = "create threw " + e.GetType().Name; }
                if (!createdOk)
                {
                    SafeDiscard(host, "create failed");
                    return new MountIslandRecoveryExecutionResult(MountIslandRecoveryExecutionOutcome.CreateFailed,
                        createReason ?? "create failed", created, MountIslandMarkerCommitOutcome.NotCommitted);
                }
                created++;
            }

            bool validateOk;
            string validateReason;
            try { validateOk = host.TryValidateCreatedBatch(out validateReason); }
            catch (Exception e) { validateOk = false; validateReason = "validate threw " + e.GetType().Name; }
            if (!validateOk)
            {
                SafeDiscard(host, "validate failed");
                return new MountIslandRecoveryExecutionResult(MountIslandRecoveryExecutionOutcome.ValidateFailed,
                    validateReason ?? "validate failed", created, MountIslandMarkerCommitOutcome.NotCommitted);
            }
            bool activateOk;
            string activateReason;
            try { activateOk = host.TryActivateCreatedBatch(out activateReason); }
            catch (Exception e) { activateOk = false; activateReason = "activate threw " + e.GetType().Name; }
            if (!activateOk)
            {
                SafeDiscard(host, "activate failed");
                return new MountIslandRecoveryExecutionResult(MountIslandRecoveryExecutionOutcome.ActivateFailed,
                    activateReason ?? "activate failed", created, MountIslandMarkerCommitOutcome.NotCommitted);
            }
        }

        int[] markerTypes = BuildMarkerTypes(plan);
        if (markerTypes.Length == 0)
        {
            return new MountIslandRecoveryExecutionResult(MountIslandRecoveryExecutionOutcome.Noop,
                "no marker work", created, MountIslandMarkerCommitOutcome.Committed);
        }

        MountIslandMarkerCommitOutcome marker;
        string markerReason;
        try { marker = host.CommitMarkers(markerTypes, out markerReason); }
        catch (Exception e)
        {
            // 异常不能证明未写；按 unknown 保留实体，不盲重试。
            marker = MountIslandMarkerCommitOutcome.Unknown;
            markerReason = "marker commit threw " + e.GetType().Name;
        }
        if (marker == MountIslandMarkerCommitOutcome.Committed)
        {
            return new MountIslandRecoveryExecutionResult(MountIslandRecoveryExecutionOutcome.Committed,
                markerReason ?? "committed", created, marker);
        }
        if (marker == MountIslandMarkerCommitOutcome.NotCommitted)
        {
            if (created > 0) SafeDiscard(host, "marker not committed");
            return new MountIslandRecoveryExecutionResult(MountIslandRecoveryExecutionOutcome.MarkerNotCommitted,
                markerReason ?? "marker not committed", created, marker);
        }

        // Unknown：marker 可能已写；保留已验证实体，不清理、不盲重试。
        return new MountIslandRecoveryExecutionResult(MountIslandRecoveryExecutionOutcome.MarkerUnknown,
            markerReason ?? "marker commit unknown", created, marker);
    }

    private static void SafeDiscard(IMountIslandRecoveryHost host, string reason)
    {
        try { host.DiscardCreatedBatch(reason); }
        catch (Exception) { }
    }

    internal static int[] BuildMarkerTypes(MountIslandRecoveryPlan plan)
    {
        if (plan == null) return Array.Empty<int>();
        var values = new List<int>(plan.MarkerOnly.Count + plan.Creates.Count);
        for (int i = 0; i < plan.MarkerOnly.Count; i++) AddUnique(values, plan.MarkerOnly[i].SteedTypeId);
        for (int i = 0; i < plan.Creates.Count; i++) AddUnique(values, plan.Creates[i].SteedTypeId);
        return values.ToArray();
    }

    private static void AddUnique(List<int> values, int value)
    {
        if (!values.Contains(value)) values.Add(value);
    }

    private static bool TryFindPlacement(IReadOnlyList<MountIslandRecoveryPlacement> placements,
        string definitionId, out MountIslandRecoveryPlacement placement)
    {
        placement = default;
        if (placements == null) return false;
        for (int i = 0; i < placements.Count; i++)
        {
            if (string.Equals(placements[i].DefinitionId, definitionId, StringComparison.Ordinal))
            {
                placement = placements[i];
                return true;
            }
        }
        return false;
    }
}
