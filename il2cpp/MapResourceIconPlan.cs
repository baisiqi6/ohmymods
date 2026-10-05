using System;
using System.Collections.Generic;

namespace KingdomEnhancedMod
{
    /// <summary>资源图标类别（与原生 UIMapIcon/MapIconType 对应：Steed=1 / Hermit=2 / Statue=0）。</summary>
    internal enum MapIconKind
    {
        Steed = 0,
        Hermit = 1,
        Statue = 2,
    }

    /// <summary>一条待显示的扩展资源（来自真实 LandMapData 数组的某个下标；不含任何推测数据）。</summary>
    internal struct MapIconRequest
    {
        internal MapIconKind Kind;
        internal int TypeId;
        internal int ArrayIndex;
        /// <summary>原生 prefab 显示尺寸（单位：UI 单位；已含 prefab 自身 localScale）。</summary>
        internal float Width;
        internal float Height;

        internal MapIconRequest(MapIconKind kind, int typeId, int arrayIndex, float width, float height)
        {
            Kind = kind;
            TypeId = typeId;
            ArrayIndex = arrayIndex;
            Width = width;
            Height = height;
        }
    }

    /// <summary>
    /// 放置结果：图标矩形左下角（surface 局部坐标，原点=surface 左下角）+ 采用的缩放 +
    /// **原请求下标**（Rebuild 必须用它检索对应 source，禁止用压缩后的 placements 下标）。
    /// </summary>
    internal struct MapIconPlacement
    {
        internal MapIconRequest Request;
        internal int RequestIndex;
        internal float X;
        internal float Y;
        internal float Scale;

        internal MapIconPlacement(MapIconRequest request, int requestIndex, float x, float y, float scale)
        {
            Request = request;
            RequestIndex = requestIndex;
            X = x;
            Y = y;
            Scale = scale;
        }
    }

    /// <summary>轴对齐矩形（纯数据）。</summary>
    internal struct MapIconBox
    {
        internal float X0;
        internal float Y0;
        internal float X1;
        internal float Y1;

        internal MapIconBox(float x0, float y0, float x1, float y1)
        {
            X0 = x0;
            Y0 = y0;
            X1 = x1;
            Y1 = y1;
        }

        internal float Width => X1 - X0;
        internal float Height => Y1 - Y0;

        internal bool Intersects(in MapIconBox other, float margin)
        {
            return X0 - margin < other.X1 && other.X0 < X1 + margin &&
                   Y0 - margin < other.Y1 && other.Y0 < Y1 + margin;
        }

        internal bool Contains(float x, float y)
        {
            return x >= X0 && x <= X1 && y >= Y0 && y <= Y1;
        }
    }

    /// <summary>图标可用面与遮挡矩形集合（surface 局部坐标，原点=左下角）。</summary>
    internal sealed class MapIconSurface
    {
        internal float X0;
        internal float Y0;
        internal float X1;
        internal float Y1;
        private readonly List<MapIconBox> _blocked = new List<MapIconBox>(24);
        // 可选 native sprite mesh 形状约束（issue-152）：null = 无约束（例如纯矩形测试台）。
        // 形状顶点/三角形已是**规划空间**坐标（identity 映射，见 MapMountIcons.TryReadNativeArtShape），
        // 拓扑（canonical 顶点、去重三角形、无向边界边）在此一次性预处理并复用。
        private MapIconMeshTopology _shapeTopology;

        internal MapIconSurface(float x0, float y0, float x1, float y1)
        {
            X0 = x0;
            Y0 = y0;
            X1 = x1;
            Y1 = y1;
        }

        internal float Width => X1 - X0;
        internal float Height => Y1 - Y0;
        internal int BlockedCount => _blocked.Count;
        internal MapIconBox Blocked(int index) => _blocked[index];
        internal bool HasMeshShape => _shapeTopology != null && _shapeTopology.Valid;

        internal void AddBlocked(in MapIconBox box)
        {
            if (box.Width <= 0f || box.Height <= 0f) return;
            _blocked.Add(box);
        }

        /// <summary>设 exact native sprite mesh 拓扑（规划空间坐标；由调用方保证已通过拓扑校验）。</summary>
        internal void SetMeshShape(MapIconMeshTopology topology)
        {
            _shapeTopology = topology;
        }

        /// <summary>box 是否完整位于 mesh 形状内：无形状约束 → true；形状无效（Unknown）→ false（fail-closed）。</summary>
        internal bool InsideMeshShape(in MapIconBox box)
        {
            if (_shapeTopology == null) return true;
            return MapIconMeshShape.FootprintInside(_shapeTopology, box, MapIconMeshShape.DefaultEpsilon);
        }

        internal bool BlockedFree(in MapIconBox box, float margin)
        {
            for (int i = 0; i < _blocked.Count; i++)
            {
                if (box.Intersects(_blocked[i], margin)) return false;
            }
            return true;
        }
    }

    /// <summary>
    /// 一个 native sprite 的 mesh → 规划空间的轴对齐映射（由真实 Image/RectTransform 变换链测出）：
    /// Sprite.vertices 是 **pivot 相对的 sprite 单位**（px = v*ppu + pivot），绘制矩形是该 sprite 的
    /// rect（或 preserveAspect 后的居中矩形）在目标空间的落点；规划坐标 = Origin + px*Scale。
    /// </summary>
    /// <summary>
    /// uGUI `Image` Simple + useSpriteMesh 的实际绘制几何（primary source：
    /// uGUI `Runtime/UGUI/UI/Core/Image.cs` — `GenerateSprite`(useSpriteMesh 路径) 与 `PreserveSpriteAspectRatio`）：
    /// - `drawingSize` = 经 `GetPixelAdjustedRect()`（+ preserveAspect 按 **RectTransform.pivot** 缩框）后的矩形尺寸；
    /// - 每个 sprite 顶点（= `Sprite.vertices`，pivot 相对的 sprite 单位）的 art-local 位置：
    ///   `v / Sprite.bounds.size * drawingSize - (rectPivot - Sprite.pivot / Sprite.rect.size) * drawingSize`
    ///   —— 归一用 **bounds.size**（不是 rect/ppu），偏移用 **两个 pivot 的差**；
    /// - 本结构只承载"尺寸与偏移"，逐顶点换算由 `LocalX/LocalY` 给出（不依赖 GetPixelAdjustedRect 的位置项，
    ///   与 `GenerateSprite` 只用 r.width/r.height 的事实一致）。
    /// </summary>
    internal struct MapIconImageDraw
    {
        internal float BoundsW;
        internal float BoundsH;        // Sprite.bounds.size（sprite 单位）
        internal float DrawingW;
        internal float DrawingH;       // 绘制矩形尺寸（art-local）
        internal float OffsetX;
        internal float OffsetY;        // (rectPivot - spritePivotNormalized) * drawingSize
        internal bool Valid;

        internal float LocalX(float vertexX) => vertexX / BoundsW * DrawingW - OffsetX;
        internal float LocalY(float vertexY) => vertexY / BoundsH * DrawingH - OffsetY;
    }

    /// <summary>
    /// native art 的 sprite/Image 几何纯计算（无 UnityEngine 依赖；运行期只负责读
    /// Image.type/useSpriteMesh/overrideSprite/preserveAspect/GetPixelAdjustedRect 与
    /// Sprite.rect/pivot/bounds/vertices/triangles，以及 RectTransform.pivot）。
    /// </summary>
    internal static class MapIconNativeArtPlan
    {
        /// <summary>
        /// 构建 Simple+useSpriteMesh 的绘制参数：等价 uGUI `PreserveSpriteAspectRatio`（按 RT.pivot 缩框，
        /// 不是居中）+ `GenerateSprite` 的 `drawingSize`/`drawOffset`。任何非法输入（非正尺寸/非有限值）→ false。
        /// </summary>
        internal static bool TryBuildSimpleMeshDraw(float rectW, float rectH, float rectPivotX, float rectPivotY,
            bool preserveAspect, float spriteRectW, float spriteRectH, float spritePivotX, float spritePivotY,
            float boundsW, float boundsH, out MapIconImageDraw draw)
        {
            draw = default;
            if (!(rectW > 0f) || !(rectH > 0f)) return false;
            if (!(spriteRectW > 0f) || !(spriteRectH > 0f)) return false;
            if (!(boundsW > 0f) || !(boundsH > 0f)) return false;
            if (!Finite(rectW) || !Finite(rectH) || !Finite(boundsW) || !Finite(boundsH)) return false;

            float drawingW = rectW;
            float drawingH = rectH;
            float spriteRatio = spriteRectW / spriteRectH;
            if (!Finite(spriteRatio)) return false;
            if (preserveAspect && spriteRatio > 0f)
            {
                float rectRatio = drawingW / drawingH;
                if (spriteRatio > rectRatio)
                {
                    // 高度缩到 rect.width*(1/spriteRatio)（宽不变）；位置项不影响顶点（GenerateSprite 只用尺寸）。
                    drawingH = drawingW * (1.0f / spriteRatio);
                }
                else
                {
                    drawingW = drawingH * spriteRatio;
                }
            }
            if (!(drawingW > 0f) || !(drawingH > 0f)) return false;

            float spritePivotNormX = spritePivotX / spriteRectW;
            float spritePivotNormY = spritePivotY / spriteRectH;
            draw = new MapIconImageDraw
            {
                BoundsW = boundsW,
                BoundsH = boundsH,
                DrawingW = drawingW,
                DrawingH = drawingH,
                OffsetX = (rectPivotX - spritePivotNormX) * drawingW,
                OffsetY = (rectPivotY - spritePivotNormY) * drawingH,
                Valid = true,
            };
            return true;
        }

        /// <summary>
        /// 自然 paper 倍率：**同一条绘制图形**在规划空间与 owner 本地空间的真实变换之比
        /// （含 art 自身 localScale 与 cluster→paper 全局 fit；不硬编码任何倍率）。
        /// 图标只能统一缩放 ⇒ 取两轴较小者（保守不放大）。
        /// </summary>
        internal static bool TryResolvePaperScale(in MapIconBox drawnInPlan, in MapIconBox drawnInOwnerLocal,
            out float scale)
        {
            scale = 0f;
            if (drawnInOwnerLocal.Width <= 1e-4f || drawnInOwnerLocal.Height <= 1e-4f) return false;
            if (drawnInPlan.Width <= 0f || drawnInPlan.Height <= 0f) return false;
            float sx = drawnInPlan.Width / drawnInOwnerLocal.Width;
            float sy = drawnInPlan.Height / drawnInOwnerLocal.Height;
            scale = sx < sy ? sx : sy;
            return scale > 1e-4f;
        }

        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>
    /// native sprite mesh 的拓扑预处理结果（规划空间坐标；identity 映射，不再做坐标换算）。
    /// 拓扑规则（几何 union，不看索引朝向）：
    /// - 按**坐标完全相等**做顶点 canonical 化（不引入 epsilon）；
    /// - 丢弃退化三角形（canonical 索引重复或零面积），去重完全相同的三角形；
    /// - 无向边计数：=1 外部边界、=2 内部接缝、&gt;2 非流形 ⇒ **Unknown**（调用方整体保留 native，
    ///   绝不默认"全部可放"）；
    /// - 边界边只用于"footprint 是否跨越海岸/凹口"，接缝不再误判为海岸。
    /// </summary>
    internal sealed class MapIconMeshTopology
    {
        internal float[] VertexX;
        internal float[] VertexY;
        internal int VertexCount;          // canonical 顶点数
        internal int[] Triangles;          // 去重后的三角形（canonical 索引）
        internal int[] BoundaryEdges;      // 成对
        internal int BoundaryCount;
        internal bool Valid;
    }

    /// <summary>
    /// native sprite mesh 形状约束（纯函数）：footprint（规划空间轴对齐矩形）是否**完整**位于 mesh 覆盖区内。
    /// 判据：四角 + 中心都在 mesh 内（triangle union，含边界）∧ 没有任何**几何边界**穿过 footprint 内部
    /// （ε 内缩）。边界的接缝（内部共享边，含 mixed winding 与重复坐标 seam）不参与该判定。
    /// </summary>
    internal static class MapIconMeshShape
    {
        internal const float DefaultEpsilon = 0.02f;
        /// <summary>拓扑/边界预计算的规模上限（超过即 Unknown：宁可整体保留 native，也不做无界扫描）。</summary>
        internal const int MaxTriangles = 512;
        internal const int MaxVertices = 2048;

        /// <summary>
        /// 拓扑预处理（canonical 顶点 / 去重三角形 / 无向边界边）。失败原因写入 reason，
        /// 调用方必须把该 native sprite 视为"未知几何"并整体保留 native 显示。
        /// </summary>
        internal static bool TryBuildTopology(float[] vertexX, float[] vertexY, int vertexCount,
            int[] triangles, out MapIconMeshTopology topology, out string reason)
        {
            topology = null;
            reason = "unset";
            if (vertexX == null || vertexY == null || triangles == null) { reason = "no-mesh"; return false; }
            if (vertexCount < 3) { reason = "mesh-too-few-vertices"; return false; }
            if (vertexCount > MaxVertices) { reason = "mesh-too-many-vertices"; return false; }
            if (triangles.Length < 3) { reason = "mesh-no-triangles"; return false; }
            if (triangles.Length / 3 > MaxTriangles) { reason = "mesh-too-many-triangles"; return false; }

            // 1) 坐标 canonical 化（完全相等；不造 epsilon）
            var canonicalIndex = new int[vertexCount];
            var order = new Dictionary<long, int>(vertexCount);
            var cx = new List<float>(vertexCount);
            var cy = new List<float>(vertexCount);
            for (int i = 0; i < vertexCount; i++)
            {
                float x = vertexX[i], y = vertexY[i];
                if (!MapIconNativeArtPlan.Finite(x) || !MapIconNativeArtPlan.Finite(y))
                {
                    reason = "mesh-nonfinite-vertex";
                    return false;
                }
                long key = ((long)BitConverter.SingleToInt32Bits(x) << 32) ^
                           (uint)BitConverter.SingleToInt32Bits(y);
                if (!order.TryGetValue(key, out int mapped))
                {
                    mapped = cx.Count;
                    order.Add(key, mapped);
                    cx.Add(x);
                    cy.Add(y);
                }
                canonicalIndex[i] = mapped;
            }
            if (cx.Count < 3) { reason = "mesh-degenerate-collapsed"; return false; }
            float[] vx = cx.ToArray();
            float[] vy = cy.ToArray();

            // 2) 三角形：丢弃退化（重复索引/零面积）、去重完全相同者
            var kept = new List<int>(triangles.Length);
            var seen = new HashSet<long>(triangles.Length / 3);
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                if (a < 0 || b < 0 || c < 0 || a >= vertexCount || b >= vertexCount || c >= vertexCount)
                {
                    continue;   // 无效索引：忽略该三角形
                }
                int ca = canonicalIndex[a], cb = canonicalIndex[b], cc = canonicalIndex[c];
                if (ca == cb || cb == cc || cc == ca) continue;   // 退化：重复顶点
                float area2 = (vx[cb] - vx[ca]) * (vy[cc] - vy[ca]) - (vx[cc] - vx[ca]) * (vy[cb] - vy[ca]);
                if (!(Math.Abs(area2) > 1e-12f)) continue;        // 退化：零面积
                int lo = Math.Min(ca, Math.Min(cb, cc));
                int hi = Math.Max(ca, Math.Max(cb, cc));
                int mid = ca + cb + cc - lo - hi;
                long triKey = ((long)lo * MaxVertices + mid) * MaxVertices + hi;
                if (!seen.Add(triKey)) continue;                  // 去重：完全相同三角形
                kept.Add(ca); kept.Add(cb); kept.Add(cc);
            }
            if (kept.Count < 3) { reason = "mesh-degenerate-no-area"; return false; }

            // 3) 无向边计数：1=边界、2=内部接缝、>2=非流形（Unknown）
            var edgeCounts = new Dictionary<long, int>(kept.Count);
            for (int t = 0; t + 2 < kept.Count; t += 3)
            {
                CountEdge(edgeCounts, kept[t], kept[t + 1], cx.Count);
                CountEdge(edgeCounts, kept[t + 1], kept[t + 2], cx.Count);
                CountEdge(edgeCounts, kept[t + 2], kept[t], cx.Count);
            }
            var boundary = new List<int>(kept.Count);
            int nonManifold = 0;
            foreach (KeyValuePair<long, int> pair in edgeCounts)
            {
                if (pair.Value > 2) { nonManifold++; continue; }
                if (pair.Value != 1) continue;
                long key = pair.Key;
                int a = (int)(key / MaxVertices);
                int b = (int)(key % MaxVertices);
                boundary.Add(a);
                boundary.Add(b);
            }
            if (nonManifold > 0) { reason = "mesh-nonmanifold"; return false; }
            if (boundary.Count < 4) { reason = "mesh-no-boundary"; return false; }

            topology = new MapIconMeshTopology
            {
                VertexX = vx,
                VertexY = vy,
                VertexCount = cx.Count,
                Triangles = kept.ToArray(),
                BoundaryEdges = boundary.ToArray(),
                BoundaryCount = boundary.Count,
                Valid = true,
            };
            reason = null;
            return true;
        }

        private static void CountEdge(Dictionary<long, int> counts, int a, int b, int vertexCount)
        {
            int lo = a < b ? a : b;
            int hi = a < b ? b : a;
            long key = (long)lo * MaxVertices + hi;
            counts.TryGetValue(key, out int current);
            counts[key] = current + 1;
        }

        /// <summary>
        /// footprint（规划空间）是否完整位于拓扑覆盖区内。拓扑无效（Unknown）时返回 false（fail-closed）。
        /// </summary>
        internal static bool FootprintInside(MapIconMeshTopology topology, in MapIconBox footprint, float epsilon)
        {
            if (topology == null || !topology.Valid) return false;
            if (footprint.Width <= 0f || footprint.Height <= 0f) return false;
            float[] vx = topology.VertexX, vy = topology.VertexY;
            int[] tris = topology.Triangles;
            int count = topology.VertexCount;

            if (!PointInside(topology, footprint.X0, footprint.Y0)) return false;
            if (!PointInside(topology, footprint.X1, footprint.Y0)) return false;
            if (!PointInside(topology, footprint.X1, footprint.Y1)) return false;
            if (!PointInside(topology, footprint.X0, footprint.Y1)) return false;
            if (!PointInside(topology, (footprint.X0 + footprint.X1) * 0.5f, (footprint.Y0 + footprint.Y1) * 0.5f))
            {
                return false;
            }

            float ex0 = footprint.X0 + epsilon, ey0 = footprint.Y0 + epsilon;
            float ex1 = footprint.X1 - epsilon, ey1 = footprint.Y1 - epsilon;
            if (ex1 <= ex0 || ey1 <= ey0) return false;
            int[] boundary = topology.BoundaryEdges;
            for (int e = 0; e + 1 < topology.BoundaryCount; e += 2)
            {
                int a = boundary[e], b = boundary[e + 1];
                if (a < 0 || b < 0 || a >= count || b >= count) continue;
                if (SegmentCrossesBox(vx[a], vy[a], vx[b], vy[b], ex0, ey0, ex1, ey1)) return false;
            }
            return true;
        }

        internal static bool PointInside(MapIconMeshTopology topology, float px, float py)
        {
            if (topology == null || !topology.Valid) return false;
            float[] vx = topology.VertexX, vy = topology.VertexY;
            int[] tris = topology.Triangles;
            int count = topology.VertexCount;
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                if (a < 0 || b < 0 || c < 0 || a >= count || b >= count || c >= count) continue;
                if (PointInTriangle(px, py, vx[a], vy[a], vx[b], vy[b], vx[c], vy[c])) return true;
            }
            return false;
        }

        private static bool PointInTriangle(float px, float py, float ax, float ay, float bx, float by,
            float cx, float cy)
        {
            float d1 = (px - bx) * (ay - by) - (ax - bx) * (py - by);
            float d2 = (px - cx) * (by - cy) - (bx - cx) * (py - cy);
            float d3 = (px - ax) * (cy - ay) - (cx - ax) * (py - ay);
            bool hasNeg = d1 < 0f || d2 < 0f || d3 < 0f;
            bool hasPos = d1 > 0f || d2 > 0f || d3 > 0f;
            if (hasNeg && hasPos) return false;
            if (!hasNeg && !hasPos)
            {
                // 退化（共线/零面积）三角形：只在点确实落在其线段上时算"内"。
                return PointOnSegment(px, py, ax, ay, bx, by) ||
                       PointOnSegment(px, py, bx, by, cx, cy) ||
                       PointOnSegment(px, py, cx, cy, ax, ay);
            }
            return true;
        }

        private static bool PointOnSegment(float px, float py, float ax, float ay, float bx, float by)
        {
            float cross = (px - ax) * (by - ay) - (py - ay) * (bx - ax);
            if (Math.Abs(cross) > 1e-6f) return false;
            return px >= Math.Min(ax, bx) - 1e-6f && px <= Math.Max(ax, bx) + 1e-6f &&
                   py >= Math.Min(ay, by) - 1e-6f && py <= Math.Max(ay, by) + 1e-6f;
        }

        /// <summary>线段是否穿过盒**内部**（贴边/擦角不算）。</summary>
        private static bool SegmentCrossesBox(float x1, float y1, float x2, float y2,
            float bx0, float by0, float bx1, float by1)
        {
            float dx = x2 - x1, dy = y2 - y1;
            float t0 = 0f, t1 = 1f;
            if (!ClipAxis(dx, bx0 - x1, bx1 - x1, ref t0, ref t1)) return false;
            if (!ClipAxis(dy, by0 - y1, by1 - y1, ref t0, ref t1)) return false;
            return t1 > t0;
        }

        private static bool ClipAxis(float d, float lo, float hi, ref float t0, ref float t1)
        {
            if (Math.Abs(d) < 1e-9f) return lo <= 0f && hi >= 0f;
            float ta = lo / d, tb = hi / d;
            if (ta > tb) { float swap = ta; ta = tb; tb = swap; }
            if (ta > t0) t0 = ta;
            if (tb < t1) t1 = tb;
            return t0 < t1;
        }
    }

    /// <summary>
    /// 资源图标排版（纯函数、确定性、可离线逐值复算）：
    /// - 候选缩放从 1.0 递减到 0.2；正常可读目标 0.42，放不下时逐级降档（最小 0.2）保证
    ///   "已知有源资源全部有位置"；到达 0.2 仍放不下才报告 failed（调用方不得部分展示）；
    /// - 货架式排布，方向可选（总览自顶向下贴岛、详情自底向上贴岛缘），遇遮挡矩形跳格；
    /// - 每条 placement 携带 RequestIndex，供调用方对应原始 request/source。
    /// </summary>
    internal static class MapResourceIconPlanner
    {
        internal static readonly float[] Scales = { 1f, 0.85f, 0.7f, 0.6f, 0.5f, 0.42f, 0.36f, 0.32f, 0.28f, 0.24f, 0.2f };
        internal const int DefaultScaleCount = 7;   // 0.36 及以上为常规档（与 golden 向量一致）
        internal const float Gutter = 1.5f;
        internal const float Margin = 1.5f;

        /// <summary>
        /// 规划全部条目。true = 全部放下；false 时 placements 为尽力结果且 failed&gt;0
        /// （调用方必须丢弃，不得部分展示）。
        /// </summary>
        internal static bool TryPlan(List<MapIconRequest> requests, MapIconSurface surface,
            List<MapIconPlacement> into, out float usedScale, out int failed)
        {
            return TryPlan(requests, surface, into, out usedScale, out failed, false);
        }

        internal static bool TryPlan(List<MapIconRequest> requests, MapIconSurface surface,
            List<MapIconPlacement> into, out float usedScale, out int failed, bool topDown)
            => TryPlan(requests, surface, into, out usedScale, out failed, topDown, Scales.Length);

        /// <summary>
        /// 同上，但把缩放阶梯限制在前 scaleCount 档（调用方可设可读性下限；总览 r7 用
        /// DefaultScaleCount=7，即最小 0.36，不再自动降到 0.2 超小档）。
        /// </summary>
        internal static bool TryPlan(List<MapIconRequest> requests, MapIconSurface surface,
            List<MapIconPlacement> into, out float usedScale, out int failed, bool topDown, int scaleCount)
        {
            usedScale = 0f;
            failed = 0;
            if (into == null) return false;
            into.Clear();
            if (requests == null) return true;
            if (requests.Count == 0) return true;
            if (surface == null || surface.Width <= 0f || surface.Height <= 0f)
            {
                failed = requests.Count;
                return false;
            }

            int count = Math.Max(1, Math.Min(scaleCount, Scales.Length));
            var work = new List<MapIconPlacement>(requests.Count);
            for (int s = 0; s < count; s++)
            {
                float scale = Scales[s];
                int misses = Pack(requests, surface, scale, topDown, work);
                if (misses == 0)
                {
                    into.AddRange(work);
                    usedScale = scale;
                    failed = 0;
                    return true;
                }
                if (s == count - 1)
                {
                    into.AddRange(work);
                    usedScale = scale;
                    failed = misses;
                    return false;
                }
            }
            failed = requests.Count;
            return false;
        }

        /// <summary>按给定缩放装箱；返回无法放置的条目数（work 中为已放置部分）。</summary>
        private static int Pack(List<MapIconRequest> requests, MapIconSurface surface, float scale,
            bool topDown, List<MapIconPlacement> work)
        {
            work.Clear();
            var placed = new List<MapIconBox>(requests.Count);
            int misses = 0;

            float shelfHeight = 0f;
            for (int i = 0; i < requests.Count; i++)
            {
                float h = requests[i].Height * scale;
                if (h > shelfHeight) shelfHeight = h;
            }
            if (shelfHeight <= 0f) shelfHeight = 1f;

            for (int i = 0; i < requests.Count; i++)
            {
                MapIconRequest req = requests[i];
                float w = req.Width * scale;
                float h = req.Height * scale;
                if (w <= 0f || h <= 0f || w > surface.Width - 2f * Margin || h > surface.Height - 2f * Margin)
                {
                    misses++;
                    continue;
                }

                bool ok = false;
                if (topDown)
                {
                    float shelfTop = surface.Y1 - Margin;
                    while (!ok && shelfTop - h >= surface.Y0 + Margin)
                    {
                        float y = shelfTop - h;
                        ok = TryRow(req, req.Width * scale, h, surface, placed, work, i, y, scale);
                        if (!ok) shelfTop -= shelfHeight + Gutter;
                    }
                }
                else
                {
                    float shelfBottom = surface.Y0 + Margin;
                    while (!ok && shelfBottom + h <= surface.Y1 - Margin)
                    {
                        ok = TryRow(req, w, h, surface, placed, work, i, shelfBottom, scale);
                        if (!ok) shelfBottom += shelfHeight + Gutter;
                    }
                }

                if (!ok) misses++;
            }

            return misses;
        }

        private static bool TryRow(MapIconRequest req, float w, float h, MapIconSurface surface,
            List<MapIconBox> placed, List<MapIconPlacement> work, int requestIndex, float y, float scale)
        {
            float x = surface.X0 + Margin;
            while (x + w <= surface.X1 - Margin)
            {
                var box = new MapIconBox(x, y, x + w, y + h);
                float jump = -1f;
                for (int b = 0; b < surface.BlockedCount; b++)
                {
                    MapIconBox blocked = surface.Blocked(b);
                    if (!box.Intersects(blocked, Gutter)) continue;
                    jump = blocked.X1 + Gutter - Margin;
                    break;
                }
                if (jump < 0f)
                {
                    bool clash = false;
                    for (int p = 0; p < placed.Count; p++)
                    {
                        if (box.Intersects(placed[p], Gutter)) { clash = true; break; }
                    }
                    if (!clash && surface.InsideMeshShape(box))
                    {
                        work.Add(new MapIconPlacement(req, requestIndex, x, y, scale));
                        placed.Add(box);
                        return true;
                    }
                }
                x = jump >= 0f ? x + Math.Max(1f, jump - x) : x + 1.5f;
            }
            return false;
        }
    }

    /// <summary>
    /// 遮挡可见性（纯函数；r4）：收集原生遮挡时，可见性相对**当前规划的 view 根**判定 ——
    /// view 根自身与其祖先的 inactive 是分页/菜单隐藏所致，必须忽略；
    /// view 根之下原生明确隐藏的单个对象（activeSelf=false 的装饰/资源）仍视为不可见。
    /// 传入链 = 自节点向上逐级 activeSelf，**不含** view 根。
    /// </summary>
    internal static class MapIconVisibility
    {
        internal static bool Visible(MapIconPlanBoolChain chain)
        {
            for (int i = 0; i < chain.Count; i++)
            {
                if (!chain[i]) return false;
            }
            return true;
        }
    }

    /// <summary>零分配的 bool 链（沿 Transform 父链写入，避免每帧分配）。</summary>
    internal sealed class MapIconPlanBoolChain
    {
        private readonly bool[] _items;
        private int _count;

        internal MapIconPlanBoolChain(int capacity) { _items = new bool[capacity < 4 ? 4 : capacity]; }

        internal int Count => _count;
        internal bool this[int index] => _items[index];
        internal void Clear() { _count = 0; }
        internal void Add(bool value)
        {
            if (_count >= _items.Length) return;   // 链深有界（UI 层级 << 容量）
            _items[_count++] = value;
        }
    }

    /// <summary>
    /// 图标指纹（纯函数；r4 从 Unity 层移入以便直测）：同 land/overview/requests 必须稳定相等，
    /// 这是"切成可见后同 fingerprint 只 Repaint"成立的前提。
    /// </summary>
    internal static class MapIconPlanFingerprint
    {
        internal static int Compute(int landIndex, bool overview, List<MapIconRequest> requests)
        {
            unchecked
            {
                int hash = landIndex * 397 ^ (overview ? 0x5BD1 : 0x1234);
                if (requests != null)
                {
                    for (int i = 0; i < requests.Count; i++)
                    {
                        hash = hash * 31 + (int)requests[i].Kind;
                        hash = hash * 31 + requests[i].TypeId;
                        hash = hash * 31 + requests[i].ArrayIndex;
                    }
                }
                return hash;
            }
        }
    }

    /// <summary>生产入口动作（r9 生命周期修正）：把"扩展地图必需几何"与"可关闭的资源 icons"分开。</summary>
    internal enum MapOverviewAction
    {
        /// <summary>完整还原（无注册 / 从未访问且 OFF / 非本菜单且无缓存几何）。</summary>
        Teardown = 0,
        /// <summary>保留扩展地图必需几何（底部带 + 原 10 统一 shift + 真实 paper/mask），撤自有 icons、还原原生槽。</summary>
        LayoutWithoutIcons = 1,
        /// <summary>几何 + 自有资源 icons。</summary>
        LayoutWithIcons = 2,
    }

    /// <summary>
    /// 生命周期裁决（纯函数；与实际 OnLandUpdated/OnClusterEnabled 入口共用同一决策表，可离线逐格复算）：
    /// - icons 开关 ON：按既有行为（几何 + 自有 icons）；
    /// - OFF：仅当"有 exact 注册扩展 **且** 该 campaign 可用（门开着 或 current==11 或 visited 含 11）"
    ///   才保留必需几何（供恢复/返航）；否则完整还原回原生几何；
    /// - 非本菜单（未知 owner / 其他 map）：OFF 且缓存几何也不再需要才清理，否则保持不动（不被后续 Teardown 撤回）。
    /// </summary>
    internal static class MapOverviewLifecycle
    {
        internal static MapOverviewAction DecideForOwnedLand(bool extensionRegistered, bool iconsEnabled,
            bool extensionAvailable)
        {
            if (iconsEnabled) return MapOverviewAction.LayoutWithIcons;
            if (extensionRegistered && extensionAvailable) return MapOverviewAction.LayoutWithoutIcons;
            return MapOverviewAction.Teardown;
        }

        internal static bool ShouldTeardownOnForeignLand(bool iconsEnabled, bool cachedGeometryRequired)
            => !iconsEnabled && !cachedGeometryRequired;
    }

    /// <summary>
    /// 视口测量阈值与周期上限（纯策略；r11）——**像素阈值与 UI 阈值分开**：
    /// - 屏幕空间交集必须 ≥ MinVisiblePixels（像素）；否则视为测量失败（不是"全纸可见"）；
    /// - 投影回 paper 本地的矩形边长必须 ≥ MinVisibleUiSide（UI 单位）；已知薄矩形被拒绝为测量失败，
    ///   但**绝不**被扩大成整张 paper（由 EffectiveContent 的空盒语义保证）；
    /// - pending 周期最多 MaxProbeFrames 次跨帧测量，之后有界停止（等待 stamp 变化再开新周期）。
    /// </summary>
    internal static class MapViewportPolicy
    {
        internal const float MinVisiblePixels = 16f;
        internal const float MinVisibleUiSide = 8f;
        internal const int MaxProbeFrames = 4;

        internal static bool AcceptScreenIntersection(float pixelWidth, float pixelHeight)
            => pixelWidth >= MinVisiblePixels && pixelHeight >= MinVisiblePixels;

        internal static bool AcceptPaperRect(in MapIconBox paperRect)
            => paperRect.Width >= MinVisibleUiSide && paperRect.Height >= MinVisibleUiSide;
    }

    /// <summary>
    /// 布局稳定校验（纯函数；r10/r11）：视口签名变化时，需要**不同帧**的两次一致测量才提交
    /// （同一帧的多个 UILand 回调/同一帧 tick 不能推进稳定计数；首次也不提前成功）。
    /// 提交后同签名 Keep；pending 由 runtime 限 MaxProbeFrames 次；签名包含有效矩形 + paper + 屏幕尺寸。
    /// </summary>
    internal static class MapOverviewCache
    {
        internal enum Action
        {
            Keep = 0,
            ApplyNow = 1,
            Defer = 2,
        }

        internal static Action Decide(bool layoutApplied, long measuredSignature, long appliedSignature,
            long pendingSignature, int measuredFrame, int pendingFrame)
        {
            if (layoutApplied && measuredSignature == appliedSignature) return Action.Keep;
            if (measuredSignature == pendingSignature && measuredFrame != pendingFrame) return Action.ApplyNow;
            return Action.Defer;
        }

        /// <summary>视口几何签名（量化 0.25 UI 单位；含 paper 尺寸/位置与屏幕尺寸）。</summary>
        internal static long Signature(in MapIconBox visiblePaper, float paperW, float paperH,
            int screenW, int screenH)
        {
            unchecked
            {
                long h = 17L;
                h = h * 31 + screenW;
                h = h * 31 + screenH;
                h = h * 31 + (long)Math.Round(paperW * 4f);
                h = h * 31 + (long)Math.Round(paperH * 4f);
                h = h * 31 + (long)Math.Round(visiblePaper.X0 * 4f);
                h = h * 31 + (long)Math.Round(visiblePaper.Y0 * 4f);
                h = h * 31 + (long)Math.Round(visiblePaper.X1 * 4f);
                h = h * 31 + (long)Math.Round(visiblePaper.Y1 * 4f);
                return h;
            }
        }
    }

    /// <summary>
    /// 当前浏览 reign 的取数索引（纯函数；r13/R3）：geometry 提交后的一次 native overview 刷新用它。
    /// 合同（native 实测）：focus==0 → 当前 campaign 的 currentReign；focus&gt;0 → previousReigns[Count−focus]
    /// （1=最近历史，Count=最早）；focus&lt;0 / previous 为空 / 越界 → 拒绝——**绝不回退 currentReign**。
    /// </summary>
    internal static class MapOverviewRefresh
    {
        /// <summary>previousIndex == CurrentReign 表示用 campaign.currentReign（仅 focus==0）；
        /// 失败时 previousIndex = NoIndex（int.MinValue，绝不等同 CurrentReign），调用方必须检查返回 bool。</summary>
        internal const int CurrentReign = -1;
        internal const int NoIndex = int.MinValue;

        internal static bool TryResolvePreviousIndex(int focus, int previousCount, out int previousIndex)
        {
            previousIndex = NoIndex;
            if (focus < 0) return false;
            if (focus == 0) { previousIndex = CurrentReign; return true; }
            int index = previousCount - focus;
            if (index < 0 || index >= previousCount) return false;
            previousIndex = index;
            return true;
        }
    }

    /// <summary>
    /// world0 完整物理纸布局域（r15/mount-island-capacity，FULL-PAPER-CONTRACT）：
    /// 输入 = runtime 已按实际物理纸/屏幕/两个确证 RectMask2D 测得并 roundtrip 验证过的 world 域
    /// （paper 本地、允许负起点或 X1&gt;paperW，**不再夹回 0..paperW**）。本类只做：
    /// - 从同一域共同分区：顶部真实按钮带、外边距、底部 extension 预留带 + 间隔 → 上部原 10 区；
    /// - 原 10 用**同一个**正倍率 + 整体平移绝对装入上部区（两两差向量同乘，方向/比例保持，允许放大到 maxScale）；
    /// - 扩展簇 banner 区（底部预留带）。
    /// 不参考任何上一轮 target（绝对重算）；不改 MapOverviewLayout 既有函数/常量/输出。
    /// </summary>
    internal static class MapWorldLayout
    {
        internal const float MaxUniformScale = MapOverviewLayout.MaxUniformScale;
        internal const float DefaultExtensionReserve = 78f;   // R7 合同：底部预留约 78 UI（300x200 下 cap 80.1 真实纳入，不私改 cap）
        internal const float ExtensionGap = 6f;               // 宽岛与上部群岛的明确间隔
        internal const float SafeMargin = 2f;

        /// <summary>共同分区（raw：不夹回 paper）：world 域 → 上部原 10 区 + 底部扩展 banner 区。</summary>
        internal static bool ComposeDomains(in MapIconBox worldVisible, float buttonBand,
            float extensionReserve, out MapIconBox upper, out MapIconBox band)
        {
            upper = default;
            band = default;
            float x0 = worldVisible.X0 + SafeMargin;
            float x1 = worldVisible.X1 - SafeMargin;
            float y0 = worldVisible.Y0 + SafeMargin;
            float y1 = worldVisible.Y1 - buttonBand - SafeMargin;   // 顶部真实按钮安全带
            if (x1 <= x0 || y1 <= y0) return false;
            float reserve = extensionReserve;
            if (reserve <= 0f)
            {
                // 无扩展簇：不预留底部带，上部区 = 全部内容（raw，不夹回 paper）。
                upper = new MapIconBox(x0, y0, x1, y1);
                band = default;
                return true;
            }
            float maxReserve = (y1 - y0) * 0.45f;
            if (reserve > maxReserve) reserve = maxReserve;
            if (reserve <= 0f) return false;
            float bandTop = y0 + reserve;
            float upperBottom = bandTop + ExtensionGap;
            if (upperBottom >= y1) return false;
            band = new MapIconBox(x0, y0, x1, bandTop);
            upper = new MapIconBox(x0, upperBottom, x1, y1);
            return true;
        }

        /// <summary>
        /// 原 10 簇：从冻结原始几何**绝对**计算一个 scale + translation 装入 area（raw 域，不做 band 扣减/夹取）。
        /// target_i 与 target_j 的差 = scale × (orig_i − orig_j)（pivot 与 art 中心皆然）；scale ≤ MaxUniformScale。
        /// </summary>
        internal static bool TryFitNatives(List<MapOverviewClusterInput> natives, float paperW, float paperH,
            in MapIconBox area, List<MapOverviewClusterTarget> into, out float uniformScale)
        {
            uniformScale = MaxUniformScale;
            if (into == null) return false;
            into.Clear();
            if (natives == null || natives.Count == 0 || paperW <= 0f || paperH <= 0f) return false;
            if (area.Width <= 0f || area.Height <= 0f) return false;

            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < natives.Count; i++)
            {
                if (!MapOverviewLayout.TryScale1Box(natives[i], paperW, paperH, out MapIconBox box)) return false;
                if (box.X0 < minX) minX = box.X0;
                if (box.Y0 < minY) minY = box.Y0;
                if (box.X1 > maxX) maxX = box.X1;
                if (box.Y1 > maxY) maxY = box.Y1;
            }
            float groupW = maxX - minX;
            float groupH = maxY - minY;
            if (groupW <= 0.0001f || groupH <= 0.0001f) return false;

            float scale = Math.Min(MaxUniformScale, Math.Min(area.Width / groupW, area.Height / groupH));
            if (!(scale > 0f)) return false;

            float groupCenterX = (minX + maxX) * 0.5f;
            float groupCenterY = (minY + maxY) * 0.5f;
            float areaCenterX = (area.X0 + area.X1) * 0.5f;
            float areaCenterY = (area.Y0 + area.Y1) * 0.5f;

            for (int i = 0; i < natives.Count; i++)
            {
                MapOverviewClusterInput input = natives[i];
                float pivotX = input.OrigX + paperW * 0.5f;
                float pivotY = input.OrigY + paperH * 0.5f;
                into.Add(new MapOverviewClusterTarget(
                    areaCenterX + scale * (pivotX - groupCenterX) - paperW * 0.5f,
                    areaCenterY + scale * (pivotY - groupCenterY) - paperH * 0.5f,
                    input.OrigScaleX * scale,
                    input.OrigScaleY * scale,
                    input.OrigScaleZ <= 0f ? 1f : input.OrigScaleZ));
            }
            uniformScale = scale;
            return true;
        }

        /// <summary>前后测量域等价（±tol UI；world 域可以是负起点/X1&gt;paperW，不做夹取）——post-final-geometry 用。</summary>
        internal static bool DomainsEquivalent(in MapIconBox a, in MapIconBox b, float tol)
            => Math.Abs(a.X0 - b.X0) <= tol && Math.Abs(a.Y0 - b.Y0) <= tol &&
               Math.Abs(a.X1 - b.X1) <= tol && Math.Abs(a.Y1 - b.Y1) <= tol;

        /// <summary>
        /// 扩展 banner 的图标区（banner 区内缩 margin）。用户 2026-10-03 直接要求取消旧的
        /// “同档 reserved 空状态车道优先”：不再为状态/船标预留空车道，真实 native 图形仍以 blockers 参与碰撞。
        /// </summary>
        internal static MapIconBox IconAreaOf(in MapIconBox banner)
        {
            return new MapIconBox(banner.X0 + MapExtensionIslandLayout.Margin, banner.Y0 + MapExtensionIslandLayout.Margin,
                banner.X1 - MapExtensionIslandLayout.Margin, banner.Y1 - MapExtensionIslandLayout.Margin);
        }
    }

    /// <summary>
    /// 岸线像素 mask（纯数据；来自实际 Sprite.rect 画布，含 2px 透明 padding）。
    /// 用积分图实现 O(1) 的“整个矩形是否全部在岸内”终检——逐像素覆盖，不是 4 角/bbox 采样。
    /// </summary>
    internal sealed class MapShoreMask
    {
        internal readonly int Width;
        internal readonly int Height;
        private readonly int[] _integral;

        internal MapShoreMask(int width, int height, bool[] inside)
        {
            Width = width < 0 ? 0 : width;
            Height = height < 0 ? 0 : height;
            _integral = new int[(Width + 1) * (Height + 1)];
            for (int y = 0; y < Height; y++)
            {
                int rowSum = 0;
                for (int x = 0; x < Width; x++)
                {
                    bool on = inside != null && y * Width + x < inside.Length && inside[y * Width + x];
                    if (on) rowSum++;
                    _integral[(y + 1) * (Width + 1) + (x + 1)] = _integral[y * (Width + 1) + (x + 1)] + rowSum;
                }
            }
        }

        internal bool Sample(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) return false;
            int stride = Width + 1;
            int sum = _integral[(y + 1) * stride + (x + 1)] - _integral[y * stride + (x + 1)]
                    - _integral[(y + 1) * stride + x] + _integral[y * stride + x];
            return sum > 0;
        }

        /// <summary>闭区间像素矩形 [x0..x1]×[y0..y1] 是否全部位于岸内（含越界拒绝）。</summary>
        internal bool RectAllInside(int x0, int y0, int x1, int y1)
        {
            if (x0 < 0 || y0 < 0 || x1 < x0 || y1 < y0 || x1 >= Width || y1 >= Height) return false;
            int stride = Width + 1;
            int sum = _integral[(y1 + 1) * stride + (x1 + 1)] - _integral[y0 * stride + (x1 + 1)]
                    - _integral[(y1 + 1) * stride + x0] + _integral[y0 * stride + x0];
            return sum == (x1 - x0 + 1) * (y1 - y0 + 1);
        }
    }

    /// <summary>
    /// 新岸线（KEM_MapExtensionIsland）展示几何（纯函数）：
    /// - 比例一律取**实际 Sprite.rect**（228×84，含透明 padding ⇒ 2.7143）——不是 raw alpha bbox
    ///   （1807/643 ⇒ 2.8103），也不是旧 native 114×82；world/detail 都用同一 aspect 的等比框
    ///   （两个视图的整体 scale 可以不同）；
    /// - world：底部预留带内等比居中（band 高度通常为瓶颈，宽度富余）；
    /// - detail：实测 legend 最右元素（Keep 右缘 −96）右侧留水道；宽 ≤230、右缘不出 282 页域；
    ///   Y 由调用方按页域/blockers 给的 centerY 决定。
    /// </summary>
    internal static class MapExtensionShapePlan
    {
        internal const float Margin = 1.5f;

        // detail 实测（review/detail-native-sidebar-metrics.json）：page 282 → [-141,141]；
        // Keep 中心 x=−107.5、宽 23 → 右缘 −96；Portals/Gems/Decay 更靠左。
        internal const float DetailPageHalfWidth = 141f;
        /// <summary>detail 页域半高（actual 页 282×196；land root 180×150 居中其中）。</summary>
        internal const float DetailPageHalfHeight = 98f;
        internal const float DetailLegendRight = -96f;
        internal const float DetailWaterGap = 4f;
        internal const float DetailMaxWidth = 230f;
        internal const float DetailRightMargin = 3f;

        internal static bool TryFitAspect(float maxWidth, float maxHeight, float aspect,
            out float width, out float height)
        {
            width = 0f;
            height = 0f;
            if (!(maxWidth > 0f) || !(maxHeight > 0f) || !(aspect > 0f)) return false;
            width = Math.Min(maxWidth, maxHeight * aspect);
            height = width / aspect;
            return width > 0f && height > 0f;
        }

        /// <summary>world 底部带：按实际 Sprite.rect aspect 等比居中放入 band（上下/两侧留 margin）。</summary>
        internal static bool TryPlanWorldBox(in MapIconBox band, float margin, float aspect, out MapIconBox box)
        {
            box = default;
            if (band.Width <= 0f || band.Height <= 0f || margin < 0f) return false;
            if (!TryFitAspect(band.Width - 2f * margin, band.Height - 2f * margin, aspect,
                    out float width, out float height)) return false;
            float cx = (band.X0 + band.X1) * 0.5f;
            float cy = (band.Y0 + band.Y1) * 0.5f;
            box = new MapIconBox(cx - width * 0.5f, cy - height * 0.5f, cx + width * 0.5f, cy + height * 0.5f);
            return true;
        }

        /// <summary>
        /// detail 框（坐标相对 land 中心）：左缘 = legend 右缘 + 水道；宽 = min(230, 右限−左缘)；
        /// 等比高、垂直中心 = centerY。gap = 左缘 − legendRight（应 = DetailWaterGap）。
        /// </summary>
        internal static bool TryPlanDetailBox(float pageHalfWidth, float legendRight, float waterGap,
            float maxWidth, float aspect, float centerY, out MapIconBox box, out float gap)
        {
            box = default;
            gap = 0f;
            if (!(aspect > 0f) || !(pageHalfWidth > 0f) || !(maxWidth > 0f) || waterGap < 0f) return false;
            float left = legendRight + waterGap;
            float rightLimit = pageHalfWidth - DetailRightMargin;
            float width = Math.Min(maxWidth, rightLimit - left);
            if (!(width > 0f)) return false;
            float right = left + width;
            if (right > rightLimit + 0.001f) return false;
            float height = width / aspect;
            box = new MapIconBox(left, centerY - height * 0.5f, right, centerY + height * 0.5f);
            gap = left - legendRight;
            return true;
        }
    }

    /// <summary>
    /// 扩展岛（exact 登记 physical11 总览簇）专属：**岛内自由错落**（用户 2026-10-03 直接要求取代
    /// 三行/6,5,5 —— 完整剪影位于岛内顶面、不成行、右侧实际利用）。
    /// 只服务扩展岛；native 十岛走各自底图盒（MapMountIcons.PlanOverviewIsland/PlanNativeDetailIsland，
    /// 不改其常量/输出）。
    /// 语义：
    /// - 请求按稳定序（面积降→高降→宽降→Kind→TypeId→ArrayIndex→原下标）排位，第 s 位固定使用第 s 个
    ///   normalized preferred anchor（常量表；同输入逐值一致，与调用顺序无关）；
    /// - 每槽可有一个固定 secondary anchor（有限回退：主锚被真实 blocker/已放置 footprint 挡住时使用）；
    /// - 候选位置只落在整数 prepared 画布像素格（有 mask 时 = 画布像素；无 mask 的纯矩形调用用
    ///   228×84 虚拟格）：确定性、按整格平移等变；
    /// - **有界确定性局部修正**：以 preferred 为基点按 Chebyshev 半径逐环搜索（≤ SearchRadiusPx），
    ///   取该环内 (距², 距¹, dy, dx) 字典序最小的可行点；无随机数、无无界搜索、无逐帧随机；
    /// - 可行 = 完整 footprint 在 area 内 + 逐像素位于传入 mask（**顶面 PlacementMask**：中心 1px
    ///   空洞/崖面像素都必须拒绝，只查 4 角/bbox 不够）+ 无 blocker 相交 + 与已放置 footprint 保持 Gutter；
    /// - 真实请求**全部**放下才返回 true（绝不部分显示/截断/补假）；RequestIndex = 原 requests 下标；
    /// - scale 取 ScaleLadder 上第一个全量可行档（≥ minScale）；失败 → placements 清空、failed = 全数。
    /// 锚点常量由独立离线推导（review/scatter-design 的固定 seed probe 解为种子 + 确定性坐标下降；
    /// 推导脚本与输出留 evidence/scatter-run/sim/），生产端只读常量、运行期零随机。
    /// </summary>
    internal static class MapExtensionIslandLayout
    {
        internal const float PreferScale = 0.6f;
        internal const float MinScale = 0.36f;
        /// <summary>容量上限：真实集 16；>MaxItems 一律整体 fail-closed（不截断）。</summary>
        internal const int MaxItems = 18;
        internal static readonly float[] ScaleLadder = { 1f, 0.85f, 0.7f, 0.6f, 0.5f, 0.42f, 0.36f };
        internal const float Gutter = 1.5f;
        internal const float Margin = 1.5f;
        /// <summary>有界修正半径（prepared 像素）：锚点=优选位置，只做有界邻域纠偏。</summary>
        internal const int SearchRadiusPx = 20;
        /// <summary>无 mask（纯矩形容量调用）时的虚拟像素格尺寸。</summary>
        internal const int PlacementGridWidth = 228;
        internal const int PlacementGridHeight = 84;

        /// <summary>固定 normalized preferred anchors（x = 列/宽，y = 自底向上/高），按稳定排位下标取用；
        /// 18 槽 = 16 真实集 + 2 备用（真实集永不用到备用槽：17/18 项时才使用）。</summary>
        internal static readonly float[] PreferredAnchorX =
        {
            0.307018f, 0.587719f, 0.798246f, 0.289474f, 0.412281f, 0.657895f, 0.149123f, 0.464912f,
            0.201754f, 0.447368f, 0.5f, 0.885965f, 0.394737f, 0.552632f, 0.570175f, 0.745614f, 0.44f,
            0.62f
        };

        internal static readonly float[] PreferredAnchorY =
        {
            0.571429f, 0.666667f, 0.47619f, 0.809524f, 0.571429f, 0.380952f, 0.714286f, 0.333333f,
            0.52381f, 0.47619f, 0.666667f, 0.47619f, 0.761905f, 0.285714f, 0.428571f, 0.714286f, 0.3f,
            0.55f
        };

        /// <summary>有限回退锚（NaN = 无第二候选）。仅主锚被真实障碍（如 detail 船标）挡住时需要。</summary>
        internal static readonly float[] FallbackAnchorX =
        {
            0.3241f, 0.587719f, 0.815789f, 0.307018f, 0.41727f, 0.67249f, 0.184211f, 0.482456f,
            0.710526f, 0.692982f, 0.47915f, 0.868421f, 0.30976f, 0.552632f, 0.75321f, 0.6067f,
            float.NaN, float.NaN
        };

        internal static readonly float[] FallbackAnchorY =
        {
            0.68176f, 0.619048f, 0.47619f, 0.809524f, 0.75545f, 0.37675f, 0.52381f, 0.333333f,
            0.571429f, 0.571429f, 0.53151f, 0.47619f, 0.84683f, 0.333333f, 0.40282f, 0.69207f,
            float.NaN, float.NaN
        };

        internal static bool TryPlan(in MapIconBox area, List<MapIconRequest> requests, List<MapIconBox> blockers,
            List<MapIconPlacement> placements, out float scale, out int failed)
            => TryPlan(area, requests, blockers, default, null, placements, out scale, out failed);

        /// <summary>
        /// 规划入口。mask = 顶面 PlacementMask（MapShoreMask；含透明 padding 的画布像素坐标）；
        /// shoreFrame = 该 mask 在 paper 的映射框。mask/shoreFrame 缺省时按纯矩形（虚拟像素格）容量语义。
        /// true ⇒ placements 完整（每项 RequestIndex = 原下标）；false ⇒ placements 清空、failed = requests.Count。
        /// </summary>
        internal static bool TryPlan(in MapIconBox area, List<MapIconRequest> requests, List<MapIconBox> blockers,
            in MapIconBox shoreFrame, MapShoreMask mask, List<MapIconPlacement> placements, out float scale,
            out int failed, float minScale = MinScale)
        {
            placements.Clear();
            scale = 0f;
            failed = requests == null ? 0 : requests.Count;
            if (requests == null || requests.Count == 0) { failed = 0; return true; }   // 合法空集
            if (area.Width <= 0f || area.Height <= 0f) return false;
            if (requests.Count > MaxItems) return false;

            var order = new List<int>(requests.Count);
            for (int i = 0; i < requests.Count; i++) order.Add(i);
            order.Sort((x, y) => CompareRequests(requests[x], requests[y], x, y));

            for (int si = 0; si < ScaleLadder.Length; si++)
            {
                float candidate = ScaleLadder[si];
                if (candidate < minScale) break;
                if (TryPlaceAll(area, candidate, requests, order, blockers, shoreFrame, mask, placements))
                {
                    scale = candidate;
                    failed = 0;
                    return true;
                }
            }
            placements.Clear();
            return false;
        }

        /// <summary>稳定排位：面积降→高降→宽降→Kind→TypeId→ArrayIndex→原下标（全序，List.Sort 不稳定也可复现）。</summary>
        private static int CompareRequests(MapIconRequest a, MapIconRequest b, int indexA, int indexB)
        {
            float areaA = a.Width * a.Height;
            float areaB = b.Width * b.Height;
            if (areaA != areaB) return areaB.CompareTo(areaA);
            if (a.Height != b.Height) return b.Height.CompareTo(a.Height);
            if (a.Width != b.Width) return b.Width.CompareTo(a.Width);
            if (a.Kind != b.Kind) return a.Kind.CompareTo(b.Kind);
            if (a.TypeId != b.TypeId) return a.TypeId.CompareTo(b.TypeId);
            if (a.ArrayIndex != b.ArrayIndex) return a.ArrayIndex.CompareTo(b.ArrayIndex);
            return indexA.CompareTo(indexB);
        }

        /// <summary>单个 scale 档：按排位逐项落位；任一项无可行点 → false（该档整体丢弃，调用方试下一档）。</summary>
        private static bool TryPlaceAll(in MapIconBox area, float scale, List<MapIconRequest> requests,
            List<int> order, List<MapIconBox> blockers, in MapIconBox shoreFrame, MapShoreMask mask,
            List<MapIconPlacement> placements)
        {
            placements.Clear();
            bool pixelMask = mask != null && mask.Width > 0 && mask.Height > 0 &&
                             shoreFrame.Width > 0f && shoreFrame.Height > 0f;
            MapIconBox reference = pixelMask ? shoreFrame : area;
            float gridW = pixelMask ? mask.Width : PlacementGridWidth;
            float gridH = pixelMask ? mask.Height : PlacementGridHeight;
            float sx = gridW / reference.Width;      // prepared 像素 per UI
            float sy = gridH / reference.Height;
            if (!(sx > 0f) || !(sy > 0f)) return false;

            for (int slot = 0; slot < order.Count; slot++)
            {
                int index = order[slot];
                MapIconRequest req = requests[index];
                float w = req.Width * scale;
                float h = req.Height * scale;
                if (!(w > 0f) || !(h > 0f)) return false;
                float wpx = w * sx;
                float hpx = h * sy;

                bool placed = false;
                for (int pass = 0; pass < 2 && !placed; pass++)
                {
                    if (!TryAnchor(slot, pass, out float ax, out float ay)) continue;
                    int baseX = (int)Math.Floor(ax * gridW - wpx * 0.5f + 0.5f);
                    int baseY = (int)Math.Floor(ay * gridH - hpx * 0.5f + 0.5f);
                    for (int radius = 0; radius <= SearchRadiusPx && !placed; radius++)
                    {
                        int bestD2 = int.MaxValue, bestL1 = int.MaxValue, bestDy = 0, bestDx = 0;
                        float bestX = 0f, bestY = 0f;
                        for (int dy = -radius; dy <= radius; dy++)
                        {
                            int ady = dy < 0 ? -dy : dy;
                            for (int dx = -radius; dx <= radius; dx++)
                            {
                                int adx = dx < 0 ? -dx : dx;
                                if ((adx > ady ? adx : ady) != radius) continue;   // 只扫本环
                                if (!TryCandidate(baseX + dx, baseY + dy, w, h, sx, sy, reference, area,
                                        blockers, mask, pixelMask, placements, out float cx, out float cy))
                                {
                                    continue;
                                }
                                int d2 = dx * dx + dy * dy;
                                int l1 = adx + ady;
                                if (d2 > bestD2) continue;
                                if (d2 == bestD2 && (l1 > bestL1 || (l1 == bestL1 &&
                                        (dy > bestDy || (dy == bestDy && dx >= bestDx))))) continue;
                                bestD2 = d2; bestL1 = l1; bestDy = dy; bestDx = dx; bestX = cx; bestY = cy;
                            }
                        }
                        if (bestD2 != int.MaxValue)
                        {
                            placements.Add(new MapIconPlacement(req, index, bestX, bestY, scale));
                            placed = true;
                        }
                    }
                }
                if (!placed) return false;
            }
            return true;
        }

        private static bool TryAnchor(int slot, int pass, out float x, out float y)
        {
            x = 0f;
            y = 0f;
            float[] tableX = pass == 0 ? PreferredAnchorX : FallbackAnchorX;
            float[] tableY = pass == 0 ? PreferredAnchorY : FallbackAnchorY;
            if (tableX == null || tableY == null || slot < 0 || slot >= tableX.Length || slot >= tableY.Length)
            {
                return false;
            }
            x = tableX[slot];
            y = tableY[slot];
            return !float.IsNaN(x) && !float.IsNaN(y);
        }

        /// <summary>候选（格点整数位）是否可行；可行时输出该 footprint 的 paper 左下角。
        /// mask 覆盖直接用整数格点 + ceil(像素尺寸)（不经过浮点回读：位置本就量化到整数 px，
        /// 回读的 ±1ulp 会在像素边界翻转覆盖 → 与整点语义不一致）。</summary>
        private static bool TryCandidate(int ix, int iy, float w, float h, float sx, float sy,
            in MapIconBox reference, in MapIconBox area, List<MapIconBox> blockers, MapShoreMask mask,
            bool pixelMask, List<MapIconPlacement> placements, out float x, out float y)
        {
            x = reference.X0 + ix / sx;
            y = reference.Y0 + iy / sy;
            var box = new MapIconBox(x, y, x + w, y + h);
            if (box.X0 < area.X0 - 0.01f || box.Y0 < area.Y0 - 0.01f ||
                box.X1 > area.X1 + 0.01f || box.Y1 > area.Y1 + 0.01f)
            {
                return false;
            }
            if (blockers != null)
            {
                for (int b = 0; b < blockers.Count; b++)
                {
                    if (Intersects(box, blockers[b], 0.01f)) return false;
                }
            }
            if (pixelMask)
            {
                int px1 = ix + (int)Math.Ceiling(w * sx) - 1;
                int py1 = iy + (int)Math.Ceiling(h * sy) - 1;
                if (!mask.RectAllInside(ix, iy, px1, py1)) return false;
            }
            for (int p = 0; p < placements.Count; p++)
            {
                MapIconPlacement other = placements[p];
                var otherBox = new MapIconBox(other.X, other.Y,
                    other.X + other.Request.Width * other.Scale, other.Y + other.Request.Height * other.Scale);
                if (WithinGutter(box, otherBox)) return false;
            }
            return true;
        }

        /// <summary>两 footprint 间隙 &lt; Gutter 即视为冲突（贴合间距 = Gutter 允许）。</summary>
        private static bool WithinGutter(in MapIconBox a, in MapIconBox b)
            => a.X0 - Gutter < b.X1 && b.X0 < a.X1 + Gutter && a.Y0 - Gutter < b.Y1 && b.Y0 < a.Y1 + Gutter;

        /// <summary>
        /// 整 footprint mask 终检：box（paper）→ 画布像素（含 padding）→ 逐像素覆盖。
        /// 像素包围盒取 floor/ceil（保守放大到完整像素格），避免浮点边界漏检；位置由 planner 量化到
        /// 整数像素格 ⇒ 边界恰好对齐，存储/重建的浮点回读噪声（&lt;1e-3 px）不得翻转判定：
        /// 先按 Align 对齐到最近的像素边界再 floor/ceil（≫Align 的真实越界仍被拒绝）。
        /// </summary>
        internal static bool FootprintInsideShore(in MapIconBox box, in MapIconBox shoreFrame, MapShoreMask mask)
        {
            if (mask == null || mask.Width <= 0 || mask.Height <= 0) return false;
            if (shoreFrame.Width <= 0f || shoreFrame.Height <= 0f) return false;
            const float Align = 1e-3f;
            float sx = mask.Width / shoreFrame.Width;
            float sy = mask.Height / shoreFrame.Height;
            int x0 = (int)Math.Floor((box.X0 - shoreFrame.X0) * sx + Align);
            int y0 = (int)Math.Floor((box.Y0 - shoreFrame.Y0) * sy + Align);
            int x1 = (int)Math.Ceiling((box.X1 - shoreFrame.X0) * sx - Align) - 1;
            int y1 = (int)Math.Ceiling((box.Y1 - shoreFrame.Y0) * sy - Align) - 1;
            return mask.RectAllInside(x0, y0, x1, y1);
        }

        private static bool Intersects(in MapIconBox a, in MapIconBox b, float eps)
            => a.X0 < b.X1 - eps && b.X0 < a.X1 - eps && a.Y0 < b.Y1 - eps && b.Y0 < a.Y1 - eps;
    }


    /// <summary>总览展示动作（world0 只显示群岛：只操作自有 CanvasGroup）。</summary>
    internal enum MapPresentationAction
    {
        /// <summary>已是目标态：O(1) 无写入。</summary>
        None = 0,
        /// <summary>alpha=0 + blocksRaycasts=false（world0 隐藏兄弟详情）。</summary>
        Hide = 1,
        /// <summary>中性 alpha=1 + blocksRaycasts=true（single1/清理前）。</summary>
        Show = 2,
        /// <summary>先中性化再销毁自有组（scope 失效/未知 state/绑定失效/外部组）。</summary>
        Withdraw = 3,
    }

    /// <summary>决策原因（诊断用；也是纯测试的可区分输出）。</summary>
    internal enum MapPresentationReason
    {
        AlreadyApplied = 0,
        Applied = 1,
        NewGroup = 2,
        ScopeInactive = 3,
        BindingInvalid = 4,
        ExternalGroup = 5,
        UnknownState = 6,
        ReadFault = 7,
    }

    /// <summary>
    /// 总览只显示群岛（r14/WORLD-OVERVIEW-ONLY）的纯决策：只读 native 事实（enum 仅 ShowingWorld=0 /
    /// ShowingSingleIsland=1，无第二阶段值）与有效性输入，输出动作；**不做任何写、不解析实例**。
    /// 调用方先捕获自有组引用，再按动作中性化/销毁；未知与读取异常一律"撤"，绝不盲隐藏。
    /// </summary>
    internal static class MapOverviewPresentationPolicy
    {
        internal const int ShowingWorld = 0;
        internal const int ShowingSingleIsland = 1;

        internal const float NeutralAlpha = 1f;
        internal const float HiddenAlpha = 0f;
        internal const bool NeutralBlocksRaycasts = true;
        internal const bool HiddenBlocksRaycasts = false;

        /// <summary>状态 → 期望展示（world0 隐藏大岛详情；single1 中性显示）；其它值无期望（调用方撤）。</summary>
        internal static bool TryDesiredVisible(int worldState, out bool visible)
        {
            if (worldState == ShowingWorld) { visible = false; return true; }
            if (worldState == ShowingSingleIsland) { visible = true; return true; }
            visible = true;
            return false;
        }

        internal static MapPresentationAction Decide(bool readFault, bool scopeActive, bool bindingValid,
            bool externalGroup, bool groupExists, int worldState, bool appliedHidden,
            out MapPresentationReason reason)
        {
            if (readFault) { reason = MapPresentationReason.ReadFault; return MapPresentationAction.Withdraw; }
            if (!bindingValid) { reason = MapPresentationReason.BindingInvalid; return MapPresentationAction.Withdraw; }
            if (externalGroup) { reason = MapPresentationReason.ExternalGroup; return MapPresentationAction.Withdraw; }
            if (!scopeActive) { reason = MapPresentationReason.ScopeInactive; return MapPresentationAction.Withdraw; }
            if (!TryDesiredVisible(worldState, out bool visible))
            {
                reason = MapPresentationReason.UnknownState;
                return MapPresentationAction.Withdraw;
            }
            if (!groupExists)
            {
                reason = MapPresentationReason.NewGroup;
                return visible ? MapPresentationAction.Show : MapPresentationAction.Hide;
            }
            if (visible == !appliedHidden)
            {
                reason = MapPresentationReason.AlreadyApplied;
                return MapPresentationAction.None;
            }
            reason = MapPresentationReason.Applied;
            return visible ? MapPresentationAction.Show : MapPresentationAction.Hide;
        }
    }

    /// <summary>
    /// 原簇布局输入（**冻结的原始快照**）：坐标 = 原始 anchoredPosition（相对 paper 中心），
    /// art 尺寸 = cluster scale=1 下的显示尺寸，offset = art 中心相对 pivot 的偏移（scale=1）。
    /// 纯数据、无 UnityEngine 依赖，离线可逐值复算。
    /// </summary>
    internal struct MapOverviewClusterInput
    {
        internal float OrigX;
        internal float OrigY;
        internal float OrigScaleX;
        internal float OrigScaleY;
        internal float OrigScaleZ;
        internal float ArtW1;
        internal float ArtH1;
        internal float OffX1;
        internal float OffY1;

        internal MapOverviewClusterInput(float origX, float origY,
            float origScaleX, float origScaleY, float origScaleZ,
            float artW1, float artH1, float offX1, float offY1)
        {
            OrigX = origX;
            OrigY = origY;
            OrigScaleX = origScaleX;
            OrigScaleY = origScaleY;
            OrigScaleZ = origScaleZ;
            ArtW1 = artW1;
            ArtH1 = artH1;
            OffX1 = offX1;
            OffY1 = offY1;
        }
    }

    /// <summary>原簇布局输出：目标 anchoredPosition（相对 paper 中心）+ 目标 localScale（= 原始 scale × 统一倍率）。</summary>
    internal struct MapOverviewClusterTarget
    {
        internal float X;
        internal float Y;
        internal float ScaleX;
        internal float ScaleY;
        internal float ScaleZ;

        internal MapOverviewClusterTarget(float x, float y, float scaleX, float scaleY, float scaleZ)
        {
            X = x;
            Y = y;
            ScaleX = scaleX;
            ScaleY = scaleY;
            ScaleZ = scaleZ;
        }
    }

    /// <summary>
    /// 原生地理总览布局（纯函数；r7 取代 5×2 网格口径）。用户 2026-10-02 明确否定网格重排，
    /// 要求保留原生 10 簇的中心出生岛/四角成对岛/奥林匹斯相对关系与尺寸比例：
    /// - **只能**对原始快照施加统一倍率 scale + 整体平移；两两差值严格按同一倍率缩放
    ///   （target_i − target_j = s · (orig_i − orig_j)，含 art 盒），不做每岛各自压缩/网格化/地理排序；
    /// - 统一倍率取"10 簇 art 并集恰好装进内容区（顶部按钮带以下、底部扩展带以上、两侧安全边距内）"
    ///   的最小值，且不超过 1（永不放大）；
    /// - 底部固定预留扩展带（BandFraction × 内容高；标称 314×208 → 39.9），供未来真实追加的第 11 个
    ///   登记 UILand 使用；原生 10 簇的倍率与位置**不含**扩展簇（新增簇不得影响原 10）；
    /// - 扩展簇（下标 ≥ NativeUiClusterCount）单独走 TryPlanExtension：同一统一倍率、底部带内居中，
    ///   带内放不下才允许其自身缩小（只影响它自己）。未登记时调用方不得放置占位。
    /// </summary>
    internal static class MapOverviewLayout
    {
        /// <summary>原生可见地图簇数量：前 10 个登记 UILand 为原生；下标 ≥10 视为扩展簇（binding 合同后补）。</summary>
        internal const int NativeUiClusterCount = 10;
        /// <summary>内容区安全边距（paper 坐标）。</summary>
        internal const float Margin = 2f;
        /// <summary>底部扩展带占内容高比例：标称 314×(208-18)=190 → 39.9 单位。</summary>
        internal const float BandFraction = 0.21f;
        internal const float MaxUniformScale = 1f;

        internal static float ContentTop(float paperH, float buttonBand)
        {
            float top = paperH - buttonBand;
            return top > 0f ? top : 0f;
        }

        internal static float BandHeight(float paperH, float buttonBand)
            => ContentTop(paperH, buttonBand) * BandFraction;

        /// <summary>整张 paper 的本地矩形（左下原点）——无有效视口测量时的回退输入。</summary>
        internal static MapIconBox FullPaper(float paperW, float paperH)
            => new MapIconBox(0f, 0f, paperW > 0f ? paperW : 0f, paperH > 0f ? paperH : 0f);

        /// <summary>
        /// 有效内容矩形（r10/r11 实机视口）：输入 = runtime **已验证成功**的可见 paper 矩形
        /// （paper 本地、左下原点、含 X0/Y0 offset）。只做夹取 + 扣顶部按钮带；**不再把已知的薄/小矩形
        /// 扩大回整张 paper**：退化输入（夹取后无面积）返回空盒，调用方必须视为 invalid 并且不提交布局。
        /// 整张 paper 只由显式旧调用或"测量成功且实际等于全纸"的情况提供。
        /// </summary>
        internal static MapIconBox EffectiveContent(float paperW, float paperH, float buttonBand,
            in MapIconBox visiblePaper)
        {
            if (paperW <= 0f || paperH <= 0f) return default;
            float x0 = Math.Max(0f, Math.Min(paperW, visiblePaper.X0));
            float x1 = Math.Max(0f, Math.Min(paperW, visiblePaper.X1));
            float y0 = Math.Max(0f, Math.Min(paperH, visiblePaper.Y0));
            float y1 = Math.Max(0f, Math.Min(paperH, visiblePaper.Y1));
            if (x1 <= x0 || y1 <= y0) return default;
            float top = Math.Min(y1, paperH - buttonBand);
            if (top <= y0) top = y1;
            return new MapIconBox(x0, y0, x1, top);
        }

        internal static float BandHeightOf(in MapIconBox content) => content.Height * BandFraction;

        /// <summary>原生 10 簇可用内容区（有效内容去掉底部扩展带，含安全边距）。</summary>
        internal static MapIconBox ContentRegion(float paperW, float paperH, float buttonBand)
            => ContentRegion(paperW, paperH, buttonBand, FullPaper(paperW, paperH));

        internal static MapIconBox ContentRegion(float paperW, float paperH, float buttonBand,
            in MapIconBox visiblePaper)
        {
            MapIconBox content = EffectiveContent(paperW, paperH, buttonBand, visiblePaper);
            float bandHeight = BandHeightOf(content);
            return new MapIconBox(content.X0 + Margin, content.Y0 + bandHeight + Margin, content.X1 - Margin,
                content.Y1 - Margin);
        }

        /// <summary>底部扩展带（含安全边距）：扩展簇及其图标的地理区域（锚在**有效**内容底部）。</summary>
        internal static MapIconBox BandRegion(float paperW, float paperH, float buttonBand)
            => BandRegion(paperW, paperH, buttonBand, FullPaper(paperW, paperH));

        internal static MapIconBox BandRegion(float paperW, float paperH, float buttonBand,
            in MapIconBox visiblePaper)
        {
            MapIconBox content = EffectiveContent(paperW, paperH, buttonBand, visiblePaper);
            float bandHeight = BandHeightOf(content);
            return new MapIconBox(content.X0 + Margin, content.Y0 + Margin, content.X1 - Margin,
                content.Y0 + bandHeight - Margin);
        }

        /// <summary>原始 scale=1 状态下的 art 盒（paper 坐标，原点=左下角）。</summary>
        internal static bool TryScale1Box(in MapOverviewClusterInput input, float paperW, float paperH,
            out MapIconBox box)
        {
            box = default;
            if (input.ArtW1 <= 0.01f || input.ArtH1 <= 0.01f || paperW <= 0f || paperH <= 0f) return false;
            float centerX = input.OrigX + paperW * 0.5f + input.OrigScaleX * input.OffX1;
            float centerY = input.OrigY + paperH * 0.5f + input.OrigScaleY * input.OffY1;
            float halfW = Math.Abs(input.OrigScaleX) * input.ArtW1 * 0.5f;
            float halfH = Math.Abs(input.OrigScaleY) * input.ArtH1 * 0.5f;
            box = new MapIconBox(centerX - halfW, centerY - halfH, centerX + halfW, centerY + halfH);
            return true;
        }

        /// <summary>布局后的 art 盒（paper 坐标）。</summary>
        internal static MapIconBox ArtBoxOf(in MapOverviewClusterInput input, in MapOverviewClusterTarget target,
            float paperW, float paperH)
        {
            float centerX = target.X + paperW * 0.5f + target.ScaleX * input.OffX1;
            float centerY = target.Y + paperH * 0.5f + target.ScaleY * input.OffY1;
            float halfW = Math.Abs(target.ScaleX) * input.ArtW1 * 0.5f;
            float halfH = Math.Abs(target.ScaleY) * input.ArtH1 * 0.5f;
            return new MapIconBox(centerX - halfW, centerY - halfH, centerX + halfW, centerY + halfH);
        }

        /// <summary>
        /// 规划原生簇（不含扩展）：统一倍率 + 整体平移；两两差值严格同一倍率。
        /// true = 成功并填充 into；false = 输入非法（调用方不得应用任何变换）。
        /// </summary>
        internal static bool TryPlan(List<MapOverviewClusterInput> natives, float paperW, float paperH,
            float buttonBand, List<MapOverviewClusterTarget> into, out float uniformScale)
            => TryPlan(natives, paperW, paperH, buttonBand, FullPaper(paperW, paperH), into, out uniformScale);

        /// <summary>同上，但装入**实际可见**的有效内容矩形（含 X0/Y0 offset；原 10 仍同一倍率+同一平移）。</summary>
        internal static bool TryPlan(List<MapOverviewClusterInput> natives, float paperW, float paperH,
            float buttonBand, in MapIconBox visiblePaper, List<MapOverviewClusterTarget> into, out float uniformScale)
        {
            uniformScale = MaxUniformScale;
            if (into == null) return false;
            into.Clear();
            if (natives == null || natives.Count == 0 || paperW <= 0f || paperH <= 0f) return false;

            MapIconBox region = ContentRegion(paperW, paperH, buttonBand, visiblePaper);
            if (region.Width <= 0f || region.Height <= 0f) return false;

            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < natives.Count; i++)
            {
                if (!TryScale1Box(natives[i], paperW, paperH, out MapIconBox box)) return false;
                if (box.X0 < minX) minX = box.X0;
                if (box.Y0 < minY) minY = box.Y0;
                if (box.X1 > maxX) maxX = box.X1;
                if (box.Y1 > maxY) maxY = box.Y1;
            }
            float groupW = maxX - minX;
            float groupH = maxY - minY;
            if (groupW <= 0.0001f || groupH <= 0.0001f) return false;

            float scale = Math.Min(MaxUniformScale, Math.Min(region.Width / groupW, region.Height / groupH));
            if (!(scale > 0f)) return false;

            float groupCenterX = (minX + maxX) * 0.5f;
            float groupCenterY = (minY + maxY) * 0.5f;
            float regionCenterX = (region.X0 + region.X1) * 0.5f;
            float regionCenterY = (region.Y0 + region.Y1) * 0.5f;

            for (int i = 0; i < natives.Count; i++)
            {
                MapOverviewClusterInput input = natives[i];
                float pivotX = input.OrigX + paperW * 0.5f;
                float pivotY = input.OrigY + paperH * 0.5f;
                into.Add(new MapOverviewClusterTarget(
                    regionCenterX + scale * (pivotX - groupCenterX) - paperW * 0.5f,
                    regionCenterY + scale * (pivotY - groupCenterY) - paperH * 0.5f,
                    input.OrigScaleX * scale,
                    input.OrigScaleY * scale,
                    input.OrigScaleZ <= 0f ? 1f : input.OrigScaleZ));
            }
            uniformScale = scale;
            return true;
        }

        /// <summary>
        /// 扩展簇（第 11 个登记 UILand）规划：底部扩展带内居中，沿用原 10 簇的统一倍率；
        /// 带内放不下才允许它自己再缩小。**不改动原 10 的目标**。
        /// </summary>
        internal static bool TryPlanExtension(in MapOverviewClusterInput ext, float uniformScale,
            float paperW, float paperH, float buttonBand, out MapOverviewClusterTarget target)
            => TryPlanExtension(ext, uniformScale, paperW, paperH, buttonBand, FullPaper(paperW, paperH),
                out target);

        /// <summary>同上，但扩展带锚在**实际可见**的有效内容底部（含 X0/Y0 offset）。</summary>
        internal static bool TryPlanExtension(in MapOverviewClusterInput ext, float uniformScale,
            float paperW, float paperH, float buttonBand, in MapIconBox visiblePaper,
            out MapOverviewClusterTarget target)
        {
            target = default;
            if (paperW <= 0f || paperH <= 0f || ext.ArtW1 <= 0.01f || ext.ArtH1 <= 0.01f) return false;
            MapIconBox band = BandRegion(paperW, paperH, buttonBand, visiblePaper);
            if (band.Width <= 0f || band.Height <= 0f) return false;

            float scale = uniformScale > 0f ? uniformScale : MaxUniformScale;
            scale = Math.Min(scale, Math.Min(band.Width / ext.ArtW1, band.Height / ext.ArtH1));
            if (!(scale > 0f)) return false;

            float centerX = (band.X0 + band.X1) * 0.5f;
            float centerY = (band.Y0 + band.Y1) * 0.5f;
            target = new MapOverviewClusterTarget(
                centerX - scale * ext.OffX1 - paperW * 0.5f,
                centerY - scale * ext.OffY1 - paperH * 0.5f,
                ext.OrigScaleX * scale,
                ext.OrigScaleY * scale,
                ext.OrigScaleZ <= 0f ? 1f : ext.OrigScaleZ);
            return true;
        }
    }

}
