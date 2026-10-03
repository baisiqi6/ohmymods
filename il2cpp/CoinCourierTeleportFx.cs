using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 传送表现句柄：一次 Begin 一代（slot + 模块单调 generation）。旧句柄永不匹配后续代
/// （Clear/世界重建后 generation 也不重置），不会误伤后来者的 effect。
/// </summary>
internal readonly struct CoinCourierFxHandle
{
    internal readonly int Slot;
    internal readonly long Generation;

    internal CoinCourierFxHandle(int slot, long generation)
    {
        Slot = slot;
        Generation = generation;
    }

    internal bool IsValid => Slot >= 0;
}

/// <summary>传送线样式：同一个脚锚上立起整组，只有线轴方向与色阶不同（几何/色阶表在 CoinCourierTeleportFx）。</summary>
internal enum CoinCourierTeleportStyle
{
    /// <summary>离散横线（y 偏移 + 中心 x 偏移 + 半长；全组统一 +TiltSlope 微斜、以横向为主）；
    /// 色阶为清晰金色。</summary>
    Horizontal,
    /// <summary>离散竖线（x 偏移 + 中心 y + 半长；全组统一 +TiltSlope 微斜、以竖向为主）；色阶为
    /// 高不透明近黑，双端色带克制暖色、沿线段插值后整条为暖近黑（暗底对比弱于亮底，已知取舍）。</summary>
    Vertical
}

/// <summary>
/// 传送动画方向：决定"激活线数"与"线长"的时程走向。
/// * Arrival（入场）：少而短的线先激活，随后各自拉长、错落增线；整组的长度峰值精确对齐调用方
///   的显形时刻（anchorSeconds，助手 .12 / 哥布林 .18），显形后只剩稀薄残线按各自时点渐退。
/// * Departure（离场）：起步即密而长，逐线错落收束（变少、变短、淡出）；绝大多数线在显形锚点前
///   消失，少数残线在锚点后按自己的时点淡出。
/// 旧 Begin 3/5/6 参形状默认 Departure（保持历史"收束"语义）。
/// </summary>
internal enum CoinCourierTeleportDirection
{
    Departure = 0,
    Arrival = 1
}

/// <summary>
/// 传送 FX 的"可见身体框"解析（纯表现、只读、Begin 一次性）：从调用方当前 SpriteRenderer 的
/// sprite 读取 alpha 可见框（不是含透明 padding 的 rect/bounds），按 pivot/PPU/带符号缩放/flip
/// 换算成"相对 renderer 原点（脚锚）"的世界尺寸与中心偏移——绝不读世界 bounds 或中心
/// （哥布林 OnJumpComplete 时 renderer transform 仍在旧位置）。Tick 不读 sprite/像素。
/// 失败（不可读/packed/越界/全透明/非有限尺度）返回 false，由调用方安全降级为无效 FX。
/// </summary>
internal static class CoinCourierTeleportBodyBox
{
    internal readonly struct Box
    {
        internal readonly bool Valid;
        internal readonly float Width;        // 世界宽
        internal readonly float Height;       // 世界高
        internal readonly Vector2 Center;     // 相对 renderer 原点的世界中心偏移

        internal Box(float width, float height, Vector2 center)
        {
            Valid = true;
            Width = width;
            Height = height;
            Center = center;
        }
    }

    private const int MaxCachedEntries = 64;
    private const int MaxScanDimension = 256;      // 单边像素上限
    private const int MaxScanPixels = 160 * 160;   // 扫描面积上限
    private const float AlphaThreshold = 8f / 255f;
    private const float NativePadPx = 2f;          // 已证据 native mesh 的保守内收（左右/顶 2px，底不动）
    private const int NativeMinVertices = 5;
    private const int NativeMaxVertices = 10;

    private readonly struct LocalBox
    {
        internal readonly int MinX;     // 相对 sprite.rect 的含像素边界
        internal readonly int MinY;     // 含（y 向上）
        internal readonly int MaxX;     // exclusive（最后可见像素 + 1）
        internal readonly int MaxY;     // exclusive

        internal LocalBox(int minX, int minY, int maxX, int maxY)
        {
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
        }
    }

    /// <summary>真正的身份键（sprite 实例 + 纹理实例 + rect），不使用 XOR/哈希当 identity。</summary>
    private readonly struct SpriteKey : IEquatable<SpriteKey>
    {
        private readonly int _spriteId;
        private readonly int _textureId;
        private readonly int _x;
        private readonly int _y;
        private readonly int _w;
        private readonly int _h;

        internal SpriteKey(int spriteId, int textureId, int x, int y, int w, int h)
        {
            _spriteId = spriteId;
            _textureId = textureId;
            _x = x;
            _y = y;
            _w = w;
            _h = h;
        }

        public bool Equals(SpriteKey other)
            => _spriteId == other._spriteId && _textureId == other._textureId
            && _x == other._x && _y == other._y && _w == other._w && _h == other._h;

        public override bool Equals(object obj) => obj is SpriteKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(_spriteId, _textureId, _x, _y, _w, _h);
    }

    private sealed class CacheEntry
    {
        internal LocalBox Box;
        internal bool Valid;
    }

    // 只缓存"局部像素 bbox"，不缓存世界尺寸/scale；失败结果也缓存，避免反复抛异常/重扫。
    private static readonly Dictionary<SpriteKey, CacheEntry> Cache = new();
    private static readonly CacheEntry InvalidEntry = new() { Valid = false };

    internal static bool TryResolve(SpriteRenderer renderer, out Box box)
    {
        box = default;
        try
        {
            if (renderer == null) return false;
            Sprite sprite = renderer.sprite;
            if (sprite == null) return false;
            Texture2D texture = sprite.texture;
            if (texture == null) return false;
            Rect rect = sprite.rect;
            float ppu = sprite.pixelsPerUnit;
            if (!float.IsFinite(ppu) || ppu <= 0f) return false;
            if (!(rect.width >= 1f) || !(rect.height >= 1f)) return false;
            if (rect.width > MaxScanDimension || rect.height > MaxScanDimension) return false;
            if (rect.width * rect.height > MaxScanPixels) return false;
            if (rect.x < 0f || rect.y < 0f
                || rect.x + rect.width > texture.width || rect.y + rect.height > texture.height) return false;

            LocalBox local;
            if (!sprite.packed)
            {
                // 自有（可读）图集：直接扫 alpha。
                if (!TryGetLocalBox(sprite, texture, rect, out local)) return false;
            }
            else
            {
                // packed：只对已证据覆盖的 native banker 家族走 sprite.vertices 近似（不读纹理像素）；
                // 其它 packed/未知 sprite 明确拒收（fail-open，不猜尺寸）。
                if (!IsEvidenceNative(sprite, rect, ppu)) return false;
                if (!TryGetNativeLocalBox(sprite, rect, ppu, out local)) return false;
            }

            // 世界尺度经 TransformVector：支持父级缩放与 flip 符号；旋转/斜切（轴向量出现交叉分量）明确拒收。
            Vector3 xAxis = renderer.transform.TransformVector(new Vector3(1f, 0f, 0f));
            Vector3 yAxis = renderer.transform.TransformVector(new Vector3(0f, 1f, 0f));
            const float axisEpsilon = 1e-4f;
            if (Math.Abs(xAxis.y) > axisEpsilon || Math.Abs(xAxis.z) > axisEpsilon
                || Math.Abs(yAxis.x) > axisEpsilon || Math.Abs(yAxis.z) > axisEpsilon) return false;
            float sx = xAxis.x;
            float sy = yAxis.y;
            if (!float.IsFinite(sx) || !float.IsFinite(sy) || sx == 0f || sy == 0f) return false;
            Vector2 pivot = sprite.pivot;
            float pixelCenterX = (local.MinX + local.MaxX) * 0.5f;
            float pixelCenterY = (local.MinY + local.MaxY) * 0.5f;
            float offsetX = (pixelCenterX - pivot.x) / ppu * sx;
            float offsetY = (pixelCenterY - pivot.y) / ppu * sy;
            if (!float.IsFinite(offsetX) || !float.IsFinite(offsetY)) return false;
            if (renderer.flipX) offsetX = -offsetX;
            if (renderer.flipY) offsetY = -offsetY;
            float width = (local.MaxX - local.MinX) / ppu * Math.Abs(sx);
            float height = (local.MaxY - local.MinY) / ppu * Math.Abs(sy);
            if (!(width > 0f) || !(height > 0f)) return false;
            box = new Box(width, height, new Vector2(offsetX, offsetY));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>已证据覆盖的 native 家族资格门（名字仅作资格门，不当离线 bbox/身份键；同名 bamboo 各按自身 vertices 计算）。</summary>
    private static bool IsEvidenceNative(Sprite sprite, Rect rect, float ppu)
    {
        string name = sprite.name;
        if (string.IsNullOrEmpty(name)) return false;
        if (!name.StartsWith("banker_idle", StringComparison.Ordinal)
            && !name.StartsWith("banker_walk", StringComparison.Ordinal)
            && !name.StartsWith("banker_run", StringComparison.Ordinal)) return false;
        if (rect.width != 32f || rect.height != 32f) return false;
        if (ppu != 32f) return false;
        if (sprite.pivot.x != 16f || sprite.pivot.y != 0f) return false;
        return true;
    }

    /// <summary>
    /// native 保守近似：读取本 sprite 的 tight mesh vertices（已是 pivot 相对局部世界单位，5~10 顶点），
    /// 按 80 帧实测边距内收（左右各 2px、顶 2px、底不动），换算成 rect 像素框；不读纹理像素、
    /// 不依赖 textureRect/同名资产。几何非法/超界/顶点数不在证据范围 → 拒收。
    /// </summary>
    private static bool TryGetNativeLocalBox(Sprite sprite, Rect rect, float ppu, out LocalBox local)
    {
        local = default;
        SpriteKey key = MakeKey(sprite, rect);
        if (Cache.TryGetValue(key, out CacheEntry cached))
        {
            if (!cached.Valid) return false;
            local = cached.Box;
            return true;
        }
        if (Cache.Count >= MaxCachedEntries) Cache.Clear();
        try
        {
            Vector2[] vertices = sprite.vertices;
            if (vertices == null || vertices.Length < NativeMinVertices || vertices.Length > NativeMaxVertices)
            {
                Cache[key] = InvalidEntry;
                return false;
            }
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < vertices.Length; i++)
            {
                float vx = vertices[i].x;
                float vy = vertices[i].y;
                if (!float.IsFinite(vx) || !float.IsFinite(vy))
                {
                    Cache[key] = InvalidEntry;
                    return false;
                }
                if (vx < minX) minX = vx;
                if (vx > maxX) maxX = vx;
                if (vy < minY) minY = vy;
                if (vy > maxY) maxY = vy;
            }
            float pad = NativePadPx / ppu;
            minX += pad;
            maxX -= pad;
            maxY -= pad;   // ymin 不扣：保留脚底/腾空的保守下沿
            if (!(maxX > minX) || !(maxY > minY))
            {
                Cache[key] = InvalidEntry;
                return false;
            }
            int pxMinX = (int)Math.Round(minX * ppu + sprite.pivot.x);
            int pxMinY = (int)Math.Round(minY * ppu + sprite.pivot.y);
            int pxMaxX = (int)Math.Round(maxX * ppu + sprite.pivot.x);
            int pxMaxY = (int)Math.Round(maxY * ppu + sprite.pivot.y);
            if (pxMinX < 0 || pxMinY < 0 || pxMaxX > rect.width || pxMaxY > rect.height
                || pxMaxX <= pxMinX || pxMaxY <= pxMinY)
            {
                Cache[key] = InvalidEntry;
                return false;
            }
            var entry = new CacheEntry { Valid = true, Box = new LocalBox(pxMinX, pxMinY, pxMaxX, pxMaxY) };
            Cache[key] = entry;
            local = entry.Box;
            return true;
        }
        catch (Exception)
        {
            Cache[key] = InvalidEntry;
            return false;
        }
    }

    private static SpriteKey MakeKey(Sprite sprite, Rect rect)
        => new SpriteKey(sprite.GetInstanceID(), sprite.texture.GetInstanceID(),
            (int)rect.x, (int)rect.y, (int)rect.width, (int)rect.height);

    private static bool TryGetLocalBox(Sprite sprite, Texture2D texture, Rect rect, out LocalBox local)
    {
        local = default;
        SpriteKey key = MakeKey(sprite, rect);
        if (Cache.TryGetValue(key, out CacheEntry cached))
        {
            if (!cached.Valid) return false;
            local = cached.Box;
            return true;
        }
        if (Cache.Count >= MaxCachedEntries) Cache.Clear();
        int x = (int)rect.x;
        int y = (int)rect.y;
        int w = (int)rect.width;
        int h = (int)rect.height;
        try
        {
            if (!texture.isReadable)
            {
                Cache[key] = InvalidEntry;   // 不可读：记录失败，避免每次重试抛异常（安全降级由调用方处理）
                return false;
            }
            Color[] pixels = texture.GetPixels(x, y, w, h);
            if (pixels == null || pixels.Length < w * h)
            {
                Cache[key] = InvalidEntry;
                return false;
            }
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            for (int row = 0; row < h; row++)
            {
                int rowBase = row * w;
                for (int col = 0; col < w; col++)
                {
                    if (pixels[rowBase + col].a < AlphaThreshold) continue;
                    if (col < minX) minX = col;
                    if (col + 1 > maxX) maxX = col + 1;
                    if (row < minY) minY = row;
                    if (row + 1 > maxY) maxY = row + 1;
                }
            }
            if (maxX < 0 || maxY < 0 || maxX <= minX || maxY <= minY)
            {
                Cache[key] = InvalidEntry;   // 全透明
                return false;
            }
            var entry = new CacheEntry { Valid = true, Box = new LocalBox(minX, minY, maxX, maxY) };
            Cache[key] = entry;
            local = entry.Box;
            return true;
        }
        catch (Exception)
        {
            Cache[key] = InvalidEntry;
            return false;
        }
    }

    /// <summary>池/世界整体清理时释放缓存（失败条目一并清掉）。</summary>
    internal static void Clear() => Cache.Clear();
}

/// <summary>
/// 金币哥布林与税收助手共用的传送表现：一次 Begin 生成一个 effect 组，组内 16 根长短、宽窄、
/// 明暗与色阶各异的离散线（6 条骨架 + 10 条填空细辅线；各自 4 点 LineRenderer，四点共线
/// 等分 0、1/3、2/3、1，宽度由一次性创建的 widthCurve 收尖，端部 ≤ 中线宽的约 10%）。
/// 同一 style 的全组线统一微斜、互相平行：斜率由单一 <see cref="TiltSlope"/>（.10 ≈ 5.7°）决定，
/// 同一样式内所有线同方向、同斜率（H 以横轴为主、V 以纵轴为主），rise/lean 与半长同乘（scale 与
/// 包络 shrink 同时生效，任何年龄方向不变）；线间无正负交叉、无随机抖动，不同线只用长短/粗细/
/// 位置/时差区分。样式与方向由调用方显式选择；颜色由
/// 样式自己拥有（Horizontal 清晰金色阶、Vertical 高不透明近黑阶 + 克制暖端），传入 Base RGB
/// 不再参与混色，只有 alpha 仍生效（白色/任意底色不会把黑色洗灰）。
///
/// 时程（<see cref="LifetimeSeconds"/> = .32s，约 60FPS 下 19~20 帧）：
/// * Arrival 的 anchor = 调用方显形时刻；增长峰值（全部 16 根满长）恰好在 anchor，晚激活的线
///   也在 anchor 前完成可见增长；anchor 后按每条线自己的 TailAt 收成残线渐退。
/// * Departure 在 t=0 即密/满长（仅 ~2 帧极短淡入防硬闪），按每条线自己的 DepartAt 错落收束，
///   多数线在 anchor 前消失；DepartAt 超过阈值的残线收短后在 anchor 后按 TailAt 淡出。
/// 两个方向都不是"所有线只同时改 alpha"：激活/收束窗口与长度包络逐线错开。
///
/// 契约：
/// * 句柄：Begin 返回 <see cref="CoinCourierFxHandle"/>；业务只取消/持有自己的句柄
///   （<see cref="Cancel"/>），旧句柄不会取消复用后的别人的 effect。
/// * <see cref="Clear"/> 只在世界/模块整体结束时调用（由 Operator 整合世界清理由）；
///   业务停用请 Cancel 自己的句柄，不得在业务关闭时清共享池。
/// * 时钟：调用方驱动。<see cref="Tick(float)"/> 按 Time.frameCount 去重，
///   同帧多次调用只推进一次（runtime 与后续 ModPanel 集中 Tick 并存时不双倍）；
///   纯核心为 <see cref="TickForFrame"/>。暂停传 0/非法值不改变任何状态。
/// * 只画线：不创建角色/Damageable/Wallet/网络组件，不生成金币、不碰账本、不持有身份。
///   排序默认 0/0；需要贴合角色层时用 5 参重载显式给。
/// * 单表：几何、宽度、色阶索引与全部时程（ArrivalAt/DepartAt/TailAt/ArrivalFrom）都来自
///   <see cref="StripeSpecs"/> 一项只读表；线数 <see cref="DashesPerEffect"/> 直接由表长派生，
///   不存在可失配的平行数组。非法 style/direction/anchor 在占用池槽前拒收。
/// * 热路径：Tick/Configure 只写既有 4 个顶点、start/endColor 与 widthMultiplier；
///   AnimationCurve 只在类型初始化时为每条线建一次，池复用与逐帧更新零数组/零 curve 分配。
/// </summary>
internal static class CoinCourierTeleportFx
{
    // 8 助手双端（16 组）+ 哥布林双端（2 组）= 18 组（issue-104）。满时复用最旧活动组。
    internal const int MaxConcurrent = 18;     // effect 组上限
    internal const float LifetimeSeconds = 0.32f;

    /// <summary>旧 Begin 形状（3/5/6 参）不带显形锚点时的默认值（税收助手的显形窗口）。</summary>
    internal const float DefaultAnchorSeconds = 0.12f;
    /// <summary>显形锚点下限：低于它增长/收束窗口过短，会退化成硬闪。</summary>
    private const float MinAnchorSeconds = 0.03f;
    /// <summary>显形锚点上限（寿命的 65%）：保证收束/残线窗口都能在寿命内闭合。</summary>
    internal const float MaxAnchorSeconds = LifetimeSeconds * 0.65f;

    private const float DepartFadeSeconds = 0.025f;   // departure 起步的极短淡入（防硬闪，仅 alpha）
    private const float ArrivalFadeSeconds = 0.03f;   // arrival 每条线激活后的淡入上限
    private const float MinFadeSeconds = 0.008f;      // 晚激活线的淡入下限（仍在 anchor 前完成）
    private const float CollapsePortion = 0.42f;      // departure 收束窗口 = anchor × 0.42
    private const float ResidualDepartShare = 0.55f;  // DepartAt 超过它的线在 anchor 后继续淡出
    private const float ResidualCollapseAlpha = 0.6f; // 残线收束阶段的 alpha 保留量（1 → .4）
    private const float TailPortion = 0.55f;          // arrival 残线渐退窗口 = 尾窗 × 0.55
    private const float ResidualLength = 0.35f;       // 收束/残线末端长度系数

    private const float Third = 1f / 3f;         // 四点等分比例（与 curve key time 一致）
    private const float BaseLineWidth = 0.026f;  // 单条线世界宽度上限；实际取 curve 值 × 表内比例
    /// <summary>全组统一微斜（style 级、非每线字段）：rise = half × TiltSlope（H）、lean = half × TiltSlope（V）。
    /// 同一样式内所有线同方向、同斜率（≈5.7°），线间严格互相平行；不是旧的逐线正负斜度，也不是 0°。</summary>
    internal const float TiltSlope = 0.10f;
    private const float TipShare = 0.09f;        // 起端收尖：中线峰值的最多约 10%
    private const float TipShareEnd = 0.07f;     // 末端收尖
    private const float EndRgbFloor = 0.88f;     // 金色端点亮度随 alpha 端点比例轻微压暗的下限
    private const float EndRgbSpan = 0.12f;
    // 近黑线的克制暖色（加在 start/end 双色上，沿线段插值后整条线为暖近黑；不新增光晕层）。
    private const float WarmTipR = 0.085f;
    private const float WarmTipG = 0.050f;
    private const float WarmTipB = 0.024f;

    private readonly struct StripeSpec
    {
        internal readonly float HorizontalOffsetY;      // 横线：相对脚锚的 y（线心中点；全组统一微斜）
        internal readonly float HorizontalCenterX;      // 横线：中心 x 相对脚锚的轻微错位
        internal readonly float HorizontalHalfLength;   // 横线：半长（rise = half × TiltSlope）
        internal readonly float VerticalOffsetX;        // 竖线：相对脚锚的 x（线心中点；全组统一微斜）
        internal readonly float VerticalCenterY;        // 竖线：中心 y
        internal readonly float VerticalHalfLength;     // 竖线：半长（lean = half × TiltSlope）
        internal readonly float WidthAtFirstThird;      // widthCurve t=1/3 值（BaseLineWidth 的比例）
        internal readonly float WidthAtSecondThird;     // widthCurve t=2/3 值（中部左右不对称）
        internal readonly byte Palette;                 // 该线在样式色阶里的档位（0..3）
        internal readonly float Brightness;             // RGB 明暗微调
        internal readonly float Opacity;                // 该线整体 alpha（乘在 Base alpha 上）
        internal readonly float StartAlpha;             // 起点端 alpha/亮度比例
        internal readonly float EndAlpha;               // 终点端 alpha/亮度比例
        internal readonly float ArrivalAt;              // Arrival：激活时间 = ArrivalAt × anchor（0..0.8）
        internal readonly float DepartAt;               // Departure：收束开始 = DepartAt × anchor（0..0.9）
        internal readonly float TailAt;                 // 残线结束 = anchor + (寿命-anchor) × TailAt
        internal readonly float ArrivalFrom;            // Arrival：激活时的长度系数（短→满长）

        internal StripeSpec(float horizontalOffsetY, float horizontalCenterX, float horizontalHalfLength,
            float verticalOffsetX, float verticalCenterY, float verticalHalfLength,
            float widthAtFirstThird, float widthAtSecondThird, byte palette,
            float brightness, float opacity, float startAlpha, float endAlpha, float arrivalAt,
            float departAt, float tailAt, float arrivalFrom)
        {
            HorizontalOffsetY = horizontalOffsetY;
            HorizontalCenterX = horizontalCenterX;
            HorizontalHalfLength = horizontalHalfLength;
            VerticalOffsetX = verticalOffsetX;
            VerticalCenterY = verticalCenterY;
            VerticalHalfLength = verticalHalfLength;
            WidthAtFirstThird = widthAtFirstThird;
            WidthAtSecondThird = widthAtSecondThird;
            Palette = palette;
            Brightness = brightness;
            Opacity = opacity;
            StartAlpha = startAlpha;
            EndAlpha = endAlpha;
            ArrivalAt = arrivalAt;
            DepartAt = departAt;
            TailAt = tailAt;
            ArrivalFrom = arrivalFrom;
        }
    }

    // 单一事实来源：16 条线各自的长短/位置/宽度/色阶档/端点明暗/时程参数。
    // 尺度按脚锚展开（不是效果中心）：横线 y 覆盖 ~.09~.66 的角色身体（0..9 行为原 10 线几何，
    // 10..15 行为填空细线，既不外扩包络也不改变原线），竖线并集覆盖 ~.06~.84（低线到脚踝、
    // 高线过头顶），长度/宽度/中心一一不同；全体共享单一 TiltSlope 微斜（同向、互相平行）。
    // 时程列（ArrivalAt/DepartAt/TailAt）逐行错开且互不重复：两方向的激活/收束窗口因此天然错落，
    // 不存在"所有线同时变化"的相位；ArrivalFrom 是入场起点长度（短→满长的增长起点）。
    private static readonly StripeSpec[] StripeSpecs = BuildStripeSpecs();

    /// <summary>每组线数：唯一由表长派生，不重复魔法数字。</summary>
    internal static readonly int DashesPerEffect = StripeSpecs.Length;

    // 模板包络（样式级，最大长度 factor = 1；含 TiltSlope 的 rise/lean）：fit 时把"中心分布/主轴长度"
    // 分别映射到可见身体框，再做一次统一收紧；每条线的斜率始终由最终半长导出（.10），不被宽高差拉歪。
    private static readonly float EnvHMinX, EnvHMaxX, EnvHMinY, EnvHMaxY;
    private static readonly float EnvVMinX, EnvVMaxX, EnvVMinY, EnvVMaxY;
    private static readonly float MaxStrokeWidth;   // 表内最大峰值世界宽（scale = 1 时）

    static CoinCourierTeleportFx()
    {
        float hMinX = float.MaxValue, hMaxX = float.MinValue;
        float hMinY = float.MaxValue, hMaxY = float.MinValue;
        float vMinX = float.MaxValue, vMaxX = float.MinValue;
        float vMinY = float.MaxValue, vMaxY = float.MinValue;
        float maxPeak = 0f;
        for (int i = 0; i < StripeSpecs.Length; i++)
        {
            StripeSpec spec = StripeSpecs[i];
            float hRise = spec.HorizontalHalfLength * TiltSlope;
            hMinX = Math.Min(hMinX, spec.HorizontalCenterX - spec.HorizontalHalfLength);
            hMaxX = Math.Max(hMaxX, spec.HorizontalCenterX + spec.HorizontalHalfLength);
            hMinY = Math.Min(hMinY, spec.HorizontalOffsetY - hRise);
            hMaxY = Math.Max(hMaxY, spec.HorizontalOffsetY + hRise);
            float vLean = spec.VerticalHalfLength * TiltSlope;
            vMinX = Math.Min(vMinX, spec.VerticalOffsetX - vLean);
            vMaxX = Math.Max(vMaxX, spec.VerticalOffsetX + vLean);
            vMinY = Math.Min(vMinY, spec.VerticalCenterY - spec.VerticalHalfLength);
            vMaxY = Math.Max(vMaxY, spec.VerticalCenterY + spec.VerticalHalfLength);
            float peak = Math.Max(spec.WidthAtFirstThird, spec.WidthAtSecondThird) * BaseLineWidth;
            if (peak > maxPeak) maxPeak = peak;
        }
        EnvHMinX = hMinX; EnvHMaxX = hMaxX; EnvHMinY = hMinY; EnvHMaxY = hMaxY;
        EnvVMinX = vMinX; EnvVMaxX = vMaxX; EnvVMinY = vMinY; EnvVMaxY = vMaxY;
        MaxStrokeWidth = maxPeak;
    }

    // 样式自有色阶（Base RGB 不再参与混色，仅 Base.a 缩放整体 alpha）：
    // * Horizontal：清晰金色阶——亮金 / 深金 / 淡金 / 琥珀。
    // * Vertical：高不透明近黑阶——核心黑 / 次黑 / 暖黑 / 最深。两端颜色另带克制暖色抬升，
    //   而 LineRenderer 的颜色沿线段在 start/end 之间插值，因此整条线实际读作"暖近黑"
    //   （并非只有端点发光、主体完全同色）；亮底上仍是近黑不白不灰，暗底对比度实际弱于亮底，
    //   属已知取舍，观感需实机确认（见 Configure 的暖端说明）。
    private static readonly Color[][] StylePalettes =
    {
        new[]
        {
            new Color(1.00f, 0.84f, 0.32f),
            new Color(0.93f, 0.70f, 0.18f),
            new Color(1.00f, 0.93f, 0.58f),
            new Color(0.80f, 0.55f, 0.12f)
        },
        new[]
        {
            new Color(0.055f, 0.050f, 0.047f),
            new Color(0.085f, 0.077f, 0.070f),
            new Color(0.115f, 0.103f, 0.093f),
            new Color(0.042f, 0.039f, 0.038f)
        }
    };

    // 每条线的宽度曲线：4 个 key 的 time 与四点顶点一一对应（0、1/3、2/3、1），
    // 端部收尖、中部不定对称，切线取相邻 secant 斜率（线性过渡，不超调）。
    // 类型初始化时一次性建立；Configure/Tick 只读引用，池复用不重建。
    private static readonly AnimationCurve[] WidthCurves = BuildWidthCurves();

    private sealed class Dash
    {
        internal GameObject Root;
        internal LineRenderer Line;
    }

    private sealed class Slot
    {
        internal GameObject Root;
        internal Dash[] Dashes;
        internal CoinCourierTeleportStyle Style;   // 每次 Begin（含池复用）先写入，再首次 Configure
        internal CoinCourierTeleportDirection Direction;
        internal float Anchor;                     // 显形/消失锚点（秒），Begin 已校验有限且在界内
        internal Vector3 Origin;
        internal float Age;
        internal bool Active;
        internal float BaseAlpha;
        internal float Scale;
        // 本代 fit（Begin 计算一次；Tick 只读）：世界偏移 = 模板值 × FitX/FitY + FitBx/FitBy；
        // 无身体框的旧形状 = (Scale, Scale, 0, 0) 模板几何。FitStroke 供 widthMultiplier。
        internal float FitX;
        internal float FitY;
        internal float FitBx;
        internal float FitBy;
        internal float FitStroke;
        internal long Generation;
    }

    private static readonly List<Slot> Slots = new();
    private static readonly HashSet<string> Warned = new();
    private static GameObject _root;
    private static Material _material;
    private static bool _materialResolved;
    private static int _lastTickFrame = int.MinValue;
    private static long _nextGeneration;   // 模块单调：Clear/Shutdown 均不重置

    internal static int ActiveCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < Slots.Count; i++)
            {
                if (Slots[i].Active) count++;
            }
            return count;
        }
    }
    /// <summary>在角色脚锚（actor 根 / S.Position，非身体中心）向上立起一组线；3/5/6 参重载保持旧调用形状（Horizontal + Departure + 默认锚点）。</summary>
    internal static CoinCourierFxHandle Begin(Vector3 worldPosition, Color color, float scale)
        => Begin(worldPosition, color, scale, 0, 0);

    /// <summary>5 参重载：排序由调用方显式指定（3 参重载用 0/0），样式保持 Horizontal、方向保持 Departure。</summary>
    internal static CoinCourierFxHandle Begin(Vector3 worldPosition, Color color, float scale,
        int sortingLayerID, int sortingOrder)
        => Begin(worldPosition, color, scale, sortingLayerID, sortingOrder, CoinCourierTeleportStyle.Horizontal);

    /// <summary>6 参重载：样式显式给；方向保持旧默认 Departure、锚点用 <see cref="DefaultAnchorSeconds"/>。</summary>
    internal static CoinCourierFxHandle Begin(Vector3 worldPosition, Color color, float scale,
        int sortingLayerID, int sortingOrder, CoinCourierTeleportStyle style)
        => Begin(worldPosition, color, scale, sortingLayerID, sortingOrder, style,
            CoinCourierTeleportDirection.Departure, DefaultAnchorSeconds);

    /// <summary>
    /// 8 参重载：样式 + 方向 + 显形锚点显式给。anchorSeconds 是"角色显形/消失"时刻：
    /// Arrival 的长度峰值与 Departure 的收束基准都对齐它。非法 style/direction/锚点在占用池槽前拒绝。
    /// </summary>
    internal static CoinCourierFxHandle Begin(Vector3 worldPosition, Color color, float scale,
        int sortingLayerID, int sortingOrder, CoinCourierTeleportStyle style,
        CoinCourierTeleportDirection direction, float anchorSeconds)
        => BeginCore(worldPosition, color, scale, sortingLayerID, sortingOrder, style,
            direction, anchorSeconds, null, false);

    /// <summary>
    /// 9 参重载：显式给"身体来源" renderer（角色根 SpriteRenderer）。Begin 内一次性解析其当前 sprite 的
    /// 可见 alpha 框（可读图集）或已证据 native mesh 近似并按实际 pivot/PPU/scale/flip 做 fit，
    /// 让最终线包络（含 tilt 与 stroke）≤ 身体框 1.05 倍且两轴都贴近身体；**bodySource 为 null 或解析
    /// 失败都在占用池槽前拒收**（真实 caller 不得退化成大模板）→ 调用方安全降级（助手不隐藏不等待、
    /// 哥布林原业务不变）。Tick 不读 sprite/像素。旧 8 参形状保留为模板几何（仅兼容入口/测试）。
    /// </summary>
    internal static CoinCourierFxHandle Begin(Vector3 worldPosition, Color color, float scale,
        int sortingLayerID, int sortingOrder, CoinCourierTeleportStyle style,
        CoinCourierTeleportDirection direction, float anchorSeconds, SpriteRenderer bodySource)
        => BeginCore(worldPosition, color, scale, sortingLayerID, sortingOrder, style,
            direction, anchorSeconds, bodySource, true);

    private static CoinCourierFxHandle BeginCore(Vector3 worldPosition, Color color, float scale,
        int sortingLayerID, int sortingOrder, CoinCourierTeleportStyle style,
        CoinCourierTeleportDirection direction, float anchorSeconds, SpriteRenderer bodySource,
        bool requireBody)
    {
        if (!float.IsFinite(scale) || scale <= 0f) scale = 1f;
        if (!IsFinite(color)) color = Color.white;
        if (style != CoinCourierTeleportStyle.Horizontal && style != CoinCourierTeleportStyle.Vertical)
        {
            WarnOnce("unknown teleport stripe style; refusing to draw");
            return new CoinCourierFxHandle(-1, 0);
        }
        if (direction != CoinCourierTeleportDirection.Departure
            && direction != CoinCourierTeleportDirection.Arrival)
        {
            WarnOnce("unknown teleport direction; refusing to draw");
            return new CoinCourierFxHandle(-1, 0);
        }
        if (!float.IsFinite(anchorSeconds) || anchorSeconds < MinAnchorSeconds
            || anchorSeconds > MaxAnchorSeconds)
        {
            WarnOnce("teleport materialize time out of range; refusing to draw");
            return new CoinCourierFxHandle(-1, 0);
        }
        CoinCourierTeleportBodyBox.Box body = default;
        if (requireBody)
        {
            if (bodySource == null)
            {
                WarnOnce("sized teleport effect requires a body source; refusing to draw");
                return new CoinCourierFxHandle(-1, 0);
            }
            if (!CoinCourierTeleportBodyBox.TryResolve(bodySource, out body))
            {
                WarnOnce("teleport body box unavailable; refusing sized effect");
                return new CoinCourierFxHandle(-1, 0);
            }
        }
        try
        {
            Slot slot = Acquire();
            if (slot == null || slot.Root == null) return new CoinCourierFxHandle(-1, 0);
            if (_nextGeneration == long.MaxValue)
            {
                WarnOnce("effect generation exhausted; refusing new effects");
                return new CoinCourierFxHandle(-1, 0);
            }
            slot.Generation = ++_nextGeneration;
            slot.Style = style;           // 池复用的槽在这里被整体覆盖：样式、方向、锚点、颜色、缩放、fit
            slot.Direction = direction;
            slot.Anchor = anchorSeconds;
            slot.Origin = worldPosition;
            slot.BaseAlpha = color.a;
            slot.Scale = scale;
            if (requireBody) ApplyFit(slot, style, body, scale);
            else
            {
                slot.FitX = scale;
                slot.FitY = scale;
                slot.FitBx = 0f;
                slot.FitBy = 0f;
                slot.FitStroke = scale;
            }
            slot.Age = 0f;
            slot.Active = true;
            slot.Root.SetActive(true);
            for (int i = 0; i < slot.Dashes.Length; i++)
            {
                if (slot.Dashes[i] != null && slot.Dashes[i].Root != null) slot.Dashes[i].Root.SetActive(true);
            }
            Configure(slot, 0f);   // 透明起步：淡入由 Tick 时程负责
            for (int i = 0; i < slot.Dashes.Length; i++)
            {
                slot.Dashes[i].Line.sortingLayerID = sortingLayerID;
                slot.Dashes[i].Line.sortingOrder = sortingOrder;
            }
            return new CoinCourierFxHandle(Slots.IndexOf(slot), slot.Generation);
        }
        catch (Exception e)
        {
            WarnOnce("begin failed: " + e.GetType().Name);
            return new CoinCourierFxHandle(-1, 0);
        }
    }

    /// <summary>
    /// Begin 一次性 fit：先用模板包络做初拟合（sx0/sy0），再由"最终几何（含 tilt 端点 + 最大 stroke）"
    /// 计算真实包络，统一 tighten = min(1, 1.05·bodyW/envW, 1.05·bodyH/envH)，然后把最终包络中心对到
    /// body.Center。斜率始终由最终半长导出（.10），不因宽高差拉歪；初始常量包络不参与最终校验。
    /// </summary>
    private static void ApplyFit(Slot slot, CoinCourierTeleportStyle style,
        in CoinCourierTeleportBodyBox.Box body, float scale)
    {
        bool vertical = style == CoinCourierTeleportStyle.Vertical;
        float templateW = vertical ? EnvVMaxX - EnvVMinX : EnvHMaxX - EnvHMinX;
        float templateH = vertical ? EnvVMaxY - EnvVMinY : EnvHMaxY - EnvHMinY;
        float sx0 = body.Width / templateW;
        float sy0 = body.Height / templateH;
        float strokeHalf = MaxStrokeWidth * 0.5f * scale;
        ComputeFitEnvelope(style, sx0, sy0, out float envW, out float envH, out _, out _);
        envW += 2f * strokeHalf;
        envH += 2f * strokeHalf;
        float tighten = 1f;
        float limitW = 1.05f * body.Width / envW;
        float limitH = 1.05f * body.Height / envH;
        if (limitW < tighten) tighten = limitW;
        if (limitH < tighten) tighten = limitH;
        if (tighten > 1f) tighten = 1f;
        float fitX = sx0 * tighten;
        float fitY = sy0 * tighten;
        ComputeFitEnvelope(style, fitX, fitY, out _, out _, out float centerX, out float centerY);
        slot.FitX = fitX;
        slot.FitY = fitY;
        slot.FitBx = body.Center.x - centerX;
        slot.FitBy = body.Center.y - centerY;
        slot.FitStroke = scale * tighten;
    }

    /// <summary>由给定 fit 系数计算该样式的真实端点包络（每条线的 tilt 端点，16 线无分配遍历）。</summary>
    private static void ComputeFitEnvelope(CoinCourierTeleportStyle style, float fitX, float fitY,
        out float width, out float height, out float centerX, out float centerY)
    {
        bool vertical = style == CoinCourierTeleportStyle.Vertical;
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        for (int i = 0; i < StripeSpecs.Length; i++)
        {
            StripeSpec spec = StripeSpecs[i];
            float x0, x1, y0, y1;
            if (vertical)
            {
                float half = spec.VerticalHalfLength * fitY;
                float lean = half * TiltSlope;
                float x = spec.VerticalOffsetX * fitX;
                float cy = spec.VerticalCenterY * fitY;
                x0 = x - lean; x1 = x + lean; y0 = cy - half; y1 = cy + half;
            }
            else
            {
                float half = spec.HorizontalHalfLength * fitX;
                float rise = half * TiltSlope;
                float cx = spec.HorizontalCenterX * fitX;
                float y = spec.HorizontalOffsetY * fitY;
                x0 = cx - half; x1 = cx + half; y0 = y - rise; y1 = y + rise;
            }
            if (x0 < minX) minX = x0;
            if (x1 > maxX) maxX = x1;
            if (y0 < minY) minY = y0;
            if (y1 > maxY) maxY = y1;
        }
        width = maxX - minX;
        height = maxY - minY;
        centerX = (minX + maxX) * 0.5f;
        centerY = (minY + maxY) * 0.5f;
    }

    /// <summary>只取消该句柄这一代；句柄过期/无效/已结束时不碰任何当前 effect。</summary>
    internal static void Cancel(CoinCourierFxHandle handle)
    {
        if (!handle.IsValid || handle.Slot >= Slots.Count) return;
        Slot slot = Slots[handle.Slot];
        if (!slot.Active || slot.Generation != handle.Generation) return;
        Deactivate(slot);
    }

    /// <summary>调用方驱动的薄包装：按 Time.frameCount 去重，同帧多次调用只推进一次。</summary>
    internal static void Tick(float gameDelta) => TickForFrame(gameDelta, Time.frameCount);

    /// <summary>纯核心：显式帧号去重；非正/非有限增量（暂停、卡帧值）忽略且不消费该帧。</summary>
    internal static void TickForFrame(float gameDelta, int frameId)
    {
        if (!float.IsFinite(gameDelta) || gameDelta <= 0f) return;
        if (frameId == _lastTickFrame) return;
        _lastTickFrame = frameId;
        for (int i = 0; i < Slots.Count; i++)
        {
            Slot slot = Slots[i];
            if (!slot.Active) continue;
            slot.Age += gameDelta;
            if (!(slot.Age / LifetimeSeconds < 1f))
            {
                Deactivate(slot);
                continue;
            }
            Configure(slot, slot.Age);
        }
    }

    /// <summary>世界/模块整体结束才调用：销毁全部 effect 组、根对象与共享材质。之后可继续 Begin。</summary>
    internal static void Clear()
    {
        for (int i = 0; i < Slots.Count; i++)
        {
            Slot slot = Slots[i];
            GameObject root = slot.Root;
            slot.Root = null;
            slot.Dashes = null;
            slot.Active = false;
            if (root != null) DestroyQuietly(root);
        }
        Slots.Clear();
        if (_root != null)
        {
            DestroyQuietly(_root);
            _root = null;
        }
        if (_material != null)
        {
            DestroyQuietly(_material);
            _material = null;
        }
        _materialResolved = false;
        CoinCourierTeleportBodyBox.Clear();   // 池清空时释放身体框缓存（含失败条目）
    }

    /// <summary>
    /// 逐帧热路径：只写既有顶点/颜色/宽度。方向决定长度/密度的时程包络，逐线错落：
    /// Arrival 在 [entry, anchor] 拉长（entry = ArrivalAt × anchor），峰值恰在 anchor，之后按
    /// TailAt 收成残线渐退；Departure 在 [DepartAt × anchor, +anchor×.42] 收束，绝大多数线在
    /// anchor 前淡尽，DepartAt 超过阈值的残线在 anchor 后按 TailAt 淡出。四点共线等分 0、1/3、2/3、1，
    /// 与 widthCurve 的 key time 对齐；端部颜色/alpha 与另一端不同，色相来自样式色阶。
    /// </summary>
    private static void Configure(Slot slot, float age)
    {
        float anchor = slot.Anchor;
        float tailSpan = LifetimeSeconds - anchor;
        bool arrival = slot.Direction == CoinCourierTeleportDirection.Arrival;
        Color[] palette = StylePalettes[(int)slot.Style];
        for (int i = 0; i < slot.Dashes.Length; i++)
        {
            Dash dash = slot.Dashes[i];
            if (dash == null || dash.Line == null) continue;
            StripeSpec spec = StripeSpecs[i];

            float lengthFactor;
            float alphaFactor;
            if (arrival)
            {
                float entry = spec.ArrivalAt * anchor;
                float fadeWindow = anchor - entry;
                if (fadeWindow > ArrivalFadeSeconds) fadeWindow = ArrivalFadeSeconds;
                if (fadeWindow < MinFadeSeconds) fadeWindow = MinFadeSeconds;
                lengthFactor = spec.ArrivalFrom
                    + (1f - spec.ArrivalFrom) * Ramp(age, entry, anchor);
                float tailEnd = anchor + tailSpan * spec.TailAt;
                float tailStart = tailEnd - tailSpan * TailPortion;
                if (tailStart < anchor) tailStart = anchor;
                float tail = Ramp(age, tailStart, tailEnd);
                lengthFactor *= 1f - (1f - ResidualLength) * tail;
                alphaFactor = Ramp(age, entry, entry + fadeWindow) * (1f - tail);
            }
            else
            {
                float inFactor = Ramp(age, 0f, DepartFadeSeconds);
                float collapseStart = spec.DepartAt * anchor;
                float collapseEnd = collapseStart + anchor * CollapsePortion;
                if (collapseEnd > LifetimeSeconds) collapseEnd = LifetimeSeconds;
                float collapse = Ramp(age, collapseStart, collapseEnd);
                lengthFactor = 1f - (1f - ResidualLength) * collapse;
                if (spec.DepartAt > ResidualDepartShare)
                {
                    // 残线：收束到短之后，按自己的 TailAt 时点淡出（与收束窗口错开，不只剩 alpha 同变）。
                    float fadeEnd = anchor + tailSpan * spec.TailAt;
                    if (fadeEnd < collapseEnd) fadeEnd = collapseEnd;
                    alphaFactor = inFactor * (1f - ResidualCollapseAlpha * collapse)
                        * (1f - Ramp(age, collapseEnd, fadeEnd));
                }
                else
                {
                    alphaFactor = inFactor * (1f - collapse);
                }
            }

            Vector3 first;
            Vector3 second;
            switch (slot.Style)
            {
                case CoinCourierTeleportStyle.Vertical:
                {
                    // 统一微斜 + 本代 fit：half 乘 FitY（主轴）、偏移用 (FitX/FitY, FitB)；
                    // lean 由最终 half 导出 → 斜率任何年龄/任何身体尺寸恒 +TiltSlope。
                    float half = spec.VerticalHalfLength * slot.FitY * lengthFactor;
                    float lean = half * TiltSlope;
                    float x = slot.Origin.x + spec.VerticalOffsetX * slot.FitX + slot.FitBx;
                    float centerY = slot.Origin.y + spec.VerticalCenterY * slot.FitY + slot.FitBy;
                    first = new Vector3(x - lean, centerY - half, slot.Origin.z);
                    second = new Vector3(x + lean, centerY + half, slot.Origin.z);
                    break;
                }
                default:   // Horizontal；非法样式已在 Begin 拒收，不可能到达这里
                {
                    float half = spec.HorizontalHalfLength * slot.FitX * lengthFactor;
                    float rise = half * TiltSlope;
                    float y = slot.Origin.y + spec.HorizontalOffsetY * slot.FitY + slot.FitBy;
                    float centerX = slot.Origin.x + spec.HorizontalCenterX * slot.FitX + slot.FitBx;
                    first = new Vector3(centerX - half, y - rise, slot.Origin.z);
                    second = new Vector3(centerX + half, y + rise, slot.Origin.z);
                    break;
                }
            }

            Vector3 step = (second - first) * Third;
            Vector3 mid1 = first + step;
            dash.Line.SetPosition(0, first);
            dash.Line.SetPosition(1, mid1);
            dash.Line.SetPosition(2, mid1 + step);
            dash.Line.SetPosition(3, second);

            Color role = palette[spec.Palette];
            float r = role.r * spec.Brightness;
            float g = role.g * spec.Brightness;
            float b = role.b * spec.Brightness;
            float baseAlpha = slot.BaseAlpha * spec.Opacity * alphaFactor;
            float startAlpha = Math.Clamp(baseAlpha * spec.StartAlpha, 0f, 1f);
            float endAlpha = Math.Clamp(baseAlpha * spec.EndAlpha, 0f, 1f);
            if (slot.Style == CoinCourierTeleportStyle.Vertical)
            {
                // 近黑近色：start/end 两个颜色各按端点权重加克制暖色；LineRenderer 只沿线段在两色
                // 之间插值，所以整条线（含中段）都是暖近黑——不是"端点抬升而主体不变"。
                // 不引入额外光晕层；亮底保持近黑，暗底对比实际弱于亮底（需实机确认）。
                float startRgb = WarmTipR * spec.StartAlpha;
                float startRgbG = WarmTipG * spec.StartAlpha;
                float startRgbB = WarmTipB * spec.StartAlpha;
                float endRgb = WarmTipR * spec.EndAlpha;
                float endRgbG = WarmTipG * spec.EndAlpha;
                float endRgbB = WarmTipB * spec.EndAlpha;
                dash.Line.startColor = new Color(r + startRgb, g + startRgbG, b + startRgbB, startAlpha);
                dash.Line.endColor = new Color(r + endRgb, g + endRgbG, b + endRgbB, endAlpha);
            }
            else
            {
                // 金色：端部随 alpha 权重轻微压暗，主体保持清晰金色。
                float startDim = EndRgbFloor + EndRgbSpan * spec.StartAlpha;
                float endDim = EndRgbFloor + EndRgbSpan * spec.EndAlpha;
                dash.Line.startColor = new Color(r * startDim, g * startDim, b * startDim, startAlpha);
                dash.Line.endColor = new Color(r * endDim, g * endDim, b * endDim, endAlpha);
            }
            dash.Line.widthMultiplier = slot.FitStroke;
            dash.Line.enabled = true;
        }
    }

    /// <summary>[start, end] 区间的线性 0→1 包络；区间退化时按 t≥end 的阶跃处理（绝不产生 NaN）。</summary>
    private static float Ramp(float t, float start, float end)
    {
        if (!(end > start)) return t >= end ? 1f : 0f;
        float value = (t - start) / (end - start);
        if (value <= 0f) return 0f;
        return value >= 1f ? 1f : value;
    }

    private static StripeSpec[] BuildStripeSpecs()
    {
        // 顺序 = 顶点索引 0..15：0~5 为原骨架（长/中/短三档），6~9 为原填 H 行间与 V 列间
        // 空隙的细辅线，10~15 为本次新增的填空细线（H 行 y≈.565/.395/.335/.215/.165/.115，
        // V 列 x≈-.235/-.045/.03/.165/.23/.275；中心与半长都收在既有包络内，不外扩边界）。
        // H（行中心 x、行中心 y、半长）、V（线中心 x、线中心 y、半长）：全组共享 +TiltSlope 微斜、同向平行。
        // 宽度对（t=1/3, t=2/3）乘 BaseLineWidth 后：骨架 .01248~.02496（互不相同），
        // 10 条辅线 .01209~.01287（互不相同、且都 ≤ .0135，细于任一骨架）。
        // 时程列互不重复：ArrivalAt 0~.80、DepartAt .06~.72、TailAt .25~1.00，两方向的激活/收束窗口因此错落。
        return new[]
        {
            new StripeSpec(0.66f, 0.02f, 0.31f, -0.25f, 0.38f, 0.32f, 0.90f, 0.96f, 0, 1.00f, 0.98f, 0.62f, 1.00f, 0.00f, 0.50f, 0.55f, 0.30f),
            new StripeSpec(0.52f, -0.08f, 0.18f, -0.14f, 0.31f, 0.13f, 0.68f, 0.71f, 1, 0.92f, 0.62f, 1.00f, 0.58f, 0.30f, 0.22f, 0.75f, 0.42f),
            new StripeSpec(0.43f, 0.07f, 0.12f, -0.015f, 0.44f, 0.40f, 0.50f, 0.52f, 0, 1.00f, 0.92f, 0.70f, 1.00f, 0.62f, 0.08f, 0.95f, 0.27f),
            new StripeSpec(0.31f, 0.025f, 0.28f, 0.11f, 0.53f, 0.21f, 0.84f, 0.88f, 2, 0.97f, 0.95f, 1.00f, 0.62f, 0.12f, 0.62f, 0.35f, 0.38f),
            new StripeSpec(0.19f, -0.04f, 0.16f, 0.205f, 0.395f, 0.33f, 0.62f, 0.65f, 1, 0.90f, 0.58f, 0.66f, 1.00f, 0.44f, 0.30f, 0.65f, 0.33f),
            new StripeSpec(0.09f, 0.06f, 0.08f, 0.29f, 0.22f, 0.095f, 0.46f, 0.48f, 0, 0.88f, 0.66f, 1.00f, 0.55f, 0.76f, 0.72f, 1.00f, 0.25f),
            new StripeSpec(0.60f, -0.05f, 0.115f, -0.20f, 0.40f, 0.16f, 0.482f, 0.495f, 1, 0.90f, 0.58f, 0.60f, 1.00f, 0.20f, 0.15f, 0.45f, 0.45f),
            new StripeSpec(0.47f, 0.04f, 0.10f, -0.08f, 0.47f, 0.12f, 0.466f, 0.478f, 0, 0.88f, 0.52f, 1.00f, 0.62f, 0.52f, 0.40f, 0.85f, 0.30f),
            new StripeSpec(0.25f, -0.055f, 0.09f, 0.055f, 0.30f, 0.15f, 0.472f, 0.484f, 2, 0.95f, 0.64f, 0.68f, 1.00f, 0.36f, 0.26f, 0.30f, 0.40f),
            new StripeSpec(0.14f, 0.045f, 0.06f, 0.255f, 0.26f, 0.10f, 0.464f, 0.475f, 1, 0.86f, 0.46f, 1.00f, 0.56f, 0.68f, 0.55f, 0.90f, 0.28f),
            new StripeSpec(0.565f, -0.07f, 0.145f, -0.235f, 0.18f, 0.115f, 0.474f, 0.490f, 1, 0.90f, 0.56f, 0.62f, 1.00f, 0.08f, 0.06f, 0.25f, 0.36f),
            new StripeSpec(0.395f, -0.03f, 0.095f, -0.045f, 0.25f, 0.135f, 0.470f, 0.461f, 0, 0.88f, 0.60f, 1.00f, 0.58f, 0.58f, 0.46f, 0.80f, 0.31f),
            new StripeSpec(0.335f, -0.01f, 0.125f, 0.03f, 0.235f, 0.155f, 0.469f, 0.457f, 3, 0.90f, 0.52f, 0.66f, 1.00f, 0.24f, 0.18f, 0.40f, 0.43f),
            new StripeSpec(0.215f, 0.01f, 0.105f, 0.165f, 0.415f, 0.105f, 0.488f, 0.475f, 1, 0.92f, 0.62f, 1.00f, 0.60f, 0.04f, 0.35f, 0.60f, 0.34f),
            new StripeSpec(0.165f, 0.05f, 0.07f, 0.23f, 0.48f, 0.091f, 0.465f, 0.452f, 0, 0.86f, 0.50f, 0.68f, 1.00f, 0.48f, 0.68f, 0.70f, 0.26f),
            new StripeSpec(0.115f, 0.065f, 0.065f, 0.272f, 0.335f, 0.125f, 0.481f, 0.469f, 2, 0.88f, 0.58f, 1.00f, 0.64f, 0.80f, 0.58f, 0.50f, 0.39f)
        };
    }

    private static AnimationCurve[] BuildWidthCurves()
    {
        var curves = new AnimationCurve[StripeSpecs.Length];
        for (int i = 0; i < curves.Length; i++)
        {
            StripeSpec spec = StripeSpecs[i];
            float peak = (spec.WidthAtFirstThird > spec.WidthAtSecondThird
                ? spec.WidthAtFirstThird : spec.WidthAtSecondThird) * BaseLineWidth;
            float v0 = peak * TipShare;
            float v1 = spec.WidthAtFirstThird * BaseLineWidth;
            float v2 = spec.WidthAtSecondThird * BaseLineWidth;
            float v3 = peak * TipShareEnd;
            float m0 = (v1 - v0) / Third;          // 相邻 key 的 secant 斜率：线性过渡、无超调
            float m1 = (v2 - v1) / Third;
            float m2 = (v3 - v2) / Third;
            var keys = new Keyframe[4];
            keys[0] = new Keyframe(0f, v0, m0, m0);
            keys[1] = new Keyframe(Third, v1, m0, m1);
            keys[2] = new Keyframe(2f * Third, v2, m1, m2);
            keys[3] = new Keyframe(1f, v3, m2, m2);
            curves[i] = new AnimationCurve(keys);
        }
        return curves;
    }

    private static Slot Acquire()
    {
        for (int i = 0; i < Slots.Count; i++)
        {
            if (!Slots[i].Active) return Slots[i];
        }
        if (Slots.Count < MaxConcurrent)
        {
            Slot created = CreateSlot();
            if (created != null) Slots.Add(created);
            return created;
        }
        Slot oldest = null;
        for (int i = 0; i < Slots.Count; i++)
        {
            if (oldest == null || Slots[i].Age > oldest.Age) oldest = Slots[i];
        }
        return oldest;   // 池满：复用最旧的 effect 组（上限按组计）
    }

    private static Slot CreateSlot()
    {
        GameObject group = null;
        try
        {
            if (!EnsureRoot()) return null;
            group = new GameObject("KEM_CoinCourierTeleportFxEffect");
            group.transform.SetParent(_root.transform, false);
            group.transform.localPosition = Vector3.zero;
            Dash[] dashes = new Dash[DashesPerEffect];
            for (int i = 0; i < dashes.Length; i++)
            {
                GameObject dashObject = new GameObject("KEM_CoinCourierTeleportDash" + i);
                dashObject.transform.SetParent(group.transform, false);
                LineRenderer line = dashObject.AddComponent<LineRenderer>();
                if (line == null)
                {
                    DestroyQuietly(dashObject);
                    throw new InvalidOperationException("line renderer missing on a dash");
                }
                line.useWorldSpace = true;
                line.loop = false;
                line.positionCount = 4;   // 四点共线等分，与 widthCurve 的 4 个 key 对齐
                line.numCapVertices = 0;
                line.numCornerVertices = 0;
                line.widthCurve = WidthCurves[i];   // 一次建立、池复用不重建
                Material material = ResolveMaterial();
                if (material != null) line.sharedMaterial = material;
                line.enabled = false;
                dashObject.SetActive(false);
                dashes[i] = new Dash { Root = dashObject, Line = line };
            }
            group.SetActive(false);
            return new Slot { Root = group, Dashes = dashes };
        }
        catch (Exception e)
        {
            if (group != null) DestroyQuietly(group);
            WarnOnce("effect group create failed: " + e.GetType().Name);
            return null;
        }
    }

    private static void Deactivate(Slot slot)
    {
        slot.Active = false;
        slot.Age = 0f;
        try
        {
            if (slot.Dashes != null)
            {
                for (int i = 0; i < slot.Dashes.Length; i++)
                {
                    Dash dash = slot.Dashes[i];
                    if (dash == null) continue;
                    if (dash.Line != null) dash.Line.enabled = false;
                    if (dash.Root != null) dash.Root.SetActive(false);
                }
            }
            if (slot.Root != null) slot.Root.SetActive(false);
        }
        catch (Exception)
        {
            // 停用失败不抛出：下一帧 Tick 会再次尝试。
        }
    }

    private static bool EnsureRoot()
    {
        if (_root != null) return true;
        try
        {
            _root = new GameObject("KEM_CoinCourierTeleportFx");
            UnityEngine.Object.DontDestroyOnLoad(_root);
            return true;
        }
        catch (Exception e)
        {
            _root = null;
            WarnOnce("root create failed: " + e.GetType().Name);
            return false;
        }
    }

    private static Material ResolveMaterial()
    {
        if (_materialResolved) return _material;
        _materialResolved = true;
        try
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null) _material = new Material(shader);
        }
        catch (Exception)
        {
            _material = null;
        }
        if (_material == null) WarnOnce("Sprites/Default shader unavailable; dashes keep the renderer default material");
        return _material;
    }

    private static bool IsFinite(Color value)
        => float.IsFinite(value.r) && float.IsFinite(value.g)
        && float.IsFinite(value.b) && float.IsFinite(value.a);

    private static void DestroyQuietly(UnityEngine.Object target)
    {
        try { if (target != null) UnityEngine.Object.Destroy(target); }
        catch (Exception) { }
    }

    private static void WarnOnce(string reason)
    {
        if (!Warned.Add(reason)) return;
        try
        {
            KingdomEnhancedPlugin.Instance?.LogSource?.LogWarning("[CoinCourierTeleportFx] " + reason);
        }
        catch (Exception)
        {
            // 日志不可用时保持静默：表现层绝不因日志失败而中断。
        }
    }
}
