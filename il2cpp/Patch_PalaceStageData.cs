// ohmymods — palace visible nodes / stage+dancer 节点：纯 managed 数据表 + dance 时钟。
// 无 Prefix/hook、无 Unity 对象/Harmony/MonoBehaviour/Renderer/场景/RPC/存档/静态 owner；本文件不接线 Root。
// 数据出处（逐项显式抄录，不由 columns/格位推算）：
//   .local/tasks/ming-imperial-palace-20261002/world-expansion-20261006/delivery/world-expansion-r1-art-reviewed/
//     actors/native-import/manifest.json（court-dancer）、architecture/native-import/manifest.json（court-performance-stage-deck/front）
//   及 .local/tasks/palace-visible-nodes-20261006/receipts/stage-dancer-static-preparation-r1.md。
// 边界：本文件常量不是 sortOrder/遮挡/native 层批准（不带 deck -2、front null 等候选值）；朝向 front-3/4-left 属原图像素，
// 本数据不含 flip/镜像/旋转字段，消费方不得对原图做朝向 transform；本包尺寸口径为 PPU32，与旧 R6 LogicalPpu 无关。
using System;

namespace KingdomEnhancedMod;

/// <summary>原图 top-left 原点像素矩形（YTop 向下）；不做 Unity (x, height−topY−h) 换算。</summary>
internal readonly struct PixelRect
{
    internal readonly int X;
    internal readonly int YTop;
    internal readonly int W;
    internal readonly int H;

    internal PixelRect(int x, int yTop, int w, int h)
    {
        X = x; YTop = yTop; W = w; H = h;
    }
}

/// <summary>
/// 舞台（deck/front）+ 舞女 atlas 的只读数据表：26 条 FrameID→rect 显式查表（0..25 全保留，含 idle/bow 帧），
/// 本阶段只消费 dance（indices 4..18）。PPU32、cell 48、atlas 384×144。
/// </summary>
internal static class PalaceStageData
{
    // 三个原 PNG 的 resource relative 名（Root 以既有 "KingdomEnhancedMod.KEM_PalaceR6/" + relative 嵌入；本类不读文件）。
    internal const string DancerAtlasRelative = "court-dancer/atlas-8x.png";
    internal const string DeckRelative = "court-performance-stage/deck-back-logical.png";
    internal const string FrontRelative = "court-performance-stage/front-rail-logical.png";

    internal const int DancerAtlasWidth = 384;
    internal const int DancerAtlasHeight = 144;
    internal const int Cell = 48;
    internal const int Ppu = 32;

    // Unity normalized pivot（bottom-left 原点）；由 manifest 像素 pivot 换算。
    internal const float DancerPivotX = 24.5f / 48f; // 24.5/48 = 0.5104167
    internal const float DancerPivotY = 4f / 48f;    // 4/48    = 0.08333333
    internal const int DeckWidth = 144;
    internal const int DeckHeight = 88;
    internal const float DeckPivotX = 0.5f;      // 72/144
    internal const float DeckPivotY = 1f / 88f;  // top-left pivot [72,87] → 88−87=1
    internal const int FrontWidth = 144;
    internal const int FrontHeight = 14;
    internal const float FrontPivotX = 0.5f; // 72/144
    internal const float FrontPivotY = 0f;   // top-left pivot [72,14] → 14−14=0

    // 装饰锚：以 deck pivot 世界位 S（Unity y-up）为基准的向上偏移，单位 world。
    internal const float StageSurfaceYOffset = 22f / 32f; // (87−65)/32 = 0.6875：舞女脚线 = S.y + 22/32
    internal const float FrontPivotYOffset = 21f / 32f;   // 0.65625：front pivot = S.y + 21/32（比脚线低 1/32）

    /// <summary>FrameID → 原图 top-left rect（26 条显式表，相同值不合并；禁止 FrameID % Columns 推算）。</summary>
    internal static readonly PixelRect[] Frames =
    {
        /*00 idle-00*/ new PixelRect(0, 0, 48, 48),
        /*01 idle-01*/ new PixelRect(48, 0, 48, 48),
        /*02 idle-02*/ new PixelRect(96, 0, 48, 48),
        /*03 idle-03*/ new PixelRect(0, 0, 48, 48),
        /*04 dance-00*/ new PixelRect(0, 0, 48, 48),
        /*05 dance-01*/ new PixelRect(144, 0, 48, 48),
        /*06 dance-02*/ new PixelRect(192, 0, 48, 48),
        /*07 dance-03*/ new PixelRect(240, 0, 48, 48),
        /*08 dance-04*/ new PixelRect(288, 0, 48, 48),
        /*09 dance-05*/ new PixelRect(336, 0, 48, 48),
        /*10 dance-06*/ new PixelRect(0, 48, 48, 48),
        /*11 dance-07*/ new PixelRect(48, 48, 48, 48),
        /*12 dance-08*/ new PixelRect(96, 48, 48, 48),
        /*13 dance-09*/ new PixelRect(144, 48, 48, 48),
        /*14 dance-10*/ new PixelRect(192, 48, 48, 48),
        /*15 dance-11*/ new PixelRect(240, 48, 48, 48),
        /*16 dance-12*/ new PixelRect(288, 48, 48, 48),
        /*17 dance-13*/ new PixelRect(336, 48, 48, 48),
        /*18 dance-14*/ new PixelRect(0, 0, 48, 48),
        /*19 bow-00*/ new PixelRect(0, 0, 48, 48),
        /*20 bow-01*/ new PixelRect(0, 96, 48, 48),
        /*21 bow-02*/ new PixelRect(48, 96, 48, 48),
        /*22 bow-03*/ new PixelRect(96, 96, 48, 48),
        /*23 bow-04*/ new PixelRect(144, 96, 48, 48),
        /*24 bow-05*/ new PixelRect(192, 96, 48, 48),
        /*25 bow-06*/ new PixelRect(0, 0, 48, 48),
    };

    // dance clip：indices 与 durationsMs 逐项配对；本阶段不提供 idle/bow 的时长数据。
    internal static readonly int[] DanceFrameIds = { 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18 };
    internal static readonly int[] DanceDurationsMs = { 200, 240, 240, 240, 480, 260, 240, 520, 280, 280, 300, 440, 340, 380, 200 };
    internal const int DanceTotalMs = 4640; // = DanceDurationsMs 逐项和
}

/// <summary>
/// dance 循环时钟（纯 managed）：per-instance elapsedMs 累计 → 当前 FrameID（初始 4 = dance 首帧）。
/// 无静态 owner：由内容 owner 持有该实例；owner 创建全新实例即复位，owner Clear 后 Root 停止调用（无独立 cleanup 框架）。
/// </summary>
internal sealed class PalaceStageDanceClock
{
    private double _elapsedMs;
    private double _phaseMs;

    internal int CurrentFrameId { get; private set; } = PalaceStageData.DanceFrameIds[0];

    /// <summary>实例累计播放毫秒（仅 playing 且有限 seconds>0 时增长）。</summary>
    internal double ElapsedMs => _elapsedMs;

    /// <summary>
    /// playing 且 seconds 为有限正数：累加 seconds×1000 后返回新帧；其余（暂停、NaN、±Infinity、≤0）不推进、原样返回。
    /// 帧 = elapsed % 4640 落入的 duration 区间；大 dt 直接落当前 phase，不逐帧补跑、不触发额外动作。
    /// </summary>
    internal int Advance(float seconds, bool playing)
    {
        if (playing && seconds > 0f && !float.IsInfinity(seconds))
        {
            double incrementMs = seconds * 1000.0;
            _elapsedMs += incrementMs; // observation only; large historical totals never drive phase
            _phaseMs = (_phaseMs + incrementMs % PalaceStageData.DanceTotalMs) % PalaceStageData.DanceTotalMs;
            CurrentFrameId = FrameAtPhase(_phaseMs);
        }
        return CurrentFrameId;
    }

    private static int FrameAtPhase(double phaseMs)
    {
        double acc = 0.0;
        for (int i = 0; i < PalaceStageData.DanceDurationsMs.Length; i++)
        {
            acc += PalaceStageData.DanceDurationsMs[i];
            if (phaseMs < acc) return PalaceStageData.DanceFrameIds[i];
        }
        return PalaceStageData.DanceFrameIds[PalaceStageData.DanceFrameIds.Length - 1];
    }
}
