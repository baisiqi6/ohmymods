using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>Knight-style empty hooks / occupied banners / torn-banner death feedback.</summary>
internal static class HeroShopBannerVisuals
{
    private static GameObject _root;
    private static SpriteRenderer _left, _right;
    private static Texture2D _atlas;
    private static Sprite[] _sprites;
    private static int _leftState = -1, _rightState = -1, _leftFrame = -1, _rightFrame = -1;
    private static float _leftReveal, _rightReveal, _nextUpdate;
    private static bool _failed, _logged;

    internal static void Tick(GameObject shop, SpriteRenderer shopRenderer)
    {
        try
        {
            if (shop == null || shopRenderer == null || !shop.activeInHierarchy || _failed) return;
            if (_root == null || _root.Pointer != shop.Pointer)
            {
                Clear();
                if (!Load()) { _failed = true; Warn("banner artwork unavailable"); return; }
                _root = shop;
                _left = Create(shopRenderer, "KEM_HeroSeat_Left", -36f / 32f);
                _right = Create(shopRenderer, "KEM_HeroSeat_Right", 30f / 32f);
            }
            if (Time.unscaledTime >= _nextUpdate)
            {
                _nextUpdate = Time.unscaledTime + 0.25f;
                Sample(-1, ref _leftState, ref _leftReveal);
                Sample(1, ref _rightState, ref _rightReveal);
            }
            Render(_left, _leftState, _leftReveal, ref _leftFrame);
            Render(_right, _rightState, _rightReveal, ref _rightFrame);
        }
        catch (Exception e) { Clear(); _failed = true; Warn(e.GetType().Name); }
    }

    private static SpriteRenderer Create(SpriteRenderer source, string name, float x)
    {
        var flag = new GameObject(name);
        try
        {
            flag.transform.SetParent(_root.transform, false);
            flag.transform.localPosition = new Vector3(x, 1.4375f, -0.001f);
            var renderer = flag.AddComponent<SpriteRenderer>();
            renderer.sortingLayerID = source.sortingLayerID;
            // Native sprites write depth. Keep the building and its flags in the same
            // sorting band, using local Z for the flag in front of the facade. Drawing
            // flags in a later band lets intervening player/FX depth cut into them.
            renderer.sortingOrder = source.sortingOrder;
            if (source.sharedMaterial != null) renderer.sharedMaterial = source.sharedMaterial;
            return renderer;
        }
        catch { UnityEngine.Object.Destroy(flag); throw; }
    }

    internal static int VisualState(HeroShopSeatState state, bool fallen)
    {
        // A reserved/unknown owner still occupies its slot; it must never look vacant or dead.
        if (state == HeroShopSeatState.Occupied) return 2;
        if (state == HeroShopSeatState.Reserved) return 3;
        return fallen ? 4 : state == HeroShopSeatState.Available ? 1 : 0;
    }

    private static void Sample(int side, ref int previous, ref float reveal)
    {
        int current = VisualState(HeroRecruitment.GetShopSeatState(side), HeroRecruitment.HasFallenSeat(side));
        if (current == previous) return;
        if (current == 2 || current == 3) reveal = Time.time;
        previous = current;
    }

    private static void Render(SpriteRenderer renderer, int state, float reveal, ref int previousFrame)
    {
        if (renderer == null) throw new InvalidOperationException("shop banner destroyed");
        renderer.enabled = state >= 2;
        if (state < 2) { previousFrame = -1; return; }
        int frame = state == 2 ? (int)(Time.time / 0.22f) % 4 : 0;
        int index = frame * 5 + state;
        if (index != previousFrame) { renderer.sprite = _sprites[index]; previousFrame = index; }
        float y = state == 2 || state == 3 ? Mathf.Lerp(0.05f, 1f, Mathf.SmoothStep(0f, 1f, (Time.time - reveal) / 0.6f)) : 1f;
        renderer.transform.localScale = new Vector3(1f, y, 1f);
    }

    internal static void Clear()
    {
        try { if (_left != null) UnityEngine.Object.Destroy(_left.gameObject); } catch { }
        try { if (_right != null) UnityEngine.Object.Destroy(_right.gameObject); } catch { }
        _root = null; _left = null; _right = null;
        _leftState = -1; _rightState = -1; _leftFrame = -1; _rightFrame = -1; _nextUpdate = 0f;
        // One immutable atlas/twenty cached sprites are reused across island shop instances.
    }

    private static bool Load()
    {
        if (_sprites != null && _sprites.Length == 20 && _atlas != null) return true;
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("KingdomEnhancedMod.HeroShopSeats.png");
        if (stream == null || stream.Length > 128 * 1024) return false;
        using var bytes = new MemoryStream(); stream.CopyTo(bytes);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        var sprites = new Sprite[20];
        try
        {
            if (!ImageConversion.LoadImage(texture, bytes.ToArray(), false) || texture.width != 80 || texture.height != 160)
                throw new InvalidDataException("banner atlas dimensions");
            texture.filterMode = FilterMode.Point; texture.wrapMode = TextureWrapMode.Clamp; texture.anisoLevel = 0;
            for (int frame = 0; frame < 4; frame++)
                for (int state = 0; state < 5; state++)
                {
                    int index = frame * 5 + state;
                    sprites[index] = Sprite.Create(texture, new Rect(state * 16, (3 - frame) * 40, 16, 40),
                        new Vector2(0.5f, 1f), 32f, 0u, SpriteMeshType.FullRect);
                    if (sprites[index] == null) throw new InvalidDataException("banner sprite");
                }
            _atlas = texture; _sprites = sprites; return true;
        }
        catch
        {
            foreach (var sprite in sprites) if (sprite != null) UnityEngine.Object.Destroy(sprite);
            UnityEngine.Object.Destroy(texture); throw;
        }
    }

    private static void Warn(string reason)
    {
        if (_logged) return; _logged = true;
        try { KingdomEnhancedPlugin.Instance?.LogSource?.LogWarning("[HeroShopBanners] " + reason); } catch { }
    }
}
