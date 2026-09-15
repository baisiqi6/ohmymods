using System;

namespace KingdomEnhancedMod;

/// <summary>
/// 英雄弓手自有动画状态：Idle / Walk / Run / Draw / Release / Recovery。
/// 与原生 Archer 的 Animator + AnimationSync **并行**：本文件只回答「此刻该显示 4x4 atlas 的哪一帧」，
/// 不写原生 Animator/AnimationSync、不改位置/速度/攻击协程、不生成箭、不碰池、不涉及网络与存档。
///
/// 契约（调用方负责，本文件不实现）：
/// * 时钟：显式传入的秒级时间（GameMod 侧用 Time.time 或自有时钟），必须单调不回退；
///   场景重载/时间源重置（如新场景 Time.time 归零）后调用 <see cref="HeroArcherAnimationState.Reset"/>。
/// * 事件（全部来自原生已核实的观测点，绝不自己每帧重置动作）：
///   拉弓开始   = 原生 Archer Shoot 协程写 Prepare=true 的那一帧
///   放箭       = 原生 Archer.FireArrow → SetAndSendAnimationTrigger(Shoot/ShootPerfect)
///   拉弓结束   = 原生写 Prepare=false（协程收尾/编队/撤退分支）
///   移动状态   = Mover 是否在移动/是否跑速（Idle/Walk/Run 由调用方判定后喂进来）
///   OnEnable/池复用 = Reset()；SetHideStatus/petrify 期间是否继续显示由调用方决定
/// * 原生协程在齐射中会在多箭之间保持 Prepare=true，因此 <see cref="HeroArcherAnimationState.NotifyRelease"/>
///   结束后若拉弓仍未结束，本状态机会回到「拉弓末帧」而不是 Idle —— 与原生语义一致。
/// * 禁用 / NaN / ±Inf / 负时间 / 回退时间：不抛异常，保持上一有效帧，绝不写坏状态机。
/// * 纯逻辑不含任何像素尺寸、PPU、pivot、画布大小假设：只产出 atlas 帧序号（row-major，索引升序）。
/// </summary>
internal enum HeroArcherAnimation
{
    Idle = 0,
    Walk = 1,
    Run = 2,
    Draw = 3,
    Release = 4,
    Recovery = 5,
}

/// <summary>单个动作在 atlas 中的帧区间 + 固定帧率 + 是否循环（不可变）。</summary>
internal readonly struct HeroArcherClip
{
    internal HeroArcherClip(HeroArcherAnimation animation, int firstFrame, int frameCount, double fps, bool loops)
    {
        Animation = animation;
        FirstFrame = firstFrame;
        FrameCount = frameCount;
        Fps = fps;
        Loops = loops;
    }

    internal HeroArcherAnimation Animation { get; }

    /// <summary>该动作首帧的 atlas 索引。</summary>
    internal int FirstFrame { get; }

    /// <summary>该动作占用的 atlas 帧数（&gt;= 1）。</summary>
    internal int FrameCount { get; }

    /// <summary>固定帧率（帧/秒），只用于把经过时间换算成帧步进。</summary>
    internal double Fps { get; }

    /// <summary>true = 循环（Idle/Walk/Run）；false = 播到末帧后停在末帧（Draw/Release/Recovery）。</summary>
    internal bool Loops { get; }

    internal int LastFrame => FirstFrame + FrameCount - 1;
}

/// <summary>
/// 4x4=16 帧 atlas 的动作布局与固定时间常量（计划表，与 Operator 最终像素尺寸/脚锚点无关）。
/// 索引约定：row-major，从 atlas 左上角起，行内自左向右、逐行向下；frame = row * Columns + column。
/// 帧区间：0..3 idle，4..7 walk，8..11 run，12..13 draw，14 release，15 recovery。
/// </summary>
internal static class HeroArcherAtlas
{
    internal const int Columns = 4;
    internal const int Rows = 4;
    internal const int FrameCount = Columns * Rows;

    internal const int IdleFirstFrame = 0;
    internal const int WalkFirstFrame = 4;
    internal const int RunFirstFrame = 8;
    internal const int DrawFirstFrame = 12;
    internal const int ReleaseFrame = 14;
    internal const int RecoveryFrame = 15;

    internal const int IdleFrames = 4;
    internal const int WalkFrames = 4;
    internal const int RunFrames = 4;
    internal const int DrawFrames = 2;

    /// <summary>idle/walk 6 fps、run 8 fps（= 参考基线 base_male clip 的 sampleRate）；纯显示节奏，不影响原生逻辑。</summary>
    internal const double IdleFps = 6d;
    internal const double WalkFps = 6d;
    internal const double RunFps = 8d;
    internal const double DrawFps = 8d;

    /// <summary>放箭帧 14 的明确停留时长（1 帧 @8fps）；到期自动进入 recovery。</summary>
    internal const double ReleaseSeconds = 0.125d;

    /// <summary>收势帧 15 的明确停留时长；到期回到拉弓末帧或移动循环。</summary>
    internal const double RecoverySeconds = 0.25d;

    /// <summary>相位秒数上限：把 double*fps 压回 long 安全区，杜绝大时间导致的换算溢出。</summary>
    internal const double MaxClipPhaseSeconds = 1000000d;

    /// <summary>
    /// 帧步进的 ULP 补偿（单位＝帧，不是秒）：调用方常以「100+t 再减 100」的形式给出相位，
    /// 6fps 边界上会丢 1e-13 量级的 ULP（足以把 1/6 秒的 floor 推低一帧），这里把边界吸附到正确帧。
    /// 1e-9 帧在 6 fps 下 &lt; 2e-10 秒，远小于任何可观察显示粒度：换帧绝不会提前到
    /// 「边界前 1e-6 秒」这种量级（有测试钉住）。
    /// </summary>
    internal const double FrameEpsilon = 1e-9d;

    internal static bool IsMotion(HeroArcherAnimation animation)
        => animation == HeroArcherAnimation.Idle
        || animation == HeroArcherAnimation.Walk
        || animation == HeroArcherAnimation.Run;

    internal static bool TryGetClip(HeroArcherAnimation animation, out HeroArcherClip clip)
    {
        switch (animation)
        {
            case HeroArcherAnimation.Idle:
                clip = new HeroArcherClip(animation, IdleFirstFrame, IdleFrames, IdleFps, true);
                return true;
            case HeroArcherAnimation.Walk:
                clip = new HeroArcherClip(animation, WalkFirstFrame, WalkFrames, WalkFps, true);
                return true;
            case HeroArcherAnimation.Run:
                clip = new HeroArcherClip(animation, RunFirstFrame, RunFrames, RunFps, true);
                return true;
            case HeroArcherAnimation.Draw:
                clip = new HeroArcherClip(animation, DrawFirstFrame, DrawFrames, DrawFps, false);
                return true;
            case HeroArcherAnimation.Release:
                clip = new HeroArcherClip(animation, ReleaseFrame, 1, DrawFps, false);
                return true;
            case HeroArcherAnimation.Recovery:
                clip = new HeroArcherClip(animation, RecoveryFrame, 1, DrawFps, false);
                return true;
            default:
                clip = default;
                return false;
        }
    }

    /// <summary>atlas 索引 → (column,row)；越界索引按 0 复用（调用方只会拿到 0..15，此处仅防御）。</summary>
    internal static void FrameToCell(int frame, out int column, out int row)
    {
        if (frame < 0 || frame >= FrameCount) frame = 0;
        column = frame % Columns;
        row = frame / Columns;
    }

    /// <summary>固定 fps 的有界帧选择：phase &lt;= 0 → 首帧；循环动作取模；非循环动作停在末帧。</summary>
    internal static int FrameAt(HeroArcherClip clip, double phaseSeconds)
    {
        if (clip.FrameCount <= 0) return 0;
        if (double.IsNaN(phaseSeconds)) return clip.FirstFrame;
        if (phaseSeconds > MaxClipPhaseSeconds) phaseSeconds = MaxClipPhaseSeconds;
        if (!(phaseSeconds > 0d)) return clip.FirstFrame;

        long step = (long)(phaseSeconds * clip.Fps + FrameEpsilon);
        if (clip.Loops)
        {
            if (step >= clip.FrameCount) step %= clip.FrameCount;
        }
        else if (step >= clip.FrameCount)
        {
            step = clip.FrameCount - 1;
        }
        if (step < 0) step = 0;
        return clip.FirstFrame + (int)step;
    }
}

/// <summary>
/// 显式时钟驱动的英雄弓手动画状态机（无 Unity 依赖、无分配：Tick 只做算术与有界状态推进）。
/// 单一可变状态：motion(Idle/Walk/Run) + 拉弓请求 + 放箭/收势阶段；事件锚点固定，
/// 同一时刻重复 Tick 返回同一帧，重复 SetMotion/BeginDraw/NotifyRelease 绝不重置已开始的片段
/// （NotifyRelease 只在同一放箭窗口内算重复；窗口已过期的放箭开新窗口，见该方法）。
/// </summary>
internal sealed class HeroArcherAnimationState
{
    private enum ShotPhase
    {
        None = 0,
        Release = 1,
        Recovery = 2,
    }

    private bool _enabled = true;
    private bool _hasSample;
    private double _lastValidNow;
    private int _lastFrame = HeroArcherAtlas.IdleFirstFrame;

    private HeroArcherAnimation _motion = HeroArcherAnimation.Idle;
    private double _motionStart;

    private bool _drawRequested;
    private double _drawStart;

    private ShotPhase _shot;
    private double _shotStart;

    /// <summary>关闭后 Tick 恒返回 idle 首帧并忽略全部事件；重新打开时以首个有效 Tick 重新锚定。</summary>
    internal bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            ClearMotionState();
        }
    }

    /// <summary>原生 Archer.OnEnable / 池复用 / 换局时调用：清空全部相位与动作（不改变 Enabled）。</summary>
    internal void Reset() => ClearMotionState();

    /// <summary>
    /// 移动状态（Idle/Walk/Run）。同值重复调用 = no-op（不会重置走路循环相位）；
    /// 传入动作枚举或其他非法值一律忽略；非法/回退时间忽略。
    /// </summary>
    internal void SetMotion(HeroArcherAnimation motion, double now)
    {
        if (!HeroArcherAtlas.IsMotion(motion)) return;
        if (!TryBeginTimeStep(now)) return;
        if (_motion == motion) return;

        _motion = motion;
        _motionStart = now;
        _lastFrame = ResolveFrame(now);
    }

    /// <summary>拉弓开始（原生 Prepare=true）。重复调用 = no-op，绝不重启已开始的拉弓片段。</summary>
    internal void BeginDraw(double now)
    {
        if (!TryBeginTimeStep(now)) return;
        if (_drawRequested) return;

        _drawRequested = true;
        _drawStart = now;
        _lastFrame = ResolveFrame(now);
    }

    /// <summary>
    /// 拉弓结束（原生 Prepare=false）。未在拉弓状态时 no-op；回退时间（早于单调时钟）拒绝，
    /// 因此「BeginDraw(10) 后到来的 EndDraw(9)」不会关掉已经在进行的拉弓。
    /// </summary>
    internal void EndDraw(double now)
    {
        if (!TryBeginTimeStep(now)) return;
        if (!_drawRequested) return;

        _drawRequested = false;
        _lastFrame = ResolveFrame(now);
    }

    /// <summary>
    /// 放箭事件（原生 Archer.FireArrow 的动画 trigger 时刻）。先按当前时刻推进已过期的窗口再判重：
    /// 同一放箭窗口内的重复上报是 no-op（不会重置 14→15→回落的时钟）；窗口已过期的第二箭
    /// （齐射连发、两箭之间没有 Tick）必须开新的放箭窗口，而不是被 duplicate 检查吞掉。
    /// </summary>
    internal void NotifyRelease(double now)
    {
        if (!TryBeginTimeStep(now)) return;
        if (_shot == ShotPhase.Release) return;

        _shot = ShotPhase.Release;
        _shotStart = now;
        _lastFrame = ResolveFrame(now);
    }

    /// <summary>当前应显示的 atlas 帧（0..15）。带状态推进，但对同一时刻可重复调用（幂等）。</summary>
    internal int Tick(double now)
    {
        if (!_enabled)
        {
            _lastFrame = HeroArcherAtlas.IdleFirstFrame;
            return _lastFrame;
        }
        if (!TryBeginTimeStep(now)) return _lastFrame;

        _lastFrame = ResolveFrame(now);
        return _lastFrame;
    }

    /// <summary>
    /// 事件与 Tick 的唯一时间入口：拒绝禁用 / NaN / ±Inf / 负时间 / 早于单调时钟的回退时间
    /// （返回 false，调用方直接返回、不改任何状态），否则把**单一单调时钟**推进到 now，
    /// 再按 now 推进已过期的放箭窗口。所有公开时间入口共用它，保证「事件先到」与「Tick 先到」
    /// 对同一时刻得到同一结果，也让「时钟只增不减」成为唯一时间语义。
    /// </summary>
    private bool TryBeginTimeStep(double now)
    {
        if (!_enabled || !IsValidTime(now)) return false;
        if (_hasSample && now < _lastValidNow) return false;

        EnsureAnchored(now);
        _lastValidNow = now;
        AdvanceShot(now);
        return true;
    }

    /// <summary>
    /// 按当前时刻推进已过期的放箭窗口（有界：最多 Release→Recovery→结束；全部以锚点+常量判定，
    /// 与采样时刻无关）。事件方法与 Tick 共用，保证「事件先到、Tick 后到」与「Tick 先到」得到同一结果。
    /// </summary>
    private void AdvanceShot(double now)
    {
        for (int i = 0; i < 3 && _shot != ShotPhase.None; i++)
        {
            if (_shot == ShotPhase.Release)
            {
                if (now - _shotStart < HeroArcherAtlas.ReleaseSeconds) break;
                _shot = ShotPhase.Recovery;
                _shotStart += HeroArcherAtlas.ReleaseSeconds;
            }
            if (_shot == ShotPhase.Recovery)
            {
                if (now - _shotStart < HeroArcherAtlas.RecoverySeconds) break;
                _shot = ShotPhase.None;
                // 收势结束立刻回到（可能仍在拉弓的）显示：移动相位确定性重锚在转换时刻。
                _motionStart = _shotStart + HeroArcherAtlas.RecoverySeconds;
                break;
            }
        }
    }

    private int ResolveFrame(double now)
    {
        if (_shot == ShotPhase.Release) return HeroArcherAtlas.ReleaseFrame;
        if (_shot == ShotPhase.Recovery) return HeroArcherAtlas.RecoveryFrame;

        if (_drawRequested)
        {
            HeroArcherAtlas.TryGetClip(HeroArcherAnimation.Draw, out HeroArcherClip draw);
            return HeroArcherAtlas.FrameAt(draw, now - _drawStart);
        }

        return HeroArcherAtlas.FrameAt(MotionClip(), now - _motionStart);
    }

    private HeroArcherClip MotionClip()
    {
        HeroArcherAtlas.TryGetClip(_motion, out HeroArcherClip clip);
        return clip;
    }

    /// <summary>首个有效时刻：把「已采样」与移动相位锚定在同一时刻（事件先于首次 Tick 时也不留悬浮相位）。</summary>
    private void EnsureAnchored(double now)
    {
        if (_hasSample) return;
        _hasSample = true;
        _lastValidNow = now;
        _motionStart = now;
    }

    private void ClearMotionState()
    {
        _hasSample = false;
        _lastValidNow = 0d;
        _lastFrame = HeroArcherAtlas.IdleFirstFrame;
        _motion = HeroArcherAnimation.Idle;
        _motionStart = 0d;
        _drawRequested = false;
        _drawStart = 0d;
        _shot = ShotPhase.None;
        _shotStart = 0d;
    }

    private static bool IsValidTime(double now)
        => !double.IsNaN(now) && !double.IsInfinity(now) && now >= 0d;
}
