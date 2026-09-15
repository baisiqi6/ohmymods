// 握手接收侧：响应校验与采纳、旧 nonce/旧 life/换绑拒绝、catchup 只消费不采纳、游标安全、补丁接线。

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using KingdomEnhancedMod;
using UnityEngine;

namespace KnightIdentityNetworkTests
{
    internal static class ReceiveTests
    {
        private static Knight ClientKnightWithSlot(out CRPCHeader header)
        {
            MockNet.Client();
            Knight knight = MockScene.SpawnKnight();
            header = MockScene.Register(knight);
            return knight;
        }

        internal static void Run()
        {
            Case.Run("完整握手往返：请求 → 响应 → 客户端采纳（按当前 life 快照）", () =>
            {
                MockReset.All();
                MockNet.Client();
                Knight knight = MockScene.SpawnKnight();
                CRPCHeader header = MockScene.Register(knight);
                Guid id = MockNet.Id(81);
                KnightIdentityRuntime.SetLifetime(knight, 4);

                KnightIdentityNetwork.Sync(new[] { knight });
                Check.Equal(1, MockNet.Deliveries.Count, "client requests");
                Check.True(MockNet.IsRequest(MockNet.Deliveries[0].Payload), "kind=1");
                byte[] request = MockNet.Deliveries[0].Payload;

                MockNet.HostReady();
                KnightIdentityRuntime.Seed(knight, id, 3);
                KnightIdentityRuntime.SetLifetime(knight, 7); // 主机世代
                MockNet.Deliver(header, request); // 回调只入队（无 cached gate，无需先 Sync）
                Check.Equal(1, KnightIdentityNetwork.PendingResponseCount, "host queues the reply");
                KnightIdentityNetwork.Sync(new[] { knight });
                Check.Equal(2, MockNet.Deliveries.Count, "host replies");

                MockNet.Client();
                KnightIdentityRuntime.SetLifetime(knight, 4); // 客户端自己的 life
                MockNet.Deliver(header, MockNet.Deliveries[1].Payload);

                Check.Equal(1, KnightIdentityRuntime.ApplyCalls, "runtime consulted");
                Check.Equal(1, KnightIdentityRuntime.ApplyAccepted, "receipt accepted");
                KnightIdentityRuntime.CurrentReceiptId(knight, out Guid gotId, out int gotStyle);
                Check.Equal(id, gotId, "guid applied");
                Check.Equal(3, gotStyle, "style applied");
                Check.Equal(4L, KnightIdentityRuntime.GetLifetime(knight), "life snapshot is the client's own");

                int sends = MockNet.Deliveries.Count;
                KnightIdentityNetwork.Sync(new[] { knight });
                Check.Equal(sends, MockNet.Deliveries.Count, "confirmed client stops requesting");
            });

            Case.Run("客户端初次 life：旧 nonce 响应被拒绝（不采纳）", () =>
            {
                MockReset.All();
                MockNet.Client();
                Knight knight = MockScene.SpawnKnight();
                CRPCHeader header = MockScene.Register(knight);
                KnightIdentityRuntime.SetLifetime(knight, 1);

                KnightIdentityNetwork.Sync(new[] { knight });
                long nonce = MockNet.NonceOfDelivery(0);

                MockNet.Deliver(header, MockNet.Response(nonce + 1, MockNet.Id(82), 1, 3));

                Check.Equal(0, KnightIdentityRuntime.ApplyCalls, "unknown nonce never reaches the runtime");
                Check.True(MockLog.CountMatching("response-nonce") >= 1, "rejection logged");

                MockNet.Deliver(header, MockNet.Response(nonce, MockNet.Id(82), 1, 3));
                Check.Equal(1, KnightIdentityRuntime.ApplyCalls, "pending nonce accepted");
            });

            Case.Run("池复用：旧包先到拒绝，随后合法新包接受", () =>
            {
                MockReset.All();
                MockNet.Client();
                Knight knight = MockScene.SpawnKnight();
                CRPCHeader header = MockScene.Register(knight);
                KnightIdentityRuntime.SetLifetime(knight, 1);

                KnightIdentityNetwork.Sync(new[] { knight });
                long oldNonce = MockNet.NonceOfDelivery(0);

                // 池复用：同一对象新 life，Runtime 清收据；旧响应（旧 nonce/旧 life）先到。
                KnightIdentityRuntime.ClearReceipt(knight);
                KnightIdentityRuntime.SetLifetime(knight, 2);
                MockNet.Deliver(header, MockNet.Response(oldNonce, MockNet.Id(83), 1, 9));
                Check.Equal(0, KnightIdentityRuntime.ApplyCalls, "stale response rejected after reuse");

                KnightIdentityNetwork.Sync(new[] { knight });
                long newNonce = MockNet.NonceOfDelivery(1);
                Check.NotEqual(oldNonce, newNonce, "new life => new nonce");

                Guid fresh = MockNet.Id(84);
                MockNet.Deliver(header, MockNet.Response(newNonce, fresh, 2, 11));
                Check.Equal(1, KnightIdentityRuntime.ApplyCalls, "fresh response applied");
                KnightIdentityRuntime.CurrentReceiptId(knight, out Guid gotId, out _);
                Check.Equal(fresh, gotId, "new identity applied");
            });

            Case.Run("Flush：旧槽 inert，且旧 nonce 在新 binding 世代下被拒绝", () =>
            {
                MockReset.All();
                MockNet.Client();
                Knight knight = MockScene.SpawnKnight();
                CRPCHeader header = MockScene.Register(knight);
                KnightIdentityRuntime.SetLifetime(knight, 3);

                KnightIdentityNetwork.Sync(new[] { knight });
                long staleNonce = MockNet.NonceOfDelivery(0);
                NetworkPostbox.DynAction retiredSlot = header.RemoteMethodList[0];

                header.Flush();
                KnightIdentityNetwork.HandleHeaderFlushed(header);
                Check.Equal(0, KnightIdentityNetwork.TrackedBindingCount, "ownership released");

                ByteBuffer.FeedForRead(MockNet.Response(staleNonce, MockNet.Id(85), 1, 1));
                retiredSlot.Invoke();
                Check.Equal(0, KnightIdentityRuntime.ApplyCalls, "flushed slot is inert");

                MockScene.Register(knight); // 原生重建 → 新 binding 世代
                MockNet.Deliver(header, MockNet.Response(staleNonce, MockNet.Id(85), 1, 1));
                Check.Equal(0, KnightIdentityRuntime.ApplyCalls, "old nonce rejected in the new binding generation");

                KnightIdentityNetwork.Sync(new[] { knight });
                long freshNonce = MockNet.NonceOfDelivery(1);
                MockNet.Deliver(header, MockNet.Response(freshNonce, MockNet.Id(85), 1, 1));
                Check.Equal(1, KnightIdentityRuntime.ApplyCalls, "fresh nonce works");
            });

            Case.Run("主机不采纳响应；客户端不响应请求", () =>
            {
                MockReset.All();
                MockNet.HostReady();
                Knight knight = MockScene.SpawnKnight();
                CRPCHeader header = MockScene.Register(knight);
                KnightIdentityRuntime.SetLifetime(knight, 1);
                KnightIdentityNetwork.Sync(new[] { knight });

                MockNet.Deliver(header, MockNet.Response(1234L, MockNet.Id(86), 1, 1));
                Check.Equal(0, KnightIdentityRuntime.ApplyCalls, "host never adopts receipts");
                Check.True(MockLog.CountMatching("response-on-host") >= 1, "host rejection logged");

                MockNet.Client();
                KnightIdentityNetwork.Sync(new[] { knight });
                int clientSends = MockNet.Deliveries.Count;
                MockNet.Deliver(header, MockNet.Request(4321L));
                Check.Equal(0, KnightIdentityNetwork.PendingResponseCount, "client never queues responses");
                Check.Equal(clientSends, MockNet.Deliveries.Count, "client never answers a request");
            });

            Case.Run("损坏包 / 未知 kind：不消费、不采纳", () =>
            {
                MockReset.All();
                Knight knight = ClientKnightWithSlot(out CRPCHeader header);

                ByteBuffer.FeedForRead(MockNet.NativePrefix(20, 0x70));
                header.RemoteMethodList[header.RemoteMethodList.Count - 1].Invoke();
                Check.Equal(0, ByteBuffer.PollIndex(), "garbage not consumed");
                Check.Equal(20, ByteBuffer.PollDataAvailableLength(), "garbage preserved");
                Check.Equal(0, KnightIdentityRuntime.ApplyCalls, "nothing applied");

                byte[] unknownKind = MockNet.Request(7L);
                unknownKind[5] = 3; // kind 字节
                ByteBuffer.FeedForRead(unknownKind);
                header.RemoteMethodList[header.RemoteMethodList.Count - 1].Invoke();
                Check.Equal(0, ByteBuffer.PollIndex(), "unknown kind not consumed");
                Check.True(MockLog.CountMatching("packet-invalid") >= 1, "invalid packet logged");
            });

            Case.Run("响应结构非法（style 越界）：不消费不采纳", () =>
            {
                MockReset.All();
                Knight knight = ClientKnightWithSlot(out CRPCHeader header);
                KnightIdentityRuntime.SetLifetime(knight, 1);
                KnightIdentityNetwork.Sync(new[] { knight });
                long nonce = MockNet.NonceOfDelivery(0);

                byte[] response = MockNet.Response(nonce, MockNet.Id(87), 1, 1);
                response[KnightIdentityNetwork.RequestBytes + 21] = 9; // tail 的 style 字节
                ByteBuffer.FeedForRead(response);
                header.RemoteMethodList[header.RemoteMethodList.Count - 1].Invoke();

                Check.Equal(0, ByteBuffer.PollIndex(), "structurally invalid response not consumed");
                Check.Equal(0, KnightIdentityRuntime.ApplyCalls, "nothing applied");
            });

            Case.Run("响应来自别的 world 场景：不采纳", () =>
            {
                MockReset.All();
                Knight knight = ClientKnightWithSlot(out CRPCHeader header);
                KnightIdentityRuntime.SetLifetime(knight, 1);
                KnightIdentityNetwork.Sync(new[] { knight });
                long nonce = MockNet.NonceOfDelivery(0);

                knight.gameObject.scene.handle = MockScene.OtherSceneHandle;
                MockNet.Deliver(header, MockNet.Response(nonce, MockNet.Id(88), 1, 1));

                Check.Equal(0, KnightIdentityRuntime.ApplyCalls, "foreign scene is fail-closed");
                Check.True(MockLog.CountMatching("packet-world") >= 1, "world rejection logged");
            });

            Case.Run("Runtime 拒绝应用：不崩溃、不落身份、同 nonce 可重试", () =>
            {
                MockReset.All();
                Knight knight = ClientKnightWithSlot(out CRPCHeader header);
                KnightIdentityRuntime.SetLifetime(knight, 2);
                Guid id = MockNet.Id(89);

                KnightIdentityNetwork.Sync(new[] { knight });
                long nonce = MockNet.NonceOfDelivery(0);
                MockNet.Deliver(header, MockNet.Response(nonce, id, 1, 1));
                Check.Equal(1, KnightIdentityRuntime.ApplyCalls, "first response applied");

                // 收据暂时不可见 → 模块重新请求（同 life/同 binding → 同 nonce）
                KnightIdentityRuntime.ClearReceipt(knight);
                KnightIdentityNetwork.Sync(new[] { knight });
                Check.Equal(nonce, MockNet.NonceOfDelivery(1), "same life keeps the nonce");

                // Runtime 侧判定拒绝（冲突 GUID / 非权威 world 之类）
                KnightIdentityRuntime.RejectApply = (k, r) => true;
                MockNet.Deliver(header, MockNet.Response(nonce, id, 1, 1));
                Check.Equal(2, KnightIdentityRuntime.ApplyCalls, "rejection surfaced to the runtime");
                Check.Equal(1, KnightIdentityRuntime.ApplyRejected, "runtime refusal honoured");
                Check.True(MockLog.CountMatching("response-rejected") >= 1, "refusal logged");
                KnightIdentityRuntime.CurrentReceiptId(knight, out Guid gotId, out _);
                Check.Equal(Guid.Empty, gotId, "no identity written");

                // 解除拒绝后同 nonce 重试即可（模块状态未被污染）
                KnightIdentityRuntime.RejectApply = null;
                KnightIdentityNetwork.Sync(new[] { knight });
                Check.Equal(nonce, MockNet.NonceOfDelivery(2), "retry keeps the nonce");
                MockNet.Deliver(header, MockNet.Response(nonce, id, 1, 1));
                Check.Equal(2, KnightIdentityRuntime.ApplyAccepted, "retry accepted");
                KnightIdentityRuntime.CurrentReceiptId(knight, out gotId, out _);
                Check.Equal(id, gotId, "identity applied after retry");
            });

            Case.Run("catchup 尾巴：校验后消费但不采纳（等握手；旧 tail 不能抢占）", () =>
            {
                MockReset.All();
                Knight knight = ClientKnightWithSlot(out _);
                KnightIdentityRuntime.SetLifetime(knight, 5);
                Guid id = MockNet.Id(91);
                byte[] native = MockNet.NativePrefix(5, 0x10);

                ByteBuffer.FeedForRead(MockNet.Concat(native, MockNet.Tail(id, 2, 7), MockNet.ForeignTail()));
                for (int i = 0; i < native.Length; i++) Check.Equal(native[i], ByteBuffer.ReadByte(), "native byte preserved " + i);

                KnightIdentityNetwork.HandleNativeDeserialize(knight);

                Check.Equal(38, ByteBuffer.PollIndex(), "own tail consumed for cursor alignment");
                Check.Equal(30, ByteBuffer.PollDataAvailableLength(), "foreign tail preserved");
                Check.Equal((byte)0x4B, ByteBuffer.ReadByte(), "foreign tail still readable");
                Check.Equal(0, KnightIdentityRuntime.ApplyCalls, "catchup tail never adopts identity");
                Check.True(TryGetReceipt(knight) == false, "runtime still has no receipt");
            });

            Case.Run("catchup：legacy 无尾巴 / 外来尾巴 一字节都不动", () =>
            {
                MockReset.All();
                Knight knight = ClientKnightWithSlot(out _);

                ByteBuffer.FeedForRead(MockNet.NativePrefix(8, 0x30));
                KnightIdentityNetwork.HandleNativeDeserialize(knight);
                Check.Equal(0, ByteBuffer.PollIndex(), "legacy payload untouched");

                ByteBuffer.FeedForRead(MockNet.Concat(MockNet.NativePrefix(4, 0x40), MockNet.ForeignTail()));
                KnightIdentityNetwork.HandleNativeDeserialize(knight);
                Check.Equal(0, ByteBuffer.PollIndex(), "foreign tail untouched");
                Check.Equal(34, ByteBuffer.PollDataAvailableLength(), "foreign payload preserved");
                Check.Equal(0, KnightIdentityRuntime.ApplyCalls, "nothing applied");
            });

            Case.Run("握手包不污染既有原生游标（只消费自己的 47 字节）", () =>
            {
                MockReset.All();
                Knight knight = ClientKnightWithSlot(out CRPCHeader header);
                KnightIdentityRuntime.SetLifetime(knight, 1);
                KnightIdentityNetwork.Sync(new[] { knight });
                long nonce = MockNet.NonceOfDelivery(0);

                ByteBuffer.FeedForRead(MockNet.NativePrefix(3, 0x60));
                Check.Equal((byte)0x60, ByteBuffer.ReadByte(), "native read 0");
                Check.Equal((byte)0x61, ByteBuffer.ReadByte(), "native read 1");
                Check.Equal((byte)0x62, ByteBuffer.ReadByte(), "native read 2");
                ByteBuffer.AppendForRead(MockNet.Response(nonce, MockNet.Id(92), 2, 4));

                header.RemoteMethodList[header.RemoteMethodList.Count - 1].Invoke();

                Check.Equal(50, ByteBuffer.PollIndex(), "advanced exactly past the response");
                Check.Equal(0, ByteBuffer.PollDataAvailableLength(), "nothing left");
            });

            Case.Run("序列化：原生字段之后追加 33 字节尾巴（不动 native out 参数）", () =>
            {
                MockReset.All();
                MockNet.HostReady();
                Knight knight = MockScene.SpawnKnight("Knight", true, false, 0);
                Guid id = MockNet.Id(93);
                KnightIdentityRuntime.Seed(knight, id, 3);
                KnightIdentityRuntime.SetLifetime(knight, 8);

                ByteBuffer.Write(0xAA);
                ByteBuffer.Write(0xBB);
                KnightIdentityNetwork.HandleNativeSerialization(knight);

                byte[] written = ByteBuffer.WrittenBytes();
                Check.Equal(35, written.Length, "native 2 bytes + 33 byte tail");
                Check.Equal((byte)0xAA, written[0], "native byte 0 preserved");
                Check.Equal((byte)0xBB, written[1], "native byte 1 preserved");
                var tail = new byte[KnightIdentityNetwork.TailBytes];
                Array.Copy(written, 2, tail, 0, tail.Length);
                Check.True(KnightIdentityNetwork.TryReadTail(tail, out Guid readId, out int style, out long lifetime), "tail decodes");
                Check.Equal(id, readId, "guid");
                Check.Equal(3, style, "style");
                Check.Equal(8L, lifetime, "local lifetime of the current generation");

                knight.GetSerializationData(out Il2CppSystem.Type nativeOut);
                Check.True(nativeOut == null, "native out parameter untouched");
            });

            Case.Run("序列化：无收据 / Runtime 抛错时一个字节都不追加", () =>
            {
                MockReset.All();
                MockNet.HostReady();
                Knight knight = MockScene.SpawnKnight("Knight", true, false, 0);

                KnightIdentityNetwork.HandleNativeSerialization(knight);
                Check.Equal(0, ByteBuffer.WrittenBytes().Length, "nothing appended without a receipt");

                KnightIdentityRuntime.Seed(knight, MockNet.Id(94), 1);
                KnightIdentityRuntime.ThrowOnGet = true;
                KnightIdentityNetwork.HandleNativeSerialization(knight);
                Check.Equal(0, ByteBuffer.WrittenBytes().Length, "runtime failure never corrupts native payload");
            });

            Case.Run("Harmony 补丁只钉在已审计的 4 个原生方法上", () =>
            {
                MockReset.All();
                var targets = new List<string>();
                foreach (Type nested in typeof(KnightIdentityNetwork).GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public))
                {
                    var attributes = nested.GetCustomAttributes(typeof(HarmonyPatchAttribute), false);
                    for (int i = 0; i < attributes.Length; i++)
                    {
                        var patch = (HarmonyPatchAttribute)attributes[i];
                        targets.Add(patch.TargetType.Name + "." + patch.MethodName);
                    }
                }

                Check.Equal(4, targets.Count, "exactly four audited patch targets");
                Check.True(targets.Contains("Knight.GetSerializationData"), "serialize hook");
                Check.True(targets.Contains("Knight.DeserializeFromData"), "deserialize hook");
                Check.True(targets.Contains("CRPCHeader.RegisterComponents"), "register hook");
                Check.True(targets.Contains("CRPCHeader.Flush"), "flush hook");
            });
        }

        private static bool TryGetReceipt(Knight knight)
        {
            return KnightIdentityRuntime.TryGetReceipt(knight, out KnightIdentityReceipt receipt) && receipt.IsValid;
        }
    }
}
