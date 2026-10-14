using System;
using UnityEngine;

namespace KingdomEnhancedMod
{
    /// <summary>
    /// Cold preparation data for the two registered paper-map islands. World generation and
    /// save identities do not depend on this art. Both final canvases remain 228x84.
    /// </summary>
    internal static class MapExtensionIslandArtVariant
    {
        private const int CanvasWidth = 228;
        private const int CanvasHeight = 84;
        // Both reviewed source images have fewer than 1.6M pixels. Bound the allocations made
        // by the existing component-label/queue preparation before its int multiplication.
        private const long MaxSourcePixels = 4L * 1024 * 1024;
        private const int PlacementInset = 2;
        private const float CutTolerance = 0.001f;

        // Authored B frontier in final prepared pixels: x from left, y from top. Independently
        // reviewed against the v3 plateau/front-cliff boundary; never classify cliffs by luma.
        private static readonly int[] CutX =
        {
            0, 8, 16, 24, 32, 40, 48, 56, 64, 72, 80, 88, 96, 104, 112,
            120, 128, 136, 144, 152, 160, 168, 176, 184, 192, 200, 208, 216, 224, 228
        };
        private static readonly int[] CutY =
        {
            45, 49, 55, 55, 53, 55, 57, 60, 61, 63, 62, 55, 48, 45, 43,
            42, 43, 44, 46, 48, 53, 56, 60, 63, 63, 58, 55, 48, 43, 43
        };
        private static readonly MapIconAnchorSet CrescentAnchors = BuildCrescentAnchors();

        internal static bool IsSupported(int physicalLand) => physicalLand == 11 || physicalLand == 13;

        internal static string ResourceNameFor(int physicalLand)
        {
            if (physicalLand == 11) return "KingdomEnhancedMod.KEM_MapExtensionIsland.png";
            if (physicalLand == 13) return "KingdomEnhancedMod.KEM_MapExtensionIslandCrescent.png";
            return null;
        }

        internal static MapExtensionIslandArt.ShorePrep Prepare(int physicalLand, Color32[] source,
            int width, int height)
        {
            if (!IsSupported(physicalLand) || source == null || width <= 0 || height <= 0) return null;
            long pixels = (long)width * height;
            if (pixels > MaxSourcePixels || pixels != source.LongLength) return null;

            bool primary = physicalLand == 11;
            var prep = MapExtensionIslandArt.BuildPrep(source, width, height, primary ? 224 : 222);
            if (primary) return HasFrame(prep, CanvasWidth, CanvasHeight) ? prep : null;
            if (!HasFrame(prep, 226, 82)) return null;

            // B uses the original single-scale nearest preparation, then one transparent pixel
            // on every side. No resampling, stretch or change to the shared UI frame/aspect.
            var mask = Pad(prep.Mask, prep.Width, prep.Height);
            bool[] placement = BuildCrescentPlacementMask(mask);
            int placementPixels = 0;
            for (int i = 0; i < placement.Length; i++) if (placement[i]) placementPixels++;
            return new MapExtensionIslandArt.ShorePrep
            {
                SrcWidth = prep.SrcWidth, SrcHeight = prep.SrcHeight,
                BboxX = prep.BboxX, BboxY = prep.BboxY,
                BboxWidth = prep.BboxWidth, BboxHeight = prep.BboxHeight,
                Aspect = prep.Aspect,
                Width = CanvasWidth, Height = CanvasHeight,
                Shore = Pad(prep.Shore, prep.Width, prep.Height),
                Outline = Pad(prep.Outline, prep.Width, prep.Height),
                Mask = mask, PlacementMask = placement, PlacementPixels = placementPixels,
                IslandPixels = prep.IslandPixels, ComponentCount = prep.ComponentCount,
                GrayMin = prep.GrayMin, GrayMax = prep.GrayMax, LevelsUsed = prep.LevelsUsed
            };
        }

        // Null means the entire legacy A profile. Unknown lands must be rejected by the caller
        // before planning; a null profile alone is not authority to handle another island.
        internal static MapIconAnchorSet AnchorsFor(int physicalLand)
            => physicalLand == 13 ? CrescentAnchors : null;

        private static bool HasFrame(MapExtensionIslandArt.ShorePrep prep, int width, int height)
        {
            if (prep == null || prep.Width != width || prep.Height != height) return false;
            int length = width * height;
            return prep.Shore != null && prep.Shore.Length == length &&
                prep.Outline != null && prep.Outline.Length == length &&
                prep.Mask != null && prep.Mask.Length == length;
        }

        private static T[] Pad<T>(T[] source, int width, int height)
        {
            var result = new T[CanvasWidth * CanvasHeight];
            for (int y = 0; y < height; y++)
                Array.Copy(source, y * width, result, (y + 1) * CanvasWidth + 1, width);
            return result;
        }

        private static float CutAt(int x)
        {
            for (int i = 1; i < CutX.Length; i++)
                if (x <= CutX[i])
                    return CutY[i - 1] + (CutY[i] - CutY[i - 1]) * (x - CutX[i - 1]) /
                        (float)(CutX[i] - CutX[i - 1]);
            return CutY[CutY.Length - 1];
        }

        private static bool[] BuildCrescentPlacementMask(bool[] alpha)
        {
            var top = new bool[CanvasWidth * CanvasHeight];
            for (int y = 0; y < CanvasHeight; y++)
            {
                int topDown = CanvasHeight - 1 - y;
                for (int x = 0; x < CanvasWidth; x++)
                {
                    // The west concave coast includes a vertical cliff inside the alpha. Its
                    // explicit exclusion is necessary in addition to the front-edge frontier.
                    bool westCliff = x >= 29 && x <= 34 && topDown >= 28 && topDown <= 43;
                    top[y * CanvasWidth + x] = alpha[y * CanvasWidth + x] &&
                        topDown <= CutAt(x) + CutTolerance && !westCliff;
                }
            }
            var eroded = new bool[top.Length];
            for (int y = 0; y < CanvasHeight; y++) for (int x = 0; x < CanvasWidth; x++)
            {
                bool keep = top[y * CanvasWidth + x];
                for (int dy = -PlacementInset; dy <= PlacementInset && keep; dy++)
                {
                    int yy = y + dy;
                    if (yy < 0 || yy >= CanvasHeight) { keep = false; break; }
                    for (int dx = -PlacementInset; dx <= PlacementInset; dx++)
                    {
                        int xx = x + dx;
                        if (xx < 0 || xx >= CanvasWidth || !top[yy * CanvasWidth + xx])
                        { keep = false; break; }
                    }
                }
                eroded[y * CanvasWidth + x] = keep;
            }
            return eroded;
        }

        private static MapIconAnchorSet BuildCrescentAnchors()
        {
            var x = (float[])MapExtensionIslandLayout.PreferredAnchorX.Clone();
            var y = (float[])MapExtensionIslandLayout.PreferredAnchorY.Clone();
            float[] firstX =
            {
                .30701753f, .5701754f, .80263156f, .39473686f,
                .65789473f, .21491228f, .80263156f, .48245615f
            };
            float[] firstY =
            {
                .5833333f, .6547619f, .5119048f, .78571427f,
                .48809522f, .4642857f, .3690476f, .4642857f
            };
            Array.Copy(firstX, x, firstX.Length);
            Array.Copy(firstY, y, firstY.Length);
            // Constructor clones all four arrays. Keep the remaining ten primary slots and
            // every legacy secondary value, including NaN-disabled passes and MaxItems18.
            return new MapIconAnchorSet(x, y, MapExtensionIslandLayout.FallbackAnchorX,
                MapExtensionIslandLayout.FallbackAnchorY);
        }
    }
}
