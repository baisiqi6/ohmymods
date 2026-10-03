using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>新四税收助手 atlas 的动作行（帧序固定：row = frame / 8）。</summary>
internal enum BankAssistantAtlasAction
{
    Idle = 0,
    Walk = 1,
    Run = 2,
    Leisure = 3,
}

/// <summary>
/// 一套新助手的图集固定数据 + 纯帧换算（无 Unity 调用、无时钟、无分配）。
///
/// 数据来源（只读核验，逐字节未改图）：2026-09-29 用户验收的四份最终素材
/// （trio32/clerk-complete、identity-motion-v2/{caravan,steward,vaultkeeper}），逐字节复制为
/// il2cpp/Assets/BankAssistant*.png；格子/anchor/fixed_ppu/步幅取自各源旁 pose-metadata.json
/// （authoritative），生产汇总见 artifacts/bank-assistant-skins-20260928/production-metadata.json
/// （含生产 PNG SHA256 与逐帧实测 bbox）。
///
/// 装配规则（本文件就是最终固定数据，运行时不再计算）：
/// * 整格网格 rect：8 列 × 4 行，rect = (col × cellW, (Rows−1−row) × cellH, cellW, cellH)。
///   不再使用逐帧 bbox 安全框，也不再每帧移动 rect 顶/尺寸；Clerk 每格 32×40（图集 256×160），
///   Caravan/Steward/Vaultkeeper 每格 40×40（图集 320×160）。
/// * pivot 固定 rig 原点：X = anchor.x（Clerk 16、其余 20），Y = cellH − anchor.y = 3，
///   不逐帧按 bbox 居中；站立脚底最后一像素行 = 36，锚线（地面线）在格内 top-origin 37。
/// * 自然起伏/腾空保留在图内像素（Run 部分帧脚底最高抬起 2px），不逐帧把脚底拉齐。
/// </summary>
internal sealed class BankAssistantAtlasStyle
{
    internal readonly string Name;
    internal readonly string ResourceName;
    internal readonly int SheetWidth;
    internal readonly int SheetHeight;

    /// <summary>固定 rig PPU：32px 站立身体 / 0.671875 世界单位 = 47.627907，四位共用
    /// （各源 pose-metadata.json 的 fixed_ppu；站高基准 = 希腊主银行家 Idle 20px / PPU32 ×
    /// 现有 Greek 路径 mod Y 1.075，见 banker-height-contract.md）；actor 有效视觉 Y=1.0 时成立。</summary>
    internal readonly float PixelsPerUnit;

    /// <summary>authored 动作时长（秒）。Idle 1.2s 是这套呼吸循环自己的周期；Leisure 沿用
    /// 既有 CharacterLeisureClock 的 2.4s。Walk 0.8s / Run 0.56s 只是已验收素材的预览时长
    /// （8 帧 × 100ms / 70ms），运行期 Walk/Run 帧由 BankAssistantAtlasVisuals 的步频相位
    /// 积分选择，这两个值绝不形成第二条运行时推进分支。</summary>
    internal readonly float IdleSeconds;
    internal readonly float WalkSeconds;
    internal readonly float RunSeconds;
    internal readonly float LeisureSeconds;

    /// <summary>一个完整步态周期内支撑脚的世界位移换算像素（strideWorld = StridePixels /
    /// PixelsPerUnit × |lossyScale.x|）。来自各源 pose-metadata.json 的 stride_logical_px：
    /// Clerk 16/24、Caravan 16/24、Steward 16/16、Vaultkeeper 8/12（矮人有 ≤0.5 逻辑像素
    /// 滚动量化差，源文件已记录）。运行期按 dt×speed/strideWorld 积分步态相位。</summary>
    internal readonly float WalkStridePixels;
    internal readonly float RunStridePixels;

    /// <summary>32 帧 rect（Unity 纹理坐标，左下原点；整格网格）。</summary>
    internal readonly Rect[] Rects;

    /// <summary>rect 内像素 pivot X：固定 rig 原点（= anchor.x，全 32 帧同一值）。</summary>
    internal readonly float[] PivotXPixels;
    internal readonly int PivotYPixels;

    internal BankAssistantAtlasStyle(string name, string resourceName, int sheetWidth, int sheetHeight,
        float pixelsPerUnit, float idleSeconds, float walkSeconds, float runSeconds, float leisureSeconds,
        float walkStridePixels, float runStridePixels,
        float[] pivotXPixels, int pivotYPixels, Rect[] rects)
    {
        Name = name;
        ResourceName = resourceName;
        SheetWidth = sheetWidth;
        SheetHeight = sheetHeight;
        PixelsPerUnit = pixelsPerUnit;
        IdleSeconds = idleSeconds;
        WalkSeconds = walkSeconds;
        RunSeconds = runSeconds;
        LeisureSeconds = leisureSeconds;
        WalkStridePixels = walkStridePixels;
        RunStridePixels = runStridePixels;
        PivotXPixels = pivotXPixels;
        PivotYPixels = pivotYPixels;
        Rects = rects;
    }

    internal float DurationOf(BankAssistantAtlasAction action)
    {
        switch (action)
        {
            case BankAssistantAtlasAction.Walk: return WalkSeconds;
            case BankAssistantAtlasAction.Run: return RunSeconds;
            case BankAssistantAtlasAction.Leisure: return LeisureSeconds;
            default: return IdleSeconds;
        }
    }

    /// <summary>frame 0..31 → 动作行（行主序）。</summary>
    internal static BankAssistantAtlasAction ActionOfFrame(int frame)
    {
        int row = frame / BankAssistantAtlasMetadata.FramesPerAction;
        return row < 0 || row > 3 ? BankAssistantAtlasAction.Idle : (BankAssistantAtlasAction)row;
    }
}

/// <summary>
/// 四套新助手图集的固定 rect/pivot/PPU/帧序数据。槽位 4..7 顺序 = Clerk / Caravan / Steward / Vaultkeeper。
/// 纯数据 + 纯换算；不含 Unity 资源加载、不含时钟、不做任何写操作。
/// </summary>
internal static class BankAssistantAtlasMetadata
{
    internal const int Columns = 8;
    internal const int Rows = 4;
    internal const int FramesPerAction = 8;
    internal const int FrameCount = Columns * Rows;

    /// <summary>站高目标（世界单位）= 希腊主银行家 Idle 可见站高：原生 20px / PPU32 × 现有 Greek 路径 mod localScale.y 1.075 = 0.671875。
    /// 2026-09-28 用户实机反馈新四整体高于原银行家后，由五位中位 0.7390625 改定（banker-height-contract.md）；现有五位不动。</summary>
    internal const float TargetStandHeight = 0.671875f;

    /// <summary>相位上限（秒）：超过即饱和，避免巨大 normalizedTime × 长度产生精度噪声。</summary>
    internal const float MaxPhaseSeconds = 1e6f;

    private const int CellHeight = 40;

    /// <summary>固定 rig 锚点（top-origin）：站立脚底最后一像素行 = 36，地面线在格内 y=37；
    /// pivot = (anchorX, cellH − 37 = 3)。不逐帧按 bbox 居中。</summary>
    private const int AnchorYFromTop = 37;
    private const int PivotY = CellHeight - AnchorYFromTop;

    /// <summary>固定 rig PPU（四位共用）：32 / 0.671875 = 47.627907。</summary>
    private const float FixedPixelsPerUnit = 32f / TargetStandHeight;

    private const int ClerkCellWidth = 32;
    private const int AssistantCellWidth = 40;

    internal static readonly BankAssistantAtlasStyle[] Styles =
    {
        // A 黑帽青衣账房（东方账房）：clerk-complete 验收图集，cell 32×40，anchor 16,37；walk/run 步幅 16/24。
        new BankAssistantAtlasStyle("clerk", "KingdomEnhancedMod.BankAssistantClerk.png",
            Columns * ClerkCellWidth, Rows * CellHeight, FixedPixelsPerUnit,
            1.2f, 0.8f, 0.56f, 2.4f,
            16f, 24f, BuildFixedPivots(16f), PivotY, BuildGridRects(ClerkCellWidth, CellHeight)),
        // B 白头巾棕衣商人（沙漠商人）：cell 40×40，anchor 20,37；walk/run 步幅 16/24。
        new BankAssistantAtlasStyle("caravan", "KingdomEnhancedMod.BankAssistantCaravan.png",
            Columns * AssistantCellWidth, Rows * CellHeight, FixedPixelsPerUnit,
            1.2f, 0.8f, 0.56f, 2.4f,
            16f, 24f, BuildFixedPivots(20f), PivotY, BuildGridRects(AssistantCellWidth, CellHeight)),
        // C 白发紫衣女管家（皇家女管家）：cell 40×40，anchor 20,37；walk/run 步幅 16/16。
        new BankAssistantAtlasStyle("steward", "KingdomEnhancedMod.BankAssistantSteward.png",
            Columns * AssistantCellWidth, Rows * CellHeight, FixedPixelsPerUnit,
            1.2f, 0.8f, 0.56f, 2.4f,
            16f, 16f, BuildFixedPivots(20f), PivotY, BuildGridRects(AssistantCellWidth, CellHeight)),
        // D 秃顶胡须绿衣掌柜（矮人金库管家）：cell 40×40，anchor 20,37；walk/run 步幅 8/12。
        new BankAssistantAtlasStyle("vaultkeeper", "KingdomEnhancedMod.BankAssistantVaultkeeper.png",
            Columns * AssistantCellWidth, Rows * CellHeight, FixedPixelsPerUnit,
            1.2f, 0.8f, 0.56f, 2.4f,
            8f, 12f, BuildFixedPivots(20f), PivotY, BuildGridRects(AssistantCellWidth, CellHeight)),
    };

    /// <summary>整格网格 rect（Unity 左下原点）：行主序 Idle/Walk/Run/Leisure，每格同一尺寸；
    /// 只构造一次，运行时零分配。</summary>
    private static Rect[] BuildGridRects(int cellWidth, int cellHeight)
    {
        var rects = new Rect[FrameCount];
        for (int frame = 0; frame < rects.Length; frame++)
        {
            int row = frame / Columns;
            int column = frame % Columns;
            rects[frame] = new Rect(column * cellWidth, (Rows - 1 - row) * cellHeight, cellWidth, cellHeight);
        }
        return rects;
    }

    /// <summary>固定 rig pivot X：全 32 帧同一 anchorX（不做逐帧衣身/bbox 对齐）。</summary>
    private static float[] BuildFixedPivots(float pivotX)
    {
        var pivots = new float[FrameCount];
        for (int frame = 0; frame < pivots.Length; frame++) pivots[frame] = pivotX;
        return pivots;
    }

    /// <summary>槽位 4..7 → 样式下标 0..3；其它槽位返回 false。</summary>
    internal static bool TryGetStyle(int slot, out BankAssistantAtlasStyle style)
    {
        style = null;
        if (slot < 4 || slot >= 4 + Styles.Length) return false;
        style = Styles[slot - 4];
        return true;
    }

    /// <summary>帧号 → sprite 需用的 rect-local 归一化 pivot（固定 rig 原点：X = anchor.x，Y = 3px）。</summary>
    internal static Vector2 PivotOf(BankAssistantAtlasStyle style, int frame)
    {
        Rect rect = style.Rects[frame];
        if (rect.width <= 0f || rect.height <= 0f) return new Vector2(0.5f, 0f);
        return new Vector2(style.PivotXPixels[frame] / rect.width, style.PivotYPixels / rect.height);
    }

    /// <summary>authored 相位（秒）→ 该动作行内的 8 帧循环帧号（0..7）。
    /// 相位来自 native 未取 fract 的 normalizedTime × clipLength；对 authored 整条时长取模。
    /// 非有限/负值/0 → 首帧；相同相位必得同一帧（幂等）。</summary>
    internal static int FrameInAction(BankAssistantAtlasStyle style, BankAssistantAtlasAction action, float phaseSeconds)
    {
        int first = (int)action * FramesPerAction;
        float duration = style.DurationOf(action);
        if (!(duration > 0f)) return first;
        if (!float.IsFinite(phaseSeconds) || phaseSeconds < 0f) return first;
        if (phaseSeconds > MaxPhaseSeconds) phaseSeconds = MaxPhaseSeconds;
        float step = duration / FramesPerAction;
        int frame = (int)(phaseSeconds % duration / step);
        if (frame < 0) frame = 0;
        else if (frame >= FramesPerAction) frame = FramesPerAction - 1;
        return first + frame;
    }
}
