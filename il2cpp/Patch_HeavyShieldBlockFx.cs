// Issue #313 presentation-only leaf: a deterministic blue/purple gem flash on an already
// successful heavy-shield Block/Break. The existing owned body renderer keeps its sprite,
// color, material, anchor and hop. This file owns one same-shaped palette renderer plus three
// fixed micro sparks beneath that owned root. No writes to the native actor/base renderer,
// damage/policy/identity input,
// RPC, scene scan, shader bundle, per-unit material or particle system.
using System;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>Natural-clock pulse. A hit never refreshes an active pulse or bypasses the
/// receive interval; pause/unknown/backward clocks only cancel this decoration.</summary>
internal struct HeavyShieldGemPulse
{
    internal const double Duration = .28;
    internal const double MinInterval = .45;
    internal const float PeakAlpha = .62f;
    private const double Epsilon = .000000001;

    private double _start;
    private double _lastAccepted;
    private double _lastObserved;
    private bool _active;
    private bool _hasAccepted;
    private bool _hasObserved;

    internal bool IsActive => _active;

    internal bool TryStart(double now, bool playing)
    {
        if (!Observe(now, playing)) return false;
        if (_active)
        {
            if (now - _start + Epsilon < Duration) return false;
            _active = false;
        }
        if (_hasAccepted && now - _lastAccepted + Epsilon < MinInterval) return false;
        _start = now;
        _lastAccepted = now;
        _hasAccepted = true;
        _active = true;
        return true;
    }

    internal float Sample(double now, bool playing)
    {
        if (!Observe(now, playing) || !_active) return 0f;
        double elapsed = now - _start;
        if (elapsed + Epsilon >= Duration) { _active = false; return 0f; }
        float alpha = (float)(PeakAlpha * (1d - elapsed / Duration));
        return alpha > PeakAlpha ? PeakAlpha : alpha;
    }

    internal void Cancel() { _active = false; }

    private bool Observe(double now, bool playing)
    {
        if (!IsFinite(now) || now < 0d || (_hasObserved && now < _lastObserved))
        { Cancel(); return false; }
        _lastObserved = now;
        _hasObserved = true;
        if (!playing) { Cancel(); return false; }
        return true;
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}

/// <summary>Pure texel mapping. Source alpha is copied exactly; transparent texels become
/// (0,0,0,0). Luminance picks one of 16 levels on a fixed diagonal blue/purple facet.</summary>
internal static class HeavyShieldGemPalette
{
    internal static uint Map(uint source, int x, int y)
    {
        byte alpha = (byte)(source >> 24);
        if (alpha == 0) return 0u;
        byte r = (byte)source;
        byte g = (byte)(source >> 8);
        byte b = (byte)(source >> 16);
        int luma = (r * 54 + g * 183 + b * 19) >> 8;
        int level = luma >> 4;
        if (level < 0) level = 0; else if (level > 15) level = 15;
        bool purple = (((x + y) / 5) & 1) != 0;
        int darkR = purple ? 42 : 14, darkG = purple ? 18 : 28, darkB = purple ? 78 : 70;
        int brightR = purple ? 226 : 150, brightG = purple ? 186 : 214, brightB = 255;
        byte outR = Ramp(darkR, brightR, level);
        byte outG = Ramp(darkG, brightG, level);
        byte outB = Ramp(darkB, brightB, level);
        return ((uint)alpha << 24) | ((uint)outB << 16) | ((uint)outG << 8) | outR;
    }

    /// <summary>3x3 diamond/cross spark; corners stay clear.</summary>
    internal static uint SparkPixel(int x, int y)
    {
        if (x < 0 || x > 2 || y < 0 || y > 2) return 0u;
        if (x == 1 && y == 1) return Pack(235, 248, 255, 255);
        if ((x == 1 && (y == 0 || y == 2)) || (y == 1 && (x == 0 || x == 2)))
            return Pack(150, 205, 255, 255);
        return 0u;
    }

    private static uint Pack(byte r, byte g, byte b, byte a)
        => ((uint)a << 24) | ((uint)b << 16) | ((uint)g << 8) | r;

    private static byte Ramp(int dark, int bright, int level)
    {
        int value = dark + ((bright - dark) * level + 7) / 15;
        return (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
    }
}

/// <summary>Per-body optional sink. It only reads the current owned sprite renderer in a
/// qualified visible tick; every failure closes this one life without touching the body.</summary>
internal sealed class HeavyShieldBlockFx
{
    private const int MaxSlots = 2;
    private const int SparkCount = 3;

    private readonly struct Key : IEquatable<Key>
    {
        internal readonly long Pointer;
        internal readonly int Instance;
        internal readonly int Density;
        internal Key(long pointer, int instance, int density) { Pointer = pointer; Instance = instance; Density = density; }
        public bool Equals(Key other) => Pointer == other.Pointer && Instance == other.Instance && Density == other.Density;
        public override bool Equals(object obj) => obj is Key other && Equals(other);
        public override int GetHashCode() => unchecked(Pointer.GetHashCode() * 397 ^ Instance * 31 ^ Density);
    }

    private sealed class Slot
    {
        internal Key Key;
        internal Texture2D Texture;
        internal Sprite[] Sprites;
        internal int Refs;
    }

    private static readonly Slot[] Slots = new Slot[MaxSlots];
    private static bool _loggedFailure;

    private SpriteRenderer _own;
    private GameObject _root;
    private SpriteRenderer _body;
    private SpriteRenderer[] _sparks;
    private Texture2D _sparkTexture;
    private Sprite _sparkSprite;
    private int _sparkDensity = -1;
    private Slot _slot;
    private HeavyShieldGemPulse _pulse;
    private int _frame = -1;
    private float _sparkPpu;
    private bool _prepared, _closed, _layerOn;

    internal static HeavyShieldBlockFx TryCreate(SpriteRenderer own)
    {
        if (own == null || own.gameObject == null || own.transform == null) return null;
        var fx = new HeavyShieldBlockFx { _own = own };
        try
        {
            fx._root = new GameObject("KEM_HeavyShieldGemFx");
            fx._root.layer = own.gameObject.layer;
            fx._root.transform.SetParent(own.transform, false);
            fx._root.transform.localPosition = Vector3.zero;
            fx._root.transform.localRotation = Quaternion.identity;
            fx._root.transform.localScale = Vector3.one;
            fx._body = fx._root.AddComponent<SpriteRenderer>();
            if (fx._body == null) throw new InvalidOperationException("owned palette renderer absent");
            fx._body.enabled = false;
            fx._sparks = new SpriteRenderer[SparkCount];
            for (int i = 0; i < SparkCount; i++)
            {
                var spark = new GameObject("KEM_HeavyShieldGemSpark" + i);
                spark.layer = own.gameObject.layer;
                spark.transform.SetParent(fx._root.transform, false);
                spark.transform.localPosition = Vector3.zero;
                spark.transform.localRotation = Quaternion.identity;
                spark.transform.localScale = Vector3.one;
                var renderer = spark.AddComponent<SpriteRenderer>();
                if (renderer == null) throw new InvalidOperationException("owned spark renderer absent");
                renderer.enabled = false;
                fx._sparks[i] = renderer;
            }
            return fx;
        }
        catch (Exception)
        {
            fx.Dispose();
            return null;
        }
    }

    /// <summary>First qualified visible tick only. Never called from a hit.</summary>
    internal void Prepare(SpriteRenderer own, int frame)
    {
        if (_closed || _prepared) return;
        try { PrepareCore(own, frame); }
        catch (Exception e) { FailClosed("prepare " + e.GetType().Name); }
    }

    private void PrepareCore(SpriteRenderer own, int frame)
    {
        if (own == null || !ReferenceEquals(own, _own) || own.sprite == null)
        { FailClosed("source sprite unavailable"); return; }
        if (frame < 0 || frame >= HeavyShieldArtLayout.SoldierFrameCount)
        { FailClosed("source frame unavailable"); return; }
        if (!EnsureSlot(own.sprite)) return;
        if (!EnsureSpark(own.sprite, _slot.Key.Density)) { FailClosed("spark unavailable"); return; }
        _prepared = true;
    }

    internal void OnBlock()
    {
        if (_closed) return;
        try { _pulse.TryStart(AndroidUnityCompat.UnscaledSeconds(), HeavyShieldRuntime.Advancing); }
        catch (Exception) { _pulse.Cancel(); }
    }

    /// <summary>Called at the end of a qualified visible tick, after Own's final commit.</summary>
    internal void Sync(int frame, int worldFacing)
    {
        if (_closed) { HideLayer(); return; }
        try { SyncCore(frame, worldFacing); }
        catch (Exception e) { FailClosed("sync " + e.GetType().Name); }
    }

    private void SyncCore(int frame, int worldFacing)
    {
        if (!_pulse.IsActive) { HideLayer(); return; }
        float alpha;
        try { alpha = _pulse.Sample(AndroidUnityCompat.UnscaledSeconds(), HeavyShieldRuntime.Advancing); }
        catch (Exception) { _pulse.Cancel(); alpha = 0f; }
        if (alpha <= 0f) { HideLayer(); return; }
        if (_own == null || _own.sprite == null || _body == null) { HideLayer(); return; }
        Color sourceColor = _own.color;
        float sourceAlpha = sourceColor.a;
        if (!(sourceAlpha > 0f) || !IsFinite(sourceAlpha)
            || !IsFinite(sourceColor.r) || !IsFinite(sourceColor.g) || !IsFinite(sourceColor.b))
        { HideAndCancel(); return; }
        if (!_prepared)
        {
            PrepareCore(_own, frame);
            if (_closed) { HideLayer(); return; }
        }
        Sprite source = _own.sprite;
        if (!EnsureSlot(source)) { HideLayer(); return; }
        if (!EnsureSpark(source, _slot.Key.Density)) { FailClosed("spark unavailable"); return; }
        Sprite frameSprite = GetFrameSprite(_slot, source, frame);
        if (frameSprite == null) { FailClosed("frame sprite unavailable"); return; }
        if (_frame != frame || !ReferenceEquals(_body.sprite, frameSprite))
        { _body.sprite = frameSprite; _frame = frame; }
        ApplyLook(_own);
        float a = alpha * Math.Min(sourceAlpha, 1f);
        if (a < 0f) a = 0f; else if (a > HeavyShieldGemPulse.PeakAlpha) a = HeavyShieldGemPulse.PeakAlpha;
        var color = new Color(sourceColor.r, sourceColor.g, sourceColor.b, a);
        if (!_layerOn)
        {
            _layerOn = true; // Cleanup owns even a partially enabled layer.
            _root.SetActive(true);
        }
        _body.color = color;
        for (int i = 0; i < _sparks.Length; i++)
        {
            var spark = _sparks[i];
            if (spark == null) continue;
            if (!ReferenceEquals(spark.sprite, _sparkSprite)) spark.sprite = _sparkSprite;
            spark.color = color;
            if (!spark.enabled) spark.enabled = true;
        }
        PlaceSparks(_own);
        if (!_body.enabled) _body.enabled = true;
        _layerOn = true;
    }

    internal void HideAndCancel()
    {
        try { _pulse.Cancel(); } catch (Exception) { }
        HideLayer(true);
    }

    private void HideLayer(bool force = false)
    {
        if (!force && !_layerOn) return;
        _layerOn = true; // Retain cleanup responsibility until root deactivation succeeds.
        try { if (_body != null) _body.enabled = false; } catch (Exception) { }
        if (_sparks != null) for (int i = 0; i < _sparks.Length; i++)
        {
            try { if (_sparks[i] != null) _sparks[i].enabled = false; } catch (Exception) { }
        }
        try
        {
            if (_root != null) _root.SetActive(false);
            _layerOn = false;
        }
        catch (Exception) { }
    }

    private bool EnsureSlot(Sprite source)
    {
        if (source == null) { FailClosed("source sprite unavailable"); return false; }
        if (!Describe(source, out Key key, out int width, out int height)) { FailClosed("unknown source atlas"); return false; }
        if (_slot != null && _slot.Key.Equals(key)) return true;
        HideLayer();
        try { if (_body != null) _body.sprite = null; } catch (Exception) { }
        ReleaseSlot();
        Slot slot = Acquire(key, width, height, source);
        if (slot == null) { FailClosed("palette cache unavailable"); return false; }
        _slot = slot;
        _frame = -1;
        return true;
    }

    private static bool Describe(Sprite source, out Key key, out int width, out int height)
    {
        key = default; width = 0; height = 0;
        Texture2D texture = source.texture;
        if (texture == null) return false;
        width = texture.width;
        height = texture.height;
        float ppu;
        int cellWidth, cellHeight, density;
        if (width == 1024 && height == 1536)
        { density = 0; ppu = HeavyShieldArtLayout.SoldierPixelsPerUnit; cellWidth = 64; cellHeight = 48; }
        else if (width == 768 && height == 1024)
        { density = 1; ppu = HeavyShieldArtLayout.SoldierCoarsePixelsPerUnit; cellWidth = 48; cellHeight = 32; }
        else return false;
        Rect rect = source.rect;
        if (rect.width != cellWidth || rect.height != cellHeight || rect.x < 0f || rect.y < 0f
            || rect.x + rect.width > width || rect.y + rect.height > height) return false;
        float actualPpu = source.pixelsPerUnit;
        if (!IsFinite(actualPpu) || actualPpu <= 0f || Math.Abs(actualPpu - ppu) > .01f) return false;
        Vector2 pivot = source.pivot;
        if (!IsFinite(pivot.x) || !IsFinite(pivot.y)) return false;
        key = new Key(texture.Pointer.ToInt64(), texture.GetInstanceID(), density);
        return true;
    }

    private static Slot Acquire(Key key, int width, int height, Sprite source)
    {
        for (int i = 0; i < Slots.Length; i++)
        {
            Slot slot = Slots[i];
            if (slot == null || !slot.Key.Equals(key)) continue;
            slot.Refs++;
            return slot;
        }
        int free = -1;
        for (int i = 0; i < Slots.Length; i++)
        {
            if (Slots[i] == null || Slots[i].Refs <= 0) { free = i; break; }
        }
        if (free < 0) return null;
        if (Slots[free] != null) { DestroySlot(Slots[free]); Slots[free] = null; }
        var created = new Slot { Key = key };
        if (!Bake(created, width, height, source))
        {
            DestroySlot(created);
            return null;
        }
        created.Refs = 1;
        Slots[free] = created;
        return created;
    }

    private static bool Bake(Slot slot, int width, int height, Sprite source)
    {
        Color32[] sourcePixels;
        try { sourcePixels = ReadPixels(source.texture, width, height); }
        catch (Exception) { return false; }
        if (sourcePixels == null || sourcePixels.Length != width * height) return false;
        if (!ValidSparkEnvelope(sourcePixels, width, height)) return false;
        Texture2D texture = null;
        try
        {
            texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var output = new Color32[sourcePixels.Length];
            int i = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++, i++)
                {
                    Color32 pixel = sourcePixels[i];
                    uint packed = ((uint)pixel.a << 24) | ((uint)pixel.b << 16) | ((uint)pixel.g << 8) | pixel.r;
                    uint mapped = HeavyShieldGemPalette.Map(packed, x, y);
                    output[i] = new Color32((byte)mapped, (byte)(mapped >> 8), (byte)(mapped >> 16), (byte)(mapped >> 24));
                }
            }
            texture.SetPixels32(output);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.anisoLevel = 0;
            texture.Apply(false, false);
            slot.Texture = texture;
            slot.Sprites = new Sprite[HeavyShieldArtLayout.SoldierFrameCount];
            return true;
        }
        catch (Exception)
        {
            Destroy(texture);
            return false;
        }
    }

    private static Color32[] ReadPixels(Texture2D texture, int width, int height)
    {
#if ANDROID
        return AndroidTexturePixels.ReadOwnedPng(texture, width, height);
#else
        return texture.GetPixels32();
#endif
    }

    private static Sprite GetFrameSprite(Slot slot, Sprite source, int frame)
    {
        if (slot == null || slot.Sprites == null || slot.Texture == null
            || frame < 0 || frame >= slot.Sprites.Length || source == null) return null;
        Sprite cached = slot.Sprites[frame];
        if (cached != null) return cached;
        Rect rect = source.rect;
        if (rect.x < 0f || rect.y < 0f || rect.x + rect.width > slot.Texture.width
            || rect.y + rect.height > slot.Texture.height) return null;
        Vector2 pixelPivot = source.pivot;
        Sprite created = Sprite.Create(slot.Texture, rect,
            new Vector2(pixelPivot.x / rect.width, pixelPivot.y / rect.height),
            source.pixelsPerUnit, 0u, SpriteMeshType.FullRect);
        if (created == null) return null;
        slot.Sprites[frame] = created;
        return created;
    }

    private bool EnsureSpark(Sprite source, int density)
    {
        if (_sparkTexture == null)
        {
            Texture2D created = null;
            try
            {
                created = new Texture2D(3, 3, TextureFormat.RGBA32, false);
                var pixels = new Color32[9];
                for (int y = 0; y < 3; y++)
                {
                    for (int x = 0; x < 3; x++)
                    {
                        uint c = HeavyShieldGemPalette.SparkPixel(x, y);
                        pixels[y * 3 + x] = new Color32((byte)c, (byte)(c >> 8), (byte)(c >> 16), (byte)(c >> 24));
                    }
                }
                created.SetPixels32(pixels);
                created.filterMode = FilterMode.Point;
                created.wrapMode = TextureWrapMode.Clamp;
                created.anisoLevel = 0;
                created.Apply(false, false);
                _sparkTexture = created;
            }
            catch (Exception)
            {
                Destroy(created);
                return false;
            }
        }
        if (_sparkSprite == null || _sparkDensity != density)
        {
            if (_sparkSprite != null) { Destroy(_sparkSprite); _sparkSprite = null; }
            _sparkSprite = Sprite.Create(_sparkTexture, new Rect(0f, 0f, 3f, 3f),
                new Vector2(.5f, .5f), source.pixelsPerUnit, 0u, SpriteMeshType.FullRect);
            if (_sparkSprite == null) return false;
            _sparkDensity = density;
            for (int i = 0; i < _sparks.Length; i++)
                if (_sparks[i] != null) _sparks[i].sprite = _sparkSprite;
        }
        _sparkPpu = source.pixelsPerUnit;
        return true;
    }

    // Derived from all 509 alpha bounds in BOTH shipped atlases. These are foot-local
    // positions; the parent carries hop/scale, and only the renderer's flip is extra.
    private static void SparkPoint(int index, out float x, out float y)
    {
        x = index == 0 ? -.16f : index == 1 ? .11f : -.03f;
        y = index == 0 ? .52f : index == 1 ? .34f : .10f;
    }

    private void PlaceSparks(SpriteRenderer own)
    {
        bool flipX = own.flipX;
#if ANDROID
        bool flipY = false;
#else
        bool flipY = own.flipY;
#endif
        for (int i = 0; i < SparkCount; i++)
        {
            SparkPoint(i, out float x, out float y);
            x = (float)Math.Round(x * _sparkPpu) / _sparkPpu;
            y = (float)Math.Round(y * _sparkPpu) / _sparkPpu;
            SetSpark(i, flipX ? -x : x, flipY ? -y : y);
        }
    }

    // An unknown replacement texture with the same dimensions must still support the
    // fixed sparkle positions. Validate once while pixels are available, not per frame.
    private static bool ValidSparkEnvelope(Color32[] pixels, int width, int height)
    {
        bool coarse = width == HeavyShieldArtLayout.SoldierCoarseCellWidth * HeavyShieldArtLayout.SoldierColumns;
        int cw = coarse ? HeavyShieldArtLayout.SoldierCoarseCellWidth : HeavyShieldArtLayout.SoldierCellWidth;
        int ch = coarse ? HeavyShieldArtLayout.SoldierCoarseCellHeight : HeavyShieldArtLayout.SoldierCellHeight;
        float ppu = coarse ? HeavyShieldArtLayout.SoldierCoarsePixelsPerUnit : HeavyShieldArtLayout.SoldierPixelsPerUnit;
        float px = coarse ? HeavyShieldArtLayout.SoldierCoarseFootPixelX + .5f : HeavyShieldArtLayout.SoldierFootPixelX + .5f;
        float py = ch - (coarse ? HeavyShieldArtLayout.SoldierCoarseFootPixelYTop : HeavyShieldArtLayout.SoldierFootPixelYTop) - .5f;
        for (int frame = 0; frame < HeavyShieldArtLayout.SoldierFrameCount; frame++)
        {
            int sx = frame % HeavyShieldArtLayout.SoldierColumns * cw;
            int sy = height - (frame / HeavyShieldArtLayout.SoldierColumns + 1) * ch;
            int left = cw, right = 0, bottom = ch, top = 0;
            for (int y = 0; y < ch; y++) for (int x = 0; x < cw; x++)
            {
                if (pixels[(sy + y) * width + sx + x].a == 0) continue;
                left = Math.Min(left, x); right = Math.Max(right, x + 1);
                bottom = Math.Min(bottom, y); top = Math.Max(top, y + 1);
            }
            if (right <= left || top <= bottom) return false;
            float mx = (right - left) * .05f, my = (top - bottom) * .05f;
            for (int i = 0; i < SparkCount; i++)
            {
                SparkPoint(i, out float x, out float y);
                float cx = px + (float)Math.Round(x * ppu), cy = py + (float)Math.Round(y * ppu);
                if (cx - 1.5f < left - mx - .0001f || cx + 1.5f > right + mx + .0001f
                    || cy - 1.5f < bottom - my - .0001f || cy + 1.5f > top + my + .0001f) return false;
            }
        }
        return true;
    }

    private void SetSpark(int index, float x, float y)
    {
        if (_sparks == null || index < 0 || index >= _sparks.Length || _sparks[index] == null) return;
        _sparks[index].transform.localPosition = new Vector3(x, y, -.001f);
    }

    private void ApplyLook(SpriteRenderer own)
    {
        if (own == null) return;
        int order = own.sortingOrder == int.MaxValue ? int.MaxValue : own.sortingOrder + 1;
        int layer = own.gameObject.layer;
        Material material = own.sharedMaterial;
        if (_body != null)
        {
            _body.sortingLayerID = own.sortingLayerID;
            _body.sortingOrder = order;
            _body.gameObject.layer = layer;
            if (material != null) _body.sharedMaterial = material;
            _body.flipX = own.flipX;
#if !ANDROID
            _body.flipY = own.flipY;
#endif
        }
        if (_sparks == null) return;
        for (int i = 0; i < _sparks.Length; i++)
        {
            var spark = _sparks[i];
            if (spark == null) continue;
            spark.sortingLayerID = own.sortingLayerID;
            spark.sortingOrder = order;
            spark.gameObject.layer = layer;
            if (material != null) spark.sharedMaterial = material;
            spark.flipX = own.flipX;
#if !ANDROID
            spark.flipY = own.flipY;
#endif
        }
    }

    private void ReleaseSlot()
    {
        if (_slot == null) return;
        try { if (_body != null) _body.sprite = null; } catch (Exception) { }
        if (_slot.Refs > 0) _slot.Refs--;
        _slot = null;
        _frame = -1;
    }

    private void FailClosed(string reason)
    {
        if (_closed) return;
        _closed = true;
        _prepared = false;
        HideAndCancel();
        try { if (_body != null) _body.sprite = null; } catch (Exception) { }
        ReleaseSlot();
        if (_loggedFailure) return;
        _loggedFailure = true;
        try { KingdomEnhancedPlugin.Instance?.LogSource?.LogWarning("[HeavyShieldBlockFx] " + reason); }
        catch (Exception) { }
    }

    internal void Dispose()
    {
        _closed = true;
        _prepared = false;
        try { _pulse.Cancel(); } catch (Exception) { }
        HideLayer(true);
        try { if (_body != null) _body.sprite = null; } catch (Exception) { }
        ReleaseSlot();
        if (_sparkSprite != null) { Destroy(_sparkSprite); _sparkSprite = null; }
        if (_sparkTexture != null) { Destroy(_sparkTexture); _sparkTexture = null; }
        if (_root != null) { Destroy(_root); _root = null; }
        _body = null;
        _sparks = null;
        _own = null;
        _sparkDensity = -1;
    }

    /// <summary>Clears only zero-ref owned cache slots; never a texture a live life still uses.</summary>
    internal static void ClearUnused()
    {
        for (int i = 0; i < Slots.Length; i++)
        {
            Slot slot = Slots[i];
            if (slot == null || slot.Refs > 0) continue;
            DestroySlot(slot);
            Slots[i] = null;
        }
    }

    private static void DestroySlot(Slot slot)
    {
        if (slot == null) return;
        if (slot.Sprites != null)
        {
            for (int i = 0; i < slot.Sprites.Length; i++)
                if (slot.Sprites[i] != null) Destroy(slot.Sprites[i]);
            slot.Sprites = null;
        }
        if (slot.Texture != null) { Destroy(slot.Texture); slot.Texture = null; }
        slot.Refs = 0;
    }

    private static void Destroy(UnityEngine.Object obj)
    {
        try { if (obj != null) UnityEngine.Object.Destroy(obj); } catch (Exception) { }
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
