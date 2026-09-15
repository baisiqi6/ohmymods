// 握手发送侧：客户端请求（nonce 生命周期、闸门、16 预算）、主机响应队列（延迟发送、去重、有界、异常隔离）。

using System;
using System.Collections.Generic;
using KingdomEnhancedMod;
using UnityEngine;

namespace KnightIdentityNetworkTests
{
    internal static class SendBudgetTests
    {
        private static Knight ClientKnight()
        {
            Knight knight = MockScene.SpawnKnight();
            MockScene.Register(knight);
            return knight;
        }

        internal static void Run()
        {
            Case.Run("客户端未注册不发请求；注册后发出 14 字节请求", () =>
            {
                MockReset.All();
                MockNet.Client();
                Knight knight = MockScene.SpawnKnight("Knight", true, false, 0); // 还没有 header
                KnightIdentityRuntime.SetLifetime(knight, 1);

                KnightIdentityNetwork.Sync(new[] { knight });
                Check.Equal(0, MockNet.Deliveries.Count, "no request without a registered header");

                MockScene.AttachHeader(knight);
                MockScene.Register(knight);
                KnightIdentityNetwork.Sync(new[] { knight });

                Check.Equal(1, MockNet.Deliveries.Count, "request sent after registration");
                Check.Equal(14, MockNet.Deliveries[0].Payload.Length, "request packet length");
                Check.True(MockNet.IsRequest(MockNet.Deliveries[0].Payload), "kind=1 request");
                Check.True(MockNet.NonceOfDelivery(0) != 0, "nonce is non-zero");
                Check.Equal(0, KnightIdentityNetwork.PendingResponseCount, "client queues no responses");
            });

            Case.Run("客户端未追平不发请求", () =>
            {
                MockReset.All();
                MockNet.Client();
                MockNet.NotCaughtUp();
                Knight knight = ClientKnight();
                KnightIdentityRuntime.SetLifetime(knight, 1);

                KnightIdentityNetwork.Sync(new[] { knight });
                Check.Equal(0, MockNet.Deliveries.Count, "nothing sent before catch-up");

                NetworkBigBoss.HasClientCaughtUp = true;
                KnightIdentityNetwork.Sync(new[] { knight });
                Check.Equal(1, MockNet.Deliveries.Count, "sent once caught up");
            });

            Case.Run("同 life 同 binding 周期重试：nonce 不变", () =>
            {
                MockReset.All();
                MockNet.Client();
                Knight knight = ClientKnight();
                KnightIdentityRuntime.SetLifetime(knight, 3);

                KnightIdentityNetwork.Sync(new[] { knight });
                KnightIdentityNetwork.Sync(new[] { knight });
                KnightIdentityNetwork.Sync(new[] { knight });

                Check.Equal(3, MockNet.Deliveries.Count, "retries every pass while unconfirmed");
                long first = MockNet.NonceOfDelivery(0);
                Check.Equal(first, MockNet.NonceOfDelivery(1), "retry keeps the nonce");
                Check.Equal(first, MockNet.NonceOfDelivery(2), "retry keeps the nonce");
            });

            Case.Run("每拍最多 16 条请求，且轮转不饿死后面的对象", () =>
            {
                MockReset.All();
                MockNet.Client();
                var knights = new Knight[20];
                for (int i = 0; i < knights.Length; i++)
                {
                    knights[i] = ClientKnight();
                    KnightIdentityRuntime.SetLifetime(knights[i], 1);
                }

                KnightIdentityNetwork.Sync(knights);
                Check.Equal(16, MockNet.Deliveries.Count, "first pass capped at 16");

                KnightIdentityNetwork.Sync(knights);
                Check.Equal(32, MockNet.Deliveries.Count, "second pass sends another 16 (all 20 unconfirmed retry)");
                var allNonces = new HashSet<long>();
                for (int i = 0; i < MockNet.Deliveries.Count; i++) allNonces.Add(MockNet.NonceOfDelivery(i));
                Check.Equal(20, allNonces.Count, "every knight got a request across the two passes");
            });

            Case.Run("已有收据（握手完成）不再请求", () =>
            {
                MockReset.All();
                MockNet.Client();
                Knight knight = ClientKnight();
                KnightIdentityRuntime.SetLifetime(knight, 2);
                KnightIdentityRuntime.Seed(knight, MockNet.Id(71), 3); // Runtime 已有本 life 收据

                KnightIdentityNetwork.Sync(new[] { knight });
                KnightIdentityNetwork.Sync(new[] { knight });

                Check.Equal(0, MockNet.Deliveries.Count, "no request while a valid receipt exists");
            });

            Case.Run("life 变化 / binding 换代 立刻换新 nonce", () =>
            {
                MockReset.All();
                MockNet.Client();
                Knight knight = ClientKnight();
                CRPCHeader header = knight.parentHeaderRef;
                KnightIdentityRuntime.SetLifetime(knight, 1);

                KnightIdentityNetwork.Sync(new[] { knight });
                long first = MockNet.NonceOfDelivery(0);

                KnightIdentityRuntime.ClearReceipt(knight);
                KnightIdentityRuntime.SetLifetime(knight, 2); // 新 life
                KnightIdentityNetwork.Sync(new[] { knight });
                long second = MockNet.NonceOfDelivery(1);
                Check.NotEqual(first, second, "new life => new nonce");

                header.Flush();
                KnightIdentityNetwork.HandleHeaderFlushed(header);
                MockScene.Register(knight); // 原生重建列表 → 新 binding 世代
                KnightIdentityNetwork.Sync(new[] { knight });
                long third = MockNet.NonceOfDelivery(2);
                Check.NotEqual(second, third, "new binding generation => new nonce");
            });

            Case.Run("不同 managed wrapper 同 native：握手记录保留（nonce 不变）", () =>
            {
                MockReset.All();
                MockNet.Client();
                Knight knight = ClientKnight();
                KnightIdentityRuntime.SetLifetime(knight, 1);

                KnightIdentityNetwork.Sync(new[] { knight });
                long first = MockNet.NonceOfDelivery(0);

                // 同一个原生对象的新托管包装（IL2CPP 常见）：不是新对象，不得重置握手记录。
                var clone = new Knight
                {
                    gameObject = knight.gameObject,
                    transform = knight.transform,
                    Pointer = knight.Pointer,
                    tag = "Knight",
                    parentHeaderRef = knight.parentHeaderRef,
                };
                Check.False(ReferenceEquals(clone, knight), "different managed wrapper");

                KnightIdentityNetwork.Sync(new[] { clone });
                Check.Equal(1, KnightIdentityNetwork.TrackedStateCount, "single tracked target");
                Check.Equal(first, MockNet.NonceOfDelivery(1), "wrapper swap keeps the pending nonce");
            });

            Case.Run("同一 GO 上换 Knight 组件实例（同 GOID 不同 Knight.Pointer）：握手记录重置", () =>
            {
                MockReset.All();
                MockNet.Client();
                Knight knight = ClientKnight();
                GameObject go = knight.gameObject;
                KnightIdentityRuntime.SetLifetime(knight, 1);

                KnightIdentityNetwork.Sync(new[] { knight });
                long first = MockNet.NonceOfDelivery(0);

                var replacement = go.AddComponent<Knight>(); // 同 GOID/同 GO.Pointer，新的组件指针
                replacement.tag = "Knight";
                replacement.parentHeaderRef = knight.parentHeaderRef;

                KnightIdentityNetwork.Sync(new[] { replacement });
                long second = MockNet.NonceOfDelivery(1);
                Check.NotEqual(first, second, "native target changed => handshake memory reset");
            });

            Case.Run("主机收到请求只入队，绝不在回调里写 ByteBuffer；下一次 Sync 发 47 字节响应", () =>
            {
                MockReset.All();
                MockNet.HostReady();
                Knight knight = MockScene.SpawnKnight();
                CRPCHeader header = MockScene.Register(knight);
                Guid id = MockNet.Id(72);
                KnightIdentityRuntime.Seed(knight, id, 2);
                KnightIdentityRuntime.SetLifetime(knight, 6);
                KnightIdentityNetwork.Sync(new[] { knight }); // 建立 host gate

                ByteBuffer.Write(0xAA);
                ByteBuffer.Write(0xBB); // 模拟原生正在写的字段
                MockNet.Deliver(header, MockNet.Request(4242L));

                Check.Equal(1, KnightIdentityNetwork.PendingResponseCount, "request queued");
                Check.Equal(0, MockNet.Deliveries.Count, "nothing sent from the receive callback");
                Check.Equal(2, ByteBuffer.WrittenBytes().Length, "receive callback never touches the write buffer");
                Check.Equal((byte)0xAA, ByteBuffer.WrittenBytes()[0], "native byte 0 preserved");

                KnightIdentityNetwork.Sync(new[] { knight });
                Check.Equal(1, MockNet.Deliveries.Count, "response sent on the next Sync");
                Check.Equal(0, KnightIdentityNetwork.PendingResponseCount, "queue drained");

                byte[] response = MockNet.Deliveries[0].Payload;
                Check.Equal(47, response.Length, "response = 14 byte prefix + 33 byte receipt tail");
                Check.True(KnightIdentityNetwork.TryReadResponse(response, out long echo, out Guid rid, out int rstyle, out long rlife), "response decodes");
                Check.Equal(4242L, echo, "echo nonce");
                Check.Equal(id, rid, "receipt guid");
                Check.Equal(2, rstyle, "receipt style");
                Check.Equal(6L, rlife, "host lifetime");
            });

            Case.Run("无收据主机不响应；客户端下一拍重试；有收据后响应", () =>
            {
                MockReset.All();
                MockNet.Client();
                Knight knight = ClientKnight();
                CRPCHeader header = knight.parentHeaderRef;
                Guid id = MockNet.Id(73);
                KnightIdentityRuntime.SetLifetime(knight, 1);

                KnightIdentityNetwork.Sync(new[] { knight });
                byte[] firstRequest = MockNet.Deliveries[0].Payload;

                MockNet.HostReady();
                KnightIdentityNetwork.Sync(new[] { knight }); // 建立 host gate（无收据）
                MockNet.Deliver(header, firstRequest);
                Check.Equal(0, KnightIdentityNetwork.PendingResponseCount, "host without receipt does not respond");

                MockNet.Client();
                KnightIdentityNetwork.Sync(new[] { knight });
                Check.Equal(2, MockNet.Deliveries.Count, "client retries on the next pass");
                Check.Equal(MockNet.NonceOfDelivery(0), MockNet.NonceOfDelivery(1), "retry keeps the nonce");

                MockNet.HostReady();
                KnightIdentityRuntime.Seed(knight, id, 4);
                KnightIdentityNetwork.Sync(new[] { knight });
                MockNet.Deliver(header, MockNet.Deliveries[1].Payload);
                Check.Equal(1, KnightIdentityNetwork.PendingResponseCount, "host with receipt queues the response");
                KnightIdentityNetwork.Sync(new[] { knight });
                Check.Equal(3, MockNet.Deliveries.Count, "response sent");
            });

            Case.Run("主机 gate 关闭（未追平）不响应", () =>
            {
                MockReset.All();
                MockNet.HostReady();
                NetworkBigBoss.HasClientCaughtUp = false;
                Knight knight = MockScene.SpawnKnight();
                CRPCHeader header = MockScene.Register(knight);
                KnightIdentityRuntime.Seed(knight, MockNet.Id(74), 1);
                KnightIdentityRuntime.SetLifetime(knight, 1);
                KnightIdentityNetwork.Sync(new[] { knight });

                MockNet.Deliver(header, MockNet.Request(99L));
                Check.Equal(0, KnightIdentityNetwork.PendingResponseCount, "no queueing while not caught up");
                Check.True(MockLog.CountMatching("request-gate") >= 1, "gate refusal logged");
            });

            Case.Run("重复请求按 binding+nonce 去重", () =>
            {
                MockReset.All();
                MockNet.HostReady();
                Knight knight = MockScene.SpawnKnight();
                CRPCHeader header = MockScene.Register(knight);
                KnightIdentityRuntime.Seed(knight, MockNet.Id(75), 1);
                KnightIdentityRuntime.SetLifetime(knight, 1);
                KnightIdentityNetwork.Sync(new[] { knight });

                byte[] request = MockNet.Request(555L);
                MockNet.Deliver(header, request);
                MockNet.Deliver(header, request);
                MockNet.Deliver(header, MockNet.Request(556L));

                Check.Equal(2, KnightIdentityNetwork.PendingResponseCount, "duplicate nonce deduped, distinct nonce kept");
            });

            Case.Run("响应队列有界（512，溢出丢最旧）", () =>
            {
                MockReset.All();
                MockNet.HostReady();
                Knight knight = MockScene.SpawnKnight();
                CRPCHeader header = MockScene.Register(knight);
                KnightIdentityRuntime.Seed(knight, MockNet.Id(76), 1);
                KnightIdentityRuntime.SetLifetime(knight, 1);
                KnightIdentityNetwork.Sync(new[] { knight });

                for (long nonce = 1; nonce <= 513; nonce++) MockNet.Deliver(header, MockNet.Request(nonce));

                Check.Equal(512, KnightIdentityNetwork.PendingResponseCount, "queue stays bounded");
                KnightIdentityNetwork.Sync(new[] { knight });
                Check.Equal(16, MockNet.Deliveries.Count, "16 responses per pass");
                Check.Equal(2L, MockNet.NonceOfDelivery(0), "oldest (nonce 1) was dropped");
            });

            Case.Run("响应发送异常被隔离且重试有界；恢复后可继续", () =>
            {
                MockReset.All();
                MockNet.HostReady();
                Knight knight = MockScene.SpawnKnight();
                CRPCHeader header = MockScene.Register(knight);
                KnightIdentityRuntime.Seed(knight, MockNet.Id(77), 1);
                KnightIdentityRuntime.SetLifetime(knight, 1);
                KnightIdentityNetwork.Sync(new[] { knight });

                header.ThrowOnRemoteCall = true;
                MockNet.Deliver(header, MockNet.Request(808L));

                KnightIdentityNetwork.Sync(new[] { knight });
                KnightIdentityNetwork.Sync(new[] { knight });
                Check.Equal(0, MockNet.Deliveries.Count, "native failure never produces a partial packet");
                Check.Equal(1, KnightIdentityNetwork.PendingResponseCount, "still queued after two attempts");

                KnightIdentityNetwork.Sync(new[] { knight });
                Check.Equal(0, KnightIdentityNetwork.PendingResponseCount, "attempt budget exhausted, dropped");
                Check.True(MockLog.CountMatching("response-exhausted") >= 1, "exhaustion logged");

                header.ThrowOnRemoteCall = false;
                MockNet.Deliver(header, MockNet.Request(808L));
                KnightIdentityNetwork.Sync(new[] { knight });
                Check.Equal(1, MockNet.Deliveries.Count, "recovers once the native path is healthy again");
                Check.Equal(808L, MockNet.NonceOfDelivery(0), "echoes the retried nonce");
            });
        }
    }
}
