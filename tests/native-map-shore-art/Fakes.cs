// Unity 最小假类型 + PNG 只读解码 + 日志假件（仅供 console 测试驱动真实 MapExtensionIslandArt）。
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace UnityEngine
{
    public class Object
    {
        internal bool Destroyed;
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.Destroyed;
            bool bn = ReferenceEquals(b, null) || b.Destroyed;
            return (an && bn) || ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) => !(a == b);
        public static implicit operator bool(Object o) => !(o == null);
        public override bool Equals(object o) => ReferenceEquals(this, o);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
        public static int DestroyCount;
        public static int CreateCount;
        public static readonly List<Object> Alive = new List<Object>();
        public static void Destroy(Object o)
        {
            DestroyCount++;
            if (!ReferenceEquals(o, null) && !o.Destroyed) { o.Destroyed = true; Alive.Remove(o); }
        }
        public static int AliveCount => Alive.Count;
        public static void Track(Object o) { CreateCount++; Alive.Add(o); }
    }

    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }

    public struct Vector2 { public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; } }

    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float w, float h) { this.x = x; this.y = y; width = w; height = h; }
    }

    public enum TextureFormat { RGBA32 = 4 }
    public enum FilterMode { Point = 0, Bilinear = 1 }
    public enum TextureWrapMode { Repeat = 0, Clamp = 1 }
    public enum SpriteMeshType { FullRect = 1 }

    public class Texture2D : Object
    {
        public int width, height;
        public FilterMode filterMode;
        public TextureWrapMode wrapMode;
        public int anisoLevel;
        internal Color32[] Pixels = Array.Empty<Color32>();
        public bool SetPixelsThrows, GetPixelsThrows;
        public Texture2D(int w, int h, TextureFormat fmt, bool mip) { width = w; height = h; Pixels = new Color32[w * h]; Object.Track(this); }
        public void SetPixels32(Color32[] p) { if (SetPixelsThrows) throw new InvalidOperationException("set-pixels"); Pixels = (Color32[])p.Clone(); }
        public Color32[] GetPixels32() { if (GetPixelsThrows) throw new InvalidOperationException("get-pixels"); return Pixels; }
        public void Apply(bool u, bool m) { }
    }

    public static class ImageConversion
    {
        public static bool FailNext;
        public static bool GetPixelsThrowsNext;
        public static bool LoadImage(Texture2D tex, byte[] bytes, bool markNonReadable)
        {
            if (FailNext) { FailNext = false; return false; }
            // Unity 契约：LoadImage 解 PNG 后，GetPixels32/SetPixels32 以 bottom-up 行序工作
            // （index 0 = 图像左下角）。唯一一次转换在 Png.DecodeBottomUp；生产 BuildPrep 不再翻。
            ShoreArtTests.Png.DecodeBottomUp(bytes, out int w, out int h, out Color32[] pixels);
            tex.width = w;
            tex.height = h;
            tex.Pixels = pixels;
            if (GetPixelsThrowsNext) tex.GetPixelsThrows = true;
            return true;
        }
    }

    public class Sprite : Object
    {
        /// <summary>共享 native 后端：同一 native Sprite 的多个 IL2CPP wrapper 共享同一状态（Il2CppObjectPool 语义）。</summary>
        internal sealed class NativeState
        {
            private static long _next;
            internal readonly System.IntPtr Pointer;
            internal readonly int InstanceId;
            public NativeState()
            {
                long id = System.Threading.Interlocked.Increment(ref _next);
                Pointer = new System.IntPtr(0x4000 + id);
                InstanceId = (int)(0x20000 + id);
            }
        }

        internal NativeState Native = new NativeState();
        public System.IntPtr Pointer => Native.Pointer;
        public int GetInstanceID() => Native.InstanceId;

        public Texture2D texture;
        public static bool FailNextCreate;
        public static Sprite Create(Texture2D tex, Rect rect, Vector2 pivot, float ppu, uint ext, SpriteMeshType type)
        {
            if (FailNextCreate) { FailNextCreate = false; return null; }
            if (tex == null) return null;
            var s = new Sprite { texture = tex };
            Object.Track(s);
            return s;
        }
    }
}

namespace UnityEngine.UI
{
    using UnityEngine;
    public class Image : Object
    {
        /// <summary>共享 native 后端：同一 native Image 的多个 IL2CPP wrapper 共享同一状态对象。</summary>
        internal sealed class NativeState
        {
            private static long _next;
            internal Sprite Value;
            internal readonly System.IntPtr Pointer;
            internal readonly int InstanceId;
            public NativeState()
            {
                long id = System.Threading.Interlocked.Increment(ref _next);
                Pointer = new System.IntPtr(0x2000 + id);
                InstanceId = (int)(0x10000 + id);
            }
        }

        internal NativeState Native = new NativeState();

        internal Sprite _sprite { get => Native.Value; set => Native.Value = value; }
        public bool ThrowOnSet;
        public bool ThrowOnGet;
        /// <summary>写后抛：sprite 已写入新值，随后 setter 抛异常（不假设 setter 原子）。</summary>
        public bool WriteThenThrowSet;
        /// <summary>写后抛时同时进入读故障（setter 写入后抛 + 随后 getter 也 unknown）。</summary>
        public bool ReadFaultAfterWriteThrow;
        /// <summary>身份读取故障：Pointer/GetInstanceID 抛异常。</summary>
        public bool ThrowOnIdentity;
        /// <summary>模拟 Il2CppObjectPool：getter 返回“同 native、不同 managed wrapper”的新实例。</summary>
        public bool FreshSpriteWrappers;

        public System.IntPtr Pointer
        {
            get
            {
                if (ThrowOnIdentity) throw new System.InvalidOperationException("identity");
                return Native.Pointer;
            }
        }

        public int GetInstanceID()
        {
            if (ThrowOnIdentity) throw new System.InvalidOperationException("identity");
            return Native.InstanceId;
        }

        private Sprite Read()
        {
            if (ThrowOnGet) throw new System.InvalidOperationException("get-sprite");
            if (FreshSpriteWrappers && _sprite != null)
            {
                return new Sprite { Native = _sprite.Native, texture = _sprite.texture };
            }
            return _sprite;
        }

        private void Write(Sprite value)
        {
            if (ThrowOnSet) throw new System.InvalidOperationException("set-sprite");
            if (WriteThenThrowSet)
            {
                _sprite = value;
                if (ReadFaultAfterWriteThrow) ThrowOnGet = true;
                throw new System.InvalidOperationException("set-then-throw");
            }
            _sprite = value;
        }

        public Sprite Sprite { get => Read(); set => Write(value); }
        public Sprite sprite { get => Read(); set => Write(value); }
    }
}

namespace KingdomEnhancedMod
{
    internal static class MapIconSources
    {
        internal static class Log
        {
            internal static void Warn(string m) { ShoreArtTests.Log.Add("WARN " + m); }
            internal static void Info(string m) { ShoreArtTests.Log.Add("INFO " + m); }
        }
    }
}

namespace ShoreArtTests
{
    using UnityEngine;

    internal static class Log
    {
        internal static readonly List<string> Lines = new List<string>();
        internal static void Add(string m) => Lines.Add(m);
    }

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
                else if (type == "IDAT") idat.Write(data, start, len);
                pos = start + len + 4;
            }
            var idatBytes = idat.ToArray();
            idat.Position = 0;
            byte[] raw;
            using (var z = new ZLibStream(new MemoryStream(idatBytes), CompressionMode.Decompress))
            using (var o = new MemoryStream()) { z.CopyTo(o); raw = o.ToArray(); }

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
                    pixels[y * w + x] = new Color32(line[x * 4], line[x * 4 + 1], line[x * 4 + 2], line[x * 4 + 3]);
                Array.Copy(line, prev, stride);
            }
        }

        /// <summary>
        /// PNG bytes → Unity GetPixels32 行序（index 0 = 图像**左下**）。
        /// 唯一一次 top-down（文件行序）→ bottom-up（Unity 纹理行序）转换；生产 BuildPrep 不额外翻。
        /// 三个 caller（Program 联合 / LayoutReport 导出 / runtime-probe fake LoadImage）都必须走这里或等价一次转换。
        /// </summary>
        public static void DecodeBottomUp(byte[] data, out int w, out int h, out Color32[] pixels)
        {
            Decode(data, out w, out h, out Color32[] topDown);
            pixels = FlipVertical(topDown, w, h);
        }

        /// <summary>top-down → bottom-up（行序翻转一次）。</summary>
        public static Color32[] FlipVertical(Color32[] topDown, int w, int h)
        {
            if (w <= 0 || h <= 0 || topDown == null || topDown.Length < w * h) return topDown;
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                Array.Copy(topDown, y * w, pixels, (h - 1 - y) * w, w);
            }
            return pixels;
        }

        /// <summary>
        /// 测试专用：把 top-down 像素编码成 RGBA8 PNG（filter 0，zlib 压缩，逐 chunk CRC32）。
        /// 只服务非对称行序 fixture；不写任何真实资产。
        /// </summary>
        public static byte[] Encode(int w, int h, Color32[] topDown)
        {
            var raw = new byte[h * (1 + w * 4)];
            for (int y = 0; y < h; y++)
            {
                int row = y * (1 + w * 4);
                raw[row] = 0;   // filter type 0
                for (int x = 0; x < w; x++)
                {
                    Color32 c = topDown[y * w + x];
                    raw[row + 1 + x * 4] = c.r;
                    raw[row + 2 + x * 4] = c.g;
                    raw[row + 3 + x * 4] = c.b;
                    raw[row + 4 + x * 4] = c.a;
                }
            }
            byte[] idat;
            using (var ms = new MemoryStream())
            {
                using (var z = new ZLibStream(ms, CompressionLevel.Optimal, true)) z.Write(raw, 0, raw.Length);
                idat = ms.ToArray();
            }
            var outp = new MemoryStream();
            outp.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);
            var ihdr = new byte[13];
            WriteInt(ihdr, 0, w);
            WriteInt(ihdr, 4, h);
            ihdr[8] = 8; ihdr[9] = 6; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;   // 8-bit RGBA
            WriteChunk(outp, "IHDR", ihdr);
            WriteChunk(outp, "IDAT", idat);
            WriteChunk(outp, "IEND", new byte[0]);
            return outp.ToArray();
        }

        private static void WriteInt(byte[] target, int offset, int value)
        {
            target[offset] = (byte)(value >> 24);
            target[offset + 1] = (byte)(value >> 16);
            target[offset + 2] = (byte)(value >> 8);
            target[offset + 3] = (byte)value;
        }

        private static void WriteChunk(Stream stream, string type, byte[] data)
        {
            var len = new byte[4];
            WriteInt(len, 0, data.Length);
            stream.Write(len, 0, 4);
            byte[] typeBytes = Encoding.ASCII.GetBytes(type);
            stream.Write(typeBytes, 0, 4);
            stream.Write(data, 0, data.Length);
            uint crc = Crc32(typeBytes, data);
            var crcBytes = new byte[4];
            crcBytes[0] = (byte)(crc >> 24);
            crcBytes[1] = (byte)(crc >> 16);
            crcBytes[2] = (byte)(crc >> 8);
            crcBytes[3] = (byte)crc;
            stream.Write(crcBytes, 0, 4);
        }

        private static readonly uint[] CrcTable = BuildCrcTable();

        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[n] = c;
            }
            return table;
        }

        private static uint Crc32(byte[] first, byte[] second)
        {
            uint c = 0xFFFFFFFFu;
            for (int i = 0; i < first.Length; i++) c = CrcTable[(c ^ first[i]) & 0xFF] ^ (c >> 8);
            for (int i = 0; i < second.Length; i++) c = CrcTable[(c ^ second[i]) & 0xFF] ^ (c >> 8);
            return c ^ 0xFFFFFFFFu;
        }
    }
}
