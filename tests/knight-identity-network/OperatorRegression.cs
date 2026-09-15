using System;
using KingdomEnhancedMod;

namespace KnightIdentityNetworkTests;

internal static class OperatorRegression
{
    internal static void Run()
    {
        Case.Run("bad queued header cannot delete the next healthy response", () =>
        {
            MockReset.All(); MockNet.HostReady();
            Knight a = MockScene.SpawnKnight(), b = MockScene.SpawnKnight();
            var ah = MockScene.Register(a); var bh = MockScene.Register(b);
            KnightIdentityRuntime.Seed(a, MockNet.Id(71), 1); KnightIdentityRuntime.SetLifetime(a, 3);
            KnightIdentityRuntime.Seed(b, MockNet.Id(72), 2); KnightIdentityRuntime.SetLifetime(b, 4);
            MockNet.Deliver(ah, MockNet.Request(111)); MockNet.Deliver(bh, MockNet.Request(222));
            Check.Equal(2, KnightIdentityNetwork.PendingResponseCount, "both requests queued");
            ah.RemoteMethodList[0] = new NetworkPostbox.DynAction(() => { });
            KnightIdentityNetwork.Sync(new[] { a, b });
            Check.Equal(1, MockNet.Deliveries.Count, "healthy response must still be sent this pass");
            Check.Equal(222L, MockNet.NonceOfDelivery(0), "the healthy request was preserved");
            Check.Equal(0, KnightIdentityNetwork.PendingResponseCount, "queue drained without nested removal");
        });
        Case.Run("uncertain native append remains rooted and poisoned until Flush", () =>
        {
            MockReset.All(); MockNet.Client();
            Knight knight = MockScene.SpawnKnight();
            var header = new CRPCHeader { Pointer = new IntPtr(MockScene.NextHeaderPointer()) };
            NetworkPostbox.Instance.Bind(knight.gameObject, header); knight.parentHeaderRef = header;
            header.RemoteMethodList.ThrowAfterAdd = true;
            KnightIdentityNetwork.HandleHeaderRegistered(header, knight.gameObject, true);
            Check.Equal(1, header.RemoteMethodList.Count, "native insertion happened before exception");
            Check.Equal(1, KnightIdentityNetwork.TrackedBindingCount, "uncertain native callback stays rooted");
            KnightIdentityNetwork.HandleHeaderRegistered(header, knight.gameObject, true);
            Check.Equal(1, header.RemoteMethodList.Count, "uncertain registration cannot append again");
            KnightIdentityNetwork.Sync(new[] { knight });
            Check.Equal(0, MockNet.Deliveries.Count, "poisoned header sends nothing");
            header.Flush(); KnightIdentityNetwork.HandleHeaderFlushed(header);
            Check.Equal(0, KnightIdentityNetwork.TrackedBindingCount, "Flush releases native callback ownership");
            KnightIdentityNetwork.HandleHeaderRegistered(header, knight.gameObject, true);
            KnightIdentityRuntime.SetLifetime(knight, 2);
            KnightIdentityNetwork.Sync(new[] { knight });
            Check.Equal(1, MockNet.Deliveries.Count, "fresh native list accepts a new handshake");
        });
    }
}
