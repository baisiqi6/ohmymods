using System;
using KingdomEnhancedMod;

internal static partial class Program
{
    private const string A = "11111111-1111-1111-1111-111111111111";
    private const string B = "22222222-2222-2222-2222-222222222222";
    private static int _passed, _failed;
    private static void Test(string name, Action test)
    { try { test(); Console.WriteLine("PASS " + name); _passed++; } catch(Exception e) { Console.WriteLine("FAIL " + name + ": " + e.Message); _failed++; } }
    internal static void Equal<T>(T expected, T actual)
    { if (!Equals(expected, actual)) throw new Exception("expected " + expected + ", got " + actual); }
    private static MountIslandGemOwner Owner(string guid = A, int land = 11, long layer = 6)
        => new MountIslandGemOwner((IntPtr)1, (IntPtr)2, (IntPtr)3, (IntPtr)4, (IntPtr)5, (IntPtr)layer, 0, land, guid);
    private static MountIslandGemRewardState Empty() => new MountIslandGemRewardState(null);
    private static string Saved(string guid, int bits) => "{\"version\":1,\"campaigns\":[{\"guid\":\""+guid+"\",\"batch1\":"+bits+"}]}";
    private static void Main()
    {
        Test("four seven-Gem boxes only on 11 and 13", () => {
            Equal(true, MountIslandGemRewardState.TryPlan(11, out int count, out int amount)); Equal(4,count); Equal(7,amount);
            Equal(true, MountIslandGemRewardState.TryPlan(13, out count, out amount)); Equal(28,count*amount);
            Equal(false, MountIslandGemRewardState.TryPlan(12, out count, out amount));
        });
        Test("campaign and island issuance independent", () => {
            var s=Empty(); Equal(true,s.RecordLive(Owner())); Equal(false,s.CanIssue(A,11));
            Equal(true,s.CanIssue(A,13)); Equal(true,s.CanIssue(B,11)); Equal(false,s.RecordLive(Owner()));
        });
        Test("saved completed marker prevents missing-box regrant", () => { var s=new MountIslandGemRewardState(Saved(A,3)); Equal(false,s.CanIssue(A,11)); Equal(false,s.CanIssue(A,13)); });
        string[] bad = { "", "{}", "{\"version\":2,\"campaigns\":[]}", "{\"version\":1,\"version\":1,\"campaigns\":[]}", Saved(A,4), Saved("not-guid",1), "{\"version\":1,\"campaigns\":[{\"guid\":\""+A+"\",\"batch1\":1},{\"guid\":\""+A+"\",\"batch1\":2}]}", "{\"version\":1,\"campaigns\":[],\"future\":1}" };
        foreach(string raw in bad) Test("unknown payload is closed and preserved: " + raw, () => { var s=new MountIslandGemRewardState(raw); Equal(false,s.Ready); Equal(false,s.CanIssue(A,11)); Equal(raw,s.SerializeStaged()); });
        Test("live issuance cannot enter global-only serialization", () => { var s=Empty(); s.RecordLive(Owner()); Equal("{\"version\":1,\"campaigns\":[]}", s.SerializeStaged()); });
        Test("wrong island marker and stale owner cannot stage", () => {
            var s=Empty(); s.RecordLive(Owner()); var c=s.BeginCapture(Owner(), (IntPtr)50);
            s.Marker(c,(IntPtr)51); Equal(false,s.EndCapture(c,Owner(),true,true));
            c=s.BeginCapture(Owner(),(IntPtr)50); s.Marker(c,(IntPtr)50); Equal(false,s.EndCapture(c,Owner(layer:7),true,true));
            Equal("{\"version\":1,\"campaigns\":[]}",s.SerializeStaged());
        });
        Test("exact successful capture stages once", () => { var s=Empty(); s.RecordLive(Owner()); var c=s.BeginCapture(Owner(),(IntPtr)50); s.Marker(c,(IntPtr)50); Equal(true,s.EndCapture(c,Owner(),true,true)); Equal(Saved(A,1),s.SerializeStaged()); Equal(false,s.EndCapture(c,Owner(),true,true)); });
        Test("capture exception or incomplete native rows keeps prior staged", () => {
            var s=new MountIslandGemRewardState(Saved(A,1)); s.RecordLive(Owner(land:13));
            var c=s.BeginCapture(Owner(land:13),(IntPtr)50); s.Marker(c,(IntPtr)50); Equal(false,s.EndCapture(c,Owner(land:13),false,true));
            c=s.BeginCapture(Owner(land:13),(IntPtr)50); s.Marker(c,(IntPtr)50); Equal(false,s.EndCapture(c,Owner(land:13),true,false)); Equal(Saved(A,1),s.SerializeStaged());
        });
        Test("bounded payload and campaign count", () => { var s=new MountIslandGemRewardState(new string('x',16385)); Equal(false,s.Ready); Equal(false,s.CanIssue(A,11)); });
        Test("owned iterator drains native seven drops and stops before fade", () => {
            var drain=new MountIslandGemDrain(); int state=1, drops=0;
            Equal(true,drain.AfterMove(true,0,()=>state,()=>{ drops++; state=drops==7?3:2; return true; }));
            Equal(7,drops); Equal(true,drain.Complete); Equal(3,state);
            drain.AfterMove(true,3,()=>state,()=>{throw new Exception("fade must remain native");}); Equal(7,drops);
        });
        Test("non-owned iterator unchanged", () => { var d=new MountIslandGemDrain(); int calls=0; Equal(true,d.AfterMove(false,0,()=>1,()=>{calls++;return true;})); Equal(0,calls); Equal(false,d.Complete); });
        Test("reentry only runs same native iterator without nested draining", () => { var d=new MountIslandGemDrain(); int state=1,calls=0; Func<bool> move=null; move=()=>{calls++; state=3; return d.AfterMove(true,1,()=>state,move);}; d.AfterMove(true,0,()=>state,move); Equal(1,calls); Equal(true,d.Complete); });
        Test("unknown state latches and never replays", () => { var d=new MountIslandGemDrain(); int calls=0; d.AfterMove(true,0,()=>99,()=>{calls++;return true;}); Equal(true,d.Unknown); d.AfterMove(true,0,()=>1,()=>{calls++;return true;}); Equal(0,calls); });
        Test("exception and sixteen-step limit latch unknown", () => { var d=new MountIslandGemDrain(); d.AfterMove(true,0,()=>1,()=>throw new Exception("native")); Equal(true,d.Unknown); d=new MountIslandGemDrain(); int calls=0; d.AfterMove(true,0,()=>2,()=>{calls++;return true;}); Equal(16,calls); Equal(true,d.Unknown); });
        Test("duplicate null guid property refuses payload",()=>{var s=new MountIslandGemRewardState("{\"version\":1,\"campaigns\":[{\"guid\":null,\"guid\":\""+A+"\",\"batch1\":1}]}");Equal(false,s.Ready);});
        Test("live campaign bound cannot create an unreadable saved document",()=>{var s=Empty();for(int n=1;n<=64;n++){string guid=n.ToString("x8")+"-1111-1111-1111-111111111111";Equal(true,s.RecordLive(Owner(guid)));}Equal(false,s.CanIssue("ffffffff-1111-1111-1111-111111111111",11));});
        Test("freezing new issuance preserves already staged batches",()=>{var s=Empty();s.RecordLive(Owner());var c=s.BeginCapture(Owner(),(IntPtr)50);s.Marker(c,(IntPtr)50);s.EndCapture(c,Owner(),true,true);s.Freeze("unknown-other-scene");Equal(Saved(A,1),s.SerializeStaged());Equal(true,s.StagedIssued(A,11));Equal(false,s.CanIssue(A,13));});
        RuntimeTests();
        PayloadTests();
        Console.WriteLine("RESULT " + _passed + " passed, " + _failed + " failed");
        if (_failed != 0) Environment.ExitCode=1;
    }
    static partial void RuntimeTests();
    static partial void PayloadTests();
}
