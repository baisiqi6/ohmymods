using System;
using System.Collections.Generic;
using KingdomEnhancedMod;
using Interval = KingdomEnhancedMod.HeroShopPlacement.Interval;
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
var empty = Array.Empty<Interval>();
bool NativeFits(float p, float halfWidth, IReadOnlyList<Interval> real)
{
    // Independent native-style closed-interval oracle, using float endpoint arithmetic.
    foreach(var block in real)
        if(p-halfWidth <= block.High && p+halfWidth >= block.Low) return false;
    return true;
}
bool Search(float left,float right,float halfWidth,IReadOnlyList<Interval> real,
    out float position,IReadOnlyList<Interval> collected=null)
    => HeroShopPlacement.Find(left,right,halfWidth,p=>NativeFits(p,halfWidth,real),
        ()=>collected??real,out position);
int collections=0;
Check(HeroShopPlacement.Find(-10,10,2,p=>true,()=>{collections++;return empty;},out float x)
    &&x==0&&collections==0,"middle succeeds without collecting exclusions");
var centerBlocked = new[]{new Interval(-1,1)};
Check(Search(-10,10,2,centerBlocked,out x)&&x < -3,"nearby gaps, equal-distance candidates prefer left");
Check(!Search(-1,1,2,empty,out x),"too small");
Check(!Search(float.NaN,1,2,empty,out x),"unknown bounds");
Check(!Search(-1,float.PositiveInfinity,2,empty,out x),"invalid bounds");
Check(!Search(10,-10,2,empty,out x),"reversed territory");
Check(!Search(-10,10,0,empty,out x),"zero half width");
Check(!Search(-10,10,float.NaN,empty,out x),"unknown half width");
Check(Search(10,14,2,empty,out x)&&x==12,"exact territory width may fit");
Check(!Search(10,14,2,new[]{new Interval(14,14)},out x),"exact territory width still rejects native closed contact");
Check(Search(-float.MaxValue,float.MaxValue,2,empty,out x)&&x==0,"finite huge bounds avoid intermediate overflow");

var farRight = new[]{new Interval(-200,60)};
var farLeft = new[]{new Interval(-60,200)};
Check(Search(-100,100,2,farRight,out x)&&x>62,"free land beyond old right thirty-unit search limit");
Check(Search(-100,100,2,farLeft,out x)&&x < -62,"free land beyond old left thirty-unit search limit");
var narrow = new[]{new Interval(-100,40.10f),new Interval(44.12f,100)};
Check(Search(-100,100,2,narrow,out x)&&x>42.10f&&x<42.12f,"narrow gap between old 0.75 samples");
var superset = new[]{new Interval(-1000,1000),narrow[1],new Interval(-90,30),narrow[0],
    new Interval(40.11f,40.11f),new Interval(-1000,1000)};
Check(Search(-100,100,2,narrow,out x,superset)&&NativeFits(x,2,narrow),
    "ignored huge exclusion does not erase the only narrow gap; overlaps and duplicate boundaries accepted");
var touching = new[]{new Interval(-100,40),new Interval(44,100)};
Check(!Search(-100,100,2,touching,out x),"zero-width gap cannot pass native closed-contact gate");
var invalid = new[]{new Interval(float.NaN,1)};
Check(!HeroShopPlacement.Find(-10,10,2,p=>false,()=>invalid,out x),"nonfinite collected interval fails whole search");
Check(!HeroShopPlacement.Find(-10,10,2,p=>false,()=>new[]{new Interval(2,1)},out x),"reversed collected interval fails whole search");
Check(!HeroShopPlacement.Find(-10,10,2,p=>false,()=>null,out x),"unavailable collection fails search");
bool collectionThrew=false;
try { HeroShopPlacement.Find(-10,10,2,p=>false,()=>throw new InvalidOperationException("collection"),out x); }
catch(InvalidOperationException){collectionThrew=true;}
Check(collectionThrew,"collection failure reaches caller error path");
// A legal center only a few representable floats wide must survive without a fixed epsilon.
float tinyLeft=100f, tinyRight=MathF.BitIncrement(MathF.BitIncrement(MathF.BitIncrement(104f)));
var tinyGap=new[]{new Interval(-200,tinyLeft),new Interval(tinyRight,200)};
Check(Search(-200,200,2,tinyGap,out x)&&NativeFits(x,2,tinyGap),"representable sub-0.01 gap survives");
var edges = new[]{new Interval(-8,8)};
Check(!Search(-10,10,2,edges,out x),"no shop footprint may cross either wall");
var occupied = new List<Interval>();
Check(Search(-20,20,2,occupied,out float first),"first shop can be placed");
occupied.Add(new Interval(first-2,first+2));
Check(Search(-20,20,2,occupied,out float second)&&NativeFits(second,2,occupied),"second shop does not overlap registered first shop");

int calls=0;
int CountFailedSearch(float left,float right,IReadOnlyList<Interval> source)
{
    calls=0;
    Check(!HeroShopPlacement.Find(left,right,2,p=>
    {
        calls++;
        if(p-2<left || p+2>right) throw new Exception("candidate crossed a wall");
        return false;
    },()=>source,out _),"failed search is finite");
    return calls;
}
int shortCalls=CountFailedSearch(-100,100,centerBlocked);
int longCalls=CountFailedSearch(-1000000,1000000,centerBlocked);
Check(shortCalls<=6*centerBlocked.Length+6&&longCalls<=6*centerBlocked.Length+6,
    "native calls scale with boundaries, not territory length");
var many=new List<Interval>();
for(int i=0;i<1500;i++) many.Add(new Interval(-5000+i*2,-4999+i*2));
many.Add(new Interval(-10000,4000));
many.Add(new Interval(4004.1f,10000));
Check(Search(-10000,10000,2,many,out x)&&x>4002&&x<4002.1f,"no candidate or source cutoff loses distant final gap");
Check(CountFailedSearch(-10000,10000,many)<=6*many.Count+6,"large source remains linear in native checks");
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
