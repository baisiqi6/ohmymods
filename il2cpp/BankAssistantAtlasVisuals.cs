using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 新四税收助手（槽位 4..7）的自有贴图显示层：把原生 banker Animator 的动作时钟映射到
/// BankAssistantAtlasMetadata 的四张固定图集，只在每个 LateUpdate 覆写 root SpriteRenderer.sprite。
///
/// 调用契约（跨 lane 冻结；调用方 = 现有 BankAssistantVisualLifecycle 的 LateUpdate / OnDisable）：
/// * <see cref="EnsureAssets"/>：模板注册前同步预检全部四张图集；任一不合格返回 false（fail-closed，
///   调用方不得登记新模板，绝不用原生 banker 皮冒充新皮肤）。
/// * <see cref="Tick"/>：仅槽位 4..7、当前希腊层/场景的活动 actor。只写 sprite；绝不写 renderer.enabled /
///   颜色 / transform / Animator / AI / 经济；每帧比较 renderer 的**真实当前 sprite**（原生 Animator 可能
///   刚覆写过），因此幂等且能持续纠正原生回写。
/// * <see cref="Forget"/>：本 life 结束（OnDisable / 回池）释放本实例状态（含闲暇时钟与帧缓存），
///   旧状态绝不污染下一 life；只删与自己 pointer 相符的条目，避免旧 life 回调误清新 life。
///
/// 原生采样（严格，不猜）：
/// * 实际 2.4 `resources.assets` AnimatorController 6129 "banker" 只有三个 state，各一个 clip：
///   Idle（nameID 2081823275, clip 908, loop, 15.1s）、Walk（765711723, 916, loop, 1.0s）、
///   Run（1748754976, 912, loop, 0.75s）；Walk/Run 不是混树。transition duration 0，
///   Idle→Walk Speed&gt;0.01、Walk→Idle&lt;0.01、Walk→Run&gt;1.0、Run→Walk&lt;1.0，natives 自己处理。
///   代码用 Animator.StringToHash 生成这三个 hash（与已装英雄/火铳同法，不手抄魔数）。
/// * state hash 不是这三个、缺 Animator、normalizedTime/length 非有限、length≤0 或相位为负：
///   取消本实例闲暇并**本帧不提交 sprite**（不猜另一动作、不用上一帧或第 0 帧冒充有效采样、无新 fallback）。
///
/// 步频时钟（2026-09-29 修订，详见 issue-89 plan）：Walk/Run 帧由本 life 的 double gaitPhase
/// 积分选择（dt×speed/strideWorld，帧=floor(phase×8)），不再映射 native clip 相位；Idle/Leisure
/// 仍走原时钟，Idle 只冻结相位不归零，Walk↔Run 共用同一 phase。推进门：非 IsWaiting(slot) 且
/// dt/timeScale/speed 均有限正（timeScale 归一前验证）；strideWorld = StridePixels/PPU×|lossyScale.x|
/// 逐帧只读，数值异常本帧不提交 sprite。速度：authority Speed 即 game 秒；客户端（Speed 由既有
/// PositionSync 补间写 |dx|/unscaledElapsed）除以 timeScale 归一；归一与 cap（3.2 展示上限）全程
/// double，积分先 %=1 防大 dt 吞小数。
///
/// 世界/时钟门：
/// * 仅 `GreekBankScope.IsActive && GreekBankScope.IsInCurrentLayer(actor)` 内创建/推进本实例状态；
///   层/场景/对象不匹配或换 world → 立即 Forget，不写、不继承旧闲暇。
/// * `Time.timeScale<=0`、`IslandSaveData.isSavingGame`、`Managers.Inst?.game?.state != Game.State.Playing`
///   时整帧冻结：不推进时钟（闲暇 delta 视为 0，不扣 cooldown、不补播）、不写 sprite，保留同 world 已知视觉状态。
///   `allowLeisure=false` 不是暂停门（那只表示当前无空闲资格），暂停/保存由上述真实门负责。
///
/// 资源为模块级缓存：只在第一次 EnsureAssets 解码一次（Point / Clamp / 无 mip），
/// 绝不每帧创建 Sprite，也不逐帧分配。4 张纹理与 128 个 sprite 在创建时即打
/// HideFlags.DontUnloadUnusedAsset——模块自有资源必须显式保留，才能跨场景的 unused-asset
/// 卸载存活；原场景曾报告帧不可用，但确切卸载者尚未验证（托管根是否参与 Unity 卸载扫描
/// 在本工程仍是待证假设，不得当作已实测结论）。保留位只在未使用资源回收时生效，不改变
/// 显式 Destroy / 进程退出释放的既有语义。
internal static class BankAssistantAtlasVisuals
{
    internal const int FirstSlot = 4;
    internal const int LastSlot = 7;

    private const int AtlasReady = 1;
    private const int AtlasFailed = 2;
    private const long MaxResourceBytes = 4L * 1024L * 1024L;

    private sealed class ActorState
    {
        internal IntPtr Pointer;
        internal SpriteRenderer Renderer;
        internal Animator Animator;
        internal CharacterLeisureClock Leisure;
        /// <summary>Walk/Run 共用步态相位（double，[0,1)）；Idle/冻结只停不归零，随本 life 丢弃。</summary>
        internal double GaitPhase;
    }

    private static readonly Dictionary<int, ActorState> States = new();
    private static readonly HashSet<string> Warned = new();
    private static readonly HashSet<int> FirstApplyLogged = new();
    private static Sprite[][] _sprites;
    private static Texture2D[] _textures;
    private static int _atlasState;

    private static readonly int HashIdle = Animator.StringToHash("Idle");
    private static readonly int HashWalk = Animator.StringToHash("Walk");
    private static readonly int HashRun = Animator.StringToHash("Run");
    /// <summary>banker 控制器 Speed 参数（步频速度来源，见类头）。</summary>
    private static readonly int HashSpeed = Animator.StringToHash("Speed");

    /// <summary>步速展示上限（units/game-sec）：归一后的 cap。</summary>
    private const float MaxDesignStepSpeed = 3.2f;

    internal static int LiveStateCount => States.Count;
    internal static bool AssetsReady => _atlasState == AtlasReady;

    /// <summary>
    /// 同步预检并解码全部四张图集（幂等）。任一资源缺失/超限/尺寸不符/整块不可见/某动作帧全透明
    /// → 全部视为失败并返回 false（已解码部分立即释放）；成功后返回 true 且之后零分配命中缓存。
    /// </summary>
    internal static bool EnsureAssets()
    {
        if (_atlasState == AtlasReady) return true;
        if (_atlasState == AtlasFailed) return false;
        _atlasState = AtlasFailed;   // 先置失败：任何异常都保持 fail-closed

        Sprite[][] sprites = null;
        Texture2D[] textures = null;
        try
        {
            BankAssistantAtlasStyle[] styles = BankAssistantAtlasMetadata.Styles;
            sprites = new Sprite[styles.Length][];
            textures = new Texture2D[styles.Length];
            for (int i = 0; i < styles.Length; i++)
            {
                if (!TryDecodeSheet(styles[i], out sprites[i], out textures[i]))
                {
                    WarnOnce("atlas prevalidation failed for style " + styles[i].Name
                        + "; new assistant templates must not be registered");
                    ReleasePartial(sprites, textures);
                    return false;
                }
            }
            _sprites = sprites;
            _textures = textures;
            _atlasState = AtlasReady;
            return true;
        }
        catch (Exception e)
        {
            WarnOnce("atlas prevalidation failed: " + e.GetType().Name);
            ReleasePartial(sprites, textures);
            return false;
        }
    }

    /// <summary>
    /// 每个 LateUpdate 一次的显示提交（槽位 4..7、当前希腊层的活动 actor）。只写 sprite；失败绝不抛给调用方。
    /// </summary>
    internal static void Tick(GameObject actor, int slot, bool allowLeisure)
    {
        try
        {
            if (actor == null || slot < FirstSlot || slot > LastSlot) return;
            if (!BankAssistantAtlasMetadata.TryGetStyle(slot, out BankAssistantAtlasStyle style)) return;
            // world/scope 门：非当前希腊层/场景/活动对象只清状态，不创建也不推进本实例状态。
            if (!GreekBankScope.IsActive || !GreekBankScope.IsInCurrentLayer(actor))
            {
                Forget(actor);
                return;
            }
            // 暂停/保存/非 Playing：整帧冻结（向闲暇时钟提供 0 语义 = 完全不触发），保留同 world 已知视觉状态。
            if (IsFrozen()) return;
            if (_atlasState != AtlasReady && !EnsureAssets()) return;
            Sprite[] sprites = _sprites != null ? _sprites[slot - FirstSlot] : null;
            if (sprites == null) return;

            int instanceId = actor.GetInstanceID();
            if (instanceId == 0) return;
            if (!States.TryGetValue(instanceId, out ActorState state) || state.Pointer != actor.Pointer)
            {
                state = new ActorState { Pointer = actor.Pointer };
                States[instanceId] = state;
            }
            if (state.Renderer == null) state.Renderer = actor.GetComponent<SpriteRenderer>();
            SpriteRenderer renderer = state.Renderer;
            if (renderer == null) return;
            if (state.Animator == null) state.Animator = actor.GetComponent<Animator>();
            Animator animator = state.Animator;

            if (!TryResolveNativeSample(animator, out BankAssistantAtlasAction action, out float phaseSeconds))
            {
                // 未知/不可读原生状态：取消闲暇并且本帧不提交 sprite（不猜动作、不用旧帧/首帧冒充）。
                if (state.Leisure != null) state.Leisure.Tick(Time.deltaTime, false);
                WarnOnce("native sample unresolved (state hash/phase unreadable); sprite held back this frame");
                return;
            }

            if (state.Leisure == null) state.Leisure = new CharacterLeisureClock(instanceId);
            // 闲暇必须同时：调用方许可 + 原生确认 Idle；许可撤销立即取消（不依赖 Idle 相位推进）。
            int leisureValue = state.Leisure.Tick(Time.deltaTime,
                allowLeisure && action == BankAssistantAtlasAction.Idle);

            int frame;
            if (leisureValue >= 0)
            {
                frame = (int)BankAssistantAtlasAction.Leisure * BankAssistantAtlasMetadata.FramesPerAction
                    + leisureValue % BankAssistantAtlasMetadata.FramesPerAction;
            }
            else if (action == BankAssistantAtlasAction.Walk || action == BankAssistantAtlasAction.Run)
            {
                // Walk/Run 帧由步频积分选择（见 TryTickGait）；stride/scale 数值异常本帧不提交。
                if (!TryTickGait(state, renderer, style, action, slot, out frame)) return;
            }
            else
            {
                frame = BankAssistantAtlasMetadata.FrameInAction(style, action, phaseSeconds);
            }

            if (frame < 0 || frame >= sprites.Length)
            {
                WarnOnce("atlas frame index out of range: style=" + style.Name + " slot=" + slot
                    + " frame=" + frame + "; sprite held back this frame");
                return;
            }
            Sprite desired = sprites[frame];
            if (desired == null)
            {
                // 区分两类原因：托管表项真为 null（从未写入）vs 代理仍在但原生对象已被回收
                // （Unity 运算符把已销毁对象判为 null；此处不推断确切卸载者）。Unity 运算符
                // 无法区分二者，ReferenceEquals 可以。两类原因都点名 slot/frame/style。
                string cause = ReferenceEquals(desired, null) ? "managed-null" : "native-destroyed";
                WarnOnce("atlas frame unavailable (" + cause + "): style=" + style.Name
                    + " slot=" + slot + " frame=" + frame + "; sprite held back this frame");
                return;
            }
            // 始终比较 renderer 的真实当前 sprite：原生 Animator 本帧可能刚写回原生帧。
            if (renderer.sprite != desired) renderer.sprite = desired;
            // 每 style 至多一行首用回执（max 4/进程）：区分"解码已装入缓存"与"确实提交过
            // renderer"，仅到 renderer 赋值为止，不代表实机可见性或玩法验收。
            if (FirstApplyLogged.Add(slot - FirstSlot))
            {
                Info("first atlas apply: style=" + style.Name + " slot=" + slot + " frame=" + frame
                    + " (sprite assigned to renderer; not in-game visibility evidence)");
            }
        }
        catch (Exception e)
        {
            WarnOnce("tick failed: " + e.GetType().Name);
        }
    }

    /// <summary>本 life 结束：按 instanceId + pointer 释放自有视觉状态；不触碰 renderer/animator。
    /// 旧 life 的滞后回调（pointer 已不同）不得清新 life 的状态。</summary>
    internal static void Forget(GameObject actor)
    {
        if (actor == null) return;
        try
        {
            int instanceId = actor.GetInstanceID();
            if (instanceId == 0) return;
            if (States.TryGetValue(instanceId, out ActorState state) && state.Pointer == actor.Pointer)
                States.Remove(instanceId);
        }
        catch (Exception)
        {
        }
    }

    /// <summary>暂停（timeScale≤0）、保存中或非 Playing：整帧冻结，不推进时钟也不写 sprite。</summary>
    private static bool IsFrozen()
    {
        try
        {
            return Time.timeScale <= 0f || IslandSaveData.isSavingGame
                || Managers.Inst?.game?.state != Game.State.Playing;
        }
        catch (Exception)
        {
            return true;
        }
    }

    /// <summary>
    /// 原生采样 → authored 动作 + 相位秒。返回 false = 无法识别（调用方取消闲暇且不提交 sprite）。
    /// 仅接受实际 banker controller 的三个 state hash；相位 = 未取 fract 的 normalizedTime × clipLength。
    /// </summary>
    private static bool TryResolveNativeSample(Animator animator, out BankAssistantAtlasAction action,
        out float phaseSeconds)
    {
        action = BankAssistantAtlasAction.Idle;
        phaseSeconds = 0f;
        if (animator == null) return false;
        int hash;
        float normalizedTime;
        float clipLength;
        try
        {
            AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
            hash = info.shortNameHash;
            normalizedTime = info.normalizedTime;
            clipLength = info.length;
        }
        catch (Exception)
        {
            return false;
        }

        if (hash == HashIdle) action = BankAssistantAtlasAction.Idle;
        else if (hash == HashWalk) action = BankAssistantAtlasAction.Walk;
        else if (hash == HashRun) action = BankAssistantAtlasAction.Run;
        else return false;

        if (!float.IsFinite(normalizedTime) || normalizedTime < 0f) return false;
        if (!float.IsFinite(clipLength) || clipLength <= 0f) return false;
        float seconds = normalizedTime * clipLength;
        if (!float.IsFinite(seconds) || seconds < 0f) return false;
        phaseSeconds = seconds;
        return true;
    }

    /// <summary>
    /// Walk/Run 步频推进 + 取帧。返回 false = stride/scale 数值异常，本帧不提交 sprite；
    /// 返回值异常（0/负/非有限速度、dt、timeScale）只冻结相位、仍提交当前相位帧。
    /// 读异常（scale/Speed/HasWorldAuth）直接上抛，由 Tick 既有外层 catch 整帧扣下——
    /// 不猜速度域，与 TryResolveNativeSample 读失败同一边界。积分全程 double，cap 在归一后。
    /// </summary>
    private static bool TryTickGait(ActorState state, SpriteRenderer renderer, BankAssistantAtlasStyle style,
        BankAssistantAtlasAction action, int slot, out int frame)
    {
        float stridePx = action == BankAssistantAtlasAction.Walk
            ? style.WalkStridePixels : style.RunStridePixels;
        float lossyX = renderer.transform.lossyScale.x;
        if (!float.IsFinite(stridePx) || stridePx <= 0f
            || !float.IsFinite(style.PixelsPerUnit) || style.PixelsPerUnit <= 0f
            || !float.IsFinite(lossyX) || lossyX == 0f)
        {
            WarnOnce("gait stride/scale invalid (style=" + style.Name + " slot=" + slot
                + "); sprite held back this frame");
            frame = 0;
            return false;
        }
        double strideWorld = (double)stridePx / style.PixelsPerUnit * Math.Abs((double)lossyX);

        // 速度/timeScale/dt 都先在原始 float 上验有限正，归一与 cap 全程 double：
        // 客户端 (double)speed/(double)scale 不会在极端 float 组合下先溢出成 inf。
        float speed = state.Animator.GetFloat(HashSpeed);
        float scale = Time.timeScale;
        float dt = Time.deltaTime;
        bool advance = !BankAssistantTeleportVisuals.IsWaiting(slot)
            && float.IsFinite(dt) && dt > 0f
            && float.IsFinite(scale) && scale > 0f
            && float.IsFinite(speed) && speed > 0f;
        if (advance)
        {
            double normalized = speed;
            if (!NetworkBigBoss.HasWorldAuth)
            {
                // 客户端 Speed = |dx|/unscaledElapsed（PositionSync 补间写入），除以 timeScale 归一 game 秒。
                normalized = (double)speed / (double)scale;
            }
            double capped = Math.Min(normalized, MaxDesignStepSpeed);
            double step = (double)dt * capped / strideWorld;
            step %= 1.0;                                   // 先取小数，防大 step 吞掉旧相位小数
            state.GaitPhase = (state.GaitPhase + step) % 1.0;
        }
        int index = (int)Math.Floor(state.GaitPhase * BankAssistantAtlasMetadata.FramesPerAction);
        if (index < 0) index = 0;
        else if (index >= BankAssistantAtlasMetadata.FramesPerAction) index = BankAssistantAtlasMetadata.FramesPerAction - 1;
        frame = (int)action * BankAssistantAtlasMetadata.FramesPerAction + index;
        return true;
    }

    private static bool TryDecodeSheet(BankAssistantAtlasStyle style, out Sprite[] sprites, out Texture2D texture)
    {
        sprites = null;
        texture = null;
        try
        {
            Assembly assembly = typeof(BankAssistantAtlasVisuals).Assembly;
            using (Stream stream = assembly.GetManifestResourceStream(style.ResourceName))
            {
                if (stream == null)
                {
                    WarnOnce("atlas resource missing: " + style.ResourceName);
                    return false;
                }
                long length = stream.Length;
                if (length <= 0 || length > MaxResourceBytes)
                {
                    WarnOnce("atlas size rejected: " + style.Name + " " + length + " bytes");
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
                    WarnOnce("atlas truncated: " + style.Name + " " + read + "/" + bytes.Length);
                    return false;
                }
                return DecodeSheet(style, bytes, out sprites, out texture);
            }
        }
        catch (Exception e)
        {
            WarnOnce("atlas load failed: " + style.Name + " " + e.GetType().Name);
            return false;
        }
    }

    /// <summary>字节 → 校验（尺寸/每帧可见像素）→ Point/Clamp 纹理 + 32 个固定 rect/pivot sprite。
    /// 与资源读取分离，测试可直接链接注入字节。</summary>
    private static bool DecodeSheet(BankAssistantAtlasStyle style, byte[] bytes, out Sprite[] sprites,
        out Texture2D texture)
    {
        sprites = null;
        texture = null;
        Texture2D created = null;
        Sprite[] createdSprites = null;
        try
        {
            created = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            // 原生保留：缓存由模块自有托管数组持有，需要显式保留位才能跨场景 unused-asset
            // 卸载存活（原场景曾报帧不可用；确切卸载者尚未验证）。保留位在创建时写入；
            // 显式 Destroy（失败清理）不受该位影响。
            created.hideFlags = HideFlags.DontUnloadUnusedAsset;
            if (!ImageConversion.LoadImage(created, bytes, false))
            {
                WarnOnce("ImageConversion.LoadImage returned false: " + style.Name);
                DestroyQuietly(created);
                return false;
            }
            if (created.width != style.SheetWidth || created.height != style.SheetHeight)
            {
                WarnOnce("atlas dimensions " + created.width + "x" + created.height + " != "
                    + style.SheetWidth + "x" + style.SheetHeight + " for " + style.Name);
                DestroyQuietly(created);
                return false;
            }
            Color32[] pixels = created.GetPixels32();
            if (pixels == null || pixels.Length != style.SheetWidth * style.SheetHeight)
            {
                WarnOnce("atlas pixel read failed: " + style.Name);
                DestroyQuietly(created);
                return false;
            }
            // 每帧 rect 至少有一个不透明像素：防止错图/错 rect 静默冒充合格资源。
            for (int frame = 0; frame < BankAssistantAtlasMetadata.FrameCount; frame++)
            {
                Rect rect = style.Rects[frame];
                if (!RectHasContent(pixels, style.SheetWidth, style.SheetHeight, rect))
                {
                    WarnOnce("atlas frame has no visible pixels: " + style.Name + " frame " + frame);
                    DestroyQuietly(created);
                    return false;
                }
            }
            created.filterMode = FilterMode.Point;
            created.wrapMode = TextureWrapMode.Clamp;
            created.anisoLevel = 0;

            createdSprites = new Sprite[BankAssistantAtlasMetadata.FrameCount];
            for (int frame = 0; frame < createdSprites.Length; frame++)
            {
                createdSprites[frame] = Sprite.Create(created, style.Rects[frame],
                    BankAssistantAtlasMetadata.PivotOf(style, frame), style.PixelsPerUnit, 0u,
                    SpriteMeshType.FullRect);
                if (createdSprites[frame] == null)
                {
                    WarnOnce("Sprite.Create returned null: " + style.Name + " frame " + frame);
                    ReleaseSprites(createdSprites);
                    DestroyQuietly(created);
                    return false;
                }
                createdSprites[frame].hideFlags = HideFlags.DontUnloadUnusedAsset;
            }
            texture = created;
            sprites = createdSprites;
            return true;
        }
        catch (Exception e)
        {
            WarnOnce("atlas decode failed: " + style.Name + " " + e.GetType().Name);
            ReleaseSprites(createdSprites);
            DestroyQuietly(created);
            return false;
        }
    }

    private static bool RectHasContent(Color32[] pixels, int width, int height, Rect rect)
    {
        int x0 = (int)rect.x;
        int y0 = (int)rect.y;
        int x1 = x0 + (int)rect.width;
        int y1 = y0 + (int)rect.height;
        if (x0 < 0) x0 = 0;
        if (y0 < 0) y0 = 0;
        if (x1 > width) x1 = width;
        if (y1 > height) y1 = height;
        for (int y = y0; y < y1; y++)
        {
            int row = y * width;
            for (int x = x0; x < x1; x++)
            {
                if (pixels[row + x].a != 0) return true;
            }
        }
        return false;
    }

    private static void ReleasePartial(Sprite[][] sprites, Texture2D[] textures)
    {
        if (sprites != null)
        {
            for (int i = 0; i < sprites.Length; i++) ReleaseSprites(sprites[i]);
        }
        if (textures != null)
        {
            for (int i = 0; i < textures.Length; i++) DestroyQuietly(textures[i]);
        }
    }

    private static void ReleaseSprites(Sprite[] sprites)
    {
        if (sprites == null) return;
        for (int i = 0; i < sprites.Length; i++) DestroyQuietly(sprites[i]);
    }

    private static void DestroyQuietly(UnityEngine.Object target)
    {
        try { if (target != null) UnityEngine.Object.Destroy(target); }
        catch (Exception) { }
    }

    private static void WarnOnce(string reason)
    {
        if (!Warned.Add(reason)) return;
        try
        {
            KingdomEnhancedPlugin.Instance?.LogSource?.LogWarning("[BankAssistantAtlasVisuals] " + reason);
        }
        catch (Exception)
        {
            // 日志不可用时保持静默：显示层绝不因日志失败而中断。
        }
    }

    private static void Info(string message)
    {
        try
        {
            KingdomEnhancedPlugin.Instance?.LogSource?.LogInfo("[BankAssistantAtlasVisuals] " + message);
        }
        catch (Exception)
        {
            // 同上：日志失败不影响显示层。
        }
    }
}
