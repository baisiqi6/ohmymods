using System;

namespace KingdomEnhancedMod;

/// <summary>Explicit logical master/transport coordinates. Caller owns reviewed asset provenance;
/// construction validates shape only and never declares art/native acceptance.</summary>
internal sealed class PalaceScrollDescriptor
{
    internal PalaceLayoutRecipe Recipe { get; }
    internal int MasterWidthPx { get; }
    internal int MasterHeightPx { get; }
    internal int AxisPixelX { get; }
    internal int FootlinePixelY { get; }
    internal int TransportLeftPx { get; }
    internal int TransportTopPx { get; }
    internal int TransportRightPx { get; }
    internal int TransportBottomPx { get; }
    internal int TransportWidthPx => TransportRightPx-TransportLeftPx;
    internal int TransportHeightPx => TransportBottomPx-TransportTopPx;
    internal static readonly PalaceScrollDescriptor LegacyR4 = new();
    internal static readonly PalaceScrollDescriptor Legacy44 = new(PalaceLayoutRecipe.Known44,1448,252,187,-32,0,1480,252);

    // The old R4 canvas is also used by the legacy48 added region. It is not a whole-island recipe.
    private PalaceScrollDescriptor()
    {
        Recipe=null; MasterWidthPx=1448; MasterHeightPx=252; AxisPixelX=645; FootlinePixelY=187;
        TransportLeftPx=-32; TransportTopPx=0; TransportRightPx=1480; TransportBottomPx=252;
    }

    internal PalaceScrollDescriptor(PalaceLayoutRecipe recipe,int masterWidthPx,int masterHeightPx,
        int footlinePixelY,int transportLeftPx,int transportTopPx,int transportRightPx,int transportBottomPx)
    {
        if(!PalaceLayoutRecipe.IsKnown(recipe))throw new ArgumentException("Unknown layout recipe.",nameof(recipe));
        if(masterWidthPx!=recipe.MasterWidthPx || masterHeightPx!=recipe.MasterHeightPx)
            throw new ArgumentException("Master dimensions differ from the known layout.");
        if(footlinePixelY<=0 || footlinePixelY>=masterHeightPx)
            throw new ArgumentOutOfRangeException(nameof(footlinePixelY));
        long tw=(long)transportRightPx-transportLeftPx,th=(long)transportBottomPx-transportTopPx;
        // Bounds arithmetic/descriptor extent; not individual texture size or a camera exposure claim.
        if(tw<masterWidthPx || tw>65536 || th<masterHeightPx || th>2048 || transportLeftPx>0 || transportTopPx>0
            || transportRightPx<masterWidthPx || transportBottomPx<masterHeightPx)
            throw new ArgumentOutOfRangeException(nameof(transportRightPx),"Transport must contain the explicit master within the bounded canvas.");
        Recipe=recipe; AxisPixelX=recipe.AxisPx; MasterWidthPx=masterWidthPx; MasterHeightPx=masterHeightPx; FootlinePixelY=footlinePixelY;
        TransportLeftPx=transportLeftPx; TransportTopPx=transportTopPx; TransportRightPx=transportRightPx; TransportBottomPx=transportBottomPx;
    }
}

/// <summary>
/// R4 compatibility and explicit known-layout scroll calibration: logical pixels (left-top, x right, y down)
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
    internal PalaceScrollDescriptor Descriptor { get; }

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
        : this(PalaceScrollDescriptor.LegacyR4,unitsPerPixel,anchorWorldX,groundWorldY,false) { }

    internal PalaceScrollCalibration(PalaceScrollDescriptor descriptor,double unitsPerPixel,double anchorWorldX,double groundWorldY)
        : this(descriptor,unitsPerPixel,anchorWorldX,groundWorldY,true) { }

    private PalaceScrollCalibration(PalaceScrollDescriptor descriptor,double unitsPerPixel,double anchorWorldX,double groundWorldY,bool physicalRecipe)
    {
        Descriptor=descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        if(physicalRecipe && (!PalaceLayoutRecipe.IsKnown(descriptor.Recipe)
            || unitsPerPixel!=descriptor.Recipe.UnitsPerPixel || anchorWorldX!=descriptor.Recipe.GateWorldX))
            throw new ArgumentException("Physical layout calibration must match the frozen recipe grid/gate.");
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
    /// Master logical X → world X: anchorWorldX + s*(pixelX-descriptor.AxisPixelX).
    /// 输入与结果都必须 finite；非 finite 或溢出抛 ArgumentOutOfRangeException，不 clamp、不 fallback。
    /// </summary>
    internal double WorldX(double masterPixelX)
    {
        RequireFinite(masterPixelX, nameof(masterPixelX));
        double worldX = AnchorWorldX + UnitsPerPixel * (masterPixelX - Descriptor.AxisPixelX);
        if (!IsFinite(worldX))
            throw new ArgumentOutOfRangeException(nameof(masterPixelX), masterPixelX, "world X result must be finite.");
        return worldX;
    }

    /// <summary>
    /// Logical master Y → world Y uses the descriptor street reference. Individual assets use semantic local supports.
    /// 输入与结果都必须 finite；非 finite 或溢出抛 ArgumentOutOfRangeException，不 clamp、不 fallback。
    /// </summary>
    internal double WorldY(double masterPixelY)
    {
        RequireFinite(masterPixelY, nameof(masterPixelY));
        double worldY = GroundWorldY + UnitsPerPixel * (Descriptor.FootlinePixelY - masterPixelY);
        if (!IsFinite(worldY))
            throw new ArgumentOutOfRangeException(nameof(masterPixelY), masterPixelY, "world Y result must be finite.");
        return worldY;
    }

    // Local semantic support placement for street assets. Source/master transport Y is deliberately
    // not an input. Boat waterlines and theatre-deck actors require their own measured/transformed plane.
    internal double StreetWorldX(double designSupportX,double supportLocalX,double localX)
    {
        RequireFinite(supportLocalX,nameof(supportLocalX)); RequireFinite(localX,nameof(localX));
        double result=WorldX(designSupportX)+UnitsPerPixel*(localX-supportLocalX);
        RequireFinite(result,nameof(localX)); return result;
    }
    internal double StreetWorldY(double supportLocalY,double localY,double authoredOffsetYUnits)
    {
        RequireFinite(supportLocalY,nameof(supportLocalY)); RequireFinite(localY,nameof(localY));
        RequireFinite(authoredOffsetYUnits,nameof(authoredOffsetYUnits));
        double result=GroundWorldY+authoredOffsetYUnits+UnitsPerPixel*(supportLocalY-localY);
        RequireFinite(result,nameof(localY)); return result;
    }

    // The adapter crosses from CLR doubles to Unity floats. Reject lost/overflowed canvas
    // extents before allocating assets; this does not choose a real Native calibration.
    internal void RequireUnityCanvas()
    {
        float ppu = (float)PixelsPerUnit;
        float width = Descriptor.TransportWidthPx / ppu, height = Descriptor.TransportHeightPx / ppu;
        float left = (float)WorldX(Descriptor.TransportLeftPx), axis = (float)WorldX(Descriptor.AxisPixelX), right = (float)WorldX(Descriptor.TransportRightPx);
        float bottom = (float)WorldY(Descriptor.TransportBottomPx), ground = (float)WorldY(Descriptor.FootlinePixelY), top = (float)WorldY(Descriptor.TransportTopPx);
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
        double x = (Descriptor.AxisPixelX - (double)cropOriginX) / width;
        double y = (height - (Descriptor.FootlinePixelY - (double)cropOriginY)) / height;
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
