#if ANDROID
using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace KingdomEnhancedMod;

internal static class AndroidTexturePixels
{
    /// <summary>Owned readable no-mip PNG texture only: this game's Android GetPixels32 ICall is unavailable.
    /// Reads public format/GetImageDataSize/GetWritableImageData(0), copies once into a managed
    /// array without retaining or freeing the native pointer (no cache/Apply/Dispose). RGBA32 = R,G,B,A;
    /// ARGB32 = A,R,G,B (Unity 6000 PNG LoadImage). Other formats/sizes/pointers throw contextual
    /// exceptions to the caller's existing catch; no contract for any other texture kind.</summary>
    internal static Color32[] ReadOwnedPng(Texture2D texture, int width, int height)
    {
        TextureFormat format = texture.format;
        if (format != TextureFormat.RGBA32 && format != TextureFormat.ARGB32)
            throw new InvalidOperationException("unsupported image format " + format + " for "
                + width + "x" + height);
        int count = checked(width * height);
        int length = checked(count * 4);
        ulong size = texture.GetImageDataSize();
        if (size != (ulong)length)
            throw new InvalidOperationException("image data size " + size + " != expected " + length
                + " for " + width + "x" + height + " " + format);
        IntPtr pointer = texture.GetWritableImageData(0);
        if (pointer == IntPtr.Zero)
            throw new InvalidOperationException("image data pointer unavailable for "
                + width + "x" + height + " " + format);
        byte[] bytes = new byte[length];
        Marshal.Copy(pointer, bytes, 0, length);
        Color32[] pixels = new Color32[count];
        for (int i = 0; i < count; i++)
        {
            int n = i * 4;
            pixels[i] = format == TextureFormat.RGBA32
                ? new Color32(bytes[n], bytes[n + 1], bytes[n + 2], bytes[n + 3])
                : new Color32(bytes[n + 1], bytes[n + 2], bytes[n + 3], bytes[n]);
        }
        return pixels;
    }
}
#endif
