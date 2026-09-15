// RPC 槽注册/所有权：注册时机、重复注册、Flush 作废、上限、槽位损坏 fail closed（绝不单边重追加）、进程边界。

using System;
using KingdomEnhancedMod;
using UnityEngine;

namespace KnightIdentityNetworkTests
{
    internal static class NetworkRegistrationTests
    {
        internal static void Run()
        {
            Case.Run("注册失败不追加；注册成功后恰好一个自有槽", () =>
            {
                MockReset.All();
                Knight knight = MockScene.SpawnKnight();
                CRPCHeader header = knight.parentHeaderRef;

                KnightIdentityNetwork.HandleHeaderRegistered(header, knight.gameObject, false);
                Check.Equal(0, header.RemoteMethodList.Count, "no slot when native registration failed");
                Check.Equal(0, KnightIdentityNetwork.TrackedBindingCount, "no binding when native registration failed");

                MockScene.Register(knight);
                Check.Equal(1, header.RemoteMethodList.Count, "exactly one own slot");
                Check.Equal(1, KnightIdentityNetwork.TrackedBindingCount, "one tracked binding");
            });

            Case.Run("Knight 与 Squire 都追加槽位（rank 变化不错位）", () =>
            {
                MockReset.All();
                Knight real = MockScene.SpawnKnight("Knight", true, true, 0);
                CRPCHeader realHeader = MockScene.Register(real);
                Knight squire = MockScene.SpawnKnight("Squire", true, true, 0);
                CRPCHeader squireHeader = MockScene.Register(squire);

                Check.Equal(1, realHeader.RemoteMethodList.Count, "Knight slot appended");
                Check.Equal(1, squireHeader.RemoteMethodList.Count, "Squire slot appended");
                Check.Equal(2, KnightIdentityNetwork.TrackedBindingCount, "both headers tracked");
                Check.Equal("Knight", real.tag, "real knight tag");
                Check.Equal("Squire", squire.tag, "squire tag");
            });

            Case.Run("不含 Knight 组件的对象不追加槽位", () =>
            {
                MockReset.All();
                var go = new GameObject("tower");
                go.AssignInstanceId(MockScene.NextGameObjectId());
                go.scene.handle = MockScene.SceneHandle;
                var header = new CRPCHeader { Pointer = new IntPtr(MockScene.NextHeaderPointer()) };
                NetworkPostbox.Instance.Bind(go, header);

                header.RegisterComponents(go);
                KnightIdentityNetwork.HandleHeaderRegistered(header, go, true);

                Check.Equal(0, header.RemoteMethodList.Count, "no slot for non-Knight header");
                Check.Equal(0, KnightIdentityNetwork.TrackedBindingCount, "no binding tracked");
            });

            Case.Run("重复 RegisterComponents 不重复追加槽位", () =>
            {
                MockReset.All();
                Knight knight = MockScene.SpawnKnight();
                CRPCHeader header = MockScene.Register(knight);
                int afterFirst = header.RemoteMethodList.Count;

                KnightIdentityNetwork.HandleHeaderRegistered(header, knight.gameObject, true);
                MockScene.Register(knight);
                KnightIdentityNetwork.HandleHeaderRegistered(header, knight.gameObject, true);

                Check.Equal(1, afterFirst, "first registration appends once");
                Check.Equal(1, header.RemoteMethodList.Count, "no duplicate append");
                Check.Equal(1, KnightIdentityNetwork.TrackedBindingCount, "single binding kept");
            });

            Case.Run("Flush 后旧槽失效；原生重注册追加新槽，客户端用新槽发请求", () =>
            {
                MockReset.All();
                MockNet.Client();
                Knight knight = MockScene.SpawnKnight();
                CRPCHeader header = MockScene.Register(knight);
                Check.Equal(1, header.RemoteMethodList.Count, "slot registered");
                NetworkPostbox.DynAction retired = header.RemoteMethodList[0];
                KnightIdentityRuntime.SetLifetime(knight, 1);

                header.Flush();
                KnightIdentityNetwork.HandleHeaderFlushed(header);
                Check.Equal(0, KnightIdentityNetwork.TrackedBindingCount, "ownership released on flush");

                ByteBuffer.FeedForRead(MockNet.Response(11L, MockNet.Id(5), 2, 3));
                retired.Invoke();
                Check.Equal(0, KnightIdentityRuntime.ApplyCalls, "delegate from flushed slot is inert");

                KnightIdentityNetwork.Sync(new[] { knight });
                Check.Equal(0, MockNet.Deliveries.Count, "no request while unregistered");

                header.RegisterComponents(knight.gameObject);
                KnightIdentityNetwork.HandleHeaderRegistered(header, knight.gameObject, true);
                Check.Equal(1, header.RemoteMethodList.Count, "fresh slot appended after flush");
                Check.NotEqual(retired.Pointer, header.RemoteMethodList[0].Pointer, "new native delegate object");

                KnightIdentityNetwork.Sync(new[] { knight });
                Check.Equal(1, MockNet.Deliveries.Count, "request sent through the fresh slot");
                Check.True(MockNet.IsRequest(MockNet.Deliveries[0].Payload), "kind=1 request");
            });

            Case.Run("原生槽位达到 256 上限时拒绝注册（绝不越界调用）", () =>
            {
                MockReset.All();
                Knight knight = MockScene.SpawnKnight("Knight", true, false, 0);
                var header = new CRPCHeader { Pointer = new IntPtr(MockScene.NextHeaderPointer()) };
                for (int i = 0; i < 256; i++) header.RemoteMethodList.Add(new NetworkPostbox.DynAction(() => { }));
                NetworkPostbox.Instance.Bind(knight.gameObject, header);
                knight.parentHeaderRef = header;

                KnightIdentityNetwork.HandleHeaderRegistered(header, knight.gameObject, true);

                Check.Equal(256, header.RemoteMethodList.Count, "native list untouched");
                Check.Equal(0, KnightIdentityNetwork.TrackedBindingCount, "refused to append beyond 256");
            });

            Case.Run("主机槽位损坏：不追加、不发响应；Flush 后重注册才恢复发送", () =>
            {
                MockReset.All();
                MockNet.HostReady();
                Knight knight = MockScene.SpawnKnight();
                CRPCHeader header = MockScene.Register(knight);
                KnightIdentityRuntime.Seed(knight, MockNet.Id(21), 1);
                KnightIdentityRuntime.SetLifetime(knight, 5);
                KnightIdentityNetwork.Sync(new[] { knight }); // 建立 host gate

                byte[] request = MockNet.Request(777L);
                MockNet.Deliver(header, request);
                Check.Equal(1, KnightIdentityNetwork.PendingResponseCount, "request queued");

                header.RemoteMethodList[header.RemoteMethodList.Count - 1] = new NetworkPostbox.DynAction(() => { }); // 槽位被回收/换主
                KnightIdentityNetwork.Sync(new[] { knight });

                Check.Equal(0, MockNet.Deliveries.Count, "corrupted slot: nothing sent");
                Check.Equal(1, header.RemoteMethodList.Count, "no unilateral re-append");
                Check.Equal(0, KnightIdentityNetwork.PendingResponseCount, "queued response dropped (fail closed)");
                Check.Equal(1, KnightIdentityNetwork.TrackedBindingCount, "retired binding remains rooted until Flush");

                MockScene.Register(knight); // No Flush: a duplicate registration cannot repair either endpoint.
                Check.Equal(1, header.RemoteMethodList.Count, "poisoned header cannot append on duplicate registration");
                header.Flush();
                KnightIdentityNetwork.HandleHeaderFlushed(header);
                MockScene.Register(knight);
                Check.Equal(1, header.RemoteMethodList.Count, "append allowed after native Flush and registration");
                Check.Equal(1, KnightIdentityNetwork.TrackedBindingCount, "fresh binding tracked");

                MockNet.Deliver(header, MockNet.Request(778L));
                Check.Equal(1, KnightIdentityNetwork.PendingResponseCount, "new request queued");
                KnightIdentityNetwork.Sync(new[] { knight });
                Check.Equal(1, MockNet.Deliveries.Count, "response sent through the fresh slot");
                Check.Equal(0, MockNet.Deliveries[0].SlotIndex, "fresh slot index in cleared native list");
                Check.Equal(778L, MockNet.NonceOfDelivery(0), "echoes the new request nonce");
            });

            Case.Run("客户端槽位损坏：不追加、不发请求；Flush 后换新 nonce 再发", () =>
            {
                MockReset.All();
                MockNet.Client();
                Knight knight = MockScene.SpawnKnight();
                CRPCHeader header = MockScene.Register(knight);
                KnightIdentityRuntime.SetLifetime(knight, 1);

                KnightIdentityNetwork.Sync(new[] { knight });
                Check.Equal(1, MockNet.Deliveries.Count, "first request sent");
                long firstNonce = MockNet.NonceOfDelivery(0);

                header.RemoteMethodList[0] = new NetworkPostbox.DynAction(() => { });
                KnightIdentityNetwork.Sync(new[] { knight });

                Check.Equal(1, MockNet.Deliveries.Count, "corrupted slot: no request");
                Check.Equal(1, header.RemoteMethodList.Count, "no unilateral re-append");
                Check.Equal(1, KnightIdentityNetwork.TrackedBindingCount, "retired binding remains rooted until Flush");

                MockScene.Register(knight);
                Check.Equal(1, header.RemoteMethodList.Count, "no append before Flush");
                KnightIdentityNetwork.Sync(new[] { knight });
                Check.Equal(1, MockNet.Deliveries.Count, "duplicate registration did not reopen sending");
                header.Flush();
                KnightIdentityNetwork.HandleHeaderFlushed(header);
                MockScene.Register(knight);
                Check.Equal(1, header.RemoteMethodList.Count, "append after Flush");
                KnightIdentityNetwork.Sync(new[] { knight });

                Check.Equal(2, MockNet.Deliveries.Count, "request sent after rebuild");
                Check.Equal(0, MockNet.Deliveries[1].SlotIndex, "fresh slot index in cleared list");
                Check.NotEqual(firstNonce, MockNet.NonceOfDelivery(1), "new binding generation => new nonce");
            });

            Case.Run("进程边界 reset 使旧 delegate 失效且清空所有权", () =>
            {
                MockReset.All();
                MockNet.Client();
                Knight knight = MockScene.SpawnKnight();
                CRPCHeader header = MockScene.Register(knight);
                NetworkPostbox.DynAction retired = header.RemoteMethodList[0];

                KnightIdentityNetwork.ResetForProcessBoundary();
                Check.Equal(0, KnightIdentityNetwork.TrackedBindingCount, "bindings cleared");

                ByteBuffer.FeedForRead(MockNet.Response(4242L, MockNet.Id(6), 1, 3));
                retired.Invoke();
                Check.Equal(0, KnightIdentityRuntime.ApplyCalls, "delegate from previous boundary is inert");
                Check.Equal(0, KnightIdentityNetwork.PendingResponseCount, "nothing queued");
            });
        }
    }
}
