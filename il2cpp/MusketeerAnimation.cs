// 火铳手·自有动画状态（runtime slice，纯逻辑：无 Unity 依赖、无原生调用、无分配）。
//
// 权威数据 = artifacts/musketeer/20260916/atlas.json（用户已确认 v8 外观，66 帧 12x6、
// 单格 56x32、PPU 32、脚点 pivot (31,2)）。本文件把清单里的帧区间/时长/holds/锚点抄成
// 编译期常量（"copied approved keys, not missing anchors"：66 个 key 的 rearGrip/frontGrip/
// muzzle/torsoLift 全部在表里，测试逐项与清单值核对）。
//
// 时钟契约（清单 clocks 字段，逐字翻译）：
// * locomotion：phaseSeconds =（unwrapped normalizedTime × 原生 clip 长度）对 authored duration 取模；
//   **绝不先取 Fraction(normalizedTime)**（原生 clip 长度与世界覆盖不同，先取小数会把相位压错，
//   希腊 2.167s 拉弓教训同一类错误）。Walk=1.0s / Run=0.75s = authored gait period，不做整段归一。
//   动作真实进入/控制器替换时重置采样原点；非法采样（NaN/±Inf/无 clip 长度）→ 挂起自有渲染。
// * Idle：elapsed-native-stand-seconds；Idleness=0（原生 Stand 相位冻结）时不累计。
// * fire：确认真实射击时刻 t0 → 显示 Fire 0.25s（后坐含在 clip 内，不是单独动作），
//   出膛位置取「已举枪」的 Aim muzzle 像素 [48,14]，不用动画帧。
// * reload：t0+0.25 起；window = min(1.4, max(0, nextEligibleShotTime-(t0+0.25)))；
//   authoredSeconds = clamp((now-t0-0.25)/window,0,1)×1.4；window=0 直接跳过 Reload；
//   Reload 完成停在 Aim。动画绝不缩短或延长玩法冷却（冷却由 runtime 的显式时间闸决定）。
//
// 边界：移动（Walk/Run）打断举枪表现（清单 interruption："native lifecycle/visibility wins;
// movement interrupts gun presentation"）——Raise/Aim/Reload/Lower 一律让位，但真实射击的
// Fire（0.25s 确认事件）播完；Unknown 原生状态（Ghost Die/Spawn/其它世界状态名）挂起自有渲染，
// 归还原生，绝不把"身体显示不出来"当成身份丢失。
// Retreat 帧（58..65）已画但**未接线**（清单 clock 明说 reserved until verified behavior routing）。

using System;

namespace KingdomEnhancedMod;

/// <summary>已绘制的火铳手动作（atlas 帧区间，见 <see cref="MusketeerAtlas"/>）。</summary>
internal enum MusketeerAction
{
    Idle = 0,
    Walk = 1,
    Run = 2,
    Raise = 3,
    Aim = 4,
    Fire = 5,
    Reload = 6,
    Lower = 7,
    /// <summary>已画但未接线（清单：reserved；普通撤退先复用 locomotion）。</summary>
    Retreat = 8,
}

/// <summary>
/// 原生 Archer 当前状态按火铳手需要归的类：Stand→Idle、Walk→Walk、Run→Run、
/// Prepare/Shoot→Gun（原生射击协程的两段）、其余（Ghost Die / Spawn / 世界特有状态）→Unknown。
/// </summary>
internal enum MusketeerMotion
{
    Unknown = 0,
    Idle = 1,
    Walk = 2,
    Run = 3,
    Gun = 4,
}

/// <summary>单个动作在 atlas 中的帧区间 + authored 时长 + 逐帧 holds（清单 holds 数组）。</summary>
internal readonly struct MusketeerClip
{
    internal MusketeerClip(MusketeerAction action, int firstFrame, double[] holds, bool loops)
    {
        Action = action;
        FirstFrame = firstFrame;
        Holds = holds;
        Loops = loops;
    }

    internal MusketeerAction Action { get; }
    internal int FirstFrame { get; }

    /// <summary>逐 key 停留时长（秒）；帧数 = Holds.Length。</summary>
    internal double[] Holds { get; }

    internal bool Loops { get; }

    internal int FrameCount => Holds.Length;
    internal int LastFrame => FirstFrame + FrameCount - 1;

    /// <summary>authored 总时长 = holds 之和（清单 duration，测试核对）。</summary>
    internal double Duration
    {
        get
        {
            double sum = 0d;
            for (int i = 0; i < Holds.Length; i++) sum += Holds[i];
            return sum;
        }
    }
}

/// <summary>逐帧锚点（清单 key.anchors，整数顶左像素索引；grip=2x2 掌心顶左，muzzle=枪口末端像素）。</summary>
internal readonly struct MusketeerAnchor
{
    internal MusketeerAnchor(byte rearGripX, byte rearGripY, byte frontGripX, byte frontGripY,
        byte muzzleX, byte muzzleY, byte torsoLift)
    {
        RearGripX = rearGripX;
        RearGripY = rearGripY;
        FrontGripX = frontGripX;
        FrontGripY = frontGripY;
        MuzzleX = muzzleX;
        MuzzleY = muzzleY;
        TorsoLift = torsoLift;
    }

    internal byte RearGripX { get; }
    internal byte RearGripY { get; }
    internal byte FrontGripX { get; }
    internal byte FrontGripY { get; }
    internal byte MuzzleX { get; }
    internal byte MuzzleY { get; }
    internal byte TorsoLift { get; }
}

/// <summary>atlas 布局与时钟常量（artifacts/musketeer/20260916/atlas.json，v8 定稿）。</summary>
internal static class MusketeerAtlas
{
    internal const int Columns = 12;
    internal const int Rows = 6;
    internal const int FrameCount = 66;
    internal const int CellWidth = 56;
    internal const int CellHeight = 32;
    internal const float PixelsPerUnit = 32f;
    internal const float PivotPixelX = 31f;
    internal const float PivotPixelY = 2f;

    internal const int IdleFirstFrame = 0;
    internal const int WalkFirstFrame = 12;
    internal const int RunFirstFrame = 20;
    internal const int RaiseFirstFrame = 28;
    internal const int AimFirstFrame = 34;
    internal const int FireFirstFrame = 36;
    internal const int ReloadFirstFrame = 40;
    internal const int LowerFirstFrame = 52;
    internal const int RetreatFirstFrame = 58;

    /// <summary>已举枪枪口像素（Aim key [48,14]）：出膛原点来源，绝不使用动画帧或原生 2.5 偏移。</summary>
    internal const int PreparedMuzzlePixelX = 48;
    internal const int PreparedMuzzlePixelY = 14;

    /// <summary>以真实游戏秒为单位的枪械动作时长（清单 duration）。</summary>
    internal const double FireSeconds = 0.25;
    internal const double RaiseSeconds = 0.33;
    internal const double LowerSeconds = 0.39;
    internal const double ReloadSeconds = 1.4;

    /// <summary>相位秒数上限：避免巨大时间换算溢出（与时钟无关的防御）。</summary>
    internal const double MaxPhaseSeconds = 1000000d;

    /// <summary>帧步进的 ULP 吸附（单位=帧）：hold 边界上的 1e-13 级误差不改变显示帧。</summary>
    internal const double FrameEpsilon = 1e-9d;

    private static readonly double[] IdleHolds = { 1.2, 0.3, 0.18, 0.35, 0.2, 0.25, 0.2, 0.3, 0.18, 0.35, 0.2, 1.1 };
    private static readonly double[] WalkHolds = { 0.125, 0.125, 0.125, 0.125, 0.125, 0.125, 0.125, 0.125 };
    private static readonly double[] RunHolds = { 0.09375, 0.09375, 0.09375, 0.09375, 0.09375, 0.09375, 0.09375, 0.09375 };
    private static readonly double[] RaiseHolds = { 0.055, 0.055, 0.055, 0.055, 0.055, 0.055 };
    private static readonly double[] AimHolds = { 0.15, 0.15 };
    private static readonly double[] FireHolds = { 0.045, 0.055, 0.07, 0.08 };
    private static readonly double[] ReloadHolds = { 0.1, 0.1, 0.12, 0.13, 0.13, 0.14, 0.13, 0.13, 0.12, 0.1, 0.1, 0.1 };
    private static readonly double[] LowerHolds = { 0.065, 0.065, 0.065, 0.065, 0.065, 0.065 };
    private static readonly double[] RetreatHolds = { 0.105, 0.105, 0.105, 0.105, 0.105, 0.105, 0.105, 0.105 };

    private static readonly MusketeerClip IdleClip = new MusketeerClip(MusketeerAction.Idle, IdleFirstFrame, IdleHolds, true);
    private static readonly MusketeerClip WalkClip = new MusketeerClip(MusketeerAction.Walk, WalkFirstFrame, WalkHolds, true);
    private static readonly MusketeerClip RunClip = new MusketeerClip(MusketeerAction.Run, RunFirstFrame, RunHolds, true);
    private static readonly MusketeerClip RaiseClip = new MusketeerClip(MusketeerAction.Raise, RaiseFirstFrame, RaiseHolds, false);
    private static readonly MusketeerClip AimClip = new MusketeerClip(MusketeerAction.Aim, AimFirstFrame, AimHolds, false);
    private static readonly MusketeerClip FireClip = new MusketeerClip(MusketeerAction.Fire, FireFirstFrame, FireHolds, false);
    private static readonly MusketeerClip ReloadClip = new MusketeerClip(MusketeerAction.Reload, ReloadFirstFrame, ReloadHolds, false);
    private static readonly MusketeerClip LowerClip = new MusketeerClip(MusketeerAction.Lower, LowerFirstFrame, LowerHolds, false);
    private static readonly MusketeerClip RetreatClip = new MusketeerClip(MusketeerAction.Retreat, RetreatFirstFrame, RetreatHolds, true);

    /// <summary>
    /// 66 个 key 的锚点（清单逐帧值，按帧号 0..65 升序）。索引即 atlas 帧号。
    /// 运行时只用到 [48,14]（已举枪枪口）与 torsoLift；其余是契约完整性（测试核对清单值）。
    /// </summary>
    internal static readonly MusketeerAnchor[] Anchors =
    {
        // Idle 0..11
        new MusketeerAnchor(29, 20, 33, 18, 41, 8, 0),
        new MusketeerAnchor(29, 20, 33, 18, 41, 8, 0),
        new MusketeerAnchor(29, 20, 33, 18, 41, 8, 0),
        new MusketeerAnchor(29, 20, 33, 18, 41, 8, 0),
        new MusketeerAnchor(29, 19, 33, 17, 41, 7, 1),
        new MusketeerAnchor(29, 19, 33, 17, 41, 7, 1),
        new MusketeerAnchor(29, 19, 33, 17, 41, 7, 1),
        new MusketeerAnchor(29, 20, 33, 18, 41, 8, 0),
        new MusketeerAnchor(28, 20, 32, 18, 40, 8, 0),
        new MusketeerAnchor(28, 20, 32, 18, 40, 8, 0),
        new MusketeerAnchor(29, 20, 33, 18, 41, 8, 0),
        new MusketeerAnchor(29, 20, 33, 18, 41, 8, 0),
        // Walk 12..19
        new MusketeerAnchor(29, 20, 33, 18, 41, 8, 0),
        new MusketeerAnchor(29, 20, 33, 18, 41, 8, 0),
        new MusketeerAnchor(30, 19, 34, 17, 42, 7, 1),
        new MusketeerAnchor(29, 20, 33, 18, 41, 8, 0),
        new MusketeerAnchor(29, 20, 33, 18, 41, 8, 0),
        new MusketeerAnchor(29, 20, 33, 18, 41, 8, 0),
        new MusketeerAnchor(28, 19, 32, 17, 40, 7, 1),
        new MusketeerAnchor(29, 20, 33, 18, 41, 8, 0),
        // Run 20..27
        new MusketeerAnchor(30, 20, 34, 18, 42, 8, 0),
        new MusketeerAnchor(31, 19, 35, 17, 43, 7, 1),
        new MusketeerAnchor(31, 18, 35, 16, 43, 6, 2),
        new MusketeerAnchor(30, 20, 34, 18, 42, 8, 0),
        new MusketeerAnchor(30, 20, 34, 18, 42, 8, 0),
        new MusketeerAnchor(29, 19, 33, 17, 41, 7, 1),
        new MusketeerAnchor(29, 18, 33, 16, 41, 6, 2),
        new MusketeerAnchor(30, 20, 34, 18, 42, 8, 0),
        // Raise 28..33
        new MusketeerAnchor(29, 20, 33, 18, 41, 8, 0),
        new MusketeerAnchor(29, 19, 34, 17, 43, 9, 0),
        new MusketeerAnchor(29, 19, 35, 17, 44, 10, 0),
        new MusketeerAnchor(30, 18, 35, 16, 46, 11, 0),
        new MusketeerAnchor(30, 18, 36, 16, 47, 12, 0),
        new MusketeerAnchor(30, 17, 37, 16, 48, 14, 0),
        // Aim 34..35
        new MusketeerAnchor(30, 17, 37, 16, 48, 14, 0),
        new MusketeerAnchor(30, 17, 37, 16, 48, 14, 0),
        // Fire 36..39
        new MusketeerAnchor(29, 17, 36, 16, 47, 14, 0),
        new MusketeerAnchor(29, 17, 36, 16, 47, 14, 0),
        new MusketeerAnchor(30, 17, 37, 16, 48, 14, 0),
        new MusketeerAnchor(30, 17, 37, 16, 48, 14, 0),
        // Reload 40..51
        new MusketeerAnchor(30, 17, 37, 16, 48, 14, 0),
        new MusketeerAnchor(30, 18, 36, 16, 46, 12, 0),
        new MusketeerAnchor(29, 19, 35, 17, 45, 10, 0),
        new MusketeerAnchor(30, 20, 35, 17, 44, 10, 0),
        new MusketeerAnchor(28, 21, 35, 17, 44, 10, 0),
        new MusketeerAnchor(27, 22, 35, 17, 44, 10, 0),
        new MusketeerAnchor(29, 21, 35, 17, 44, 10, 0),
        new MusketeerAnchor(31, 20, 35, 17, 44, 10, 0),
        new MusketeerAnchor(32, 18, 35, 17, 44, 10, 0),
        new MusketeerAnchor(30, 18, 36, 16, 46, 11, 0),
        new MusketeerAnchor(30, 17, 36, 16, 47, 13, 0),
        new MusketeerAnchor(30, 17, 37, 16, 48, 14, 0),
        // Lower 52..57
        new MusketeerAnchor(30, 17, 37, 16, 48, 14, 0),
        new MusketeerAnchor(30, 18, 36, 16, 47, 12, 0),
        new MusketeerAnchor(30, 18, 35, 16, 46, 11, 0),
        new MusketeerAnchor(29, 19, 35, 17, 44, 10, 0),
        new MusketeerAnchor(29, 19, 34, 17, 43, 9, 0),
        new MusketeerAnchor(29, 20, 33, 18, 41, 8, 0),
        // Retreat 58..65（未接线）
        new MusketeerAnchor(28, 20, 32, 18, 40, 8, 0),
        new MusketeerAnchor(28, 20, 32, 18, 40, 8, 0),
        new MusketeerAnchor(29, 19, 33, 17, 41, 7, 1),
        new MusketeerAnchor(28, 20, 32, 18, 40, 8, 0),
        new MusketeerAnchor(28, 20, 32, 18, 40, 8, 0),
        new MusketeerAnchor(28, 20, 32, 18, 40, 8, 0),
        new MusketeerAnchor(27, 19, 31, 17, 39, 7, 1),
        new MusketeerAnchor(28, 20, 32, 18, 40, 8, 0),
    };

    internal static bool TryGetClip(MusketeerAction action, out MusketeerClip clip)
    {
        switch (action)
        {
            case MusketeerAction.Idle: clip = IdleClip; return true;
            case MusketeerAction.Walk: clip = WalkClip; return true;
            case MusketeerAction.Run: clip = RunClip; return true;
            case MusketeerAction.Raise: clip = RaiseClip; return true;
            case MusketeerAction.Aim: clip = AimClip; return true;
            case MusketeerAction.Fire: clip = FireClip; return true;
            case MusketeerAction.Reload: clip = ReloadClip; return true;
            case MusketeerAction.Lower: clip = LowerClip; return true;
            case MusketeerAction.Retreat: clip = RetreatClip; return true;
            default: clip = default; return false;
        }
    }

    /// <summary>帧号 → 网格坐标（row-major：左上角起、行内自左向右、逐行向下）。越界返回 false。</summary>
    internal static bool FrameToCell(int frame, out int column, out int row)
    {
        column = 0;
        row = 0;
        if (frame < 0 || frame >= FrameCount) return false;
        column = frame % Columns;
        row = frame / Columns;
        return true;
    }

    /// <summary>
    /// 该动作在给定相位秒数下的显示帧（逐帧 holds；循环动作取模、非循环停在末帧）。
    /// 非有限/负相位 → 首帧；越界一律钳制，绝不返回区间外的帧号。
    /// </summary>
    internal static int FrameAt(in MusketeerClip clip, double phaseSeconds)
    {
        if (clip.FrameCount <= 0) return clip.FirstFrame;
        if (double.IsNaN(phaseSeconds)) return clip.FirstFrame;
        if (phaseSeconds > MaxPhaseSeconds) phaseSeconds = MaxPhaseSeconds;
        if (!(phaseSeconds > 0d)) return clip.FirstFrame;

        if (clip.Loops)
        {
            double duration = clip.Duration;
            if (duration > 0d && double.IsFinite(duration))
            {
                phaseSeconds %= duration;
                if (duration - phaseSeconds <= FrameEpsilon) phaseSeconds = 0d;
            }
        }

        double elapsed = 0d;
        for (int i = 0; i < clip.Holds.Length; i++)
        {
            elapsed += clip.Holds[i];
            if (phaseSeconds < elapsed - FrameEpsilon) return clip.FirstFrame + i;
        }
        return clip.LastFrame;
    }

    /// <summary>
    /// locomotion 相位：`(unwrapped normalizedTime × clipLength) mod authored duration`。
    /// 绝不先取 Fraction(normalizedTime)：原生 clip 长度≠authored 时长（世界覆盖会变），
    /// 先取小数会把相位压进错误区间。非法输入（非有限/负值/无 clip 长度）返回 false = 挂起。
    /// </summary>
    internal static bool TryLocomotionPhaseSeconds(double normalizedTime, double clipLength, double authoredDuration,
        out double phaseSeconds)
    {
        phaseSeconds = 0d;
        if (!double.IsFinite(normalizedTime) || !double.IsFinite(clipLength)) return false;
        if (!(clipLength > 0d) || !(authoredDuration > 0d)) return false;
        double seconds = normalizedTime * clipLength;
        if (!double.IsFinite(seconds) || seconds < 0d) return false;
        if (seconds > MaxPhaseSeconds) seconds = MaxPhaseSeconds;
        phaseSeconds = seconds % authoredDuration;
        return true;
    }

    /// <summary>
    /// 锚点像素 → 角色本地坐标（清单 anchorCoordinates：local=((x+0.5-31)/32,(32-y-0.5-2)/32)）。
    /// 朝向/翻面由调用方用角色 transform 的正负 x 缩放处理（TransformPoint 自带符号）。
    /// </summary>
    internal static void AnchorToLocal(int pixelX, int pixelY, out float localX, out float localY)
    {
        localX = (pixelX + 0.5f - PivotPixelX) / PixelsPerUnit;
        localY = (CellHeight - pixelY - 0.5f - PivotPixelY) / PixelsPerUnit;
    }

    /// <summary>已举枪枪口本地坐标（Aim key [48,14]）。</summary>
    internal static void PreparedMuzzleLocal(out float localX, out float localY)
        => AnchorToLocal(PreparedMuzzlePixelX, PreparedMuzzlePixelY, out localX, out localY);
}

/// <summary>
/// 火铳手自有动画状态机（显式时钟 + 事件驱动；无 Unity 依赖、无分配）。
///
/// 输入分三类（全部来自 runtime 已核观测点，本文件绝不自己重设动作）：
/// * <see cref="SetMotion"/>：原生 Animator 当前状态的归类 + normalizedTime + clip 长度 + 帧间隔；
/// * <see cref="SetAiming"/>：runtime 的"处于举枪姿态"（有合法地面目标或原生 Prepare/Shoot 状态）；
/// * <see cref="NotifyShot"/>：**确认真实射击**（我们的子弹实际出膛的那一帧）+ 下一次可射时间。
///
/// 输出只有一件事：此刻应显示 atlas 的哪一帧（<see cref="Tick"/> 返回 -1 = 挂起，调用方归还原生）。
/// 时钟单调（拒绝 NaN/±Inf/负值/回退），暂停（dt=0、相位冻结）时整机冻结。
/// </summary>
internal sealed class MusketeerAnimationState
{
    private enum GunPhase
    {
        None = 0,
        Raise = 1,
        Aim = 2,
        Fire = 3,
        Reload = 4,
        Lower = 5,
    }

    private bool _enabled = true;
    private bool _hasSample;
    private double _lastNow;
    private int _frame = -1;
    private bool _suspended = true;

    private MusketeerMotion _motion = MusketeerMotion.Unknown;
    private bool _motionValid;
    private double _motionNormalizedTime;
    private double _motionClipLength;
    private double _standSeconds;
    private double _lastStandNormalizedTime = double.NaN;

    private GunPhase _gun = GunPhase.None;
    private double _gunStart;
    private bool _aiming;
    private double _shotTime = double.NaN;
    private double _nextEligibleShotTime = double.NaN;
    private double _reloadStart;
    private double _reloadWindow;

    private MusketeerAction _action = MusketeerAction.Idle;

    /// <summary>关闭后 Tick 恒返回 -1（调用方归还原生渲染），事件全部忽略。</summary>
    internal bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            if (!_enabled) Suspend();
        }
    }

    /// <summary>是否挂起自有渲染（-1）。调用方据此归还原生 renderer。</summary>
    internal bool Suspended => _suspended;

    /// <summary>当前动作（诊断/测试；挂起时为最后有效动作）。</summary>
    internal MusketeerAction CurrentAction => _action;

    /// <summary>当前显示帧（-1 = 挂起）。</summary>
    internal int CurrentFrame => _frame;

    /// <summary>新生命/池复用/换世界：清空全部相位与采样原点（不改变 Enabled）。</summary>
    internal void Reset() => Suspend();

    /// <summary>
    /// 原生 Animator 当前状态归类 + 采样（每帧一次，必须在原生 Animator 产出本帧结果之后）。
    /// 同一动作重复调用只推进采样；动作变化 = 真实进入 → 重置采样原点（清单 clocks：
    /// "True action entry/controller replacement reset sampling origin"）。
    /// </summary>
    internal void SetMotion(MusketeerMotion motion, double now, double normalizedTime, double clipLength, double deltaSeconds)
    {
        if (!TryBeginTimeStep(now)) return;

        bool entered = motion != _motion;
        _motion = motion;
        _motionNormalizedTime = normalizedTime;
        _motionClipLength = clipLength;
        // Idle 时钟用累计站立秒数（不需要 clip 长度）；Walk/Run 的相位必须能解析
        // "unwrapped nt × clip 长度"，缺 clip 长度 = 非法采样 → 挂起归还原生。
        _motionValid = motion != MusketeerMotion.Unknown
            && double.IsFinite(normalizedTime)
            && (motion != MusketeerMotion.Walk && motion != MusketeerMotion.Run
                || double.IsFinite(clipLength) && clipLength > 0d);

        if (motion == MusketeerMotion.Unknown)
        {
            // 原生生命周期/未知状态优先：挂起自有表现，清掉在途枪械表现（回来时重新举枪）。
            _gun = GunPhase.None;
            _standSeconds = 0d;
            _lastStandNormalizedTime = double.NaN;
            return;
        }

        if (entered)
        {
            _standSeconds = 0d;
            _lastStandNormalizedTime = normalizedTime;
            CancelGunForMovement(motion);
            return;
        }

        if (motion == MusketeerMotion.Idle)
        {
            // elapsed-native-stand-seconds：只在原生 Stand 相位确实推进时累计（Idleness=0 → 冻结）。
            bool advanced = !double.IsNaN(_lastStandNormalizedTime) && normalizedTime != _lastStandNormalizedTime;
            if (advanced && deltaSeconds > 0d && double.IsFinite(deltaSeconds)) _standSeconds += deltaSeconds;
            _lastStandNormalizedTime = normalizedTime;
        }
    }

    /// <summary>
    /// 举枪姿态输入（runtime 回答：本帧有合法地面目标，或原生正处于 Prepare/Shoot）。
    /// true：无枪械表现时开 Raise（已有 → 不动）；false：Raise/Aim/Reload → Lower（Fire 播完再让位）。
    /// </summary>
    internal void SetAiming(bool aiming, double now)
    {
        if (!TryBeginTimeStep(now)) return;
        if (_aiming == aiming) return;
        _aiming = aiming;
        if (aiming)
        {
            // 移动中（Walk/Run）绝不进入举枪姿态：目标在移动途中被选中也不冻结成站立姿势。
            if ((_gun == GunPhase.None || _gun == GunPhase.Lower) && !IsLocomotion(_motion))
            {
                _gun = GunPhase.Raise;
                _gunStart = now;
            }
        }
        else if (_gun == GunPhase.Raise || _gun == GunPhase.Aim || _gun == GunPhase.Reload)
        {
            _gun = GunPhase.Lower;
            _gunStart = now;
        }
    }

    /// <summary>
    /// 确认真实射击（子弹已出膛；视觉事件绝不造成伤害）。锚定 Fire 0.25s，并携带
    /// runtime 的下一次可射时间（Reload 窗口 = 两者之差，动画绝不改写玩法时间）。
    /// </summary>
    internal void NotifyShot(double now, double nextEligibleShotTime)
    {
        if (!TryBeginTimeStep(now)) return;
        _shotTime = now;
        _nextEligibleShotTime = nextEligibleShotTime;
        _aiming = true;
        _gun = GunPhase.Fire;
        _gunStart = now;
    }

    /// <summary>
    /// 当前应显示的 atlas 帧；-1 = 挂起（关闭/未知原生状态/非法采样），调用方必须归还原生渲染。
    /// 对同一时刻可重复调用（幂等）；时间回退/非法时保持上一结果。
    /// </summary>
    internal int Tick(double now)
    {
        if (!_enabled)
        {
            Suspend();
            return -1;
        }
        if (!TryBeginTimeStep(now)) return _frame;

        AdvanceGun(now);
        if (!_motionValid || _motion == MusketeerMotion.Unknown)
        {
            // 未知/非法原生采样（含 NaN/无 clip 长度）**优先于一切枪械表现（含 Fire）**：
            // 不猜动作、归还原生显现；枪械相位保留给下一次有效采样。
            _suspended = true;
            _frame = -1;
            return -1;
        }
        if (_aiming && _gun == GunPhase.None && !IsLocomotion(_motion))
        {
            // 移动打断后重新站定：有合法目标/原生射击状态 → 重新起举枪（幂等：只在无枪械表现时）。
            _gun = GunPhase.Raise;
            _gunStart = now;
        }

        _frame = ResolveFrame(now, out _action);
        _suspended = _frame < 0;
        return _frame;
    }

    // ============================================================
    // 私有：时间闸 / 相位推进 / 帧解析
    // ============================================================

    /// <summary>
    /// 唯一时间入口：拒绝禁用/NaN/±Inf/负值/回退（返回 false，调用方保持现状），
    /// 否则把单调时钟推进到 now。事件与 Tick 共用它，保证"事件先到"与"Tick 先到"同解。
    /// </summary>
    private bool TryBeginTimeStep(double now)
    {
        if (!_enabled) return false;
        if (!IsValidTime(now)) return false;
        if (_hasSample && now < _lastNow) return false;
        _hasSample = true;
        _lastNow = now;
        return true;
    }

    private void AdvanceGun(double now)
    {
        for (int i = 0; i < 4; i++)
        {
            switch (_gun)
            {
                case GunPhase.Fire:
                    if (now - _shotTime < MusketeerAtlas.FireSeconds) return;
                    if (!_aiming)
                    {
                        _gun = GunPhase.Lower;
                        _gunStart = _shotTime + MusketeerAtlas.FireSeconds;
                        break;
                    }
                    if (_motion == MusketeerMotion.Walk || _motion == MusketeerMotion.Run)
                    {
                        _gun = GunPhase.None;   // 移动打断装填表现（Fire 已播完）
                        return;
                    }
                    double window = Math.Min(MusketeerAtlas.ReloadSeconds,
                        Math.Max(0d, _nextEligibleShotTime - (_shotTime + MusketeerAtlas.FireSeconds)));
                    if (double.IsNaN(window) || !(window > 0d))
                    {
                        _gun = GunPhase.Aim;   // window=0：跳过 Reload，直接停在 Aim
                        return;
                    }
                    _gun = GunPhase.Reload;
                    _reloadStart = _shotTime + MusketeerAtlas.FireSeconds;
                    _reloadWindow = window;
                    break;
                case GunPhase.Reload:
                    if (now - _reloadStart < _reloadWindow) return;
                    _gun = GunPhase.Aim;       // 装填完成停在 Aim（清单）
                    return;
                case GunPhase.Raise:
                    if (now - _gunStart < MusketeerAtlas.RaiseSeconds) return;
                    _gun = GunPhase.Aim;
                    return;
                case GunPhase.Lower:
                    if (now - _gunStart < MusketeerAtlas.LowerSeconds) return;
                    _gun = GunPhase.None;
                    return;
                default:
                    return;
            }
        }
    }

    private int ResolveFrame(double now, out MusketeerAction action)
    {
        if (IsLocomotion(_motion) && _gun != GunPhase.Fire)
        {
            // 移动优先于一切非 Fire 枪械表现（清单 interruption "movement interrupts gun presentation"）：
            // 无论相位是 Raise/Aim/Reload/Lower，走动中一律显示 locomotion；只有确认真实射击的
            // 0.25s Fire 例外。这里顺手清相位（幂等），避免走停反复时残留半个举枪序列。
            _gun = GunPhase.None;
            return LocomotionFrame(out action);
        }

        switch (_gun)
        {
            case GunPhase.Fire:
                action = MusketeerAction.Fire;
                MusketeerAtlas.TryGetClip(action, out MusketeerClip fire);
                return MusketeerAtlas.FrameAt(fire, now - _shotTime);

            case GunPhase.Reload:
                action = MusketeerAction.Reload;
                MusketeerAtlas.TryGetClip(action, out MusketeerClip reload);
                double progress = _reloadWindow > 0d ? (now - _reloadStart) / _reloadWindow : 1d;
                if (!(progress > 0d)) progress = 0d;
                else if (progress > 1d) progress = 1d;
                return MusketeerAtlas.FrameAt(reload, progress * MusketeerAtlas.ReloadSeconds);

            case GunPhase.Raise:
                action = MusketeerAction.Raise;
                MusketeerAtlas.TryGetClip(action, out MusketeerClip raise);
                return MusketeerAtlas.FrameAt(raise, now - _gunStart);

            case GunPhase.Lower:
                action = MusketeerAction.Lower;
                MusketeerAtlas.TryGetClip(action, out MusketeerClip lower);
                return MusketeerAtlas.FrameAt(lower, now - _gunStart);

            case GunPhase.Aim:
                action = MusketeerAction.Aim;
                return MusketeerAtlas.AimFirstFrame + 1;   // 末帧停住（lastFrameHold）
        }

        if (_motion == MusketeerMotion.Gun)
        {
            // 原生射击协程的两段（Prepare/Shoot）而没有我们的枪械相位（例如无目标的开火尝试）：
            // 显示"已举枪"稳定帧，绝不落到未知。
            action = MusketeerAction.Aim;
            return MusketeerAtlas.AimFirstFrame + 1;
        }

        return LocomotionFrame(out action);
    }

    /// <summary>locomotion（Idle/Walk/Run）帧解析；非法/未知一律 -1（挂起）。</summary>
    private int LocomotionFrame(out MusketeerAction action)
    {
        if (_motion == MusketeerMotion.Idle)
        {
            action = MusketeerAction.Idle;
            MusketeerAtlas.TryGetClip(action, out MusketeerClip idle);
            return MusketeerAtlas.FrameAt(idle, _standSeconds);
        }

        if (_motion == MusketeerMotion.Walk || _motion == MusketeerMotion.Run)
        {
            action = _motion == MusketeerMotion.Walk ? MusketeerAction.Walk : MusketeerAction.Run;
            MusketeerAtlas.TryGetClip(action, out MusketeerClip clip);
            if (!MusketeerAtlas.TryLocomotionPhaseSeconds(_motionNormalizedTime, _motionClipLength,
                    clip.Duration, out double phase))
                return -1;   // 非法采样：挂起，归还原生
            return MusketeerAtlas.FrameAt(clip, phase);
        }

        action = MusketeerAction.Idle;
        return -1;
    }

    private static bool IsLocomotion(MusketeerMotion motion)
        => motion == MusketeerMotion.Walk || motion == MusketeerMotion.Run;

    private void CancelGunForMovement(MusketeerMotion motion)
    {
        if (motion != MusketeerMotion.Walk && motion != MusketeerMotion.Run) return;
        if (_gun == GunPhase.Raise || _gun == GunPhase.Aim || _gun == GunPhase.Reload || _gun == GunPhase.Lower)
            _gun = GunPhase.None;   // Fire（确认真实射击的 0.25s）不打断
    }

    private void Suspend()
    {
        _hasSample = false;
        _lastNow = 0d;
        _frame = -1;
        _suspended = true;
        _motion = MusketeerMotion.Unknown;
        _motionValid = false;
        _motionNormalizedTime = 0d;
        _motionClipLength = 0d;
        _standSeconds = 0d;
        _lastStandNormalizedTime = double.NaN;
        _gun = GunPhase.None;
        _gunStart = 0d;
        _aiming = false;
        _shotTime = double.NaN;
        _nextEligibleShotTime = double.NaN;
        _reloadStart = 0d;
        _reloadWindow = 0d;
        _action = MusketeerAction.Idle;
    }

    private static bool IsValidTime(double now)
        => !double.IsNaN(now) && !double.IsInfinity(now) && now >= 0d;
}
