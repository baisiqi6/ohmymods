using System;
using KingdomEnhancedMod;
int checks=0;
void Check(bool ok,string name) { checks++; if(!ok)throw new Exception(name); }
var payment=new HeroShopPayment();
Check(!payment.Consume(1,true,8),"unarmed callback");
payment.Arm(1);
Check(payment.IsArmed(1)&&!payment.IsArmed(2),"final native gate matches payer");
Check(!payment.Consume(2,true,8),"another player's callback");
Check(!payment.Consume(1,false,8),"incomplete state");
Check(!payment.Consume(1,true,7),"partial coins");
Check(payment.Consume(1,true,8),"completed payment");
Check(!payment.IsArmed(1)&&payment.AlreadySettled(1),"settled cannot pass final CanPay gate");
Check(!payment.AlreadySettled(2),"other payer cannot claim settled receipt");
Check(!payment.Consume(1,true,8),"duplicate callback");
payment.Arm(1); payment.Clear();
Check(!payment.IsArmed(1)&&!payment.AlreadySettled(1),"cancel clears both states");
Check(!payment.Consume(1,true,8),"cancelled callback");
payment.Arm(0); Check(!payment.Consume(0,true,8),"null payer");
payment.Arm(2); Check(payment.Consume(2,true,8),"next purchase");
payment.Arm(2); Check(payment.IsArmed(2)&&!payment.AlreadySettled(2),"new purchase resets settlement");
Check(HeroShopPlacement.Find(-10,10,2,p=>true,out float x)&&x==0,"middle");
Check(HeroShopPlacement.Find(-10,10,2,p=>p>=3,out x)&&x==3,"nearby free gap");
Check(!HeroShopPlacement.Find(-1,1,2,p=>true,out x),"too small");
Check(!HeroShopPlacement.Find(float.NaN,1,2,p=>true,out x),"unknown bounds");
Check(!HeroShopPlacement.Find(-1,float.PositiveInfinity,2,p=>true,out x),"invalid bounds");
int calls=0; Check(!HeroShopPlacement.Find(-100,100,2,p=>{calls++;return false;},out x)&&calls==81,"bounded search");
Check(HeroShopPlacement.Find(10,14,2,p=>true,out x)&&x==12,"exact fit");
calls=0; Check(!HeroShopPlacement.Find(0,10,2,p=>{calls++;Check(p>=2&&p<=8,"inside borders");return false;},out x),"no gap");
Check(HeroShopGrounding.TryResolve(true,true,0.875f,out float gy)&&gy==0.875f,"same-world active native root is the baseline");
Check(HeroShopGrounding.TryResolve(true,true,-1.25f,out gy)&&gy==-1.25f,"negative native root kept as-is");
Check(HeroShopGrounding.TryResolve(true,true,0f,out gy)&&gy==0f,"zero native root is still a real reference");
Check(!HeroShopGrounding.TryResolve(false,true,0.875f,out gy)&&gy==0f,"other-world root ignored, no guessed y");
Check(!HeroShopGrounding.TryResolve(true,false,0.875f,out gy),"inactive/pooled shop ignored");
Check(!HeroShopGrounding.TryResolve(true,true,float.NaN,out gy),"unknown native root y deferred");
Check(!HeroShopGrounding.TryResolve(true,true,float.PositiveInfinity,out gy),"infinite native root y deferred");
Check(!HeroShopGrounding.TryResolve(true,true,float.NegativeInfinity,out gy),"negative infinite native root y deferred");

// Production retention policy: a Menu pause keeps the same-world shop (no clear, no create,
// no payment) while Playing resumes it; real losses still clear immediately.
HeroShopRetention.Outcome Decide(bool enabled,bool offline,bool shop,bool scene,bool playing,
    bool world,bool header,bool alive=true,bool menuState=true)
    => HeroShopRetention.Decide(new HeroShopRetention.Probe
    {
        FeatureEnabled=enabled,Offline=offline,HasShop=shop,SameScene=scene,Playing=playing,
        SameWorld=world,HeaderOk=header,SceneAlive=alive,Menu=menuState,
    });
var menu=new HeroShopRetention.Probe{FeatureEnabled=true,Offline=true,HasShop=true,SameScene=true,
    Playing=false,Menu=true,SameWorld=true,HeaderOk=true,SceneAlive=true};
var resumed=menu; resumed.Playing=true;
Check(Decide(true,true,true,true,false,true,true) == HeroShopRetention.Outcome.Keep,"same-world Menu pause retains the existing shop");
Check(Decide(true,true,true,true,true,true,true) == HeroShopRetention.Outcome.Active,"resume keeps serving the same retained shop");
Check(!HeroShopRetention.CanServe(menu),"payment stays blocked in the Menu");
Check(HeroShopRetention.CanServe(resumed),"payment served only while Playing");
Check(Decide(true,true,false,true,false,true,true) == HeroShopRetention.Outcome.Wait,"creation deferred in the Menu");
Check(Decide(true,true,false,true,true,true,true) == HeroShopRetention.Outcome.Create,"creation only while Playing");
Check(Decide(true,true,false,false,false,false,false) == HeroShopRetention.Outcome.Wait,"missing context never spawns a shop");
Check(Decide(false,true,true,true,true,true,true) == HeroShopRetention.Outcome.ClearFeatureDisabled,"feature off clears instead of retaining");
Check(Decide(true,false,true,true,false,true,true) == HeroShopRetention.Outcome.ClearNetworkLost,"network/authority loss clears while paused");
Check(Decide(true,true,true,true,false,false,true) == HeroShopRetention.Outcome.ClearWorldChanged,"foreign world clears while paused");
Check(Decide(true,true,true,true,true,true,false) == HeroShopRetention.Outcome.ClearHeaderMismatch,"header mismatch clears");
Check(Decide(true,true,true,false,false,true,true,true,false) == HeroShopRetention.Outcome.ClearContextLost,"unknown scene cannot retain a stale world");
Check(Decide(true,true,true,false,false,true,true,false,false) == HeroShopRetention.Outcome.ClearContextLost,"destroyed scene clears instead of retaining");
Check(Decide(true,true,true,true,false,true,true,true,false) == HeroShopRetention.Outcome.ClearNonPlayable,"Loading/Intro/Loss/Quitting are not pause menus");
var resident = resumed; int creates=0,clears=0; const int originalInstance=714; int instance=originalInstance;
foreach(var state in new[]{resumed,menu,menu,menu,resumed})
{
    var action=HeroShopRetention.Decide(state);
    if(action==HeroShopRetention.Outcome.Create){creates++;instance++;}
    else if(action!=HeroShopRetention.Outcome.Keep&&action!=HeroShopRetention.Outcome.Active){clears++;instance=0;}
}
Check(instance==originalInstance&&creates==0&&clears==0,"playing-menu-playing retains one physical lifetime without rebuilding");
Console.WriteLine($"HeroShop core: {checks} assertions passed.");
