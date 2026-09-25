using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>Eight ghost renderers plus one burst body per knight; no original renderer/material writes or gameplay components.</summary>
internal static class SamuraiDashVisuals
{
    // 残影淡出 2 秒（2026-09-25 用户要求便于观察）：幻影淡出总窗 1 s → 2 s（单点常量）；
    // 连续 TrailRenderer 拖尾已按用户要求移除；这里的定格残影独立保留。
    // 采样密度（间隔/距离）、8 槽与透明度阶梯不动。
    private const float Lifetime = 2f, SampleInterval = .04f, SampleDistance = .4f;
    // 2026-09-25 用户裁定（八道不同姿态白光）：8 槽全部用已实机验证可见的标准白配方
// （recipe C），沿冲刺路径逐格定格不同拔刀姿态，2s 内保持并淡出。
private const int GhostSlots = 8;
private static float GhostOpacity(int rankFromNewest)
{
    // newest ~0.55 → oldest ~0.10：清晰的近端、可辨的远端
    return Mathf.Max(.10f, .55f - .0625f * rankFromNewest);
}
    private static readonly Dictionary<int, OwnerState> Owners = new();
    private static readonly List<int> Retire = new();
    private static readonly HashSet<string> Logged = new();
    // 2026-09-25 根因 D（残影从未发白）：recipe 2 的 Sprites/Default 是乘法着色器，顶点色 (1,1,1)
    // 是恒等乘——残影此前一直是武士原色的半透明复制。修法=按姿态在 CPU 上把源 sprite 的网格
    // 光栅化成 RGB=255/原 alpha 的白剪影小贴图（真实 2.4 的 bamboo 骑士 sprite 全部打包在
    // 2048² 图集：rect 41x32、采样区在 textureRect，必须走 UV 插值，不能整块拷 rect），再用
    // 输出 rect(0,0,w,h)+源归一化 pivot+PPU 重建 sprite；源 sprite/纹理/材质/属性块零写入。
    // 姿态缓存 soft cap 64：在飞 ghost 持有的条目不淘汰（上限 ≤9×owner 数，引用释放后下一次
    // Whitened 收敛，2× 上限时一条一次性日志）。姿态贴图由条目自持、同生共死；图集只缓存
    // alpha 单通道（LRU ≤4 张且 ≤16MiB 字节预算），不含任何全图白化大纹理。
    private const int MaxWhiteSprites = 64, MaxWhiteAtlases = 4, MaxWhiteAtlasBytes = 16 * 1024 * 1024;
    private static readonly Dictionary<WhiteKey, WhiteSpriteEntry> WhiteSprites = new();
    private static readonly Dictionary<AtlasKey, WhiteAtlasEntry> WhiteAtlases = new();
    private static int WhiteAtlasBytes;
    private static SamuraiDashVisualsDriver Driver;
    private static int OverlayId, FlashId;
    private static bool OverlayIdReady;
    private static float RetryAt;

    internal sealed class Token
    {
        internal Knight Owner;
        internal int Id;
    }
    private sealed class Ghost
    {
        internal SpriteRenderer Renderer;
        internal float Born;
        internal bool Alive;
    }
    private sealed class OwnerState
    {
        internal Knight Owner;
        internal SpriteRenderer Source, Body;
        internal GameObject Root;
        internal Ghost[] Ghosts = new Ghost[GhostSlots];
        internal Token Current;
        internal SamuraiDashDiagnostics.Trace Diagnostics;
        internal string RetireReason;
        internal bool Emitting, Tail;
        internal int NextSlot;
        internal int Samples;
        internal float NextSample, LastX;
    }
    // 姿态条目自持输出 Sprite 与 Texture2D：销毁必同生共死（无引用计数链）。
    private sealed class WhiteSpriteEntry
    {
        internal Sprite Sprite;     // null=烘焙失败（负结果也缓存，避免逐帧重试）
        internal Texture2D Texture;
        internal float LastUsed;
    }
    private sealed class WhiteAtlasEntry
    {
        internal byte[] Alpha;
        internal float LastUsed;
    }
    private readonly struct AtlasKey : IEquatable<AtlasKey>
    {
        private readonly IntPtr Texture;
        private readonly int Width, Height, NameHash;
        internal AtlasKey(Texture2D texture)
        {
            Texture = texture.Pointer; Width = texture.width; Height = texture.height;
            NameHash = texture.name != null ? texture.name.GetHashCode() : 0;   // 指针复用防线（尺寸+名再核一次）
        }
        public bool Equals(AtlasKey other) => Texture == other.Texture && Width == other.Width && Height == other.Height && NameHash == other.NameHash;
        public override bool Equals(object other) => other is AtlasKey key && Equals(key);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Texture.GetHashCode();
                hash = (hash * 397) ^ Width; hash = (hash * 397) ^ Height; hash = (hash * 397) ^ NameHash;
                return hash;
            }
        }
    }
    // 身份必须含 source Sprite 实例：真实打包姿态的 rect/pivot/PPU 可以完全相同（只差 textureRect/UV），
    // 只按 rect 键控会让八个残影槽全用同一姿态（2026-09-25 packed 审查头号缺陷）。
    private readonly struct WhiteKey : IEquatable<WhiteKey>
    {
        private readonly int SpriteId;
        private readonly IntPtr Texture;
        private readonly int X, Y, Width, Height, PivotX, PivotY, Ppu, Extrude;
        internal WhiteKey(Sprite sprite)
        {
            SpriteId = sprite.GetInstanceID();
            var texture = sprite.texture;
            Texture = texture != null ? texture.Pointer : IntPtr.Zero;
            var rect = sprite.rect;
            X = (int)rect.x; Y = (int)rect.y; Width = (int)rect.width; Height = (int)rect.height;
            PivotX = BitConverter.SingleToInt32Bits(sprite.pivot.x);
            PivotY = BitConverter.SingleToInt32Bits(sprite.pivot.y);
            Ppu = BitConverter.SingleToInt32Bits(sprite.pixelsPerUnit);
            Extrude = (int)sprite.extrude;
        }
        public bool Equals(WhiteKey other) => SpriteId == other.SpriteId && Texture == other.Texture && X == other.X &&
            Y == other.Y && Width == other.Width && Height == other.Height && PivotX == other.PivotX && PivotY == other.PivotY &&
            Ppu == other.Ppu && Extrude == other.Extrude;
        public override bool Equals(object other) => other is WhiteKey key && Equals(key);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = SpriteId;
                hash = (hash * 397) ^ Texture.GetHashCode();
                hash = (hash * 397) ^ X; hash = (hash * 397) ^ Y; hash = (hash * 397) ^ Width;
                hash = (hash * 397) ^ Height; hash = (hash * 397) ^ PivotX; hash = (hash * 397) ^ PivotY;
                hash = (hash * 397) ^ Ppu; hash = (hash * 397) ^ Extrude;
                return hash;
            }
        }
    }
    private static bool Same(UnityEngine.Object a, UnityEngine.Object b) => a != null && b != null && a.Pointer == b.Pointer;
    private static bool Valid(Knight owner) => ModConfig.Enabled.Value && owner != null && owner.gameObject != null &&
        owner.gameObject.activeInHierarchy && owner._damageable != null && !owner._damageable.isDead &&
        PatchRoles_KnightStyle.TryGetResolvedStyleIndex(owner, out int style) && style == 2;
    private static void Log(string key, Exception e)
    {
        if (!Logged.Add(key)) return;
        try { KingdomEnhancedPlugin.Instance?.LogSource.LogWarning("[SamuraiVisuals] " + key + ": " + e.GetType().Name); }
        catch { }
    }
    // 2026-09-25 审查 P1-2：着色器降级的一次性日志（镜像上面 Logged 门），每级一条、
    // 内容标明降级到了哪级（unlit-texture / source-clone）。
    private static void LogShaderFallback(string level, string detail)
    {
        if (!Logged.Add("shader-fallback:" + level)) return;
        try { KingdomEnhancedPlugin.Instance?.LogSource.LogWarning("[SamuraiDashVisuals] shader-fallback level=" + level + ": " + detail); }
        catch { }
    }
    private static bool SourceReady(SpriteRenderer source) => source != null && source.gameObject != null &&
        source.gameObject.activeInHierarchy && source.enabled && source.sprite != null && source.sharedMaterial != null &&
        source.sharedMaterial.HasProperty(OverlayId);

    // ---- 白剪影烘焙（2026-09-25 根因 D + packed 修正）--------------------------------------
    // 单路径：几何校验 + 逐三角形光栅化。UV 插值天然消化图集偏移/旋转/裁边——不做"rect 区域
    // 整体拷贝"（tight 打包允许邻居像素占据本 sprite rect 的透明角，整拷必串帧）。
    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static void DegradeWhiten(string reason, Sprite sprite)
    {
        if (!Logged.Add("whiten-degraded:" + reason)) return;   // 限频：每类原因一条
        try
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogWarning("[SamuraiDashVisuals] whiten-degraded reason=" + reason
                + " sprite=" + (sprite != null && sprite.name != null ? sprite.name : "null")
                + "; ghost keeps the source sprite color (NOT white)");
        }
        catch { }
    }
    private static void DestroyQuietly(UnityEngine.Object target)
    {
        if (target == null) return;
        try { UnityEngine.Object.Destroy(target); }
        catch (Exception e) { Log("destroy-white", e); }
    }
    private static byte[] AtlasAlpha(Texture2D source)
    {
        var key = new AtlasKey(source);
        if (WhiteAtlases.TryGetValue(key, out var cached)) { cached.LastUsed = Time.time; return cached.Alpha; }
        byte[] alpha = source.isReadable ? AlphaFromPixels(source) : null;
        if (alpha == null) alpha = AlphaFromGpu(source);   // 不可读：Blit+ReadPixels 走一次 GPU 读回
        if (alpha == null) { DegradeWhiten("atlas-alpha-unavailable", null); return null; }
        WhiteAtlasBytes += alpha.Length;
        WhiteAtlases[key] = new WhiteAtlasEntry { Alpha = alpha, LastUsed = Time.time };
        TrimWhiteAtlases();
        return alpha;
    }
    private static byte[] AlphaFromPixels(Texture2D source)
    {
        try
        {
            Color32[] pixels = source.GetPixels32();
            if (pixels == null || pixels.Length != source.width * source.height) return null;
            var alpha = new byte[pixels.Length];
            for (int i = 0; i < alpha.Length; i++) alpha[i] = pixels[i].a;
            return alpha;
        }
        catch { return null; }
    }
    private static byte[] AlphaFromGpu(Texture2D source)
    {
        RenderTexture temporary = null;
        Texture2D staging = null;
        RenderTexture previous = RenderTexture.active;
        try
        {
            temporary = RenderTexture.GetTemporary(source.width, source.height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            if (temporary == null) return null;
            Graphics.Blit(source, temporary);   // Blit 只要求源在 GPU 侧可见，不要求 CPU 可读
            RenderTexture.active = temporary;
            staging = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            staging.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0, false);
            Color32[] pixels = staging.GetPixels32();
            if (pixels == null || pixels.Length != source.width * source.height) return null;
            var alpha = new byte[pixels.Length];
            for (int i = 0; i < alpha.Length; i++) alpha[i] = pixels[i].a;
            return alpha;
        }
        catch { return null; }
        finally
        {
            RenderTexture.active = previous;                                     // 审查要求：active 恢复在 finally
            if (temporary != null) RenderTexture.ReleaseTemporary(temporary);    // 临时 RT 归还也在 finally
            DestroyQuietly(staging);                                             // staging 贴图同样不泄漏
        }
    }
    // 输出尺寸 = 源 rect 取整；顶点像素坐标 = vertex*PPU + pivot（pivot 是相对 rect 左下角的像素），
    // 三角形重心插值 UV → 最近邻取图集 alpha；覆盖内 RGB=255/A=源值，覆盖外 A=0。
    private static Texture2D BakePose(Sprite sprite, byte[] atlasAlpha, out string failure)
    {
        failure = null;
        var rect = sprite.rect;
        int width = (int)MathF.Round(rect.width), height = (int)MathF.Round(rect.height);
        if (width < 1 || height < 1 || sprite.pixelsPerUnit <= 0f) { failure = "invalid-rect-or-ppu"; return null; }
        int atlasWidth = sprite.texture.width, atlasHeight = sprite.texture.height;
        if (atlasWidth < 1 || atlasHeight < 1) { failure = "invalid-atlas-size"; return null; }
        Vector2[] vertices; Vector2[] uvs; ushort[] triangles;
        try
        {
            vertices = sprite.vertices; uvs = sprite.uv; triangles = sprite.triangles;   // 每次烘焙只读一次（interop 分配副本）
        }
        catch { failure = "geometry-read-failed"; return null; }
        if (vertices == null || uvs == null || triangles == null || vertices.Length < 3 ||
            uvs.Length != vertices.Length || triangles.Length < 3 || triangles.Length % 3 != 0)
        { failure = "missing-or-mismatched-geometry"; return null; }
        var vertexX = new float[vertices.Length];
        var vertexY = new float[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            float x = vertices[i].x * sprite.pixelsPerUnit + sprite.pivot.x;
            float y = vertices[i].y * sprite.pixelsPerUnit + sprite.pivot.y;
            if (!IsFinite(x) || !IsFinite(y) || !IsFinite(uvs[i].x) || !IsFinite(uvs[i].y)) { failure = "non-finite-geometry"; return null; }
            vertexX[i] = x; vertexY[i] = y;
        }
        Texture2D white = null;
        try
        {
            white = new Texture2D(width, height, TextureFormat.RGBA32, false);
            white.hideFlags = HideFlags.HideAndDontSave;
            white.name = "KEM_SamuraiWhiteGhost";
            white.filterMode = sprite.texture.filterMode;
            white.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color32[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 0);   // 覆盖外：RGB255/A0
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                int i0 = triangles[t], i1 = triangles[t + 1], i2 = triangles[t + 2];
                if (i0 >= vertices.Length || i1 >= vertices.Length || i2 >= vertices.Length)
                { failure = "triangle-index-out-of-range"; DestroyQuietly(white); return null; }
                RasterizeTriangle(pixels, width, height, vertexX, vertexY, uvs, i0, i1, i2, atlasAlpha, atlasWidth, atlasHeight);
            }
            white.SetPixels32(pixels);
            white.Apply(false, false);
            return white;
        }
        catch (Exception e) { failure = "rasterize-exception:" + e.GetType().Name; DestroyQuietly(white); return null; }
    }
    private static void RasterizeTriangle(Color32[] pixels, int width, int height,
        float[] vertexX, float[] vertexY, Vector2[] uvs, int i0, int i1, int i2,
        byte[] atlasAlpha, int atlasWidth, int atlasHeight)
    {
        float ax = vertexX[i0], ay = vertexY[i0], bx = vertexX[i1], by = vertexY[i1], cx = vertexX[i2], cy = vertexY[i2];
        float area = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
        if (Mathf.Abs(area) < .0001f) return;   // 退化三角形跳过
        int minX = (int)MathF.Floor(Mathf.Min(ax, Mathf.Min(bx, cx)));
        int maxX = (int)MathF.Ceiling(Mathf.Max(ax, Mathf.Max(bx, cx)));
        int minY = (int)MathF.Floor(Mathf.Min(ay, Mathf.Min(by, cy)));
        int maxY = (int)MathF.Ceiling(Mathf.Max(ay, Mathf.Max(by, cy)));
        if (minX < 0) minX = 0;
        if (minY < 0) minY = 0;
        if (maxX > width - 1) maxX = width - 1;
        if (maxY > height - 1) maxY = height - 1;
        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                float px = x + .5f, py = y + .5f;
                float w0 = ((bx - px) * (cy - py) - (by - py) * (cx - px)) / area;
                float w1 = ((cx - px) * (ay - py) - (cy - py) * (ax - px)) / area;
                float w2 = 1f - w0 - w1;
                if (w0 < -1e-4f || w1 < -1e-4f || w2 < -1e-4f) continue;   // 像素中心重心坐标
                float u = w0 * uvs[i0].x + w1 * uvs[i1].x + w2 * uvs[i2].x;
                float v = w0 * uvs[i0].y + w1 * uvs[i1].y + w2 * uvs[i2].y;
                if (!IsFinite(u) || !IsFinite(v)) continue;
                int sx = (int)(u * atlasWidth);
                int sy = (int)(v * atlasHeight);
                if (sx < 0) sx = 0; else if (sx >= atlasWidth) sx = atlasWidth - 1;
                if (sy < 0) sy = 0; else if (sy >= atlasHeight) sy = atlasHeight - 1;
                pixels[y * width + x] = new Color32(255, 255, 255, atlasAlpha[sy * atlasWidth + sx]);
            }
    }
    private static void ReleaseWhiteSprite(WhiteKey key, WhiteSpriteEntry entry)
    {
        WhiteSprites.Remove(key);
        DestroyQuietly(entry.Sprite);
        DestroyQuietly(entry.Texture);   // 姿态贴图随 sprite 同生共死
    }
    private static bool WhiteReferenced(Sprite sprite)
    {
        foreach (var pair in Owners)
        {
            var state = pair.Value;
            if (state.Root == null) continue;
            if (state.Body != null && state.Body.enabled && Same(state.Body.sprite, sprite)) return true;
            foreach (var ghost in state.Ghosts)
                if (ghost != null && ghost.Renderer != null && ghost.Renderer.enabled && Same(ghost.Renderer.sprite, sprite)) return true;
        }
        return false;
    }
    private static void TrimWhiteSprites(WhiteSpriteEntry protect)
    {
        while (WhiteSprites.Count > MaxWhiteSprites)
        {
            if (!TryEvictWhiteSprite(out var key, protect)) break;   // 全部在飞：保留（soft cap），引用释放后下次收敛
            ReleaseWhiteSprite(key, WhiteSprites[key]);
        }
        if (WhiteSprites.Count > MaxWhiteSprites * 2 && Logged.Add("whiten-cache-overflow"))
        {
            try
            {
                KingdomEnhancedPlugin.Instance?.LogSource.LogWarning("[SamuraiDashVisuals] whiten-cache-overflow entries="
                    + WhiteSprites.Count + " softCap=" + MaxWhiteSprites + " (in-flight ghosts pin them; converges when released)");
            }
            catch { }
        }
    }
    private static bool TryEvictWhiteSprite(out WhiteKey victim, WhiteSpriteEntry protect)
    {
        victim = default; bool found = false; float oldest = float.MaxValue;
        foreach (var pair in WhiteSprites)
        {
            if (ReferenceEquals(pair.Value, protect)) continue;   // 本次刚烘焙的条目不参与本轮淘汰
            if (pair.Value.LastUsed >= oldest) continue;
            if (pair.Value.Sprite != null && WhiteReferenced(pair.Value.Sprite)) continue;   // 在飞 ghost 引用的不淘汰
            oldest = pair.Value.LastUsed; victim = pair.Key; found = true;
        }
        return found;
    }
    private static void TrimWhiteAtlases()
    {
        while (WhiteAtlases.Count > MaxWhiteAtlases || WhiteAtlasBytes > MaxWhiteAtlasBytes)
        {
            AtlasKey victim = default; bool found = false; float oldest = float.MaxValue;
            foreach (var pair in WhiteAtlases)
            {
                if (pair.Value.LastUsed >= oldest) continue;
                oldest = pair.Value.LastUsed; victim = pair.Key; found = true;
            }
            if (!found) break;
            WhiteAtlasBytes -= WhiteAtlases[victim].Alpha.Length;
            WhiteAtlases.Remove(victim);   // 纯 alpha 字节、无在飞依赖（姿态贴图已自持），可直接淘汰
        }
    }
    private static void DestroyWhiteCache()
    {
        foreach (var pair in WhiteSprites) { DestroyQuietly(pair.Value.Sprite); DestroyQuietly(pair.Value.Texture); }
        WhiteSprites.Clear();
        WhiteAtlases.Clear();
        WhiteAtlasBytes = 0;
    }
    // 白化成功的一次性诊断（只报真实已烘焙主路径；与 NOT white 降级日志区分）。
    private static void LogWhitenBaked(Sprite source, int width, int height)
    {
        if (!Logged.Add("whiten-baked")) return;
        try
        {
            var rect = source.rect;
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[SamuraiDashVisuals] whiten-baked: sprite="
                + (source.name != null ? source.name : "null")
                + " rect=" + rect.width.ToString("0.#") + "x" + rect.height.ToString("0.#")
                + " out=" + width + "x" + height + " ppu=" + source.pixelsPerUnit.ToString("0.##")
                + " pivot=" + source.pivot.x.ToString("0.##") + "," + source.pivot.y.ToString("0.##")
                + " packed=" + source.packed + " raster=uv-triangles");
        }
        catch { }
    }
    private static Sprite Whitened(Sprite source)
    {
        if (source == null) return null;
        var key = new WhiteKey(source);
        if (WhiteSprites.TryGetValue(key, out var cached)) { cached.LastUsed = Time.time; return cached.Sprite; }
        Sprite baked = null;
        Texture2D texture = null;
        try
        {
            if (source.texture == null) DegradeWhiten("missing-texture", source);
            else
            {
                var rect = source.rect;
                if (rect.width < 1f || rect.height < 1f || source.pixelsPerUnit <= 0f) DegradeWhiten("invalid-rect-or-ppu", source);
                else
                {
                    var atlasAlpha = AtlasAlpha(source.texture);
                    if (atlasAlpha != null)
                    {
                        texture = BakePose(source, atlasAlpha, out var failure);
                        if (texture == null) DegradeWhiten(failure, source);
                        else
                        {
                            int width = texture.width, height = texture.height;
                            baked = Sprite.Create(texture, new Rect(0f, 0f, width, height),
                                new Vector2(source.pivot.x / width, source.pivot.y / height),
                                source.pixelsPerUnit, source.extrude, SpriteMeshType.FullRect);
                            if (baked == null) DegradeWhiten("sprite-create-null", source);
                            else LogWhitenBaked(source, width, height);
                        }
                    }
                }
            }
        }
        catch (Exception e) { DegradeWhiten("bake-exception:" + e.GetType().Name, source); baked = null; }
        if (baked == null) { DestroyQuietly(texture); texture = null; }   // 失败不留孤儿
        var entry = new WhiteSpriteEntry { Sprite = baked, Texture = baked != null ? texture : null, LastUsed = Time.time };
        WhiteSprites[key] = entry;
        TrimWhiteSprites(entry);   // 本轮淘汰保护本次产物：绝不销毁在飞自引用
        return baked;
    }

    private static void EnsureDriver()
    {
        if (Driver != null && Driver.gameObject != null) return;
        GameObject go = null;
        try
        {
            if (!ClassInjector.IsTypeRegisteredInIl2Cpp(typeof(SamuraiDashVisualsDriver)))
                ClassInjector.RegisterTypeInIl2Cpp<SamuraiDashVisualsDriver>();
            go = new GameObject("KEM_SamuraiVisualDriver");
            var driver = go.AddComponent<SamuraiDashVisualsDriver>();
            UnityEngine.Object.DontDestroyOnLoad(go);
            Driver = driver;
        }
        catch { if (go != null) UnityEngine.Object.Destroy(go); throw; }
    }
    // 2026-09-24 残影盲审的 A/B/C 三配方诊断已定案（2026-09-25 用户裁定八道白光）：C 胜出，
    // 8 槽全部+body 走 recipe 2（C=Sprites/Default 标准材质+原贴图+纯白顶点色，ZWrite 关）
    // = 当前生产路径。A=源材质拷贝+_Overlay 白（旧线上配方）、B=A+Flash 浮点 1+FLASH_ON
    // 关键字，两臂代码保留未调用，仅作历史诊断记录。
    private static SpriteRenderer MakeRenderer(GameObject root, SpriteRenderer source, string name, int recipe)
    {
        var go = new GameObject(name);
        try
        {
            go.transform.SetParent(root.transform, false);
            go.layer = source.gameObject.layer;
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.enabled = false;
            if (recipe == 2)
            {
                // 2026-09-25 审查 P1-2：三级降级链——IL2CPP 剥离未引用着色器时不再静默退化成
                // 源调色板克隆。Sprites/Default → Unlit/Texture → 源材质克隆（保底，行为同旧两级链）。
                // Unlit/Texture 无 _Overlay/顶点色乘法能力，第三级本就是最后手段，不做额外补偿。
                // 每级降级各一条一次性日志（LogShaderFallback，内容含降级到了哪级）。
                Shader standard = Shader.Find("Sprites/Default");
                Material plain;
                if (standard != null)
                    plain = new Material(standard);
                else
                {
                    Shader unlit = Shader.Find("Unlit/Texture");
                    if (unlit != null)
                    {
                        LogShaderFallback("unlit-texture",
                            "Sprites/Default missing; ghost material degraded to Unlit/Texture (no _Overlay/vertex-color tint)");
                        plain = new Material(unlit);
                    }
                    else
                    {
                        LogShaderFallback("source-clone",
                            "Sprites/Default and Unlit/Texture missing; ghost material degraded to a source-palette clone");
                        plain = new Material(source.sharedMaterial);
                    }
                }
                renderer.sharedMaterial = plain;
                return renderer;
            }
            Material overlay = new Material(source.sharedMaterial);
            overlay.SetColor(OverlayId, Color.white);
            if (recipe == 1)
            {
                overlay.SetFloat(FlashId, 1f);
                overlay.EnableKeyword("FLASH_ON");
            }
            renderer.sharedMaterial = overlay;
            return renderer;
        }
        catch { UnityEngine.Object.Destroy(go); throw; }
    }
    private static OwnerState Build(Knight owner, SpriteRenderer source)
    {
        var s = new OwnerState { Owner = owner, Source = source };
        try
        {
            // Unparented scene object with identity transform: every child has an independent frozen world pose.
            // Unlike the small driver this root is not DontDestroyOnLoad.
            s.Root = new GameObject("KEM_SamuraiAfterimages");
            for (int i = 0; i < GhostSlots; i++)
                s.Ghosts[i] = new Ghost { Renderer = MakeRenderer(s.Root, source, "Ghost" + i, 2) };
            s.Body = MakeRenderer(s.Root, source, "BurstWhite", 2);
            return s;
        }
        catch { if (s.Root != null) UnityEngine.Object.Destroy(s.Root); throw; }
    }
    private static void Pose(SpriteRenderer renderer, SpriteRenderer source, bool body)
    {
        // 2026-09-25 根因 D：用烘焙白剪影 sprite（失败时 Whitened 返回 null → 原 sprite 回退 +
        // 一次性降级日志；绝不把原色回退写成白色成功）。
        renderer.sprite = Whitened(source.sprite) ?? source.sprite;
        // 勿再重指 sharedMaterial：会顶掉 MakeRenderer 实例化材质上的 _Overlay 白色叠加
        //（OwnerState 每武士构建一次，创建时已随源拷贝调色板，材质实例稳定）。
        renderer.flipX = source.flipX;
        renderer.flipY = source.flipY;
        renderer.gameObject.layer = source.gameObject.layer;
        renderer.sortingLayerID = source.sortingLayerID;
        int order = source.sortingOrder;
        // 2026-09-24 盲审 P2②：残影原画在 order-1=人群之下，即使白色也被后绘单位盖住。
        // 抬升：幻影 order+2（人群上、本体闪白之下）、body order+3——A/B/C 诊断臂全部
        // 换到可见层级，定案后终版沿用。
        renderer.sortingOrder = body ? (order == int.MaxValue ? order : order + 3)
            : (order == int.MinValue ? order : order + 2);
        renderer.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
        renderer.transform.localScale = source.transform.lossyScale; // Root is world identity; keep signed facing.
    }
    private static void Emit(OwnerState s, float now)
    {
        var ghost = s.Ghosts[s.NextSlot];
        Pose(ghost.Renderer, s.Source, false);
        ghost.Born = now;
        ghost.Alive = s.Tail = true;
        ghost.Renderer.color = new Color(1, 1, 1, GhostOpacity(0));
        ghost.Renderer.enabled = true;
        s.Samples++; // Count only samples whose renderer was successfully enabled.
        s.NextSlot = (s.NextSlot + 1) % GhostSlots;
        s.LastX = s.Source.transform.position.x;
        s.NextSample = now + SampleInterval;
    }
    private static void Hide(OwnerState s, string reason = "replaced")
    {
        s.Current = null;
        s.Emitting = s.Tail = false;
        if (s.Body != null) s.Body.enabled = false;
        foreach (var ghost in s.Ghosts)
        {
            if (ghost == null) continue;
            ghost.Alive = false;
            if (ghost.Renderer != null) ghost.Renderer.enabled = false;
        }
        LogVisual(s, "visual-cleared", reason);
        s.Diagnostics = null;
    }
    private static void Remove(int id, OwnerState s)
    {
        if (Owners.TryGetValue(id, out var current) && ReferenceEquals(current, s)) Owners.Remove(id);
        Hide(s, s.RetireReason ?? "owner-or-renderer-replaced");
        if (s.Root != null) UnityEngine.Object.Destroy(s.Root);
    }

    private static void CompleteTailDiagnostic(OwnerState s)
    {
        if (s.Diagnostics == null || s.Emitting || s.Tail) return;
        if (!s.Diagnostics.TailLogged)
        { s.Diagnostics.TailLogged = true; LogVisual(s, "tail-cleared", "all-ghosts-expired"); }
        s.Diagnostics = null;
    }

    private static void LogSourceSkip(SamuraiDashDiagnostics.Trace trace, SpriteRenderer source)
    {
        if (trace == null) return;
        try
        {
            string reason = source == null ? "missing-source" : source.gameObject == null ? "missing-source-object"
                : !source.gameObject.activeInHierarchy ? "source-inactive" : !source.enabled ? "source-disabled"
                : source.sprite == null ? "missing-sprite" : source.sharedMaterial == null ? "missing-material"
                : !source.sharedMaterial.HasProperty(OverlayId) ? "missing-overlay-property" : "source-changed";
            SamuraiDashDiagnostics.Write(trace, "visual-skipped", "reason=" + reason);
        }
        catch (Exception e) { SamuraiDashDiagnostics.Write(trace, "visual-skipped", "reason=state-read-failed type=" + e.GetType().Name); }
    }

    private static void LogVisual(OwnerState s, string eventName, string reason)
    {
        if (s.Diagnostics == null) return;
        try
        {
            int enabledGhosts = 0;
            foreach (var ghost in s.Ghosts)
                if (ghost != null && ghost.Renderer != null && ghost.Renderer.enabled) enabledGhosts++;
            var body = s.Body;
            var source = s.Source;
            string details = "reason=" + reason + " elapsed=" + (Time.time - s.Diagnostics.StartedAt).ToString("0.###")
                + " ghostSamples=" + s.Samples + " emitting=" + s.Emitting + " ghostSlots=" + GhostSlots + " enabledGhosts=" + enabledGhosts
                + " whitePresent=" + (body != null) + " whiteEnabled=" + (body != null && body.enabled);
            if (source != null)
            {
                details += " sourceEnabled=" + source.enabled + " sprite=" + (source.sprite != null ? source.sprite.name : "null")
                    + " sourceLayer=" + source.sortingLayerID + " sourceOrder=" + source.sortingOrder;
                var material = source.sharedMaterial;
                details += " material=" + (material != null ? material.name : "null")
                    + " shader=" + (material != null && material.shader != null ? material.shader.name : "null");
            }
            if (eventName == "visual-first-update" && s.Owner != null)
            {
                var trail = s.Owner._trail;
                details += " trailPresent=" + (trail != null);
                if (trail != null) details += " trailEnabled=" + trail.enabled + " trailEmitting=" + trail.emitting + " trailPoints=" + trail.positionCount;
            }
            SamuraiDashDiagnostics.Write(s.Diagnostics, eventName, details);
        }
        catch (Exception e) { SamuraiDashDiagnostics.Write(s.Diagnostics, eventName, "state-read-failed=" + e.GetType().Name); }
    }

    internal static Token Begin(Knight owner, SamuraiDashDiagnostics.Trace diagnostics = null)
    {
        OwnerState state = null;
        int id = 0;
        try
        {
            if (!Valid(owner)) { SamuraiDashDiagnostics.Write(diagnostics, "visual-skipped", "reason=invalid-owner"); return null; }
            if (Time.timeScale <= 0) { SamuraiDashDiagnostics.Write(diagnostics, "visual-skipped", "reason=paused"); return null; }
            if (Time.time < RetryAt) { SamuraiDashDiagnostics.Write(diagnostics, "visual-skipped", "reason=retry-backoff"); return null; }
            if (!OverlayIdReady) { OverlayId = Shader.PropertyToID("_Overlay"); FlashId = Shader.PropertyToID("_Flash"); OverlayIdReady = true; }
            id = owner.gameObject.GetInstanceID();
            var source = owner.GetComponent<SpriteRenderer>();
            if (!SourceReady(source))
            {
                RetryAt = Time.time + 5f;
                Log("source-overlay-unavailable", new InvalidOperationException());
                LogSourceSkip(diagnostics, source);
                return null;
            }
            EnsureDriver();
            if (Owners.TryGetValue(id, out state) && (!Same(state.Owner, owner) || state.Root == null || !Same(state.Source, source)))
            { Remove(id, state); state = null; }
            if (state == null) { state = Build(owner, source); Owners[id] = state; }
            Hide(state);
            state.Diagnostics = diagnostics;
            state.Samples = 0;
            state.RetireReason = null;
            var token = new Token { Owner = owner, Id = id };
            state.Current = token;
            state.Emitting = true;
            Emit(state, Time.time);
            Pose(state.Body, source, true);
            state.Body.color = new Color(1, 1, 1, .85f);
            state.Body.enabled = true;
            LogVisual(state, "visual-ready", "created-or-reused; renderer-state-not-screen-proof");
            if (Logged.Add("ready"))
            {
                try { KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[SamuraiVisuals] ready: 8 ghosts alpha=.55->.10 (-.0625/rank, floor .10) lifetime=" + Lifetime.ToString("0.##") + "s; body overlay follows burst"); }
                catch { }
            }
            return token;
        }
        catch (Exception e)
        {
            RetryAt = Time.time + 5f;
            if (state != null) { try { Remove(id, state); } catch { } }
            SamuraiDashDiagnostics.Write(diagnostics, "visual-skipped", "reason=exception type=" + e.GetType().Name);
            Log("begin", e);
            return null;
        }
    }
    internal static void End(Token token, bool immediate = false)
    {
        try
        {
            if (token == null || !Owners.TryGetValue(token.Id, out var s) ||
                !Same(s.Owner, token.Owner) || !ReferenceEquals(s.Current, token)) return;
            s.Current = null;
            s.Emitting = false;
            if (s.Body != null) s.Body.enabled = false;
            if (immediate) Hide(s, "immediate");
            else
            {
                LogVisual(s, "visual-stop", "burst-ended; remaining-ghosts-fade");
                CompleteTailDiagnostic(s);
            }
        }
        catch (Exception e) { Log("end", e); }
    }
    internal static void Clear(Knight owner)
    {
        try
        {
            if (owner == null || owner.gameObject == null) return;
            int id = owner.gameObject.GetInstanceID();
            if (Owners.TryGetValue(id, out var s) && Same(s.Owner, owner))
            { s.RetireReason = "owner-disabled-or-cleared"; Remove(id, s); }
        }
        catch (Exception e) { Log("clear", e); }
    }
    internal static void ClearAll()
    {
        var snapshot = new List<OwnerState>(Owners.Values);
        Owners.Clear();
        foreach (var s in snapshot)
        {
            try { Hide(s, "driver-disabled-or-destroyed"); if (s.Root != null) UnityEngine.Object.Destroy(s.Root); }
            catch (Exception e) { Log("clear-all", e); }
        }
        DestroyWhiteCache();   // 缓存资源随全部 owner 清理一并释放（下次使用时惰性重建）
    }
    internal static void Tick()
    {
        if (Owners.Count == 0) return;
        Retire.Clear();
        foreach (var pair in Owners)
        {
            var s = pair.Value;
            try
            {
                // Cleanup gates precede pause: disabling the mod or despawning never leaves a white actor.
                if (!Valid(s.Owner) || s.Root == null) { s.RetireReason = "owner-invalid-or-root-gone"; Retire.Add(pair.Key); continue; }
                if (!s.Emitting && !s.Tail) continue; // No renderer/material/property reads for an idle owner.
                if (!SourceReady(s.Source)) { s.RetireReason = "source-unavailable"; Retire.Add(pair.Key); continue; }
                if (Time.timeScale <= 0) continue;
                float now = Time.time;
                if (s.Emitting)
                {
                    Pose(s.Body, s.Source, true);
                    if (now >= s.NextSample && Mathf.Abs(s.Source.transform.position.x - s.LastX) >= SampleDistance) Emit(s, now);
                }
                s.Tail = false;
                int rank = 0;
                for (int n = 0; n < GhostSlots; n++)
                {
                    var ghost = s.Ghosts[(s.NextSlot + GhostSlots - 1 - n) % GhostSlots];
                    if (!ghost.Alive) continue;
                    float remaining = Mathf.Clamp01(1f - (now - ghost.Born) / Lifetime);
                    ghost.Renderer.color = new Color(1, 1, 1, GhostOpacity(rank++) * remaining);
                    if (remaining <= 0) { ghost.Alive = false; ghost.Renderer.enabled = false; }
                    else s.Tail = true;
                }
                if (s.Diagnostics != null)
                {
                    if (!s.Diagnostics.FirstUpdateLogged)
                    { s.Diagnostics.FirstUpdateLogged = true; LogVisual(s, "visual-first-update", "existing-LateUpdate-ran"); }
                    CompleteTailDiagnostic(s);
                }
            }
            catch (Exception e) { s.RetireReason = "tick-exception:" + e.GetType().Name; Retire.Add(pair.Key); Log("tick", e); }
        }
        foreach (int id in Retire)
            if (Owners.TryGetValue(id, out var s)) { try { Remove(id, s); } catch (Exception e) { Log("retire", e); } }
    }
}

public sealed class SamuraiDashVisualsDriver : MonoBehaviour
{
    public SamuraiDashVisualsDriver(IntPtr pointer) : base(pointer) { }
    private void LateUpdate() => SamuraiDashVisuals.Tick();
    private void OnDisable() => SamuraiDashVisuals.ClearAll();
    private void OnDestroy() => SamuraiDashVisuals.ClearAll();
}
