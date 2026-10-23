using System;

namespace KingdomEnhancedMod;

/// <summary>
/// Known whole-island layouts. The private constructor and exact resolver make the immutable
/// singleton both the layout identity and its geometry; no live configuration is read here.
/// Legacy48 is an added court area, not a whole-island layout, and has no recipe in this class.
/// </summary>
internal sealed class PalaceLayoutRecipe
{
    internal static readonly PalaceLayoutRecipe Known44 = new(
        "palace-owned44-r1", 2, -22, 22, 11, -16f, -2.46875f, 1448, 645);
    internal static readonly PalaceLayoutRecipe Capital400 = new(
        "palace-capital400-r1", 3, -200, 200, 100, -194f, 0f, 12840, 6420);

    internal string LayoutId { get; }
    internal int LayoutVersion => 1;
    internal int MetadataSchema { get; }
    internal int Left { get; }
    internal int Right { get; }
    internal int Width => Right - Left;
    internal int TileWidth => 4;
    internal int TileCount { get; }
    internal float SpawnRootShiftX { get; }
    internal float GateWorldX { get; }
    internal float FreshStandX => GateWorldX - 2f;
    internal int MasterWidthPx { get; }
    internal int MasterHeightPx => 252;
    internal int AxisPx { get; }
    internal int BodyLeftPx => 20;
    internal int BodyRightPx => MasterWidthPx - 20;
    internal double UnitsPerPixel => 1d / 32d;
    internal float BeachLeftX => Left + 7.3125f;
    internal float BeachRightX => Right - 7.3125f;

    private PalaceLayoutRecipe(string layoutId, int metadataSchema, int left, int right,
        int tileCount, float spawnRootShiftX, float gateWorldX, int masterWidthPx, int axisPx)
    {
        LayoutId = layoutId;
        MetadataSchema = metadataSchema;
        Left = left;
        Right = right;
        TileCount = tileCount;
        SpawnRootShiftX = spawnRootShiftX;
        GateWorldX = gateWorldX;
        MasterWidthPx = masterWidthPx;
        AxisPx = axisPx;
    }

    internal float TileLeft(int index)
    {
        if (index < 0 || index >= TileCount)
            throw new ArgumentOutOfRangeException(nameof(index));
        return Left + TileWidth * index;
    }

    internal double WorldX(double pixelX)
    {
        if (!double.IsFinite(pixelX))
            throw new ArgumentOutOfRangeException(nameof(pixelX));
        double result = GateWorldX + (pixelX - AxisPx) / 32d;
        if (!double.IsFinite(result))
            throw new ArgumentOutOfRangeException(nameof(pixelX));
        return result;
    }

    internal static bool IsKnown(PalaceLayoutRecipe recipe)
        => ReferenceEquals(recipe, Known44) || ReferenceEquals(recipe, Capital400);

    internal static bool TryResolve(string id, int version, out PalaceLayoutRecipe recipe)
    {
        recipe = null;
        if (version != 1) return false;
        if (string.Equals(id, Known44.LayoutId, StringComparison.Ordinal)) recipe = Known44;
        else if (string.Equals(id, Capital400.LayoutId, StringComparison.Ordinal)) recipe = Capital400;
        return recipe != null;
    }
}
