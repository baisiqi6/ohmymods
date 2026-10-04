using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 金币哥布林休闲动作的纯显示金币（把玩 → 抛起 → 接回掌心 → 收回袋边并隐藏）。
///
/// 采样是纯函数：pose + phase（秒）→ 已按图集像素量化的局部坐标/横向压缩/可见性。
/// 没有时钟、没有随机、没有分配、没有 Unity 调用；同一输入必得同一输出（暂停=同相位不漂移），
/// NaN/±Inf/负相位按循环起点处理，与 <see cref="CoinCourierPoseTable.FrameIndex"/> 的非法相位策略一致。
///
/// 渲染部分每 view 至多持有一个自有子对象：惰性创建一次，失败/原生回收后不逐帧重试（fail-closed）；
/// 共享金币纹理与切图是模块级资源，只在 <see cref="ShutdownModule"/> 释放（view 的 Destroy 只拆自己的子对象）。
/// 本类型不生成 DroppableCurrency、不碰钱包/银行/账本/任务，也不读写身体 renderer 的 sprite/材质/缩放，
/// 只跟随身体的 sorting 层号 +1 让金币盖在身前。
/// </summary>
internal static class CoinCourierLeisureCoin
{
    /// <summary>程序金币的边长像素：视觉上约 3~4 个图集像素，小于身体宽度 1/6。</summary>
    internal const int CoinPixels = 4;
    private const float PixelsPerUnit = 32f;
    internal const string ChildName = "KEM_CoinCourierLeisureCoin";

    /// <summary>Leisure 单帧秒数（0.6s）；所有段边界都锚在帧节奏上。</summary>
    private static float FrameSeconds => CoinCourierPoseTable.PoseSecondsPerFrame(CoinCourierPose.Leisure);
    /// <summary>Leisure 整圈（4 × 0.6s = 2.4s），也是本动画的循环长度。</summary>
    internal static float LoopSeconds => CoinCourierPoseTable.PoseFrameCount(CoinCourierPose.Leisure) * FrameSeconds;

    // 段边界（单位：Leisure 帧，F = 0.6s）：
    //   把玩 [0, 1F) ｜ 上抛/下落 [1F, 2.5F) ｜ 接住并在手落下时跟手 [2.5F, 10F/3) ｜ 收袋 [10F/3, 4F)
    private const float TossStartFrames = 1f;
    private const float TossEndFrames = 2.5f;
    private const float RetractStartFrames = 10f / 3f;

    // 手部锚点：cell 内像素（左上原点），脚点 pivot (28,12) → 顶起第 44 行。
    // 帧 8 手垂在腰带前，帧 9 基本同位，帧 10 抬手到胸前，帧 11 收回腰带前。
    private const float FootRowFromTop = 44f;
    private const float PivotPixelX = CoinCourierPoseTable.PivotPixelX;
    private static readonly float[] HandPixelX = { 33f, 33f, 25f, 33f };
    private static readonly float[] HandPixelY = { 29f, 28f, 25f, 29f };

    /// <summary>抛起顶点相对两端连线的抬升（cell 像素）：顶点略高于头顶（头顶约 top 11px）。</summary>
    private const float ApexLiftPixels = 17f;
    /// <summary>收袋终点（袋口一侧；帧 11 的左手就在这只袋子上）。</summary>
    private const float BagPixelX = 22f;
    private const float BagPixelY = 24f;

    // 自转速度（每圈秒数）与 6 档量化宽度（币面 → 3/4 → 侧边 → 3/4 → 币面）。
    private const float PlaySpinSeconds = 0.30f;
    // 空中整 2 圈（0.9s 抛接里 1 圈到位、1 圈落手）：起抛/顶点/接住都落在币面帧上。
    private const float TossSpinSeconds = 0.45f;
    private const float HoldSpinSeconds = 0.60f;
    private const float RetractSpinSeconds = 0.80f;
    private static readonly float[] SpinWidths = { 1f, 0.75f, 0.5f, 0.25f, 0.5f, 0.75f };

    // 金币配色沿用 CoinCourierCoinFlight 的程序金币：外圈暗金 + 亮心 + 左上高光。
    // （internal 供测试/预览导出这些真实像素，不参与任何运行时逻辑。）
    internal static readonly Color32[] CoinPixelsRgba =
    {
        new Color32(0, 0, 0, 0), new Color32(176, 122, 22, 255), new Color32(176, 122, 22, 255), new Color32(0, 0, 0, 0),
        new Color32(176, 122, 22, 255), new Color32(255, 244, 190, 255), new Color32(247, 199, 66, 255), new Color32(176, 122, 22, 255),
        new Color32(176, 122, 22, 255), new Color32(247, 199, 66, 255), new Color32(247, 199, 66, 255), new Color32(176, 122, 22, 255),
        new Color32(0, 0, 0, 0), new Color32(176, 122, 22, 255), new Color32(176, 122, 22, 255), new Color32(0, 0, 0, 0),
    };

    private const int StateNone = 0;
    private const int StateReady = 1;
    private const int StateFailed = 2;

    /// <summary>量化后的采样：所有坐标都是图集整像素（1/32 单位）的整数倍。</summary>
    internal struct Sample
    {
        /// <summary>相对 view root 原点（脚点）的本地坐标；1 单位 = 32 图集像素。</summary>
        internal float X;
        internal float Y;
        /// <summary>币面↔侧边的横向压缩（1/0.75/0.5/0.25），只作用在金币子对象的 x 上。</summary>
        internal float WidthScale;
        internal bool Visible;
    }

    /// <summary>每 view 的惰性装饰子对象；创建失败时保留 Failed 标记，绝不逐帧重试。</summary>
    internal sealed class Child
    {
        internal GameObject Root;
        internal Transform Transform;
        internal SpriteRenderer Renderer;
        internal bool Failed;
    }

    private static Texture2D _texture;
    private static Sprite _sprite;
    private static int _state;
    private static readonly HashSet<string> Warned = new();

    /// <summary>
    /// 纯相位采样：只有 <see cref="CoinCourierPose.Leisure"/> 有金币；其余姿态返回 false（调用方立即隐藏）。
    /// 返回 true 时 <see cref="Sample.Visible"/> 仍可能是 false（收袋末段已进袋）。
    /// </summary>
    internal static bool TrySample(CoinCourierPose pose, float phaseSeconds, out Sample sample)
    {
        sample = default;
        if (pose != CoinCourierPose.Leisure) return false;
        float frame = FrameSeconds;
        float loop = LoopSeconds;
        if (!(frame > 0f) || !(loop > 0f)) return false;   // 表被改坏：宁可不画
        float phase = 0f;
        if (float.IsFinite(phaseSeconds) && phaseSeconds > 0f)
        {
            phase = phaseSeconds % loop;
            if (!(phase >= 0f) || phase >= loop) phase = 0f;
        }
        float tossStart = TossStartFrames * frame;
        float tossEnd = TossEndFrames * frame;
        float retractStart = RetractStartFrames * frame;
        if (phase < tossStart)
        {
            SetPixels(ref sample, HandPixelX[0], HandPixelY[0], SpinCycles(phase), true);
        }
        else if (phase < tossEnd)
        {
            float u = (phase - tossStart) / (tossEnd - tossStart);
            float x = HandPixelX[0] + (HandPixelX[2] - HandPixelX[0]) * u;
            float y = HandPixelY[0] + (HandPixelY[2] - HandPixelY[0]) * u
                - ApexLiftPixels * 4f * u * (1f - u);
            SetPixels(ref sample, x, y, SpinCycles(phase), true);
        }
        else if (phase < retractStart)
        {
            // 先在掌心停半拍（接住轻转），随后精灵手落下时金币跟着回到腰带前。
            float v = (phase - tossEnd) / (retractStart - tossEnd);
            float t = v <= 0.6f ? 0f : (v - 0.6f) / 0.4f;
            float s = t * t * (3f - 2f * t);
            float x = HandPixelX[2] + (HandPixelX[3] - HandPixelX[2]) * s;
            float y = HandPixelY[2] + (HandPixelY[3] - HandPixelY[2]) * s;
            SetPixels(ref sample, x, y, SpinCycles(phase), true);
        }
        else
        {
            // 收袋：前 90% 走到袋口，最后一段已进袋（隐藏），循环回起点重新拿出来。
            float v = (phase - retractStart) / (loop - retractStart);
            float t = v / 0.9f;
            if (t > 1f) t = 1f;
            float s = t * t * (3f - 2f * t);
            float x = HandPixelX[3] + (BagPixelX - HandPixelX[3]) * s;
            float y = HandPixelY[3] + (BagPixelY - HandPixelY[3]) * s;
            SetPixels(ref sample, x, y, SpinCycles(phase), v < 0.9f);
        }
        return true;
    }

    /// <summary>把金币挂到 view（首次可见 Leisure 才真正创建；失败后本 view 永久关闭装饰）。</summary>
    internal static void Render(CoinCourierView view, CoinCourierPose pose, float phaseSeconds)
    {
        if (view == null || view.Destroyed) return;
        if (!TrySample(pose, phaseSeconds, out Sample sample))
        {
            Hide(view);
            return;
        }
        Child child = view.LeisureCoin;
        if (child == null)
        {
            child = Create(view);
            view.LeisureCoin = child;
        }
        if (child == null || child.Failed) return;
        try
        {
            Transform transform = child.Transform;
            SpriteRenderer renderer = child.Renderer;
            GameObject root = child.Root;
            if (transform == null || renderer == null || root == null)
            {
                // 原生已回收装饰子对象：先把还活着的部分安全关掉再一次性关门（不逐帧重挂，视图重建归调用方）。
                HideChild(child);
                child.Failed = true;
                WarnOnce("coin child was reclaimed; decoration disabled for this view");
                return;
            }
            if (sample.Visible && !SpriteUsable(renderer.sprite))
            {
                // 自有切图/纹理被原生回收（存活 view 也走这里）：共享缓存有界重建一次，各 view 下一帧重绑；
                // 重建失败（确证坏素材）保持关门，绝不给每帧热点，也绝不留旧残影。
                Sprite fresh = EnsureSprite();
                if (fresh == null)
                {
                    HideChild(child);
                    return;
                }
                renderer.sprite = fresh;
            }
            transform.localPosition = new Vector3(sample.X, sample.Y, 0f);
            transform.localScale = new Vector3(sample.WidthScale, 1f, 1f);
            SpriteRenderer body = view.Renderer;
            if (body != null)
            {
                renderer.sortingLayerID = body.sortingLayerID;
                renderer.sortingOrder = body.sortingOrder + 1;
            }
            if (sample.Visible && !root.activeSelf) root.SetActive(true);   // Hide 退路可能停用过自有 child
            renderer.enabled = sample.Visible;
        }
        catch (Exception e)
        {
            HideChild(child);   // 先关掉可能只写了一半的旧状态，再关门并告警
            child.Failed = true;
            WarnOnce("coin render failed: " + e.GetType().Name);
        }
    }

    /// <summary>立即隐藏（非 Leisure / 隐藏 / 身体当前帧画不出来时的让位）；从不创建金币，也不因 Failed 跳过仍活的自有 renderer。</summary>
    internal static void Hide(CoinCourierView view)
    {
        if (view == null || view.Destroyed) return;
        HideChild(view.LeisureCoin);
    }

    /// <summary>
    /// 只关自有子对象：先关自己的 renderer；renderer 不可写时就停用自有 child 根作退路。
    /// 绝不碰 actor/body root 或其它 view 的对象；任何失败都静默（下次异常/Render 还会再试）。
    /// </summary>
    private static void HideChild(Child child)
    {
        if (child == null) return;
        try
        {
            SpriteRenderer renderer = child.Renderer;
            if (renderer != null)
            {
                renderer.enabled = false;
                return;
            }
        }
        catch (Exception)
        {
            // renderer 写失败：走下面的自有 child 停用退路
        }
        try
        {
            GameObject root = child.Root;
            if (root != null) root.SetActive(false);
        }
        catch (Exception)
        {
            // 连自有对象都不可写：保持静默，显示层绝不因隐藏失败中断业务。
        }
    }

    /// <summary>拆掉本 view 的装饰子对象（view 销毁/回池时调用）；共享纹理/切图留给 <see cref="ShutdownModule"/>。</summary>
    internal static void Release(CoinCourierView view)
    {
        if (view == null) return;
        Child child = view.LeisureCoin;
        view.LeisureCoin = null;
        if (child == null) return;
        GameObject root = child.Root;
        child.Root = null;
        child.Transform = null;
        child.Renderer = null;
        if (root != null) DestroyQuietly(root);
    }

    /// <summary>模块/世界整体结束才调用：释放共享金币纹理与切图并复位懒加载状态。</summary>
    internal static void ShutdownModule()
    {
        ReleaseSprite();
    }

    private static void SetPixels(ref Sample sample, float pixelX, float pixelYTop, float spinCycles, bool visible)
    {
        float x = MathF.Round(pixelX) - PivotPixelX;
        float y = FootRowFromTop - MathF.Round(pixelYTop);
        sample.X = x / PixelsPerUnit;
        sample.Y = y / PixelsPerUnit;
        int step = (int)(spinCycles * SpinWidths.Length) % SpinWidths.Length;
        if (step < 0) step += SpinWidths.Length;
        sample.WidthScale = SpinWidths[step];
        sample.Visible = visible;
    }

    /// <summary>自转累计圈数：段内连续、段间衔接（把玩最快 → 空中翻滚 → 掌心放慢 → 收袋最慢）。</summary>
    private static float SpinCycles(float phase)
    {
        float frame = FrameSeconds;
        float tossStart = TossStartFrames * frame;
        float tossEnd = TossEndFrames * frame;
        float retractStart = RetractStartFrames * frame;
        float cycles = MathF.Min(phase, tossStart) / PlaySpinSeconds;
        if (phase <= tossStart) return cycles;
        cycles += (MathF.Min(phase, tossEnd) - tossStart) / TossSpinSeconds;
        if (phase <= tossEnd) return cycles;
        cycles += (MathF.Min(phase, retractStart) - tossEnd) / HoldSpinSeconds;
        if (phase <= retractStart) return cycles;
        return cycles + (phase - retractStart) / RetractSpinSeconds;
    }

    private static Child Create(CoinCourierView view)
    {
        Child child = new Child();
        GameObject root = null;
        try
        {
            Sprite sprite = EnsureSprite();
            if (sprite == null)
            {
                WarnOnce("coin sprite unavailable; leisure decoration disabled");
                child.Failed = true;
                return child;
            }
            root = new GameObject(ChildName);
            Transform transform = root.transform;
            transform.SetParent(view.Transform, false);
            transform.localPosition = Vector3.zero;
            transform.localScale = new Vector3(1f, 1f, 1f);
            SpriteRenderer renderer = root.AddComponent<SpriteRenderer>();
            if (renderer == null)
            {
                DestroyQuietly(root);
                WarnOnce("coin renderer missing on the new child");
                child.Failed = true;
                return child;
            }
            renderer.sprite = sprite;
            renderer.enabled = false;
            Material material = SharedMaterial(view.Renderer);
            if (material != null) renderer.sharedMaterial = material;
            child.Root = root;
            child.Transform = transform;
            child.Renderer = renderer;
            return child;
        }
        catch (Exception e)
        {
            WarnOnce("coin child create failed: " + e.GetType().Name);
            DestroyQuietly(root);
            child.Failed = true;
            return child;
        }
    }

    private static Material SharedMaterial(SpriteRenderer renderer)
    {
        try { return renderer != null ? renderer.sharedMaterial : null; }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// 4×4 程序金币（外圈暗金 + 亮心 + 左上高光），PPU 与角色图集一致、point filter。
    /// 只解码一次；缓存的切图/纹理被原生回收时整体释放并走一次有界重建，确证不可用后保持关门。
    /// </summary>
    private static Sprite EnsureSprite()
    {
        if (_state == StateReady)
        {
            if (SpriteUsable(_sprite)) return _sprite;
            // 与图集缓存同一策略：原生回收后有界恢复一次，成功与否由随后的解码结果决定。
            WarnOnce("coin sprite invalidated (native release); recovery attempt starting");
            ReleaseSprite();
        }
        else if (_state == StateFailed)
        {
            return null;
        }
        _state = StateFailed;   // 先置失败：任何异常/空返回都保持关门，绝不逐帧重试
        Texture2D texture = null;
        try
        {
            texture = new Texture2D(CoinPixels, CoinPixels, TextureFormat.RGBA32, false);
            texture.SetPixels32(CoinPixelsRgba);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.anisoLevel = 0;
            texture.Apply(false, false);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, CoinPixels, CoinPixels),
                new Vector2(0.5f, 0.5f), PixelsPerUnit, 0u, SpriteMeshType.FullRect);
            if (sprite == null)
            {
                DestroyQuietly(texture);
                WarnOnce("coin sprite create returned null");
                return null;
            }
            _texture = texture;
            _sprite = sprite;
            _state = StateReady;
            return sprite;
        }
        catch (Exception e)
        {
            WarnOnce("coin sprite failed: " + e.GetType().Name);
            DestroyQuietly(texture);   // 只清本次局部产物；_state 保持 Failed（fail-closed）
            return null;
        }
    }

    /// <summary>切图可用 = 代理存活且其原生纹理存活（Unity fake-null：任一被回收都画不出来）。</summary>
    private static bool SpriteUsable(Sprite sprite)
    {
        try { return sprite != null && sprite.texture != null; }
        catch (Exception) { return false; }
    }

    /// <summary>释放共享金币纹理/切图并复位懒加载状态（存活者销毁、引用清空）。</summary>
    private static void ReleaseSprite()
    {
        Sprite sprite = _sprite;
        _sprite = null;
        Texture2D texture = _texture;
        _texture = null;
        _state = StateNone;
        if (sprite != null) DestroyQuietly(sprite);
        if (texture != null) DestroyQuietly(texture);
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
            KingdomEnhancedPlugin.Instance?.LogSource?.LogWarning("[CoinCourierLeisureCoin] " + reason);
        }
        catch (Exception)
        {
            // 日志不可用时保持静默：显示层绝不因日志失败而中断。
        }
    }
}
