// 尾巴编解码（真实生产代码）的字节级用例：布局、拒绝非法载荷、游标语义。

using System;
using KingdomEnhancedMod;

namespace KnightIdentityNetworkTests
{
    internal static class TailCodecTests
    {
        internal static void Run()
        {
            Case.Run("tail: 33 字节布局 magic|version|guid|style|lifetime(LE)", () =>
            {
                MockReset.All();
                Guid id = MockNet.Id(1);
                var buffer = new byte[KnightIdentityNetwork.TailBytes];
                Check.True(KnightIdentityNetwork.WriteTail(buffer, id, 3, 0x0102030405060708L), "write accepted");
                Check.Equal(33, buffer.Length, "tail length constant");
                Check.Equal((byte)0x4B, buffer[0], "magic0 'K'");
                Check.Equal((byte)0x4E, buffer[1], "magic1 'N'");
                Check.Equal((byte)0x49, buffer[2], "magic2 'I'");
                Check.Equal((byte)0x31, buffer[3], "magic3 '1'");
                Check.Equal((byte)1, buffer[4], "schema version");

                byte[] guidBytes = id.ToByteArray();
                for (int i = 0; i < 16; i++) Check.Equal(guidBytes[i], buffer[5 + i], "guid byte " + i);

                Check.Equal(3, buffer[21], "style b0");
                Check.Equal(0, buffer[22], "style b1");
                Check.Equal(0, buffer[23], "style b2");
                Check.Equal(0, buffer[24], "style b3");
                for (int i = 0; i < 8; i++)
                {
                    Check.Equal((byte)(0x0102030405060708L >> (8 * i)), buffer[25 + i], "lifetime byte " + i);
                }
            });

            Case.Run("tail: 空 guid / style 越界 / 负 lifetime / 目标过小 一律拒绝且不改目标", () =>
            {
                MockReset.All();
                var buffer = new byte[KnightIdentityNetwork.TailBytes];
                Check.False(KnightIdentityNetwork.WriteTail(buffer, Guid.Empty, 2, 1), "empty guid rejected");
                Check.False(KnightIdentityNetwork.WriteTail(buffer, MockNet.Id(2), -1, 1), "style -1 rejected");
                Check.False(KnightIdentityNetwork.WriteTail(buffer, MockNet.Id(2), 5, 1), "style 5 rejected");
                Check.False(KnightIdentityNetwork.WriteTail(buffer, MockNet.Id(2), 0, -1), "negative lifetime rejected");

                for (int i = 0; i < buffer.Length; i++) Check.Equal((byte)0, buffer[i], "target untouched at " + i);

                var shortTarget = new byte[KnightIdentityNetwork.TailBytes - 1];
                Check.False(KnightIdentityNetwork.WriteTail(shortTarget, MockNet.Id(2), 0, 1), "short target rejected");
            });

            Case.Run("tail: 写→读 往返保持 guid/style/lifetime", () =>
            {
                MockReset.All();
                Guid id = MockNet.Id(9);
                byte[] payload = MockNet.Tail(id, 4, 987654321L);
                Check.True(KnightIdentityNetwork.TryReadTail(payload, out Guid readId, out int style, out long lifetime), "read ok");
                Check.Equal(id, readId, "guid");
                Check.Equal(4, style, "style");
                Check.Equal(987654321L, lifetime, "lifetime");
            });

            Case.Run("tail: 非法 magic/version/style/guid/lifetime 一律 false", () =>
            {
                MockReset.All();
                byte[] good = MockNet.Tail(MockNet.Id(3), 2, 7L);

                byte[] foreign = (byte[])good.Clone();
                foreign[0] = 0x4B; foreign[1] = 0x48; foreign[2] = 0x4D; foreign[3] = 0x31; // 'KHM1'
                Check.False(KnightIdentityNetwork.TryReadTail(foreign, out _, out _, out _), "foreign magic");

                byte[] wrongVersion = (byte[])good.Clone();
                wrongVersion[4] = 2;
                Check.False(KnightIdentityNetwork.TryReadTail(wrongVersion, out _, out _, out _), "unknown version");

                byte[] badStyle = (byte[])good.Clone();
                badStyle[21] = 9;
                Check.False(KnightIdentityNetwork.TryReadTail(badStyle, out _, out _, out _), "style out of range");

                byte[] emptyGuid = (byte[])good.Clone();
                for (int i = 5; i < 21; i++) emptyGuid[i] = 0;
                Check.False(KnightIdentityNetwork.TryReadTail(emptyGuid, out _, out _, out _), "empty guid");

                byte[] negative = (byte[])good.Clone();
                negative[32] = 0x80; // 最高字节置 1 → 负数
                Check.False(KnightIdentityNetwork.TryReadTail(negative, out _, out _, out _), "negative lifetime");

                var truncated = new byte[KnightIdentityNetwork.TailBytes - 1];
                Array.Copy(good, truncated, truncated.Length);
                Check.False(KnightIdentityNetwork.TryReadTail(truncated, out _, out _, out _), "truncated payload");

                Check.False(KnightIdentityNetwork.TryReadTail(good, out Guid parsed, out int parsedStyle, out long parsedLifetime) == false, "sanity");
            });

            Case.Run("tail: 更长载荷只取前 33 字节（后续 mod 尾巴不被吞）", () =>
            {
                MockReset.All();
                Guid id = MockNet.Id(11);
                byte[] payload = MockNet.Concat(MockNet.Tail(id, 1, 42L), MockNet.ForeignTail());
                Check.True(KnightIdentityNetwork.TryReadTail(payload, out Guid readId, out int style, out long lifetime), "read ok");
                Check.Equal(id, readId, "guid");
                Check.Equal(1, style, "style");
                Check.Equal(42L, lifetime, "lifetime");
            });

            Case.Run("packet: 请求 14 字节 / 响应 47 字节 布局与往返", () =>
            {
                MockReset.All();
                long nonce = 0x0102030405060708L;
                var request = new byte[KnightIdentityNetwork.RequestBytes];
                Check.True(KnightIdentityNetwork.WriteRequest(request, nonce), "request write");
                Check.Equal(14, request.Length, "request length");
                Check.Equal((byte)0x4B, request[0], "magic0");
                Check.Equal((byte)0x4E, request[1], "magic1");
                Check.Equal((byte)0x49, request[2], "magic2");
                Check.Equal((byte)0x31, request[3], "magic3");
                Check.Equal((byte)1, request[4], "packet version");
                Check.Equal(KnightIdentityNetwork.KindRequest, request[5], "kind=1");
                for (int i = 0; i < 8; i++)
                {
                    Check.Equal((byte)(nonce >> (8 * i)), request[6 + i], "nonce byte " + i);
                }
                Check.True(KnightIdentityNetwork.TryReadRequest(request, out long readNonce), "request round trip");
                Check.Equal(nonce, readNonce, "nonce preserved");
                Check.False(KnightIdentityNetwork.WriteRequest(request, 0L), "zero nonce rejected");

                Guid id = MockNet.Id(12);
                var response = new byte[KnightIdentityNetwork.ResponseBytes];
                Check.True(KnightIdentityNetwork.WriteResponse(response, nonce, id, 3, 0x0A0B0C0DL), "response write");
                Check.Equal(47, response.Length, "response length");
                Check.Equal(KnightIdentityNetwork.KindResponse, response[5], "kind=2");
                Check.True(KnightIdentityNetwork.TryReadResponse(response, out long echo, out Guid rid, out int rstyle, out long rlife), "response round trip");
                Check.Equal(nonce, echo, "echo nonce");
                Check.Equal(id, rid, "guid");
                Check.Equal(3, rstyle, "style");
                Check.Equal(0x0A0B0C0DL, rlife, "host lifetime");
                Check.False(KnightIdentityNetwork.TryReadRequest(response, out _), "response is not a request");
            });

            Case.Run("packet: 非法 magic/version/kind/长度/越界 style 一律拒绝", () =>
            {
                MockReset.All();
                long nonce = 7L;
                byte[] request = MockNet.Request(nonce);

                byte[] foreign = (byte[])request.Clone();
                foreign[0] = 0x4B; foreign[1] = 0x48; foreign[2] = 0x4D; foreign[3] = 0x31;
                Check.False(KnightIdentityNetwork.TryReadRequest(foreign, out _), "foreign magic");

                byte[] wrongVersion = (byte[])request.Clone();
                wrongVersion[4] = 2;
                Check.False(KnightIdentityNetwork.TryReadRequest(wrongVersion, out _), "unknown packet version");

                byte[] wrongKind = (byte[])request.Clone();
                wrongKind[5] = 9;
                Check.False(KnightIdentityNetwork.TryReadRequest(wrongKind, out _), "unknown kind");

                byte[] zeroNonce = (byte[])request.Clone();
                for (int i = 6; i < 14; i++) zeroNonce[i] = 0;
                Check.False(KnightIdentityNetwork.TryReadRequest(zeroNonce, out _), "zero nonce");

                var truncated = new byte[KnightIdentityNetwork.RequestBytes - 1];
                Array.Copy(request, truncated, truncated.Length);
                Check.False(KnightIdentityNetwork.TryReadRequest(truncated, out _), "truncated request");

                Guid id = MockNet.Id(13);
                byte[] response = MockNet.Response(nonce, id, 2, 4);
                byte[] badStyle = (byte[])response.Clone();
                badStyle[KnightIdentityNetwork.RequestBytes + 21] = 9;
                Check.False(KnightIdentityNetwork.TryReadResponse(badStyle, out _, out _, out _, out _), "style out of range");

                byte[] shortResponse = new byte[KnightIdentityNetwork.ResponseBytes - 1];
                Array.Copy(response, shortResponse, shortResponse.Length);
                Check.False(KnightIdentityNetwork.TryReadResponse(shortResponse, out _, out _, out _, out _), "truncated response");
            });

            Case.Run("tail: 协议常量与原生槽位上限一致（防协议漂移）", () =>
            {
                MockReset.All();
                Check.Equal(33, KnightIdentityNetwork.TailBytes, "33 byte tail");
                Check.Equal(14, KnightIdentityNetwork.RequestBytes, "14 byte request");
                Check.Equal(47, KnightIdentityNetwork.ResponseBytes, "47 byte response");
                Check.Equal(16, KnightIdentityNetwork.MaxSendsPerPass, "16 per pass");
                Check.Equal((byte)1, KnightIdentityNetwork.KindRequest, "kind request");
                Check.Equal((byte)2, KnightIdentityNetwork.KindResponse, "kind response");
                Check.Equal((byte)0x4B, KnightIdentityNetwork.TailMagicBytes[0], "'K'");
                Check.Equal((byte)0x4E, KnightIdentityNetwork.TailMagicBytes[1], "'N'");
                Check.Equal((byte)0x49, KnightIdentityNetwork.TailMagicBytes[2], "'I'");
                Check.Equal((byte)0x31, KnightIdentityNetwork.TailMagicBytes[3], "'1'");
                Check.Equal(KnightIdentityNetwork.TailMagic, 0x31494E4B, "magic int");
            });
        }
    }
}
