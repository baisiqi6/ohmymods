using System.Reflection;
using KingdomEnhancedMod;
using UnityEngine;
int passed=0;
void Check(bool value,string name) { if(!value) throw new Exception(name); passed++; }
MusketeerIdentity.IslandState Setup()
{
    foreach(string name in new[]{"Roots","Bound","RestockEnded"}) {
        var obj=typeof(MusketeerIdentity).GetField(name,BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
        obj.GetType().GetMethod("Clear").Invoke(obj,null);
    }
    Managers.Inst=new Managers {world=new World {gameLayer=new GameObject().Add(new Transform())}};
    GlobalSaveData.loaded=new GlobalSaveData {currentCampaign=1,currentChallenge=0};
    CampaignSaveData.current=new CampaignSaveData {CurrentIsland=new IslandSaveData {land=1}};
    MusketeerAccess.TrackAllowed=MusketeerAccess.Enabled=MusketeerAccess.Playing=MusketeerAccess.InWorldResult=true;
    IslandSaveData.isSavingGame=false; Time.frameCount++; MusketeerIdentity.InvalidateContextCache();
    Check(MusketeerIdentity.TryContext(out var key,out var world,true),"context");
    var state=new MusketeerIdentity.IslandState {ContextKey=key,World=world,Ready=true,HasBaseline=true,Epoch="epoch"};
    MusketeerIdentity.SetCurrent(state); return state;
}
MusketeerIdentity.Career Bind(MusketeerIdentity.IslandState state,bool unit)
{
    var go=new GameObject {tag="Bow"};
    Character c=unit?go.Add(new Character()):null; Archer a=unit?go.Add(new Archer()):null;
    DroppableTool tool=unit?null:go.Add(new DroppableTool());
    var career=new MusketeerIdentity.Career {State=state,Id=Guid.NewGuid()}; state.Careers.Add(career);
    Check(MusketeerIdentity.Bind(career,MusketeerIdentity.RootOf(go,true),c,a,tool,-1),"bind"); return career;
}
void Count(int live,int guns,string message)
{
    Check(MusketeerIdentity.TryGetRestockCounts(out var l,out var g),message+" ready");
    Check(l==live&&g==guns,message+" counts");
}
var s=Setup(); var u=Bind(s,true); var gun=Bind(s,false); Count(1,1,"separate alive and usable gun");
u.Character.gameObject.ThrowOnActiveRead=true;
Check(!MusketeerIdentity.TryGetRestockCounts(out _,out _),"bound active native read failure cannot publish zero");
u.Character.gameObject.ThrowOnActiveRead=false;
u.Character._damageable.isDead=true; Count(0,1,"dead excluded");
gun.Tool.enemyClaimer=new GameObject(); Count(0,0,"enemy claimed excluded");
gun.Tool.enemyClaimer=null;gun.Tool.pickedUp=true;Count(0,0,"picked excluded");
gun.Tool.pickedUp=false;Count(0,1,"ground gun still covers");
MusketeerAccess.InWorldResult=false; Count(0,0,"foreign objects excluded");
MusketeerAccess.InWorldResult=true; u.Character._damageable=null;
Check(!MusketeerIdentity.TryGetRestockCounts(out _,out _),"unknown health fail closed");
foreach(string condition in new[]{"readonly","unresolved","notready","baseline","epoch","restore","world","context","offline"}) {
    s=Setup();
    switch(condition) {
        case "readonly":s.ReadOnly=true;break; case "unresolved":s.Unresolved=true;break;
        case "notready":s.Ready=false;break; case "baseline":s.HasBaseline=false;break;
        case "epoch":s.Epoch=null;break; case "restore":s.StockRestores.Add(new());break;
        case "world":s.World++;break;case "context":s.ContextKey="unknown";break;
        case "offline":MusketeerAccess.TrackAllowed=false;break;
    }
    Check(!MusketeerIdentity.TryGetRestockCounts(out _,out _),condition+" rejects unknown zero");
}
s=Setup();s.Careers.Add(new MusketeerIdentity.Career {State=s});
Check(!MusketeerIdentity.TryGetRestockCounts(out _,out _),"unbound historical career cannot become zero");
foreach(bool unit in new[]{true,false}) {
    s=Setup();var career=Bind(s,unit); var root=unit?career.Character.gameObject:career.Tool.gameObject;
    if(unit) career.Character._damageable.isDead=true;else career.Tool.enemyClaimer=new GameObject();
    MusketeerIdentity.OnPoolDespawnBegin(root,0,out var evidence);root.activeInHierarchy=false;
    MusketeerIdentity.OnPoolDespawnEnd(evidence);Count(0,0,unit?"dead recycled":"enemy taken recycled");
    var loaded=new MusketeerIdentity.IslandState {World=s.World,ContextKey=s.ContextKey,Epoch="epoch",Ready=true,HasBaseline=true};
    loaded.Careers.Add(new MusketeerIdentity.Career {State=loaded});MusketeerIdentity.SetCurrent(loaded);
    Check(!MusketeerIdentity.TryGetRestockCounts(out _,out _),"session evidence never licenses historical reload");
}
foreach(string outcome in new[]{"stillactive","delay","save","life","newworld","unreadable"}) {
    s=Setup();var career=Bind(s,true);var root=career.Character.gameObject;
    if(outcome=="save") IslandSaveData.isSavingGame=true;
    MusketeerIdentity.OnPoolDespawnBegin(root,outcome=="delay"?1:0,out var evidence);
    if(outcome!="stillactive") root.activeInHierarchy=false;
    if(outcome=="unreadable") root.ThrowOnActiveRead=true;
    if(outcome=="life") career.Life++;
    if(outcome=="newworld") Managers.Inst.world.gameLayer=new GameObject().Add(new Transform());
    MusketeerIdentity.OnPoolDespawnEnd(evidence);
    if(outcome=="stillactive") Count(1,0,"native refused despawn keeps live count");
    else if(outcome=="delay") Check(career.Slot!=null,"delayed request does not prove loss");
    else Check(!MusketeerIdentity.TryGetRestockCounts(out _,out _),outcome+" not confirmed end");
}
s=Setup();u=Bind(s,true);var old=u.Character.gameObject;
var dropped=new GameObject {tag="Bow"}.Add(new DroppableTool());
MusketeerIdentity.OnDroppableDrop(dropped,old);Count(0,1,"unit death drop transfers exact paid gun");
MusketeerIdentity.OnPoolDespawnBegin(old,0,out var oldEnd);old.activeInHierarchy=false;MusketeerIdentity.OnPoolDespawnEnd(oldEnd);
Count(0,1,"old unit recycling cannot consume transferred gun");
Console.WriteLine($"PASS {passed} real identity/restock assertions; no archive writes");
