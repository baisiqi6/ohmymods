using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 自有地表 palette builder：在 Unity runtime 用嵌入的 64x32 brick/stone PNG 合成与 native tile 同 footprint 的
/// 自有 128x32 Sprite（rect 128x32 / pivot(0,0) / PPU 32 / bounds 4x1），并以同一 Sprite 填满等长 Il2Cpp list
/// 供 Root 接入 actual restoration boundary。本类不注册 Harmony hook、不发起任何游戏调用、不改动共享资产。
/// 失败语义：native sprite 契约不符、资源缺失/超限/截断/解码失败/源尺寸不符、Sprite.Create 失败或自有 bounds
/// 与 native 不符 → 抛具体异常（无 fallback/无占位）；失败时先按清单撤销全部自有 Texture/Sprite 再原样抛出。
/// 所有权：只销毁本类自建的 Texture/Sprite；原 nativeSprite 与原 list 绝不 destroy/clear；等长 list 由 Root
/// 保持强引用直到显式 DestroyAll。
/// </summary>
internal sealed class PalaceTerrainPalette
{
    private const string ResourcePrefix = "KingdomEnhancedMod.KEM_PalaceCourt/";
    private const int MaxResourceBytes = 256 * 1024;    // 单张源 PNG 字节上限
    private const int TileWidth = 128;                  // native tile 口径：128x32 @ PPU32 = bounds 4x1
    private const int TileHeight = 32;
    private const int SourceWidth = 64;                 // brick-surface.png / stone-bank.png 均为 64x32
    private const int SourceHeight = 32;
    private const float TilePixelsPerUnit = 32f;
    private const float Tolerance = 1e-4f;
    private const int MinListCount = 1;
    private const int MaxListCount = 32;
    // PNG top-down 行（已只读核验：brick 不透明行 10..13 共 4 行、stone 14..24 共 11 行，均 64 列）：
    // 上移 6px 到 dest top-down 4..18；砖面顶缘 (32-4)/32 = 0.875 与 native surface 匹配，石岸在其下。
    private const int FirstContentRow = 10;
    private const int BrickLastRow = 13;
    private const int LastContentRow = 24;
    private const int ShiftUpRows = 6;

    private readonly List<Texture2D> _textures = new List<Texture2D>(3);
    private bool _destroyed;

    /// <summary>合成后的唯一自有 tile Sprite（rect 128x32 / pivot(0,0) / PPU 32）；DestroyAll 后为 null。</summary>
    internal UnityEngine.Sprite Sprite;

    /// <summary>长度为 listCount 的自有 Il2Cpp list，每项为同一自有 Sprite；DestroyAll 后为 null。</summary>
    internal Il2CppSystem.Collections.Generic.List<UnityEngine.Sprite> Sprites;

    /// <summary>先只读校验 native tile 契约（128x32 / PPU32 / pivot(0,0) / bounds 4x1），再构建自有 palette。</summary>
    internal static PalaceTerrainPalette CreateLike(UnityEngine.Sprite nativeSprite, int listCount)
    {
        if (listCount < MinListCount || listCount > MaxListCount)
            throw new ArgumentOutOfRangeException(nameof(listCount), listCount, "listCount must be within 1..32");
        ValidateNativeSprite(nativeSprite);
        PalaceTerrainPalette palette = new PalaceTerrainPalette();
        try
        {
            palette.Build(listCount, nativeSprite.bounds.size);
            return palette;
        }
        catch
        {
            palette.DestroyAll();
            throw;
        }
    }

    internal static void ValidateNativeSprite(UnityEngine.Sprite nativeSprite)
    {
        if (nativeSprite == null) throw new ArgumentNullException(nameof(nativeSprite));
        Rect rect = nativeSprite.rect;
        if (Math.Abs(rect.width - TileWidth) > Tolerance || Math.Abs(rect.height - TileHeight) > Tolerance)
            throw new ArgumentException("native tile rect is " + Fmt(rect.width) + "x" + Fmt(rect.height)
                + ", expected 128x32", nameof(nativeSprite));
        float pixelsPerUnit = nativeSprite.pixelsPerUnit;
        if (Math.Abs(pixelsPerUnit - TilePixelsPerUnit) > Tolerance)
            throw new ArgumentException("native tile pixelsPerUnit is " + Fmt(pixelsPerUnit)
                + ", expected 32", nameof(nativeSprite));
        Vector2 pivot = nativeSprite.pivot;
        if (Math.Abs(pivot.x) > Tolerance || Math.Abs(pivot.y) > Tolerance)
            throw new ArgumentException("native tile pivot is (" + Fmt(pivot.x) + "," + Fmt(pivot.y)
                + "), expected (0,0)", nameof(nativeSprite));
        Bounds nativeBounds = nativeSprite.bounds;
        if (Math.Abs(nativeBounds.size.x - TileWidth / TilePixelsPerUnit) > 1e-3f
            || Math.Abs(nativeBounds.size.y - TileHeight / TilePixelsPerUnit) > 1e-3f)
            throw new ArgumentException("native tile bounds are " + Fmt(nativeBounds.size.x) + "x" + Fmt(nativeBounds.size.y)
                + ", expected 4x1", nameof(nativeSprite));

    }

    /// <summary>只销毁本类自建的 Texture 与 Sprite；幂等；原 nativeSprite 与原 list 绝不 destroy/clear。</summary>
    internal void DestroyAll()
    {
        if (_destroyed) return;
        _destroyed = true;
        UnityEngine.Sprite ownSprite = Sprite;
        Sprite = null;
        Sprites = null;   // 仅丢弃自有 list 的引用（交 GC）；不 clear/destroy 原生 list
        if (ownSprite != null) DestroyQuietly(ownSprite);
        foreach (Texture2D texture in _textures) DestroyQuietly(texture);
        _textures.Clear();
    }

    private void Build(int listCount, Vector3 nativeSize)
    {
        Texture2D brick = LoadSource("ground/brick-surface.png");
        Texture2D stone = LoadSource("ground/stone-bank.png");
        Texture2D tile = new Texture2D(TileWidth, TileHeight, TextureFormat.RGBA32, false);
        _textures.Add(tile);   // 先登记：此后任何失败由 DestroyAll 统一撤销
        tile.name = "KEM_PalaceTerrainPalette_tile_128x32";
        tile.filterMode = FilterMode.Point;
        tile.wrapMode = TextureWrapMode.Clamp;
        tile.anisoLevel = 0;
        Compose(tile, brick, stone);
        UnityEngine.Sprite sprite = UnityEngine.Sprite.Create(tile, new Rect(0f, 0f, TileWidth, TileHeight),
            new Vector2(0f, 0f), TilePixelsPerUnit, 0u, SpriteMeshType.FullRect);
        if (sprite == null) throw new InvalidOperationException("palace terrain palette Sprite.Create returned null");
        Sprite = sprite;   // 登记：其后 bounds 核验失败也由 DestroyAll 撤销
        sprite.name = "KEM_PalaceTerrainPalette_tile";
        // 借 native bounds 仅核验自有 footprint（不写回、不修改任何共享资产）。
        Bounds own = sprite.bounds;
        if (Math.Abs(own.size.x - nativeSize.x) > 1e-3f || Math.Abs(own.size.y - nativeSize.y) > 1e-3f)
            throw new InvalidOperationException("palace terrain palette bounds " + Fmt(own.size.x) + "x" + Fmt(own.size.y)
                + " do not match native " + Fmt(nativeSize.x) + "x" + Fmt(nativeSize.y));
        var list = new Il2CppSystem.Collections.Generic.List<UnityEngine.Sprite>();
        for (int i = 0; i < listCount; i++) list.Add(sprite);
        Sprites = list;
    }

    /// <summary>
    /// 128x32 合成（bottom-up 数组坐标）：
    /// 源行 sourceY = 31 - pngY（pngY 为 top-down 行）；destY = 31 - (pngY - 6)。
    /// pngY 10..13 取 brick、14..24 取 stone（两段行区间不交叠），dest x = x % 64 平铺；
    /// 未覆盖像素保持 (0,0,0,0) 全透明（不做不透明黑色填充），源 alpha 原样复用。
    /// </summary>
    private static void Compose(Texture2D tile, Texture2D brick, Texture2D stone)
    {
        Color32[] brickPixels = ReadSourcePixels(brick);
        Color32[] stonePixels = ReadSourcePixels(stone);
        Color32[] tilePixels = new Color32[TileWidth * TileHeight];   // 默认全透明
        for (int pngY = FirstContentRow; pngY <= LastContentRow; pngY++)
        {
            Color32[] source = pngY <= BrickLastRow ? brickPixels : stonePixels;
            int sourceBase = ((SourceHeight - 1) - pngY) * SourceWidth;
            int destBase = ((TileHeight - 1) - (pngY - ShiftUpRows)) * TileWidth;
            for (int x = 0; x < TileWidth; x++)
                tilePixels[destBase + x] = source[sourceBase + (x % SourceWidth)];
        }
        tile.SetPixels32(tilePixels);
        tile.Apply(false, false);
    }

    private Texture2D LoadSource(string relative)
    {
        byte[] bytes = ReadEmbedded(relative);
        Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);   // LoadImage 依 PNG 头重设尺寸
        _textures.Add(texture);   // 先登记：任何后续失败由 DestroyAll 统一撤销
        texture.name = "KEM_PalaceTerrainPalette_source_" + Path.GetFileNameWithoutExtension(relative);
        if (!ImageConversion.LoadImage(texture, bytes, false))
            throw new InvalidOperationException("palace terrain palette decode failed: " + relative);
        if (texture.width != SourceWidth || texture.height != SourceHeight)
            throw new InvalidOperationException("palace terrain palette source " + relative + " is "
                + texture.width + "x" + texture.height + ", expected 64x32");
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.anisoLevel = 0;
        return texture;
    }

    private static Color32[] ReadSourcePixels(Texture2D source)
    {
        Color32[] pixels = source.GetPixels32();
        if (pixels == null || pixels.Length != SourceWidth * SourceHeight)
            throw new InvalidOperationException("palace terrain palette pixel read failed: " + source.name
                + " (" + (pixels == null ? "null" : pixels.Length.ToString()) + ")");
        return pixels;
    }

    private static byte[] ReadEmbedded(string relative)
    {
        string logicalName = ResourcePrefix + relative;
        Assembly assembly = typeof(PalaceTerrainPalette).Assembly;
        using (Stream stream = assembly.GetManifestResourceStream(logicalName))
        {
            if (stream == null)
                throw new FileNotFoundException("embedded resource missing: " + logicalName);
            long length = stream.Length;
            if (length <= 0 || length > MaxResourceBytes)
                throw new InvalidDataException("resource size rejected: " + logicalName + " " + length + " bytes");
            byte[] bytes = new byte[(int)length];
            int read = 0;
            while (read < bytes.Length)
            {
                int step = stream.Read(bytes, read, bytes.Length - read);
                if (step <= 0) break;
                read += step;
            }
            if (read != bytes.Length)
                throw new InvalidDataException("resource truncated: " + logicalName + " " + read + "/" + bytes.Length);
            return bytes;
        }
    }

    /// <summary>清理阶段逐个尝试：单个销毁异常不阻塞其余自有对象的撤销。</summary>
    private static void DestroyQuietly(UnityEngine.Object obj)
    {
        try { if (obj != null) UnityEngine.Object.Destroy(obj); } catch (Exception) { }
    }

    private static string Fmt(float value) => value.ToString("0.####");
}
