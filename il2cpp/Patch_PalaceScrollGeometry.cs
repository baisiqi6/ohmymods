using System;

namespace KingdomEnhancedMod;

/// <summary>
/// R4「诸岛共天、同入天朝」1448×252 卷轴的共享坐标标定：逻辑像素（left-top 原点、x 向右、y 向下）
/// 与归一化 pivot（BottomLeft）到世界坐标的换算。
/// source-only 纯离线数学：仅依赖 System；无 Unity/Native/GUI/系统调用/文件读取/环境变量，
/// 无周期扫描、无 hook、无全局可变状态、无 clamp/fallback（非法值一律拒绝）。
///
/// 唯一创建/标定 owner 是未来 <c>PalaceCourtScene.Create → CourtBinding</c>：由创建处取得当前 world/ground/父层后构造一次，
/// 所有模块与角色消费同一校准。本模块不测量、不推导、不设置真实 s，也不为 s/anchor/footline 提供默认值；
/// 禁止用旧线索（19px 身体、PPU32、45.7142857、112u 陆地跨度）反推 s——它们不是 R4 比例。
/// 当前原生 BODY、PPU、waterplane 与 camera world 宽均未知，世界值只能由 owner 显式给出。
/// 不派生任何视差 ratio、不采样/扫描相机；不引入新交互、新副作用或新角色机制。
/// </summary>
internal sealed class PalaceScrollCalibration
{
    /// <summary>主画布逻辑宽（px）。</summary>
    internal const int CanvasWidth = 1448;

    /// <summary>主画布逻辑高（px）。</summary>
    internal const int CanvasHeight = 252;

    /// <summary>共享锚点像素 X：footline 上的水平锚（主图坐标系）。</summary>
    internal const int AnchorPixelX = 645;

    /// <summary>共享脚线像素 Y：1448×252 主图自上而下的脚线位置。</summary>
    internal const int FootlinePixelY = 187;

    /// <summary>世界单位/逻辑像素（s）；由创建 owner 显式给出，本模块不提供默认值。</summary>
    internal double UnitsPerPixel { get; }

    /// <summary>锚点垂线（主图 x=645）的世界 X；由创建 owner 显式给出。</summary>
    internal double AnchorWorldX { get; }

    /// <summary>脚线（主图 y=187）的世界 Y；由创建 owner 显式给出。</summary>
    internal double GroundWorldY { get; }

    /// <summary>1/s，即 Unity SpriteRenderer 的 PPU；构造时已验证可表达为 positive finite float。</summary>
    internal double PixelsPerUnit { get; }

    /// <summary>
    /// 由创建 owner（未来 PalaceCourtScene.Create → CourtBinding）在当前场景读取/决定后调用一次。
    /// 三个参数都必须 finite；s 必须 &gt; 0，且 1/s 必须 finite 且能表达为 positive finite float（供 Unity PPU）。
    /// 任一不满足抛 ArgumentOutOfRangeException——拒绝而非默认。
    /// </summary>
    internal PalaceScrollCalibration(double unitsPerPixel, double anchorWorldX, double groundWorldY)
    {
        if (!IsFinite(unitsPerPixel))
            throw new ArgumentOutOfRangeException(nameof(unitsPerPixel), unitsPerPixel, "unitsPerPixel must be finite.");
        if (unitsPerPixel <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(unitsPerPixel), unitsPerPixel, "unitsPerPixel must be > 0.");

        double pixelsPerUnit = 1.0 / unitsPerPixel;
        if (!IsFinite(pixelsPerUnit))
            throw new ArgumentOutOfRangeException(nameof(unitsPerPixel), unitsPerPixel, "1/unitsPerPixel must be finite.");
        float ppuFloat = (float)pixelsPerUnit;
        if (float.IsInfinity(ppuFloat) || !(ppuFloat > 0f))
            throw new ArgumentOutOfRangeException(nameof(unitsPerPixel), unitsPerPixel,
                "1/unitsPerPixel must be expressible as a positive finite float (Unity PPU).");

        if (!IsFinite(anchorWorldX))
            throw new ArgumentOutOfRangeException(nameof(anchorWorldX), anchorWorldX, "anchorWorldX must be finite.");
        if (!IsFinite(groundWorldY))
            throw new ArgumentOutOfRangeException(nameof(groundWorldY), groundWorldY, "groundWorldY must be finite.");

        UnitsPerPixel = unitsPerPixel;
        AnchorWorldX = anchorWorldX;
        GroundWorldY = groundWorldY;
        PixelsPerUnit = pixelsPerUnit;
    }

    /// <summary>
    /// 主图逻辑像素 X → 世界 X：anchorWorldX + s*(pixelX-AnchorPixelX)。
    /// 输入与结果都必须 finite；非 finite 或溢出抛 ArgumentOutOfRangeException，不 clamp、不 fallback。
    /// </summary>
    internal double WorldX(double masterPixelX)
    {
        RequireFinite(masterPixelX, nameof(masterPixelX));
        double worldX = AnchorWorldX + UnitsPerPixel * (masterPixelX - AnchorPixelX);
        if (!IsFinite(worldX))
            throw new ArgumentOutOfRangeException(nameof(masterPixelX), masterPixelX, "world X result must be finite.");
        return worldX;
    }

    /// <summary>
    /// 主图逻辑像素 Y → 世界 Y：groundY + s*(FootlinePixelY-pixelY)（像素 y 向下，世界 y 向上）。
    /// 输入与结果都必须 finite；非 finite 或溢出抛 ArgumentOutOfRangeException，不 clamp、不 fallback。
    /// </summary>
    internal double WorldY(double masterPixelY)
    {
        RequireFinite(masterPixelY, nameof(masterPixelY));
        double worldY = GroundWorldY + UnitsPerPixel * (FootlinePixelY - masterPixelY);
        if (!IsFinite(worldY))
            throw new ArgumentOutOfRangeException(nameof(masterPixelY), masterPixelY, "world Y result must be finite.");
        return worldY;
    }

    // The adapter crosses from CLR doubles to Unity floats. Reject lost/overflowed canvas
    // extents before allocating assets; this does not choose a real Native calibration.
    internal void RequireUnityCanvas()
    {
        float ppu = (float)PixelsPerUnit;
        float width = 1512f / ppu, height = CanvasHeight / ppu;
        float left = (float)WorldX(-32), axis = (float)WorldX(AnchorPixelX), right = (float)WorldX(1480);
        float bottom = (float)WorldY(CanvasHeight), ground = (float)WorldY(FootlinePixelY), top = (float)WorldY(0);
        if (!float.IsFinite(width) || !float.IsFinite(height) || width <= 0f || height <= 0f
            || !float.IsFinite(left) || !float.IsFinite(axis) || !float.IsFinite(right)
            || !float.IsFinite(bottom) || !float.IsFinite(ground) || !float.IsFinite(top)
            || !(left < axis && axis < right && bottom < ground && ground < top))
            throw new ArgumentOutOfRangeException(nameof(UnitsPerPixel), "R4 canvas is not representable in Unity float coordinates.");
    }

    /// <summary>
    /// 主图与摘件（含 bleed）共用的归一化 BottomLeft pivot（Unity sprite pivot 约定）：
    /// X = (AnchorPixelX-originX)/width，Y = (height-(FootlinePixelY-originY))/height。
    /// 这是共同锚，不是每张图底部归零；允许合法的 0..1 之外的值（摘件），原样返回、不 clamp。
    /// origin 允许为负（bleed）；width/height 必须 &gt; 0，否则抛 ArgumentOutOfRangeException。
    /// </summary>
    internal PalaceScrollPivot SharedPivot(int cropOriginX, int cropOriginY, int width, int height)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), width, "width must be > 0.");
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), height, "height must be > 0.");

        // origin 先提升为 double 再相减：避免极值 origin 下 int 减法回绕。
        double x = (AnchorPixelX - (double)cropOriginX) / width;
        double y = (height - (FootlinePixelY - (double)cropOriginY)) / height;
        return new PalaceScrollPivot(x, y);
    }

    /// <summary>
    /// 只校验显式 future 计划里已评审的后景曝光范围（px）：三个参数 finite、min &lt;= max、min &gt;= -16、max &lt;= 16、vertical == 0，
    /// 否则抛 ArgumentOutOfRangeException。本方法不派生任何 ratio、不采样/扫描相机，
    /// 也不宣称 camera / Native ratio / 32px bleed 的全程行为已被验证。
    /// </summary>
    internal static void RequireReviewedRearEnvelope(double minOffsetPixels, double maxOffsetPixels, double verticalOffsetPixels)
    {
        RequireFinite(minOffsetPixels, nameof(minOffsetPixels));
        RequireFinite(maxOffsetPixels, nameof(maxOffsetPixels));
        RequireFinite(verticalOffsetPixels, nameof(verticalOffsetPixels));

        if (minOffsetPixels > maxOffsetPixels)
            throw new ArgumentOutOfRangeException(nameof(minOffsetPixels), minOffsetPixels, "minOffsetPixels must be <= maxOffsetPixels.");
        if (minOffsetPixels < -16.0)
            throw new ArgumentOutOfRangeException(nameof(minOffsetPixels), minOffsetPixels, "minOffsetPixels must be >= -16.");
        if (maxOffsetPixels > 16.0)
            throw new ArgumentOutOfRangeException(nameof(maxOffsetPixels), maxOffsetPixels, "maxOffsetPixels must be <= 16.");
        if (verticalOffsetPixels != 0.0)
            throw new ArgumentOutOfRangeException(nameof(verticalOffsetPixels), verticalOffsetPixels, "verticalOffsetPixels must be 0.");
    }

    private static void RequireFinite(double value, string parameterName)
    {
        if (!IsFinite(value))
            throw new ArgumentOutOfRangeException(parameterName, value, "value must be finite.");
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}

/// <summary>
/// 归一化 BottomLeft pivot（Unity sprite pivot 约定），由 SharedPivot 按共同锚计算；
/// 允许 0..1 之外的值（摘件），原样传递、不 clamp。
/// </summary>
internal readonly struct PalaceScrollPivot
{
    internal double X { get; }
    internal double Y { get; }

    internal PalaceScrollPivot(double x, double y)
    {
        X = x;
        Y = y;
    }
}
