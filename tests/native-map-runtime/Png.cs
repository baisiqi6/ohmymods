// 只读 PNG 解码（探针侧 fixture 解码；不写任何 PNG、不修改原 bytes）。
// 与 tests/shore-art 的 Png 解码同一算法；供 ImageConversion.LoadImage 桩驱动真实 MapExtensionIslandArt。
using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

namespace RuntimeProbe
{
    internal static class Png
    {
        public static void Decode(byte[] data, out int w, out int h, out Color32[] pixels)
        {
            int pos = 8;
            w = h = 0;
            var idat = new MemoryStream();
            while (pos + 8 <= data.Length)
            {
                int len = (data[pos] << 24) | (data[pos + 1] << 16) | (data[pos + 2] << 8) | data[pos + 3];
                string type = Encoding.ASCII.GetString(data, pos + 4, 4);
                int start = pos + 8;
                if (type == "IHDR")
                {
                    w = (data[start] << 24) | (data[start + 1] << 16) | (data[start + 2] << 8) | data[start + 3];
                    h = (data[start + 4] << 24) | (data[start + 5] << 16) | (data[start + 6] << 8) | data[start + 7];
                }
                else if (type == "IDAT")
                {
                    idat.Write(data, start, len);
                }
                pos = start + len + 4;
            }
            byte[] idatBytes = idat.ToArray();
            byte[] raw;
            using (var z = new ZLibStream(new MemoryStream(idatBytes), CompressionMode.Decompress))
            using (var o = new MemoryStream())
            {
                z.CopyTo(o);
                raw = o.ToArray();
            }

            int bpp = 4, stride = w * 4;
            pixels = new Color32[w * h];
            var line = new byte[stride];
            var prev = new byte[stride];
            int rp = 0;
            for (int y = 0; y < h; y++)
            {
                int filter = raw[rp++];
                Array.Copy(raw, rp, line, 0, stride);
                rp += stride;
                for (int x = 0; x < stride; x++)
                {
                    int a = x >= bpp ? line[x - bpp] : 0;
                    int b = prev[x];
                    int c = x >= bpp ? prev[x - bpp] : 0;
                    int v = line[x];
                    if (filter == 1) v += a;
                    else if (filter == 2) v += b;
                    else if (filter == 3) v += (a + b) / 2;
                    else if (filter == 4)
                    {
                        int p = a + b - c;
                        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                        v += (pa <= pb && pa <= pc) ? a : (pb <= pc ? b : c);
                    }
                    line[x] = (byte)v;
                }
                for (int x = 0; x < w; x++)
                {
                    pixels[y * w + x] = new Color32(line[x * 4], line[x * 4 + 1], line[x * 4 + 2], line[x * 4 + 3]);
                }
                Array.Copy(line, prev, stride);
            }
        }
    }
}
