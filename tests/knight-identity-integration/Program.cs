using KingdomEnhancedMod;

internal static class Program
{
    static int asserts;
    static void Require(bool condition, string message) { asserts++; if (!condition) throw new Exception(message); }
    static CRPCHeader Register(KnightUnit unit, int pointer)
    {
        var h = new CRPCHeader { Pointer = new IntPtr(pointer), referencedGO = unit.Go };
        NetworkPostbox.Instance.Bind(unit.Go, h); unit.Knight.parentHeaderRef = h;
        KnightIdentityNetwork.HandleHeaderRegistered(h, unit.Go, true); return h;
    }
    static void Deliver(CRPCHeader target, byte[] bytes)
    {
        ByteBuffer.FeedForRead(bytes); target.RemoteMethodList[0].Invoke();
    }
    static void Main()
    {
        NativeSim.ResetWorld(12345); NetworkPostbox.Instance = new NetworkPostbox();
        var host = NativeSim.NewKnight(55001); var client = NativeSim.NewKnight(55002);
        KnightIdentityRuntime.OnEnable(host.Knight); KnightIdentityRuntime.OnEnable(client.Knight);
        var hh = Register(host, 9001); var ch = Register(client, 9002);
        Require(hh.RemoteMethodList.Count == ch.RemoteMethodList.Count, "both endpoints register identical slot layouts");
        int[] all = { 0, 1, 2, 3, 4 };
        NetworkBigBoss.HasWorldAuth = true;
        Require(KnightIdentityRuntime.TryResolve(host.Knight, 2, 0, all, out _), "host allocates identity");
        Require(KnightIdentityRuntime.TryGetReceipt(host.Knight, out var original), "host receipt exists");
        NetworkBigBoss.HasWorldAuth = false;
        KnightIdentityNetwork.Sync(new[] { client.Knight });
        byte[] request = MockNetwork.Sent[^1].Payload;
        NetworkBigBoss.HasWorldAuth = true; Deliver(hh, request);
        KnightIdentityNetwork.Sync(new[] { host.Knight });
        byte[] response = MockNetwork.Sent[^1].Payload;
        NetworkBigBoss.HasWorldAuth = false; Deliver(ch, response);
        Require(KnightIdentityRuntime.TryGetReceipt(client.Knight, out var received) && received.Equals(original), "real runtime accepts real network handshake");
        KnightIdentityRuntime.OnEnable(client.Knight);
        Deliver(ch, response);
        Require(!KnightIdentityRuntime.TryGetReceipt(client.Knight, out _), "old response cannot claim empty new life");
        KnightIdentityNetwork.Sync(new[] { client.Knight });
        request = MockNetwork.Sent[^1].Payload;
        NetworkBigBoss.HasWorldAuth = true;
        KnightIdentityRuntime.OnEnable(host.Knight);
        Require(KnightIdentityRuntime.TryResolve(host.Knight, 4, 0, all, out _), "host new life allocated");
        Require(KnightIdentityRuntime.TryGetReceipt(host.Knight, out var replacement) && replacement.Id != original.Id, "host lifecycle gets new GUID");
        Deliver(hh, request); KnightIdentityNetwork.Sync(new[] { host.Knight }); response = MockNetwork.Sent[^1].Payload;
        NetworkBigBoss.HasWorldAuth = false; Deliver(ch, response);
        Require(KnightIdentityRuntime.TryGetReceipt(client.Knight, out received) && received.Equals(replacement), "new life receives correct replacement identity");
        int sends = MockNetwork.Sent.Count; KnightIdentityNetwork.Sync(new[] { client.Knight });
        Require(MockNetwork.Sent.Count == sends, "confirmed identity stops requests");
        Console.WriteLine($"PASS integrated real Archive/Runtime/Network: {asserts} assertions; native boundary is simulated");
    }
}
