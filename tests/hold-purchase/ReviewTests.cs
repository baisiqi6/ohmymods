// Independent closeout counterexamples, driving the production Prefix before native work.
using System;
using KingdomEnhancedMod;
using UnityEngine;
internal static partial class Program
{
    private static (Env Env, Shop Shop, Player Player) NeighborFirstPurchase()
    {
        Reset(); var env = new Env(priceA: 2, limitA: 20);
        var shop = env.ShopA; shop.gameObject.Tag = "ShopBow"; shop.PayPointX = .25f;
        var p = env.P1; p.transform.position = new Vector3(1f, 0f, 0f); p.coins = 100;
        for (int i=0; i<400 && p.Purchases<1; i++) Env.Frame(p,true,i==0);
        Eq(1,p.Purchases,"first real simulated native purchase");
        Eq(Native.None,p._payState,"native left None after the receipt");
        return (env,shop,p);
    }
    private static void PrefixMustNotInject(Player p)
    {
        Time.deltaTime=.02f; Time.unscaledDeltaTime=.02f;
        Time.time+=.02f; Time.unscaledTime+=.02f;
        bool down=false;
        PatchPlayer_HoldPurchase.BeforePayUpdate(p,true,ref down,out var receipt);
        True(!down && !receipt.Injected,"Prefix rejected the synthesized press before native runs");
        PatchPlayer_HoldPurchase.FinishPayUpdate(p,null,receipt);
        Eq(0,p.GroundDrops,"Prefix itself never drops a coin");
    }
    private static void ReviewScenarios()
    {
        Run("fallback_null_rejects_before_native_then_can_resume",()=>{
            var(e,s,p)=NeighborFirstPurchase();
            e.Managers.payables.GetClosestCalls=0; e.Managers.payables.ReturnNullOnQuery=true;
            PrefixMustNotInject(p);
            Eq(1,e.Managers.payables.GetClosestCalls,"one qualification query, no diagnostic query");
            Eq(1,p.Purchases,"no fabricated receipt");
            float rejectedAt=Time.unscaledTime;
            e.Managers.payables.ReturnNullOnQuery=false;
            for(int i=0;i<400 && p.Purchases<2;i++) Env.Frame(p,true,false);
            Eq(2,p.Purchases,"real same-shop reselection can resume within the existing grace");
            Env.Frame(p,false,false);
            True(HasInfo(KingdomEnhancedPlugin.Instance.LogSource.Infos,"drop: reason=release"),"final reason is release");
            True(HasInfo(KingdomEnhancedPlugin.Instance.LogSource.Infos,"first-rejection[t="+rejectedAt.ToString("R")),"old rejection is explicitly timed history");
            Eq(0,p.GroundDrops,"no ground coin across null, resume and release");
        });
        Run("fallback_identity_reuse_cannot_transfer_captured_session",()=>{
            var(e,s,p)=NeighborFirstPurchase();
            e.Managers.payables.BeforeQueryReturn=()=>s.gameObject.Id++;
            PrefixMustNotInject(p);
            Eq(1,p.Purchases,"changing the current object identity cannot authorize another receipt");
            Eq(0,PatchPlayer_HoldPurchase.ActiveSessionCount,"captured instance identity remains authoritative");
        });
        Run("bind_scale_reads_shop_not_topmost_ancestor",()=>{
            Reset(); var e=new Env(); var p=e.P1; var s=e.ShopA;
            e.Layer.localScale=new Vector3(-7f,1f,1f); s.transform.localScale=new Vector3(-1f,1f,1f);
            p.coins=100; Env.StandAt(p,s); Env.Frame(p,true,true);
            True(HasInfo(KingdomEnhancedPlugin.Instance.LogSource.Infos,"scale=-1"),"shop cached transform sign is logged: "+string.Join(" | ",KingdomEnhancedPlugin.Instance.LogSource.Infos));
            True(!HasInfo(KingdomEnhancedPlugin.Instance.LogSource.Infos,"scale=-7"),"GameLayer scale is not the pay-point transform");
        });
        foreach(int kind in new[]{0,1,2,3}) Run("invalid_geometry_prefix_rejects_"+kind,()=>{
            var(e,s,p)=NeighborFirstPurchase();
            if(kind==0)s.playerPayDistance=float.PositiveInfinity;
            if(kind==1)s.PayPointX=float.NegativeInfinity;
            if(kind==2)p.transform.position=new Vector3(float.NaN,0,0);
            if(kind==3)s.playerPayDistance=float.NaN;
            PrefixMustNotInject(p);
            Eq(1,p.Purchases,"invalid input never starts a second payment");
            Eq(0,PatchPlayer_HoldPurchase.ActiveSessionCount,"invalid geometry fails closed");
            True(HasInfo(KingdomEnhancedPlugin.Instance.LogSource.Infos,"drop: reason=reach-invalid"),"invalid is not real out-of-reach evidence");
        });
    }
}
