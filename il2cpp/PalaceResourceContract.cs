// 付前资源契约的最小纯 BCL helper（source-only）。
//
// 边界（activation r3 §E）：
// - 零 Unity 对象/零解码/零贴图创建/零对象加载行为变更；只做有界读取、PNG IHDR 尺寸与 SHA256 全量比对。
// - literals-first：本文件不携带任何资源 hash 字面值。契约由各 loader 自己的字面表导出
//   （PalaceEntryArt 尺寸常量、PalaceScrollScene Layers、PalaceDepthScene Specs/LocalFrameHashes、
//   PalaceWorldExpansion actors manifest 解析、PalaceTerrainPalette 源 PNG 尺寸常量），
//   Root 后续更新 loader literal 时 precheck 自动消费同一契约，不存在第二权威表。
// - 失败一律 false + reason（不抛给调用方）；所有读取都有调用方给定的字节上界。
using System;
using System.IO;
using System.Security.Cryptography;

namespace KingdomEnhancedMod;

/// <summary>一条固定 profile 资源契约（纯数据；不含 Unity 对象）。</summary>
internal sealed class PalaceResourceContract
{
    internal readonly string LogicalName; // 完整 embedded LogicalName（精确相等，不接受前缀/别名）
    internal readonly string Sha256Hex;   // 已有字面 hash；null = 尚无 literal（literals-first 缺口）
    internal readonly int Width;          // 0 = 未冻结尺寸（由来源/manifest 决定）
    internal readonly int Height;
    internal readonly long MaxBytes;      // 与 loader 同域的字节上界
    internal readonly string Source;      // 契约来源（诊断/日志）

    internal PalaceResourceContract(string logicalName, string sha256Hex, int width, int height,
        long maxBytes, string source)
    {
        LogicalName = logicalName;
        Sha256Hex = sha256Hex ?? PalaceResourceHashGaps.Resolve(logicalName);
        Width = width;
        Height = height;
        MaxBytes = maxBytes;
        Source = source ?? "";
    }
}

/// <summary>有界读取 / PNG IHDR / SHA256 纯函数（只读；全部 total，异常转 false）。</summary>
internal static class PalaceResourceBytes
{
    private const int SignatureBytes = 8;
    private const int IhdrTypeOffset = 12;    // 8 签名 + 4 chunk 长度
    private const int IhdrWidthOffset = 16;
    private const int IhdrHeightOffset = 20;
    private const int IhdrMinBytes = 24;
    private const int IhdrChunkLength = 13;

    /// <summary>按 maxBytes 有界读满整个流；长度非法/截断/异常一律 false。</summary>
    internal static bool TryReadBounded(Stream stream, long maxBytes, out byte[] bytes, out string reason)
    {
        bytes = null;
        reason = "";
        if (stream == null) { reason = "stream-null"; return false; }
        try
        {
            long length = stream.Length;
            if (length <= 0 || length > maxBytes) { reason = "size:" + length + "/" + maxBytes; return false; }
            byte[] buffer = new byte[checked((int)length)];
            int read = 0;
            while (read < buffer.Length)
            {
                int step = stream.Read(buffer, read, buffer.Length - read);
                if (step <= 0) { reason = "truncated:" + read + "/" + buffer.Length; return false; }
                read += step;
            }
            bytes = buffer;
            return true;
        }
        catch (Exception e) { reason = "read-fault:" + e.GetType().Name; return false; }
    }

    /// <summary>PNG 签名 + 第一个 chunk 必须是 IHDR（长度 13）+ big-endian 尺寸；不解码像素。</summary>
    internal static bool TryReadPngSize(byte[] bytes, out int width, out int height, out string reason)
    {
        width = 0;
        height = 0;
        reason = "";
        if (bytes == null || bytes.Length < IhdrMinBytes)
        { reason = "png-truncated:" + (bytes != null ? bytes.Length : -1); return false; }
        if (bytes[0] != 0x89 || bytes[1] != 0x50 || bytes[2] != 0x4E || bytes[3] != 0x47
            || bytes[4] != 0x0D || bytes[5] != 0x0A || bytes[6] != 0x1A || bytes[7] != 0x0A)
        { reason = "png-signature"; return false; }
        if (bytes[8] != 0 || bytes[9] != 0 || bytes[10] != 0 || bytes[11] != IhdrChunkLength)
        { reason = "png-first-chunk-not-ihdr13"; return false; }
        if (bytes[IhdrTypeOffset] != 'I' || bytes[IhdrTypeOffset + 1] != 'H'
            || bytes[IhdrTypeOffset + 2] != 'D' || bytes[IhdrTypeOffset + 3] != 'R')
        { reason = "png-ihdr-type"; return false; }
        width = (bytes[IhdrWidthOffset] << 24) | (bytes[IhdrWidthOffset + 1] << 16)
            | (bytes[IhdrWidthOffset + 2] << 8) | bytes[IhdrWidthOffset + 3];
        height = (bytes[IhdrHeightOffset] << 24) | (bytes[IhdrHeightOffset + 1] << 16)
            | (bytes[IhdrHeightOffset + 2] << 8) | bytes[IhdrHeightOffset + 3];
        if (width <= 0 || height <= 0) { reason = "png-size:" + width + "x" + height; return false; }
        return true;
    }

    /// <summary>全量 SHA256 比对（64 hex，大小写不敏感；绝不做前缀/部分比较）。</summary>
    internal static bool MatchesSha256(byte[] bytes, string expectedHex, out string reason)
    {
        reason = "";
        if (bytes == null) { reason = "bytes-null"; return false; }
        if (string.IsNullOrEmpty(expectedHex) || expectedHex.Length != 64)
        { reason = "hash-expected:" + (expectedHex != null ? expectedHex.Length : -1); return false; }
        string actual = Convert.ToHexString(SHA256.HashData(bytes));
        if (!string.Equals(actual, expectedHex, StringComparison.OrdinalIgnoreCase))
        { reason = "hash:" + actual; return false; }
        return true;
    }
}
