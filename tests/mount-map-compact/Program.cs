using System;
using KingdomEnhancedMod;

// Compiles the production solver itself. No Unity, game startup, resources, or native calls.
internal static class Program
{
    private static int _checks;
    private static int _failed;
    private static readonly MountIslandFrame Visible = new MountIslandFrame(0f, -5.8f, 312f, 180.8f);
    private static readonly MountIslandFrame Native = new MountIslandFrame(4f, 9.5f, 286.5f, 189.5f);
    private static readonly MountIslandFrame State = new MountIslandFrame(-27f, -25f, -11f, -9f);
    private const float BaseWidth = 98f;
    private const float BaseHeight = 98f * 84f / 228f;

    private static void Check(bool condition, string reason)
    {
        _checks++;
        if (condition) return;
        _failed++;
        Console.WriteLine("FAIL: " + reason);
    }

    private static bool Near(float a, double b, double eps = 0.0001) => Math.Abs(a - b) <= eps;
    private static bool PositiveFinite(in MountIslandFrame r)
        => float.IsFinite(r.X0) && float.IsFinite(r.Y0) && float.IsFinite(r.X1) && float.IsFinite(r.Y1)
        && r.IsValid && float.IsFinite(r.Width) && float.IsFinite(r.Height);

    private static bool Plan(in MountIslandFrame visible, float band, in MountIslandFrame native,
        float width, float height, bool hasState, in MountIslandFrame state,
        out MountIslandFrame upper, out MountIslandFrame row, out MountIslandFrame a,
        out MountIslandFrame b, out float scale)
        => MountIslandCompactLayout.TryPlan(visible, band, native, width, height, hasState, state,
            out upper, out row, out a, out b, out scale);

    private static MountIslandFrame Envelope(in MountIslandFrame shore, bool hasState, in MountIslandFrame state)
    {
        if (!hasState) return shore;
        float cx = (shore.X0 + shore.X1) * 0.5f;
        float cy = (shore.Y0 + shore.Y1) * 0.5f;
        return new MountIslandFrame(Math.Min(shore.X0, cx + state.X0), Math.Min(shore.Y0, cy + state.Y0),
            Math.Max(shore.X1, cx + state.X1), Math.Max(shore.Y1, cy + state.Y1));
    }

    private static void Invariants(in MountIslandFrame visible, float buttonBand, in MountIslandFrame native,
        bool hasState, in MountIslandFrame state, in MountIslandFrame upper, in MountIslandFrame row,
        in MountIslandFrame a, in MountIslandFrame b, float scale, string name)
    {
        var usable = new MountIslandFrame(visible.X0 + 2f, visible.Y0 + 2f,
            visible.X1 - 2f, visible.Y1 - buttonBand - 2f);
        var ea = Envelope(a, hasState, state);
        var eb = Envelope(b, hasState, state);
        Check(PositiveFinite(upper) && PositiveFinite(row) && PositiveFinite(a) && PositiveFinite(b), name + " finite positive outputs");
        Check(upper.IsInside(usable) && row.IsInside(usable), name + " respects mask and button band");
        Check(ea.IsInside(row) && eb.IsInside(row), name + " all state and shore inside row");
        Check(!ea.Intersects(eb) && Near(ea.HorizontalGap(eb), 4f), name + " envelope gap is exactly 4");
        Check(Near((ea.X0 + eb.X1) * 0.5f, (usable.X0 + usable.X1) * 0.5f), name + " envelope pair centered");
        Check(Near(ea.Y0 - row.Y0, 1.5f) && Near(row.Y1 - ea.Y1, 1.5f), name + " no old oversized row reservation");
        Check(Near(upper.Y0 - row.Y1, 6f), name + " native gap retained");
        Check(Near(a.Width, b.Width) && Near(a.Height, b.Height), name + " both shore frames use same scale");
        Check(scale > 0f && scale <= 1f, name + " uniform scale positive and capped at 1");
        Check(MountIslandCompactLayout.TryTransformFrame(native, upper, scale, native, out var transformed)
            && transformed.IsInside(upper), name + " complete native graphics fit upper");
    }

    private static void CurrentGraphicCounterexamples()
    {
        Check(Plan(Visible, 18f, Native, BaseWidth, BaseHeight, true, State,
            out var upper, out var row, out var a, out var b, out float scale), "actual current bounds fit");
        // Closed-form expected for this observed fixture: 180*s + 6 + 3 + 25 + BaseHeight*s/2 = 164.6.
        double usableHeight = (double)Visible.Y1 - Visible.Y0 - 4 - 18;
        double expected = (usableHeight - 6 - 3 - 25) / (180 + BaseHeight / 2.0);
        Check(Near(scale, expected), "actual complete graphics reach ~0.65942 instead of old ~0.507");
        Check(scale <= expected && expected - scale <= 1.0 / 16777216.0,
            "returns the feasible low, never the infeasible upper bisection endpoint");
        Check(row.Height < 40f && row.Height < 74.1f, "state envelope uses ~39.904 rather than old 74.1 row");
        Check(scale > 0.65f, "original group materially enlarged");
        Check(Near(a.Width / a.Height, 228.0 / 84.0), "shore aspect remains native");
        Check(Near((a.Y0 + a.Y1) * 0.5f, row.Y0 + 26.5f), "root positioned by real lower state edge");
        Check(Math.Abs((a.Y0 + a.Y1) * 0.5f - (row.Y0 + row.Y1) * 0.5f) > 6f,
            "state root is not incorrectly centered in band");
        Invariants(Visible, 18f, Native, true, State, upper, row, a, b, scale, "current");

        Check(Plan(Visible, 18f, Native, BaseWidth, BaseHeight, false, default,
            out var u0, out var r0, out var a0, out var b0, out float noStateScale), "state-free complete graphics fit");
        Check(Near(noStateScale, (164.6 - 6 - 3) / (180 + BaseHeight)), "complete graphics state-free is ~0.72002");
        Invariants(Visible, 18f, Native, false, default, u0, r0, a0, b0, noStateScale, "no-state");

        var terrainOnly = new MountIslandFrame(4f, 9.5f, 286.5f, 176f);
        Check(Plan(Visible, 18f, terrainOnly, BaseWidth, BaseHeight, false, default,
            out _, out _, out _, out _, out float terrainScale)
            && Near(terrainScale, (164.6 - 6 - 3) / (166.5 + BaseHeight)),
            "~0.768 is only terrain-theory, not complete graphics");
    }

    private static void NarrowAsymmetricAndOrigins()
    {
        var shortVisible = new MountIslandFrame(0f, 0f, 140f, 104f);
        Check(Plan(shortVisible, 18f, Native, BaseWidth, BaseHeight, true, State,
            out var upper, out var row, out var a, out var b, out float scale), "low scale state-extending fixture fits");
        Check(scale < 54f / BaseWidth, "fixed ship extends left beyond scaled shore");
        Check(a.HorizontalGap(b) > 4.1f, "terrain-only gap would miss extending fixed state");
        Invariants(shortVisible, 18f, Native, true, State, upper, row, a, b, scale, "low-scale");

        var offVisible = new MountIslandFrame(1000f, -305.8f, 1312f, -119.2f);
        var offNative = new MountIslandFrame(-796f, 509.5f, -513.5f, 689.5f);
        Check(Plan(offVisible, 18f, offNative, BaseWidth, BaseHeight, true, State,
            out var offUpper, out var offRow, out var offA, out var offB, out float offScale), "nonzero origins fit");
        Check(Plan(Visible, 18f, Native, BaseWidth, BaseHeight, true, State,
            out var origUpper, out _, out var origA, out _, out float origScale), "origin reference fits");
        Check(Near(offScale, origScale) && Near(offA.X0 - origA.X0, 1000f)
            && Near(offA.Y0 - origA.Y0, -300f), "translation does not alter common scale");
        Invariants(offVisible, 18f, offNative, true, State, offUpper, offRow, offA, offB, offScale, "offset");

        var asymmetric = new MountIslandFrame(20f, -7f, 57f, 41f);
        Check(Plan(new MountIslandFrame(-60f, -10f, 240f, 220f), 3f, Native, 98f, BaseHeight,
            true, asymmetric, out var ua, out var ra, out var aa, out var ba, out float sa), "asymmetric right-top state fits");
        Invariants(new MountIslandFrame(-60f, -10f, 240f, 220f), 3f, Native, true, asymmetric,
            ua, ra, aa, ba, sa, "asymmetric");

        var tinyButFeasible = new MountIslandFrame(0f, 0f, 12f, 20f);
        Check(Plan(tinyButFeasible, 0f, new MountIslandFrame(0f, 0f, 30f, 20f), 30f, 20f,
            false, default, out var ut, out var rt, out var at, out var bt, out float st) && st > 0f && st < 0.1f,
            "small feasible window has no magic minimum scale");
        Invariants(tinyButFeasible, 0f, new MountIslandFrame(0f, 0f, 30f, 20f), false, default,
            ut, rt, at, bt, st, "tiny-feasible");

        var roomy = new MountIslandFrame(-500f, -500f, 500f, 500f);
        Check(Plan(roomy, 0f, Native, BaseWidth, BaseHeight, true, State,
            out _, out _, out _, out _, out float one) && one == 1f, "roomy paper stops at exactly scale 1");
        Check(origUpper.Height >= Native.Height * origScale, "returned low is feasible without post-hoc size clamp");
    }

    private static void TransformKeepsNativeGeography()
    {
        Check(Plan(Visible, 18f, Native, BaseWidth, BaseHeight, true, State,
            out var upper, out _, out _, out _, out float scale), "transform fixture planned");
        Check(MountIslandCompactLayout.TryTransformPoint(Native, upper, scale, 4f, 9.5f, out float x0, out float y0)
            && MountIslandCompactLayout.TryTransformPoint(Native, upper, scale, 286.5f, 189.5f, out float x1, out float y1),
            "extreme graphic points transformed");
        // Call again so definite assignment remains explicit rather than conditional local outputs.
        MountIslandCompactLayout.TryTransformPoint(Native, upper, scale, 286.5f, 189.5f, out float xEnd, out float yEnd);
        Check(Near(xEnd - x0, Native.Width * scale) && Near(yEnd - y0, Native.Height * scale),
            "one transform preserves full graphic spacing");
        Check(Near((xEnd + x0) * 0.5f, (upper.X0 + upper.X1) * 0.5f)
            && Near((yEnd + y0) * 0.5f, (upper.Y0 + upper.Y1) * 0.5f), "native union centers in upper");
        var deco = new MountIslandFrame(150f, 110f, 165f, 122f);
        Check(MountIslandCompactLayout.TryTransformFrame(Native, upper, scale, deco, out var transformedDeco)
            && Near(transformedDeco.Width, deco.Width * scale) && Near(transformedDeco.Height, deco.Height * scale),
            "direct decoration shares root transform without individual rebasing");
        Check(!MountIslandCompactLayout.TryTransformPoint(Native, upper, 0f, 4f, 5f, out _, out _)
            && !MountIslandCompactLayout.TryTransformPoint(Native, upper, 1.001f, 4f, 5f, out _, out _),
            "invalid transform scale rejected");
        Check(!MountIslandCompactLayout.TryTransformPoint(Native, upper, scale, float.NaN, 5f, out _, out _)
            && !MountIslandCompactLayout.TryTransformFrame(Native, upper, scale, default, out _),
            "invalid transform input rejected");
    }

    private static void ButtonBandIsAtTop()
    {
        Check(Plan(Visible, 18f, Native, BaseWidth, BaseHeight, true, State,
            out var upper, out var row, out _, out _, out _), "top button contract fixture fits");
        Check(Near(row.Y0, -3.8f), "row starts at bottom safe margin, not above a bottom button band");
        Check(Near(upper.Y1, 160.8f), "upper stops below the native top button band");

        var translated = new MountIslandFrame(1000f, -305.8f, 1312f, -119.2f);
        Check(Plan(translated, 18f, Native, BaseWidth, BaseHeight, true, State,
            out var translatedUpper, out var translatedRow, out _, out _, out _), "translated top button fixture fits");
        Check(Near(translatedRow.Y0, -303.8f), "translated bottom still reserves only safe margin");
        Check(Near(translatedUpper.Y1, -139.2f), "translated top reserves button band plus safe margin");
    }

    private static void NativeWidthCanBeTheBindingLimit()
    {
        var wideNative = new MountIslandFrame(-40f, 20f, 960f, 60f);
        Check(Plan(Visible, 18f, wideNative, BaseWidth, BaseHeight, true, State,
            out var upper, out _, out _, out _, out float scale), "wide native graphics remain feasible");
        // This fixture has 308 usable width and 1000 native width. Height and the fixed-state
        // row have ample room, so native width is the binding limit independently of the solver.
        double expected = 308.0 / 1000.0;
        Check(scale <= expected && Near(scale, expected, 0.0000001), "native width limits scale to feasible ~0.308");
        Check(MountIslandCompactLayout.TryTransformFrame(wideNative, upper, scale, wideNative,
            out var transformed) && transformed.IsInside(upper), "complete wide native bbox maps inside upper");
        Check(Near(transformed.Width, 308.0), "complete mapped native width uses ~308 available units");
    }

    private static void RejectsUnknownContradictoryAndNonFinite()
    {
        void Reject(in MountIslandFrame visible, float button, in MountIslandFrame native,
            float w, float h, bool hasState, in MountIslandFrame state, string name)
        {
            bool ok = Plan(visible, button, native, w, h, hasState, state, out var u, out var r, out var a, out var b, out float s);
            Check(!ok && s == 0f && !u.IsValid && !r.IsValid && !a.IsValid && !b.IsValid, name + " fails with empty outputs");
        }
        Reject(default, 18f, Native, BaseWidth, BaseHeight, true, State, "empty visible");
        Reject(Visible, 18f, default, BaseWidth, BaseHeight, true, State, "empty native union");
        Reject(Visible, -1f, Native, BaseWidth, BaseHeight, true, State, "negative button band");
        Reject(Visible, 190f, Native, BaseWidth, BaseHeight, true, State, "button band consumes paper");
        Reject(Visible, 18f, Native, 0f, BaseHeight, true, State, "zero shore width");
        Reject(Visible, 18f, Native, BaseWidth, -1f, true, State, "negative shore height");
        Reject(Visible, 18f, Native, BaseWidth, BaseHeight, true, default, "unknown state is not empty");
        Reject(Visible, 18f, Native, BaseWidth, BaseHeight, false, State, "no-state flag contradicts provided bbox");
        Reject(Visible, 18f, Native, BaseWidth, BaseHeight, false, new MountIslandFrame(1f, 0f, 0f, 0f),
            "nonzero degenerate state is not explicit default");
        Reject(new MountIslandFrame(0f, 0f, 9f, 20f), 0f, Native, BaseWidth, BaseHeight, false, default,
            "too narrow even for fixed margins");
        Reject(new MountIslandFrame(0f, 0f, 64f, 104f), 18f, Native, BaseWidth, BaseHeight, true, State,
            "fixed state prevents pretending tiny scale fits");
        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Reject(new MountIslandFrame(bad, 0f, 312f, 200f), 0f, Native, BaseWidth, BaseHeight, false, default, "nonfinite visible");
            Reject(Visible, bad, Native, BaseWidth, BaseHeight, true, State, "nonfinite button band");
            Reject(Visible, 18f, new MountIslandFrame(4f, 9f, bad, 189f), BaseWidth, BaseHeight, false, default, "nonfinite native");
            Reject(Visible, 18f, Native, bad, BaseHeight, true, State, "nonfinite base width");
            Reject(Visible, 18f, Native, BaseWidth, BaseHeight, true, new MountIslandFrame(-27f, -25f, bad, -9f), "nonfinite fixed state");
        }
        Reject(new MountIslandFrame(-float.MaxValue, 0f, float.MaxValue, 200f), 0f, Native, BaseWidth, BaseHeight,
            false, default, "float width overflow");
        Reject(Visible, 18f, new MountIslandFrame(-float.MaxValue, 0f, float.MaxValue, 200f), BaseWidth, BaseHeight,
            false, default, "native extent overflow");
        Reject(Visible, 18f, Native, float.Epsilon, float.Epsilon, false, default, "collapsed float shore output");
    }

    private static int Main()
    {
        CurrentGraphicCounterexamples();
        NarrowAsymmetricAndOrigins();
        TransformKeepsNativeGeography();
        ButtonBandIsAtTop();
        NativeWidthCanBeTheBindingLimit();
        RejectsUnknownContradictoryAndNonFinite();
        Console.WriteLine($"checks={_checks} passed={_checks - _failed} failed={_failed}");
        Console.WriteLine(_failed == 0 ? "RESULT: PASS" : "RESULT: FAIL");
        return _failed == 0 ? 0 : 1;
    }
}
