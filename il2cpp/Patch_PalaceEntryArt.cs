using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 入口可见节点自有 art 服务：唯一 PNG（EmbeddedResource
/// KingdomEnhancedMod.KEM_PalaceEntryIdle.png，192x192画布、PPU 64、pivot 底中；原字节不可变、不缩放像素）。
/// 只做冷加载/缓存同一 Sprite 与释放；Texture 一律 Point/Clamp。
/// 失败（资源缺失、字节数异常、解码失败、尺寸不符、Sprite.Create 失败）抛明确异常且不进入缓存，
/// 由 Root 捕获并清理自身对象：无占位 fallback，也不在本类内自动重试。
/// 只会销毁本类自建的 Sprite/Texture；绝不 Destroy 原生借用对象。
/// </summary>
internal static class PalaceEntryArt
{
    internal const string ResourceName = "KingdomEnhancedMod.KEM_PalaceEntryIdle.png";
    private const int ExpectedWidth = 192;
    private const int ExpectedHeight = 192;
    private const float PixelsPerUnit = 64f;
    private const int MaxResourceBytes = 64 * 1024;   // 实际 <16KiB；上限只拒绝被异常替换的资源

    /// <summary>付前资源契约（只读；precheck 与 GetSprite 共用同一 LogicalName/IHDR 尺寸/字节上限；
    /// 本资源目前无 literal hash，缺口由 Root 后续补 literal 时自动 supersede）。</summary>
    internal static void DescribeResourceContract(List<PalaceResourceContract> into)
    {
        if (into == null) return;
        into.Add(new PalaceResourceContract(ResourceName, null, ExpectedWidth, ExpectedHeight, MaxResourceBytes, "entry-idle"));
    }

    private static Texture2D _texture;
    private static Sprite _sprite;

    /// <summary>冷加载并缓存同一 Sprite；成功后重复调用返回同一实例（本类内不做重试机制）。</summary>
    internal static Sprite GetSprite()
    {
        if (_sprite != null) return _sprite;

        byte[] bytes = ReadResource();
        Texture2D texture = null;
        Sprite sprite = null;
        try
        {
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, bytes, false))
                throw new InvalidOperationException("PalaceEntryIdle decode failed: " + ResourceName);
            if (texture.width != ExpectedWidth || texture.height != ExpectedHeight)
                throw new InvalidOperationException("PalaceEntryIdle size " + texture.width + "x" + texture.height +
                    " != expected " + ExpectedWidth + "x" + ExpectedHeight);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.anisoLevel = 0;

            // Read-only PNG alpha bounds: top-down (5,41,188,183), so Unity bottom-up rect
            // is (5,9,183,142). Drop transparent canvas padding; retain original pixels and PPU.
            sprite = Sprite.Create(texture, new Rect(5f, 9f, 183f, 142f),
                new Vector2(0.5f, 0f), PixelsPerUnit, 0u, SpriteMeshType.FullRect);
            if (sprite == null)
                throw new InvalidOperationException("PalaceEntryIdle Sprite.Create failed: " + ResourceName);

            _texture = texture;
            _sprite = sprite;
            return sprite;
        }
        catch
        {
            // 失败路径：回收本次部分创建的自有对象（均在缓存写入前，不影响既有实例）
            if (sprite != null) DestroyQuietly(sprite);
            DestroyQuietly(texture);
            throw;
        }
    }

    /// <summary>只销毁本类自有的 Sprite/Texture 并清缓存；Root 须在入口 Clear（不再引用）后调用。</summary>
    internal static void Release()
    {
        DestroyQuietly(_sprite);
        _sprite = null;
        DestroyQuietly(_texture);
        _texture = null;
    }

    private static byte[] ReadResource()
    {
        Assembly assembly = typeof(PalaceEntryArt).Assembly;
        using (Stream stream = assembly.GetManifestResourceStream(ResourceName))
        {
            if (stream == null)
                throw new InvalidOperationException("PalaceEntryIdle resource missing: " + ResourceName);
            long length = stream.Length;
            if (length <= 0 || length > MaxResourceBytes)
                throw new InvalidOperationException("PalaceEntryIdle resource size rejected: " + length + " bytes");
            byte[] bytes = new byte[(int)length];
            int read = 0;
            while (read < bytes.Length)
            {
                int step = stream.Read(bytes, read, bytes.Length - read);
                if (step <= 0) break;
                read += step;
            }
            if (read != bytes.Length)
                throw new InvalidOperationException("PalaceEntryIdle resource truncated " + read + "/" + bytes.Length);
            return bytes;
        }
    }

    private static void DestroyQuietly(UnityEngine.Object obj)
    {
        if (obj == null) return;
        try { UnityEngine.Object.Destroy(obj); }
        catch (Exception e) { PalaceVisibleEntry.Log("own art cleanup failed: " + e); }
    }
}
