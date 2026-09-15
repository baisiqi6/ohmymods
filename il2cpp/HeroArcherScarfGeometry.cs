using System;

namespace KingdomEnhancedMod;

/// <summary>
/// Pure pixel ribbon geometry. Each segment has two independent flat-color quads.
/// Cardinal cross sections keep each face at least one source pixel in Manhattan width; unlike a
/// rounded unit normal, they cannot round a narrow fold into a line. Segment-local
/// cross sections also avoid inverted miters at the chain's existing sharp folds.
/// No simulation mutation, allocation, Unity dependency, or per-frame palette changes.
/// </summary>
internal static class HeroArcherScarfGeometry
{
    internal const int FacesPerSegment = 2;
    internal const int VerticesPerSegment = 8;
    internal const int VertexCount = (HeroArcherClothChain.NodeCount - 1) * VerticesPerSegment;

    internal readonly struct Point
    {
        internal readonly float X, Y;
        internal Point(float x, float y) { X = x / 32f; Y = y / 32f; }
    }

    internal readonly struct Segment
    {
        internal readonly Point StartOuter, StartFold, StartInner, EndOuter, EndFold, EndInner;
        internal Segment(Point a, Point b, Point c, Point d, Point e, Point f)
        { StartOuter = a; StartFold = b; StartInner = c; EndOuter = d; EndFold = e; EndInner = f; }
    }

    // The optional bridge is only for the upper-mounted secondary attachment. It
    // lowers drawn nodes 0/1 by 2/1px, without changing the simulated chain or the
    // general (default) integer-ground geometry contract. Those two pinned-length
    // nodes remain far above the secondary's -17px floor in the actual view.
    internal static bool TrySegment(float[] x, float[] y, int segment, int ribbon, float clock, out Segment result,
        bool attachmentBridge = false)
    {
        result = default;
        if (x == null || y == null || segment < 0 || segment >= HeroArcherClothChain.NodeCount - 1
            || x.Length < HeroArcherClothChain.NodeCount || y.Length < HeroArcherClothChain.NodeCount
            || (ribbon != 0 && ribbon != 1)) return false;
        float ax = x[segment] * 32f, ay = y[segment] * 32f;
        float bx = x[segment + 1] * 32f, by = y[segment + 1] * 32f;
        if (!HeroArcherClothMath.IsFinite(ax) || !HeroArcherClothMath.IsFinite(ay)
            || !HeroArcherClothMath.IsFinite(bx) || !HeroArcherClothMath.IsFinite(by)
            || Math.Abs(ax) > 320f || Math.Abs(ay) > 320f || Math.Abs(bx) > 320f || Math.Abs(by) > 320f) return false;
        if (attachmentBridge && ribbon == 1)
        {
            ay -= segment == 0 ? 2f : segment == 1 ? 1f : 0f;
            by -= segment == 0 ? 1f : 0f;
        }
        // A degenerate input cannot safely invent a direction or move a grounded endpoint.
        float dx = Round(bx) - Round(ax), dy = Round(by) - Round(ay);
        if (dx == 0f && dy == 0f) return false;
        bool horizontalSection = Math.Abs(dy) >= Math.Abs(dx);
        // Outer->inner runs opposite the left normal, keeping every triangle counterclockwise.
        int sign = horizontalSection ? (dy < 0f ? 1 : -1) : (dx > 0f ? 1 : -1);
        CrossSection(ax, ay, segment, ribbon, clock, horizontalSection, sign, out Point a, out Point b, out Point c);
        CrossSection(bx, by, segment + 1, ribbon, clock, horizontalSection, sign, out Point d, out Point e, out Point f);
        result = new Segment(a, b, c, d, e, f);
        return true;
    }

    private static void CrossSection(float x, float y, int node, int ribbon, float clock, bool horizontal, int sign,
        out Point outer, out Point fold, out Point inner)
    {
        if (!HeroArcherClothMath.IsFinite(clock)) clock = 0f;
        double wave = Math.Sin(clock * (ribbon == 0 ? 0.85 : 0.67) - node * 0.48 + ribbon * 1.3);
        int width = (node < 5 ? 3 : 2) + (wave > 0.2 ? 1 : 0);
        // Broad main cloth plus a one-pixel folded edge; only a four-pixel band can
        // expose a second pixel of the fold. The palette remains constant in time.
        int folded = width == 4 && wave > 0.75 ? 2 : 1;
        float center = horizontal ? x : y;
        float low = Round(center - width * 0.5f), high = low + width;
        float a = sign > 0 ? high : low;
        float c = sign > 0 ? low : high;
        float b = c + sign * folded;
        if (horizontal)
        {
            float row = Round(y);
            outer = new Point(a, row); fold = new Point(b, row); inner = new Point(c, row);
        }
        else
        {
            float column = Round(x);
            outer = new Point(column, a); fold = new Point(column, b); inner = new Point(column, c);
        }
    }

    private static float Round(float value) => (float)Math.Floor(value + 0.5f);

    /// <summary>RGB24, matching the neck atlas palette. Each whole quad uses one color.</summary>
    internal static int FaceRgb(int ribbon, int segment, int face)
    {
        if (face == 0) return ribbon == 0 ? 0xBE1C24 : 0xE43923;
        if (ribbon == 0) return segment == 2 || segment == 5 ? 0xE43923 : 0x64121D;
        return 0x89121D;
    }
}
