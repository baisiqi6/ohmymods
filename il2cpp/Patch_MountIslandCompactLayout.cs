using System;

namespace KingdomEnhancedMod
{
    /// <summary>
    /// Overview-only geometry: fit the complete native graphic union above one compact row of
    /// two extension shores. State graphics retain their measured size and root-relative offset.
    /// No Unity, hooks, allocations, native state changes, or periodic corrections.
    /// </summary>
    internal static class MountIslandCompactLayout
    {
        internal const float SafeMargin = 2f;
        internal const float RowMargin = 1.5f;
        internal const float NativeGap = 6f;
        internal const float PairGap = 4f;
        private const int SearchSteps = 24;
        // Only comparison tolerance for float output rounding; never enlarges the usable paper.
        private const double GapTolerance = 0.0001;

        private readonly struct Bounds
        {
            internal readonly double X0, Y0, X1, Y1;
            internal Bounds(double x0, double y0, double x1, double y1)
            { X0 = x0; Y0 = y0; X1 = x1; Y1 = y1; }
            internal double Width => X1 - X0;
            internal double Height => Y1 - Y0;
            internal double Cx => X0 + Width * 0.5;
            internal double Cy => Y0 + Height * 0.5;
        }

        internal static bool TryPlan(in MountIslandFrame visible, float buttonBand,
            in MountIslandFrame nativeUnion, float baseWidth, float baseHeight,
            bool hasFixedState, in MountIslandFrame fixedStateUnion,
            out MountIslandFrame upper, out MountIslandFrame band,
            out MountIslandFrame primary, out MountIslandFrame secondary, out float scale)
        {
            upper = band = primary = secondary = default;
            scale = 0f;
            if (!TryBounds(visible, out Bounds v) || !TryBounds(nativeUnion, out Bounds n)
                || !Finite(buttonBand) || buttonBand < 0f
                || !Finite(baseWidth) || !Finite(baseHeight) || baseWidth <= 0f || baseHeight <= 0f)
                return false;

            Bounds state = default;
            if (hasFixedState)
            {
                if (!TryBounds(fixedStateUnion, out state)) return false;
            }
            else if (fixedStateUnion.X0 != 0f || fixedStateUnion.Y0 != 0f
                || fixedStateUnion.X1 != 0f || fixedStateUnion.Y1 != 0f)
                return false;

            // The native map's button band is at the top (positive paper y).
            var usable = new Bounds(v.X0 + SafeMargin, v.Y0 + SafeMargin,
                v.X1 - SafeMargin, v.Y1 - buttonBand - SafeMargin);
            if (!(usable.Width > 0.0) || !(usable.Height > 0.0)) return false;

            // Each constraint grows monotonically with scale, including the union with fixed state.
            if (!Fits(0.0, usable, n, baseWidth, baseHeight, hasFixedState, state)) return false;
            double low = 0.0;
            if (Fits(1.0, usable, n, baseWidth, baseHeight, hasFixedState, state)) low = 1.0;
            else
            {
                double high = 1.0;
                for (int i = 0; i < SearchSteps; i++)
                {
                    double mid = (low + high) * 0.5;
                    if (Fits(mid, usable, n, baseWidth, baseHeight, hasFixedState, state)) low = mid;
                    else high = mid;
                }
            }

            // Converting the last feasible double must not round up to an unverified scale.
            float candidateScale = (float)low;
            if (candidateScale > low) candidateScale = MathF.BitDecrement(candidateScale);
            if (!(candidateScale > 0f) || !Fits(candidateScale, usable, n, baseWidth,
                baseHeight, hasFixedState, state)) return false;

            Bounds envelope = RowEnvelope(candidateScale, baseWidth, baseHeight, hasFixedState, state);
            double reserve = envelope.Height + 2.0 * RowMargin;
            double groupWidth = 2.0 * envelope.Width + PairGap;
            double left = usable.Cx - groupWidth * 0.5;
            double firstCx = left - envelope.X0;
            double secondCx = firstCx + envelope.Width + PairGap;
            double rootCy = usable.Y0 + RowMargin - envelope.Y0;
            double halfWidth = baseWidth * (double)candidateScale * 0.5;
            double halfHeight = baseHeight * (double)candidateScale * 0.5;

            if (!TryFrame(usable.X0, usable.Y0, usable.X1, usable.Y0 + reserve, out var row)
                || !TryFrame(usable.X0, usable.Y0 + reserve + NativeGap, usable.X1, usable.Y1, out var top)
                || !TryFrame(firstCx - halfWidth, rootCy - halfHeight,
                    firstCx + halfWidth, rootCy + halfHeight, out var first)
                || !TryFrame(secondCx - halfWidth, rootCy - halfHeight,
                    secondCx + halfWidth, rootCy + halfHeight, out var second)
                || !Verify(usable, nativeUnion, top, row, first, second, candidateScale,
                    hasFixedState, fixedStateUnion)) return false;

            upper = top;
            band = row;
            primary = first;
            secondary = second;
            scale = candidateScale;
            return true;
        }

        /// <summary>One affine transform for native roots and exact native decorations.</summary>
        internal static bool TryTransformPoint(in MountIslandFrame nativeUnion, in MountIslandFrame upper,
            float scale, float x, float y, out float transformedX, out float transformedY)
        {
            transformedX = transformedY = 0f;
            if (!TryBounds(nativeUnion, out Bounds source) || !TryBounds(upper, out Bounds target)
                || !Finite(scale) || scale <= 0f || scale > 1f || !Finite(x) || !Finite(y)) return false;
            double tx = target.Cx + ((double)x - source.Cx) * scale;
            double ty = target.Cy + ((double)y - source.Cy) * scale;
            if (!Representable(tx) || !Representable(ty)) return false;
            transformedX = (float)tx;
            transformedY = (float)ty;
            return true;
        }

        internal static bool TryTransformFrame(in MountIslandFrame nativeUnion, in MountIslandFrame upper,
            float scale, in MountIslandFrame frame, out MountIslandFrame transformed)
        {
            transformed = default;
            if (!TryBounds(frame, out _)
                || !TryTransformPoint(nativeUnion, upper, scale, frame.X0, frame.Y0, out float x0, out float y0)
                || !TryTransformPoint(nativeUnion, upper, scale, frame.X1, frame.Y1, out float x1, out float y1))
                return false;
            return TryFrame(x0, y0, x1, y1, out transformed);
        }

        private static Bounds RowEnvelope(double scale, float width, float height, bool hasState, in Bounds state)
        {
            double hw = width * scale * 0.5, hh = height * scale * 0.5;
            return hasState
                ? new Bounds(Math.Min(-hw, state.X0), Math.Min(-hh, state.Y0),
                    Math.Max(hw, state.X1), Math.Max(hh, state.Y1))
                : new Bounds(-hw, -hh, hw, hh);
        }

        private static bool Fits(double scale, in Bounds usable, in Bounds native,
            float width, float height, bool hasState, in Bounds state)
        {
            Bounds e = RowEnvelope(scale, width, height, hasState, state);
            return native.Width * scale <= usable.Width
                && 2.0 * e.Width + PairGap + 2.0 * RowMargin <= usable.Width
                && native.Height * scale + NativeGap + e.Height + 2.0 * RowMargin <= usable.Height;
        }

        private static bool Verify(in Bounds usable, in MountIslandFrame nativeUnion,
            in MountIslandFrame upper, in MountIslandFrame band, in MountIslandFrame primary,
            in MountIslandFrame secondary, float scale, bool hasState, in MountIslandFrame state)
        {
            if (!TryBounds(upper, out Bounds u) || !TryBounds(band, out Bounds b)
                || !TryBounds(primary, out Bounds p) || !TryBounds(secondary, out Bounds s)
                || !Inside(u, usable) || !Inside(b, usable)
                || !TryTransformFrame(nativeUnion, upper, scale, nativeUnion, out var nativeTransformed)
                || !TryBounds(nativeTransformed, out Bounds nt) || !Inside(nt, u)) return false;
            Bounds pe = OutputEnvelope(p, hasState, state), se = OutputEnvelope(s, hasState, state);
            return Inside(pe, b) && Inside(se, b)
                && pe.X0 >= b.X0 + RowMargin - GapTolerance
                && se.X1 <= b.X1 - RowMargin + GapTolerance
                && pe.Y0 >= b.Y0 + RowMargin - GapTolerance
                && pe.Y1 <= b.Y1 - RowMargin + GapTolerance
                && se.X0 - pe.X1 >= PairGap - GapTolerance
                && u.Y0 - b.Y1 >= NativeGap - GapTolerance
                && Math.Abs((pe.X0 + se.X1) * 0.5 - usable.Cx) <= GapTolerance
                && Math.Abs(p.Width - s.Width) <= GapTolerance
                && Math.Abs(p.Height - s.Height) <= GapTolerance;
        }

        private static Bounds OutputEnvelope(in Bounds shore, bool hasState, in MountIslandFrame state)
            => hasState ? new Bounds(Math.Min(shore.X0, shore.Cx + state.X0), Math.Min(shore.Y0, shore.Cy + state.Y0),
                Math.Max(shore.X1, shore.Cx + state.X1), Math.Max(shore.Y1, shore.Cy + state.Y1)) : shore;

        private static bool Inside(in Bounds frame, in Bounds container)
            => frame.X0 >= container.X0 && frame.Y0 >= container.Y0
            && frame.X1 <= container.X1 && frame.Y1 <= container.Y1;

        private static bool TryBounds(in MountIslandFrame frame, out Bounds bounds)
        {
            bounds = new Bounds(frame.X0, frame.Y0, frame.X1, frame.Y1);
            return Finite(frame.X0) && Finite(frame.Y0) && Finite(frame.X1) && Finite(frame.Y1)
                && bounds.Width > 0.0 && bounds.Width <= float.MaxValue
                && bounds.Height > 0.0 && bounds.Height <= float.MaxValue;
        }

        private static bool TryFrame(double x0, double y0, double x1, double y1, out MountIslandFrame frame)
        {
            frame = default;
            if (!Representable(x0) || !Representable(y0) || !Representable(x1) || !Representable(y1)) return false;
            var result = new MountIslandFrame((float)x0, (float)y0, (float)x1, (float)y1);
            if (!TryBounds(result, out _)) return false;
            frame = result;
            return true;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Representable(double value)
            => !double.IsNaN(value) && !double.IsInfinity(value) && value >= -float.MaxValue && value <= float.MaxValue;
    }
}
