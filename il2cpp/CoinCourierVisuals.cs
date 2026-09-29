using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 金币哥布林 B 版动作（atlas 帧区间与时钟）。与
/// artifacts/coin-courier-blueprint-b-20260927/animation-draft/manifest.json 保持一致：
/// 8x4 格、单格 56x56、PPU 32、固定脚点 pivot (28,12)（格左下原点）、32 帧。
///
/// 纯逻辑：只有区间/循环策略/相位→帧号的确定性换算；无 Unity 调用、无时钟、无分配。
/// 循环 Idle/Leisure/Run；Collect/Deliver/Jump/Fall/Land 由调用方给相位，超出时长后停在末帧。
/// 非法相位（NaN/±Inf/负值）一律回退首帧，绝不随机、绝不每帧重启。
/// </summary>
internal enum CoinCourierPose
{
    Idle = 0,
    Leisure = 1,
    Run = 2,
    Collect = 3,
    Deliver = 4,
    Jump = 5,
    Fall = 6,
    Land = 7,
}

/// <summary>
/// 帧表：索引 = 行主序 cell 序 = atlas 帧序（cell = row*8 + col）。
/// 区间与秒/帧直接抄自已批准动作清单（manifest.json poses），测试逐项与 manifest 核对。
/// </summary>
internal static class CoinCourierPoseTable
{
    internal const int AtlasColumns = 8;
    internal const int AtlasRows = 4;
    internal const int CellPixels = 56;
    internal const float PixelsPerUnit = 32f;
    internal const int PivotPixelX = 28;
    internal const int PivotPixelY = 12;
    internal const int FrameCount = AtlasColumns * AtlasRows;

    // 行序（cell 序）：0..7 跑步，8..11 闲暇，12..15 取币，16..19 补给，20..23 起跳，24..27 降落，28..31 待机。
    // Fall = 24..25（空中/伸脚），Land = 26..27（压低/站起）。
    private static readonly int[] FirstFrames = { 28, 8, 0, 12, 16, 20, 24, 26 };
    private static readonly int[] LastFrames = { 31, 11, 7, 15, 19, 23, 25, 27 };
    private static readonly float[] SecondsPerFrame = { 0.50f, 0.60f, 0.075f, 0.18f, 0.20f, 0.10f, 0.12f, 0.12f };
    private static readonly bool[] Looping = { true, true, true, false, false, false, false, false };

    internal static int FirstFrame(CoinCourierPose pose)
        => FirstFrames[Clamped(pose)];

    internal static int LastFrame(CoinCourierPose pose)
        => LastFrames[Clamped(pose)];

    internal static int PoseFrameCount(CoinCourierPose pose)
        => LastFrames[Clamped(pose)] - FirstFrames[Clamped(pose)] + 1;

    internal static float PoseSecondsPerFrame(CoinCourierPose pose)
        => SecondsPerFrame[Clamped(pose)];

    internal static bool IsLooping(CoinCourierPose pose)
        => Looping[Clamped(pose)];

    private static int Clamped(CoinCourierPose pose)
    {
        int index = (int)pose;
        return index < 0 || index >= FirstFrames.Length ? 0 : index;
    }

    /// <summary>
    /// 相位（秒，由调用方给）→ atlas 帧号。循环姿态按 authored duration 取模；
    /// 非循环姿态 clamp 到末帧。NaN/±Inf/负值/0 一律首帧；相同相位必得同一帧（幂等）。
    /// </summary>
    internal static int FrameIndex(CoinCourierPose pose, float phaseSeconds)
    {
        int poseIndex = Clamped(pose);
        int first = FirstFrames[poseIndex];
        int count = LastFrames[poseIndex] - first + 1;
        float step = SecondsPerFrame[poseIndex];
        if (count <= 1 || !(step > 0f)) return first;
        if (!(phaseSeconds > 0f)) return first;
        float duration = step * count;
        if (Looping[poseIndex])
        {
            if (phaseSeconds >= duration)
            {
                phaseSeconds %= duration;
                if (!(phaseSeconds >= 0f) || phaseSeconds >= duration) phaseSeconds = 0f;
            }
        }
        else if (phaseSeconds >= duration)
        {
            return first + count - 1;
        }
        int frame = (int)(phaseSeconds / step);
        if (frame < 0) frame = 0;
        else if (frame >= count) frame = count - 1;
        return first + frame;
    }
}

/// <summary>自有显示句柄：Create 返回，Destroy 显式释放（销毁整棵子对象）。</summary>
internal sealed class CoinCourierView
{
    internal GameObject Root;
    internal Transform Transform;
    internal SpriteRenderer Renderer;
    internal int LastSpriteFrame = -1;
    internal bool Destroyed;
}

/// <summary>
/// 金币哥布林纯显示角色：自建 GameObject + SpriteRenderer，读取内嵌 atlas
/// （KingdomEnhancedMod.CoinCourierBAtlas.png），按 <see cref="CoinCourierPose"/> 与相位换帧。
///
/// 契约：
/// * 无自动 Update、无全局时钟；位置/相位/可见性全部由调用方每帧显式给出。相同输入幂等。
/// * 排序固定 numeric layerID 0/order 1（自有前置决定，不读参考）；material 只读参考的 sharedMaterial，
///   绝不隐藏/修改参考对象，参考缺失时用默认材质。
/// * 不选目标、不扣款、不生成身份、不动 root 物理；不能作为战斗/经济角色使用。
/// * 失败路径一次性告警并保持空渲染（fail-closed），绝不抛给调用方。
/// * 共享 atlas 纹理/切图/材质是模块级资源：只在 <see cref="CoinCourierVisuals.ShutdownModule"/>
///   （世界/模块整体结束）释放；Destroy/DestroyAll 只拆自己的 view。
/// </summary>
internal static class CoinCourierVisuals
{
    internal const string ResourceName = "KingdomEnhancedMod.CoinCourierBAtlas.png";

    /// <summary>
    /// MOD 自有角色（招募标记与已招哥布林）的固定前置排序：主城已核 serialized layerID/order 为
    /// 0/0，这里取 numeric 0/1 明确前置。这是本模块的前置决定，不复制参考 renderer、不累加偏移，
    /// 也不依赖 Banker/商人/任何参考对象存在。
    /// </summary>
    internal const int SortingLayerId = 0;
    internal const int SortingOrder = 1;

    /// <summary>
    /// 共用哥布林视觉节点的统一缩放（2026-09-28 用户批准）：由 20/32 像素高度比得到，
    /// 只缩本模块自有的视觉子节点。X 保留 ±镜像、Y 同值、Z 保持 1；每帧绝对赋值，
    /// 不累乘、不依赖旧 scale。父支付 root/世界层/PPU/图集/地面/经济/跳跃路径都不动。
    /// </summary>
    internal const float AppearanceScale = 0.625f;

    private const int AtlasWidth = CoinCourierPoseTable.AtlasColumns * CoinCourierPoseTable.CellPixels;
    private const int AtlasHeight = CoinCourierPoseTable.AtlasRows * CoinCourierPoseTable.CellPixels;
    private const int AtlasReady = 1;
    private const int AtlasFailed = 2;

    private static readonly List<CoinCourierView> Views = new();
    private static readonly HashSet<string> Warned = new();
    private static Sprite[] _sprites;
    private static Texture2D _texture;
    private static int _atlasState;
    private static Material _fallbackMaterial;
    private static bool _materialResolved;

    internal static int LiveViewCount => Views.Count;

    /// <summary>
    /// 在 <paramref name="parent"/> 下建自有显示对象：排序固定 <see cref="SortingLayerId"/>/<see cref="SortingOrder"/>，
    /// material 取 <paramref name="reference"/>（可为 null，用默认材质）。失败返回 null 并告警。
    /// </summary>
    internal static CoinCourierView Create(Transform parent, SpriteRenderer reference)
    {
        if (parent == null)
        {
            WarnOnce("create called without a parent");
            return null;
        }
        GameObject root = null;
        try
        {
            root = new GameObject("KEM_CoinCourierView");
            Transform transform = root.transform;
            transform.SetParent(parent, false);
            transform.localPosition = Vector3.zero;
            transform.localScale = new Vector3(AppearanceScale, AppearanceScale, 1f);
            SpriteRenderer renderer = root.AddComponent<SpriteRenderer>();
            if (renderer == null)
            {
                DestroyQuietly(root);
                WarnOnce("sprite renderer missing on the new view");
                return null;
            }
            renderer.enabled = false;
            renderer.sortingLayerID = SortingLayerId;
            renderer.sortingOrder = SortingOrder;
            Material material = reference != null ? SharedMaterial(reference) : null;
            if (material == null) material = FallbackMaterial();
            if (material != null) renderer.sharedMaterial = material;
            CoinCourierView view = new CoinCourierView { Root = root, Transform = transform, Renderer = renderer };
            Views.Add(view);
            return view;
        }
        catch (Exception e)
        {
            WarnOnce("create failed: " + e.GetType().Name);
            DestroyQuietly(root);
            return null;
        }
    }

    /// <summary>
    /// 每帧显式渲染：世界位置、朝向（左右镜像由 localScale.x 承担，与原生单位同一约定）、
    /// 姿态 + 相位、可见性。不可见时只落位置/朝向并关闭 renderer，不动帧号。
    /// </summary>
    internal static void Render(
        CoinCourierView view, Vector3 worldPosition, bool facingRight,
        CoinCourierPose pose, float phaseSeconds, bool visible)
    {
        if (view == null || view.Destroyed) return;
        try
        {
            view.Transform.position = worldPosition;
            // 每帧绝对赋值（含朝向镜像）：不累乘、不依赖旧 scale，Z 恒 1。
            view.Transform.localScale = facingRight
                ? new Vector3(AppearanceScale, AppearanceScale, 1f)
                : new Vector3(-AppearanceScale, AppearanceScale, 1f);
            view.Renderer.enabled = visible;
            if (!visible) return;
            int frame = CoinCourierPoseTable.FrameIndex(pose, phaseSeconds);
            if (frame == view.LastSpriteFrame) return;
            Sprite sprite = SpriteFor(frame);
            if (sprite == null) return;
            view.Renderer.sprite = sprite;
            view.LastSpriteFrame = frame;
        }
        catch (Exception e)
        {
            WarnOnce("render failed: " + e.GetType().Name);
        }
    }

    /// <summary>显式销毁（幂等）：销毁自有 GameObject 并移出登记表。</summary>
    internal static void Destroy(CoinCourierView view)
    {
        if (view == null || view.Destroyed) return;
        view.Destroyed = true;
        Views.Remove(view);
        GameObject root = view.Root;
        view.Root = null;
        view.Transform = null;
        view.Renderer = null;
        if (root != null) DestroyQuietly(root);
    }

    /// <summary>拆掉全部自有 view（不动共享 atlas/材质）；之后仍可再 Create。</summary>
    internal static void DestroyAll()
    {
        for (int i = Views.Count - 1; i >= 0; i--) Destroy(Views[i]);
    }

    /// <summary>
    /// 模块/世界整体结束才调用（由 Operator 整合）：拆全部 view，再释放共享 atlas 纹理/切图/材质并复位懒加载。
    /// 业务停用只能 Destroy 自己的 view，不得清理共享资源。
    /// </summary>
    internal static void ShutdownModule()
    {
        DestroyAll();
        Sprite[] sprites = _sprites;
        _sprites = null;
        Texture2D texture = _texture;
        _texture = null;
        Material material = _fallbackMaterial;
        _fallbackMaterial = null;
        _materialResolved = false;
        _atlasState = 0;
        if (sprites != null)
        {
            for (int i = 0; i < sprites.Length; i++)
            {
                if (sprites[i] != null) DestroyQuietly(sprites[i]);
            }
        }
        if (texture != null) DestroyQuietly(texture);
        if (material != null) DestroyQuietly(material);
    }

    private static Sprite SpriteFor(int frame)
    {
        if (_atlasState != AtlasReady && !EnsureAtlas()) return null;
        if (_sprites == null || frame < 0 || frame >= _sprites.Length) return null;
        return _sprites[frame];
    }

    /// <summary>惰性解码内嵌 atlas（只一次）；尺寸/内容校验失败即整块不可用。</summary>
    private static bool EnsureAtlas()
    {
        if (_atlasState == AtlasReady) return true;
        if (_atlasState == AtlasFailed) return false;
        _atlasState = AtlasFailed;   // 先置失败：任何异常都保持空渲染
        try
        {
            Assembly assembly = typeof(CoinCourierVisuals).Assembly;
            using (Stream stream = assembly.GetManifestResourceStream(ResourceName))
            {
                if (stream == null)
                {
                    WarnOnce("atlas resource missing: " + ResourceName);
                    return false;
                }
                long length = stream.Length;
                if (length <= 0 || length > 4L * 1024L * 1024L)
                {
                    WarnOnce("atlas size rejected: " + length + " bytes");
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
                    WarnOnce("atlas truncated: " + read + "/" + bytes.Length);
                    return false;
                }
                if (!DecodeAtlas(bytes)) return false;
                _atlasState = AtlasReady;
                return true;
            }
        }
        catch (Exception e)
        {
            WarnOnce("atlas load failed: " + e.GetType().Name);
            return false;
        }
    }

    private static bool DecodeAtlas(byte[] bytes)
    {
        Texture2D texture = null;
        Sprite[] sprites = null;
        try
        {
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, bytes, false))
            {
                WarnOnce("ImageConversion.LoadImage returned false");
                DestroyQuietly(texture);
                return false;
            }
            if (texture.width != AtlasWidth || texture.height != AtlasHeight)
            {
                WarnOnce("atlas dimensions " + texture.width + "x" + texture.height
                    + " != " + AtlasWidth + "x" + AtlasHeight);
                DestroyQuietly(texture);
                return false;
            }
            Color32[] pixels = texture.GetPixels32();
            if (pixels == null || pixels.Length != AtlasWidth * AtlasHeight)
            {
                WarnOnce("atlas pixel read failed");
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
                WarnOnce("atlas fully transparent");
                DestroyQuietly(texture);
                return false;
            }
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.anisoLevel = 0;

            sprites = new Sprite[CoinCourierPoseTable.FrameCount];
            Vector2 pivot = new Vector2(
                CoinCourierPoseTable.PivotPixelX / (float)CoinCourierPoseTable.CellPixels,
                CoinCourierPoseTable.PivotPixelY / (float)CoinCourierPoseTable.CellPixels);
            for (int frame = 0; frame < sprites.Length; frame++)
            {
                int column = frame % CoinCourierPoseTable.AtlasColumns;
                int row = frame / CoinCourierPoseTable.AtlasColumns;
                // Unity 纹理原点在左下：atlas 行 0 在最上 → y 从底部倒算。
                float y = (CoinCourierPoseTable.AtlasRows - 1 - row) * CoinCourierPoseTable.CellPixels;
                Rect rect = new Rect(column * CoinCourierPoseTable.CellPixels, y,
                    CoinCourierPoseTable.CellPixels, CoinCourierPoseTable.CellPixels);
                sprites[frame] = Sprite.Create(texture, rect, pivot,
                    CoinCourierPoseTable.PixelsPerUnit, 0u, SpriteMeshType.FullRect);
            }
            _texture = texture;
            _sprites = sprites;
            return true;
        }
        catch (Exception e)
        {
            WarnOnce("atlas decode failed: " + e.GetType().Name);
            if (sprites != null)
            {
                for (int i = 0; i < sprites.Length; i++)
                {
                    if (sprites[i] != null) DestroyQuietly(sprites[i]);
                }
            }
            if (texture != null) DestroyQuietly(texture);
            return false;
        }
    }

    private static Material SharedMaterial(SpriteRenderer renderer)
    {
        try { return renderer.sharedMaterial; }
        catch (Exception) { return null; }
    }

    private static Material FallbackMaterial()
    {
        if (_materialResolved) return _fallbackMaterial;
        _materialResolved = true;
        try
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null) _fallbackMaterial = new Material(shader);
        }
        catch (Exception)
        {
            _fallbackMaterial = null;
        }
        return _fallbackMaterial;
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
            KingdomEnhancedPlugin.Instance?.LogSource?.LogWarning("[CoinCourierVisuals] " + reason);
        }
        catch (Exception)
        {
            // 日志不可用时保持静默：显示层绝不因日志失败而中断。
        }
    }
}
