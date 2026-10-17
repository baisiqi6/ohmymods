using KingdomEnhancedMod;

// Pure world/slot plan regression for Issue #255.
// Compile twice: default (PC table) and -p:DefineConstants=ANDROID (Greek-only table).
internal static class Program
{
    private static int _pass;
    private static int _fail;

    private static void Check(bool condition, string label)
    {
        if (condition) { _pass++; Console.WriteLine("PASS " + label); }
        else { _fail++; Console.WriteLine("FAIL " + label); }
    }

    private static int Main()
    {
#if ANDROID
        Console.WriteLine("mode: ANDROID (Greek-only preservation)");
#else
        Console.WriteLine("mode: PC (world shop extension)");
#endif

        Check(WorldShopPlan.EuropeBiomeIndex == 0, "Europe index 0");
        Check(WorldShopPlan.ShogunBiomeIndex == 1, "Shogun index 1");
        Check(WorldShopPlan.DeadLandsBiomeIndex == 2, "DeadLands index 2");
        Check(WorldShopPlan.NorseBiomeIndex == 3, "Norse index 3");

        var greece = WorldShopPlan.Get(5, 5);
        Check(greece.Ninja && greece.Berserker && greece.Greece && greece.Any,
            "Greek keeps existing ninja + berserker adaptation");

#if ANDROID
        foreach (var biome in new[] { 0, 1, 2, 3, 4 })
        {
            var p = WorldShopPlan.Get(biome, 5);
            Check(!p.Any && !p.Greece, "Android: biome " + biome + " stays unexpanded");
        }
#else
        var europe = WorldShopPlan.Get(0, 5);
        Check(europe.Ninja && europe.Berserker && !europe.Greece, "Europe: ninja 7/8 + berserker 12/13");

        var shogun = WorldShopPlan.Get(1, 5);
        Check(!shogun.Ninja && shogun.Berserker, "Shogun: berserker only (native ninja kept)");

        var dead = WorldShopPlan.Get(2, 5);
        Check(dead.Ninja && dead.Berserker, "DeadLands: ninja + berserker");

        var norse = WorldShopPlan.Get(3, 5);
        Check(norse.Ninja && !norse.Berserker, "Norse: ninja only (native berserker kept)");

        foreach (var biome in new[] { 4, 6, 7 })
        {
            var p = WorldShopPlan.Get(biome, 5);
            Check(!p.Any, "unknown biome " + biome + " stays unexpanded");
        }

        // Greece index is supplied by BiomeHolder, not hard-coded here.
        var shifted = WorldShopPlan.Get(7, 7);
        Check(shifted.Ninja && shifted.Berserker && shifted.Greece,
            "Greek detection follows supplied GreeceBiomeIndex");
#endif

        Console.WriteLine($"RESULT: {_pass} passed, {_fail} failed");
        return _fail == 0 ? 0 : 1;
    }
}
