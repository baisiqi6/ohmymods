using System;
using System.Collections.Generic;

namespace KingdomEnhancedMod
{
    /// <summary>
    /// 双扩展岛总览帧几何 + 详情分页（纯函数：零 Unity/Il2Cpp/Harmony 依赖，不注册 hook、
    /// 不读写存档、不请求资源）。
    ///
    /// 契约（用户 2026-10-09 双岛方案 + policy-map-discovery 冻结要求）：
    /// - 总览不再把全剪影撑成横幅：一次 frame 规划取 native 普通 cluster 的实际比例、两岛
    ///   共用同一 scale（≤1，不放大小岛），互不相交且都在 paper/mask 内；放不下返回 false，
    ///   绝不通过扩大 reserve/paper、clamp 或 retry 掩错。
    /// - 每岛 terrain/outline/click hitbox/ship 状态图形共用同一坐标框（<see cref="SameFrame"/>）。
    /// - 总览视图不建立本扩展岛的全资源 requests/clones（native 十岛资源仍由原生负责）。
    /// - native UI0..9（含 native 10→UI9 的焦点）永不接管；扩展逻辑只服务 UI≥10。
    /// - 详情分页：按真实可用行/列给每页连续区间，保证 8/7/15/16 等任意实际授予数全部可达。
    /// </summary>
    internal readonly struct MountIslandFrame
    {
        internal readonly float X0;
        internal readonly float Y0;
        internal readonly float X1;
        internal readonly float Y1;

        internal MountIslandFrame(float x0, float y0, float x1, float y1)
        {
            X0 = x0;
            Y0 = y0;
            X1 = x1;
            Y1 = y1;
        }

        internal float Width => X1 - X0;
        internal float Height => Y1 - Y0;

        internal bool IsValid => X1 > X0 && Y1 > Y0;

        internal bool IsInside(in MountIslandFrame paper)
            => X0 >= paper.X0 - 0.001f && Y0 >= paper.Y0 - 0.001f
            && X1 <= paper.X1 + 0.001f && Y1 <= paper.Y1 + 0.001f;

        internal bool Intersects(in MountIslandFrame other)
            => X0 < other.X1 && other.X0 < X1 && Y0 < other.Y1 && other.Y0 < Y1;

        /// <summary>与另一框的水平间隙（重叠时为负）。</summary>
        internal float HorizontalGap(in MountIslandFrame other)
            => Math.Max(other.X0 - X1, X0 - other.X1);
    }

    internal static class MountIslandOverviewLayout
    {
        /// <summary>帧与 paper 边缘的留白（不随调用放大）。</summary>
        internal const float FrameMargin = 1.5f;
        /// <summary>两岛帧之间的最小水平间隙。</summary>
        internal const float MinFrameGap = 4f;
        /// <summary>
        /// 两岛帧 scale 的**纯建议**参考值（无 actual 证据支撑，仅是审阅起点）：调用方可把它作为
        /// <see cref="TryPlanTwoFrames(in MountIslandFrame, float, float, float, out MountIslandFrame, out MountIslandFrame, out float)"/>
        /// 的 minScale 传入，但默认路径**不**用它——常规窗口不得被魔数拦下；真实下限应由 root 按
        /// 实际 native cluster 尺寸与 paper 实测核定。
        /// </summary>
        internal const float SuggestedMinimumFrameScale = 0.5f;

        /// <summary>
        /// 一次规划两岛 frame：band 均分左右两半，各自按 native 普通 cluster 的 base 尺寸
        /// 以**同一 scale（≤1）**放置。任何一半放不下（或结果为负间隙/越出 band）→ false，
        /// 不 clamp、不扩 band、不重试。
        /// </summary>
        internal static bool TryPlanTwoFrames(in MountIslandFrame band, float baseWidth, float baseHeight,
            out MountIslandFrame primary, out MountIslandFrame secondary, out float scale)
            => TryPlanTwoFrames(band, baseWidth, baseHeight, 0f, out primary, out secondary, out scale);

        /// <summary>
        /// 同 <see cref="TryPlanTwoFrames(in MountIslandFrame, float, float, out MountIslandFrame, out MountIslandFrame, out float)"/>，
        /// 但由调用方给定 minScale（0 = 只要求正 scale，不拦常规窗口）。低于 minScale → false（不 clamp）。
        /// </summary>
        internal static bool TryPlanTwoFrames(in MountIslandFrame band, float baseWidth, float baseHeight,
            float minScale, out MountIslandFrame primary, out MountIslandFrame secondary, out float scale)
        {
            primary = default;
            secondary = default;
            scale = 0f;
            if (!band.IsValid || !(baseWidth > 0f) || !(baseHeight > 0f)) return false;

            float usableWidth = band.Width - 2f * FrameMargin - MinFrameGap;
            float usableHeight = band.Height - 2f * FrameMargin;
            if (!(usableWidth > 0f) || !(usableHeight > 0f)) return false;

            float sideWidth = usableWidth * 0.5f;
            float fit = Math.Min(1f, Math.Min(sideWidth / baseWidth, usableHeight / baseHeight));
            if (!(fit > 0f) || fit < minScale) return false;
            scale = fit;

            float width = baseWidth * scale;
            float height = baseHeight * scale;
            float mid = (band.X0 + band.X1) * 0.5f;
            float cy = (band.Y0 + band.Y1) * 0.5f;

            float leftCx = band.X0 + FrameMargin + sideWidth * 0.5f;
            float rightCx = mid + MinFrameGap * 0.5f + sideWidth * 0.5f;
            primary = new MountIslandFrame(leftCx - width * 0.5f, cy - height * 0.5f,
                leftCx + width * 0.5f, cy + height * 0.5f);
            secondary = new MountIslandFrame(rightCx - width * 0.5f, cy - height * 0.5f,
                rightCx + width * 0.5f, cy + height * 0.5f);

            if (!primary.IsInside(band) || !secondary.IsInside(band)) return false;
            if (primary.Intersects(secondary)) return false;
            if (primary.HorizontalGap(secondary) < MinFrameGap - 0.001f) return false;
            return true;
        }

        /// <summary>
        /// terrain/outline/click hitbox 三个绘制元素必须写**同一 bounding box**（坐标完全一致）。
        /// 注意：船标等 state 图标**不**适用本判定 —— 只共享坐标变换/锚点，保持自身尺寸同比定位
        /// （见 TryAnchorStateIcon），不得被拉到整岛 bbox。
        /// </summary>
        internal static bool SameFrame(in MountIslandFrame a, in MountIslandFrame b)
            => a.X0 == b.X0 && a.Y0 == b.Y0 && a.X1 == b.X1 && a.Y1 == b.Y1;

        /// <summary>两个 state 图标锚点是否同一坐标（同 eps 内，供船标共享变换断言）。</summary>
        internal static bool SameAnchorPoint(float ax, float ay, float bx, float by, float eps)
            => Math.Abs(ax - bx) <= eps && Math.Abs(ay - by) <= eps;

        /// <summary>
        /// state 图标（船标等）放置：锚点在岛框内 (anchorX, anchorY)，图标保持**自身尺寸**（不随框拉伸），
        /// 且必须完整落在岛框内；放不下返回 false（不 clamp）。
        /// </summary>
        internal static bool TryAnchorStateIcon(in MountIslandFrame frame, float iconWidth, float iconHeight,
            float anchorX, float anchorY, out MountIslandFrame icon)
        {
            icon = default;
            if (!(iconWidth > 0f) || !(iconHeight > 0f)) return false;
            if (!frame.IsValid) return false;
            if (anchorX < frame.X0 || anchorX > frame.X1 || anchorY < frame.Y0 || anchorY > frame.Y1) return false;
            icon = new MountIslandFrame(anchorX - iconWidth * 0.5f, anchorY - iconHeight * 0.5f,
                anchorX + iconWidth * 0.5f, anchorY + iconHeight * 0.5f);
            return icon.IsInside(frame);
        }

        /// <summary>
        /// base 尺寸归一：native 普通 cluster 的 1x art 尺寸必须乘**共同 uniformScale 后的实际倍率**
        /// （否则 normal 原生簇缩到 0.6 时扩展岛 scale1 会反而大 67%）。非正输入返回 0（fail-closed）。
        /// </summary>
        internal static float NormalizeBaseSize(float artSize1x, float appliedScale)
        {
            if (!(artSize1x > 0f) || !(appliedScale > 0f)) return 0f;
            return artSize1x * appliedScale;
        }

        /// <summary>岛框基准尺寸（shore aspect），不超过 native 呈现 bbox 两方向。</summary>
        internal static bool TryBaseInAspect(float nativeWidth, float nativeHeight, float aspect,
            out float baseWidth, out float baseHeight)
        {
            baseWidth = 0f;
            baseHeight = 0f;
            if (!(nativeWidth > 0f) || !(nativeHeight > 0f) || !(aspect > 0f)) return false;
            baseWidth = Math.Min(nativeWidth, nativeHeight * aspect);
            baseHeight = baseWidth / aspect;
            return baseWidth > 0f && baseHeight > 0f;
        }

        /// <summary>总览视图不建立本扩展岛全资源 requests/clones；详情视图才允许。</summary>
        internal static bool BuildsOwnResourceRequests(bool isOverviewView) => !isOverviewView;

    }

    /// <summary>
    /// 详情翻页组事务（纯顺序/回滚决策，动作由调用方回调；production 与 tests 共用同一 helper）：
    /// 固定顺序 = swapRefs → activate → destroyOld；swapRefs/activate 任一步异常必须回滚引用并保留旧页，
    /// 只有前三步都真正成功才返回 true（绝不把 activate 失败吞成成功）。
    /// </summary>
    internal static class MountIslandPageHolderTransaction
    {
        internal static bool TryRunPageSwap(System.Action swapRefs, System.Action activateNew,
            System.Action destroyOld, System.Action rollback)
        {
            if (swapRefs == null || activateNew == null || destroyOld == null || rollback == null) return false;
            try { swapRefs(); }
            catch (Exception)
            {
                try { rollback(); } catch (Exception) { }
                return false;
            }
            try { activateNew(); }
            catch (Exception)
            {
                try { rollback(); } catch (Exception) { }
                return false;
            }
            try { destroyOld(); } catch (Exception) { }
            return true;
        }
    }

    /// <summary>
    /// LAND_TO_MAP_LAND 两键（11/13）追加事务的纯决策：捕获 prior（存在/值），后续任何失败时按
    /// CAS 恢复（仅当当前值仍是我们写入的值），从未存在过的键则移除；foreign/未知值绝不覆盖。
    /// </summary>
    internal readonly struct MountIslandMapDictEntry
    {
        internal readonly int Physical;
        internal readonly bool HadKey;
        internal readonly int PriorValue;
        internal readonly int WrittenValue;

        internal MountIslandMapDictEntry(int physical, bool hadKey, int priorValue, int writtenValue)
        {
            Physical = physical;
            HadKey = hadKey;
            PriorValue = priorValue;
            WrittenValue = writtenValue;
        }
    }

    internal static class MountIslandMapDictTransaction
    {
        internal static MountIslandMapDictEntry Capture(int physical, bool hadKey, int priorValue, int writtenValue)
            => new MountIslandMapDictEntry(physical, hadKey, priorValue, writtenValue);

        /// <summary>回滚：键仍存在且当前值 == 我们写入的值才允许恢复 prior（CAS）。</summary>
        internal static bool ShouldRestoreRollback(in MountIslandMapDictEntry entry, bool keyPresent, int currentValue)
            => entry.HadKey && keyPresent && currentValue == entry.WrittenValue;

        /// <summary>回滚：我们新建的键（prior 不存在）且当前值仍是我们的写入值才允许移除（CAS）。</summary>
        internal static bool ShouldRemoveRollback(in MountIslandMapDictEntry entry, bool keyPresent, int currentValue)
            => !entry.HadKey && keyPresent && currentValue == entry.WrittenValue;

        /// <summary>正常撤销降级（→9）：仅当当前值仍是本 tuple 的 owned 期望映射时才写，foreign 不动。</summary>
        internal static bool OwnsExpectedValue(int currentValue, int expectedUi) => currentValue == expectedUi;

        /// <summary>
        /// 回滚 setter/remove/array setter 失败/读回未知 → 该菜单代次必须 poisoned（不 dispatch/不 append/不 travel），
        /// 由 Native Clear / menu lifecycle 有界归还；不做自动 retry 扫描。anyUnknown=true 才 poison。
        /// </summary>
        internal static bool RequiresPoisonOnRollbackFailure(bool anyUnknownReadback) => anyUnknownReadback;
    }

    /// <summary>
    /// 详情分页（纯函数）：每页连续区间、行主序、全部条目恰可达一次；配合真实可用行/列
    /// （MountMapLayoutPlan.PickFreeRows/ColumnsFor 的产物）使用，不提高行数上限替代可达性。
    /// </summary>
    internal static class MountIslandDetailPagePlan
    {
        internal static int PerPage(int rowsPerPage, int columns)
        {
            if (rowsPerPage <= 0 || columns <= 0) return 0;
            return rowsPerPage * columns;
        }

        internal static int PageCount(int entryCount, int rowsPerPage, int columns)
        {
            int perPage = PerPage(rowsPerPage, columns);
            if (entryCount <= 0 || perPage <= 0) return 0;
            return (entryCount + perPage - 1) / perPage;
        }

        /// <summary>第 page 页的连续条目区间 [start, start+length)；越界或空输入返回 false。</summary>
        internal static bool TryPageRange(int entryCount, int rowsPerPage, int columns, int page,
            out int start, out int length)
        {
            start = 0;
            length = 0;
            int perPage = PerPage(rowsPerPage, columns);
            int pages = PageCount(entryCount, rowsPerPage, columns);
            if (pages <= 0 || page < 0 || page >= pages) return false;
            start = page * perPage;
            length = Math.Min(perPage, entryCount - start);
            return length > 0;
        }

        /// <summary>
        /// 有限枚举选页容量：capacity 从 maxCapacity 递减，对**每个连续页**调用真实 planner 预验
        /// （pageFits(offset,length) 必须证明该页全量可放）；第一个所有页都通过的容量才 commit；
        /// 没有可行容量返回 false（调用方整体 fallback）。无副作用、非周期 retry、不改 paper/scale。
        /// </summary>
        internal static bool TryFindUniformPageCapacity(int entryCount, int maxCapacity,
            System.Func<int, int, bool> pageFits, out int capacity)
        {
            capacity = 0;
            if (entryCount <= 0 || pageFits == null) return false;
            int upper = Math.Min(Math.Max(maxCapacity, 1), entryCount);
            for (int candidate = upper; candidate >= 1; candidate--)
            {
                int pages = PageCount(entryCount, candidate, 1);
                bool allPagesFit = pages > 0;
                for (int page = 0; page < pages && allPagesFit; page++)
                {
                    if (!TryPageRange(entryCount, candidate, 1, page, out int offset, out int length))
                    {
                        allPagesFit = false;
                        break;
                    }
                    if (!pageFits(offset, length)) allPagesFit = false;
                }
                if (allPagesFit)
                {
                    capacity = candidate;
                    return true;
                }
            }
            return false;
        }

        /// <summary>逐页区间必须恰好覆盖 [0,entryCount) 且不重叠（全部条目可达的证明函数）。</summary>
        internal static bool CoversAllOnce(int entryCount, int rowsPerPage, int columns)
        {
            int pages = PageCount(entryCount, rowsPerPage, columns);
            if (pages <= 0) return false;
            int cursor = 0;
            for (int page = 0; page < pages; page++)
            {
                if (!TryPageRange(entryCount, rowsPerPage, columns, page, out int start, out int length)) return false;
                if (start != cursor) return false;
                cursor += length;
            }
            return cursor == entryCount;
        }
    }
}
