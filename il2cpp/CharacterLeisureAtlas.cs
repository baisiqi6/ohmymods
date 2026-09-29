using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>Optional 4x4 overlay atlas. Missing/invalid art leaves the original visual path intact.</summary>
internal static class CharacterLeisureAtlas
{
    internal const int Columns = 4;
    internal const int Rows = 4;
    internal const int Frames = 16;

    internal static bool TryLoad(string resource, int width, int height, out Texture2D atlas, out Sprite[] sprites)
    {
        atlas = null;
        sprites = null;
        Texture2D pending = null;
        Sprite[] pendingSprites = null;
        try
        {
            Assembly assembly = typeof(CharacterLeisureAtlas).Assembly;
            using Stream stream = assembly.GetManifestResourceStream(resource);
            if (stream == null || stream.Length <= 0 || stream.Length > 4L * 1024L * 1024L) return false;
            byte[] bytes = new byte[(int)stream.Length];
            int read = 0;
            while (read < bytes.Length)
            {
                int step = stream.Read(bytes, read, bytes.Length - read);
                if (step <= 0) return false;
                read += step;
            }
            pending = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(pending, bytes, false)) return false;
            if (pending.width != width * Columns || pending.height != height * Rows) return false;
            Color32[] pixels = pending.GetPixels32();
            if (pixels == null || pixels.Length != pending.width * pending.height) return false;
            for (int frame = 0; frame < Frames; frame++)
            {
                int col = frame % Columns;
                int row = frame / Columns;
                bool seen = false;
                for (int y = 0; y < height && !seen; y++)
                    for (int x = 0; x < width; x++)
                        if (pixels[((Rows - 1 - row) * height + y) * pending.width + col * width + x].a != 0)
                        { seen = true; break; }
                if (!seen) return false;
            }
            pending.filterMode = FilterMode.Point;
            pending.wrapMode = TextureWrapMode.Clamp;
            pending.anisoLevel = 0;
            pendingSprites = new Sprite[Frames];
            Vector2 pivot = new Vector2(31f / width, 2f / height);
            for (int frame = 0; frame < Frames; frame++)
            {
                Rect rect = new Rect((frame % Columns) * width,
                    (Rows - 1 - frame / Columns) * height, width, height);
                pendingSprites[frame] = Sprite.Create(pending, rect, pivot, 32f, 0u, SpriteMeshType.FullRect);
                if (pendingSprites[frame] == null) return false;
            }
            atlas = pending;
            sprites = pendingSprites;
            pending = null;
            pendingSprites = null;
            return true;
        }
        catch (Exception) { return false; }
        finally
        {
            if (pendingSprites != null)
                foreach (Sprite sprite in pendingSprites)
                    if (sprite != null) try { UnityEngine.Object.Destroy(sprite); } catch (Exception) { }
            if (pending != null)
            {
                try { UnityEngine.Object.Destroy(pending); } catch (Exception) { }
            }
        }
    }
}
