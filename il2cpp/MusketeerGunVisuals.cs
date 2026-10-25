using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

// Presentation only. Native Bow pool, colliders, pickup policy and Persistent stay untouched.
//
// 原生 gun renderer 隐藏：PC 原路径为 DroppableTool SpriteRenderer.forceRenderingOff；Android 2.4
// 没有该 ICall，隐藏原语改为共享 Renderer.enabled（AndroidUnityCompat.Hide/Show，写后读回确认）。
// Android 语义差：我们写下的隐藏与游戏自己写 enabled=false 共用同一字段、不可区分，因此
// 「游戏禁用该 renderer」不能再作为 view 可见性判据（PC 由 forceRenderingOff/enabled 区分）。
internal static class MusketeerGunVisuals
{
    private sealed class Entry
    {
        internal DroppableTool Tool;
        internal int Id;
        internal SpriteRenderer Native, View;
        internal GameObject Child;
        internal bool Owned, Original, Parked;
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
                if (!Entries.TryGetValue(key, out var entry) && (entry = CreateEntry(tool)) == null) continue;
                entry.Seen = _frame;
                bool visible = !tool.pickedUp && !tool.IsFake() && entry.Native != null && NativeRenderable(entry);
                if (entry.View != null) entry.View.enabled = visible && !entry.Parked;
                if (!visible) { Restore(entry); continue; }
                entry.Child.transform.localPosition = new Vector3(0f, 0f, -0.001f);
                bool onRack = MusketeerShop.TryGetRackSorting(tool, out int rackLayer, out int rackOrder);
                entry.View.sortingLayerID = onRack ? rackLayer : entry.Native.sortingLayerID;
                entry.View.sortingOrder = onRack ? rackOrder : entry.Native.sortingOrder;
                if (entry.Native.sharedMaterial != null) entry.View.sharedMaterial = entry.Native.sharedMaterial;
                entry.View.color = entry.Native.color;
                if (!entry.Owned)
                {
                    // PC 原式：if (native.forceRenderingOff) → 第三方已隐藏时不夺权。
                    // Android 2.4：IsHidden 读共享 enabled（谁写的不可区分，沿用 PC 的“不夺权”行为）。
                    if (AndroidUnityCompat.IsHidden(entry.Native)) { entry.View.enabled = false; continue; }
                    entry.Original = false; entry.Owned = true;
                }
                // PC 原式：entry.Native.forceRenderingOff = true。
                // Android 2.4：写共享 enabled 并读回确认，未生效抛异常（Sync 外层既有 catch）。
                AndroidUnityCompat.Hide(entry.Native);
            }
        }
        Remove.Clear();
        foreach (var pair in Entries)
            if (!enabled || (!pair.Value.Parked && pair.Value.Seen != _frame)) { if (Release(pair.Value)) Remove.Add(pair.Key); }
        foreach (var key in Remove) Entries.Remove(key);
    }
    private static Entry CreateEntry(DroppableTool tool)
    {
        if (tool == null || tool.gameObject == null || !Load()) return null;
        var native = tool.GetComponent<SpriteRenderer>();
        // PC 原式：native == null || native.forceRenderingOff（已隐藏 → 不接管）。
        // Android 2.4：IsHidden 读共享 enabled。
        if (native == null || AndroidUnityCompat.IsHidden(native)) return null;
        var child = new GameObject("KEM_MusketeerGunView");
        child.transform.SetParent(tool.transform, false);
        var view = child.AddComponent<SpriteRenderer>(); view.sprite = _sprite;
        // PC 原式：Original = native.forceRenderingOff；这里正常分支恒为 false（上面已拒绝隐藏的
        // renderer），IsHidden 在 PC 上逐字等价、Android 上读共享 enabled。
        var entry = new Entry { Tool = tool, Id = tool.gameObject.GetInstanceID(), Native = native,
            Child = child, View = view, Original = AndroidUnityCompat.IsHidden(native) };
        Entries.Add(tool.Pointer, entry); return entry;
    }
    /// <summary>
    /// PC original: <c>entry.Native.enabled</c>（游戏自己的 renderer 开关）。
    /// Android 2.4：<see cref="AndroidUnityCompat.Hide"/> 写的就是同一个共享 <c>Renderer.enabled</c>
    /// 字段，因此“我们把隐藏写进去”不能让 entry 被误判为第三方禁用；游戏自己写的 enabled=false
    /// 与我们的隐藏在该 player 上不可区分。
    /// </summary>
    private static bool NativeRenderable(Entry entry)
    {
#if ANDROID
        return entry.Owned || entry.Native.enabled;
#else
        return entry.Native.enabled;
#endif
    }

    /// <summary>
    /// PC original: <c>!parked &amp;&amp; entry.Native.forceRenderingOff &amp;&amp; !tool.pickedUp
    /// &amp;&amp; !tool.IsFake() &amp;&amp; entry.Native.enabled</c>。
    /// Android 2.4：forceRenderingOff 缺失且隐藏写入共享 enabled，因此不能再要求 enabled=true。
    /// <c>entry.Owned</c> 是“我们已写下隐藏”的凭据：唯一调用方（CustomShopStocks/ShopStockTransit）
    /// 先 park 再 restore，restore 时 Owned 必为 true 且隐藏仍在，PC 原式在该路径上等价于本式；
    /// 游戏禁用 renderer 与我们的隐藏不可区分（见文件头注释）。
    /// </summary>
    private static bool NativeGunVisible(Entry entry, DroppableTool tool, bool parked)
    {
#if ANDROID
        return !parked && entry.Owned && !tool.pickedUp && !tool.IsFake();
#else
        return !parked && entry.Native.forceRenderingOff && !tool.pickedUp && !tool.IsFake()
            && entry.Native.enabled;
#endif
    }

    internal static bool IsParked(DroppableTool tool)
        => tool != null && tool.gameObject != null && Entries.TryGetValue(tool.Pointer, out var entry)
            && entry.Id == tool.gameObject.GetInstanceID() && entry.Parked;
    internal static void SetParked(DroppableTool tool, bool parked)
    {
        if (tool == null || tool.gameObject == null) throw new InvalidOperationException("paid gun missing");
        if (!Entries.TryGetValue(tool.Pointer, out var entry)) entry = CreateEntry(tool);
        if (entry == null || entry.Id != tool.gameObject.GetInstanceID())
            throw new InvalidOperationException("paid gun visual unavailable or reused");
        if (parked) entry.Parked = true; // retain restore responsibility if a native write throws
        if (!entry.Owned)
        {
            // PC 原式：if (native.forceRenderingOff) → 原生 renderer 属于别人。
            // Android 2.4：IsHidden 读共享 enabled（游戏禁用与第三方隐藏不可区分，一律拒绝夺权）。
            if (AndroidUnityCompat.IsHidden(entry.Native)) throw new InvalidOperationException("native renderer belongs to another owner");
            entry.Original = false; entry.Owned = true;
        }
        if (parked)
        {
            // PC 原式：forceRenderingOff = true + 显式读回确认。
            // Android 2.4：写共享 enabled（Hide 内部已读回确认），显式确认判断保留。
            AndroidUnityCompat.Hide(entry.Native);
            if (!AndroidUnityCompat.IsHidden(entry.Native)) throw new InvalidOperationException("native gun visibility write unconfirmed");
        }
        bool visible = NativeGunVisible(entry, tool, parked);
        entry.View.enabled = visible;
        if (entry.View.enabled != visible) throw new InvalidOperationException("paid gun visibility write unconfirmed");
        if (!parked) entry.Parked = false; // commit release only after the native renderer write
    }
    private static bool Restore(Entry entry)
    {
        try
        {
            if (!entry.Owned) return true;
            if (entry.Native != null && entry.Tool != null && entry.Tool.gameObject != null
                && entry.Tool.gameObject.GetInstanceID() == entry.Id
                && AndroidUnityCompat.IsHidden(entry.Native))
            {
                // PC 原式：entry.Native.forceRenderingOff = entry.Original。Original 在唯一可达路径
                // （CreateEntry 拒绝已隐藏 renderer）恒为 false，因此正常恢复等价于 Show；理论上
                // 不可达的 Original=true 仍按 PC 原义写回隐藏。两者都做写后读回确认。
                if (entry.Original) AndroidUnityCompat.Hide(entry.Native);
                else AndroidUnityCompat.Show(entry.Native);
            }
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
            entry.Parked = false;
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
