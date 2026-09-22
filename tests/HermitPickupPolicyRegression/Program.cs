using System.Collections;
using System.Reflection;
using HarmonyLib;
using KingdomEnhancedMod;
using UnityEngine;
using Policy=KingdomEnhancedMod.PatchRoles_Hermit;

static class Program
{
 static int passed,failed;
 static IDictionary Receipts=>(IDictionary)typeof(Policy).GetField("Tracked",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
 static void Eq<T>(T expected,T actual,string label){if(!EqualityComparer<T>.Default.Equals(expected,actual))throw new Exception($"{label}: expected {expected}, got {actual}");}
 static void Test(string name,Action action)
 {
  Receipts.Clear();
  foreach(string field in new[]{"_dirty","_lastEnabled","_lastAuthority","_lastHasScope","_loggedProtection","_loggedFailure"})typeof(Policy).GetField(field,BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,false);
  foreach(string field in new[]{"_lastWorld","_lastLayer"})typeof(Policy).GetField(field,BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,IntPtr.Zero);
  typeof(Policy).GetField("_lastScene",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,0);
  typeof(Policy).GetField("_nextCheck",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,0f);
  Managers.Inst=new();NetworkBigBoss.HasWorldAuth=true;ModConfig.Enabled.Value=true;ModConfig.PetGuardEnabled.Value=true;Time.unscaledTime=0;KingdomEnhancedPlugin.Instance=new();
  try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception ex){failed++;Console.WriteLine("FAIL "+name+": "+ex.Message);}
 }
 static Droppable New(PickUpPolicy policy=PickUpPolicy.Anybody,bool hermit=true)
 {
  var go=new GameObject();go.transform.parent=Managers.Inst.world.gameLayer;go.scene=Managers.Inst.world.gameLayer.gameObject.scene;
  var d=go.AddComponent<Droppable>();d.NativePolicy(policy);d.NativeOriginal(policy);if(hermit)go.AddComponent<Hermit>();return d;
 }
 static void Enable(Droppable d){d.OnEnable();Droppable_OnEnable_HermitPickupPolicy_Patch.Postfix(d);}
 static void Disable(Droppable d){Droppable_OnDisable_HermitPickupPolicy_Patch.Prefix(d);d.OnDisable();}
 static void Tick(float time=.5f){Time.unscaledTime=time;Policy.Tick();}
 static void Main()
 {
  Test("Only long Droppable OnEnable and OnDisable methods are Harmony targets",()=>
  {
   var targets=typeof(Policy).Assembly.GetTypes().SelectMany(t=>t.GetCustomAttributes<HarmonyPatch>()).ToArray();
   Eq(2,targets.Length,"exact hook count");Eq(true,targets.All(t=>t.Target==typeof(Droppable)),"target type");
   Eq(true,targets.Any(t=>t.Method=="OnEnable"),"enable hook");Eq(true,targets.Any(t=>t.Method=="OnDisable"),"disable hook");
   Eq(false,targets.Any(t=>t.Method=="CanBePickedUpByEnemy"),"short getter not hooked");
   Eq(true,typeof(Droppable_OnEnable_HermitPickupPolicy_Patch).GetMethod("Postfix",BindingFlags.Static|BindingFlags.NonPublic).IsDefined(typeof(HarmonyPostfix)),"enable postfix");
   Eq(true,typeof(Droppable_OnDisable_HermitPickupPolicy_Patch).GetMethod("Prefix",BindingFlags.Static|BindingFlags.NonPublic).IsDefined(typeof(HarmonyPrefix)),"disable prefix");
  });
  Test("Anybody and EnemyOnly become Nobody and restore exact original policy",()=>
  {
   foreach(var original in new[]{PickUpPolicy.Anybody,PickUpPolicy.EnemyOnly})
   {
    ModConfig.PetGuardEnabled.Value=true;var d=New(original);Enable(d);Eq(PickUpPolicy.Nobody,d.CurrentEnemyPolicy,"protected");Eq(1,d.EnemyWrites,"single protection write");
    ModConfig.PetGuardEnabled.Value=false;Tick();Eq(original,d.CurrentEnemyPolicy,"exact original restored");Eq(2,d.EnemyWrites,"one restore write");
    Eq(original,d._originalEnemyPolicy,"native original unchanged");Eq(0,d.OriginalWrites,"never writes native original");Eq(PickUpPolicy.AnyPlayer,d.pickUpPolicy,"general player policy unchanged");Eq(0,d.GeneralWrites,"never writes general pickup");
   }
  });
  Test("Coins tools and a named Hermit without same-object Hermit component are untouched",()=>
  {
   var d=New(hermit:false);d.gameObject.name="Hermit";var child=new GameObject();child.transform.parent=d.transform;child.AddComponent<Hermit>();
   Enable(d);Tick();Eq(0,d.EnemyWrites,"no nonhermit writes");Eq(0,Receipts.Count,"no name or child matching");
  });
  Test("All originally non-enemy-pickable policies remain unchanged",()=>
  {
   foreach(var original in new[]{PickUpPolicy.AnybodyExceptDropper,PickUpPolicy.OnlyClaimer,PickUpPolicy.AnyPlayer,PickUpPolicy.Nobody,PickUpPolicy.Blocked,PickUpPolicy.WorkerOnly})
   {var d=New(original);Enable(d);ModConfig.PetGuardEnabled.Value=false;Tick();Eq(original,d.CurrentEnemyPolicy,"native restrictive policy");Eq(0,d.EnemyWrites,"no policy write");ModConfig.PetGuardEnabled.Value=true;}
  });
  Test("Disabled registration permits later enabling and repeated ticks do not stack",()=>
  {
   ModConfig.PetGuardEnabled.Value=false;var d=New();Enable(d);Eq(1,Receipts.Count,"registered while disabled");Eq(0,d.EnemyWrites,"disabled unchanged");
   ModConfig.PetGuardEnabled.Value=true;Tick(.01f);Eq(PickUpPolicy.Nobody,d.CurrentEnemyPolicy,"immediate config change");for(int i=1;i<20;i++)Tick(i);Eq(1,d.EnemyWrites,"stable receipt no repeated writes");
  });
  Test("Repeated OnEnable preserves Original including after disable toggle",()=>
  {
   var d=New(PickUpPolicy.EnemyOnly);Enable(d);for(int i=0;i<20;i++)Enable(d);Eq(1,Receipts.Count,"one generation");Eq(1,d.EnemyWrites,"duplicate enable no writes");
   ModConfig.PetGuardEnabled.Value=false;Tick();Eq(PickUpPolicy.EnemyOnly,d.CurrentEnemyPolicy,"original six retained");
   ModConfig.PetGuardEnabled.Value=true;Tick(.51f);ModConfig.PetGuardEnabled.Value=false;Tick(.52f);Eq(PickUpPolicy.EnemyOnly,d.CurrentEnemyPolicy,"re-enabled protection restores same six");
  });
  Test("OnDisable only retires receipt and native reset owns pool restoration",()=>
  {
   var d=New();Enable(d);Droppable_OnDisable_HermitPickupPolicy_Patch.Prefix(d);Eq(0,Receipts.Count,"retired before native disable");Eq(1,d.EnemyWrites,"no mod disable write");Eq(PickUpPolicy.Nobody,d.CurrentEnemyPolicy,"prefix leaves native field alone");
   d.OnDisable();Eq(PickUpPolicy.Anybody,d.CurrentEnemyPolicy,"native reset");Disable(d);Eq(1,d.EnemyWrites,"duplicate disable no writes");
   d.NativePolicy(PickUpPolicy.EnemyOnly);d.NativeOriginal(PickUpPolicy.EnemyOnly);Enable(d);ModConfig.PetGuardEnabled.Value=false;Tick();Eq(PickUpPolicy.EnemyOnly,d.CurrentEnemyPolicy,"new pool generation captures new original");
  });
  Test("External Blocked policy survives disable and subsequent toggles",()=>
  {
   var d=New();Enable(d);d.NativePolicy(PickUpPolicy.Blocked);ModConfig.PetGuardEnabled.Value=false;Tick();Eq(PickUpPolicy.Blocked,d.CurrentEnemyPolicy,"external five preserved");
   ModConfig.PetGuardEnabled.Value=true;Tick(.51f);ModConfig.PetGuardEnabled.Value=false;Tick(.52f);Eq(1,d.EnemyWrites,"no overwrite after external ownership");
  });
  Test("External change relinquishes this generation even when it later permits pickup",()=>
  {
   var d=New();Enable(d);d.NativePolicy(PickUpPolicy.EnemyOnly);Tick();Tick(1);Eq(PickUpPolicy.EnemyOnly,d.CurrentEnemyPolicy,"external permissive policy retained");
   ModConfig.PetGuardEnabled.Value=false;Tick(1.1f);ModConfig.PetGuardEnabled.Value=true;Tick(1.2f);Eq(1,d.EnemyWrites,"contested generation never reclaims");
  });
  Test("Client registers without even reading policy then host takeover protects",()=>
  {
   NetworkBigBoss.HasWorldAuth=false;Managers.Inst.game.state=Game.State.NetworkClientPlaying;var d=New();d.ThrowRead=true;Enable(d);Tick();
   Eq(1,Receipts.Count,"client registration");Eq(0,d.EnemyReads,"no client policy reads");Eq(0,d.EnemyWrites,"no client writes");
   d.ThrowRead=false;NetworkBigBoss.HasWorldAuth=true;Managers.Inst.game.state=Game.State.Playing;Tick(.51f);Eq(PickUpPolicy.Nobody,d.CurrentEnemyPolicy,"new host protects");
  });
  Test("Authority loss suspends Original and host return with mod disabled restores it",()=>
  {
   var d=New(PickUpPolicy.EnemyOnly);Enable(d);int reads=d.EnemyReads;NetworkBigBoss.HasWorldAuth=false;d.ThrowRead=true;Tick(.01f);
   ModConfig.PetGuardEnabled.Value=false;Tick(.02f);Enable(d);Eq(1,d.EnemyWrites,"no writes while authority absent");Eq(reads,d.EnemyReads,"no policy reads while authority absent");Eq(1,Receipts.Count,"receipt retained during client suspension");
   d.ThrowRead=false;NetworkBigBoss.HasWorldAuth=true;Tick(.03f);Eq(PickUpPolicy.EnemyOnly,d.CurrentEnemyPolicy,"six restored after host regain");
  });
  Test("Host regain while enabled keeps protection and its original receipt",()=>
  {
   var d=New();Enable(d);NetworkBigBoss.HasWorldAuth=false;Tick(.01f);NetworkBigBoss.HasWorldAuth=true;Tick(.02f);Eq(1,d.EnemyWrites,"no reapplication of four");
   ModConfig.PetGuardEnabled.Value=false;Tick(.03f);Eq(PickUpPolicy.Anybody,d.CurrentEnemyPolicy,"original zero retained through host migration");
  });
  Test("External policy during authority suspension is retained after host regain",()=>
  {
   var d=New(PickUpPolicy.EnemyOnly);Enable(d);NetworkBigBoss.HasWorldAuth=false;Tick(.01f);d.NativePolicy(PickUpPolicy.Blocked);
   ModConfig.PetGuardEnabled.Value=false;NetworkBigBoss.HasWorldAuth=true;Tick(.02f);Eq(PickUpPolicy.Blocked,d.CurrentEnemyPolicy,"external five beats suspended receipt");Eq(1,d.EnemyWrites,"no host regain restore");
  });
  Test("Initial missing context and late parent remain pending until attached",()=>
  {
   var m=Managers.Inst;var d=New();d.transform.parent=null;Managers.Inst=null;Enable(d);Eq(1,Receipts.Count,"registered without context");Eq(0,d.EnemyWrites,"no context no policy write");
   Managers.Inst=m;Tick(.5f);Eq(1,Receipts.Count,"same-scene unattached pending");Eq(0,d.EnemyWrites,"no parent no policy write");
   d.transform.parent=m.world.gameLayer;Tick(1);Eq(PickUpPolicy.Nobody,d.CurrentEnemyPolicy,"late parent protected from pending cache");
  });
  Test("Bound object leaving layer retires without restoring while pending attachment differs",()=>
  {
   var d=New();Enable(d);d.transform.parent=null;ModConfig.PetGuardEnabled.Value=false;Tick(.01f);Eq(0,Receipts.Count,"bound detachment retired");Eq(1,d.EnemyWrites,"no out-of-layer restore");
   d.transform.parent=Managers.Inst.world.gameLayer;Tick(1);Eq(1,d.EnemyWrites,"retired object not resurrected by Tick");
  });
  Test("Inactive and other-scene generations prune without native writes",()=>
  {
   var inactive=New();var oldScene=New();Enable(inactive);Enable(oldScene);inactive.gameObject.activeInHierarchy=false;oldScene.gameObject.scene=new(){handle=99};
   ModConfig.PetGuardEnabled.Value=false;Tick();Eq(0,Receipts.Count,"stale entries pruned");Eq(1,inactive.EnemyWrites,"inactive not restored");Eq(1,oldScene.EnemyWrites,"other scene not restored");
  });
  Test("Changed world retires bound receipt even when same object joins new layer",()=>
  {
   var d=New();Enable(d);Managers.Inst.world=new();d.transform.parent=Managers.Inst.world.gameLayer;ModConfig.PetGuardEnabled.Value=false;Tick(.01f);
   Eq(0,Receipts.Count,"world generation retired");Eq(1,d.EnemyWrites,"never restores into replacement world");
  });
  Test("Loading suspends existing ownership and permits pending enable for later playing",()=>
  {
   var old=New();Enable(old);Managers.Inst.game.state=Game.State.Loading;Tick(.01f);Eq(1,Receipts.Count,"bound receipt retained during loading");Eq(1,old.EnemyWrites,"loading does not restore");
   var fresh=New();Enable(fresh);Eq(2,Receipts.Count,"new enable pending during load");Eq(0,fresh.EnemyWrites,"pending load no writes");Managers.Inst.game.state=Game.State.Playing;Tick(.02f);Eq(1,fresh.EnemyWrites,"playing resolves pending");
  });
  Test("Temporary Loading managers world or layer loss retains originals through playing or pause recovery",()=>
  {
   foreach(var original in new[]{PickUpPolicy.Anybody,PickUpPolicy.EnemyOnly})
   foreach(int missing in new[]{0,1,2,3})
   foreach(var resumedState in new[]{Game.State.Playing,Game.State.Menu})
   {
    Receipts.Clear();Managers.Inst=new();ModConfig.PetGuardEnabled.Value=true;var m=Managers.Inst;var world=m.world;var layer=world.gameLayer;var d=New(original);Enable(d);
    if(missing==0)m.game.state=Game.State.Loading;else if(missing==1)Managers.Inst=null;else if(missing==2)m.world=null;else world.gameLayer=null;
    ModConfig.PetGuardEnabled.Value=false;Tick(.1f);Eq(1,Receipts.Count,"temporary context loss retains receipt");Eq(1,d.EnemyWrites,"no write without context");
    Managers.Inst=m;m.world=world;world.gameLayer=layer;m.game.state=resumedState;Tick(.2f);
    Eq(original,d.CurrentEnemyPolicy,"same-world recovery restores original "+(int)original);Eq(2,d.EnemyWrites,"exactly one restore after context returns");
   }
  });
  Test("Known pause keeps live ownership and permits host config restore",()=>
  {
   var d=New();Enable(d);Managers.Inst.game.state=Game.State.Menu;ModConfig.PetGuardEnabled.Value=false;Tick(.01f);Eq(PickUpPolicy.Anybody,d.CurrentEnemyPolicy,"paused host restores");Eq(1,Receipts.Count,"pause does not retire live receipt");
  });
  Test("Exact component and GO identities reject reused pointers before policy writes",()=>
  {
   foreach(int changed in new[]{0,1,2})
   {
    ModConfig.PetGuardEnabled.Value=true;var d=New();Enable(d);
    if(changed==0)d.Id+=100000;else if(changed==1)d.gameObject.GetComponent<Hermit>().Id+=100000;else d.gameObject.Id+=100000;
    ModConfig.PetGuardEnabled.Value=false;Tick(changed+1);Eq(1,d.EnemyWrites,"identity mismatch not restored");Eq(0,Receipts.Count,"mismatched identity retired");
   }
  });
  Test("Mounted nested parent remains in current layer without touching general policy",()=>
  {
   var mount=new GameObject();mount.transform.parent=Managers.Inst.world.gameLayer;var saddle=new GameObject();saddle.transform.parent=mount.transform;
   var d=New();d.transform.parent=saddle.transform;Enable(d);Eq(PickUpPolicy.Nobody,d.CurrentEnemyPolicy,"nested hermit protected");Eq(0,d.GeneralWrites,"mount/player pickup unchanged");
  });
  Test("Tick and hooks contain failures and emit bounded diagnostic warning",()=>
  {
   var d=New();d.ThrowRead=true;Enable(d);for(int i=1;i<5;i++)Tick(i);Eq(1,KingdomEnhancedPlugin.Instance.LogSource.Warning.Count,"one warning across repeated native faults");
   d.ThrowRead=false;Tick(5);Eq(PickUpPolicy.Nobody,d.CurrentEnemyPolicy,"later valid read recovers");
   var bad=New();bad.gameObject.ThrowComponent=true;Enable(bad);Eq(1,KingdomEnhancedPlugin.Instance.LogSource.Warning.Count,"hook error shares bounded warning");
  });
  Test("First successful protection log includes native original policy and object id",()=>
  {
   var d=New(PickUpPolicy.EnemyOnly);Enable(d);Enable(New());var messages=KingdomEnhancedPlugin.Instance.LogSource.Info;
   Eq(1,messages.Count,"one successful evidence log");Eq(true,messages[0].Contains("id="+d.gameObject.Id),"object identity logged");Eq(true,messages[0].Contains("original=6 current=4"),"policy transition logged");
  });
  Console.WriteLine($"RESULT: {passed} passed, {failed} failed");Environment.ExitCode=failed==0?0:1;
 }
}
