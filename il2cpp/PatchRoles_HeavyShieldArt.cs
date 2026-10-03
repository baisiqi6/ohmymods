// Final gem shield assets: detail is the default; coarse is a complete alternative.
// Manifests: artifacts/gem-shield-assets-20261001/ (77 sequences / 509 slots).
// Row-major atlas order starts at the top-left; Unity rect Y is converted from the bottom.
// Each resource loads once, uses Point/Clamp/no mipmaps, and fails closed on invalid data.
using System;
#if !HEAVY_SHIELD_ART_CORE_ONLY
using System.IO;
using System.Reflection;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;
#endif

namespace KingdomEnhancedMod;

internal enum HeavyShieldAtlasId
{
    Soldier = 0,
    Shop = 1,
    ShopLayers = 2,
    GreekShopLayers = 3,
    SoldierCoarse = 4,
    ShopCoarse = 5,
    ShopLayersCoarse = 6,
    GreekShopLayersCoarse = 7,
}

internal enum HeavyShieldArtDensity { Detail = 0, Coarse = 1 }

internal readonly struct HeavyShieldSequence
{
    internal readonly string Name;
    internal readonly int First;
    internal readonly int Count;
    internal readonly float Fps;
    internal readonly bool Loop;

    internal HeavyShieldSequence(string name, int first, int count, float fps = 12f, bool loop = false)
    {
        Name = name;
        First = first;
        Count = count;
        Fps = fps;
        Loop = loop;
    }
    internal int Last => First + Count - 1;
}

/// <summary>Pure metadata. Pixel-center foot pivots preserve the artist's top-left foot point.</summary>
internal static class HeavyShieldArtLayout
{
    internal const float SoldierPixelsPerUnit = 45.7142857f;
    internal const float SoldierCoarsePixelsPerUnit = 31.4285714f;
    internal const float ShopPixelsPerUnit = 48f;
    internal const float ShopCoarsePixelsPerUnit = 32f;
    internal const string SoldierResource = "KingdomEnhancedMod.HeavyShieldSoldierAtlas.png";
    internal const string SoldierCoarseResource = "KingdomEnhancedMod.HeavyShieldSoldierAtlasCoarse.png";
    internal const string ShopResource = "KingdomEnhancedMod.HeavyShieldShopAtlas.png";
    internal const string ShopCoarseResource = "KingdomEnhancedMod.HeavyShieldShopAtlasCoarse.png";
    internal const string ShopLayersResource = "KingdomEnhancedMod.HeavyShieldShopLayers.png";
    internal const string ShopLayersCoarseResource = "KingdomEnhancedMod.HeavyShieldShopLayersCoarse.png";
    internal const string GreekShopLayersResource = "KingdomEnhancedMod.HeavyShieldGreekShopLayers.png";
    internal const string GreekShopLayersCoarseResource = "KingdomEnhancedMod.HeavyShieldGreekShopLayersCoarse.png";
    internal const string PaidShieldResource = "KingdomEnhancedMod.HeavyShieldPaidShield.png";
    internal const string PaidShieldCoarseResource = "KingdomEnhancedMod.HeavyShieldPaidShieldCoarse.png";

    internal const int SoldierColumns = 16, SoldierRows = 32, SoldierFrameCount = 509;
    internal const int SoldierCellWidth = 64, SoldierCellHeight = 48;
    internal const int SoldierCoarseCellWidth = 48, SoldierCoarseCellHeight = 32;
    internal const int SoldierSheetWidth = SoldierColumns * SoldierCellWidth;
    internal const int SoldierSheetHeight = SoldierRows * SoldierCellHeight;
    internal const float SoldierFootPixelX = 24f, SoldierFootPixelYTop = 43f;
    internal const float SoldierCoarseFootPixelX = 17f, SoldierCoarseFootPixelYTop = 29f;
    internal const float SoldierPivotX = (SoldierFootPixelX + .5f) / SoldierCellWidth;
    internal const float SoldierPivotY = (SoldierCellHeight - SoldierFootPixelYTop - .5f) / SoldierCellHeight;
    internal const float SoldierCoarsePivotX = (SoldierCoarseFootPixelX + .5f) / SoldierCoarseCellWidth;
    internal const float SoldierCoarsePivotY = (SoldierCoarseCellHeight - SoldierCoarseFootPixelYTop - .5f) / SoldierCoarseCellHeight;

    internal const int ShopColumns = 4, ShopRows = 4, ShopFrameCount = 13, ShopLayerFrameCount = 14;
    internal const int ShopCellWidth = 144, ShopCellHeight = 90;
    internal const int ShopCoarseCellWidth = 96, ShopCoarseCellHeight = 60;
    internal const int ShopSheetWidth = ShopColumns * ShopCellWidth;
    internal const int ShopSheetHeight = ShopRows * ShopCellHeight;
    internal const float ShopGroundPixelX = 72f, ShopGroundPixelYTop = 84f;
    internal const float ShopCoarseGroundPixelX = 48f, ShopCoarseGroundPixelYTop = 56f;
    internal const float ShopPivotX = (ShopGroundPixelX + .5f) / ShopCellWidth;
    internal const float ShopPivotY = (ShopCellHeight - ShopGroundPixelYTop - .5f) / ShopCellHeight;
    internal const float ShopCoarsePivotX = (ShopCoarseGroundPixelX + .5f) / ShopCoarseCellWidth;
    internal const float ShopCoarsePivotY = (ShopCoarseCellHeight - ShopCoarseGroundPixelYTop - .5f) / ShopCoarseCellHeight;

    internal const int PaidShieldWidth = 19, PaidShieldHeight = 30;
    internal const int PaidShieldCoarseWidth = 13, PaidShieldCoarseHeight = 20;
    internal const float PaidShieldPivotX = .5f, PaidShieldPivotY = 0f;

    private static readonly HeavyShieldSequence[] SoldierSequenceTable =
    {
        new HeavyShieldSequence("back_idle", 0, 4, 4f, true),
        new HeavyShieldSequence("back_walk", 4, 6, 9f, true),
        new HeavyShieldSequence("walk_start", 10, 3, 9f, false),
        new HeavyShieldSequence("walk_start_alt", 13, 3, 9f, false),
        new HeavyShieldSequence("walk_stop", 16, 3, 9f, false),
        new HeavyShieldSequence("walk_stop_alt", 19, 3, 9f, false),
        new HeavyShieldSequence("guard_idle", 22, 4, 4f, true),
        new HeavyShieldSequence("equip", 26, 23, 15f, false),
        new HeavyShieldSequence("stow", 49, 23, 15f, false),
        new HeavyShieldSequence("defense_advance", 72, 6, 8f, true),
        new HeavyShieldSequence("block", 78, 5, 12f, false),
        new HeavyShieldSequence("bash", 83, 8, 12f, false),
        new HeavyShieldSequence("worn_back_idle", 91, 4, 4f, true),
        new HeavyShieldSequence("worn_back_walk", 95, 6, 9f, true),
        new HeavyShieldSequence("worn_walk_start", 101, 3, 9f, false),
        new HeavyShieldSequence("worn_walk_start_alt", 104, 3, 9f, false),
        new HeavyShieldSequence("worn_walk_stop", 107, 3, 9f, false),
        new HeavyShieldSequence("worn_walk_stop_alt", 110, 3, 9f, false),
        new HeavyShieldSequence("worn_guard_idle", 113, 4, 4f, true),
        new HeavyShieldSequence("worn_equip", 117, 23, 15f, false),
        new HeavyShieldSequence("worn_stow", 140, 23, 15f, false),
        new HeavyShieldSequence("worn_defense_advance", 163, 6, 8f, true),
        new HeavyShieldSequence("worn_block", 169, 5, 12f, false),
        new HeavyShieldSequence("worn_bash", 174, 8, 12f, false),
        new HeavyShieldSequence("critical_back_idle", 182, 4, 4f, true),
        new HeavyShieldSequence("critical_back_walk", 186, 6, 9f, true),
        new HeavyShieldSequence("critical_walk_start", 192, 3, 9f, false),
        new HeavyShieldSequence("critical_walk_start_alt", 195, 3, 9f, false),
        new HeavyShieldSequence("critical_walk_stop", 198, 3, 9f, false),
        new HeavyShieldSequence("critical_walk_stop_alt", 201, 3, 9f, false),
        new HeavyShieldSequence("critical_guard_idle", 204, 4, 4f, true),
        new HeavyShieldSequence("critical_equip", 208, 23, 15f, false),
        new HeavyShieldSequence("critical_stow", 231, 23, 15f, false),
        new HeavyShieldSequence("critical_defense_advance", 254, 6, 8f, true),
        new HeavyShieldSequence("critical_block", 260, 5, 12f, false),
        new HeavyShieldSequence("critical_bash", 265, 8, 12f, false),
        new HeavyShieldSequence("half_back_idle", 273, 4, 4f, true),
        new HeavyShieldSequence("half_back_walk", 277, 6, 9f, true),
        new HeavyShieldSequence("half_walk_start", 283, 3, 9f, false),
        new HeavyShieldSequence("half_walk_start_alt", 286, 3, 9f, false),
        new HeavyShieldSequence("half_walk_stop", 289, 3, 9f, false),
        new HeavyShieldSequence("half_walk_stop_alt", 292, 3, 9f, false),
        new HeavyShieldSequence("half_guard_idle", 295, 4, 4f, true),
        new HeavyShieldSequence("half_equip", 299, 23, 15f, false),
        new HeavyShieldSequence("half_stow", 322, 23, 15f, false),
        new HeavyShieldSequence("half_defense_advance", 345, 6, 8f, true),
        new HeavyShieldSequence("half_block", 351, 5, 12f, false),
        new HeavyShieldSequence("half_bash", 356, 8, 12f, false),
        new HeavyShieldSequence("break", 364, 5, 12f, false),
        new HeavyShieldSequence("back_run", 369, 8, 14f, true),
        new HeavyShieldSequence("run_start", 377, 3, 14f, false),
        new HeavyShieldSequence("run_stop", 380, 4, 12f, false),
        new HeavyShieldSequence("relax_idle", 384, 8, 4f, true),
        new HeavyShieldSequence("rest_enter", 392, 3, 6f, false),
        new HeavyShieldSequence("rest_idle", 395, 6, 4f, true),
        new HeavyShieldSequence("rest_exit", 401, 3, 6f, false),
        new HeavyShieldSequence("worn_back_run", 404, 8, 14f, true),
        new HeavyShieldSequence("worn_run_start", 412, 3, 14f, false),
        new HeavyShieldSequence("worn_run_stop", 415, 4, 12f, false),
        new HeavyShieldSequence("worn_relax_idle", 419, 8, 4f, true),
        new HeavyShieldSequence("worn_rest_enter", 427, 3, 6f, false),
        new HeavyShieldSequence("worn_rest_idle", 430, 6, 4f, true),
        new HeavyShieldSequence("worn_rest_exit", 436, 3, 6f, false),
        new HeavyShieldSequence("critical_back_run", 439, 8, 14f, true),
        new HeavyShieldSequence("critical_run_start", 447, 3, 14f, false),
        new HeavyShieldSequence("critical_run_stop", 450, 4, 12f, false),
        new HeavyShieldSequence("critical_relax_idle", 454, 8, 4f, true),
        new HeavyShieldSequence("critical_rest_enter", 462, 3, 6f, false),
        new HeavyShieldSequence("critical_rest_idle", 465, 6, 4f, true),
        new HeavyShieldSequence("critical_rest_exit", 471, 3, 6f, false),
        new HeavyShieldSequence("half_back_run", 474, 8, 14f, true),
        new HeavyShieldSequence("half_run_start", 482, 3, 14f, false),
        new HeavyShieldSequence("half_run_stop", 485, 4, 12f, false),
        new HeavyShieldSequence("half_relax_idle", 489, 8, 4f, true),
        new HeavyShieldSequence("half_rest_enter", 497, 3, 6f, false),
        new HeavyShieldSequence("half_rest_idle", 500, 6, 4f, true),
        new HeavyShieldSequence("half_rest_exit", 506, 3, 6f, false),
    };
    private static readonly HeavyShieldSequence[] ShopSequenceTable =
    {
        new HeavyShieldSequence("states", 0, 7, 8f, false),
        new HeavyShieldSequence("idle", 7, 6, 8f, true),
    };
    private static readonly HeavyShieldSequence[] ShopLayerSequenceTable =
    {
        new HeavyShieldSequence("rear", 0, 1, 8f, false),
        new HeavyShieldSequence("fixtures", 1, 6, 8f, false),
        new HeavyShieldSequence("merchant", 7, 6, 8f, true),
        new HeavyShieldSequence("front", 13, 1, 8f, false),
    };
    private static bool IsSoldier(HeavyShieldAtlasId atlas) => atlas == HeavyShieldAtlasId.Soldier || atlas == HeavyShieldAtlasId.SoldierCoarse;
    private static bool IsShop(HeavyShieldAtlasId atlas) => atlas == HeavyShieldAtlasId.Shop || atlas == HeavyShieldAtlasId.ShopCoarse;
    private static bool IsLayers(HeavyShieldAtlasId atlas) => atlas == HeavyShieldAtlasId.ShopLayers || atlas == HeavyShieldAtlasId.GreekShopLayers
        || atlas == HeavyShieldAtlasId.ShopLayersCoarse || atlas == HeavyShieldAtlasId.GreekShopLayersCoarse;
    private static bool IsKnown(HeavyShieldAtlasId atlas) => IsSoldier(atlas) || IsShop(atlas) || IsLayers(atlas);
    internal static bool IsCoarse(HeavyShieldAtlasId atlas) => atlas == HeavyShieldAtlasId.SoldierCoarse || atlas == HeavyShieldAtlasId.ShopCoarse
        || atlas == HeavyShieldAtlasId.ShopLayersCoarse || atlas == HeavyShieldAtlasId.GreekShopLayersCoarse;
    private static HeavyShieldSequence[] Table(HeavyShieldAtlasId atlas) => IsSoldier(atlas) ? SoldierSequenceTable
        : IsShop(atlas) ? ShopSequenceTable : IsLayers(atlas) ? ShopLayerSequenceTable : null;
    internal static int FrameCount(HeavyShieldAtlasId atlas) => IsSoldier(atlas) ? SoldierFrameCount : IsShop(atlas) ? ShopFrameCount : IsLayers(atlas) ? ShopLayerFrameCount : 0;
    internal static int Columns(HeavyShieldAtlasId atlas) => IsSoldier(atlas) ? SoldierColumns : IsKnown(atlas) ? ShopColumns : 0;
    internal static int Rows(HeavyShieldAtlasId atlas) => IsSoldier(atlas) ? SoldierRows : IsKnown(atlas) ? ShopRows : 0;
    internal static int CellWidth(HeavyShieldAtlasId atlas) => IsSoldier(atlas) ? (IsCoarse(atlas) ? SoldierCoarseCellWidth : SoldierCellWidth)
        : IsKnown(atlas) ? (IsCoarse(atlas) ? ShopCoarseCellWidth : ShopCellWidth) : 0;
    internal static int CellHeight(HeavyShieldAtlasId atlas) => IsSoldier(atlas) ? (IsCoarse(atlas) ? SoldierCoarseCellHeight : SoldierCellHeight)
        : IsKnown(atlas) ? (IsCoarse(atlas) ? ShopCoarseCellHeight : ShopCellHeight) : 0;
    internal static int SheetWidth(HeavyShieldAtlasId atlas) => Columns(atlas) * CellWidth(atlas);
    internal static int SheetHeight(HeavyShieldAtlasId atlas) => Rows(atlas) * CellHeight(atlas);
    internal static float PivotX(HeavyShieldAtlasId atlas) => IsSoldier(atlas) ? (IsCoarse(atlas) ? SoldierCoarsePivotX : SoldierPivotX)
        : IsKnown(atlas) ? (IsCoarse(atlas) ? ShopCoarsePivotX : ShopPivotX) : 0f;
    internal static float PivotY(HeavyShieldAtlasId atlas) => IsSoldier(atlas) ? (IsCoarse(atlas) ? SoldierCoarsePivotY : SoldierPivotY)
        : IsKnown(atlas) ? (IsCoarse(atlas) ? ShopCoarsePivotY : ShopPivotY) : 0f;
    internal static float PixelsPerUnitFor(HeavyShieldAtlasId atlas) => IsSoldier(atlas) ? (IsCoarse(atlas) ? SoldierCoarsePixelsPerUnit : SoldierPixelsPerUnit)
        : IsKnown(atlas) ? (IsCoarse(atlas) ? ShopCoarsePixelsPerUnit : ShopPixelsPerUnit) : 0f;
    internal static string ResourceName(HeavyShieldAtlasId atlas) => atlas switch
    {
        HeavyShieldAtlasId.Soldier => SoldierResource,
        HeavyShieldAtlasId.SoldierCoarse => SoldierCoarseResource,
        HeavyShieldAtlasId.Shop => ShopResource,
        HeavyShieldAtlasId.ShopCoarse => ShopCoarseResource,
        HeavyShieldAtlasId.ShopLayers => ShopLayersResource,
        HeavyShieldAtlasId.ShopLayersCoarse => ShopLayersCoarseResource,
        HeavyShieldAtlasId.GreekShopLayers => GreekShopLayersResource,
        HeavyShieldAtlasId.GreekShopLayersCoarse => GreekShopLayersCoarseResource,
        _ => null,
    };
    /// <summary>序列数量（角色 77 / 商店 2 / 分层 4）；未知 id 返回 0。</summary>
    internal static int SequenceCount(HeavyShieldAtlasId atlas)
    {
        HeavyShieldSequence[] table = Table(atlas);
        return table != null ? table.Length : 0;
    }

    /// <summary>按 manifest 顺序取第 index 个序列；越界/未知 id 返回 false 且 sequence = default。</summary>
    internal static bool TryGetSequenceAt(HeavyShieldAtlasId atlas, int index, out HeavyShieldSequence sequence)
    {
        sequence = default;
        HeavyShieldSequence[] table = Table(atlas);
        if (table == null || index < 0 || index >= table.Length) return false;
        sequence = table[index];
        return true;
    }

    /// <summary>按 manifest 里的精确序列名（区分大小写）查找；未知名字/未知 id 返回 false。</summary>
    internal static bool TryGetSequence(HeavyShieldAtlasId atlas, string name, out HeavyShieldSequence sequence)
    {
        sequence = default;
        if (name == null) return false;
        HeavyShieldSequence[] table = Table(atlas);
        if (table == null) return false;
        for (int i = 0; i < table.Length; i++)
        {
            if (string.Equals(table[i].Name, name, StringComparison.Ordinal))
            {
                sequence = table[i];
                return true;
            }
        }
        return false;
    }

    /// <summary>序列名 + 序号（0..Count-1）→ 全局帧号；未知序列/越界返回 false 且 frame = -1。</summary>
    internal static bool TryGetSequenceFrame(HeavyShieldAtlasId atlas, string name, int offset, out int frame)
    {
        frame = -1;
        if (!TryGetSequence(atlas, name, out HeavyShieldSequence sequence)) return false;
        if (offset < 0 || offset >= sequence.Count) return false;
        frame = sequence.First + offset;
        return true;
    }

    /// <summary>帧号是否合法（0..FrameCount-1）。</summary>
    internal static bool IsValidFrame(HeavyShieldAtlasId atlas, int frame)
    {
        int count = FrameCount(atlas);
        return frame >= 0 && frame < count;
    }

    /// <summary>帧号 → 网格坐标（行优先：atlas 左上角起，行内自左向右、逐行向下）。越界返回 false。</summary>
    internal static bool FrameToCell(HeavyShieldAtlasId atlas, int frame, out int column, out int row)
    {
        column = 0;
        row = 0;
        if (!IsValidFrame(atlas, frame)) return false;
        int columns = Columns(atlas);
        if (columns <= 0) return false;
        column = frame % columns;
        row = frame / columns;
        return true;
    }

    /// <summary>该 id 的布局自检（序列表 + 帧数 + 槽位）；供加载器在解码前一次性调用。</summary>
    internal static bool Validate(HeavyShieldAtlasId atlas, out string error)
        => ValidateSequences(Table(atlas), FrameCount(atlas), Columns(atlas) * Rows(atlas), out error);

    /// <summary>
    /// 序列表自检（纯逻辑、一次性）：名称非空且唯一、First 从 0 起严格连续、合计 = frameCount、
    /// Count > 0、Fps 正且有限、frameCount ≤ 列×行（末尾空槽只作透明填充）。任一不满足 → false。
    /// </summary>
    internal static bool ValidateSequences(HeavyShieldSequence[] sequences, int frameCount, int slotCount, out string error)
    {
        error = null;
        if (sequences == null || sequences.Length == 0)
        {
            error = "sequence table empty";
            return false;
        }
        if (frameCount <= 0 || slotCount <= 0)
        {
            error = "frame/slot counts must be positive";
            return false;
        }
        if (frameCount > slotCount)
        {
            error = "frameCount " + frameCount + " exceeds atlas slots " + slotCount;
            return false;
        }
        int expected = 0;
        for (int i = 0; i < sequences.Length; i++)
        {
            HeavyShieldSequence current = sequences[i];
            if (string.IsNullOrEmpty(current.Name))
            {
                error = "sequence " + i + " has no name";
                return false;
            }
            if (current.Count <= 0)
            {
                error = "sequence " + current.Name + " is empty";
                return false;
            }
            if (current.First != expected)
            {
                error = "sequence " + current.Name + " first " + current.First + " does not continue at " + expected;
                return false;
            }
            for (int j = 0; j < i; j++)
            {
                if (string.Equals(sequences[j].Name, current.Name, StringComparison.Ordinal))
                {
                    error = "duplicate sequence name " + current.Name;
                    return false;
                }
            }
            if (!(current.Fps > 0f) || float.IsNaN(current.Fps) || float.IsInfinity(current.Fps))
            {
                error = "sequence " + current.Name + " has invalid fps";
                return false;
            }
            if (current.Count > frameCount - expected)
            {
                error = "sequence " + current.Name + " exceeds frameCount";
                return false;
            }
            expected += current.Count;
        }
        if (expected != frameCount)
        {
            error = "sequence frames " + expected + " != frameCount " + frameCount;
            return false;
        }
        return true;
    }
}

#if !HEAVY_SHIELD_ART_CORE_ONLY
/// <summary>
/// 重盾兵美术的只读加载器：静态、懒加载、失败整体 fail-closed（详见文件头）。
/// 主线程专用；不做任何锁（与项目其它视觉模块一致，全部调用都发生在 Unity 主线程）。
/// </summary>
internal static class HeavyShieldArt
{
    private enum AtlasState
    {
        Unknown = 0,
        Ready = 1,
        Unavailable = 2,
    }

    /// <summary>嵌入 PNG 的硬上限；超限直接拒绝，不改动原生外观。</summary>
    private const long MaxResourceBytes = 4L * 1024L * 1024L;

    private sealed class Atlas
    {
        internal readonly HeavyShieldAtlasId Id;
        internal AtlasState State;
        internal Texture2D Texture;
        internal Sprite[] Sprites;
        internal bool LoggedUnavailable;

        internal Atlas(HeavyShieldAtlasId id)
        {
            Id = id;
        }
    }

    private static readonly Atlas SoldierAtlas = new Atlas(HeavyShieldAtlasId.Soldier);
    private static readonly Atlas ShopAtlas = new Atlas(HeavyShieldAtlasId.Shop);
    private static readonly Atlas ShopLayersAtlas = new Atlas(HeavyShieldAtlasId.ShopLayers);
    private static readonly Atlas GreekShopLayersAtlas = new Atlas(HeavyShieldAtlasId.GreekShopLayers);
    private static readonly Atlas SoldierCoarseAtlas = new Atlas(HeavyShieldAtlasId.SoldierCoarse);
    private static readonly Atlas ShopCoarseAtlas = new Atlas(HeavyShieldAtlasId.ShopCoarse);
    private static readonly Atlas ShopLayersCoarseAtlas = new Atlas(HeavyShieldAtlasId.ShopLayersCoarse);
    private static readonly Atlas GreekShopLayersCoarseAtlas = new Atlas(HeavyShieldAtlasId.GreekShopLayersCoarse);
    private sealed class PaidShieldAsset
    {
        internal AtlasState State;
        internal Texture2D Texture;
        internal Sprite Sprite;
        internal bool LoggedUnavailable;
    }
    private static readonly PaidShieldAsset PaidShield = new PaidShieldAsset();
    private static readonly PaidShieldAsset PaidShieldCoarse = new PaidShieldAsset();

    /// <summary>The one collectible shield has its own PNG and bottom-center pivot.</summary>
    internal static bool TryGetPaidShieldSprite(out Sprite sprite)
        => TryGetPaidShieldSprite(HeavyShieldArtDensity.Detail, out sprite);

    internal static bool TryGetPaidShieldSprite(HeavyShieldArtDensity density, out Sprite sprite)
    {
        sprite = null;
        if (density != HeavyShieldArtDensity.Detail && density != HeavyShieldArtDensity.Coarse) return false;
        bool coarse = density == HeavyShieldArtDensity.Coarse;
        PaidShieldAsset asset = coarse ? PaidShieldCoarse : PaidShield;
        if (asset.State == AtlasState.Ready) { sprite = asset.Sprite; return sprite != null; }
        if (asset.State == AtlasState.Unavailable) return false;
        asset.State = AtlasState.Unavailable;
        Texture2D texture = null;
        Sprite created = null;
        try
        {
            string resource = coarse ? HeavyShieldArtLayout.PaidShieldCoarseResource : HeavyShieldArtLayout.PaidShieldResource;
            byte[] bytes;
            using (Stream stream = typeof(HeavyShieldArt).Assembly.GetManifestResourceStream(resource))
            {
                if (stream == null) throw new InvalidDataException("resource missing: " + resource);
                long length = stream.Length;
                if (length <= 0 || length > MaxResourceBytes) throw new InvalidDataException("resource size rejected");
                bytes = new byte[(int)length];
                int read = 0;
                while (read < bytes.Length)
                {
                    int step = stream.Read(bytes, read, bytes.Length - read);
                    if (step <= 0) break;
                    read += step;
                }
                if (read != bytes.Length) throw new InvalidDataException("resource truncated");
            }
            int width = coarse ? HeavyShieldArtLayout.PaidShieldCoarseWidth : HeavyShieldArtLayout.PaidShieldWidth;
            int height = coarse ? HeavyShieldArtLayout.PaidShieldCoarseHeight : HeavyShieldArtLayout.PaidShieldHeight;
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, bytes, false) || texture.width != width || texture.height != height)
                throw new InvalidDataException("paid shield decode/dimensions invalid");
            Color32[] pixels = texture.GetPixels32();
            if (pixels == null || pixels.Length != width * height) throw new InvalidDataException("paid shield pixel read failed");
            int opaque = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].a == 0) continue;
                if (pixels[i].a != 255) throw new InvalidDataException("paid shield non-binary alpha");
                opaque++;
            }
            if (opaque == 0 || opaque == pixels.Length) throw new InvalidDataException("paid shield alpha invalid");
            texture.filterMode = FilterMode.Point; texture.wrapMode = TextureWrapMode.Clamp; texture.anisoLevel = 0;
            created = Sprite.Create(texture, new Rect(0, 0, width, height),
                new Vector2(HeavyShieldArtLayout.PaidShieldPivotX, HeavyShieldArtLayout.PaidShieldPivotY),
                coarse ? HeavyShieldArtLayout.ShopCoarsePixelsPerUnit : HeavyShieldArtLayout.ShopPixelsPerUnit,
                0u, SpriteMeshType.FullRect);
            if (created == null) throw new InvalidDataException("paid shield Sprite.Create failed");
            asset.Texture = texture; asset.Sprite = created; asset.State = AtlasState.Ready;
            sprite = created; return true;
        }
        catch (Exception e)
        {
            DestroyQuietly(created); DestroyQuietly(texture);
            if (!asset.LoggedUnavailable)
            {
                asset.LoggedUnavailable = true;
                try { KingdomEnhancedPlugin.Instance?.LogSource?.LogWarning("[HeavyShieldArt] PaidShield " + density + ": " + e.Message); } catch { }
            }
            return false;
        }
    }

    /// <summary>All eight atlases are available without changing the default Detail entry points.</summary>
    internal static bool TryGetSprite(HeavyShieldAtlasId id, int frame, out Sprite sprite)
    {
        sprite = null;
        Atlas atlas = id switch
        {
            HeavyShieldAtlasId.Soldier => SoldierAtlas,
            HeavyShieldAtlasId.Shop => ShopAtlas,
            HeavyShieldAtlasId.ShopLayers => ShopLayersAtlas,
            HeavyShieldAtlasId.GreekShopLayers => GreekShopLayersAtlas,
            HeavyShieldAtlasId.SoldierCoarse => SoldierCoarseAtlas,
            HeavyShieldAtlasId.ShopCoarse => ShopCoarseAtlas,
            HeavyShieldAtlasId.ShopLayersCoarse => ShopLayersCoarseAtlas,
            HeavyShieldAtlasId.GreekShopLayersCoarse => GreekShopLayersCoarseAtlas,
            _ => null,
        };
        return atlas != null && TryGetSprite(atlas, frame, out sprite);
    }
    internal static bool TryGetSoldierSprite(int frame, HeavyShieldArtDensity density, out Sprite sprite)
    {
        sprite = null;
        return density == HeavyShieldArtDensity.Detail ? TryGetSprite(SoldierAtlas, frame, out sprite)
            : density == HeavyShieldArtDensity.Coarse && TryGetSprite(SoldierCoarseAtlas, frame, out sprite);
    }

    /// <summary>角色帧（0..508）→ Sprite；越界或资源不可用 → false 且 sprite = null（原生外观继续显示）。</summary>
    internal static bool TryGetSoldierSprite(int frame, out Sprite sprite)
        => TryGetSprite(SoldierAtlas, frame, out sprite);

    /// <summary>商店整图帧（0..12）→ Sprite；越界或资源不可用 → false 且 sprite = null。</summary>
    internal static bool TryGetShopSprite(int frame, out Sprite sprite)
        => TryGetSprite(ShopAtlas, frame, out sprite);

    /// <summary>商店分层帧（0..13）→ Sprite；越界或资源不可用 → false 且 sprite = null。</summary>
    internal static bool TryGetShopLayerSprite(int frame, out Sprite sprite)
        => TryGetSprite(ShopLayersAtlas, frame, out sprite);

    internal static bool TryGetGreekShopLayerSprite(int frame, out Sprite sprite)
        => TryGetSprite(GreekShopLayersAtlas, frame, out sprite);

    /// <summary>按图集取帧（常数时间数组索引；不做任何序列名解析）。</summary>
    private static bool TryGetSprite(Atlas atlas, int frame, out Sprite sprite)
    {
        sprite = null;
        if (!HeavyShieldArtLayout.IsValidFrame(atlas.Id, frame)) return false;
        if (!EnsureAtlas(atlas)) return false;
        Sprite[] sprites = atlas.Sprites;
        if (sprites == null || frame >= sprites.Length) return false;
        sprite = sprites[frame];
        return sprite != null;
    }

    /// <summary>惰性加载单张图集（只一次）；任何失败都一次性记录诊断并保持 Unavailable。</summary>
    private static bool EnsureAtlas(Atlas atlas)
    {
        if (atlas.State == AtlasState.Ready) return true;
        if (atlas.State == AtlasState.Unavailable) return false;
        atlas.State = AtlasState.Unavailable;   // 先置失败：任何异常/早退都保持原生外观
        try
        {
            if (!HeavyShieldArtLayout.Validate(atlas.Id, out string layoutError))
            {
                LogOnce(atlas, "layout invalid: " + layoutError);
                return false;
            }

            string resourceName = HeavyShieldArtLayout.ResourceName(atlas.Id);
            Assembly assembly = typeof(HeavyShieldArt).Assembly;
            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    LogOnce(atlas, "resource missing: " + resourceName);
                    return false;
                }
                long length = stream.Length;
                if (length <= 0 || length > MaxResourceBytes)
                {
                    LogOnce(atlas, "resource size rejected: " + length + " bytes");
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
                    LogOnce(atlas, "resource truncated: " + read + "/" + bytes.Length);
                    return false;
                }
                return DecodeAtlas(atlas, bytes);
            }
        }
        catch (Exception e)
        {
            LogOnce(atlas, "atlas load failed: " + e.GetType().Name);
            return false;
        }
    }

    /// <summary>解码 + 尺寸/像素校验 + 逐帧 Sprite.Create；任何一步失败整体回滚（销毁贴图、保持 Unavailable）。</summary>
    private static bool DecodeAtlas(Atlas atlas, byte[] bytes)
    {
        Texture2D texture = null;
        Sprite[] sprites = null;
        try
        {
            HeavyShieldAtlasId id = atlas.Id;
            int cellWidth = HeavyShieldArtLayout.CellWidth(id);
            int cellHeight = HeavyShieldArtLayout.CellHeight(id);
            int rows = HeavyShieldArtLayout.Rows(id);
            int frameCount = HeavyShieldArtLayout.FrameCount(id);
            int sheetWidth = HeavyShieldArtLayout.SheetWidth(id);
            int sheetHeight = HeavyShieldArtLayout.SheetHeight(id);

            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, bytes, false))
            {
                Fail(atlas, texture, "ImageConversion.LoadImage returned false");
                return false;
            }
            if (texture.width != sheetWidth || texture.height != sheetHeight)
            {
                Fail(atlas, texture, "sheet " + texture.width + "x" + texture.height
                    + " != " + sheetWidth + "x" + sheetHeight);
                return false;
            }

            Color32[] pixels = texture.GetPixels32();
            if (pixels == null || pixels.Length != sheetWidth * sheetHeight)
            {
                Fail(atlas, texture, "pixel read failed");
                return false;
            }
            int opaque = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                byte alpha = pixels[i].a;
                if (alpha == 0) continue;
                if (alpha != 255)
                {
                    Fail(atlas, texture, "non-binary alpha at pixel " + i);
                    return false;
                }
                opaque++;
            }
            if (opaque == 0)
            {
                Fail(atlas, texture, "sheet fully transparent");
                return false;
            }
            if (opaque == pixels.Length)
            {
                Fail(atlas, texture, "sheet fully opaque (no alpha channel)");
                return false;
            }

            int columns = HeavyShieldArtLayout.Columns(id);
            for (int slot = frameCount; slot < columns * rows; slot++)
            {
                int column = slot % columns, bottomRow = rows - 1 - slot / columns;
                for (int y = 0; y < cellHeight; y++)
                    for (int x = 0; x < cellWidth; x++)
                        if (pixels[(bottomRow * cellHeight + y) * sheetWidth + column * cellWidth + x].a != 0)
                        {
                            Fail(atlas, texture, "non-transparent padding at slot " + slot);
                            return false;
                        }
            }

            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.anisoLevel = 0;

            Vector2 pivot = new Vector2(HeavyShieldArtLayout.PivotX(id), HeavyShieldArtLayout.PivotY(id));
            sprites = new Sprite[frameCount];
            for (int frame = 0; frame < frameCount; frame++)
            {
                if (!HeavyShieldArtLayout.FrameToCell(id, frame, out int column, out int row))
                {
                    Fail(atlas, texture, "frame " + frame + " has no atlas cell", sprites);
                    return false;
                }
                // Unity 纹理原点在左下：atlas 行 0 在最上 → y 从底部倒算（bottom-left 行翻转）。
                float y = (rows - 1 - row) * cellHeight;
                Rect rect = new Rect(column * cellWidth, y, cellWidth, cellHeight);
                sprites[frame] = Sprite.Create(texture, rect, pivot, HeavyShieldArtLayout.PixelsPerUnitFor(id),
                    0u, SpriteMeshType.FullRect);
                if (sprites[frame] == null)
                {
                    Fail(atlas, texture, "Sprite.Create failed at frame " + frame, sprites);
                    return false;
                }
            }

            atlas.Texture = texture;
            atlas.Sprites = sprites;
            atlas.State = AtlasState.Ready;
            return true;
        }
        catch (Exception e)
        {
            Fail(atlas, texture, "atlas decode failed: " + e.GetType().Name, sprites);
            return false;
        }
    }

    private static void Fail(Atlas atlas, Texture2D texture, string message, Sprite[] sprites = null)
    {
        LogOnce(atlas, message);
        if (sprites != null)
            for (int i = 0; i < sprites.Length; i++) DestroyQuietly(sprites[i]);
        DestroyQuietly(texture);
    }

    /// <summary>每张图集最多一次告警（其余调用静默返回 false，避免逐帧刷日志）。</summary>
    private static void LogOnce(Atlas atlas, string message)
    {
        if (atlas.LoggedUnavailable) return;
        atlas.LoggedUnavailable = true;
        try
        {
            KingdomEnhancedPlugin.Instance?.LogSource?.LogWarning("[HeavyShieldArt] " + atlas.Id + ": " + message);
        }
        catch (Exception)
        {
        }
    }

    private static void DestroyQuietly(UnityEngine.Object target)
    {
        try
        {
            if (target != null) UnityEngine.Object.Destroy(target);
        }
        catch (Exception)
        {
        }
    }
}

/// <summary>Only an identity-proven paid ToolBow borrows its renderer/physics; native pool disable returns them.</summary>
internal sealed class HeavyShieldPaidBowVisual : MonoBehaviour
{
    private HeavyShieldCareerHandle _handle;
    private SpriteRenderer _body, _shield;
    private Rigidbody2D _rigidbody;
    private GameObject _child;
    private bool _bodyEnabled, _kinematic, _placed;
    private Vector2 _velocity;
    private float _angularVelocity;
    public HeavyShieldPaidBowVisual(IntPtr pointer) : base(pointer) { }

    [HideFromIl2Cpp] internal bool Place(DroppableTool bow, in HeavyShieldCareerHandle handle, Vector3 position)
    {
        if (_placed) return _handle == handle;
        if (bow == null || bow.gameObject == null || gameObject == null || bow.gameObject.Pointer != gameObject.Pointer
            || bow.gameObject.GetInstanceID() != handle.GoId || handle.Root != gameObject.Pointer
            || bow.pickedUp || bow.friendlyClaimer != null || bow.enemyClaimer != null
            || !HeavyShieldIdentity.TryGetPaidBow(bow, out var actual) || actual != handle
            || !HeavyShieldIdentity.ValidateCareer(in handle) || !HeavyShieldArt.TryGetPaidShieldSprite(out var sprite)) return false;
        try
        {
            _handle = handle; _body = bow.GetComponent<SpriteRenderer>();
            if (_body == null) return false;
            _bodyEnabled = _body.enabled; _rigidbody = bow._rigidbody;
            if (_rigidbody != null)
            {
                _kinematic = _rigidbody.isKinematic; _velocity = _rigidbody.velocity; _angularVelocity = _rigidbody.angularVelocity;
                _rigidbody.velocity = Vector2.zero; _rigidbody.angularVelocity = 0; _rigidbody.isKinematic = true;
            }
            _child = new GameObject("KEM_PaidShield"); _child.transform.SetParent(bow.transform, false);
            _child.transform.localPosition = new Vector3(0, 0, -.001f);
            _shield = _child.AddComponent<SpriteRenderer>(); _shield.sprite = sprite;
            _shield.flipX = handle.Side == HeavyShieldQuota.Side.Right;
            _shield.sortingLayerID = _body.sortingLayerID; _shield.sortingOrder = _body.sortingOrder;
            if (_body.sharedMaterial != null) _shield.sharedMaterial = _body.sharedMaterial;
            _body.enabled = false; bow.transform.position = position; _placed = true; return true;
        }
        catch { Restore(); return false; }
    }

    public void OnDisable() => Restore();
    [HideFromIl2Cpp] private void Restore()
    {
        try { if (_body != null && !_body.enabled) _body.enabled = _bodyEnabled; } catch { }
        try
        {
            if (_rigidbody != null && _rigidbody.isKinematic)
            {
                _rigidbody.isKinematic = _kinematic;
                if (_rigidbody.velocity == Vector2.zero) _rigidbody.velocity = _velocity;
                if (_rigidbody.angularVelocity == 0) _rigidbody.angularVelocity = _angularVelocity;
            }
        }
        catch { }
        try { if (_shield != null) _shield.enabled = false; if (_child != null) UnityEngine.Object.Destroy(_child); } catch { }
        _body = null; _shield = null; _rigidbody = null; _child = null; _handle = default; _placed = false;
    }
}
#endif
