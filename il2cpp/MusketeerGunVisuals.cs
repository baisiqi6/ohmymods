using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

// Presentation only. Native Bow pool, colliders, pickup policy and Persistent stay untouched.
internal static class MusketeerGunVisuals
{
    private sealed class Entry
    {
        internal DroppableTool Tool;
        internal int Id;
        internal SpriteRenderer Native, View;
        internal GameObject Child;
        internal bool Owned, Original;
        internal int Seen;
    }
    private static readonly Dictionary<IntPtr, Entry> Entries = new();
    private static readonly List<DroppableTool> Guns = new();
    private static readonly List<IntPtr> Remove = new();
    private static Sprite _sprite;
    private static Texture2D _texture;
    private static int _frame = -1;
    internal static void Sync()
    {
        if (_frame == Time.frameCount) return;
        _frame = Time.frameCount;
        bool enabled = MusketeerAccess.Enabled;
        if (enabled && Load())
        {
            MusketeerIdentity.CopyGuns(Guns);
            foreach (var tool in Guns)
            {
                if (tool == null || tool.gameObject == null || !MusketeerAccess.InWorld(tool)) continue;
                IntPtr key = tool.Pointer;
                if (Entries.TryGetValue(key, out var old) && old.Id != tool.gameObject.GetInstanceID())
                {
                    if (!Release(old)) continue;
                    Entries.Remove(key);
                }
                if (!Entries.TryGetValue(key, out var entry))
                {
                    var native = tool.GetComponent<SpriteRenderer>();
                    if (native == null || native.forceRenderingOff) continue; // do not take another system's hidden object
                    var child = new GameObject("KEM_MusketeerGunView");
                    child.transform.SetParent(tool.transform, false);
                    var view = child.AddComponent<SpriteRenderer>();
                    view.sprite = _sprite;
                    entry = new Entry { Tool = tool, Id = tool.gameObject.GetInstanceID(), Native = native, Child = child, View = view, Original = native.forceRenderingOff };
                    Entries.Add(key, entry);
                }
                entry.Seen = _frame;
                bool visible = !tool.pickedUp && !tool.IsFake() && entry.Native != null && entry.Native.enabled;
                if (entry.View != null) entry.View.enabled = visible;
                if (!visible) { Restore(entry); continue; }
                entry.Child.transform.localPosition = new Vector3(0f, MusketeerShop.IsRackGun(tool) ? 0.23f : 0f, -0.001f);
                entry.View.sortingLayerID = entry.Native.sortingLayerID;
                entry.View.sortingOrder = entry.Native.sortingOrder;
                if (entry.Native.sharedMaterial != null) entry.View.sharedMaterial = entry.Native.sharedMaterial;
                entry.View.color = entry.Native.color;
                if (!entry.Owned)
                {
                    if (entry.Native.forceRenderingOff) { entry.View.enabled = false; continue; }
                    entry.Original = false; entry.Owned = true;
                }
                entry.Native.forceRenderingOff = true;
            }
        }
        Remove.Clear();
        foreach (var pair in Entries)
            if (!enabled || pair.Value.Seen != _frame) { if (Release(pair.Value)) Remove.Add(pair.Key); }
        foreach (var key in Remove) Entries.Remove(key);
    }
    private static bool Restore(Entry entry)
    {
        try
        {
            if (!entry.Owned) return true;
            if (entry.Native != null && entry.Tool != null && entry.Tool.gameObject != null
                && entry.Tool.gameObject.GetInstanceID() == entry.Id && entry.Native.forceRenderingOff)
                entry.Native.forceRenderingOff = entry.Original;
            entry.Owned = false;
            return true;
        }
        catch { return false; }
    }
    private static bool Release(Entry entry)
    {
        if (entry.View != null) entry.View.enabled = false;
        if (!Restore(entry)) return false;
        if (entry.Child != null) { entry.Child.SetActive(false); UnityEngine.Object.Destroy(entry.Child); }
        return true;
    }
    internal static void BeforeReuse(GameObject root)
    {
        if (root == null) return;
        foreach (var entry in Entries.Values)
        {
            if (entry.Tool == null || entry.Tool.gameObject == null || entry.Tool.gameObject.Pointer != root.Pointer) continue;
            if (entry.View != null) entry.View.enabled = false;
            Restore(entry); entry.Seen = -1; // destruction remains in ordinary LateUpdate
        }
    }
    private static bool Load()
    {
        if (_sprite != null) return true;
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("KingdomEnhancedMod.MusketeerGun.png");
        if (stream == null || stream.Length > 65536) return false;
        using var bytes = new MemoryStream(); stream.CopyTo(bytes);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!ImageConversion.LoadImage(texture, bytes.ToArray(), false) || texture.width != 26 || texture.height != 8)
        { UnityEngine.Object.Destroy(texture); return false; }
        texture.filterMode = FilterMode.Point; texture.wrapMode = TextureWrapMode.Clamp;
        _texture = texture;
        _sprite = Sprite.Create(texture, new Rect(0, 0, 26, 8), new Vector2(0.5f, 0.5f), 32f, 0u, SpriteMeshType.FullRect);
        return _sprite != null;
    }
    [HarmonyPatch(typeof(DroppableTool), nameof(DroppableTool.OnEnable))]
    private static class ReusePatch
    {
        [HarmonyPrefix] private static void Before(DroppableTool __instance)
        { try { if (__instance != null) BeforeReuse(__instance.gameObject); } catch { } }
    }
}
