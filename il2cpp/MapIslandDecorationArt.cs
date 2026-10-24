using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomEnhancedMod
{
    /// <summary>
    /// Pure geometry contract for the optional 228x84 decoration overlays. The overlay never
    /// changes shore alpha/mask/outline; high-contrast land art must stay inside one of the two
    /// authored reserved frames and the reviewed PlacementMask. Water art must stay below the
    /// island's south waterline, outside the shore and its 1px outline, and at least two pixels
    /// away from the shore mask (Chebyshev distance).
    /// </summary>
    internal static class MapIslandDecorationArt
    {
        internal const int CanvasWidth = 228;
        internal const int CanvasHeight = 84;
        internal const int WaterTopY = 55;

        internal static bool TryValidate(int physicalLand, Color32[] pixels, int width, int height,
            MapExtensionIslandArt.ShorePrep basePrep)
            => TryValidate(physicalLand, pixels, width, height, basePrep, out _, out _);

        internal static bool TryValidate(int physicalLand, Color32[] pixels, int width, int height,
            MapExtensionIslandArt.ShorePrep basePrep, out int landPixels, out int waterPixels)
        {
            landPixels = 0;
            waterPixels = 0;
            if (!MapExtensionIslandArtVariant.IsSupported(physicalLand) ||
                basePrep == null || basePrep.Width != width || basePrep.Height != height ||
                basePrep.Mask == null || basePrep.Outline == null || basePrep.PlacementMask == null ||
                basePrep.Mask.Length < width * height || basePrep.Outline.Length < width * height ||
                basePrep.PlacementMask.Length < width * height ||
                pixels == null || pixels.Length < width * height || width != CanvasWidth || height != CanvasHeight)
            {
                return false;
            }

            // The reserved frames live in the production profile; recover the exact half-open
            // top-left boxes from the single blocker source instead of duplicating coordinates.
            var blockers = new List<MapIconBox>(2);
            MapExtensionIslandArtVariant.AppendDecorationBlockers(physicalLand,
                new MapIconBox(0f, 0f, width, height), blockers);
            float gap = MapExtensionIslandLayout.Gutter;
            int[] frames = new int[blockers.Count * 4];
            for (int i = 0; i < blockers.Count; i++)
            {
                MapIconBox box = blockers[i];
                frames[i * 4] = (int)Math.Round(box.X0 + gap);
                frames[i * 4 + 1] = (int)Math.Round(box.Y0 + gap);
                frames[i * 4 + 2] = (int)Math.Round(box.X1 - gap);
                frames[i * 4 + 3] = (int)Math.Round(box.Y1 - gap);
            }

            for (int y = 0; y < height; y++)
            {
                int topDown = height - 1 - y;
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    Color32 pixel = pixels[index];
                    if (pixel.a == 0) continue;
                    if (pixel.a != 255) return false;
                    if (pixel.r != pixel.g || pixel.g != pixel.b) return false;
                    bool tone = false;
                    for (int i = 0; i < MapExtensionIslandArt.TonePalette.Length; i++)
                    {
                        if (MapExtensionIslandArt.TonePalette[i] != pixel.r) continue;
                        tone = true;
                        break;
                    }
                    if (!tone) return false;

                    if (topDown < WaterTopY)
                    {
                        bool inFrame = false;
                        for (int f = 0; f + 3 < frames.Length; f += 4)
                        {
                            if (x >= frames[f] && x < frames[f + 2] &&
                                y >= frames[f + 1] && y < frames[f + 3])
                            {
                                inFrame = true;
                                break;
                            }
                        }
                        if (!inFrame || !basePrep.PlacementMask[index]) return false;
                        landPixels++;
                    }
                    else
                    {
                        if (basePrep.Mask[index] || basePrep.Outline[index].a != 0) return false;
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int nx = x + dx, ny = y + dy;
                                if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                                if (basePrep.Mask[ny * width + nx]) return false;
                            }
                        }
                        waterPixels++;
                    }
                }
            }
            return landPixels > 0;
        }
    }
}
