using System;
using System.Linq;
using System.Reflection;
using KingdomEnhancedMod;

internal static class Program
{
    private static int _passed;
    private static void Check(bool ok, string label)
    { if (!ok) throw new Exception("FAIL " + label); _passed++; }
    private static void Throws(Action action, string label)
    { try { action(); } catch (ArgumentOutOfRangeException) { _passed++; return; } throw new Exception("FAIL " + label); }
    private static void Recipe(PalaceLayoutRecipe r, string id, int schema, int left, int count,
        float shift, float gate, int master, int axis)
    {
        Check(PalaceLayoutRecipe.IsKnown(r), id + " known");
        Check(r.LayoutId == id && r.LayoutVersion == 1 && r.MetadataSchema == schema, id + " identity");
        Check(r.Left == left && r.Right == -left && r.Width == -2 * left, id + " whole span");
        Check(r.TileCount == count && r.TileWidth == 4, id + " tile budget");
        for (int i = 0; i < count; i++) Check(r.TileLeft(i) == left + 4 * i, id + " tile" + i);
        Check(r.TileLeft(count - 1) + r.TileWidth == r.Right, id + " continuous to right");
        Check(r.SpawnRootShiftX == shift && r.GateWorldX == gate && r.FreshStandX == gate - 2, id + " anchors");
        Check(r.BeachLeftX == left + 7.3125f && r.BeachRightX == -left - 7.3125f, id + " shores");
        Check(r.MasterWidthPx == master && r.MasterHeightPx == 252 && r.AxisPx == axis, id + " master");
        Check(r.UnitsPerPixel == 1d / 32 && r.BodyLeftPx == 20 && r.BodyRightPx == master - 20, id + " pixels");
        Check(r.WorldX(20) == left && r.WorldX(axis) == gate && r.WorldX(master - 20) == -left, id + " mapping");
        Check(PalaceLayoutRecipe.TryResolve(id, 1, out var resolved) && ReferenceEquals(r, resolved), id + " singleton resolution");
        Throws(() => r.TileLeft(-1), id + " negative tile index");
        Throws(() => r.TileLeft(count), id + " excess tile index");
        Throws(() => r.WorldX(double.NaN), id + " NaN");
        Throws(() => r.WorldX(double.PositiveInfinity), id + " infinity");
        Throws(() => r.WorldX(double.NegativeInfinity), id + " negative infinity");
    }
    private static int Main()
    {
        Recipe(PalaceLayoutRecipe.Known44, "palace-owned44-r1", 2, -22, 11, -16, -2.46875f, 1448, 645);
        Recipe(PalaceLayoutRecipe.Capital400, "palace-capital400-r1", 3, -200, 100, -194, 0, 12840, 6420);
        Check(!PalaceLayoutRecipe.IsKnown(null), "null is unknown");
        foreach (var id in new[] { null, "", "palace-capital400-r1 ", "PALACE-CAPITAL400-R1", "palace-owned48-r1" })
            Check(!PalaceLayoutRecipe.TryResolve(id, 1, out var r) && r == null, "unknown ordinal identity");
        foreach (int v in new[] { int.MinValue, -1, 0, 2, int.MaxValue })
            Check(!PalaceLayoutRecipe.TryResolve("palace-capital400-r1", v, out var r) && r == null, "unknown version");
        var t = typeof(PalaceLayoutRecipe);
        Check(t.IsSealed && t.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .All(c => c.IsPrivate), "private constructor only");
        Check(t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .All(f => f.IsInitOnly), "instance fields readonly");
        Check(t.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .All(p => p.SetMethod == null), "properties have no setters");
        Console.WriteLine("PASS PalaceLayoutRecipe production source checks=" + _passed);
        return 0;
    }
}
