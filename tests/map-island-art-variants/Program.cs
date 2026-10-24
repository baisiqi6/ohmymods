using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using KingdomEnhancedMod;
using ShoreArtTests;
using UnityEngine;

internal static class Program
{
    private const string AResource = "KingdomEnhancedMod.KEM_MapExtensionIsland.png";
    private const string BResource = "KingdomEnhancedMod.KEM_MapExtensionIslandCrescent.png";
    private static int _checks, _failures;

    private static void Check(bool condition, string name)
    {
        _checks++;
        if (!condition) { _failures++; Console.WriteLine("FAIL " + name); }
    }

    private static string Sha(byte[] values)
        => Convert.ToHexString(SHA256.HashData(values)).ToLowerInvariant();

    private static byte[] Read(string resource)
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
        if (stream == null) throw new InvalidOperationException("fixture missing: " + resource);
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }

    private static byte[] Rgba(Color32[] values)
    {
        var bytes = new byte[values.Length * 4];
        for (int i = 0; i < values.Length; i++)
        {
            bytes[i * 4] = values[i].r; bytes[i * 4 + 1] = values[i].g;
            bytes[i * 4 + 2] = values[i].b; bytes[i * 4 + 3] = values[i].a;
        }
        return bytes;
    }

    private static byte[] Alpha(Color32[] values)
    {
        var bytes = new byte[values.Length];
        for (int i = 0; i < values.Length; i++) bytes[i] = values[i].a;
        return bytes;
    }

    private static byte[] MaskBytes(bool[] values)
    {
        var bytes = new byte[values.Length];
        for (int i = 0; i < values.Length; i++) bytes[i] = values[i] ? (byte)1 : (byte)0;
        return bytes;
    }

    private static Color32[] FlipVertical(Color32[] bottomUp, int width, int height)
    {
        var topDown = new Color32[bottomUp.Length];
        for (int y = 0; y < height; y++)
            Array.Copy(bottomUp, y * width, topDown, (height - 1 - y) * width, width);
        return topDown;
    }

    private static bool SamePixels(Color32[] left, Color32[] right)
        => left != null && right != null && Rgba(left).SequenceEqual(Rgba(right));

    // Reviewed palette contract after remapping the source RGB: primary 227 plateau, 189 cliff,
    // 245 shallow shore and 255 highlight must remain; the two dark detail tones must be gone.
    private static bool ToneContract(Color32[] shore, out int primary)
    {
        var counts = new SortedDictionary<byte, int>();
        foreach (Color32 pixel in shore)
        {
            if (pixel.a == 0) continue;
            counts.TryGetValue(pixel.r, out int count);
            counts[pixel.r] = count + 1;
        }
        primary = 0;
        int best = -1;
        foreach (KeyValuePair<byte, int> pair in counts)
            if (pair.Value > best) { best = pair.Value; primary = pair.Key; }
        return primary == 227 && !counts.ContainsKey(116) && !counts.ContainsKey(155) &&
            counts.ContainsKey(189) && counts.ContainsKey(227) &&
            counts.ContainsKey(245) && counts.ContainsKey(255);
    }

    private static bool SameFloat(float left, float right)
        => BitConverter.SingleToInt32Bits(left) == BitConverter.SingleToInt32Bits(right);

    private static void SameMetadata(MapExtensionIslandArt.ShorePrep left,
        MapExtensionIslandArt.ShorePrep right, string name)
    {
        Check(left.SrcWidth == right.SrcWidth && left.SrcHeight == right.SrcHeight &&
            left.BboxX == right.BboxX && left.BboxY == right.BboxY &&
            left.BboxWidth == right.BboxWidth && left.BboxHeight == right.BboxHeight &&
            SameFloat(left.Aspect, right.Aspect) && left.IslandPixels == right.IslandPixels &&
            left.ComponentCount == right.ComponentCount && left.GrayMin == right.GrayMin &&
            left.GrayMax == right.GrayMax && left.LevelsUsed == right.LevelsUsed, name);
    }

    private static void TestA(Color32[] raw, int width, int height)
    {
        byte[] before = Rgba(raw);
        var expected = MapExtensionIslandArt.BuildPrep(raw, width, height, 224);
        var actual = MapExtensionIslandArtVariant.Prepare(11, raw, width, height);
        Check(actual != null, "A has prepared result");
        if (actual == null) return;
        Check(actual.Width == 228 && actual.Height == 84, "A strict unchanged frame");
        SameMetadata(expected, actual, "A source diagnostic metadata unchanged");
        int primary;
        Check(ToneContract(actual.Shore, out primary),
            "A prepared palette primary 227 without 116/155 primary=" + primary);
        Check(SamePixels(expected.Shore, actual.Shore), "A shore byte exact legacy BuildPrep224");
        Check(SamePixels(expected.Outline, actual.Outline), "A outline byte exact legacy");
        Check(expected.Mask.SequenceEqual(actual.Mask), "A alpha mask unchanged");
        Check(expected.PlacementMask.SequenceEqual(actual.PlacementMask) &&
            expected.PlacementPixels == actual.PlacementPixels, "A placement mask unchanged");
        // Fixed geometry goldens from the reviewed source: the palette art worker may change RGB,
        // never alpha/outline/PlacementMask. Compare prepared streams, not only Prepare-vs-BuildPrep.
        Check(Sha(Alpha(actual.Shore)) == "011d2cc782c236742b615e43c7f9c5f47d24ffb4990c397861c061b729ff68ff",
            "A prepared alpha matches baseline golden");
        Check(Sha(Rgba(actual.Outline)) == "14874916f960a9b86401b34d67237c54a8bc7756b18fafe60e915fb164d6f018" &&
            Sha(Alpha(actual.Outline)) == "103e8c439a1f734d6b26ea94a2cf28adbae0f5d4b64d6ff1c7466871d66ed368",
            "A outline matches baseline golden");
        Check(Sha(MaskBytes(actual.Mask)) == "3429318ae2fe419966af1f52b6528ec7337ee65b03bced2794cda79d1f7fb055",
            "A shore mask matches baseline golden");
        Check(Sha(MaskBytes(actual.PlacementMask)) == "ec1389ae7c847a3d9961bf26de5340ba21f6a5c9aefd90823128dab109c51f29",
            "A placement mask matches baseline golden");
        Check(before.SequenceEqual(Rgba(raw)), "A source input never written");
    }

    private static void TestB(Color32[] raw, int width, int height)
    {
        byte[] before = Rgba(raw);
        var original224 = MapExtensionIslandArt.BuildPrep(raw, width, height, 224);
        Check(original224.Width == 228 && original224.Height == 83, "B224 is discarded intermediate");
        var small = MapExtensionIslandArt.BuildPrep(raw, width, height, 222);
        Check(small.Width == 226 && small.Height == 82, "B222 reference canvas");
        var actual = MapExtensionIslandArtVariant.Prepare(13, raw, width, height);
        Check(actual != null, "B has prepared result");
        if (actual == null) return;
        Check(actual.Width == 228 && actual.Height == 84, "B final strict frame");
        SameMetadata(small, actual, "B source diagnostic metadata survives letterbox");
        bool pixels = true, mask = true, border = true, outline = true, tones = true;
        for (int y = 0; y < 84; y++) for (int x = 0; x < 228; x++)
        {
            int i = y * 228 + x;
            bool inner = x >= 1 && x <= 226 && y >= 1 && y <= 82;
            if (inner)
            {
                int source = (y - 1) * 226 + x - 1;
                Color32 a = small.Shore[source], b = actual.Shore[i];
                pixels &= a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
                a = small.Outline[source]; b = actual.Outline[i];
                pixels &= a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
                mask &= small.Mask[source] == actual.Mask[i];
            }
            else border &= actual.Shore[i].a == 0 && actual.Outline[i].a == 0 && !actual.Mask[i];
            Color32 p = actual.Shore[i], ring = actual.Outline[i];
            tones &= p.a == 0 || p.a == 255 && p.r == p.g && p.g == p.b &&
                MapExtensionIslandArt.TonePalette.Contains(p.r);
            outline &= ring.a == 0 || ring.a == 255 && ring.r == 255 && ring.g == 255 &&
                ring.b == 255 && !actual.Mask[i];
        }
        Check(pixels && mask && border, "B shore outline and mask exact +1/+1 transparent letterbox");
        Check(tones && outline, "B palette tones binary alpha and white external outline");
        int primary;
        Check(ToneContract(actual.Shore, out primary),
            "B prepared palette primary 227 without 116/155 primary=" + primary);
        Check(actual.Mask.Count(value => value) == 10611 && actual.IslandPixels == 10611, "B alpha pixel count");
        // Geometry goldens stay fixed when the art worker repaints RGB. The B RGBA hash below is the
        // current reviewed artwork and is updated only after the Operator adopts a final candidate.
        Check(Sha(Alpha(actual.Shore)) == "70b5e1c557f51360cb07b3c849b3269be83351c28e1803b079f9241ec4c26c1a",
            "B prepared alpha matches baseline golden");
        Check(Sha(Rgba(actual.Outline)) == "6b50512a40d94c950fcc7256b8c9c3bb7d6696fdc7688ca9ea80255bc52382f6" &&
            Sha(Alpha(actual.Outline)) == "3d0bfb869b1bd307526900ce1dbc9e65b04fbbeaf75c7ae978c32ec3a34406d2",
            "B outline matches baseline golden");
        Check(Sha(MaskBytes(actual.Mask)) == "951e159372665ba4fe414b2c9764c8bd116c9269de87c7f59296204f44198b00",
            "B shore mask matches baseline golden");
        Check(Sha(Rgba(actual.Shore)) == "0a2d3bb6c1f1617e2c63c8171466a90e2305f1fc345bb23552f254ce0048aeed",
            "B RGBA matches reviewed palette-remapped preparation");
        Check(actual.PlacementPixels == 5652 && actual.PlacementMask.Count(value => value) == 5652,
            "B independently authored top mask pixel count");
        Check(Sha(actual.PlacementMask.Select(value => value ? (byte)1 : (byte)0).ToArray()) ==
            "e57bbbfc108c5433550aaa610379412a29be363c602b72dd9c18311026625739",
            "B mask exact independent review golden bottom-up bool bytes");
        bool inside = true, west = true;
        for (int y = 0; y < 84; y++) for (int x = 0; x < 228; x++)
            if (actual.PlacementMask[y * 228 + x])
            {
                inside &= actual.Mask[y * 228 + x] && actual.Outline[y * 228 + x].a == 0;
                west &= !(x >= 29 && x <= 34 && 83 - y >= 28 && 83 - y <= 43);
            }
        Check(inside && west, "B top mask excludes sea outline and west vertical cliff");
        Check(before.SequenceEqual(Rgba(raw)), "B source input never written");
        var again = MapExtensionIslandArtVariant.Prepare(13, raw, width, height);
        actual.Mask[0] = true; actual.PlacementMask[0] = true;
        Check(!again.Mask[0] && !again.PlacementMask[0], "B preparation arrays are not shared mutable state");
    }

    private static void TestAnchors()
    {
        Check(MapExtensionIslandArtVariant.AnchorsFor(11) == null, "A uses exact legacy null profile");
        Check(MapExtensionIslandArtVariant.AnchorsFor(-1) == null &&
            MapExtensionIslandArtVariant.AnchorsFor(12) == null, "unknown returns no profile");
        MapIconAnchorSet profile = MapExtensionIslandArtVariant.AnchorsFor(13);
        Check(profile != null && profile.IsValid, "B immutable anchor profile valid");
        if (profile == null) return;
        Check(ReferenceEquals(profile, MapExtensionIslandArtVariant.AnchorsFor(13)), "B profile reused immutable");
        float[] bx = { .30701753f, .5701754f, .80263156f, .39473686f, .65789473f, .21491228f, .80263156f, .48245615f };
        float[] by = { .5833333f, .6547619f, .5119048f, .78571427f, .48809522f, .4642857f, .3690476f, .4642857f };
        for (int slot = 0; slot < 18; slot++)
        {
            Check(profile.TryGet(slot, 0, out float x, out float y) &&
                SameFloat(x, slot < 8 ? bx[slot] : MapExtensionIslandLayout.PreferredAnchorX[slot]) &&
                SameFloat(y, slot < 8 ? by[slot] : MapExtensionIslandLayout.PreferredAnchorY[slot]),
                "B exact mixed primary slot " + slot);
            bool disabled = float.IsNaN(MapExtensionIslandLayout.FallbackAnchorX[slot]) ||
                float.IsNaN(MapExtensionIslandLayout.FallbackAnchorY[slot]);
            bool found = profile.TryGet(slot, 1, out x, out y);
            Check(disabled ? !found : found && SameFloat(x, MapExtensionIslandLayout.FallbackAnchorX[slot]) &&
                SameFloat(y, MapExtensionIslandLayout.FallbackAnchorY[slot]), "B exact secondary/NaN semantics " + slot);
        }
        Check(!profile.TryGet(18, 0, out _, out _) && !profile.TryGet(-1, 0, out _, out _), "B range is 18, not8");
        float oldX = MapExtensionIslandLayout.PreferredAnchorX[8];
        float oldFX = MapExtensionIslandLayout.FallbackAnchorX[0];
        try
        {
            MapExtensionIslandLayout.PreferredAnchorX[8] = .99f;
            MapExtensionIslandLayout.FallbackAnchorX[0] = .99f;
            Check(profile.TryGet(8, 0, out float x, out _) && SameFloat(x, oldX) &&
                profile.TryGet(0, 1, out x, out _) && SameFloat(x, oldFX),
                "B cloned data cannot be changed through legacy source arrays");
        }
        finally
        {
            MapExtensionIslandLayout.PreferredAnchorX[8] = oldX;
            MapExtensionIslandLayout.FallbackAnchorX[0] = oldFX;
        }
    }

    private static void TestCachesAndSharedLeases()
    {
        MapExtensionIslandArt.Reset();
        int created = UnityEngine.Object.CreateCount;
        Check(!MapExtensionIslandArt.TryEnsure(12) && MapExtensionIslandArt.ShoreFor(12) == null &&
            MapExtensionIslandArt.OutlineFor(12) == null && MapExtensionIslandArt.PrepFor(12) == null &&
            MapExtensionIslandArt.ReadResourceBytes(12) == null && UnityEngine.Object.CreateCount == created,
            "unknown service land rejects without cache or Unity allocation");
        Check(MapExtensionIslandArt.TryEnsure(13) && !MapExtensionIslandArt.Ready &&
            MapExtensionIslandArt.Shore == null && MapExtensionIslandArt.Prep == null,
            "cold B decodes its own resource without touching A");
        var bShore = MapExtensionIslandArt.ShoreFor(13);
        var bOutline = MapExtensionIslandArt.OutlineFor(13);
        var bPrep = MapExtensionIslandArt.PrepFor(13);
        Check(bShore != null && bOutline != null && bShore.texture.width == 228 &&
            bShore.texture.height == 84 && bPrep.PlacementPixels == 5652, "B cache has strict frame and own mask");
        Check(MapExtensionIslandArt.TryEnsure() && MapExtensionIslandArt.Ready, "legacy ensure alias still loads A");
        var aShore = MapExtensionIslandArt.Shore;
        var aOutline = MapExtensionIslandArt.Outline;
        var aPrep = MapExtensionIslandArt.Prep;
        Check(aShore != bShore && aOutline != bOutline && !ReferenceEquals(aPrep, bPrep) &&
            ReferenceEquals(MapExtensionIslandArt.PrepFor(13), bPrep), "A and B distinct pairs and prep identities");
        var nativeA = new Sprite(); var nativeB = new Sprite();
        var imageA = new UnityEngine.UI.Image { sprite = nativeA };
        var imageB = new UnityEngine.UI.Image { sprite = nativeB };
        object ownerA = new object(), ownerB = new object();
        Check(MapExtensionIslandArt.TryBorrow(imageA, aShore, ownerA) != null &&
            MapExtensionIslandArt.TryBorrow(imageB, bShore, ownerB) != null &&
            MapExtensionIslandArt.LeaseCount == 2, "both caches use the shared image lease registry");
        Check(!MapExtensionIslandArt.DestroyCacheIfUnused() && MapExtensionIslandArt.Shore != null &&
            MapExtensionIslandArt.ShoreFor(13) != null, "A/B leases prevent cache destruction");
        MapExtensionIslandArt.ReleaseOwner(ownerA);
        Check(imageA.sprite == nativeA && imageB.sprite == bShore &&
            !MapExtensionIslandArt.DestroyCacheIfUnused() && MapExtensionIslandArt.Shore != null,
            "A released but live B lease still protects both pairs");
        MapExtensionIslandArt.ReleaseOwner(ownerB);
        Check(imageB.sprite == nativeB && MapExtensionIslandArt.DestroyCacheIfUnused() &&
            MapExtensionIslandArt.Shore == null && MapExtensionIslandArt.ShoreFor(13) == null &&
            MapExtensionIslandArt.Prep == null && MapExtensionIslandArt.PrepFor(13) == null,
            "last release allows both cache pairs and prep identities to clear");
        Check(MapExtensionIslandArt.TryEnsure(13) && MapExtensionIslandArt.TryEnsure(11) &&
            !ReferenceEquals(MapExtensionIslandArt.Prep, aPrep) &&
            !ReferenceEquals(MapExtensionIslandArt.PrepFor(13), bPrep), "destroy/rebuild generates fresh per-land prep keys");

        // A failed B restore must retain both pairs while its shared lease is outstanding.
        aPrep = MapExtensionIslandArt.Prep; bPrep = MapExtensionIslandArt.PrepFor(13);
        imageB = new UnityEngine.UI.Image { sprite = nativeB };
        MapExtensionIslandArt.TryBorrow(imageB, MapExtensionIslandArt.ShoreFor(13), ownerB);
        imageB.ThrowOnSet = true;
        int destroyed = UnityEngine.Object.DestroyCount;
        MapExtensionIslandArt.Reset();
        Check(MapExtensionIslandArt.PendingRestores > 0 && UnityEngine.Object.DestroyCount == destroyed &&
            ReferenceEquals(MapExtensionIslandArt.Prep, aPrep) &&
            ReferenceEquals(MapExtensionIslandArt.PrepFor(13), bPrep), "B before-write setter throw keeps restore responsibility and both pairs");
        imageB.ThrowOnSet = false;
        MapExtensionIslandArt.Reset();
        Check(imageB.sprite == nativeB && MapExtensionIslandArt.PendingRestores == 0 &&
            MapExtensionIslandArt.LeaseCount == 0 && MapExtensionIslandArt.Prep == null &&
            MapExtensionIslandArt.PrepFor(13) == null, "B before-write failure recovery clears both pairs after restore");

        MapExtensionIslandArt.TryEnsure(11); MapExtensionIslandArt.TryEnsure(13);
        aPrep = MapExtensionIslandArt.Prep; bPrep = MapExtensionIslandArt.PrepFor(13);
        imageB = new UnityEngine.UI.Image { sprite = nativeB };
        MapExtensionIslandArt.TryBorrow(imageB, MapExtensionIslandArt.ShoreFor(13), ownerB);
        imageB.WriteThenThrowSet = true; imageB.ReadFaultAfterWriteThrow = true;
        destroyed = UnityEngine.Object.DestroyCount;
        MapExtensionIslandArt.Reset();
        Check(imageB._sprite == nativeB && MapExtensionIslandArt.PendingRestores > 0 &&
            UnityEngine.Object.DestroyCount == destroyed && ReferenceEquals(MapExtensionIslandArt.Prep, aPrep) &&
            ReferenceEquals(MapExtensionIslandArt.PrepFor(13), bPrep), "B write-then-throw with unknown readback retains both pairs");
        imageB.WriteThenThrowSet = false; imageB.ReadFaultAfterWriteThrow = false; imageB.ThrowOnGet = false;
        MapExtensionIslandArt.Reset();
        Check(imageB.sprite == nativeB && MapExtensionIslandArt.LeaseCount == 0 &&
            MapExtensionIslandArt.PendingRestores == 0 && MapExtensionIslandArt.Prep == null &&
            MapExtensionIslandArt.PrepFor(13) == null, "B write-then-throw recovery finalizes before cache destruction");

        // Existing fake hooks cover B decode and first-Sprite failures without changing shared stubs.
        MapExtensionIslandArt.TryEnsure(11); aPrep = MapExtensionIslandArt.Prep;
        int alive = UnityEngine.Object.AliveCount;
        ImageConversion.FailNext = true;
        Check(!MapExtensionIslandArt.TryEnsure(13) && ReferenceEquals(MapExtensionIslandArt.Prep, aPrep) &&
            MapExtensionIslandArt.PrepFor(13) == null && UnityEngine.Object.AliveCount == alive,
            "B decode failure cleans temporaries and cannot substitute A cache");
        created = UnityEngine.Object.CreateCount;
        Check(!MapExtensionIslandArt.TryEnsure(13) && UnityEngine.Object.CreateCount == created,
            "B failed attempt does not retry on every ensure");
        MapExtensionIslandArt.Reset();
        MapExtensionIslandArt.TryEnsure(11); aPrep = MapExtensionIslandArt.Prep;
        alive = UnityEngine.Object.AliveCount;
        Sprite.FailNextCreate = true;
        Check(!MapExtensionIslandArt.TryEnsure(13) && ReferenceEquals(MapExtensionIslandArt.Prep, aPrep) &&
            MapExtensionIslandArt.ShoreFor(13) == null && MapExtensionIslandArt.OutlineFor(13) == null &&
            UnityEngine.Object.AliveCount == alive, "B Sprite failure cleans its partial pair and preserves A");
        MapExtensionIslandArt.Reset();
        Check(MapExtensionIslandArt.TryEnsure(13), "B retry is available only after explicit cache reset");
        MapExtensionIslandArt.Reset();
    }

    private static void TestInvalid()
    {
        Check(MapExtensionIslandArtVariant.Prepare(12, new Color32[1], 1, 1) == null, "unknown prepare rejected");
        Check(MapExtensionIslandArtVariant.Prepare(11, null, 1, 1) == null, "null pixels rejected");
        Check(MapExtensionIslandArtVariant.Prepare(13, new Color32[1], 0, 1) == null &&
            MapExtensionIslandArtVariant.Prepare(13, new Color32[1], -1, 1) == null, "nonpositive dimensions rejected");
        Check(MapExtensionIslandArtVariant.Prepare(11, new Color32[1], int.MaxValue, 2) == null &&
            MapExtensionIslandArtVariant.Prepare(13, new Color32[1], 65536, 65536) == null,
            "long dimensions guard rejects int multiplication overflow before BuildPrep");
        Check(MapExtensionIslandArtVariant.Prepare(13, new Color32[6], 2, 4) == null,
            "source length mismatch rejected");
        var oversized = new Color32[3500 * 1250];
        Array.Fill(oversized, new Color32(227, 227, 227, 255));
        Check(MapExtensionIslandArtVariant.Prepare(11, oversized, 3500, 1250) == null,
            "opaque >4M A-shaped input rejected before allocating legacy prep scratch arrays");
        var opaque = Enumerable.Repeat(new Color32(227, 227, 227, 255), 64).ToArray();
        Check(MapExtensionIslandArtVariant.Prepare(11, opaque, 8, 8) == null &&
            MapExtensionIslandArtVariant.Prepare(13, opaque, 8, 8) == null, "unexpected prepared dimensions rejected");
        Check(MapExtensionIslandArtVariant.Prepare(13, new Color32[64], 8, 8) == null,
            "empty alpha rejected");
    }

    private static int Main()
    {
        Check(MapExtensionIslandArtVariant.IsSupported(11), "physical11 supported");
        if (_failures > 0) return 1;
        Check(MapExtensionIslandArtVariant.IsSupported(13) && !MapExtensionIslandArtVariant.IsSupported(10) &&
            !MapExtensionIslandArtVariant.IsSupported(12) && !MapExtensionIslandArtVariant.IsSupported(-1),
            "support only physical11/13");
        Check(MapExtensionIslandArtVariant.ResourceNameFor(11) == AResource &&
            MapExtensionIslandArtVariant.ResourceNameFor(13) == BResource &&
            MapExtensionIslandArtVariant.ResourceNameFor(12) == null, "exact per-land resource names");
        byte[] a = Read(AResource), b = Read(BResource);
        Check(Sha(a) == "44c7463fed7e6cb222dfae8c701e492f000b26a21b7d37541fdc76fa994b1479" &&
            Sha(b) == "a056d1a3ff81ea13c5d9e065abc71d734e45226df2829184e1bd183606a79357", "reviewed raw asset bytes embedded");
        Png.DecodeBottomUp(a, out int aw, out int ah, out Color32[] ap);
        Png.DecodeBottomUp(b, out int bw, out int bh, out Color32[] bp);
        Check(Sha(Alpha(FlipVertical(ap, aw, ah))) ==
            "b2a5493130a20384b3f41af23b8c02eb24d47e616bce83dcc1b29e6d38fda149",
            "A decoded source alpha matches reviewed baseline stream");
        Check(Sha(Alpha(FlipVertical(bp, bw, bh))) ==
            "281f1cf55624a14bb5a7697f652579a6b1b8e0940226080b30f97d37f195f191",
            "B decoded source alpha matches reviewed baseline stream");
        TestA(ap, aw, ah); TestB(bp, bw, bh); TestAnchors(); TestInvalid(); TestCachesAndSharedLeases();
        Console.WriteLine("Map art variant checks=" + _checks + " failures=" + _failures);
        return _failures == 0 ? 0 : 1;
    }
}
