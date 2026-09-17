// 英雄弓箭手·视觉 slice（operator 2026-09-14 契约，含独立 review 修订）。
//
// 资源：embedded PNG `KingdomEnhancedMod.HeroArcherAtlas.png`（31 帧 = 8x4 网格，单帧 48x32，
//       整图 384x128，末格空白，PPU 32，单帧 pivot 像素 (31,2) = 脚跟锚点）。经
//       UnityEngine.ImageConversion.LoadImage 解码；Point / Clamp / 无 MipMap；
//       只加载一次（惰性）；尺寸或 alpha 校验失败 → 整块 fail-closed，保持原版外观。
//
// 挂点（review 修订）：自有 SpriteRenderer 挂在**原生 renderer 的 Transform 之下**，
//       localPosition 零 / localRotation identity / localScale one —— 位置、朝向（父链 localScale.x 符号）、
//       缩放全部自然继承，绝不复制一次性的 position（那会变成不跟随的静态兄弟节点），也不写父链任何 local 值。
//
// 表现：每名英雄一个自有 SpriteRenderer（纯 SpriteRenderer + 自有 LateUpdate 驱动，无其它组件），
//       不复用原生 renderer、不调 renderer.material、不改共享 material/texture、不改全局 swap。
//       原生 renderer 用 forceRenderingOff 隐藏：**自有对象与状态全部就绪后才隐藏**；任何一步失败都回滚
//       （销毁自有对象 + 归还 forceRenderingOff）。归还时只有仍是 true 才复位（CAS，尊重第三方）；
//       归还失败（瞬时异常）保留凭据下次重试，绝不丢归还责任。
//       原生本来已隐藏（enabled=false / forceRenderingOff=true）→ 英雄视觉不出现（不接管）。
//
// 帧选择（2026-09-15 native-follow 修订 + 同日 live-fix）：不再自建时钟/六状态机，而是每帧读原生
//       Animator 当前 current state 的 shortNameHash + normalizedTime + clip 长度（HeroArcherNativePose），
//       转场时也不预取 next —— Stand 0..9 / Walk 10..15 / Run 16..21 循环取相位小数；
//       Shoot 26..30 按整段 clip 归一（原生会播完整 clip 再退出）。Prepare 22..25 用「clip 时间 /
//       有效窗口」归一：原生只播 shootPrepTime 那么长的前缀，而希腊 prepare clip 长 2.167s，按全局
//       相位归一会让拉弓 4 帧全挤进 clip 前缀（只显示 22 号帧 → 看不到拉弓）。窗口只来自 operator 桥的
//       RecordPrepareWindow（原生同一步 MoveNext 读到的临时 shootPrepTime，rate/DL 效果已含）：
//       pending 记录、Prepare 进入/同状态回绕时锁入 active，缺失 → 回退原始相位；
//       locomotion/防守/撤退永远只跟原生相位，绝不用自有时钟。
//       释放表现（自有帧 26..30）由真实放箭事件锚定、0.18s 有界：覆盖 perfect 弹跳过原生 Shoot 状态时
//       「没有释放动作」的观感；进入 Walk/Run/Shoot/Unknown/新 Prepare 或超时立即取消。
//       它只覆盖自有 sprite，不写 Animator、不动位置/朝向、不推进布料，两个英雄各自独立。
//       Animator 停用或采样非法时归还原生。
//       未绘状态（Ghost Die / Spawn 及世界其它状态名）= Unknown → 自有 body/cloth 隐藏、归还原生
//       forceRenderingOff 让原生显现；保留 VisualState/身份，回到支持状态再安全接管（不 Remove/Apply 重建）。
//       旧 HeroArcherAnimation（16 帧六状态草稿机）保留给旧测试，不再是当前视觉权威。
//
// 生命周期：Apply / Remove / NotifyRelease / Sync / Clear，全部异常隔离；状态表 ≤2 条（每侧 1 名英雄），
//       驱动是挂在自有子物体上的 LateUpdate（最多 2 个），无场景扫描、无协程。
//       Sync 每帧至多跑一次（frameCount 去重）；source 只记录「本帧哪个入口赢得去重」，不声称能证明
//       引擎内执行顺序——要求是它发生在原生 Animator 产出本帧结果之后。
//
// ---------------------------------------------------------------------------
// 布料接线（HeroArcherCloth 已实现并接线，三处均在本文件内）：
//   1) Apply 就绪后：state.Cloth = HeroArcherCloth.Create(anchor, own)
//      （anchor = 原生 renderer 的 Transform，即自有 renderer 的父；参考 renderer = own）
//   2) 每帧原生采样（ApplyNativePose）之后的 TickPresentation：
//      HeroArcherCloth.Tick(state.Cloth, Time.deltaTime, localVelocity, own.enabled) —— 每帧恰好一次，
//      挂起（未知状态/原生隐藏）时以 visible=false 继续 Tick，句柄不销毁；
//      风向取 World.WindSpeedCapped（4Hz 平滑），朝向取原生父链 localScale.x 符号（不要自己翻转）；
//      肩部锚点 local(-5/32, 14/32)×0.9（HeroArcherMotion.ClothRootLocal），叠加当前姿势躯干起伏。
//   3) Remove / Clear / Rollback：HeroArcherCloth.Destroy(state.Cloth) 后才销毁自有子物体。
//   双红飘带由独立动态网格绘制，不烘焙进 body atlas；组合预览与实际游戏验证分开记录。
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Reflection;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace KingdomEnhancedMod;

internal static class HeroArcherVisuals
{
    private const string ResourceName = "KingdomEnhancedMod.HeroArcherAtlas.png";
    // 尺寸/网格/锚点全部来自新布局（HeroArcherPoseAtlas，31 帧 8×4）；不再引用旧 HeroArcherAtlas。
    private const int CellWidth = HeroArcherPoseAtlas.CellWidth;
    private const int CellHeight = HeroArcherPoseAtlas.CellHeight;
    private const int AtlasWidth = HeroArcherPoseAtlas.SheetWidth;    // 384
    private const int AtlasHeight = HeroArcherPoseAtlas.SheetHeight;  // 128
    private const float PixelsPerUnit = HeroArcherPoseAtlas.PixelsPerUnit;
    private const float PivotPixelX = HeroArcherPoseAtlas.PivotPixelX;
    private const float PivotPixelY = HeroArcherPoseAtlas.PivotPixelY;

    /// <summary>
    /// 事件锚定释放表现的时长（秒）：真实放箭事件触发一次，自有帧 26..30 在此时长内播完，
    /// 覆盖原生 perfect 弹跳过 Shoot 状态时"没有释放动作"的观感。有界、只改自有 sprite、
    /// 不写 Animator/不动位置朝向、不驱动 Stand/Walk/Run/Shoot 的相位。
    /// </summary>
    internal const float ReleasePresentationSeconds = 0.18f;

    /// <summary>
    /// 有界全局 transition 诊断（≤120 行，按事件变化；每 actor 固定槽去重）：
    /// 核心日志失败不破坏渲染（Log 内部吞异常）。
    /// </summary>
    private static readonly HeroArcherNativeEventLog _events = new HeroArcherNativeEventLog();

    /// <summary>单次动作访问（Prepare/Shoot）诊断行上限：自 Clear 起计，事件触发（非逐帧）。</summary>
    internal const int VisitLogCapacity = 60;
    /// <summary>挂载/卸载事件诊断行上限：自 Clear 起计（暴露身份抖动导致的原生/自有交替）。</summary>
    internal const int LifeLogCapacity = 60;
    private static int _visitLines;
    private static int _lifeLines;

    private enum AtlasState
    {
        Unknown = 0,
        Ready = 1,
        Unavailable = 2,
    }

    private sealed class VisualState
    {
        internal Archer Ref;
        internal IntPtr Pointer;
        internal int GoId;
        internal int Life;
        internal GameObject Root;
        internal SpriteRenderer Own;
        internal SpriteRenderer Native;
        internal bool HidNative;
        internal int LastFrame = -1;
        internal HeroArcherNativeAction LastAction = HeroArcherNativeAction.Unknown;
        internal int NativeStateHash;
        internal float NativeNormalizedTime;
        internal HeroArcherNativeFallback LastFallback;
        // 放箭事件标记（原生 FireArrowInternal → NotifyRelease）：只并入下一条有界诊断行；
        // 绝不重置/覆盖原生姿势（相位与动作只跟原生 Animator），也不推进布料。
        internal bool ShotPending;
        // Prepare 可见窗口（秒）：pending 由 operator 桥记录（原生同一步 MoveNext 读到的临时 shootPrepTime，
        // 含 rate/DL 效果）；active 在 Prepare 进入或同状态回绕时从 pending 锁入。进行中的拉弓不会被
        // 中途改窗口，池/换世界重建后 pending/active 归零（不沿用旧值）。
        internal float PendingWindow;
        internal float ActiveWindow;
        // 事件锚定的释放表现（仅自有帧 26..30）：0.18s 有界恢复，独立于原生是否跳过 Shoot 状态；
        // 只改自有 sprite，不写 Animator、不动位置/朝向、不驱动 locomotion。
        internal bool ReleaseActive;
        internal bool ReleaseFirstFrame;
        internal float ReleaseElapsed;
        internal int ReleaseStartFrame = int.MinValue;
        // 单次动作（Prepare/Shoot）访问跟踪：**只做诊断证据**（帧区间/时长/放箭时刻），绝不喂计时。
        internal HeroArcherNativeAction VisitAction = HeroArcherNativeAction.Unknown;
        internal float VisitElapsed;
        internal float VisitMaxClipTime;
        internal int VisitMinFrame = int.MaxValue;
        internal int VisitMaxFrame = -1;
        internal int VisitShotCount;
        internal float VisitShotCtMax;
        /// <summary>本帧检测到 Prepare 同状态回绕（normalizedTime 递减）→ 重新锁窗口并取消释放。</summary>
        internal bool PrepareRestarted;
        // 最近一次采样载荷（诊断用；不参与渲染决策）。
        internal float LastClipLength;
        internal float LastClipTime;
        internal float LastNt;
        /// <summary>本帧原生采样帧（诊断/访问区间用；显示帧可能是释放表现的覆盖值，见 LastFrame）。</summary>
        internal int LastNativeFrame = -1;
        internal HeroArcherCloth.Handle Cloth;
        internal float LastWorldX;
        internal float NextWindSample, WindDrive;
    }

    private static readonly Dictionary<int, VisualState> _visuals = new Dictionary<int, VisualState>(4);
    private static readonly List<int> _scratch = new List<int>(4);
    private static readonly MaterialPropertyBlock _propertyBlock = new MaterialPropertyBlock();

    private static AtlasState _atlasState = AtlasState.Unknown;
    private static Texture2D _atlas;
    private static Sprite[] _sprites;
    private static bool _driverRegistered;
    private static bool _loggedUnavailable;
    private static bool _loggedCopyFailure;
    private static int _lastSyncedFrame = int.MinValue;

    /// <summary>当前有视觉的英雄数（自检/日志用）。</summary>
    internal static int Count => _visuals.Count;

    /// <summary>该英雄是否已有自有视觉（runtime 的补挂载重试用它判断，缺视觉也可在原生隐藏解除后重试）。</summary>
    internal static bool HasVisual(Archer archer)
    {
        try
        {
            if (archer == null || archer.gameObject == null) return false;
            int goId = SafeGoId(archer);
            return goId != 0 && _visuals.TryGetValue(goId, out VisualState state) && SameObject(state, archer);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>atlas 已判定不可用（缺资源/尺寸/alpha 失败）：runtime 不再重复尝试加载。</summary>
    internal static bool AtlasUnavailable => _atlasState == AtlasState.Unavailable;

    /// <summary>
    /// 为英雄建立自有视觉（幂等：已在表中 → no-op）。全部步骤按「先就绪、后隐藏」排列：
    /// 资源/原生 renderer/挂点/精灵/驱动任一步失败 → 原版外观保持，原生 renderer 绝不被我们关掉。
    /// </summary>
    internal static void Apply(Archer archer)
    {
        if (archer == null || archer.gameObject == null) return;
        int goId = SafeGoId(archer);
        if (goId == 0) return;
        if (_visuals.TryGetValue(goId, out VisualState existing))
        {
            if (SameObject(existing, archer)) return;
            RemoveByKey(goId, "replace");
            if (_visuals.ContainsKey(goId)) return;
        }

        SpriteRenderer native = FindNativeRenderer(archer);
        if (native == null) return;

        bool nativeVisible;
        bool nativeForceOff;
        try
        {
            nativeVisible = native.enabled;
            nativeForceOff = native.forceRenderingOff;
        }
        catch (Exception)
        {
            return;
        }
        // 本体已隐藏 / 已被强制关闭渲染 → 不接管（英雄绝不比本体更显眼）。
        if (!nativeVisible || nativeForceOff) return;

        if (!EnsureAtlas()) return;

        Transform anchor;
        try
        {
            anchor = native.transform;
            if (anchor == null) return;
        }
        catch (Exception)
        {
            return;
        }

        int life = HeroArcherRuntime.CurrentActorLife(archer);
        if (life == 0) return; // 身份/life 未知：不接管

        VisualState state = new VisualState();
        state.Ref = archer;
        state.Pointer = SafePointer(archer);
        state.GoId = goId;
        state.Life = life;
        state.Native = native;
        try
        {
            state.Root = new GameObject("KEM_HeroArcherSprite");
            state.Own = state.Root.AddComponent<SpriteRenderer>();
            if (state.Own == null) throw new InvalidOperationException("sprite renderer missing");

            // 挂进原生 renderer 之下：局部零偏移/单位缩放 → 位置、朝向、父链缩放全继承。
            state.Root.transform.SetParent(anchor, false);
            state.Root.transform.localPosition = Vector3.zero;
            state.Root.transform.localRotation = Quaternion.identity;
            state.Root.transform.localScale = Vector3.one;

            CopyRendererLook(state);

            // 驱动必须真正注入成功（缺驱动 = 帧不会推进，属于半套状态）：失败即整块回滚。
            state.Cloth = HeroArcherCloth.Create(state.Root.transform.parent, state.Own);
            if (state.Cloth == null) throw new InvalidOperationException("cloth unavailable");
            state.LastWorldX = archer.transform.position.x;
            if (!ApplyVisualScale(state)) { Rollback(state); return; }

            EnsureDriverRegistered();
            if (!_driverRegistered) throw new InvalidOperationException("visual driver not registered");
            HeroArcherVisualDriver driver = state.Root.AddComponent<HeroArcherVisualDriver>();
            if (driver == null) throw new InvalidOperationException("visual driver injection failed");

            // 自有表现已就绪：注册凭据（随后可能写原生 forceRenderingOff），再按原生状态决定
            // 接管（隐藏本体 + 显示自有帧）还是挂起（未知/不可读 → 原生显现；状态留在表里，不重建）。
            _visuals[goId] = state;
            ApplyNativePose(state, "apply");

            // 视觉全部就绪后压共享英雄深度；失败（参考层非法/写入/恢复异常）→ 整组回滚，
            // 绝不带半套深度或声称已还原的自有对象继续运行。
            if (!ApplyHeroPlane(state)) { Rollback(state); return; }

            // 有界生命周期诊断：与 detach 行配对，暴露「身份抖动静默重建」导致原生/自有交替的节奏。
            LogLife("attach actor=" + goId + " life=" + life + " action=" + state.LastAction);
        }
        catch (Exception e)
        {
            Rollback(state);
            if (!_loggedCopyFailure) { _loggedCopyFailure = true; Log("apply failed: " + e.GetType().Name); }
        }
    }

    /// <summary>
    /// 共享英雄视觉深度（HeroVisualPriority）：只写自有 body/cloth transform 的世界 z，
    /// x/y、原生对象、sortingLayer/order 一概不动。返回 false = 本次失败（参考层失效/写入或恢复异常），
    /// 调用方必须处理：Apply → Rollback，Sync → RemoveByKey（原生还原由既有逻辑保证）。
    /// 另：Cloth 旧的 sortOrder -1/-2 在透明队列（不写 depth）下会被后画 NPC 盖掉，
    /// 故每次在 Cloth.Tick/ApplyFlip 之后把自有 cloth 两个 MeshRenderer 的 sorting 与自有 body 对齐
    /// （同队列内按 z 深度排序；body 的 layer/order 本身沿用 CopyRendererLook 复制来的原生值）。
    /// </summary>
    private static bool ApplyHeroPlane(VisualState state)
    {
        Transform gameLayer = null;
        try
        {
            var world = Managers.Inst != null ? Managers.Inst.world : null;
            gameLayer = world != null ? world.gameLayer : null;
        }
        catch (Exception)
        {
        }

        Transform body;
        HeroArcherCloth.Handle cloth = state.Cloth;
        try
        {
            body = state.Root != null ? state.Root.transform : null;
        }
        catch (Exception)
        {
            body = null;
        }
        if (!HeroVisualPriority.ApplyHeroDepth(gameLayer, body, cloth != null ? cloth.RootTransform : null))
            return false;

        if (cloth != null)
        {
            try
            {
                SpriteRenderer own = state.Own;
                if (own == null) return false;
                int layer = own.sortingLayerID;
                int order = own.sortingOrder;
                MeshRenderer[] renderers = cloth.Renderers;
                if (renderers == null) return false;
                for (int i = 0; i < renderers.Length; i++)
                {
                    MeshRenderer renderer = renderers[i];
                    if (renderer == null) return false;
                    renderer.sortingLayerID = layer;   // 与自有 body 相同：同队列按深度排序，不再用旧 -1/-2
                    renderer.sortingOrder = order;
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>移除英雄视觉：归还原生 forceRenderingOff（CAS）、销毁自有子物体。</summary>
    internal static void Remove(Archer archer)
    {
        try
        {
            if (archer == null || archer.gameObject == null) return;
            int goId = SafeGoId(archer);
            if (goId == 0) return;
            if (!_visuals.TryGetValue(goId, out VisualState state)) return;
            if (!SameObject(state, archer)) return; // 换主/换 life 的包装引用不动别人的凭据
            RemoveByKey(goId, "remove");
        }
        catch (Exception)
        {
        }
    }

    /// <summary>
    /// 放箭事件（原生 FireArrowInternal 每发恰好一次；散射补箭会在同一帧多发）：
    /// 1) 诊断标记 ShotPending（并入下一条有界转场行）+ 当前访问内的放箭计数与时刻证据（只做证据）；
    /// 2) 启动自有释放表现（帧 26..30，0.18s 有界）：同帧重复调用不重启，隔帧的真实放箭重启；
    /// 绝不重置/覆盖原生姿势（相位与动作只跟原生 Animator）、不写 Animator、不推进布料。
    /// </summary>
    internal static void NotifyRelease(Archer archer)
    {
        try
        {
            if (archer == null || archer.gameObject == null) return;
            int goId = SafeGoId(archer);
            if (goId == 0 || !_visuals.TryGetValue(goId, out VisualState state)) return;
            if (!SameObject(state, archer)) return;
            state.ShotPending = true;
            if (state.VisitAction != HeroArcherNativeAction.Unknown)
            {
                state.VisitShotCount++;
                if (state.LastClipTime > state.VisitShotCtMax) state.VisitShotCtMax = state.LastClipTime;
            }
            int frame = FrameCount();
            if (state.ReleaseActive && state.ReleaseStartFrame == frame) return;   // 同帧散射多发：不重启
            state.ReleaseActive = true;
            state.ReleaseFirstFrame = true;
            state.ReleaseElapsed = 0f;
            state.ReleaseStartFrame = frame;
        }
        catch (Exception)
        {
        }
    }

    /// <summary>
    /// Prepare 可见窗口的旁路记录（operator 桥在既有 Priority.Last cadence prefix 借用之后调用）：
    /// 传入的 seconds 就是原生同一次 MoveNext 里读到的临时 <c>archer.shootPrepTime</c>（rate/DL 效果已含），
    /// 因此它是 Prepare 可见窗口的精确来源：本模块不测量、不加 getter hook。
    /// 只在同一个已知自有视觉（GO id + pointer + 当前 life 全同）上生效；非有限/非正 → 忽略。
    /// 窗口只在 Prepare 进入（或同状态回绕）时从 pending 锁入 active；池/换世界重建后两者归零，
    /// 因此绝不会沿用上一生命的窗口。
    /// </summary>
    internal static void RecordPrepareWindow(Archer archer, float seconds)
    {
        try
        {
            if (!(seconds > 0f) || !float.IsFinite(seconds)) return;
            if (archer == null || archer.gameObject == null) return;
            int goId = SafeGoId(archer);
            if (goId == 0 || !_visuals.TryGetValue(goId, out VisualState state)) return;
            if (!SameObject(state, archer)) return;
            if (state.Life == 0 || state.Life != HeroArcherRuntime.CurrentActorLife(archer)) return;
            state.PendingWindow = seconds;
        }
        catch (Exception)
        {
        }
    }

    /// <summary>
    /// 每帧同步（frameCount 去重：多个入口同帧只执行一次）。source 仅记录本帧赢得去重的入口
    /// （诊断用）；不声称能证明引擎内执行顺序——要求是视觉采样发生在原生 Animator 产出本帧结果之后。
    /// </summary>
    internal static void Sync() => Sync("panel");

    internal static void Sync(string source)
    {
        try
        {
            if (_visuals.Count == 0) return;
            int frame = FrameCount();
            if (frame == _lastSyncedFrame) return;   // 两个驱动/面板同帧只跑一次，绝不双倍推进动画
            _lastSyncedFrame = frame;
            _scratch.Clear();
            foreach (KeyValuePair<int, VisualState> pair in _visuals) _scratch.Add(pair.Key);
            for (int i = 0; i < _scratch.Count; i++)
            {
                if (!_visuals.TryGetValue(_scratch[i], out VisualState state)) continue;
                if (!Valid(state)) { RemoveByKey(state.GoId, "invalid"); continue; }
                CopyRendererLook(state);
                ApplyNativePose(state, source);
                TickPresentation(state);
            }
        }
        catch (Exception)
        {
        }
    }

    /// <summary>
    /// 布料与深度表现（每帧一次）：原生风 4Hz 采样平滑 → 本地速度喂布 → Tick（必须恰好一次）→
    /// 重断言绝对 0.9 缩放与共享英雄深度（Cloth.Tick/ApplyFlip 每帧会重写这两处）。
    /// </summary>
    private static void TickPresentation(VisualState state)
    {
        float dt = Time.deltaTime;
        // 单次动作访问跟踪（有界诊断 + Prepare 有效窗口测量）：必须在本帧原生采样之后。
        TrackNativeVisit(state, dt);
        float worldX = state.Ref.transform.position.x;
        float velocity = dt > 0f ? (worldX - state.LastWorldX) / dt : 0f;
        state.LastWorldX = worldX;
        float localVelocity = HeroArcherClothMath.LocalVelocityXFromWorld(velocity, state.Root.transform.parent.lossyScale.x);
        float now = Now();
        if (now >= state.NextWindSample)
        {
            state.NextWindSample = now + .25f;
            try
            {
                var world = Managers.Inst != null ? Managers.Inst.world : null;
                float raw = world != null ? world.WindSpeedCapped(state.Ref.transform.position) : 0f;
                if (!float.IsFinite(raw)) raw = 0f;
                float mapped = raw / (1f + Mathf.Abs(raw)); // Visual strength only, not meters/second.
                state.WindDrive += (mapped - state.WindDrive) * .4f;
            }
            catch { state.WindDrive = 0f; }
        }
        float windDirection = state.Root.transform.parent.lossyScale.x < 0 ? -1f : 1f;
        if (state.Own.flipX) windDirection = -windDirection;
        HeroArcherCloth.Reorient(state.Cloth, state.Root.transform.parent.lossyScale.x);
        HeroArcherCloth.SetWind(state.Cloth, state.WindDrive * windDirection);
        HeroArcherCloth.Tick(state.Cloth, dt, localVelocity, state.Own.enabled);
        // Tick/Reorient/ApplyFlip 每帧把 cloth root 重写回全尺寸：之后统一重断言绝对 0.9
        // （不乘当前 scale，重复调用幂等；flip 符号按 reference.flipX 重取，z 保持 1 不动深度）。
        if (!ApplyVisualScale(state)) { RemoveByKey(state.GoId, "scale"); return; }
        // Tick 内 ApplyFlip 会按 reference.localPosition 重建布料 z、ApplySorting 会写回旧 -1/-2
        // → 每帧在最后重压共享英雄深度并覆盖自有 cloth 排序；失败（含参考层失效）→ 撤销整组自有表现。
        if (!ApplyHeroPlane(state)) { RemoveByKey(state.GoId, "plane"); return; }
    }

    /// <summary>全量移除（模组关闭 / world 切换 / Clear）：归还全部原生 renderer；诊断预算重置。</summary>
    internal static void Clear()
    {
        try
        {
            // 换 world / 关功能：有界诊断预算与去重槽重新开始（含单次动作访问与生命周期两类上限）。
            if (_visuals.Count == 0) return;
            _scratch.Clear();
            foreach (KeyValuePair<int, VisualState> pair in _visuals) _scratch.Add(pair.Key);
            for (int i = 0; i < _scratch.Count; i++) RemoveByKey(_scratch[i], "clear");
        }
        catch (Exception)
        {
        }
        finally
        {
            _events.Reset();
            _visitLines = 0;
            _lifeLines = 0;
        }
    }

    // ============================================================
    // 内部实现
    // ============================================================

    /// <summary>
    /// 释放凭据；瞬时失败（探测/写入抛错）保留条目，下一次 LateUpdate/Remove 重试。
    /// reason 只进有界生命周期诊断（invalid/scale/plane/clear/remove），不改变归还语义。
    /// </summary>
    private static void RemoveByKey(int goId, string reason)
    {
        if (!_visuals.TryGetValue(goId, out VisualState state)) return;
        if (!TryReleaseNative(state)) return;
        _visuals.Remove(goId);
        state.Root = null;
        state.Own = null;
        state.Native = null;
        state.ShotPending = false;
        state.VisitAction = HeroArcherNativeAction.Unknown;
        state.Ref = null;
        LogLife("detach actor=" + goId + " reason=" + reason);
    }

    /// <summary>
    /// 归还原生 renderer：只有同一对象（pointer + GOID 相同，含旧 world/旧 life 的同一 GO）才写回，
    /// 且只写 false（我们写过 true）；对象已销毁/换主 → 无需归还，正常丢弃。
    /// 返回 false = 瞬时异常，调用方保留凭据重试（绝不丢归还责任）。
    /// </summary>
    private static bool TryReleaseNative(VisualState state)
    {
        if (!ReleaseNativeHide(state)) return false;
        HeroArcherCloth.Destroy(state.Cloth);
        state.Cloth = null;
        if (state.Root != null)
        {
            DestroyQuietly(state.Root);
            state.Root = null;
        }
        return true;
    }

    /// <summary>
    /// 只归还「我们写过的」原生 forceRenderingOff（CAS：凭据在 state.HidNative，写回只写 false）。
    /// 挂起路径（未知状态/不可读/Animator 停用）与非挂起路径共用本方法：归还成功即丢凭据，
    /// 失败（瞬时异常）保留凭据下一帧重试 → 未知读取失败不丢所有权；也不覆盖第三方设置的隐藏。
    /// </summary>
    private static bool ReleaseNativeHide(VisualState state)
    {
        if (!state.HidNative || state.Native == null)
        {
            state.HidNative = false;
            return true;
        }
        bool reachable;
        try
        {
            Archer archer = state.Ref;
            reachable = archer != null && archer.gameObject != null
                && SafeGoId(archer) == state.GoId
                && SafePointer(archer) == state.Pointer;
        }
        catch (Exception)
        {
            return false;
        }
        if (reachable)
        {
            try
            {
                if (state.Native.forceRenderingOff) state.Native.forceRenderingOff = false;
            }
            catch (Exception)
            {
                return false;
            }
        }
        state.HidNative = false;
        return true;
    }

    private static void Rollback(VisualState state)
    {
        if (state == null) return;
        if (!TryReleaseNative(state)) { _visuals[state.GoId] = state; return; }
        if (_visuals.TryGetValue(state.GoId, out VisualState current) && ReferenceEquals(current, state))
            _visuals.Remove(state.GoId);
    }

    /// <summary>
    /// 身份与存活核对：GOID + pointer + 当前 live life + **即时 IsHero**（配置/世界范围/资格现算）
    /// + 自有/原生对象健在。任一不成立 → 该视觉凭据由调用方释放（归还 forceRenderingOff）。
    /// </summary>
    private static bool Valid(VisualState state)
    {
        try
        {
            Archer archer = state.Ref;
            if (archer == null || archer.gameObject == null) return false;
            if (!archer.gameObject.activeInHierarchy) return false;
            if (SafeGoId(archer) != state.GoId) return false;
            if (SafePointer(archer) != state.Pointer) return false;
            int life = HeroArcherRuntime.CurrentActorLife(archer);
            if (life == 0 || life != state.Life) return false;
            if (!HeroArcherRuntime.IsHero(archer)) return false;
            return state.Native != null && state.Own != null && state.Own.gameObject != null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>同一对象核对（不要求 life 相同：池复用/换 life 时仍是我们自己的凭据）。</summary>
    private static bool SameObject(VisualState state, Archer archer)
    {
        try
        {
            return SafeGoId(archer) == state.GoId && SafePointer(archer) == state.Pointer;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>把原生 renderer 的可见性/染色/排序/翻转/材质照搬给自有 renderer（只读原生，不写）。</summary>
    private static void CopyRendererLook(VisualState state)
    {
        SpriteRenderer native = state.Native;
        SpriteRenderer own = state.Own;
        if (native == null || own == null) return;
        try
        {
            // 原生隐藏（SetHideStatus / 石化清理等）时英雄一并隐藏；原生恢复可见时跟着恢复。
            own.enabled = native.enabled;
            own.sortingLayerID = native.sortingLayerID;
            own.sortingOrder = native.sortingOrder;
            own.flipX = native.flipX;
            own.flipY = native.flipY;
            own.color = native.color;
            // 层跟随原生（碰撞/渲染筛选与本体一致；层是 GameObject 属性，不是 renderer 独有）。
            GameObject ownObject = own.gameObject;
            GameObject nativeObject = native.gameObject;
            if (ownObject != null && nativeObject != null && ownObject.layer != nativeObject.layer)
            {
                ownObject.layer = nativeObject.layer;
            }
        }
        catch (Exception e)
        {
            if (!_loggedCopyFailure) { _loggedCopyFailure = true; Log("renderer copy failed: " + e.GetType().Name); }
            return;
        }
        try
        {
            Material shared = native.sharedMaterial;
            if (shared != null) own.sharedMaterial = shared;
        }
        catch (Exception)
        {
        }
        try
        {
            native.GetPropertyBlock(_propertyBlock);
            own.SetPropertyBlock(_propertyBlock);
        }
        catch (Exception)
        {
        }
    }

    /// <summary>
    /// 原生跟随（每帧一次，Sync 内调用）：读原生 Animator 当前（转场按策略取 next）state → atlas 帧；
    /// 据此接管（隐藏本体 + 显示自有帧）或挂起（未知状态/不可读/Animator 停用 → 自有隐藏，
    /// 归还原生 forceRenderingOff 让原生显现；VisualState/身份保留，回到支持状态再安全接管）。
    /// 只读原生（不写 Animator/参数/位置）；只写自有 renderer 与「我们写过 true」的原生 forceRenderingOff。
    /// </summary>
    private static void ApplyNativePose(VisualState state, string source)
    {
        SpriteRenderer native = state.Native;
        SpriteRenderer own = state.Own;
        if (native == null || own == null) return;

        HeroArcherNativeAction previousAction = state.LastAction;
        float previousNt = state.LastNt;
        HeroArcherNativePoseSample sample = HeroArcherNativeSampler.Sample(ReadAnimator(state.Ref), state.ActiveWindow);
        // 诊断载荷（不参与渲染决策）：本帧 clip 长度 / clip 时间 / 相位（Prepare 归一窗口与访问证据）。
        state.LastClipLength = sample.ClipLength;
        float clipTime = sample.NormalizedTime * sample.ClipLength;
        state.LastClipTime = float.IsFinite(clipTime) ? clipTime : 0f;
        state.LastNt = sample.NormalizedTime;
        state.LastNativeFrame = sample.Frame;
        // 同状态回绕：Prepare 相位倒退（换皮/重置重建同一状态）→ 视作新一次拉弓。
        state.PrepareRestarted = sample.Action == HeroArcherNativeAction.Prepare
            && previousAction == HeroArcherNativeAction.Prepare
            && float.IsFinite(previousNt) && float.IsFinite(sample.NormalizedTime)
            && sample.NormalizedTime < previousNt - PrepareRollbackEpsilon;
        if (sample.Action == HeroArcherNativeAction.Prepare
            && (previousAction != HeroArcherNativeAction.Prepare || state.PrepareRestarted))
        {
            state.ActiveWindow = state.PendingWindow;
            sample = new HeroArcherNativePoseSample(sample.Action, sample.StateHash, sample.NormalizedTime,
                sample.ClipLength, HeroArcherNativePose.FrameFor(sample.Action, sample.NormalizedTime,
                    sample.ClipLength, state.ActiveWindow), sample.Fallback);
            state.LastNativeFrame = sample.Frame;
        }
        bool nativeEnabled;
        bool nativeForceOff;
        try
        {
            nativeEnabled = native.enabled;
            nativeForceOff = native.forceRenderingOff;
        }
        catch (Exception)
        {
            return;   // 原生 renderer 不可读：本帧完全不动（保留上次帧/可见性/凭据），下一帧重试
        }

        // 事件锚定的释放表现：只在允许的动作上覆盖自有帧（Walk/Run/Shoot/Unknown/新 Prepare 立即取消）。
        UpdateRelease(state, in sample, previousAction);
        int frame = state.ReleaseActive ? ReleaseFrameFor(state.ReleaseElapsed) : sample.Frame;

        // 「原生不可见」= 原生 renderer 被关，或被**第三方** forceRenderingOff。
        // 我们自己写下的隐藏（state.HidNative）不算原生不可见，否则会把自己也一起藏掉。
        bool nativeHidden = !nativeEnabled || (nativeForceOff && !state.HidNative);
        HeroArcherNativeFallback reason = sample.Fallback;
        bool show = sample.Drawn && !nativeHidden;
        if (sample.Drawn && nativeHidden) reason = HeroArcherNativeFallback.NativeHidden;

        if (show && !state.HidNative)
        {
            // 走到这里说明原生原本可见（enabled 且未被第三方隐藏）→ 写 true 是 CAS 抢隐藏，不覆盖第三方；
            // 写失败 → 本帧继续挂起（没写成功就不持有），下一帧重试。
            try
            {
                state.HidNative = true; // Record responsibility before a setter can partly apply then throw.
                native.forceRenderingOff = true;
            }
            catch (Exception)
            {
                show = false;
                reason = HeroArcherNativeFallback.Unreadable;
            }
        }

        if (show)
        {
            try
            {
                if (state.LastFrame != frame || state.LastAction != sample.Action)
                {
                    Sprite sprite = _sprites != null && frame >= 0 && frame < _sprites.Length
                        ? _sprites[frame]
                        : null;
                    if (sprite == null)
                    {
                        show = false;
                        reason = HeroArcherNativeFallback.SpriteUnavailable;
                    }
                    else
                    {
                        own.sprite = sprite;   // 先有 sprite 才允许可见：绝不留「可见但空帧」的半个对象
                        state.LastFrame = frame;
                        state.LastAction = sample.Action;
                    }
                }
            }
            catch (Exception)
            {
                show = false;
                reason = HeroArcherNativeFallback.Unreadable;
            }
        }

        if (show)
        {
            try
            {
                own.enabled = true;
            }
            catch (Exception)
            {
                show = false;
                reason = HeroArcherNativeFallback.Unreadable;
            }
        }

        if (!show)
        {
            try { own.enabled = false; } catch (Exception) { }   // 原生 enabled=false（SetHideStatus）→ 跟随隐藏，绝不强行显示
            state.LastAction = sample.Action;
            // 只有「需要让原生显现」的挂起才归还隐藏；原生本就不可见（NativeHidden）时保留凭据，
            // 避免原生短暂不可见期间来回抢/放造成闪烁。
            if (reason != HeroArcherNativeFallback.NativeHidden) ReleaseNativeHide(state);
        }

        state.NativeStateHash = sample.StateHash;
        state.NativeNormalizedTime = sample.NormalizedTime;
        state.LastFallback = reason;
        ReportNativeEvent(state, in sample, frame, !nativeHidden, nativeForceOff, show, source);
    }

    /// <summary>
    /// 有界诊断（全局 ≤120 行；按 actor 的转场/可见性/原因变化记一行，非逐帧）：
    /// 时间/帧号/来源 phase/actor/life/native state hash/相位/clip 长度与 clip 时间/显示帧与原生帧/
    /// 有效窗口/朝向证据/世界 y/原生可用性+隐藏位/自有可见性/挂起原因/放箭标记。
    /// 日志失败由 Log 吞掉，绝不影响渲染；每 actor 只占固定去重槽，不随 actor 数增长。
    /// </summary>
    private static void ReportNativeEvent(VisualState state, in HeroArcherNativePoseSample sample,
        int displayFrame, bool nativeVisible, bool nativeForceOff, bool heroVisible, string source)
    {
        if (_events.Exhausted) return;
        HeroArcherNativeEventKey key = new HeroArcherNativeEventKey(
            state.GoId, state.Life, sample.StateHash, nativeVisible, heroVisible, state.LastFallback);
        if (!_events.Accept(in key)) return;
        bool shot = state.ShotPending;
        state.ShotPending = false;
        Log("[HeroArcherNative] t=" + Now().ToString("F3") + " frame=" + FrameCount() + " src=" + source
            + " actor=" + state.GoId + " life=" + state.Life + " hash=" + sample.StateHash
            + " nt=" + FormatPhase(sample.NormalizedTime) + " len=" + FormatPhase(sample.ClipLength)
            + " ct=" + FormatPhase(state.LastClipTime) + " pose=" + displayFrame + " nat=" + sample.Frame
            + " win=" + state.ActiveWindow.ToString("F3")
            + " native=" + (nativeVisible ? 1 : 0) + " forceoff=" + (nativeForceOff ? 1 : 0)
            + " hero=" + (heroVisible ? 1 : 0)
            + " reason=" + state.LastFallback
            + " y=" + NativeWorldY(state) + " face=" + FacingEvidence(state)
            + (shot ? " shot=1" : ""));
    }

    /// <summary>
    /// 朝向证据（诊断，只读）：原生父链世界 x 缩放符号 / 自有 renderer 世界 x 缩放符号 / 原生 flipX。
    /// 自有 sprite 是原生 renderer 的子物体且逐帧复制 flipX，两者符号必须一致；不一致即真实回归。
    /// </summary>
    private static string FacingEvidence(VisualState state)
    {
        try
        {
            Transform anchor = state.Root != null ? state.Root.transform.parent : null;
            float rootLossy = anchor != null ? anchor.lossyScale.x : 0f;
            float ownLossy = state.Own != null ? state.Own.transform.lossyScale.x : 0f;
            bool flip = state.Native != null && state.Native.flipX;
            return SignOf(rootLossy) + "/" + SignOf(ownLossy) + (flip ? "f1" : "f0");
        }
        catch (Exception)
        {
            return "?";
        }
    }

    /// <summary>原生 renderer 的世界 y（诊断：墙位/塔位与地面高度差异的证据）。</summary>
    private static string NativeWorldY(VisualState state)
    {
        try
        {
            return state.Native != null ? state.Native.transform.position.y.ToString("F2") : "?";
        }
        catch (Exception)
        {
            return "?";
        }
    }

    private static string SignOf(float value)
        => !float.IsFinite(value) ? "?" : (value < 0f ? "-1" : "1");

    // ---- 单次动作访问跟踪（Prepare/Shoot，**只做诊断**）--------------------------------
    // 原诊断只在转场瞬间记一行，无法证明「短状态内相位是否在推进」（nt=0.000 只是转场那一刻）。
    // 本跟踪在访问期间累计时长、clip 时间峰值、帧区间与放箭时刻，动作结束时写一行
    // （事件触发；自 Clear 起 ≤60 行）。这些数字**不参与任何计时**：Prepare 窗口只来自
    // operator 桥的 RecordPrepareWindow（原生同一步读到的临时 shootPrepTime），
    // 释放表现只来自真实放箭事件 + 0.18s 有界呈现时长。

    /// <summary>Prepare 同状态回绕判定阈值（normalizedTime 递减超过该值 = 状态重启）。</summary>
    private const float PrepareRollbackEpsilon = 0.0005f;

    /// <summary>
    /// 每帧一次（TickPresentation 顶部，本帧原生采样之后）：动作进入/同状态回绕 → 结算上一个访问、
    /// 从 pending 锁定 Prepare 窗口；Prepare/Shoot 访问期间累计时长、clip 时间峰值与帧区间。
    /// **只做诊断证据**：访问时长不参与任何计时或姿势决策（窗口只来自 operator 桥的 RecordPrepareWindow）。
    /// </summary>
    private static void TrackNativeVisit(VisualState state, float dt)
    {
        HeroArcherNativeAction action = state.LastAction;
        bool entered = action != state.VisitAction;
        bool rolledBack = state.PrepareRestarted
            && action == HeroArcherNativeAction.Prepare
            && state.VisitAction == HeroArcherNativeAction.Prepare;
        if (entered || rolledBack)
        {
            CloseVisit(state, action);
            if (action == HeroArcherNativeAction.Prepare || action == HeroArcherNativeAction.Shoot)
            {
                state.VisitAction = action;
                state.VisitElapsed = 0f;
                state.VisitMaxClipTime = 0f;
                state.VisitMinFrame = int.MaxValue;
                state.VisitMaxFrame = -1;
                state.VisitShotCount = 0;
                state.VisitShotCtMax = 0f;
            }
        }
        if (state.VisitAction == HeroArcherNativeAction.Unknown) return;
        if (dt > 0f) state.VisitElapsed += dt;
        if (state.LastClipTime > state.VisitMaxClipTime) state.VisitMaxClipTime = state.LastClipTime;
        if (state.LastNativeFrame >= 0)
        {
            if (state.LastNativeFrame < state.VisitMinFrame) state.VisitMinFrame = state.LastNativeFrame;
            if (state.LastNativeFrame > state.VisitMaxFrame) state.VisitMaxFrame = state.LastNativeFrame;
        }
    }

    /// <summary>
    /// 结算访问（**只写诊断行**）：时长/帧区间/clip 时间峰值/放箭计数与放箭时刻/当前有效窗口/下一个动作。
    /// 放箭证据归属「事件发生时打开的那个访问」，不会被随后开启的新访问吞掉；
    /// 访问时长包含放箭后的状态尾巴，因此它只是证据，不是 Prepare 窗口（窗口见 RecordPrepareWindow）。
    /// </summary>
    private static void CloseVisit(VisualState state, HeroArcherNativeAction next)
    {
        if (state.VisitAction == HeroArcherNativeAction.Unknown) return;
        HeroArcherNativeAction action = state.VisitAction;
        state.VisitAction = HeroArcherNativeAction.Unknown;
        if (_visitLines >= VisitLogCapacity) return;
        _visitLines++;
        string frames = state.VisitMaxFrame >= 0 && state.VisitMinFrame <= state.VisitMaxFrame
            ? state.VisitMinFrame + "-" + state.VisitMaxFrame
            : "-";
        Log("[HeroArcherPoseVisit] t=" + Now().ToString("F3") + " frame=" + FrameCount()
            + " actor=" + state.GoId + " action=" + action
            + " dur=" + state.VisitElapsed.ToString("F3")
            + " ctMax=" + FormatPhase(state.VisitMaxClipTime) + "/" + FormatPhase(state.LastClipLength)
            + " frames=" + frames
            + " win=" + state.ActiveWindow.ToString("F3")
            + " next=" + next
            + (state.VisitShotCount > 0
                ? " shots=" + state.VisitShotCount + "@" + FormatPhase(state.VisitShotCtMax)
                : ""));
    }

    /// <summary>
    /// 事件锚定的释放表现：只在允许的原生动作上覆盖自有帧，其他情况立即取消。
    /// - 取消：Walk/Run（不冻结移动）、Shoot（原生自己就播 26..30，优先原生）、Unknown（含停用/非法/回退）、
    ///   新的 Prepare 进入（新一次拉弓接管），以及时长走完（0.18s 有界）。
    /// - 保留：Prepare 尾巴（放箭当帧原生可能还没切状态）与 Stand（perfect 弹跳过 Shoot 时的落点）。
    /// 不写 Animator、不动位置/朝向、不驱动 locomotion；两个英雄各自独立。
    /// </summary>
    private static void UpdateRelease(VisualState state, in HeroArcherNativePoseSample sample,
        HeroArcherNativeAction previousAction)
    {
        if (!state.ReleaseActive) return;
        bool cancel = sample.Action == HeroArcherNativeAction.Unknown
            || sample.Action == HeroArcherNativeAction.Shoot
            || sample.Action == HeroArcherNativeAction.Walk
            || sample.Action == HeroArcherNativeAction.Run
            || (sample.Action == HeroArcherNativeAction.Prepare
                && (sample.Action != previousAction || state.PrepareRestarted));   // 新一次拉弓（含同状态回绕）
        if (cancel)
        {
            state.ReleaseActive = false;
            return;
        }
        if (state.ReleaseFirstFrame)
        {
            state.ReleaseFirstFrame = false;
            return; // Show the release key pose before advancing, including slow frames.
        }
        float dt = Time.deltaTime;   // 只用游戏时间：暂停(dt=0)时释放表现一起冻结
        if (dt <= 0f) return;
        state.ReleaseElapsed += dt;
        if (state.ReleaseElapsed >= ReleasePresentationSeconds) state.ReleaseActive = false;
    }

    /// <summary>释放表现帧：0.18s 内从 26（拉满）走到 30（收势），越界钳制、非法输入取首帧。</summary>
    internal static int ReleaseFrameFor(float elapsedSeconds)
    {
        int index = 0;
        if (float.IsFinite(elapsedSeconds) && elapsedSeconds > 0f)
        {
            index = (int)(elapsedSeconds / ReleasePresentationSeconds * HeroArcherNativePose.ShootFrameCount);
            if (index >= HeroArcherNativePose.ShootFrameCount) index = HeroArcherNativePose.ShootFrameCount - 1;
            if (index < 0) index = 0;
        }
        return HeroArcherNativePose.ShootFirstFrame + index;
    }

    /// <summary>有界生命周期诊断（≤60 行/自 Clear 起）：attach/detach 配对暴露身份抖动导致的重建节奏。</summary>
    private static void LogLife(string message)
    {
        if (_lifeLines >= LifeLogCapacity) return;
        _lifeLines++;
        Log("[HeroArcherVisualLife] t=" + Now().ToString("F3") + " frame=" + FrameCount() + " " + message);
    }

    private static string FormatPhase(float value)
        => float.IsFinite(value) ? value.ToString("F3") : "nonfinite";

    private static Animator ReadAnimator(Archer archer)
    {
        try { return archer != null ? archer._animator : null; }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// 整体 0.9 表现缩放（只写自有 body/cloth 的 local x/y；z 保持 1，不影响 HeroVisualPriority 深度/置前，
    /// 也不写任何原生 transform）。body 的 sprite pivot 在脚 → 以 pivot 缩小即脚点着地、位置继承不变；
    /// cloth root 被 Cloth.Create/Tick/ApplyFlip 按全尺寸重写 → 本方法每次都以**绝对值**重断言
    /// （绝不乘当前 scale，重复调用幂等），肩部锚点偏移 ×0.9（身体变小飘带不悬空），flip 符号按
    /// reference.flipX 重取（与 Cloth 自身规则同源），不改原生朝向。
    /// </summary>
    private static bool ApplyVisualScale(VisualState state)
    {
        try
        {
            Transform body = state.Root != null ? state.Root.transform : null;
            if (body == null) return false;
            body.localScale = new Vector3(HeroArcherMotion.VisualScale, HeroArcherMotion.VisualScale, 1f);

            HeroArcherCloth.Handle cloth = state.Cloth;
            if (cloth == null || cloth.RootTransform == null) return false;
            bool flip = state.Own != null && state.Own.flipX;
            Vector3 origin = body.localPosition; // Cloth 的 reference 即自有 body root：localPosition 同源
            (float x, float y) anchor = HeroArcherMotion.ClothRootLocal(origin.x, origin.y, flip);
            float lift = HeroArcherPoseAtlas.TorsoLiftPixels(state.LastFrame)
                * HeroArcherMotion.VisualScale / HeroArcherPoseAtlas.PixelsPerUnit;
            cloth.RootTransform.localPosition = new Vector3(anchor.x, anchor.y + lift, origin.z);
            cloth.RootTransform.localScale = new Vector3(
                flip ? -HeroArcherMotion.VisualScale : HeroArcherMotion.VisualScale,
                HeroArcherMotion.VisualScale,
                1f);
            return true;
        }
        catch (Exception)
        {
            return false; // Caller removes the entire owned visual if any scale write fails.
        }
    }

    private static SpriteRenderer FindNativeRenderer(Archer archer)
    {
        try
        {
            SpriteRenderer renderer = archer.gameObject.GetComponent<SpriteRenderer>();
            if (renderer != null) return renderer;
            return archer.GetComponentInChildren<SpriteRenderer>();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>惰性加载 embedded atlas（只一次）；尺寸/alpha 校验失败即整块不可用。</summary>
    private static bool EnsureAtlas()
    {
        if (_atlasState == AtlasState.Ready) return true;
        if (_atlasState == AtlasState.Unavailable) return false;

        _atlasState = AtlasState.Unavailable; // 先置失败：任何异常都保持原版外观
        try
        {
            Assembly assembly = typeof(HeroArcherVisuals).Assembly;
            using (System.IO.Stream stream = assembly.GetManifestResourceStream(ResourceName))
            {
                if (stream == null)
                {
                    LogOnceUnavailable("atlas resource missing: " + ResourceName);
                    return false;
                }
                long length = stream.Length;
                if (length <= 0 || length > 4L * 1024L * 1024L)
                {
                    LogOnceUnavailable("atlas size rejected: " + length + " bytes");
                    return false;
                }
                byte[] bytes = new byte[(int)length];
                int read = 0;
                while (read < bytes.Length)
                {
                    int step = stream.Read(bytes, read, bytes.Length - read);
                    if (step <= 0) break;
                    read += step;
                }
                if (read != bytes.Length)
                {
                    LogOnceUnavailable("atlas truncated: " + read + "/" + bytes.Length);
                    return false;
                }
                return DecodeAtlas(bytes);
            }
        }
        catch (Exception e)
        {
            LogOnceUnavailable("atlas load failed: " + e.GetType().Name);
            return false;
        }
    }

    private static bool DecodeAtlas(byte[] bytes)
    {
        Texture2D texture = null;
        try
        {
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, bytes, false))
            {
                LogOnceUnavailable("ImageConversion.LoadImage returned false");
                DestroyQuietly(texture);
                return false;
            }
            if (texture.width != AtlasWidth || texture.height != AtlasHeight)
            {
                LogOnceUnavailable("atlas dimensions " + texture.width + "x" + texture.height
                    + " != " + AtlasWidth + "x" + AtlasHeight);
                DestroyQuietly(texture);
                return false;
            }

            Color32[] pixels = texture.GetPixels32();
            if (pixels == null || pixels.Length != AtlasWidth * AtlasHeight)
            {
                LogOnceUnavailable("atlas pixel read failed");
                DestroyQuietly(texture);
                return false;
            }
            bool anyVisible = false;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].a != 0) { anyVisible = true; break; }
            }
            if (!anyVisible)
            {
                LogOnceUnavailable("atlas fully transparent");
                DestroyQuietly(texture);
                return false;
            }

            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.anisoLevel = 0;

            Sprite[] sprites = new Sprite[HeroArcherPoseAtlas.FrameCount];   // 31：末格空白，不建 sprite
            Vector2 pivot = new Vector2(PivotPixelX / CellWidth, PivotPixelY / CellHeight);
            for (int frame = 0; frame < sprites.Length; frame++)
            {
                if (!HeroArcherPoseAtlas.FrameToCell(frame, out int column, out int row)) continue;
                // Unity 纹理坐标原点在左下：atlas 行 0 在最上 → y 从底部倒算（bottom-left 行翻转）。
                float y = (HeroArcherPoseAtlas.Rows - 1 - row) * CellHeight;
                Rect rect = new Rect(column * CellWidth, y, CellWidth, CellHeight);
                sprites[frame] = Sprite.Create(texture, rect, pivot, PixelsPerUnit, 0u, SpriteMeshType.FullRect);
            }

            _atlas = texture;
            _sprites = sprites;
            _atlasState = AtlasState.Ready;
            return true;
        }
        catch (Exception e)
        {
            LogOnceUnavailable("atlas decode failed: " + e.GetType().Name);
            if (texture != null) DestroyQuietly(texture);
            return false;
        }
    }

    private static void EnsureDriverRegistered()
    {
        if (_driverRegistered) return;
        try
        {
            if (!ClassInjector.IsTypeRegisteredInIl2Cpp(typeof(HeroArcherVisualDriver)))
            {
                ClassInjector.RegisterTypeInIl2Cpp(typeof(HeroArcherVisualDriver));
            }
            _driverRegistered = true;
        }
        catch (Exception e)
        {
            if (!_loggedCopyFailure) { _loggedCopyFailure = true; Log("driver registration failed: " + e.GetType().Name); }
        }
    }

    private static void DestroyQuietly(UnityEngine.Object target)
    {
        try
        {
            if (target != null) UnityEngine.Object.Destroy(target);
        }
        catch (Exception)
        {
        }
    }

    private static int SafeGoId(Archer archer)
    {
        try { return archer.gameObject != null ? archer.gameObject.GetInstanceID() : 0; }
        catch (Exception) { return 0; }
    }

    private static IntPtr SafePointer(Archer archer)
    {
        try { return archer.Pointer; }
        catch (Exception) { return IntPtr.Zero; }
    }

    private static float Now()
    {
        try { return Time.time; }
        catch (Exception) { return 0f; }
    }

    private static int FrameCount()
    {
        try { return Time.frameCount; }
        catch (Exception) { return int.MinValue; }   // 取不到帧号：不做去重，宁可多跑一次也不停帧
    }

    private static void LogOnceUnavailable(string message)
    {
        if (_loggedUnavailable) return;
        _loggedUnavailable = true;
        Log(message);
    }

    private static void Log(string message)
    {
        try { KingdomEnhancedPlugin.Instance?.LogSource?.LogInfo("[HeroArcherVisuals] " + message); }
        catch (Exception) { }
    }
}

/// <summary>
/// 自有 LateUpdate 驱动（挂在自有子物体上，随英雄移除一起销毁；最多 2 个）。
/// 顶层 internal 类型：ClassInjector 注入要求可反射构造的 MonoBehaviour（IntPtr ctor）。
/// </summary>
internal sealed class HeroArcherVisualDriver : MonoBehaviour
{
    public HeroArcherVisualDriver(IntPtr ptr) : base(ptr) { }

    private void LateUpdate()
    {
        HeroArcherVisuals.Sync("driver");
    }
}
