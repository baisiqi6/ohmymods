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

        internal void AddBlocked(in MapIconBox box)
        {
            if (box.Width <= 0f || box.Height <= 0f) return;
            _blocked.Add(box);
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

        /// <summary>
        /// 固定档位单次装箱（总览一个岛的多个自由矩形共享同一档缩放时使用）：
        /// true = 全部放下；false 时 into 为该档的尽力结果 + failed&gt;0，调用方可继续在下一矩形重试剩余项，
        /// 但**单个展示根**仍然只在全部放下时才作为成功（调用方负责整体裁决）。
        /// </summary>
        internal static bool TryPlanAtScale(List<MapIconRequest> requests, MapIconSurface surface, float scale,
            List<MapIconPlacement> into, out int failed, bool topDown)
        {
            failed = 0;
            if (into == null) return false;
            into.Clear();
            if (requests == null || requests.Count == 0) return true;
            if (surface == null || surface.Width <= 0f || surface.Height <= 0f || scale <= 0f)
            {
                failed = requests.Count;
                return false;
            }
            int misses = Pack(requests, surface, scale, topDown, into);
            failed = misses;
            return misses == 0;
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
                    if (!clash)
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

        /// <summary>扩展 banner 的图标区（banner 区内缩 margin；需要状态车道时右侧让出 reserve 宽）。</summary>
        internal static MapIconBox IconAreaOf(in MapIconBox banner, bool reserveStatusLane, float statusLaneWidth)
        {
            float x1 = banner.X1 - MapExtensionIslandLayout.Margin;
            if (reserveStatusLane)
            {
                x1 -= statusLaneWidth;
                if (x1 <= banner.X0) x1 = banner.X0 + banner.Width * 0.5f;
            }
            return new MapIconBox(banner.X0 + MapExtensionIslandLayout.Margin, banner.Y0 + MapExtensionIslandLayout.Margin,
                x1, banner.Y1 - MapExtensionIslandLayout.Margin);
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
    /// 扩展岛（exact 登记 physical11 总览簇）专属：底部带内三行 6/5/5（16 项真实容量）。
    /// 只服务扩展岛；原十岛继续用 MapOverviewLayout/MapIconRegionPlanner（本类不改它们的常量/输出）。
    /// 语义：真实请求**全部**放下才返回 true（绝不部分显示/截断/补假）；`RequestIndex` 稳定指向原 requests 下标；
    /// 首选 ≥PreferScale（0.6），仅真正容量不足按 ScaleLadder 降到 MinScale（0.36）为止，不再更低。
    /// 该岛的**已确认自有底图**（新 shore/outline/同框 button）不算障碍，由调用方从 blockers 中排除；
    /// 船标/灯塔/状态等语义障碍仍作为 blockers 参与边界与碰撞终检。
    /// 岸内终检：传入 shoreFrame + MapShoreMask 时，每个图标 footprint 必须**逐像素**位于岸内
    /// （只查 4 角/bbox 不够——中心 1px 水洞也会被拒绝）。
    /// </summary>
    internal static class MapExtensionIslandLayout
    {
        internal const float PreferScale = 0.6f;
        internal const float MinScale = 0.36f;
        /// <summary>三行 6/5/5：单行上限 6、行数上限 3、总容量 18；16 项真实集 → 6/5/5。</summary>
        internal const int MaxPerRow = 6;
        internal const int MaxRows = 3;
        internal const int MaxItems = MaxPerRow * MaxRows;
        internal static readonly float[] ScaleLadder = { 1f, 0.85f, 0.7f, 0.6f, 0.5f, 0.42f, 0.36f };
        /// <summary>行在可用区内的少量候选对齐（scale 主序）——用于避开真实船标/灯塔与岸线凹湾。</summary>
        internal static readonly float[] RowAlignX = { 0.5f, 0f, 1f };   // 居中/靠左/靠右
        internal static readonly float[] RowAlignY = { 0.5f, 0f, 1f };   // 居中/靠下/靠上
        internal const float Gutter = 1.5f;
        internal const float Margin = 1.5f;

        /// <summary>
        /// 三行分组（新合同）：请求按（高降、宽降、原 index 升）稳定排序，做**高度降序的连续切分**
        /// （16 项 → 6/5/5），枚举切点并取 (行高和, 最大行宽, 组大小不平衡) 字典序最小——行高和最小的
        /// 分组把高项集中，与 review/compact-capacity-math.json 的 6/5/5 分组逐值一致。
        /// 行 = 排序序列切片；RequestIndex 保持原下标。
        /// </summary>
        internal static bool TrySplitRows(List<MapIconRequest> requests, List<int> rowA, List<int> rowB, List<int> rowC,
            out float widthA, out float widthB, out float widthC,
            out float heightA, out float heightB, out float heightC)
        {
            widthA = widthB = widthC = 0f;
            heightA = heightB = heightC = 0f;
            if (rowA == null || rowB == null || rowC == null) return false;
            rowA.Clear();
            rowB.Clear();
            rowC.Clear();
            if (requests == null || requests.Count == 0) return true;
            int n = requests.Count;
            if (n > MaxItems) return false;

            var order = new List<int>(n);
            for (int i = 0; i < n; i++) order.Add(i);
            order.Sort((x, y) =>
            {
                MapIconRequest a = requests[x];
                MapIconRequest b = requests[y];
                if (a.Height != b.Height) return b.Height.CompareTo(a.Height);
                if (a.Width != b.Width) return b.Width.CompareTo(a.Width);
                return x.CompareTo(y);
            });

            int rows = (n + MaxPerRow - 1) / MaxPerRow;
            if (rows > MaxRows) return false;
            // 均衡切分（按排序序列，largest-first）：16 → 6/5/5、17 → 6/6/5、12 → 6/6、7 → 4/3……
            // 与独立审查 review/compact-capacity-math.json 的 6/5/5 分组逐值一致（高项集中在第一行）。
            int baseCount = n / rows;
            int extra = n % rows;
            int bestA = baseCount + (extra > 0 ? 1 : 0);
            int bestB = rows >= 2 ? baseCount + (extra > 1 ? 1 : 0) : 0;
            int bestC = rows >= 3 ? baseCount : 0;
            FillRow(order, rowA, 0, bestA);
            FillRow(order, rowB, bestA, bestB);
            FillRow(order, rowC, bestA + bestB, bestC);
            SliceMetrics(order, requests, 0, bestA, out widthA, out heightA);
            SliceMetrics(order, requests, bestA, bestB, out widthB, out heightB);
            SliceMetrics(order, requests, bestA + bestB, bestC, out widthC, out heightC);
            return true;
        }

        private static void SliceMetrics(List<int> order, List<MapIconRequest> requests, int start, int count,
            out float width, out float height)
        {
            width = 0f;
            height = 0f;
            for (int i = start; i < start + count; i++)
            {
                MapIconRequest req = requests[order[i]];
                width += req.Width;
                if (req.Height > height) height = req.Height;
            }
        }

        private static void FillRow(List<int> order, List<int> row, int start, int count)
        {
            for (int i = start; i < start + count; i++) row.Add(order[i]);
        }

        /// <summary>
        /// 所需外框（scale 下）：W = max 行宽×s + (max 行项数−1)×Gutter + 2×Margin；
        /// H = Σ(行高)×s + (非空行数−1)×Gutter + 2×Margin。
        /// </summary>
        internal static void RequiredSize(float scale, float widthA, float widthB, float widthC,
            float heightA, float heightB, float heightC, int countA, int countB, int countC,
            out float width, out float height)
        {
            float rowW = Math.Max(widthA, Math.Max(widthB, widthC)) * scale;
            int maxCount = Math.Max(countA, Math.Max(countB, countC));
            int rows = (countA > 0 ? 1 : 0) + (countB > 0 ? 1 : 0) + (countC > 0 ? 1 : 0);
            width = rowW + Math.Max(0, maxCount - 1) * Gutter + 2f * Margin;
            height = (heightA + heightB + heightC) * scale + Math.Max(0, rows - 1) * Gutter + 2f * Margin;
        }

        internal static bool TryPlan(in MapIconBox area, List<MapIconRequest> requests, List<MapIconBox> blockers,
            List<MapIconPlacement> placements, out float scale, out int failed)
            => TryPlan(area, requests, blockers, default, null, placements, out scale, out failed);

        /// <summary>
        /// 规划：area（paper 坐标）内按 ScaleLadder（降序，下限 MinScale）与行列对齐候选，三行放下全部请求。
        /// shoreFrame + mask 非空时，每个图标 footprint 必须逐像素位于岸内
        /// （mask 坐标系 = 实际 Sprite.rect 画布，含透明 padding）。
        /// true ⇒ placements 完整（每项 RequestIndex = 原下标）；false ⇒ placements 清空、failed = requests.Count
        /// （调用方整体 fallback，绝不部分展示）。
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
            if (requests.Count > MaxItems) return false;                                 // 三行容量上限：真实更多 → 整体 fail

            var rowA = new List<int>(MaxPerRow);
            var rowB = new List<int>(MaxPerRow);
            var rowC = new List<int>(MaxPerRow);
            if (!TrySplitRows(requests, rowA, rowB, rowC, out float widthA, out float widthB, out float widthC,
                    out float heightA, out float heightB, out float heightC))
            {
                return false;
            }

            for (int si = 0; si < ScaleLadder.Length; si++)
            {
                float candidate = ScaleLadder[si];
                if (candidate < minScale) break;
                RequiredSize(candidate, widthA, widthB, widthC, heightA, heightB, heightC,
                    rowA.Count, rowB.Count, rowC.Count, out float needW, out float needH);
                if (needW > area.Width || needH > area.Height) continue;
                // 行序置换（bottom→top 的 3! 排列）：“宽行可中间、窄行上下”——真实自然岸线是斜向岛，
                // 宽行（高项组）放进岛最宽的中带才可能通过逐像素岸内终检。
                for (int oi = 0; oi < RowOrders.Length; oi++)
                {
                    if (TryLayoutOrder(area, candidate, rowA, rowB, rowC, widthA, widthB, widthC,
                            heightA, heightB, heightC, RowOrders[oi], requests, blockers,
                            shoreFrame, mask, placements))
                    {
                        scale = candidate;
                        failed = 0;
                        return true;
                    }
                }
            }
            placements.Clear();
            return false;
        }

        /// <summary>bottom→top 的行序候选（0=行 A 高项组、1=行 B、2=行 C）。</summary>
        private static readonly int[][] RowOrders =
        {
            new[] { 2, 1, 0 },
            new[] { 2, 0, 1 },
            new[] { 1, 0, 2 },
            new[] { 1, 2, 0 },
            new[] { 0, 1, 2 },
            new[] { 0, 2, 1 },
        };

        private static bool TryLayoutRows(in MapIconBox area, float scale,
            List<int> rowA, List<int> rowB, List<int> rowC,
            float widthA, float widthB, float widthC, float heightA, float heightB, float heightC,
            int[] order, float alignX, float alignY, List<MapIconRequest> requests, List<MapIconBox> blockers,
            in MapIconBox shoreFrame, MapShoreMask mask, List<MapIconPlacement> placements)
        {
            var rows = new[] { rowA, rowB, rowC };
            var widths = new[] { widthA, widthB, widthC };
            var heights = new[] { heightA, heightB, heightC };
            int rowsUsed = (rowA.Count > 0 ? 1 : 0) + (rowB.Count > 0 ? 1 : 0) + (rowC.Count > 0 ? 1 : 0);
            float totalH = (heightA + heightB + heightC) * scale + Math.Max(0, rowsUsed - 1) * Gutter;
            if (totalH > area.Height - 2f * Margin) return false;
            float bottomBase = area.Y0 + Margin + Math.Max(0f, area.Height - 2f * Margin - totalH) * alignY;
            placements.Clear();
            // 按给定行序自下而上排列；每行按各自行宽独立水平对齐（不随长岛横向拉伸）。
            float bottom = bottomBase;
            for (int oi = 0; oi < order.Length; oi++)
            {
                int r = order[oi];
                if (!LayoutRow(area, scale, rows[r], widths[r], heights[r], bottom, alignX, requests, blockers,
                        shoreFrame, mask, placements))
                {
                    placements.Clear();
                    return false;
                }
                bottom += heights[r] * scale + (rows[r].Count > 0 ? Gutter : 0f);
            }
            // 终检：条目两两不重叠（同/跨行）且都在 area 内。
            for (int i = 0; i < placements.Count; i++)
            {
                if (!Inside(area, placements[i])) { placements.Clear(); return false; }
                for (int j = i + 1; j < placements.Count; j++)
                {
                    if (Overlaps(placements[i], placements[j])) { placements.Clear(); return false; }
                }
            }
            return true;
        }

        private static bool LayoutRow(in MapIconBox area, float scale, List<int> row, float rowWidth,
            float rowHeight, float bottom, float alignX, List<MapIconRequest> requests, List<MapIconBox> blockers,
            in MapIconBox shoreFrame, MapShoreMask mask, List<MapIconPlacement> placements)
        {
            if (row.Count == 0) return true;
            float needW = rowWidth * scale + (row.Count - 1) * Gutter;
            if (needW > area.Width - 2f * Margin) return false;
            float left = area.X0 + Margin + Math.Max(0f, area.Width - 2f * Margin - needW) * alignX;
            return TryRowAt(left, bottom, scale, row, rowHeight, requests, blockers, shoreFrame, mask, placements);
        }

        /// <summary>
        /// 行序/位置裁决：
        /// - 有岸内 mask：垂直按 mask 像素粒度扫描行块偏移 + 每行**独立**水平逐像素扫描——斜向自然岸线
        ///   只在特定行位/行偏移下有完整合法解（“宽行可中间、窄行上下”）；
        /// - 无 mask（纯矩形容量/旧合同）：保留 3×3 对齐候选。
        /// </summary>
        private static bool TryLayoutOrder(in MapIconBox area, float scale,
            List<int> rowA, List<int> rowB, List<int> rowC,
            float widthA, float widthB, float widthC, float heightA, float heightB, float heightC,
            int[] order, List<MapIconRequest> requests, List<MapIconBox> blockers,
            in MapIconBox shoreFrame, MapShoreMask mask, List<MapIconPlacement> placements)
        {
            if (mask != null && shoreFrame.Width > 1f && shoreFrame.Height > 1f)
            {
                return TryLayoutOrderFine(area, scale, rowA, rowB, rowC, widthA, widthB, widthC,
                    heightA, heightB, heightC, order, requests, blockers, shoreFrame, mask, placements);
            }
            for (int ax = 0; ax < RowAlignX.Length; ax++)
            {
                for (int ay = 0; ay < RowAlignY.Length; ay++)
                {
                    if (TryLayoutRows(area, scale, rowA, rowB, rowC, widthA, widthB, widthC,
                            heightA, heightB, heightC, order, RowAlignX[ax], RowAlignY[ay],
                            requests, blockers, shoreFrame, null, placements))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool TryLayoutOrderFine(in MapIconBox area, float scale,
            List<int> rowA, List<int> rowB, List<int> rowC,
            float widthA, float widthB, float widthC, float heightA, float heightB, float heightC,
            int[] order, List<MapIconRequest> requests, List<MapIconBox> blockers,
            in MapIconBox shoreFrame, MapShoreMask mask, List<MapIconPlacement> placements)
        {
            var rows = new[] { rowA, rowB, rowC };
            var widths = new[] { widthA, widthB, widthC };
            var heights = new[] { heightA, heightB, heightC };
            int rowsUsed = (rowA.Count > 0 ? 1 : 0) + (rowB.Count > 0 ? 1 : 0) + (rowC.Count > 0 ? 1 : 0);
            float totalH = (heightA + heightB + heightC) * scale + Math.Max(0, rowsUsed - 1) * Gutter;
            float slackH = area.Height - 2f * Margin - totalH;
            if (slackH < -0.01f) return false;
            float stepY = shoreFrame.Height / mask.Height;
            if (!(stepY > 0.01f)) stepY = 1f;
            int stepsY = Math.Max(0, (int)Math.Ceiling(slackH / stepY));
            for (int iy = 0; iy <= stepsY; iy++)
            {
                float offsetY = Math.Min(slackH, iy * stepY);
                float bottom = area.Y0 + Margin + offsetY;
                placements.Clear();
                bool ok = true;
                for (int oi = 0; oi < order.Length && ok; oi++)
                {
                    int r = order[oi];
                    ok = LayoutRowFine(area, scale, rows[r], widths[r], heights[r], bottom, requests, blockers,
                        shoreFrame, mask, placements);
                    bottom += heights[r] * scale + (rows[r].Count > 0 ? Gutter : 0f);
                }
                if (!ok) continue;
                bool valid = true;
                for (int i = 0; i < placements.Count && valid; i++)
                {
                    if (!Inside(area, placements[i])) valid = false;
                    for (int j = i + 1; j < placements.Count && valid; j++)
                    {
                        if (Overlaps(placements[i], placements[j])) valid = false;
                    }
                }
                if (valid) return true;
            }
            placements.Clear();
            return false;
        }

        private static bool LayoutRowFine(in MapIconBox area, float scale, List<int> row, float rowWidth,
            float rowHeight, float bottom, List<MapIconRequest> requests, List<MapIconBox> blockers,
            in MapIconBox shoreFrame, MapShoreMask mask, List<MapIconPlacement> placements)
        {
            if (row.Count == 0) return true;
            float needW = rowWidth * scale + (row.Count - 1) * Gutter;
            float xMin = area.X0 + Margin;
            float xMax = area.X1 - Margin - needW;
            if (xMax < xMin - 0.001f) return false;
            float stepX = shoreFrame.Width / mask.Width;
            if (!(stepX > 0.01f)) stepX = 1f;
            int stepsX = Math.Max(0, (int)Math.Floor((xMax - xMin) / stepX));
            for (int ix = 0; ix <= stepsX; ix++)
            {
                int mark = placements.Count;
                if (TryRowAt(xMin + ix * stepX, bottom, scale, row, rowHeight, requests, blockers,
                        shoreFrame, mask, placements))
                {
                    return true;
                }
                placements.RemoveRange(mark, placements.Count - mark);
            }
            int lastMark = placements.Count;
            if (TryRowAt(xMax, bottom, scale, row, rowHeight, requests, blockers, shoreFrame, mask, placements))
            {
                return true;
            }
            placements.RemoveRange(lastMark, placements.Count - lastMark);
            return false;
        }

        /// <summary>在给定 left/bottom 处放整行（逐 icon 检查 blockers + 岸内 footprint）；成功才追加 placements。</summary>
        private static bool TryRowAt(float left, float bottom, float scale, List<int> row, float rowHeight,
            List<MapIconRequest> requests, List<MapIconBox> blockers, in MapIconBox shoreFrame, MapShoreMask mask,
            List<MapIconPlacement> placements)
        {
            float x = left;
            for (int i = 0; i < row.Count; i++)
            {
                int index = row[i];
                MapIconRequest req = requests[index];
                float w = req.Width * scale;
                float h = req.Height * scale;
                float y = bottom + (rowHeight - req.Height) * scale * 0.5f;   // 行内垂直居中（保持各自比例）
                var box = new MapIconBox(x, y, x + w, y + h);
                if (blockers != null)
                {
                    for (int b = 0; b < blockers.Count; b++)
                    {
                        if (Intersects(box, blockers[b], 0.01f)) return false;
                    }
                }
                if (mask != null && !FootprintInsideShore(box, shoreFrame, mask)) return false;
                placements.Add(new MapIconPlacement(req, index, x, y, scale));
                x += w + Gutter;
            }
            return true;
        }

        /// <summary>
        /// 整 footprint 岸内终检：box（paper）→ 实际 Sprite.rect 画布像素（含 padding）→ 逐像素覆盖。
        /// 像素包围盒取 floor/ceil（保守放大到完整像素格），避免浮点边界漏检。
        /// </summary>
        internal static bool FootprintInsideShore(in MapIconBox box, in MapIconBox shoreFrame, MapShoreMask mask)
        {
            if (mask == null || mask.Width <= 0 || mask.Height <= 0) return false;
            if (shoreFrame.Width <= 0f || shoreFrame.Height <= 0f) return false;
            float sx = mask.Width / shoreFrame.Width;
            float sy = mask.Height / shoreFrame.Height;
            int x0 = (int)Math.Floor((box.X0 - shoreFrame.X0) * sx);
            int y0 = (int)Math.Floor((box.Y0 - shoreFrame.Y0) * sy);
            int x1 = (int)Math.Ceiling((box.X1 - shoreFrame.X0) * sx) - 1;
            int y1 = (int)Math.Ceiling((box.Y1 - shoreFrame.Y0) * sy) - 1;
            return mask.RectAllInside(x0, y0, x1, y1);
        }

        private static bool Inside(in MapIconBox area, in MapIconPlacement p)
            => p.X >= area.X0 - 0.01f && p.Y >= area.Y0 - 0.01f &&
               p.X + p.Request.Width * p.Scale <= area.X1 + 0.01f &&
               p.Y + p.Request.Height * p.Scale <= area.Y1 + 0.01f;

        private static bool Overlaps(in MapIconPlacement a, in MapIconPlacement b)
            => Intersects(new MapIconBox(a.X, a.Y, a.X + a.Request.Width * a.Scale, a.Y + a.Request.Height * a.Scale),
                          new MapIconBox(b.X, b.Y, b.X + b.Request.Width * b.Scale, b.Y + b.Request.Height * b.Scale),
                          0.01f);

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

    /// <summary>一个岛（owner）的 art 盒（paper 坐标）。owner 语义由调用方定义（本项目 = land 下标）。</summary>
    internal struct MapIconRegionOwner
    {
        internal int Owner;
        internal MapIconBox Art;

        internal MapIconRegionOwner(int owner, in MapIconBox art)
        {
            Owner = owner;
            Art = art;
        }
    }

    /// <summary>
    /// 图标区域规划（纯函数；r7）：把自由区域按"最近岛"归属（真实 art footprint 的格点 Voronoi），
    /// 再分解为最大矩形（面积降序）。属性：
    /// - 区域两两不相交（每个格点只属于一个 owner）⇒ 不同岛的图标不可能互相串位/错归属；
    /// - 每个区域矩形不与任何 art 盒相交（格点中心在 art 外 + 调用方再按连续 art 盒阻断）；
    /// - 确定性：同输入（含 owner 顺序）逐值一致；可离线复算。
    /// 中间岛在原生地理下自由空间有限 ⇒ 区域小是**真实几何结果**，调用方按"放不下就不显示并报告"处理，
    /// 不移动/缩小岛来腾地方。
    /// </summary>
    internal static class MapIconRegionPlanner
    {
        internal const float DefaultCell = 2f;
        internal const int MaxRectsPerOwner = 8;
        /// <summary>小于该边长的矩形对最小可读档（0.36）图标也无意义，不输出（仍消耗格点，避免死循环）。</summary>
        internal const float MinRectSide = 8f;

        internal static void Build(List<MapIconRegionOwner> owners, in MapIconBox freeArea, float cellSize,
            List<MapIconBox> intoRects, List<int> intoOwners)
        {
            if (intoRects != null) intoRects.Clear();
            if (intoOwners != null) intoOwners.Clear();
            if (owners == null || owners.Count == 0 || intoRects == null || intoOwners == null) return;
            if (freeArea.Width <= MinRectSide || freeArea.Height <= MinRectSide) return;

            float cell = cellSize > 0.5f ? cellSize : 0.5f;
            int nx = (int)(freeArea.Width / cell);
            int ny = (int)(freeArea.Height / cell);
            if (nx <= 0 || ny <= 0) return;

            int[] ownerGrid = new int[nx * ny];
            for (int i = 0; i < ownerGrid.Length; i++) ownerGrid[i] = -1;
            for (int j = 0; j < ny; j++)
            {
                float cy = freeArea.Y0 + (j + 0.5f) * cell;
                for (int i = 0; i < nx; i++)
                {
                    float cx = freeArea.X0 + (i + 0.5f) * cell;
                    float half = cell * 0.5f;
                    bool inside = false;
                    for (int o = 0; o < owners.Count; o++)
                    {
                        MapIconBox art = owners[o].Art;
                        // 格点矩形与 art 盒有任何交叠即不算自由（不用中心点判定：避免区域矩形切进 art 边缘）
                        if (art.X0 < cx + half && cx - half < art.X1 &&
                            art.Y0 < cy + half && cy - half < art.Y1) { inside = true; break; }
                    }
                    if (inside) continue;

                    int bestOwner = -1;
                    float bestDistance = float.MaxValue;
                    for (int o = 0; o < owners.Count; o++)
                    {
                        float distance = BoxDistanceSq(cx, cy, owners[o].Art);
                        if (distance < bestDistance) { bestDistance = distance; bestOwner = o; }
                    }
                    ownerGrid[j * nx + i] = bestOwner;
                }
            }

            bool[] used = new bool[nx * ny];
            int[] heights = new int[nx];
            int[] stackIndex = new int[nx + 1];
            int[] stackHeight = new int[nx + 1];
            for (int o = 0; o < owners.Count; o++)
            {
                var rects = new List<MapIconBox>(MaxRectsPerOwner);
                var areas = new List<float>(MaxRectsPerOwner);
                for (int iteration = 0; iteration < MaxRectsPerOwner; iteration++)
                {
                    int bestArea = 0;
                    int bestI0 = 0, bestJ0 = 0, bestI1 = 0, bestJ1 = 0;
                    for (int i = 0; i < nx; i++) heights[i] = 0;
                    for (int j = 0; j < ny; j++)
                    {
                        for (int i = 0; i < nx; i++)
                        {
                            bool mine = ownerGrid[j * nx + i] == o && !used[j * nx + i];
                            heights[i] = mine ? heights[i] + 1 : 0;
                        }
                        // 直方图最大矩形（push 栈，O(nx)）
                        int top = 0;
                        for (int i = 0; i <= nx; i++)
                        {
                            int h = i < nx ? heights[i] : 0;
                            int start = i;
                            while (top > 0 && stackHeight[top - 1] > h)
                            {
                                top--;
                                int si = stackIndex[top];
                                int sh = stackHeight[top];
                                int area = sh * (i - si);
                                if (area > bestArea)
                                {
                                    bestArea = area;
                                    bestI0 = si; bestI1 = i;
                                    bestJ0 = j - sh + 1; bestJ1 = j;
                                }
                                start = si;
                            }
                            stackIndex[top] = start;
                            stackHeight[top] = h;
                            top++;
                        }
                    }
                    if (bestArea <= 0) break;
                    for (int j = bestJ0; j <= bestJ1; j++)
                    {
                        for (int i = bestI0; i < bestI1; i++) used[j * nx + i] = true;
                    }
                    var rect = new MapIconBox(
                        freeArea.X0 + bestI0 * cell, freeArea.Y0 + bestJ0 * cell,
                        freeArea.X0 + bestI1 * cell, freeArea.Y0 + (bestJ1 + 1) * cell);
                    if (rect.Width >= MinRectSide && rect.Height >= MinRectSide)
                    {
                        rects.Add(rect);
                        areas.Add(rect.Width * rect.Height);
                    }
                }
                // 面积降序（同面积：y 高者优先、再 x 小者优先，保证确定性）
                for (int a = 0; a < rects.Count; a++)
                {
                    for (int b = a + 1; b < rects.Count; b++)
                    {
                        bool swap = areas[b] > areas[a] + 0.001f ||
                            (Math.Abs(areas[b] - areas[a]) <= 0.001f &&
                             (rects[b].Y0 > rects[a].Y0 + 0.001f ||
                              (Math.Abs(rects[b].Y0 - rects[a].Y0) <= 0.001f && rects[b].X0 < rects[a].X0)));
                        if (swap)
                        {
                            MapIconBox rectSwap = rects[a]; rects[a] = rects[b]; rects[b] = rectSwap;
                            float areaSwap = areas[a]; areas[a] = areas[b]; areas[b] = areaSwap;
                        }
                    }
                }
                for (int r = 0; r < rects.Count; r++)
                {
                    intoRects.Add(rects[r]);
                    intoOwners.Add(owners[o].Owner);
                }
            }
        }

        private static float BoxDistanceSq(float x, float y, in MapIconBox box)
        {
            float dx = box.X0 - x;
            if (dx < 0f) dx = x > box.X1 ? x - box.X1 : 0f;
            float dy = box.Y0 - y;
            if (dy < 0f) dy = y > box.Y1 ? y - box.Y1 : 0f;
            return dx * dx + dy * dy;
        }
    }
}
