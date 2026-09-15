// 最小 PNG 解码器：只服务替身 ImageConversion（8-bit、非隔行、RGBA/RGB/灰度），
// 让「模块的真实 embedded 资源」在本机被真切解码、真切校验；生产路径用的是 Unity ImageConversion.LoadImage。

using System;
using System.IO;
using System.IO.Compression;
using UnityEngine;

internal static class PngCodec
{
    private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

    internal static bool TryDecode(byte[] data, out int width, out int height, out Color32[] pixels)
    {
        width = 0;
        height = 0;
        pixels = null;
        try
        {
            if (data == null || data.Length < 8 + 25) return false;
            for (int i = 0; i < Signature.Length; i++) if (data[i] != Signature[i]) return false;

            int offset = 8;
            int bitDepth = 0;
            int colorType = -1;
            int interlace = 0;
            using (MemoryStream idat = new MemoryStream())
            {
                while (offset + 12 <= data.Length)
                {
                    int length = ReadInt32(data, offset);
                    if (length < 0 || offset + 12 + length > data.Length) return false;
                    string type = "" + (char)data[offset + 4] + (char)data[offset + 5] + (char)data[offset + 6] + (char)data[offset + 7];
                    int payload = offset + 8;
                    if (type == "IHDR")
                    {
                        if (length != 13) return false;
                        width = ReadInt32(data, payload);
                        height = ReadInt32(data, payload + 4);
                        bitDepth = data[payload + 8];
                        colorType = data[payload + 9];
                        interlace = data[payload + 12];
                    }
                    else if (type == "IDAT")
                    {
                        idat.Write(data, payload, length);
                    }
                    else if (type == "IEND")
                    {
                        break;
                    }
                    offset = payload + length + 4;
                }

                if (width <= 0 || height <= 0 || bitDepth != 8 || interlace != 0) return false;
                int channels;
                if (colorType == 6) channels = 4;
                else if (colorType == 2) channels = 3;
                else if (colorType == 0) channels = 1;
                else return false;

                idat.Position = 0;
                byte[] raw;
                using (ZLibStream inflate = new ZLibStream(idat, CompressionMode.Decompress))
                using (MemoryStream output = new MemoryStream())
                {
                    inflate.CopyTo(output);
                    raw = output.ToArray();
                }

                int stride = width * channels;
                if (raw.Length < height * (stride + 1)) return false;

                byte[] previous = new byte[stride];
                byte[] current = new byte[stride];
                Color32[] result = new Color32[width * height];
                for (int y = 0; y < height; y++)
                {
                    int rowStart = y * (stride + 1);
                    int filter = raw[rowStart];
                    Buffer.BlockCopy(raw, rowStart + 1, current, 0, stride);
                    for (int x = 0; x < stride; x++)
                    {
                        int left = x >= channels ? current[x - channels] : 0;
                        int up = previous[x];
                        int upLeft = x >= channels ? previous[x - channels] : 0;
                        int value = current[x];
                        switch (filter)
                        {
                            case 0: break;
                            case 1: value = (value + left) & 0xFF; break;
                            case 2: value = (value + up) & 0xFF; break;
                            case 3: value = (value + ((left + up) >> 1)) & 0xFF; break;
                            case 4: value = (value + Paeth(left, up, upLeft)) & 0xFF; break;
                            default: return false;
                        }
                        current[x] = (byte)value;
                    }
                    for (int x = 0; x < width; x++)
                    {
                        byte r, g, b, a;
                        if (channels == 4)
                        {
                            r = current[x * 4]; g = current[x * 4 + 1]; b = current[x * 4 + 2]; a = current[x * 4 + 3];
                        }
                        else if (channels == 3)
                        {
                            r = current[x * 3]; g = current[x * 3 + 1]; b = current[x * 3 + 2]; a = 255;
                        }
                        else
                        {
                            r = g = b = current[x]; a = 255;
                        }
                        result[y * width + x] = new Color32(r, g, b, a);
                    }
                    byte[] swap = previous;
                    previous = current;
                    current = swap;
                }
                pixels = result;
                return true;
            }
        }
        catch (Exception)
        {
            pixels = null;
            return false;
        }
    }

    private static int Paeth(int left, int up, int upLeft)
    {
        int p = left + up - upLeft;
        int pa = Math.Abs(p - left);
        int pb = Math.Abs(p - up);
        int pc = Math.Abs(p - upLeft);
        if (pa <= pb && pa <= pc) return left;
        if (pb <= pc) return up;
        return upLeft;
    }

    private static int ReadInt32(byte[] data, int offset)
    {
        return (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
    }
}
