using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 纯显示的金币飞行（国库→袋、袋→骑士）：固定条数池 + 程序生成的小金币精灵，
/// 只做位置/帧表现，不生成 DroppableCurrency、不写 Wallet/银行/账本、不持有身份或任务。
/// 每次交接确认 Applied 后由运行时调用一次；同一次交接绝不会因此产生第二次账务。
///
/// 契约：
/// * 无自动 Update；位置/生命期全部由调用方 Tick(gameDelta) 推进（暂停传 0 即冻结）。
/// * 池满复用最旧一枚，条数上限 <see cref="CoinCourierTiming.CoinFlightCapacity"/>。
/// * Clear 用于世界卸载/功能关闭；失败路径一次性告警并静默，绝不抛给调用方。
/// </summary>
internal static class CoinCourierCoinFlight
{
    private const int CoinPixels = 12;
    private const float PixelsPerUnit = 32f;

    private sealed class Slot
    {
        internal GameObject Root;
        internal SpriteRenderer Renderer;
        internal float Age;
        internal bool Active;
        internal Vector2 From;
        internal Vector2 To;
    }

    private static readonly List<Slot> Slots = new(CoinCourierTiming.CoinFlightCapacity);
    private static readonly HashSet<string> Warned = new();
    private static GameObject _root;
    private static Sprite _sprite;
    private static bool _spriteResolved;
    private static Material _material;
    private static bool _materialResolved;

    internal static int ActiveCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < Slots.Count; i++)
            {
                if (Slots[i].Active) count++;
            }
            return count;
        }
    }

    /// <summary>从 from 飞到 to；排序跟随调用方给定的渲染层（默认 0/0）。</summary>
    internal static void Begin(Vector3 from, Vector3 to)
        => Begin(from, to, 0, 0);

    internal static void Begin(Vector3 from, Vector3 to, int sortingLayerID, int sortingOrder)
    {
        if (!float.IsFinite(from.x) || !float.IsFinite(from.y)
            || !float.IsFinite(to.x) || !float.IsFinite(to.y)) return;
        try
        {
            Slot slot = Acquire();
            if (slot == null) return;
            slot.From = new Vector2(from.x, from.y);
            slot.To = new Vector2(to.x, to.y);
            slot.Age = 0f;
            slot.Active = true;
            if (slot.Renderer != null)
            {
                slot.Renderer.sprite = EnsureSprite();
                slot.Renderer.sortingLayerID = sortingLayerID;
                slot.Renderer.sortingOrder = sortingOrder;
                slot.Renderer.enabled = slot.Renderer.sprite != null;
            }
            if (slot.Root != null)
            {
                slot.Root.transform.position = from;
                slot.Root.SetActive(true);
            }
            Apply(slot, 0f);
        }
        catch (Exception e)
        {
            WarnOnce("begin failed: " + e.GetType().Name);
        }
    }

    /// <summary>由调用方每帧推进；非正/非有限值（暂停、卡帧）直接忽略，不改变任何状态。</summary>
    internal static void Tick(float gameDelta)
    {
        if (Slots.Count == 0) return;
        if (!float.IsFinite(gameDelta) || gameDelta <= 0f) return;
        for (int i = 0; i < Slots.Count; i++)
        {
            Slot slot = Slots[i];
            if (!slot.Active) continue;
            slot.Age += gameDelta;
            float progress = slot.Age / CoinCourierTiming.CoinFlightSeconds;
            if (!(progress < 1f))
            {
                Deactivate(slot);
                continue;
            }
            Apply(slot, progress);
        }
    }

    /// <summary>销毁全部飞行金币（世界卸载/关闭功能）；之后可继续 Begin。</summary>
    internal static void Clear()
    {
        for (int i = 0; i < Slots.Count; i++)
        {
            Slot slot = Slots[i];
            GameObject root = slot.Root;
            slot.Root = null;
            slot.Renderer = null;
            slot.Active = false;
            if (root != null) DestroyQuietly(root);
        }
        Slots.Clear();
        if (_root != null)
        {
            DestroyQuietly(_root);
            _root = null;
        }
    }

    private static void Apply(Slot slot, float progress)
    {
        if (!CoinCourierPlacementRules.TryCoinFlightSample(slot.From.x, slot.From.y, slot.To.x, slot.To.y,
                progress, out float x, out float y)) return;
        if (slot.Root == null) return;
        Transform transform = slot.Root.transform;
        transform.position = new Vector3(x, y, transform.position.z);
    }

    private static Slot Acquire()
    {
        for (int i = 0; i < Slots.Count; i++)
        {
            if (!Slots[i].Active) return Slots[i];
        }
        if (Slots.Count < CoinCourierTiming.CoinFlightCapacity)
        {
            Slot created = CreateSlot();
            if (created != null) Slots.Add(created);
            return created;
        }
        Slot oldest = null;
        for (int i = 0; i < Slots.Count; i++)
        {
            if (oldest == null || Slots[i].Age > oldest.Age) oldest = Slots[i];
        }
        return oldest;   // 池满：复用最旧一枚（固定有限条数）
    }

    private static Slot CreateSlot()
    {
        try
        {
            if (_root == null)
            {
                // 不跨 world 保活：根对象随场景销毁；世界结束时运行时还会显式调用 Clear()。
                _root = new GameObject("KEM_CoinCourierCoinFlight");
            }
            GameObject go = new GameObject("KEM_CoinCourierCoin");
            go.transform.SetParent(_root.transform, false);
            SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
            if (renderer == null)
            {
                DestroyQuietly(go);
                WarnOnce("sprite renderer missing on a new coin");
                return null;
            }
            Material material = ResolveMaterial();
            if (material != null) renderer.sharedMaterial = material;
            renderer.enabled = false;
            go.SetActive(false);
            return new Slot { Root = go, Renderer = renderer };
        }
        catch (Exception e)
        {
            WarnOnce("coin create failed: " + e.GetType().Name);
            return null;
        }
    }

    private static void Deactivate(Slot slot)
    {
        slot.Active = false;
        slot.Age = 0f;
        try
        {
            if (slot.Renderer != null) slot.Renderer.enabled = false;
            if (slot.Root != null) slot.Root.SetActive(false);
        }
        catch (Exception)
        {
            // 停用失败不抛出：下一帧 Tick 会再次尝试。
        }
    }

    /// <summary>12×12 程序金币（外圈暗金 + 中心亮金 + 左上高光）；只解码一次。</summary>
    private static Sprite EnsureSprite()
    {
        if (_spriteResolved) return _sprite;
        _spriteResolved = true;
        try
        {
            Texture2D texture = new Texture2D(CoinPixels, CoinPixels, TextureFormat.RGBA32, false);
            Color32[] pixels = new Color32[CoinPixels * CoinPixels];
            float center = (CoinPixels - 1) * 0.5f;
            for (int y = 0; y < CoinPixels; y++)
            {
                for (int x = 0; x < CoinPixels; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    Color32 pixel = new Color32(0, 0, 0, 0);
                    if (distance <= 5.6f)
                    {
                        bool rim = distance > 4.6f;
                        bool highlight = dx < -1.6f && dy > 1.4f && distance < 3.4f;
                        if (highlight) pixel = new Color32(255, 244, 190, 255);
                        else if (rim) pixel = new Color32(176, 122, 22, 255);
                        else pixel = new Color32(247, 199, 66, 255);
                    }
                    pixels[y * CoinPixels + x] = pixel;
                }
            }
            texture.SetPixels32(pixels);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.anisoLevel = 0;
            texture.Apply(false, false);
            _sprite = Sprite.Create(texture, new Rect(0f, 0f, CoinPixels, CoinPixels),
                new Vector2(0.5f, 0.5f), PixelsPerUnit, 0u, SpriteMeshType.FullRect);
        }
        catch (Exception e)
        {
            _sprite = null;
            WarnOnce("coin sprite failed: " + e.GetType().Name);
        }
        return _sprite;
    }

    private static Material ResolveMaterial()
    {
        if (_materialResolved) return _material;
        _materialResolved = true;
        try
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null) _material = new Material(shader);
        }
        catch (Exception)
        {
            _material = null;
        }
        if (_material == null) WarnOnce("Sprites/Default shader unavailable; coins keep the renderer default material");
        return _material;
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
            KingdomEnhancedPlugin.Instance?.LogSource?.LogWarning("[CoinCourierCoins] " + reason);
        }
        catch (Exception)
        {
            // 日志不可用时保持静默：表现层绝不因日志失败而中断。
        }
    }
}
