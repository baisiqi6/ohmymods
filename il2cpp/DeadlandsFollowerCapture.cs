// 死地骑士弩手随从·行走跳帧有界录制（issue-79 诊断修正：只取证，不修行为）。
//
// 触发：F5 → 骑士页临时按钮只 Arm；关面板恢复游戏后由 ModPanel.LateUpdate 调 SampleLate。
//       锚点唯一来源 = 原生主视口 0（CameraMarshaller.InstExists → Inst.GetCamera(0)；本地双人同样固定 0），
//       解析不到本次以 primary-viewport-unresolved 结束，绝不回退 Camera.main/玩家位置；
//       随后经 UnitScanCache 一次扫描，在“可绑定的合格随从”里选离锚点最近者；无合格目标结束、绝不重扫。
// 身份：绑定 Archer/Knight(pointer+GO)/Animator/Mover/body(animator 同 GO renderer) 指针；任一失效/替换立即收尾；
//       逐样本复核队籍、死地风格、弩手 marker、北境、存活与 world 归属，失配即停且不重选。
// 生命周期：既有 Archer.OnDisable 清理入口单点调用 NotifyDisabled 只置失效标志（不写盘、自隔离异常），
//       由下一次 SampleLate 以 target-disabled 正常收尾；同一帧 disable→enable 同样停止。不依赖 Hero 模块 life 账本。
// 预算：8 秒游戏时间 / 1024 样本 / 30 秒真实时间上限（暂停不耗样本但目标与真实上限仍每帧复核）；事件 64 条满后只计数；
//       停用/换队/换世界/Animator/Mover/body 变化立即收尾并清引用；结束后入口仅布尔早退。
// 输出：一份有界 CSV（anchor_kind=native-viewport-0、state_short、state.speed/speedMultiplier、animator.enabled、
//       null sprite 样本计数）+ 一条摘要日志（文件路径只记一次）；写盘失败只报一次并清会话，无重试。
// 只读：不写 controller/Speed/sprite/scale/AI/存档，不注入组件、不新增逐帧扫描。
// 已知盲区（合同 F1'）：ApplyFollowerSkinTo 的 5s 巡检/面板重派调用方没有 style-before/after
//       标签，其实际 controller 写入由 ctl-write-before/after 窄读点覆盖（未重排 Apply 控制流）。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>一次录制会话的只读采样器：不写任何游戏状态，失败只结束本次并记一条日志。</summary>
internal static class DeadlandsFollowerCapture
{
    internal const int MaxSamples = 1024;
    internal const int MaxEvents = 64;
    private const int MaxNameCache = 32;
    private const int MaxSpriteIds = 256;
    private const float GameTimeLimitSeconds = 8f;
    private const float RealTimeLimitSeconds = 30f;
    private const float MovingSpeedThreshold = 0.005f;   // 原生 Stand/Walk 阈值
    // same-state 大幅回退定义保持 >0.5：仅同 controllerId 且同 full state hash 的连续样本比较；
    // 正常跨圈 1.98→2.01 是递增；0→0/小回退不构成“无重启”证据。
    private const float PhaseRestartDrop = 0.5f;

    private static readonly int HashSpeed = Animator.StringToHash("Speed");

    private enum Phase : byte { Idle, Armed, Recording, Finished }

    private enum EKind : byte
    {
        ConvertBefore, PostfixEntry, StyleBefore, StyleAfter,
        ControllerWriteBefore, ControllerWriteAfter, BoostWrite, RestoreWrite,
    }

    private static readonly string[] EventLabels =
    {
        "convert-before", "postfix-entry", "style-before", "style-after",
        "ctl-write-before", "ctl-write-after", "boost-write", "restore-write",
    };

    private struct Sample
    {
        internal int Frame;
        internal float Time;
        internal float Dt;
        internal int StateHash;
        internal int StateShortHash;
        internal float RawNormalized;
        internal float StateLength;
        internal float StateSpeed;
        internal float StateSpeedMultiplier;
        internal int NextHash;
        internal float NextNormalized;
        internal bool InTransition;
        internal float SpeedParam;
        internal float AnimatorSpeed;
        internal bool AnimatorEnabled;
        internal int ControllerId;
        internal int SpriteId;
        internal float PosX;
        internal float ScaleY;
        internal bool RendererEnabled;
        internal bool RendererActive;
        internal bool HasObserver;
        internal bool Boosting;
        internal bool Captured;
        internal int AttackStateHash;
    }

    private struct CaptureEvent
    {
        internal int Frame;
        internal float Time;
        internal EKind Kind;
        internal int Variant;
        internal int ControllerId;
        internal int StateHash;
        internal float RawNormalized;
        internal float SpeedParam;
        internal float AnimatorSpeed;
        internal int SpriteId;
        internal float SpeedBefore;
        internal float SpeedAfter;
        internal bool Boosting;
        internal bool Captured;
    }

    private static readonly Sample[] Samples = new Sample[MaxSamples];
    private static readonly CaptureEvent[] Events = new CaptureEvent[MaxEvents];
    private static readonly Dictionary<long, string> StateNames = new Dictionary<long, string>(MaxNameCache);
    private static readonly Dictionary<long, byte> StateClass = new Dictionary<long, byte>(MaxNameCache);
    private static readonly Dictionary<int, string> ControllerNames = new Dictionary<int, string>(MaxNameCache);
    private static readonly Dictionary<int, string> SpriteNames = new Dictionary<int, string>(MaxNameCache);
    private static readonly HashSet<int> SpriteIds = new HashSet<int>();
    private static readonly HashSet<string> LoggedErrors = new HashSet<string>(StringComparer.Ordinal);

    private static Phase _phase = Phase.Idle;
    private static Archer _target;
    private static IntPtr _targetPtr;
    private static int _targetGoId;
    private static int _knightGoId;
    private static IntPtr _knightPtr;
    private static Animator _animator;
    private static IntPtr _animatorPtr;
    private static SpriteRenderer _body;
    private static IntPtr _bodyPtr;
    private static IntPtr _moverPtr;
    private static IntPtr _worldPtr;
    private static IntPtr _layerPtr;
    private static DeadlandsAnimObserver _observer;
    private static float _startGameTime;
    private static float _startRealTime;
    private static int _lastFrame = -1;
    private static int _sampleCount;
    private static int _eventCount;
    private static int _droppedEvents;
    private static float _speedMin = float.PositiveInfinity;
    private static float _speedMax = float.NegativeInfinity;
    private static float _animSpeedMin = float.PositiveInfinity;
    private static float _animSpeedMax = float.NegativeInfinity;
    private static int _movingSamples;
    private static int _stateSwitches;
    private static int _phaseBackJumps;
    private static int _controllerWrites;
    private static int _controllerNoWrites;
    private static int _rendererDisabledSamples;
    private static int _rendererInactiveSamples;
    private static int _observerSamples;
    private static int _boostingSamples;
    private static int _capturedSamples;
    private static int _boostLocoSamples;
    private static bool _spriteIdsOverflow;
    private static int _nullSpriteSamples;
    private static int _previousStateHash;
    private static int _previousControllerId;
    private static float _previousRaw;
    private static bool _hasPrevious;
    private static int _controllerChanges;
    private static bool _targetDisabled;
    private static string _lastStopReason = "";
    private static string _lastOutputPath = "";
    private static bool _writeErrorLogged;
    private static string _anchorXText = "na";
    private static int _candidateCount;
    private static int _sessionSerial;

    /// <summary>面板显示用状态（只读，不触发任何游戏调用）。</summary>
    internal static string StatusText
    {
        get
        {
            try
            {
                if (_phase == Phase.Armed) return "等待关闭面板并恢复游戏";
                if (_phase == Phase.Recording)
                {
                    float elapsed = Time.time - _startGameTime;
                    return "记录中 " + _sampleCount + "/" + MaxSamples + " 样本 · "
                        + elapsed.ToString("F1", CultureInfo.InvariantCulture) + "/8.0 秒";
                }
                if (_phase == Phase.Finished)
                    return _lastStopReason.Length > 0 ? "已结束：" + _lastStopReason : "已结束";
                return "未记录";
            }
            catch
            {
                return "未记录";
            }
        }
    }

    internal static string HelpText =>
        "临时诊断（issue-79）：点击后关闭面板并恢复游戏，以原生主视口 0 为锚点（本地双人同样固定视口 0），"
        + "只选最近的合格死地骑士随从记录 8 秒；只读取，不改玩法、不修行为。";

    // ---- 测试/自检只读面（不改变生产语义）----
    internal static int SampleCount => _sampleCount;
    internal static int EventCount => _eventCount;
    internal static int DroppedEvents => _droppedEvents;
    internal static int StateSwitches => _stateSwitches;
    internal static int PhaseBackJumps => _phaseBackJumps;
    internal static string LastStopReason => _lastStopReason;
    internal static string LastOutputPath => _lastOutputPath;

    /// <summary>按钮入口：只 Arm，不开始计时、不修改配置；重复点击/记录中忽略。</summary>
    internal static void ArmFromPanel()
    {
        try
        {
            if (_phase == Phase.Recording) return;
            if (_phase == Phase.Armed) return;
            _phase = Phase.Armed;
            _lastStopReason = "";
            // 本次会话元数据只在这里重置：早退的下一轮不得显示/导出上一轮的路径、锚点与候选计数。
            _lastOutputPath = "";
            _anchorXText = "na";
            _candidateCount = 0;
            _targetDisabled = false;
            Log("[FollowerCapture] armed: close the panel and resume the game; anchor=native-viewport-0, "
                + "one target is selected once");
        }
        catch
        {
        }
    }

    /// <summary>每帧末尾由 ModPanel.LateUpdate 调用。非 Armed/Recording 时只做布尔早退。</summary>
    internal static void SampleLate(bool panelShown)
    {
        if (_phase != Phase.Armed && _phase != Phase.Recording) return;
        try
        {
            if (_phase == Phase.Armed)
            {
                if (panelShown || Time.timeScale <= 0f) return;   // 等关面板 + 真正恢复游戏
                BeginRecording();
                return;
            }
            // OnDisable 通知（含同帧 disable→enable）优先收尾：不依赖 activeInHierarchy 仍为假。
            if (_targetDisabled)
            {
                Finish("target-disabled");
                return;
            }
            if (Time.frameCount == _lastFrame) return;            // 每帧至多一个样本
            _lastFrame = Time.frameCount;
            string invalid = CheckTarget();
            if (invalid != null)
            {
                Finish(invalid);
                return;
            }
            if (Time.time - _startGameTime >= GameTimeLimitSeconds)
            {
                Finish("game-8s");
                return;
            }
            if (Time.realtimeSinceStartup - _startRealTime >= RealTimeLimitSeconds)
            {
                Finish("real-30s");
                return;
            }
            if (_sampleCount >= MaxSamples)
            {
                Finish("samples-1024");
                return;
            }
            if (Time.deltaTime <= 0f) return;                     // 暂停不耗样本；目标/真实上限已在上方复核
            TakeSample();
            if (_sampleCount >= MaxSamples) Finish("samples-1024");
        }
        catch (Exception e)
        {
            Fail("sample", e);
        }
    }

    // ============================================================
    // 选择（一次扫描，绝不重扫）
    // ============================================================

    private static void BeginRecording()
    {
        // world/layer 先解析：候选资格与绑定后的逐样本复核都以此为界。
        Managers managers = Managers.Inst;
        World world = managers != null ? managers.world : null;
        Transform layer = world != null ? world.gameLayer : null;
        if (world == null || layer == null)
        {
            Finish("world-unresolved");
            return;
        }

        // 唯一锚点：原生主视口 0（本地双人同样固定 0）；不做 Camera.main / 玩家位置回退。
        if (!TryReadPrimaryViewportX(out float anchorX))
        {
            Finish("primary-viewport-unresolved");
            return;
        }
        _anchorXText = F(anchorX);

        // 一次扫描：只在“可绑定的合格随从”里选离锚点最近者；坏掉的近邻直接跳过，不中途放弃。
        Archer[] archers = UnitScanCache.GetArchers(0f);
        Archer best = null;
        Animator bestAnimator = null;
        SpriteRenderer bestBody = null;
        float bestDistance = float.MaxValue;
        int candidates = 0;
        if (archers != null)
        {
            for (int i = 0; i < archers.Length; i++)
            {
                Archer archer = archers[i];
                if (CandidateFailure(archer, layer, out Animator animator, out SpriteRenderer body) != null) continue;
                candidates++;
                float distance = Mathf.Abs(archer.transform.position.x - anchorX);
                if (!float.IsFinite(distance)) continue;
                if (best == null || distance < bestDistance)
                {
                    best = archer;
                    bestDistance = distance;
                    bestAnimator = animator;
                    bestBody = body;
                }
            }
        }
        _candidateCount = candidates;
        if (best == null)
        {
            Finish("no-target");
            return;
        }

        // winner 的 animator/body 已由资格门确认并捕获；这里只补身份（池复用判别的最小面）。
        int targetGoId = best.gameObject.GetInstanceID();
        IntPtr targetPtr = best.Pointer;
        Knight knight = best._knight;
        int knightGoId = knight.gameObject.GetInstanceID();
        IntPtr knightPtr = knight.Pointer;
        if (targetPtr == IntPtr.Zero || targetGoId == 0 || knightPtr == IntPtr.Zero || knightGoId == 0)
        {
            Finish("target-id-unresolved");
            return;
        }

        // 先清会话引用再写入；anchor/candidate 元数据不属于 ClearSession（见该方法注释），不会被擦掉。
        ClearSession();
        _target = best;
        _targetPtr = targetPtr;
        _targetGoId = targetGoId;
        _knightPtr = knightPtr;
        _knightGoId = knightGoId;
        _animator = bestAnimator;
        _animatorPtr = bestAnimator.Pointer;
        _body = bestBody;
        _bodyPtr = bestBody.Pointer;
        _moverPtr = best._mover.Pointer;
        _worldPtr = world.Pointer;
        _layerPtr = layer.Pointer;
        _observer = ObserverTypeReady() ? bestAnimator.gameObject.GetComponent<DeadlandsAnimObserver>() : null;
        _startGameTime = Time.time;
        _startRealTime = Time.realtimeSinceStartup;
        _lastFrame = -1;
        _targetDisabled = false;
        _phase = Phase.Recording;
        Log("[FollowerCapture] recording go=" + targetGoId + " knight_go=" + knightGoId
            + " candidates=" + candidates + " anchor=native-viewport-0 x=" + _anchorXText
            + " body=animator-go sprite_renderer");
    }

    /// <summary>
    /// 唯一正规锚点：原生主视口 0（CameraMarshaller；本地双人同样固定视口 0）。
    /// 不串联 Camera.main、不读玩家位置、无任何 fallback；对象/激活/transform/X 有限任一不成立即不可用。
    /// </summary>
    private static bool TryReadPrimaryViewportX(out float x)
    {
        x = 0f;
        try
        {
            if (!CameraMarshaller.InstExists) return false;
            CameraMarshaller marshaller = CameraMarshaller.Inst;
            if (marshaller == null) return false;
            MainCamera viewport = marshaller.GetCamera(0);
            if (viewport == null || viewport.gameObject == null) return false;
            if (!viewport.gameObject.activeInHierarchy) return false;
            Transform viewportTransform = viewport.transform;
            if (viewportTransform == null) return false;
            float value = viewportTransform.position.x;
            if (!float.IsFinite(value)) return false;
            x = value;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 选人与逐样本复核共用的资格门（读失败按不合格处理）：同 world/layer、存活、已解析死地风格、
    /// 非独立弩手 marker、非有效北境、原生 Animator/Mover/同 GO body 有效。
    /// 返回 null 表示合格（并输出 animator/body），否则返回窄代码；选人时跳过，采样时映射为 stop reason。
    /// </summary>
    private static string CandidateFailure(Archer archer, Transform layer, out Animator animator, out SpriteRenderer body)
    {
        animator = null;
        body = null;
        try
        {
            if (archer == null || archer.gameObject == null || !archer.gameObject.activeInHierarchy) return "inactive";
            Transform self = archer.transform;
            if (self == null || layer == null || !self.IsChildOf(layer)) return "world";
            Knight knight = archer._knight;
            if (knight == null || knight.gameObject == null) return "team";
            if (!PatchRoles_KnightStyle.TryGetResolvedStyleIndex(knight, out int style)
                || style != PatchRoles_DeadlandsPowers.DeadlandsStyleIndex) return "style";
            if (PatchRoles_Crossbowman.IsCrossbowman(archer)) return "crossbow";        // 独立守墙弩手（marker 群体）
            if (PatchRoles_NorseSquad.IsNorseArcherInstance(archer)) return "norse";    // 有效风格是北境，单看骑士风格会选错
            Damageable damageable = archer._damageable;
            if (damageable == null || damageable.isDead) return "dead";
            Animator current = archer._animator;                                        // 只认 Archer._animator（显式身份）
            if (current == null || current.gameObject == null) return "animator";
            SpriteRenderer currentBody = current.gameObject.GetComponent<SpriteRenderer>();
            if (currentBody == null || currentBody.gameObject == null) return "body";    // 禁止盲取子 renderer
            if (archer._mover == null) return "mover";
            animator = current;
            body = currentBody;
            return null;
        }
        catch
        {
            return "read-error";
        }
    }

    /// <summary>注入的 DeadlandsAnimObserver 必须已由攻击路径注册才可查询；未注册记为 none，
    /// 不为诊断注册类型、不 AddComponent，也不让第一次纯走路录制因此失败。</summary>
    private static bool ObserverTypeReady()
    {
        try
        {
            return ClassInjector.IsTypeRegisteredInIl2Cpp(typeof(DeadlandsAnimObserver));
        }
        catch
        {
            return false;
        }
    }

    // ============================================================
    // 采样（只读；身份 + world 每帧核对）
    // ============================================================

    private static string CheckTarget()
    {
        if (_target == null || _target.gameObject == null || !_target.gameObject.activeInHierarchy)
            return "target-inactive";
        if (_target.Pointer != _targetPtr || _target.gameObject.GetInstanceID() != _targetGoId)
            return "target-identity-changed";
        Knight knight = _target._knight;
        if (knight == null || knight.gameObject == null
            || knight.Pointer != _knightPtr || knight.gameObject.GetInstanceID() != _knightGoId)
            return "team-changed";
        if (_animator == null || _animator.gameObject == null)
            return "animator-changed";
        if (_target._animator == null || _target._animator.Pointer != _animatorPtr)
            return "animator-changed";
        IntPtr moverPtr = _target._mover != null ? _target._mover.Pointer : IntPtr.Zero;
        if (moverPtr != _moverPtr)
            return "mover-changed";
        if (_body == null || _body.gameObject == null || _body.Pointer != _bodyPtr)
            return "body-changed";
        SpriteRenderer currentBody;
        try
        {
            currentBody = _animator.gameObject.GetComponent<SpriteRenderer>();
        }
        catch
        {
            currentBody = null;
        }
        if (currentBody == null || currentBody.Pointer != _bodyPtr)
            return "body-changed";
        Managers managers = Managers.Inst;
        World world = managers != null ? managers.world : null;
        Transform layer = world != null ? world.gameLayer : null;
        if (world == null || layer == null) return "world-changed";
        if (world.Pointer != _worldPtr || layer.Pointer != _layerPtr) return "world-changed";
        // 资格复核（与选人同一门）：死地风格、弩手 marker、北境、存活、world 归属、原生引用。
        string failure = CandidateFailure(_target, layer, out _, out _);
        return failure == null ? null : MapTargetFailure(failure);
    }

    /// <summary>资格门窄代码 → 现场 stop reason（既有 reason 名保持不变，新增项各自显式）。</summary>
    private static string MapTargetFailure(string failure)
    {
        switch (failure)
        {
            case "inactive": return "target-inactive";
            case "world": return "world-changed";
            case "team": return "team-changed";
            case "style": return "style-changed";
            case "crossbow": return "crossbow-changed";
            case "norse": return "norse-changed";
            case "dead": return "target-dead";
            case "animator": return "animator-changed";
            case "body": return "body-changed";
            case "mover": return "mover-changed";
            default: return "eligibility-read-error";
        }
    }

    /// <summary>
    /// 既有 Archer.OnDisable 清理入口的只读通知（PatchRoles_DeadlandsPowers 单点调用）：
    /// 只标记当前已绑定目标（pointer + GO）失效，不写盘、不读游戏状态；disable→enable 同帧也停止。
    /// 完全自隔离（内部吞掉一切异常）：诊断自身绝不打乱原清理路径或异常传播。
    /// </summary>
    internal static void NotifyDisabled(Archer archer)
    {
        try
        {
            if (_phase != Phase.Recording || archer == null || _target == null) return;
            if (archer.Pointer != _targetPtr) return;
            GameObject go = archer.gameObject;
            if (go == null || go.GetInstanceID() != _targetGoId) return;
            _targetDisabled = true;
        }
        catch
        {
        }
    }

    private static void TakeSample()
    {
        AnimatorStateInfo state = _animator.GetCurrentAnimatorStateInfo(0);
        AnimatorStateInfo next = _animator.GetNextAnimatorStateInfo(0);
        bool inTransition = _animator.IsInTransition(0);
        float speedParam = _animator.GetFloat(HashSpeed);
        float animSpeed = _animator.speed;
        bool animatorEnabled = _animator.enabled;
        RuntimeAnimatorController controller = _animator.runtimeAnimatorController;
        int controllerId = controller != null ? controller.GetInstanceID() : 0;
        Sprite sprite = _body.sprite;
        int spriteId = sprite != null ? sprite.GetInstanceID() : 0;

        // 名字只少量缓存：state 按 controller+state 组合首次见到时读取（同 state 换 override 会重读），
        // controller/sprite 按各自 id；多 clip 标 blend、附 clip.length。null sprite（id 0）不入表。
        long stateKey = StateKey(controllerId, state.fullPathHash);
        CacheStateName(controllerId, state.fullPathHash);
        CacheControllerName(controllerId, controller);
        if (spriteId != 0) CacheSpriteName(spriteId, sprite);

        DeadlandsAnimObserver o = _observer != null && _observer.gameObject != null ? _observer : null;
        int idx = _sampleCount;
        Samples[idx].Frame = Time.frameCount;
        Samples[idx].Time = Time.time;
        Samples[idx].Dt = Time.deltaTime;
        Samples[idx].StateHash = state.fullPathHash;
        Samples[idx].StateShortHash = state.shortNameHash;
        Samples[idx].RawNormalized = state.normalizedTime;
        Samples[idx].StateLength = state.length;
        Samples[idx].StateSpeed = state.speed;
        Samples[idx].StateSpeedMultiplier = state.speedMultiplier;
        Samples[idx].NextHash = next.fullPathHash;
        Samples[idx].NextNormalized = next.normalizedTime;
        Samples[idx].InTransition = inTransition;
        Samples[idx].SpeedParam = speedParam;
        Samples[idx].AnimatorSpeed = animSpeed;
        Samples[idx].AnimatorEnabled = animatorEnabled;
        Samples[idx].ControllerId = controllerId;
        Samples[idx].SpriteId = spriteId;
        Samples[idx].PosX = _target.transform.position.x;
        Samples[idx].ScaleY = _target.transform.localScale.y;
        Samples[idx].RendererEnabled = _body.enabled;
        Samples[idx].RendererActive = _body.gameObject.activeInHierarchy;
        Samples[idx].HasObserver = o != null;
        Samples[idx].Boosting = o != null && o.Boosting;
        Samples[idx].Captured = o != null && o.Captured;
        Samples[idx].AttackStateHash = o != null ? o.AttackStateHash : 0;
        _sampleCount++;

        if (speedParam < _speedMin) _speedMin = speedParam;
        if (speedParam > _speedMax) _speedMax = speedParam;
        if (animSpeed < _animSpeedMin) _animSpeedMin = animSpeed;
        if (animSpeed > _animSpeedMax) _animSpeedMax = animSpeed;
        if (Mathf.Abs(speedParam) > MovingSpeedThreshold) _movingSamples++;
        if (!_body.enabled) _rendererDisabledSamples++;
        if (!_body.gameObject.activeInHierarchy) _rendererInactiveSamples++;
        if (o != null)
        {
            _observerSamples++;
            if (o.Boosting) _boostingSamples++;
            if (o.Captured) _capturedSamples++;
            if (o.Boosting && StateClass.TryGetValue(stateKey, out byte cls) && (cls == 2 || cls == 3))
                _boostLocoSamples++;
        }
        if (_hasPrevious)
        {
            if (state.fullPathHash != _previousStateHash) _stateSwitches++;
            // same-state 回退只在同 controller 且同 full hash 的连续样本上比较；controller 变更单独计数。
            else if (controllerId == _previousControllerId
                && state.normalizedTime < _previousRaw - PhaseRestartDrop) _phaseBackJumps++;
            if (controllerId != _previousControllerId) _controllerChanges++;
        }
        _previousStateHash = state.fullPathHash;
        _previousControllerId = controllerId;
        _previousRaw = state.normalizedTime;
        _hasPrevious = true;
        if (spriteId == 0)
        {
            _nullSpriteSamples++;   // null sprite 不是有效贴图：不计入 distinct_sprites
        }
        else if (!SpriteIds.Contains(spriteId))
        {
            if (SpriteIds.Count < MaxSpriteIds) SpriteIds.Add(spriteId);
            else _spriteIdsOverflow = true;
        }
    }

    /// <summary>clip 身份按 (controllerId, stateHash) 组合缓存：同 state 换 override 得到新键并重读，
    /// 不会沿用旧 clip；每个组合只在首次见到时调用 GetCurrentAnimatorClipInfo，不逐帧重读。</summary>
    private static void CacheStateName(int controllerId, int stateHash)
    {
        if (stateHash == 0) return;
        long key = StateKey(controllerId, stateHash);
        if (StateNames.ContainsKey(key) || StateNames.Count >= MaxNameCache) return;
        string name = "unknown";
        try
        {
            var clips = _animator.GetCurrentAnimatorClipInfo(0);
            int count = clips != null ? clips.Length : 0;
            if (count == 0) name = "none";
            else
            {
                AnimationClip clip = clips[0].clip;
                string clipName = clip != null ? clip.name : "null";
                name = count == 1
                    ? clipName + "(len=" + F(clip != null ? clip.length : 0f) + ")"
                    : "blend" + count + ":" + clipName;
            }
        }
        catch
        {
            name = "clipinfo-error";
        }
        StateNames[key] = name;
        StateClass[key] = ClassifyState(name);
    }

    private static long StateKey(int controllerId, int stateHash)
    {
        return ((long)controllerId << 32) | (uint)stateHash;
    }

    private static string StateKeyText(long key)
    {
        return (int)(uint)(key & 0xffffffffL) + "@" + (int)(key >> 32);
    }

    private static byte ClassifyState(string name)
    {
        if (string.IsNullOrEmpty(name)) return 0;
        string lower = name.ToLowerInvariant();
        if (lower.Contains("walk")) return 2;
        if (lower.Contains("run")) return 3;
        if (lower.Contains("stand") || lower.Contains("idle")) return 1;
        return 4;
    }

    private static void CacheControllerName(int id, RuntimeAnimatorController controller)
    {
        if (id == 0 || ControllerNames.ContainsKey(id) || ControllerNames.Count >= MaxNameCache) return;
        string name;
        try
        {
            name = controller != null && !string.IsNullOrEmpty(controller.name) ? controller.name : "unnamed";
        }
        catch
        {
            name = "name-error";
        }
        ControllerNames[id] = name;
    }

    private static void CacheSpriteName(int id, Sprite sprite)
    {
        if (SpriteNames.ContainsKey(id) || SpriteNames.Count >= MaxNameCache) return;
        string name;
        try
        {
            name = sprite != null && !string.IsNullOrEmpty(sprite.name) ? sprite.name : "null";
        }
        catch
        {
            name = "name-error";
        }
        SpriteNames[id] = name;
    }

    // ============================================================
    // 事件读点（由既有 patch 调用；只对当前目标记录，绝不改原函数）
    // ============================================================

    internal static void OnConvertBefore(Archer archer, bool hunter)
    {
        EventForTarget(archer, EKind.ConvertBefore, hunter ? 1 : 0);
    }

    internal static void OnPostfixEntry(Archer archer, bool hunter)
    {
        EventForTarget(archer, EKind.PostfixEntry, hunter ? 1 : 0);
    }

    internal static void OnStyleBefore(Archer archer)
    {
        EventForTarget(archer, EKind.StyleBefore, 0);
    }

    internal static void OnStyleAfter(Archer archer)
    {
        EventForTarget(archer, EKind.StyleAfter, 0);
    }

    internal static void OnControllerWriteBefore(Archer archer)
    {
        EventForTarget(archer, EKind.ControllerWriteBefore, 0);
    }

    internal static void OnControllerWriteAfter(Archer archer)
    {
        EventForTarget(archer, EKind.ControllerWriteAfter, 0);
        if (_phase == Phase.Recording && archer != null && archer.Pointer == _targetPtr) _controllerWrites++;
    }

    internal static void OnControllerNoWrite(Archer archer)
    {
        if (_phase == Phase.Recording && archer != null && archer.Pointer == _targetPtr) _controllerNoWrites++;
    }

    /// <summary>HandleAnimTrigger / RestoreObserver 的既有 Animator.speed 写点（原行为不变）。</summary>
    internal static void OnAnimatorSpeedWrite(Animator animator, DeadlandsAnimObserver observer,
        int triggerHash, float before, float after, bool restore)
    {
        if (_phase != Phase.Recording) return;
        try
        {
            if (observer == null || observer.Archer == null || observer.Archer.Pointer != _targetPtr) return;
            if (animator == null || animator.Pointer != _animatorPtr) return;   // 事件必须来自已绑定的 Animator
            if (observer.gameObject != null) _observer = observer;               // 观察者出现时惰性缓存一次
            if (restore) RecordEvent(EKind.RestoreWrite, 0, before, after);
            else RecordEvent(EKind.BoostWrite, triggerHash, before, after);
        }
        catch
        {
        }
    }

    private static void EventForTarget(Archer archer, EKind kind, int variant)
    {
        if (_phase != Phase.Recording) return;
        try
        {
            if (archer == null || archer.Pointer != _targetPtr) return;
            RecordEvent(kind, variant, 0f, 0f);
        }
        catch
        {
        }
    }

    private static void RecordEvent(EKind kind, int variant, float speedBefore, float speedAfter)
    {
        if (_eventCount >= MaxEvents)
        {
            _droppedEvents++;
            return;
        }
        try
        {
            AnimatorStateInfo state = _animator.GetCurrentAnimatorStateInfo(0);
            RuntimeAnimatorController controller = _animator.runtimeAnimatorController;
            Sprite sprite = _body.sprite;
            DeadlandsAnimObserver o = _observer;
            int idx = _eventCount;
            Events[idx].Frame = Time.frameCount;
            Events[idx].Time = Time.time;
            Events[idx].Kind = kind;
            Events[idx].Variant = variant;
            Events[idx].ControllerId = controller != null ? controller.GetInstanceID() : 0;
            Events[idx].StateHash = state.fullPathHash;
            Events[idx].RawNormalized = state.normalizedTime;
            Events[idx].SpeedParam = _animator.GetFloat(HashSpeed);
            Events[idx].AnimatorSpeed = _animator.speed;
            Events[idx].SpriteId = sprite != null ? sprite.GetInstanceID() : 0;
            Events[idx].SpeedBefore = speedBefore;
            Events[idx].SpeedAfter = speedAfter;
            Events[idx].Boosting = o != null && o.Boosting;
            Events[idx].Captured = o != null && o.Captured;
            _eventCount++;
        }
        catch
        {
            // 取证事件失败不影响原调用路径。
        }
    }

    // ============================================================
    // 收尾与输出
    // ============================================================

    /// <summary>收尾：身份错误在此显式成为 stop reason，不需要对 Finish/Fail 再套兜底 catch。
    /// 输出失败由 WriteOutput 自己的边界处理、日志失败由 Log 吞掉，其余是纯字段清理。</summary>
    private static void Finish(string reason)
    {
        if (_phase != Phase.Armed && _phase != Phase.Recording) return;
        _lastStopReason = reason;
        WriteOutput(reason);
        Log(BuildSummaryLine(reason));
        ClearSession();
        _phase = Phase.Finished;
    }

    /// <summary>只清本次会话的引用/计数：anchor/candidate/lastOutputPath 是跨会话元数据，
    /// 由 ArmFromPanel 重置、Begin 写入——这里不清，否则会擦掉 Begin 刚解析的锚点值。</summary>
    private static void ClearSession()
    {
        _target = null;
        _targetPtr = IntPtr.Zero;
        _targetGoId = 0;
        _knightPtr = IntPtr.Zero;
        _knightGoId = 0;
        _animator = null;
        _animatorPtr = IntPtr.Zero;
        _body = null;
        _bodyPtr = IntPtr.Zero;
        _moverPtr = IntPtr.Zero;
        _worldPtr = IntPtr.Zero;
        _layerPtr = IntPtr.Zero;
        _observer = null;
        _lastFrame = -1;
        _sampleCount = 0;
        _eventCount = 0;
        _droppedEvents = 0;
        _speedMin = float.PositiveInfinity;
        _speedMax = float.NegativeInfinity;
        _animSpeedMin = float.PositiveInfinity;
        _animSpeedMax = float.NegativeInfinity;
        _movingSamples = 0;
        _stateSwitches = 0;
        _phaseBackJumps = 0;
        _controllerChanges = 0;
        _controllerWrites = 0;
        _controllerNoWrites = 0;
        _rendererDisabledSamples = 0;
        _rendererInactiveSamples = 0;
        _observerSamples = 0;
        _boostingSamples = 0;
        _capturedSamples = 0;
        _boostLocoSamples = 0;
        _spriteIdsOverflow = false;
        _nullSpriteSamples = 0;
        _targetDisabled = false;
        _previousControllerId = 0;
        _hasPrevious = false;
        StateNames.Clear();
        StateClass.Clear();
        ControllerNames.Clear();
        SpriteNames.Clear();
        SpriteIds.Clear();
    }

    /// <summary>测试/自检复位（清会话与一次性日志标记，不改游戏状态）。</summary>
    internal static void ResetForTests()
    {
        ClearSession();
        _writeErrorLogged = false;
        _lastStopReason = "";
        _lastOutputPath = "";
        _anchorXText = "na";
        _candidateCount = 0;
        LoggedErrors.Clear();
        _phase = Phase.Idle;
    }

    private static void WriteOutput(string reason)
    {
        string path = null;
        try
        {
            string directory = Path.Combine(Paths.ConfigPath, "KingdomEnhancedMod", "Diagnostics");
            Directory.CreateDirectory(directory);
            _sessionSerial++;
            path = Path.Combine(directory,
                "follower-capture-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)
                + "-" + _sessionSerial + ".csv");
            var sb = new StringBuilder(64 * 1024);
            BuildCsv(sb, reason);
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            _lastOutputPath = path;
        }
        catch (Exception e)
        {
            _lastOutputPath = "";
            if (!_writeErrorLogged)
            {
                _writeErrorLogged = true;
                LogError("[FollowerCapture] csv write failed (recording discarded, no retry): "
                    + (path != null ? path + " :: " : "") + e.GetType().Name + " " + e.Message);
            }
        }
    }

    private static void BuildCsv(StringBuilder sb, string reason)
    {
        sb.Append("# DeadlandsFollowerCapture v2 (issue-79 diagnostics; read-only)\n");
        sb.Append("# event variant: convert 0=soldier/1=hunter; boost=trigger hash; others 0\n");
        sb.Append("# anchor_kind=native-viewport-0 (local coop also fixed to viewport 0) anchor_x=")
            .Append(_anchorXText)
            .Append(" candidates=").Append(_candidateCount)
            .Append(" target_go=").Append(_targetGoId).Append(" knight_go=").Append(_knightGoId)
            .Append(" stop_reason=").Append(reason).Append('\n');
        sb.Append("# samples\n");
        sb.Append("frame,time,dt,state_full,state_short,raw_nt,len,next_full,next_nt,transition,speed,"
            + "state_speed,state_speed_mult,anim_speed,animator_enabled,controller,sprite,pos_x,scale_y,"
            + "renderer,renderer_active,observer,boost,captured,attack_state\n");
        for (int i = 0; i < _sampleCount; i++)
        {
            sb.Append(Samples[i].Frame).Append(',').Append(F(Samples[i].Time)).Append(',').Append(F(Samples[i].Dt))
                .Append(',').Append(Samples[i].StateHash).Append(',').Append(Samples[i].StateShortHash)
                .Append(',').Append(F4(Samples[i].RawNormalized))
                .Append(',').Append(F4(Samples[i].StateLength)).Append(',').Append(Samples[i].NextHash)
                .Append(',').Append(F4(Samples[i].NextNormalized)).Append(',').Append(Samples[i].InTransition ? 1 : 0)
                .Append(',').Append(F4(Samples[i].SpeedParam))
                .Append(',').Append(F4(Samples[i].StateSpeed))
                .Append(',').Append(F4(Samples[i].StateSpeedMultiplier))
                .Append(',').Append(F4(Samples[i].AnimatorSpeed))
                .Append(',').Append(Samples[i].AnimatorEnabled ? 1 : 0)
                .Append(',').Append(Samples[i].ControllerId).Append(',').Append(Samples[i].SpriteId)
                .Append(',').Append(F4(Samples[i].PosX)).Append(',').Append(F4(Samples[i].ScaleY))
                .Append(',').Append(Samples[i].RendererEnabled ? 1 : 0)
                .Append(',').Append(Samples[i].RendererActive ? 1 : 0)
                .Append(',').Append(Samples[i].HasObserver ? 1 : 0)
                .Append(',').Append(Samples[i].Boosting ? 1 : 0)
                .Append(',').Append(Samples[i].Captured ? 1 : 0)
                .Append(',').Append(Samples[i].AttackStateHash).Append('\n');
        }
        sb.Append("# events\n");
        sb.Append("frame,time,kind,variant,controller,state_full,raw_nt,speed,anim_speed,sprite,"
            + "before_speed,after_speed,boost,captured\n");
        for (int i = 0; i < _eventCount; i++)
        {
            sb.Append(Events[i].Frame).Append(',').Append(F(Events[i].Time)).Append(',')
                .Append(EventLabels[(int)Events[i].Kind]).Append(',').Append(Events[i].Variant)
                .Append(',').Append(Events[i].ControllerId).Append(',').Append(Events[i].StateHash)
                .Append(',').Append(F4(Events[i].RawNormalized)).Append(',').Append(F4(Events[i].SpeedParam))
                .Append(',').Append(F4(Events[i].AnimatorSpeed)).Append(',').Append(Events[i].SpriteId)
                .Append(',').Append(F4(Events[i].SpeedBefore)).Append(',').Append(F4(Events[i].SpeedAfter))
                .Append(',').Append(Events[i].Boosting ? 1 : 0).Append(',').Append(Events[i].Captured ? 1 : 0)
                .Append('\n');
        }
        sb.Append("# summary\n");
        sb.Append("samples=").Append(_sampleCount).Append('\n');
        sb.Append("events=").Append(_eventCount).Append('\n');
        sb.Append("dropped_events=").Append(_droppedEvents).Append('\n');
        sb.Append("moving_samples=").Append(_movingSamples).Append('\n');
        sb.Append("state_switches=").Append(_stateSwitches).Append('\n');
        sb.Append("phase_back_jumps=").Append(_phaseBackJumps).Append('\n');
        sb.Append("controller_changes=").Append(_controllerChanges).Append('\n');
        sb.Append("controller_writes=").Append(_controllerWrites).Append('\n');
        sb.Append("controller_calls_no_write=").Append(_controllerNoWrites).Append('\n');
        sb.Append("distinct_sprites=").Append(SpriteIds.Count).Append(_spriteIdsOverflow ? "+" : "").Append('\n');
        sb.Append("null_sprite_samples=").Append(_nullSpriteSamples).Append('\n');
        sb.Append("speed_param_range=").Append(Range(_speedMin, _speedMax)).Append('\n');
        sb.Append("anim_speed_range=").Append(Range(_animSpeedMin, _animSpeedMax)).Append('\n');
        sb.Append("observer_samples=").Append(_observerSamples).Append('\n');
        sb.Append("boosting_samples=").Append(_boostingSamples).Append('\n');
        sb.Append("captured_samples=").Append(_capturedSamples).Append('\n');
        sb.Append("boost_locomotion_samples=").Append(_boostLocoSamples).Append('\n');
        sb.Append("renderer_disabled_samples=").Append(_rendererDisabledSamples).Append('\n');
        sb.Append("renderer_inactive_samples=").Append(_rendererInactiveSamples).Append('\n');
        AppendNameList(sb, "controller_names", ControllerNames);
        AppendNameList(sb, "state_names", StateNames, StateKeyText);
        AppendNameList(sb, "sprite_names", SpriteNames);
    }

    /// <summary>导出全部有界缓存项（每张表 ≤ MaxNameCache），不在 8 项处截断——唯一完整 CSV 不套日志精简规则。</summary>
    private static void AppendNameList<TKey>(StringBuilder sb, string key, Dictionary<TKey, string> names,
        Func<TKey, string> formatKey = null)
    {
        sb.Append(key).Append('=');
        bool first = true;
        foreach (KeyValuePair<TKey, string> pair in names)
        {
            if (!first) sb.Append('|');
            first = false;
            sb.Append(formatKey != null ? formatKey(pair.Key) : pair.Key.ToString()).Append(':').Append(pair.Value);
        }
        sb.Append('\n');
    }

    private static string BuildSummaryLine(string reason)
    {
        var sb = new StringBuilder(512);
        sb.Append("[FollowerCapture] done reason=").Append(reason)
            .Append(" samples=").Append(_sampleCount)
            .Append(" events=").Append(_eventCount).Append('/').Append(MaxEvents)
            .Append(" dropped=").Append(_droppedEvents)
            .Append(" moving=").Append(_movingSamples)
            .Append(" switches=").Append(_stateSwitches)
            .Append(" backjumps=").Append(_phaseBackJumps)
            .Append(" ctl_changes=").Append(_controllerChanges)
            .Append(" ctl_writes=").Append(_controllerWrites)
            .Append(" ctl_nowrite=").Append(_controllerNoWrites)
            .Append(" sprites=").Append(SpriteIds.Count).Append(_spriteIdsOverflow ? "+" : "")
            .Append(" null_sprites=").Append(_nullSpriteSamples)
            .Append(" speed=[").Append(Range(_speedMin, _speedMax))
            .Append("] anim_speed=[").Append(Range(_animSpeedMin, _animSpeedMax))
            .Append("] obs=").Append(_observerSamples)
            .Append(" boost=").Append(_boostingSamples)
            .Append(" captured=").Append(_capturedSamples)
            .Append(" boost_loco=").Append(_boostLocoSamples)
            .Append(" file=").Append(_lastOutputPath.Length > 0 ? _lastOutputPath : "none");
        return sb.ToString();
    }

    private static string Range(float min, float max)
    {
        if (!float.IsFinite(min) || !float.IsFinite(max) || min > max) return "na";
        return F4(min) + ".." + F4(max);
    }

    private static string F(float value)
    {
        return float.IsFinite(value) ? value.ToString("F2", CultureInfo.InvariantCulture) : "na";
    }

    private static string F4(float value)
    {
        return float.IsFinite(value) ? value.ToString("F4", CultureInfo.InvariantCulture) : "na";
    }

    private static void Fail(string key, Exception e)
    {
        if (LoggedErrors.Add(key))
            LogError("[FollowerCapture] " + key + " failed: " + e.GetType().Name + " " + e.Message);
        Finish("error-" + key);
    }

    private static void Log(string message)
    {
        try
        {
            KingdomEnhancedPlugin.Instance?.LogSource?.LogInfo(message);
        }
        catch
        {
        }
    }

    private static void LogError(string message)
    {
        try
        {
            KingdomEnhancedPlugin.Instance?.LogSource?.LogError(message);
        }
        catch
        {
        }
    }
}
