// 火铳手·自有视觉（runtime slice）。
//
// 资源：embedded PNG `KingdomEnhancedMod.MusketeerAtlas.png`（12 列 × 6 行、单格 56x32、
//       整图 672x192、66 有效帧、PPU 32、单帧 pivot 像素 (31,2) = 脚点）。经
//       UnityEngine.ImageConversion.LoadImage 解码；Point / Clamp / 无 MipMap；只加载一次；
//       尺寸或内容校验失败 → 整块 fail-closed（保持原版外观，绝不留半个对象）。
//
// 挂点：自有 SpriteRenderer 挂在**原生 renderer 的 Transform 之下**，localPosition 零 /
//       localRotation identity / localScale one —— 位置、朝向（父链 localScale.x 符号）、
//       缩放全部自然继承，绝不复制一次性的世界坐标、不改父链任何 local 值、
//       **不做任何额外缩放**（普通量产职业，不套英雄的 0.9/置前平面）。
//
// 原生隐藏：native.forceRenderingOff = true（CAS：凭据 HidNative，只写我们写过的 true，
//       归还只写 false）。任何一步失败整组回滚（销毁自有子物体 + 归还原生渲染）。
//       原生本来不可见（enabled=false / 第三方 forceRenderingOff）→ 不接管。
//
// 帧选择：每帧读原生 Animator 当前 state（shortNameHash + unwrapped normalizedTime + clip 长度）
//       喂 <see cref="MusketeerAnimationState"/>：locomotion 用 "(nt × clip 长度) mod authored"，
//       枪械表现由真实射击事件 + runtime 的 nextEligibleShotTime 驱动；暂停/相位冻结时整机冻结；
//       未绘制/不可读状态（Ghost Die/Spawn/其它世界名）→ 挂起自有渲染并归还原生，
//       绝不把"身体显示不出来"当成身份丢失、也绝不猜动作。
//
// 生命周期：Apply / Remove / Sync / NotifyShot / Clear，全部异常隔离；表按 GameObject
//       InstanceID 索引（≤ MaxUnits），无场景扫描、无协程、无逐帧分配。

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>未显示自有帧的原因（诊断；同时决定是否归还原生 forceRenderingOff）。</summary>
internal enum MusketeerVisualFallback
{
    None = 0,
    NoAnimator = 1,
    UnknownState = 2,
    Unreadable = 3,
    NativeHidden = 4,
    SpriteUnavailable = 5,
    NotArmed = 6,
    /// <summary>原生 inert/grabbed（被抓起、免疫等）：自有表现让位，归还原生。</summary>
    InertOrGrabbed = 7,
}

internal static class MusketeerVisuals
{
    private const string ResourceName = "KingdomEnhancedMod.MusketeerAtlas.png";
    private const int AtlasWidth = MusketeerAtlas.Columns * MusketeerAtlas.CellWidth;    // 672
    private const int AtlasHeight = MusketeerAtlas.Rows * MusketeerAtlas.CellHeight;     // 192

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
        internal GameObject Root;
        internal SpriteRenderer Own;
        internal SpriteRenderer Native;
        internal bool HidNative;
        internal int LastFrame = -1;
        internal MusketeerAction LastAction = MusketeerAction.Idle;
        internal int NativeStateHash;
        internal float NativeNormalizedTime;
        internal MusketeerVisualFallback LastFallback;
        internal readonly MusketeerAnimationState Animation = new MusketeerAnimationState();
        /// <summary>池复用后旧 life 的表现已退场：它不是"当前 life 的视觉"，也绝不代表当前 life 隐藏原生。</summary>
        internal bool Detached;
    }

    private static readonly Dictionary<int, VisualState> Visuals = new Dictionary<int, VisualState>(8);
    private static readonly List<int> Scratch = new List<int>(8);
    /// <summary>BeforeReuse 里归还 native forceRenderingOff 失败的状态（回调里不能重试/销毁）：保留凭据。</summary>
    private static readonly List<VisualState> PendingReleases = new List<VisualState>(4);
    /// <summary>BeforeReuse 里只能先摘下的自有根（回调里绝不 Destroy）：下一次 Sync 收尾销毁。</summary>
    private static readonly List<GameObject> PendingRetireRoots = new List<GameObject>(4);
    private const int MaxPendingReleases = 16;
    private static readonly MaterialPropertyBlock PropertyBlock = new MaterialPropertyBlock();

    private static AtlasState _atlasState = AtlasState.Unknown;
    private static Texture2D _atlas;
    private static Sprite[] _sprites;
    private static int _lastSyncedFrame = int.MinValue;
    private static bool _loggedUnavailable;
    private static bool _loggedFailure;
    private static bool _loggedReleaseBacklog;

    // 原生控制器状态名（与 hero native-follow 研究同一批名字；表外一律 Unknown）。
    private static readonly int HashStand = Animator.StringToHash("Stand");
    private static readonly int HashWalk = Animator.StringToHash("Walk");
    private static readonly int HashRun = Animator.StringToHash("Run");
    private static readonly int HashIdle = Animator.StringToHash("Idle");
    private static readonly int HashPrepare = Animator.StringToHash("Prepare");
    private static readonly int HashShoot = Animator.StringToHash("Shoot");

    /// <summary>当前有自有视觉的火铳手数。</summary>
    internal static int Count => Visuals.Count;

    /// <summary>该火铳手是否已有**当前 life** 的自有视觉（runtime 补挂载用；已退场的旧 life 状态不算）。</summary>
    internal static bool HasVisual(Archer archer)
    {
        try
        {
            if (archer == null || archer.gameObject == null) return false;
            int goId = SafeGoId(archer);
            return goId != 0 && Visuals.TryGetValue(goId, out VisualState state) && SameObject(state, archer)
                && !state.Detached && state.Own != null && state.Root != null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 池复用/OnEnable 回调安全的重置点（runtime 的 `Archer.OnEnable` prefix 调用；**可能位于物理回调里**）：
    /// 无论新 life 是否仍是火铳手，都先让旧 life 的自有表现彻底退场——
    /// 1) 清掉动作/射击时钟（Fire/Reload/idle 相位绝不跨 life，`Animation.Reset()`）；
    /// 2) 隐藏自有 renderer（只写 enabled，不 Destroy）；
    /// 3) 让出 GameObject 槽位（`Detached`）——`HasVisual`/`Sync` 因此绝不再把它当成当前 life 的视觉，
    ///    也绝不让它替当前 life 隐藏原生；
    /// 4) 归还原生 `forceRenderingOff`；失败保留凭据（PendingReleases）由下一次 Sync 重试，
    ///    自有根同样交给下一次 Sync 销毁（回调里绝不 Destroy）。
    /// O(1) 查表，不扫世界、不逐帧调用。
    /// </summary>
    internal static void BeforeReuse(Archer archer)
    {
        try
        {
            if (archer == null || archer.gameObject == null) return;
            int goId = SafeGoId(archer);
            if (goId == 0) return;
            if (!Visuals.TryGetValue(goId, out VisualState state)) return;
            DetachState(state);   // 同一对象也照样退场（绝不沿用旧 life 的时钟/遮蔽责任）
        }
        catch (Exception e)
        {
            if (!_loggedFailure)
            {
                _loggedFailure = true;
                Log("before-reuse failed: " + e.GetType().Name);
            }
        }
    }

    /// <summary>
    /// 回调安全的退场（BeforeReuse 的实际动作）：复位时钟 → 隐藏自有 renderer → 置 Detached 并让出槽位 →
    /// 归还原生隐藏（失败保留凭据供 Sync 重试）→ 自有根延迟到 Sync 销毁（回调里绝不 Destroy）。
    /// </summary>
    private static void DetachState(VisualState state)
    {
        if (state == null) return;
        state.Animation.Reset();     // 时钟绝不跨 life
        state.LastFrame = -1;
        HideOwn(state);              // 立即不可见（回调里只置 enabled）
        state.Detached = true;
        Visuals.Remove(state.GoId);  // 让出槽位：新 life 可以有自己的视觉

        if (ReleaseNativeHide(state))
        {
            GameObject root = state.Root;
            state.Root = null;
            state.Own = null;
            state.Native = null;
            state.Ref = null;
            RetireRootDeferred(root);
        }
        else
        {
            // 归还 native 隐藏责任失败：保留凭据（含 Native/Ref）继续重试，绝不把它当成新 life 的。
            if (PendingReleases.Count >= MaxPendingReleases && !_loggedReleaseBacklog)
            {
                _loggedReleaseBacklog = true;
                Log("pending native-hide releases piling up; receipts are kept for retry");
            }
            if (!PendingReleases.Contains(state)) PendingReleases.Add(state);
        }
    }

    /// <summary>回调里只能先摘下的自有根：交给下一次 Sync（LateUpdate，非物理回调）销毁。</summary>
    private static void RetireRootDeferred(GameObject root)
    {
        if (root == null) return;
        if (!PendingRetireRoots.Contains(root)) PendingRetireRoots.Add(root);
    }

    private static void HideOwn(VisualState state)
    {
        try
        {
            if (state.Own != null) state.Own.enabled = false;
        }
        catch (Exception)
        {
        }
    }

    /// <summary>延迟收尾（Sync 的安全点）：销毁待销毁的自有根 + 重试失败的原生隐藏归还。</summary>
    private static void ProcessDeferred()
    {
        for (int i = PendingRetireRoots.Count - 1; i >= 0; i--)
        {
            GameObject root = PendingRetireRoots[i];
            if (root == null)
            {
                PendingRetireRoots.RemoveAt(i);
                continue;
            }
            try
            {
                UnityEngine.Object.Destroy(root);
                PendingRetireRoots.RemoveAt(i);
            }
            catch (Exception)
            {
                // 保留凭据，下次再试。
            }
        }

        for (int i = PendingReleases.Count - 1; i >= 0; i--)
        {
            VisualState state = PendingReleases[i];
            if (state == null)
            {
                PendingReleases.RemoveAt(i);
                continue;
            }
            if (!ReleaseNativeHide(state)) continue;   // 成功才销账
            PendingReleases.RemoveAt(i);
            GameObject root = state.Root;
            state.Root = null;
            state.Own = null;
            state.Native = null;
            state.Ref = null;
            DestroyOwnRoot(root);                      // Sync 是安全点：延迟下来的自有根在这里真正销毁
        }
    }

    /// <summary>销毁自有根（安全点用）；失败则转延迟收尾继续持有引用。</summary>
    private static void DestroyOwnRoot(GameObject root)
    {
        if (root == null) return;
        try
        {
            UnityEngine.Object.Destroy(root);
        }
        catch (Exception)
        {
            RetireRootDeferred(root);
        }
    }

    /// <summary>该 GameObject 是否还有旧 life 未完成的原生隐藏归还（有则先不装新视觉，原生保持可见）。</summary>
    private static bool HasPendingRelease(int goId)
    {
        for (int i = 0; i < PendingReleases.Count; i++)
        {
            VisualState state = PendingReleases[i];
            if (state != null && state.GoId == goId) return true;
        }
        return false;
    }

    /// <summary>测试钩子：注入整套帧精灵（测试程序集没有嵌入图集）；null = 回到"未加载"。</summary>
    internal static void SetAtlasForTests(Sprite[] sprites)
    {
        _sprites = sprites;
        _atlasState = sprites != null && sprites.Length >= MusketeerAtlas.FrameCount
            ? AtlasState.Ready
            : AtlasState.Unknown;
        if (_atlasState == AtlasState.Unknown)
        {
            _loggedUnavailable = false;
            _loggedFailure = false;
        }
    }

    /// <summary>为火铳手建立自有视觉（幂等）。全部步骤"先就绪、后隐藏"：任一步失败原版外观保持。</summary>
    internal static void Apply(Archer archer)
    {
        if (archer == null || archer.gameObject == null) return;
        int goId = SafeGoId(archer);
        if (goId == 0) return;
        if (HasPendingRelease(goId)) return;   // 旧 life 的隐藏归还还没成功：先不装（原生可见 = 安全）
        if (Visuals.TryGetValue(goId, out VisualState existing))
        {
            if (SameObject(existing, archer) && !existing.Detached) return;
            RemoveByKey(goId);
            if (Visuals.ContainsKey(goId)) return;
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
        if (!nativeVisible || nativeForceOff) return;   // 原生已隐藏：不接管
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

        VisualState state = new VisualState
        {
            Ref = archer,
            Pointer = SafePointer(archer),
            GoId = goId,
            Native = native,
        };
        try
        {
            state.Root = new GameObject("KEM_MusketeerSprite");
            state.Own = state.Root.AddComponent<SpriteRenderer>();
            if (state.Own == null) throw new InvalidOperationException("sprite renderer missing");

            state.Root.transform.SetParent(anchor, false);
            state.Root.transform.localPosition = Vector3.zero;
            state.Root.transform.localRotation = Quaternion.identity;
            state.Root.transform.localScale = Vector3.one;

            CopyRendererLook(state);
            Visuals[goId] = state;
            ApplyFrame(state, -1, MusketeerVisualFallback.NotArmed);   // 首帧前先挂起（下一帧 Sync 接管）
        }
        catch (Exception e)
        {
            Rollback(state);
            if (!_loggedFailure)
            {
                _loggedFailure = true;
                Log("apply failed: " + e.GetType().Name);
            }
        }
    }

    /// <summary>移除自有视觉：CAS 归还原生 forceRenderingOff + 销毁自有子物体。</summary>
    internal static void Remove(Archer archer)
    {
        try
        {
            if (archer == null || archer.gameObject == null) return;
            int goId = SafeGoId(archer);
            if (goId == 0) return;
            if (!Visuals.TryGetValue(goId, out VisualState state)) return;
            if (!SameObject(state, archer)) return;
            RemoveByKey(goId);
        }
        catch (Exception)
        {
        }
    }

    /// <summary>ModPanel.LateUpdate 接线（必须在原生 Animator 产出本帧结果之后）；同帧多入口只跑一次。</summary>
    internal static void Sync()
    {
        try
        {
            ProcessDeferred();   // 回调里不能销毁/需要重试的收尾：这里才是安全点（即使当前没有视觉也要跑）
            if (Visuals.Count == 0) return;
            int frame = FrameCount();
            if (frame == _lastSyncedFrame) return;
            _lastSyncedFrame = frame;

            Scratch.Clear();
            foreach (KeyValuePair<int, VisualState> pair in Visuals) Scratch.Add(pair.Key);
            for (int i = 0; i < Scratch.Count; i++)
            {
                if (!Visuals.TryGetValue(Scratch[i], out VisualState state)) continue;
                if (!Valid(state)) { RemoveByKey(state.GoId); continue; }
                CopyRendererLook(state);
                ApplyNativePose(state);
            }
        }
        catch (Exception)
        {
        }
    }

    /// <summary>真实射击事件（子弹出膛那一帧）：锚定 Fire 窗口与 Reload 窗口。</summary>
    internal static void NotifyShot(Archer archer, float nextEligibleShotTime)
    {
        try
        {
            if (archer == null || archer.gameObject == null) return;
            int goId = SafeGoId(archer);
            if (goId == 0 || !Visuals.TryGetValue(goId, out VisualState state)) return;
            if (!SameObject(state, archer)) return;
            double now = NowSeconds();
            state.Animation.NotifyShot(now, nextEligibleShotTime);
        }
        catch (Exception)
        {
        }
    }

    /// <summary>
    /// 整体清理（功能关/卸载）：归还原生 + 销毁自有对象。**只移除成功归还的条目**——
    /// `RemoveByKey` 归还失败（瞬时异常）时保留回执，由后续 Sync/Clear 继续重试，绝不丢责任。
    /// </summary>
    internal static void Clear()
    {
        ProcessDeferred();   // 先收尾回调里留下的收尾（销毁自有根/重试归还）
        Scratch.Clear();
        foreach (KeyValuePair<int, VisualState> pair in Visuals) Scratch.Add(pair.Key);
        for (int i = 0; i < Scratch.Count; i++) RemoveByKey(Scratch[i]);
        Scratch.Clear();
    }

    // ============================================================
    // 原生采样与接管决策
    // ============================================================

    private static void ApplyNativePose(VisualState state)
    {
        SpriteRenderer native = state.Native;
        SpriteRenderer own = state.Own;
        if (native == null || own == null) return;

        bool nativeEnabled;
        bool nativeForceOff;
        try
        {
            nativeEnabled = native.enabled;
            nativeForceOff = native.forceRenderingOff;
        }
        catch (Exception)
        {
            return;   // 原生不可读：本帧完全不动（保留上次状态），下一帧重试
        }

        MusketeerMotion motion = MusketeerMotion.Unknown;
        double now = NowSeconds();
        float delta = DeltaSeconds();
        bool aiming = false;
        bool readable = false;
        bool hasAnimator = false;
        try
        {
            Animator animator = state.Ref != null ? state.Ref._animator : null;
            hasAnimator = animator != null && animator.isActiveAndEnabled;
            if (hasAnimator)
            {
                AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
                int hash = info.shortNameHash;
                float normalizedTime = info.normalizedTime;
                float clipLength = info.length;
                motion = ClassifyNativeState(hash);
                state.NativeStateHash = hash;
                state.NativeNormalizedTime = normalizedTime;
                state.Animation.SetMotion(motion, now, normalizedTime, clipLength, delta);
                readable = motion != MusketeerMotion.Unknown;
            }
            else
            {
                state.Animation.SetMotion(MusketeerMotion.Unknown, now, 0d, 0d, delta);
            }

            aiming = motion == MusketeerMotion.Gun || HasGroundTarget(state.Ref);
            state.Animation.SetAiming(aiming, now);
        }
        catch (Exception)
        {
            state.Animation.SetMotion(MusketeerMotion.Unknown, now, 0d, 0d, delta);
        }

        int frame = state.Animation.Tick(now);
        MusketeerVisualFallback reason = MusketeerVisualFallback.None;
        if (!readable && motion != MusketeerMotion.Gun)
            reason = hasAnimator ? MusketeerVisualFallback.UnknownState : MusketeerVisualFallback.NoAnimator;
        if (frame < 0 && reason == MusketeerVisualFallback.None) reason = MusketeerVisualFallback.UnknownState;

        // 「原生不可见」= renderer 被关，或被**第三方** forceRenderingOff；我们自己写下的隐藏不算。
        bool nativeHidden = !nativeEnabled || (nativeForceOff && !state.HidNative);
        if (nativeHidden && reason == MusketeerVisualFallback.None) reason = MusketeerVisualFallback.NativeHidden;
        if (!NormalCharacter(state) && reason == MusketeerVisualFallback.None)
            reason = MusketeerVisualFallback.InertOrGrabbed;
        bool show = frame >= 0 && !nativeHidden && reason == MusketeerVisualFallback.None;

        ApplyFrame(state, show ? frame : -1, reason);
    }

    /// <summary>原生 Character 处于可接管状态（非 inert/grabbed）时为 true；读不到 = false（fail-closed）。</summary>
    private static bool NormalCharacter(VisualState state)
    {
        try
        {
            Character character = state.Ref != null ? state.Ref._character : null;
            if (character == null) return false;      // 读不到：不接管
            return !character.inert && !character.grabbed;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void ApplyFrame(VisualState state, int frame, MusketeerVisualFallback reason)
    {
        SpriteRenderer own = state.Own;
        SpriteRenderer native = state.Native;
        if (own == null) return;

        bool show = frame >= 0;
        if (show)
        {
            Sprite sprite = _sprites != null && frame < _sprites.Length ? _sprites[frame] : null;
            if (sprite == null)
            {
                show = false;
                reason = MusketeerVisualFallback.SpriteUnavailable;
            }
            else
            {
                try
                {
                    if (state.LastFrame != frame)
                    {
                        own.sprite = sprite;   // 先有 sprite 才允许可见：绝不留"可见但空帧"
                        state.LastFrame = frame;
                    }
                }
                catch (Exception)
                {
                    show = false;
                    reason = MusketeerVisualFallback.Unreadable;
                }
            }
        }

        if (show && !state.HidNative)
        {
            try
            {
                state.HidNative = true;   // 先记责任再写 setter（写一半抛异常也能归还）
                native.forceRenderingOff = true;
            }
            catch (Exception)
            {
                show = false;
                reason = MusketeerVisualFallback.Unreadable;
            }
        }

        if (show)
        {
            try { own.enabled = true; }
            catch (Exception)
            {
                show = false;
                reason = MusketeerVisualFallback.Unreadable;
            }
        }

        if (!show)
        {
            try { own.enabled = false; } catch (Exception) { }
            // 原生本就不可见（NativeHidden）时保留隐藏凭据，避免原生短暂不可见期间来回抢/放。
            if (reason != MusketeerVisualFallback.NativeHidden) ReleaseNativeHide(state);
        }
        state.LastAction = state.Animation.CurrentAction;
        state.LastFallback = reason;
    }

    /// <summary>原生 state shortNameHash → 火铳手归类（表外 = Unknown → 挂起自有渲染）。</summary>
    private static MusketeerMotion ClassifyNativeState(int hash)
    {
        if (hash == 0) return MusketeerMotion.Unknown;
        if (hash == HashStand || hash == HashIdle) return MusketeerMotion.Idle;
        if (hash == HashWalk) return MusketeerMotion.Walk;
        if (hash == HashRun) return MusketeerMotion.Run;
        if (hash == HashPrepare || hash == HashShoot) return MusketeerMotion.Gun;
        return MusketeerMotion.Unknown;
    }

    /// <summary>本帧是否处于"举枪姿态"：原生射击状态，或当前瞄准的是合法地面敌人。</summary>
    private static bool HasGroundTarget(Archer archer)
    {
        try
        {
            if (archer == null || archer.gameObject == null) return false;
            GameObject target = archer._shootingTarget;
            return MusketeerFoeFilter.IsValidGroundFoe(target, archer.gameObject);
        }
        catch (Exception)
        {
            return false;
        }
    }

    // ============================================================
    // 渲染器所有权 / 表管理
    // ============================================================

    private static void CopyRendererLook(VisualState state)
    {
        SpriteRenderer native = state.Native;
        SpriteRenderer own = state.Own;
        if (native == null || own == null) return;
        try
        {
            own.enabled = native.enabled;      // 原生隐藏（SetHideStatus/石化）时跟随；恢复时跟着恢复
            own.sortingLayerID = native.sortingLayerID;
            own.sortingOrder = native.sortingOrder;
            own.flipX = native.flipX;
            own.flipY = native.flipY;
            own.color = native.color;          // 命中闪白/染色/石化色一起继承（原始调色以原生为准）
            GameObject ownObject = own.gameObject;
            GameObject nativeObject = native.gameObject;
            if (ownObject != null && nativeObject != null && ownObject.layer != nativeObject.layer)
                ownObject.layer = nativeObject.layer;
        }
        catch (Exception)
        {
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
            native.GetPropertyBlock(PropertyBlock);
            own.SetPropertyBlock(PropertyBlock);
        }
        catch (Exception)
        {
        }
    }

    private static bool Valid(VisualState state)
    {
        try
        {
            if (state.Detached) return false;   // 旧 life 已退场：绝不代表当前 life
            Archer archer = state.Ref;
            if (archer == null || archer.gameObject == null) return false;
            if (SafeGoId(archer) != state.GoId) return false;
            if (SafePointer(archer) != state.Pointer) return false;
            if (!MusketeerRuntime.IsMusketeer(archer) || !MusketeerRuntime.IsArmedMusketeer(archer)) return false;
            return state.Native != null && state.Own != null && state.Root != null;
        }
        catch (Exception)
        {
            return false;
        }
    }

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

    private static void RemoveByKey(int goId)
    {
        if (!Visuals.TryGetValue(goId, out VisualState state)) return;
        if (!ReleaseNativeHide(state)) return;   // 归还失败：保留凭据下一帧重试（绝不丢责任）
        Visuals.Remove(goId);
        state.Animation.Reset();
        if (state.Root != null)
        {
            DestroyQuietly(state.Root);
            state.Root = null;
        }
        state.Own = null;
        state.Native = null;
        state.Ref = null;
    }

    /// <summary>只归还「我们写过的」原生 forceRenderingOff（CAS：凭据 HidNative，只写 false）。</summary>
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
                && SafeGoId(archer) == state.GoId && SafePointer(archer) == state.Pointer;
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
        if (!ReleaseNativeHide(state)) { Visuals[state.GoId] = state; return; }
        if (Visuals.TryGetValue(state.GoId, out VisualState current) && ReferenceEquals(current, state))
            Visuals.Remove(state.GoId);
        if (state.Root != null)
        {
            DestroyQuietly(state.Root);
            state.Root = null;
        }
        state.Own = null;
        state.Native = null;
        state.Ref = null;
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

    /// <summary>惰性解码 embedded atlas（只一次）；尺寸/内容校验失败即整块不可用。</summary>
    private static bool EnsureAtlas()
    {
        if (_atlasState == AtlasState.Ready) return true;
        if (_atlasState == AtlasState.Unavailable) return false;
        _atlasState = AtlasState.Unavailable;   // 先置失败：任何异常都保持原版外观
        try
        {
            Assembly assembly = typeof(MusketeerVisuals).Assembly;
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

            Sprite[] sprites = new Sprite[MusketeerAtlas.FrameCount];
            Vector2 pivot = new Vector2(MusketeerAtlas.PivotPixelX / MusketeerAtlas.CellWidth,
                MusketeerAtlas.PivotPixelY / MusketeerAtlas.CellHeight);
            for (int frame = 0; frame < sprites.Length; frame++)
            {
                if (!MusketeerAtlas.FrameToCell(frame, out int column, out int row)) continue;
                // Unity 纹理原点在左下：atlas 行 0 在最上 → y 从底部倒算。
                float y = (MusketeerAtlas.Rows - 1 - row) * MusketeerAtlas.CellHeight;
                Rect rect = new Rect(column * MusketeerAtlas.CellWidth, y,
                    MusketeerAtlas.CellWidth, MusketeerAtlas.CellHeight);
                sprites[frame] = Sprite.Create(texture, rect, pivot, MusketeerAtlas.PixelsPerUnit,
                    0u, SpriteMeshType.FullRect);
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

    private static double NowSeconds()
    {
        try { return Time.time; }
        catch (Exception) { return 0d; }
    }

    private static float DeltaSeconds()
    {
        try { return Time.deltaTime; }
        catch (Exception) { return 0f; }
    }

    private static int FrameCount()
    {
        try { return Time.frameCount; }
        catch (Exception) { return int.MinValue; }
    }

    private static void LogOnceUnavailable(string message)
    {
        if (_loggedUnavailable) return;
        _loggedUnavailable = true;
        Log(message);
    }

    private static void Log(string message)
    {
        try { KingdomEnhancedPlugin.Instance?.LogSource?.LogWarning("[MusketeerVisuals] " + message); }
        catch (Exception) { }
    }
}
