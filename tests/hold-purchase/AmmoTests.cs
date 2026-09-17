using System;
using KingdomEnhancedMod;
using UnityEngine;
internal static partial class Program
{
    private static (Env Env, Shop Shop, Player Player) Ammo(bool fire, int limit=20)
    {
        Reset();var e=new Env();var shop=e.AddAmmo(fire,limit);var p=e.P1;
        Env.StandAt(p,shop);p.coins=100;p.RecordReads=true;return(e,shop,p);
    }
    private static void FirstPurchase(Player p)
    {
        for(int i=0;i<300&&p.Purchases==0;i++)Env.Frame(p,true,i==0);
        Eq(1,p.Purchases,"one native completed purchase");
    }
    private static int Continue(Player p,int frames=200)
    {int injected=0;for(int i=0;i<frames;i++)if(Env.Frame(p,true,false))injected++;return injected;}
    private static void AmmoScenarios()
    {
        foreach(bool fire in new[]{false,true})
        {
            string name=fire?"firetower_2":"barrel_5";
            Run(name+"_native_hold_continues_at_original_price",()=>
            {
                var(e,shop,p)=Ammo(fire);Env.Hold(p,220);
                True(p.Purchases>=3,"continuous native purchases");Eq(fire?2:5,shop.Price,"price unchanged");
                Eq(p.Purchases,shop.Stock,"one stock item per native success");
                Eq(100-p.Purchases*shop.Price-p._floatingCurrency.Count,p.coins,"coin conservation");
                Eq(0,p.GroundDrops,"synthetic presses never drop coins");Eq(.4f,p.timeBetweenCoins,"interval returned");
            });
            Run(name+"_first_coin_and_point_six_threshold_unchanged",()=>
            {
                var(e,shop,p)=Ammo(fire);Env.Hold(p,15);
                Eq(1,p.CoinLaunchTimes.Count,"first real coin only");Between(.26f,.30f,p.CoinLaunchTimes[0],"native holding threshold");
                NoRead(p,.1f,"before hold threshold no speedup");Continue(p,60);HasRead(p,.1f,"after threshold accelerate");
            });
            Run(name+"_off_first_hold_matches_native_coin_trace",()=>
            {
                var(e,shop,p)=Ammo(fire);ModConfig.HoldPurchaseEnabled.Value=false;Env.Hold(p,200);
                string coins=string.Join(",",p.CoinLaunchTimes);int balance=p.coins,stock=shop.Stock;
                var(reference,native,np)=Ammo(fire);
                for(int i=0;i<200;i++)Harness.NativeFrame(np,true,i==0);
                Eq(coins,string.Join(",",np.CoinLaunchTimes),"off coin timing exactly native");
                Eq(balance,np.coins,"off wallet exactly native");Eq(stock,native.Stock,"off native stock unchanged");
            });
            foreach(string stop in new[]{"full","unready","funds","release","off","pause","disabled","inactive","distance","authority"})
                Run(name+"_stops_without_extra_charge_"+stop,()=>
                {
                    var(e,shop,p)=Ammo(fire);FirstPurchase(p);int paid=shop.Stock;
                    switch(stop){
                        case "full":shop.Limit=paid;break;case "unready":shop.CanPayHook=_=>false;break;
                        case "funds":p.coins=shop.Price-1;break;case "release":Env.Frame(p,false,false);break;
                        case "off":ModConfig.HoldPurchaseEnabled.Value=false;break;case "pause":Time.timeScale=0;break;
                        case "disabled":shop.enabled=false;break;case "inactive":shop.gameObject.SetActive(false);break;
                        case "distance":p.transform.position=new Vector3(100,0,0);break;case "authority":p.hasLocalAuthority=false;break;
                    }
                    int money=p.coins;Eq(0,Continue(p),"no continuation after stop");
                    Eq(paid,shop.Stock,"no extra stock");Eq(money,p.coins,"no extra debit");Eq(.4f,p.timeBetweenCoins,"interval restored");
                });
            Run(name+"_client_waits_and_denial_refunds_without_retry",()=>
            {
                var(e,shop,_)=Ammo(fire);NetworkBigBoss.HasWorldAuth=false;var p=e.P2;Env.StandAt(p,shop);p.coins=100;
                for(int i=0;i<300&&!shop.PendingReply;i++)Env.Frame(p,true,i==0);
                True(shop.PendingReply,"native pay RPC pending");int balance=p.coins;
                Eq(0,Continue(p,80),"no synthetic input while awaiting real receipt");Eq(balance,p.coins,"waiting costs nothing extra");
                shop.RecvDenied(p.playerId);Eq(100,p.coins,"native denial refund");
                Eq(0,Continue(p),"no retry after rejected RPC");Eq(0,shop.Stock,"no stock on denial");Eq(100,p.coins,"denial costs nothing extra");
            });
        }
        Run("firetower_ai_disabled_still_valid_for_client_purchase",()=>
        {
            var(e,shop,_)=Ammo(true);shop.gameObject.GetComponent<FireTower>().enabled=false;
            NetworkBigBoss.HasWorldAuth=false;var p=e.P2;Env.StandAt(p,shop);p.coins=100;
            for(int i=0;i<200&&!shop.PendingReply;i++)Env.Frame(p,true,i==0);
            True(shop.PendingReply,"AI disabled does not disable native payable");shop.RecvApproved(p);
            True(Continue(p,100)>0,"real approval continues with disabled client AI");Eq(1,shop.Stock,"second RPC still unapproved");
        });
        foreach(string change in new[]{"wrong-owner","null-owner","new-valid-owner","pool-inactive","foreign-layer","unreadable-owner"})
            Run("firetower_stale_goods_cache_rejected_"+change,()=>
            {
                var(e,shop,p)=Ammo(true);FirstPurchase(p);var component=(PayableComponent)shop;
                switch(change){
                    case "wrong-owner":component._owner=new Component{Pointer=(IntPtr)999};break;
                    case "null-owner":component._owner=null;break;
                    case "new-valid-owner":component._owner.Pointer=(IntPtr)888;break; // same-GO tower and owner both now valid under another pointer
                    case "pool-inactive":shop.gameObject.SetActive(false);break;
                    case "foreign-layer":shop.transform.Parent=null;break;
                    case "unreadable-owner":component.ThrowOwnerRead=true;break;
                }
                int balance=p.coins;Eq(0,Continue(p),"previous owner receipt cannot continue");
                Eq(1,shop.Stock,"one original purchase only");Eq(balance,p.coins,"no extra debit");
                Eq(0,PatchPlayer_HoldPurchase.ActiveSessionCount,"stale ammo session removed");
            });
        Run("firetower_wrong_owner_from_first_press_has_only_vanilla_purchase",()=>
        {
            var(e,shop,p)=Ammo(true);((PayableComponent)shop)._owner=new Component{Pointer=(IntPtr)42};
            Env.Hold(p,220);Eq(1,shop.Stock,"native manual purchase only");NoRead(p,.1f,"unrelated owner is not accelerated");
            Eq(98,p.coins,"no extra debit");
        });
        Run("firetower_owner_changed_during_client_wait_cannot_accept_old_receipt",()=>
        {
            var(e,shop,_)=Ammo(true);NetworkBigBoss.HasWorldAuth=false;var p=e.P2;Env.StandAt(p,shop);p.coins=100;
            for(int i=0;i<200&&!shop.PendingReply;i++)Env.Frame(p,true,i==0);
            ((PayableComponent)shop)._owner.Pointer=(IntPtr)999;shop.RecvApproved(p);
            Eq(0,Continue(p),"approval for old hold owner cannot continue");Eq(1,shop.Stock,"only already submitted native transaction");
        });
        Run("firetower_pool_disable_reenable_cannot_inherit_hold",()=>
        {
            var(e,shop,p)=Ammo(true);FirstPurchase(p);int balance=p.coins;
            shop.gameObject.SetActive(false);PatchPlayer_HoldPurchase.Tick();shop.gameObject.SetActive(true);
            Eq(0,Continue(p),"reactivated object needs a fresh press");Eq(balance,p.coins,"no pooled extra charge");Eq(1,shop.Stock,"no pooled extra stock");
        });
        Run("generic_payable_component_tag_cannot_bypass_owner_whitelist",()=>
        {
            var(e,shop,p)=Ammo(true);shop.gameObject.Tag="ShopBow";((PayableComponent)shop)._owner=null;
            Env.Hold(p,200);Eq(1,shop.Stock,"mis-tagged component retains vanilla only");NoRead(p,.1f,"no generic component speedup");
        });
        Run("bind_owner_capture_failure_cannot_keep_cached_goods_true",()=>
        {
            var(e,shop,p)=Ammo(true);shop.ThrowOnCastCall=3; // IsSafeGoods succeeded; Bind's second-stage component read fails.
            Env.Hold(p,220);Eq(1,shop.Stock,"partial bind retains only native first purchase");
            NoRead(p,.1f,"partial capture never accelerates");Eq(98,p.coins,"partial bind never synthesizes an extra purchase");
        });
    }
}
