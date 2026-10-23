using System;
using UnityEngine;

namespace KingdomEnhancedMod;

// R6 六局部动态逐帧时钟 helper（source-only：无 hook、无 MonoBehaviour、无 Update、无扫描、不读资源、不创建/销毁任何 Unity 对象）。
//
// Root（唯一 owner）接线顺序：
//   1. 创建自有 inactive root（原 Depth 根）→ new PalaceLocalMotionClock(root GameObject)。
//   2. 在 PalaceDepthScene.AddLayer 的六个 dyn-* 分支：每件按运行时帧清单原字节加载 8 张 frames PNG，创建 8 Sprite/Texture
//      并立即登记进原 _sprites/_textures；仍只创建原一 renderer，先显示 frames[0]（phase0），
//      然后 Bind(id, renderer, frames8, out reason)。六 id 与顺序见 Ids；sourceSlot 17..22 只作参考 recipe 索引，不是 runtime 排序。
//   3. Depth.Activate 的 SetActive+readback 成功之后 Begin(UnityEngine.Time.time)。
//   4. 既有 Depth.Tick 的 owner/camera 门全部通过后 Tick(UnityEngine.Time.time)。
//   5. outer Clear：先 Close()，再由 Depth 原逻辑解绑六 renderer.sprite 并销毁 48 帧 Sprite/Texture（本 helper 不销毁任何对象）。
//
// 责任边界：本 helper 只借 renderer/frame 引用，仅在相位改变时写六个自有 renderer.sprite 并读回 pointer 核对；
// 不写 transform/scale/position/sorting/PPU/材质/Animator/wallet/save/Native 层；不重建、不重试、不持续纠正、
// 不猜测风雨/原水/Native 天气，也不触碰其它 18 层与 borrowed material。任何校验失败 → false+reason，交 Root 原有 Clear。
//
// 时钟：同一 begin 之后只用调用方传入的 scaled Time.time（暂停即冻结）；phase = floor(((now - begin) mod 4s) / .5s)，
// 8 帧 × 500ms 循环，六件同 phase、无随机 stagger、无相位持久化。非有限时间、时间倒退、root/renderer 身份/场景/child/active
// 漂移、外来 sprite 写入、写后读回不符 → 拒绝（不 clamp、不 reset、不覆写修复）。
internal sealed class PalaceLocalMotionClock
{
    private const int FrameCount = 8;
    private const float FrameSeconds = .5f;                      // 500ms/帧
    private const float LoopSeconds = FrameSeconds * FrameCount; // 4000ms/圈
    private const int TargetCount = 6;
    private const int MaxPhaseLogs = 16;

    // 固定六 id（runtime-frame-inputs.json objects 顺序）；不接受其它 id，也不按 sourceSlot/runtime 数字裸索引。
    private static readonly string[] Ids =
    {
        "dyn-awning-w1",
        "dyn-awning-w2",
        "dyn-awning-w3",
        "dyn-willow-west",
        "dyn-willow-east",
        "dyn-bamboo-leaves"
    };

    /// <summary>每 id 一次 Bind 的借用记录：renderer/frames 引用 + 冻结精确指针（identity 与读回都用 pointer 比较）。</summary>
    private sealed class Target
    {
        internal readonly string Id;
        internal readonly SpriteRenderer Renderer;
        internal readonly IntPtr RendererPointer;
        internal readonly Sprite[] Frames;        // 8 张预加载帧；owner 是 Root，最终销毁也归 Root
        internal readonly IntPtr[] FramePointers; // frames[i].Pointer 冻结副本

        internal Target(string id, SpriteRenderer renderer, IntPtr rendererPointer, Sprite[] frames, IntPtr[] framePointers)
        {
            Id = id;
            Renderer = renderer;
            RendererPointer = rendererPointer;
            Frames = frames;
            FramePointers = framePointers;
        }
    }

    private readonly Target[] _targets = new Target[TargetCount];
    private GameObject _root;
    private readonly IntPtr _rootPointer;
    private readonly int _rootSceneHandle;
    private bool _begun, _closed;
    private float _begin, _lastSample;
    private int _phase;
    private int _phaseLogs;

    /// <summary>当前循环相位。未 Begin 时保持创建即显示的 0；Close 后保留最后值，只读不驱动。</summary>
    internal int CurrentPhase => _phase;

    internal PalaceLocalMotionClock(GameObject root)
    {
        if (root == null) throw new ArgumentNullException(nameof(root));
        if (!(bool)root) throw new InvalidOperationException("R6 motion clock requires a live root GameObject");
        if (!root.scene.IsValid()) throw new InvalidOperationException("R6 motion clock root has no valid scene");
        _root = root;
        _rootPointer = root.Pointer;
        _rootSceneHandle = root.scene.handle;
    }

    /// <summary>
    /// 六固定 id 各一次：校验 renderer 存活 / 同 root scene / 是 root 后代 / 当前 sprite 就是 frames[0]，
    /// 以及 frames 恰为 8 张且全部非 null/native alive。成功只冻结引用与精确指针；不放宽、不默认填空、不改 sprite/transform。
    /// root 此刻仍可 inactive（Root 在 Activate 前完成全部 Bind）。
    /// </summary>
    internal bool Bind(string id, SpriteRenderer renderer, Sprite[] frames, out string reason)
    {
        reason = null;
        try
        {
            if (_closed) { reason = "clock already closed"; return false; }
            if (_begun) { reason = "clock already begun; late Bind rejected"; return false; }
            int slot = -1;
            for (int i = 0; i < Ids.Length; i++)
                if (string.Equals(Ids[i], id, StringComparison.Ordinal)) { slot = i; break; }
            if (slot < 0) { reason = "id is not one of the six fixed R6 motion ids: " + (id ?? "null"); return false; }
            if (_targets[slot] != null) { reason = id + ": already bound; exactly one Bind per id"; return false; }
            if (_root == null || !(bool)_root) { reason = "owned root unavailable for " + id; return false; }
            if (_root.Pointer != _rootPointer || _root.scene.handle != _rootSceneHandle)
            { reason = "owned root identity/scene changed before Bind of " + id; return false; }
            if (renderer == null || !(bool)renderer || renderer.Pointer == IntPtr.Zero)
            { reason = id + ": renderer null/destroyed"; return false; }
            GameObject go = renderer.gameObject;
            if (go == null || !(bool)go || renderer.transform == null)
            { reason = id + ": renderer GameObject/Transform unavailable"; return false; }
            if (frames == null || frames.Length != FrameCount)
            { reason = id + ": frames must be exactly " + FrameCount; return false; }
            var pointers = new IntPtr[FrameCount];
            for (int i = 0; i < FrameCount; i++)
            {
                Sprite frame = frames[i];
                if (frame == null || !(bool)frame || frame.Pointer == IntPtr.Zero)
                { reason = id + ": frame " + i + " null/destroyed"; return false; }
                pointers[i] = frame.Pointer;
            }
            if (go.scene.handle != _rootSceneHandle)
            { reason = id + ": renderer scene differs from the owned root scene"; return false; }
            if (!renderer.transform.IsChildOf(_root.transform))
            { reason = id + ": renderer is not a descendant of the owned root"; return false; }
            Sprite current = renderer.sprite;
            if (current == null || !(bool)current || current.Pointer != pointers[0])
            { reason = id + ": renderer must already show frames[0] (phase0) at Bind"; return false; }
            _targets[slot] = new Target(id, renderer, renderer.Pointer, frames, pointers);
            return true;
        }
        catch (Exception error)
        {
            reason = "R6 motion bind fault for " + (id ?? "null") + ": " + error.GetType().Name + ": " + error.Message;
            return false;
        }
    }

    /// <summary>
    /// 六件全部 Bind 且 root 已 active（Root 在 Depth.Activate SetActive+readback 之后）时一次。
    /// 复核 root 与六 renderer 当前就是 frames[0] 之后只记 epoch（scaledTime）与初始 phase0；不写 sprite。
    /// </summary>
    internal bool Begin(float scaledTime, out string reason)
    {
        reason = null;
        try
        {
            if (_closed) { reason = "clock already closed"; return false; }
            if (_begun) { reason = "clock already begun; Begin is once"; return false; }
            if (!float.IsFinite(scaledTime)) { reason = "scaled time is not finite"; return false; }
            for (int i = 0; i < TargetCount; i++)
                if (_targets[i] == null) { reason = "Bind incomplete: " + Ids[i] + " not bound"; return false; }
            if (!VerifyRoot(true, out string rootReason)) { reason = rootReason; return false; }
            for (int i = 0; i < TargetCount; i++)
            {
                Target target = _targets[i];
                if (!VerifyTarget(target, out string targetReason)) { reason = targetReason; return false; }
                Sprite current = target.Renderer.sprite;
                if (current == null || !(bool)current || current.Pointer != target.FramePointers[0])
                { reason = target.Id + ": renderer is not on frames[0] at Begin"; return false; }
            }
            _begun = true;
            _begin = scaledTime;
            _lastSample = scaledTime;
            _phase = 0;
            Log("[PalaceR6Motion] armed: scaledTime=" + scaledTime.ToString("F3") + " root=" + Ptr(_rootPointer)
                + " scene=" + _rootSceneHandle + " targets=" + TargetCount + " framesEach=" + FrameCount
                + " frameSeconds=" + FrameSeconds.ToString("F3") + " loopSeconds=" + LoopSeconds.ToString("F3")
                + " phase=0; six renderers verified on phase0 (no write); clock source is caller's scaled Time.time only");
            return true;
        }
        catch (Exception error)
        {
            reason = "R6 motion begin fault: " + error.GetType().Name + ": " + error.Message;
            return false;
        }
    }

    /// <summary>
    /// Root 既有 Depth.Tick 的 owner/camera 门全部通过后每帧调用，传当前 Unity Time.time（scaled；暂停即冻结）。
    /// phase 未变 → 不写；相位改变 → 先整体验证（root identity/sameScene/active、六 renderer identity/child/scene、
    /// 8 帧有效、当前 sprite 就是上次本 owner 值）再写六 renderer.sprite 并读回 pointer 核对。
    /// 非有限/时间倒退/身份漂移/外来写入/读回不符 → false+reason，由 Root 原有 Clear 清当前自有场景；
    /// 不重建、不重 arm、不覆写修复、不逐帧补跑。
    /// </summary>
    internal bool Tick(float scaledTime, out string reason)
    {
        reason = null;
        try
        {
            if (_closed) { reason = "clock already closed"; return false; }
            if (!_begun) { reason = "tick before Begin is an owner error"; return false; }
            if (!float.IsFinite(scaledTime)) { reason = "scaled time is not finite"; return false; }
            if (scaledTime < _lastSample)
            {
                reason = "scaled clock regressed: " + scaledTime.ToString("R") + " < previous " + _lastSample.ToString("R")
                    + "; no reset/clamp/fallback";
                return false;
            }
            if (!TryPhase(_begin, scaledTime, out int phase, out string phaseReason))
            { reason = "phase rejected: " + phaseReason; return false; }
            if (phase == _phase) { _lastSample = scaledTime; return true; }

            if (!VerifyRoot(true, out string rootReason)) { reason = rootReason; return false; }
            for (int i = 0; i < TargetCount; i++)
            {
                Target target = _targets[i];
                if (target == null) { reason = "target " + i + " (" + Ids[i] + ") not bound"; return false; }
                if (!VerifyTarget(target, out string targetReason)) { reason = targetReason; return false; }
                Sprite last = target.Frames[_phase];
                Sprite current = target.Renderer.sprite;
                if (current == null || !(bool)current || current.Pointer != last.Pointer)
                {
                    reason = target.Id + ": current sprite is not this owner's phase-" + _phase
                        + " write; foreign write rejected without overwrite/repair";
                    return false;
                }
            }
            for (int i = 0; i < TargetCount; i++)
            {
                Target target = _targets[i];
                target.Renderer.sprite = target.Frames[phase];
                Sprite readback = target.Renderer.sprite;
                if (readback == null || !(bool)readback || readback.Pointer != target.FramePointers[phase])
                { reason = target.Id + ": sprite readback rejected after write to phase " + phase; return false; }
            }
            _phase = phase;
            _lastSample = scaledTime;
            if (_phaseLogs < MaxPhaseLogs) LogPhaseChange(scaledTime, phase);
            return true;
        }
        catch (Exception error)
        {
            reason = "R6 motion tick fault: " + error.GetType().Name + ": " + error.Message;
            return false;
        }
    }

    /// <summary>只关门并清 managed 借引用（closed 不可重开）：不销毁任何 Unity 对象，48 帧 Sprite/Texture 仍归 Root 清理清单。</summary>
    internal void Close()
    {
        if (_closed) return;
        _closed = true;
        _begun = false;
        for (int i = 0; i < TargetCount; i++) _targets[i] = null;
        _root = null;
    }

    /// <summary>
    /// 纯 phase 函数（无状态、无分配）：phase = floor(((now - begin) mod 4s) / .5s)，结果 0..7。
    /// begin/now 必须有限；now 早于 begin（delta 为负）即拒绝——不 clamp、不 reset、不使用替代时钟；Root 负责 CLR 边界检查。
    /// </summary>
    internal static bool TryPhase(float begin, float now, out int phase, out string reason)
    {
        phase = 0;
        reason = null;
        if (!float.IsFinite(begin)) { reason = "begin is not finite"; return false; }
        if (!float.IsFinite(now)) { reason = "now is not finite"; return false; }
        double elapsedSeconds = (double)now - begin;
        if (!(elapsedSeconds >= 0d))
        {
            reason = "now is earlier than begin (delta " + elapsedSeconds.ToString("R") + "s); no clamp/reset";
            return false;
        }
        double loopSeconds = elapsedSeconds % LoopSeconds;
        double index = Math.Floor(loopSeconds / FrameSeconds);
        if (!(index >= 0d) || !(index < FrameCount))
        { reason = "phase index out of range: " + index.ToString("R"); return false; }
        phase = (int)index;
        return true;
    }

    /// <summary>root identity/sameScene；Tick 变更前与 Begin 还要求 activeInHierarchy。不重选、不重建。</summary>
    private bool VerifyRoot(bool requireActive, out string reason)
    {
        reason = null;
        if (_root == null) { reason = "owned root reference unavailable"; return false; }
        if (!(bool)_root) { reason = "owned root destroyed"; return false; }
        if (_root.Pointer != _rootPointer) { reason = "owned root replaced"; return false; }
        if (_root.scene.handle != _rootSceneHandle) { reason = "owned root scene changed"; return false; }
        if (requireActive && !_root.activeInHierarchy) { reason = "owned root is not active in hierarchy"; return false; }
        return true;
    }

    /// <summary>renderer identity/sameScene/child + 8 帧引用与冻结 pointer 全部有效；任一漂移即 false（不修复）。</summary>
    private bool VerifyTarget(Target target, out string reason)
    {
        reason = null;
        SpriteRenderer renderer = target.Renderer;
        if (renderer == null || !(bool)renderer) { reason = target.Id + ": renderer null/destroyed"; return false; }
        if (renderer.Pointer != target.RendererPointer) { reason = target.Id + ": renderer replaced"; return false; }
        GameObject go = renderer.gameObject;
        if (go == null || !(bool)go) { reason = target.Id + ": renderer GameObject unavailable"; return false; }
        if (go.scene.handle != _rootSceneHandle) { reason = target.Id + ": renderer scene changed"; return false; }
        Transform transform = renderer.transform;
        if (transform == null || !transform.IsChildOf(_root.transform))
        { reason = target.Id + ": renderer is no longer a child of the owned root"; return false; }
        Sprite[] frames = target.Frames;
        IntPtr[] pointers = target.FramePointers;
        if (frames == null || frames.Length != FrameCount || pointers == null || pointers.Length != FrameCount)
        { reason = target.Id + ": frame table unavailable"; return false; }
        for (int i = 0; i < FrameCount; i++)
        {
            Sprite frame = frames[i];
            if (frame == null || !(bool)frame || frame.Pointer == IntPtr.Zero || frame.Pointer != pointers[i])
            { reason = target.Id + ": frame " + i + " invalid/replaced"; return false; }
        }
        return true;
    }

    /// <summary>相位变更日志仅首 16 条（含六 renderer 实际 sprite 读回 bool/pointer，便于实机核 0→7→0）；预算耗尽后静默继续切换。</summary>
    private void LogPhaseChange(float scaledTime, int phase)
    {
        _phaseLogs++;
        double elapsed = (double)scaledTime - _begin;
        string readbacks = "";
        for (int i = 0; i < TargetCount; i++)
        {
            Target target = _targets[i];
            Sprite current = target.Renderer.sprite;
            bool ok = current != null && (bool)current && current.Pointer == target.FramePointers[phase];
            readbacks += (i == 0 ? "" : "; ") + target.Id + "=" + (ok ? "1" : "0") + "@" + Ptr(current);
        }
        Log("[PalaceR6Motion] phase=" + phase + " scaledTime=" + scaledTime.ToString("F3")
            + " elapsed=" + elapsed.ToString("F3") + "s loopSeconds=" + LoopSeconds.ToString("F3")
            + " readback6=[" + readbacks + "]"
            + (_phaseLogs >= MaxPhaseLogs
                ? "; phase-log budget " + MaxPhaseLogs + "/" + MaxPhaseLogs + " reached, later phase changes apply silently"
                : ""));
    }

    private static string Ptr(IntPtr pointer) => pointer == IntPtr.Zero ? "none" : pointer.ToInt64().ToString();

    private static string Ptr(Sprite sprite) => sprite == null ? "none" : Ptr(sprite.Pointer);

    private static void Log(string message) => PalaceCourtVisuals.Log(message);
}
