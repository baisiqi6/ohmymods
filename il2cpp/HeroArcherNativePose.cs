// 英雄弓手·原生动作跟随（worker 2026-09-15 native-follow slice）。
//
// 目标：自有姿势 atlas 的推进不用自有时钟/六状态机，而是每帧读原生 Animator 的当前
//       state：shortNameHash + normalizedTime，转场期间也仅采样 current，对应姿势：
//         Stand 0-9（循环）· Walk 10-15（循环）· Run 16-21（循环）· Prepare 22-25（单次）· Shoot 26-30（单次）
//       （operator 2026-09-15 重制：8 列 × 4 行、单格 48×32、整图 384×128，31 个有效帧，末格空白）
//       Ghost Die / Spawn 及一切未绘状态（Norse 系 Defend/Get Bow/Attack (Defend) 等）= Unknown
//       → 调用方（HeroArcherVisuals）挂起自有表现、归还原生 renderer，让原生显现；
//         不猜动作、不因未知状态 Remove/Apply 反复重建。
//
// 已核资源事实（artifacts/hero-animation-study/20260915/controllers.json；resources.assets:6128）：
//   base 弓手状态 = Stand / Shoot / Prepare / Walk / Run / "Ghost Die" / Spawn（状态名即 shortNameHash 来源）；
//   base 状态间 transition duration 全为 0（IsInTransition 通常为 false）；Stand→Walk 门 Speed>0.005、
//   Walk↔Run 门 1.0、Prepare 可被 Speed 打断、Stand 播放速度参数 Idleness —— 这些判据全部留给原生，
//   本文件不再读 Speed/Prepare、不再 Classify(walkSpeed+0.05)、不再让 Prepare 永远压住移动。
//
// 何时读：必须发生在原生 Animator 产出本帧结果之后（调用方入口 = 自有驱动/面板的 LateUpdate 同阶段入口）。
//         本文件不对「本机实际执行顺序」作任何断言，只记录来源标签。
//
// Prepare 的相位归一（2026-09-15 live-fix）：原生 Prepare 状态只持续 shootPrepTime，而它播放的 clip 是
//         基础 0.5s / 希腊覆盖 2.167s。按全局 normalizedTime 归一会让 4 张拉弓帧全部落在 clip 的
//         极小前缀里（希腊世界只可能显示 22 号帧），用户因此看不到拉弓动作。
//         现用「clip 时间（= normalizedTime × clipLength）/ 有效窗口」归一。窗口不由本文件测量：
//         operator 桥在 native cadence 借用之后调用 RecordPrepareWindow，传入的正是原生同一步
//         MoveNext 里读到的临时 shootPrepTime（rate/DL 效果已含）；调用方未给出窗口时确定性回退原始相位。
//         无 clip 信息（length 不可读）时同样回退。
//
// 不做：写 Animator（无 Play/CrossFade/Update/参数/speed 写入）、加 Harmony 钩子、扫描场景或全部角色、
//       逐帧日志、按 actor 无界增长的记录；材质/染色仍由调用方只读复制原生。
//
// 分工：
//   * 纯逻辑（tests/hero-native-pose 直接链接）—— 状态映射表、相位→帧号、肩部起伏、
//     诊断事件去重与全局上限。
//   * Unity 桥（HeroArcherNativeSampler，只读）—— GetCurrentAnimatorStateInfo /
//     isActiveAndEnabled。只缓存「状态名 → hash」常量，
//     与 controller 无关（世界/性别换皮替换 controller 后依旧成立），绝不缓存旧 controller 的状态数据。

using System;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>原生 Archer 控制器中我们已绘制对应姿势的状态；其余一律 Unknown（自有表现挂起，原生显现）。</summary>
internal enum HeroArcherNativeAction
{
    Unknown = 0,
    Stand = 1,
    Walk = 2,
    Run = 3,
    Prepare = 4,
    Shoot = 5,
}

/// <summary>本帧为什么不显示自有姿势（诊断字段，同时决定是否归还原生 forceRenderingOff）。</summary>
internal enum HeroArcherNativeFallback
{
    /// <summary>已按原生状态接管并显示。</summary>
    None = 0,
    /// <summary>_animator 缺失（池化/初始化窗口）。</summary>
    NoAnimator = 1,
    /// <summary>Animator 未启用/所在对象未激活（原生停播）。</summary>
    AnimatorDisabled = 2,
    /// <summary>读取 Animator 抛错，或写自有 sprite 抛错。</summary>
    Unreadable = 3,
    /// <summary>当前 state 未绘制（Ghost Die / Spawn / 世界的其它状态名）。</summary>
    UnknownState = 4,
    /// <summary>原生 renderer 不可见（SetHideStatus enabled=false / 第三方 forceRenderingOff）：跟随隐藏。</summary>
    NativeHidden = 5,
    /// <summary>atlas 帧缺失（尺寸/解码异常后的兜底）。</summary>
    SpriteUnavailable = 6,
}

/// <summary>一次原生采样结果（不可变）。可见性与接管与否由调用方决定。</summary>
internal readonly struct HeroArcherNativePoseSample
{
    internal readonly HeroArcherNativeAction Action;
    internal readonly int StateHash;
    internal readonly float NormalizedTime;
    /// <summary>当前状态 clip 的长度（秒）；0 = 不可读/非法（Prepare 回退到原始相位）。</summary>
    internal readonly float ClipLength;
    internal readonly int Frame;
    internal readonly HeroArcherNativeFallback Fallback;

    internal HeroArcherNativePoseSample(HeroArcherNativeAction action, int stateHash, float normalizedTime,
        float clipLength, int frame, HeroArcherNativeFallback fallback)
    {
        Action = action;
        StateHash = stateHash;
        NormalizedTime = normalizedTime;
        ClipLength = clipLength;
        Frame = frame;
        Fallback = fallback;
    }

    /// <summary>可绘制 = 动作已绘且帧号有效（可见性仍须原生 renderer 可见）。</summary>
    internal bool Drawn => Action != HeroArcherNativeAction.Unknown && Frame >= 0;

    internal static HeroArcherNativePoseSample Failed(HeroArcherNativeFallback fallback)
        => new HeroArcherNativePoseSample(HeroArcherNativeAction.Unknown, 0, 0f, 0f, -1, fallback);
}

/// <summary>五个已绘状态的 shortNameHash（由 Unity 侧 Animator.StringToHash 一次性取得；不手抄魔数）。</summary>
internal readonly struct HeroArcherNativeStateHashes
{
    internal readonly int Stand;
    internal readonly int Walk;
    internal readonly int Run;
    internal readonly int Prepare;
    internal readonly int Shoot;

    internal HeroArcherNativeStateHashes(int stand, int walk, int run, int prepare, int shoot)
    {
        Stand = stand;
        Walk = walk;
        Run = run;
        Prepare = prepare;
        Shoot = shoot;
    }
}

/// <summary>
/// 英雄姿势 atlas 布局（operator 2026-09-15 重制版：8 列 × 4 行、单格 48×32、整图 384×128，
/// 共 31 个有效帧，末格（col 7 / row 3 → index 31）空白）。PPU 32、脚点 pivot 像素 (31,2) 不变。
/// 旧 HeroArcherAnimation/HeroArcherAtlas 的 16 帧定义只留给旧回归，不再被视觉引用。
/// </summary>
internal static class HeroArcherPoseAtlas
{
    internal const int Columns = 8;
    internal const int Rows = 4;
    internal const int CellWidth = 48;
    internal const int CellHeight = 32;
    internal const int SheetWidth = Columns * CellWidth;    // 384
    internal const int SheetHeight = Rows * CellHeight;     // 128
    /// <summary>有效帧数（最后格空白，不是 32）。</summary>
    internal const int FrameCount = 31;

    internal const float PixelsPerUnit = 32f;
    internal const float PivotPixelX = 31f;
    internal const float PivotPixelY = 2f;

    internal static bool IsValidFrame(int frame) => frame >= 0 && frame < FrameCount;

    // Matches the authored upper-body lift in the 31-slot atlas. Unity y points up.
    internal static int TorsoLiftPixels(int frame) => frame switch
    {
        17 or 20 => 2,
        4 or 5 or 6 or 12 or 15 or 18 or 21 => 1,
        _ => 0
    };

    /// <summary>帧号 → 网格坐标（row-major：从左上角起，行内自左向右、逐行向下）。非法帧返回 false。</summary>
    internal static bool FrameToCell(int frame, out int column, out int row)
    {
        column = 0;
        row = 0;
        if (!IsValidFrame(frame)) return false;
        column = frame % Columns;
        row = frame / Columns;
        return true;
    }
}

/// <summary>纯逻辑：原生状态/相位 → atlas 帧号；转场选择；无 Unity 依赖、无分配。</summary>
internal static class HeroArcherNativePose
{
    // 31 帧布局的动作区间（合计 31）：Stand 0..9 / Walk 10..15 / Run 16..21 / Prepare 22..25 / Shoot 26..30。
    internal const int StandFirstFrame = 0;
    internal const int StandFrameCount = 10;
    internal const int WalkFirstFrame = 10;
    internal const int WalkFrameCount = 6;
    internal const int RunFirstFrame = 16;
    internal const int RunFrameCount = 6;
    internal const int PrepareFirstFrame = 22;
    internal const int PrepareFrameCount = 4;
    internal const int ShootFirstFrame = 26;
    internal const int ShootFrameCount = 5;

    /// <summary>
    /// shortNameHash → 已绘动作；0 与表外 hash 一律 Unknown（表未就绪/换 controller/未绘制状态都走这里）。
    /// </summary>
    internal static HeroArcherNativeAction ResolveAction(int shortNameHash, in HeroArcherNativeStateHashes hashes)
    {
        if (shortNameHash == 0) return HeroArcherNativeAction.Unknown;
        if (shortNameHash == hashes.Stand) return HeroArcherNativeAction.Stand;
        if (shortNameHash == hashes.Walk) return HeroArcherNativeAction.Walk;
        if (shortNameHash == hashes.Run) return HeroArcherNativeAction.Run;
        if (shortNameHash == hashes.Prepare) return HeroArcherNativeAction.Prepare;
        if (shortNameHash == hashes.Shoot) return HeroArcherNativeAction.Shoot;
        return HeroArcherNativeAction.Unknown;
    }

    /// <summary>Stand/Walk/Run 循环（取相位小数部分）；Prepare/Shoot 单次（clamp 到最后）。</summary>
    internal static bool IsLooping(HeroArcherNativeAction action)
        => action == HeroArcherNativeAction.Stand
            || action == HeroArcherNativeAction.Walk
            || action == HeroArcherNativeAction.Run;

    /// <summary>该动作在 atlas 中的首帧；Unknown = -1。</summary>
    internal static int FirstFrame(HeroArcherNativeAction action)
    {
        switch (action)
        {
            case HeroArcherNativeAction.Stand: return StandFirstFrame;
            case HeroArcherNativeAction.Walk: return WalkFirstFrame;
            case HeroArcherNativeAction.Run: return RunFirstFrame;
            case HeroArcherNativeAction.Prepare: return PrepareFirstFrame;
            case HeroArcherNativeAction.Shoot: return ShootFirstFrame;
            default: return -1;
        }
    }

    /// <summary>该动作占用的 atlas 帧数；Unknown = 0（调用方视为不可绘制）。</summary>
    internal static int FrameCount(HeroArcherNativeAction action)
    {
        switch (action)
        {
            case HeroArcherNativeAction.Stand: return StandFrameCount;
            case HeroArcherNativeAction.Walk: return WalkFrameCount;
            case HeroArcherNativeAction.Run: return RunFrameCount;
            case HeroArcherNativeAction.Prepare: return PrepareFrameCount;
            case HeroArcherNativeAction.Shoot: return ShootFrameCount;
            default: return 0;
        }
    }

    /// <summary>
    /// 相位 → atlas 帧号。循环动作取 normalizedTime 小数部分（原生多圈累积 3.7 → 0.7；负值安全归一化）；
    /// Shoot clamp 到 [0,1]（&gt;1 停在末帧）；Prepare 见 <see cref="PreparePhase"/>；
    /// NaN/±Inf → 该动作首帧（确定性，不抛、不跳帧）；Unknown → -1；绝不越界。
    /// clipLength ≤ 0 / 非有限 = 无 clip 信息 → Prepare 回退到原始相位（退化路径：只用于测试与异常读取）。
    /// </summary>
    internal static int FrameFor(HeroArcherNativeAction action, float normalizedTime,
        float clipLength = 0f, float prepareWindowSeconds = 0f)
    {
        int count = FrameCount(action);
        if (count <= 0) return -1;
        int first = FirstFrame(action);
        if (!float.IsFinite(normalizedTime)) return first;
        float phase;
        if (IsLooping(action)) phase = Fraction(normalizedTime);
        else if (action == HeroArcherNativeAction.Prepare) phase = PreparePhase(normalizedTime, clipLength, prepareWindowSeconds);
        else phase = Clamp01(normalizedTime);
        int index = (int)(phase * count);
        if (index >= count) index = count - 1;
        if (index < 0) index = 0;
        return first + index;
    }

    // ---- 原生 Prepare 可见窗口（秒）------------------------------------------
    // 原生 Prepare 状态只持续 shootPrepTime，而它播放的 clip 远长于此（基础弓手 0.5s、
    // 希腊覆盖 2.167s 的投掷旋转）。直接用全局 normalizedTime 归一，4 张拉弓帧会全部挤在 clip 的
    // 极小前缀里（希腊世界只可能显示 22 号帧 → 用户看不到拉弓）。故单次 Prepare 用
    // 「clip 时间 / 有效窗口」归一。
    // 窗口由调用方给出（operator 桥在 native cadence 借用之后记录同一步的原生读值）；
    // 调用方未给出（≤0/非有限）→ 回退原始相位，行为与旧实现一致（确定性、不抛）。

    /// <summary>
    /// Prepare 相位 = clip 时间 / 有效窗口（两者任一非法/非正 → 回退原始相位，确定性、不抛）。
    /// clip 时间 = normalizedTime × clipLength（状态速度 1 下即该 clip 内的秒数）。
    /// </summary>
    internal static float PreparePhase(float normalizedTime, float clipLength, float prepareWindowSeconds)
    {
        if (!(clipLength > 0f) || !float.IsFinite(clipLength)) return Clamp01(normalizedTime);
        if (!(prepareWindowSeconds > 0f) || !float.IsFinite(prepareWindowSeconds)) return Clamp01(normalizedTime);
        float clipTime = normalizedTime * clipLength;
        if (!float.IsFinite(clipTime)) return Clamp01(normalizedTime);
        return Clamp01(clipTime / prepareWindowSeconds);
    }

    /// <summary>小数部分 ∈ [0,1)：对负值、多圈、超大幅度都成立；非有限/舍入异常 → 0。</summary>
    internal static float Fraction(float value)
    {
        float fraction = value - (float)Math.Floor(value);
        if (!(fraction >= 0f) || fraction >= 1f) return 0f;
        return fraction;
    }

    private static float Clamp01(float value)
    {
        if (!(value > 0f)) return 0f;   // NaN/负值 → 0
        return value >= 1f ? 1f : value;
    }
}

/// <summary>
/// 诊断事件键（不可变）：只在「转场/可见性/来源」变化时记一行。帧号与时间是载荷，不参与去重，
/// 因此全局上限是「事件数」而不是「帧数」。
/// </summary>
internal readonly struct HeroArcherNativeEventKey
{
    internal readonly int Actor;
    internal readonly int Life;
    internal readonly int StateHash;
    internal readonly bool NativeVisible;
    internal readonly bool HeroVisible;
    internal readonly HeroArcherNativeFallback Fallback;

    internal HeroArcherNativeEventKey(int actor, int life, int stateHash, bool nativeVisible, bool heroVisible,
        HeroArcherNativeFallback fallback)
    {
        Actor = actor;
        Life = life;
        StateHash = stateHash;
        NativeVisible = nativeVisible;
        HeroVisible = heroVisible;
        Fallback = fallback;
    }

    /// <summary>同一个 actor 的同一个转场（含可见性与挂起原因）视为已记录。</summary>
    internal static bool SameTransition(in HeroArcherNativeEventKey a, in HeroArcherNativeEventKey b)
        => a.Actor == b.Actor
            && a.Life == b.Life
            && a.StateHash == b.StateHash
            && a.NativeVisible == b.NativeVisible
            && a.HeroVisible == b.HeroVisible
            && a.Fallback == b.Fallback;
}

/// <summary>
/// 有界全局 transition 事件日志（纯逻辑，无 Unity/无日志 IO）：
/// 每个 actor 一条「上次事件」槽（固定 Slots=4，LRU 复用 → 内存不随 actor 数增长），
/// 全局最多 Capacity 行；同一 actor 的相同转场重复采样不重复记录。日志失败由调用方吞掉，绝不影响渲染。
/// </summary>
internal sealed class HeroArcherNativeEventLog
{
    /// <summary>全局事件上限（本模组每次 Clear 后重新计数）。</summary>
    internal const int Capacity = 120;

    /// <summary>每 actor 去重槽数（上限内固定，actor 再多也只是轮换槽位）。</summary>
    private const int Slots = 4;

    private struct Slot
    {
        internal int Actor;
        internal bool Used;
        internal int Stamp;
        internal HeroArcherNativeEventKey Key;
    }

    private readonly Slot[] _slots = new Slot[Slots];
    private int _stamp;
    private int _count;

    /// <summary>已记录行数（自检/测试用）。</summary>
    internal int Count => _count;

    /// <summary>上限用尽：不再接受任何事件（调用方直接跳过日志构造）。</summary>
    internal bool Exhausted => _count >= Capacity;

    /// <summary>true = 调用方应写一行日志（该 actor 的转场/可见性/原因发生了变化）。</summary>
    internal bool Accept(in HeroArcherNativeEventKey key)
    {
        if (_count >= Capacity) return false;
        int index = SlotFor(key.Actor);
        if (_slots[index].Used && HeroArcherNativeEventKey.SameTransition(in _slots[index].Key, in key)) return false;
        _slots[index].Actor = key.Actor;
        _slots[index].Key = key;
        _slots[index].Used = true;
        _slots[index].Stamp = ++_stamp;
        _count++;
        return true;
    }

    /// <summary>换 world / 模组关闭：预算与去重槽重置（不做无界账本）。</summary>
    internal void Reset()
    {
        for (int i = 0; i < _slots.Length; i++) _slots[i] = default;
        _stamp = 0;
        _count = 0;
    }

    private int SlotFor(int actor)
    {
        int free = -1;
        int oldest = 0;
        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i].Used && _slots[i].Actor == actor) return i;
            if (!_slots[i].Used && free < 0) free = i;
            if (_slots[i].Stamp < _slots[oldest].Stamp) oldest = i;
        }
        return free >= 0 ? free : oldest;
    }
}

/// <summary>
/// Unity 桥（只读 Animator）。每帧从当下 controller 读 base layer 的当前/下一 state；
/// 绝不 Play/CrossFade/Update，绝不写参数或 speed，不加钩子。
/// </summary>
internal static class HeroArcherNativeSampler
{
    private const int Layer = 0;

    private static HeroArcherNativeStateHashes _hashes;
    private static bool _hashesReady;

    /// <summary>
    /// 采样当前姿势（含转场策略）。任何异常都收敛成 Fallback（Unreadable），绝不向外抛。
    /// <paramref name="prepareWindowSeconds"/> 为 Prepare 可见窗口（秒；≤0 = 无窗口信息 → 原始相位）。
    /// </summary>
    internal static HeroArcherNativePoseSample Sample(Animator animator, float prepareWindowSeconds = 0f)
    {
        if (animator == null) return HeroArcherNativePoseSample.Failed(HeroArcherNativeFallback.NoAnimator);
        try
        {
            if (!animator.isActiveAndEnabled)
                return HeroArcherNativePoseSample.Failed(HeroArcherNativeFallback.AnimatorDisabled);
            if (!TryGetHashes(out HeroArcherNativeStateHashes hashes))
                return HeroArcherNativePoseSample.Failed(HeroArcherNativeFallback.Unreadable);

            AnimatorStateInfo chosen = animator.GetCurrentAnimatorStateInfo(Layer);

            int hash = chosen.shortNameHash;
            HeroArcherNativeAction action = HeroArcherNativePose.ResolveAction(hash, in hashes);
            float normalizedTime = chosen.normalizedTime;
            if (!float.IsFinite(normalizedTime))
                return HeroArcherNativePoseSample.Failed(HeroArcherNativeFallback.Unreadable);
            float clipLength = ReadClipLength(in chosen);
            int frame = HeroArcherNativePose.FrameFor(action, normalizedTime, clipLength, prepareWindowSeconds);
            return new HeroArcherNativePoseSample(action, hash, normalizedTime, clipLength, frame,
                action == HeroArcherNativeAction.Unknown
                    ? HeroArcherNativeFallback.UnknownState
                    : HeroArcherNativeFallback.None);
        }
        catch (Exception)
        {
            return HeroArcherNativePoseSample.Failed(HeroArcherNativeFallback.Unreadable);
        }
    }

    /// <summary>当前状态 clip 长度（秒）：不可读/非法/非正 → 0（= 无 clip 信息，Prepare 回退原始相位）。</summary>
    private static float ReadClipLength(in AnimatorStateInfo state)
    {
        try
        {
            float length = state.length;
            return float.IsFinite(length) && length > 0f ? length : 0f;
        }
        catch (Exception)
        {
            return 0f;
        }
    }

    /// <summary>
    /// 状态名 → hash 一次算好（首次采样；与原生同一算法，不手抄魔数）。失败 → false（调用方挂起，不猜表）。
    /// 只缓存这五个常量：状态数据每帧仍从当下 Animator/controller 现读。
    /// </summary>
    private static bool TryGetHashes(out HeroArcherNativeStateHashes hashes)
    {
        if (_hashesReady)
        {
            hashes = _hashes;
            return true;
        }
        try
        {
            _hashes = new HeroArcherNativeStateHashes(
                Animator.StringToHash("Stand"),
                Animator.StringToHash("Walk"),
                Animator.StringToHash("Run"),
                Animator.StringToHash("Prepare"),
                Animator.StringToHash("Shoot"));
            _hashesReady = true;
            hashes = _hashes;
            return true;
        }
        catch (Exception)
        {
            hashes = default;
            return false;
        }
    }
}
