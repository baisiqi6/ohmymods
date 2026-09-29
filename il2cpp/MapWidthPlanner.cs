using System;
using System.Collections.Generic;

namespace KingdomEnhancedMod;

/// <summary>
/// 纯宽度规划器（不接触 Unity/原生调用）。issue-77：新生成岛实际长度倍率。
///
/// 输入：本次 LevelLayout.GetBlocks 返回列表逐块实际 GetWidth 之和 W0、滑块倍率快照，
/// 以及由 MapWidthTerrain 核准并按（精确名 → group → 实际宽）稳定排序的候选地块。
/// 输出：追加宽度最接近 W0×m − W0 的完整块组合（优先精确解，误差并列向上），
/// 以及把追加块确定性分散到合法接缝的槽位分配。
///
/// 只用整数宽度与稳定顺序；唯一浮点运算是一次 W0×m 与误差比较，全部有有限性/溢出检查。
/// </summary>
internal readonly struct MapWidthCandidate
{
    /// <summary>稳定键（精确名|groupOne|实际宽），用于排序/日志/同键冲突拒绝；禁止指针或 instanceID。</summary>
    internal readonly string Key;

    /// <summary>原生 GetWidth() 快照，必须为正。</summary>
    internal readonly int Width;

    /// <summary>true = Clearing（优先增加可用土地）；false = Forest（补齐余宽）。</summary>
    internal readonly bool IsClearing;

    internal MapWidthCandidate(string key, int width, bool isClearing)
    {
        Key = key ?? string.Empty;
        Width = width;
        IsClearing = isClearing;
    }
}

internal sealed class MapWidthPlan
{
    internal bool Success;

    /// <summary>失败原因（Success=false 时非空），例如 no-candidates / no seam 由调用方补齐。</summary>
    internal string Failure;

    /// <summary>本次原生列表实际总宽 W0（逐块 GetWidth 之和）。</summary>
    internal int BaselineWidth;

    /// <summary>W0 × 滑块倍率（未取整的原始目标）。</summary>
    internal double TargetWidth;

    /// <summary>选中追加宽度之和。</summary>
    internal int AddedWidth;

    /// <summary>计划实际宽 = W0 + AddedWidth。</summary>
    internal int PlannedWidth;

    /// <summary>追加块序列（候选索引，每项一个块实例，顺序即分散分配顺序）。</summary>
    internal int[] Paddings = Array.Empty<int>();
}

internal static class MapWidthPlanner
{
    /// <summary>
    /// 规划总宽安全上限（W0 + 追加）。正常岛总宽在数千级；此上限只拦溢出与荒谬输入，
    /// 不用小上限让正常大岛失效——超出时明确失败而不是截断。
    /// </summary>
    internal const int MaxTotalWidth = 1 << 20;

    /// <summary>
    /// 求追加宽度最接近 Wt − W0 的完整块组合。m ≤ 1 或 W0=0 时成功且不追加（调用方通常已提前跳过）。
    /// 失败时绝不返回部分结果：调用方必须保持原列表不变。
    /// </summary>
    internal static MapWidthPlan Plan(int baselineWidth, float multiplier, IReadOnlyList<MapWidthCandidate> candidates)
    {
        var plan = new MapWidthPlan { BaselineWidth = baselineWidth };
        if (baselineWidth < 0 || baselineWidth > MaxTotalWidth) return Reject(plan, "baseline-out-of-range");
        if (!float.IsFinite(multiplier) || multiplier <= 0f) return Reject(plan, "multiplier-invalid");

        double target = (double)baselineWidth * multiplier;
        plan.TargetWidth = target;
        if (!double.IsFinite(target) || target > MaxTotalWidth) return Reject(plan, "target-out-of-range");

        if (multiplier <= 1f || baselineWidth == 0)
        {
            plan.Success = true;
            plan.PlannedWidth = baselineWidth;
            return plan;
        }

        // 有效候选：正宽且在安全上限内。非法候选被忽略；全部非法等价于无候选。
        var valid = new List<MapWidthCandidate>(candidates == null ? 0 : candidates.Count);
        var inputIndex = new List<int>(valid.Capacity);
        int minWidth = int.MaxValue;
        if (candidates != null)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                MapWidthCandidate candidate = candidates[i];
                if (candidate.Width <= 0 || candidate.Width > MaxTotalWidth) continue;
                valid.Add(candidate);
                inputIndex.Add(i);
                if (candidate.Width < minWidth) minWidth = candidate.Width;
            }
        }
        if (valid.Count == 0) return Reject(plan, "no-candidates");

        // 与调用方顺序无关的稳定遍历顺序；相同键按输入序（键在适配器中已去重）。
        int[] order = new int[valid.Count];
        for (int i = 0; i < order.Length; i++) order[i] = i;
        Array.Sort(order, (a, b) =>
        {
            int cmp = string.CompareOrdinal(valid[a].Key, valid[b].Key);
            return cmp != 0 ? cmp : a.CompareTo(b);
        });

        double need = target - baselineWidth; // > 0
        int addedCap = MaxTotalWidth - baselineWidth;
        if (addedCap < need - minWidth / 2.0) return Reject(plan, "target-too-large");
        int maxSum = (int)Math.Ceiling(need) + minWidth + 1;
        if (maxSum > addedCap) maxSum = addedCap;
        if (maxSum < 0) return Reject(plan, "target-too-large");

        // DP：每个可表示宽度记录（Forest 块数, 总块数）字典序最小；同成本保留先到路径（稳定）。
        int[] forest = new int[maxSum + 1];
        int[] blocks = new int[maxSum + 1];
        int[] parent = new int[maxSum + 1];
        int[] parentCandidate = new int[maxSum + 1];
        for (int s = 1; s <= maxSum; s++) forest[s] = -1;
        parent[0] = -1;
        parentCandidate[0] = -1;
        for (int s = 0; s <= maxSum; s++)
        {
            if (forest[s] < 0) continue;
            for (int oi = 0; oi < order.Length; oi++)
            {
                int vi = order[oi];
                int width = valid[vi].Width;
                int next = s + width;
                if (next > maxSum) continue;
                int nextForest = forest[s] + (valid[vi].IsClearing ? 0 : 1);
                int nextBlocks = blocks[s] + 1;
                if (forest[next] < 0 || nextForest < forest[next]
                    || (nextForest == forest[next] && nextBlocks < blocks[next]))
                {
                    forest[next] = nextForest;
                    blocks[next] = nextBlocks;
                    parent[next] = s;
                    parentCandidate[next] = vi;
                }
            }
        }

        // 最邻近扫描：上偏候选先看；误差并列取较大宽度（并列向上）。
        int baseUp = (int)Math.Ceiling(need);
        int baseDown = (int)Math.Floor(need);
        int bestSum = -1;
        double bestError = double.PositiveInfinity;
        for (int k = 0; ; k++)
        {
            Consider(baseUp + k);
            Consider(baseDown - k);
            // k 越大误差至少为 k；bestError ≤ k 之后不可能更优。
            if (bestSum >= 0 && bestError <= k) break;
            if (k > maxSum + 2) break; // 防御：正常路径在找到 0 或达标后即退出
        }
        if (bestSum < 0) return Reject(plan, "no-representable-width");
        // 误差必须不超过最小可用块的就近舍入界（半块）；不能粗暴追加到超过目标冒充成功。
        if (bestError > minWidth / 2.0 + 1e-9) return Reject(plan, "no-width-within-rounding-bound");

        var reversed = new List<int>();
        int cursor = bestSum;
        while (cursor > 0)
        {
            reversed.Add(inputIndex[parentCandidate[cursor]]);
            cursor = parent[cursor];
        }
        reversed.Reverse();

        plan.Success = true;
        plan.AddedWidth = bestSum;
        plan.PlannedWidth = baselineWidth + bestSum;
        plan.Paddings = reversed.ToArray();
        return plan;

        void Consider(int sum)
        {
            if (sum < 0 || sum > maxSum || forest[sum] < 0) return;
            double error = Math.Abs(sum - need);
            if (error < bestError || (error == bestError && sum > bestSum))
            {
                bestError = error;
                bestSum = sum;
            }
        }
    }

    /// <summary>
    /// 把 paddingCount 个追加块轮转分散到 seamCount 个合法接缝，返回每个追加块的目标接缝槽
    /// （0..seamCount-1，按原接缝从左到右）。确定性且尽量均匀；块宽不参与分配。
    /// </summary>
    internal static int[] AssignSeams(int paddingCount, int seamCount)
    {
        if (paddingCount <= 0) return Array.Empty<int>();
        if (seamCount <= 0) throw new ArgumentOutOfRangeException(nameof(seamCount));
        var slots = new int[paddingCount];
        for (int i = 0; i < paddingCount; i++) slots[i] = i % seamCount;
        return slots;
    }

    private static MapWidthPlan Reject(MapWidthPlan plan, string reason)
    {
        plan.Success = false;
        plan.Failure = reason;
        return plan;
    }
}
