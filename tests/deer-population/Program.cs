using System.Collections;
using System.Reflection;
using HarmonyLib;
using KingdomEnhancedMod;
using UnityEngine;
using Patch=KingdomEnhancedMod.PopulationController_Update_DeerPopulation_Patch;
using Policy=KingdomEnhancedMod.PatchWorld_DeerPopulation;

static class Program
{
 static int passed,failed;
 static IDictionary Active=>(IDictionary)typeof(Policy).GetField("Active",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
 static IDictionary Pending=>(IDictionary)typeof(Policy).GetField("Pending",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
 static void Eq<T>(T expected,T actual,string label){if(!EqualityComparer<T>.Default.Equals(expected,actual))throw new Exception($"{label}: expected {expected}, got {actual}");}
 static void Test(string name,Action action)
 {
  Active.Clear();Pending.Clear();foreach(string f in new[]{"_loggedApplied","_loggedFailure"})typeof(Policy).GetField(f,BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,false);
  Managers.Inst=new();BiomeHolder.Inst=new();NetworkBigBoss.HasWorldAuth=true;ModConfig.Enabled.Value=true;Time.deltaTime=.25f;KingdomEnhancedPlugin.Instance=new();
  try{action();Eq(0,Active.Count,"no leaked active lease");Eq(0,Pending.Count,"no unrecovered pending lease");passed++;Console.WriteLine("PASS "+name);}catch(Exception ex){failed++;Console.WriteLine("FAIL "+name+": "+ex.Message);}
 }
 static PopulationController New(bool deer=true)
 {
  var go=new GameObject();go.transform.parent=Managers.Inst.world.gameLayer;go.scene=Managers.Inst.world.gameLayer.gameObject.scene;
  var c=go.AddComponent<PopulationController>();c.prefab=new GameObject();if(deer)c.prefab.AddComponent<Deer>();return c;
 }
 static void Run(PopulationController c,Action body=null)
 {
  Action old=c.NativeBody;c.NativeBody=body;Patch.Prefix(c,out var state);Exception error=null;
  try{c.Update();Patch.Postfix(state);}catch(Exception ex){error=ex;}
  finally{c.NativeBody=old;}
  error=Patch.Finalizer(error,state);if(error!=null)throw error;
 }
 static void Original(PopulationController c)
 {Eq(.125f,c.density,"density restored");Eq(0f,c.winterDensityDefault,"winter zero restored");Eq(.0625f,c.winterDensitySpecial,"winter special restored");Eq(3f,c._actualUpdateInterval,"interval restored");}
 static void Boosted(PopulationController c)
 {Eq(.375f,c.density,"3x density");Eq(0f,c.winterDensityDefault,"winter zero preserved");Eq(.1875f,c.winterDensitySpecial,"3x special winter");Eq(1f,c._actualUpdateInterval,"one-third interval");}
 static void Main()
 {
  Test("Only PopulationController.Update is hooked with prefix postfix and finalizer",()=>
  {
   var attrs=typeof(Policy).Assembly.GetTypes().SelectMany(t=>t.GetCustomAttributes<HarmonyPatch>()).ToArray();Eq(1,attrs.Length,"single hook");Eq(typeof(PopulationController),attrs[0].Target,"controller target");Eq("Update",attrs[0].Method,"long native Update target");
   foreach(var pair in new[]{("Prefix",typeof(HarmonyPrefix)),("Postfix",typeof(HarmonyPostfix)),("Finalizer",typeof(HarmonyFinalizer))})
    Eq(true,typeof(Patch).GetMethod(pair.Item1,BindingFlags.Static|BindingFlags.NonPublic).IsDefined(pair.Item2),pair.Item1+" annotated");
  });
  Test("Eligible Update temporarily scales three densities and interval, then restores exactly once",()=>
  {
   var c=New();Run(c,()=>Boosted(c));Original(c);Eq(1,c.NativeCalls,"native still runs once");foreach(int n in c.Writes)Eq(2,n,"one application and one restore despite both cleanup hooks");
  });
  Test("Native refill decisions reach 3x same-window replenishment and stop at 3x capacity",()=>
  {
   var normal=New();var boosted=New();
   for(int i=0;i<36;i++)
   {ModConfig.Enabled.Value=false;Run(normal,normal.SimulateNativeDecisions);ModConfig.Enabled.Value=true;Run(boosted,boosted.SimulateNativeDecisions);Original(boosted);}
   Eq(3,normal.SpawnDecisions,"three baseline replenishments in nine seconds");Eq(9,boosted.SpawnDecisions,"nine boosted replenishments in same window");
   for(int i=0;i<320;i++)
   {ModConfig.Enabled.Value=false;Run(normal,normal.SimulateNativeDecisions);ModConfig.Enabled.Value=true;Run(boosted,boosted.SimulateNativeDecisions);}
   Eq(10,normal.Population,"baseline ceil capacity");Eq(30,boosted.Population,"3x density ceil capacity");Eq(30,boosted.SpawnDecisions,"stops replenishment at capacity");Eq(normal.NativeCalls,boosted.NativeCalls,"native Update frequency unchanged");
   Eq(0,boosted.ElapsedPatchWrites,"native elapsed state never written by patch");Eq(0,boosted.TargetPatchWrites,"native target state never written by patch");
  });
  Test("Native winter zero remains no-spawn and special winter uses the same native selection",()=>
  {
   var c=New();Managers.Inst.world.IsWinter=true;for(int i=0;i<160;i++)Run(c,c.SimulateNativeDecisions);Eq(0,c.SpawnDecisions,"zero winter density remains zero");
   c.SpecialWinter=true;for(int i=0;i<160;i++)Run(c,c.SimulateNativeDecisions);Eq(15,c.Population,"special winter original cap five becomes fifteen");Original(c);
  });
  Test("Native minimum region and three-coin deer prefab remain unchanged",()=>
  {
   var c=New();var prefab=c.prefab;var scale=prefab.transform.localScale;var deer=prefab.GetComponent<Deer>();c.RegionWidth=20;
   for(int i=0;i<80;i++)Run(c,c.SimulateNativeDecisions);Eq(0,c.SpawnDecisions,"native minimum region still rejects");Eq(20f,c.minimumRegionSize,"region input untouched");Eq(3,deer.numCoinsDropped,"each deer still drops three coins");Eq(0,deer.CoinWrites,"no loot writes");Eq(prefab,c.prefab,"same native pool prefab");Eq(scale,prefab.transform.localScale,"existing deer scale unchanged");Eq(3f,c.updateInterval,"serialized updateInterval unchanged");
  });
  Test("Disabled mod client and non-Greek worlds have no temporary writes",()=>
  {
   for(int mode=0;mode<3;mode++)
   {
    ModConfig.Enabled.Value=true;NetworkBigBoss.HasWorldAuth=true;BiomeHolder.Inst.BiomeIndex=BiomeHolder.GreeceBiomeIndex;
    if(mode==0)ModConfig.Enabled.Value=false;else if(mode==1)NetworkBigBoss.HasWorldAuth=false;else BiomeHolder.Inst.BiomeIndex=1;
    var c=New();Run(c,()=>Original(c));foreach(int n in c.Writes)Eq(0,n,"excluded Update no writes");Eq(1,c.NativeCalls,"excluded Update stays native");
   }
  });
  Test("Biome critters non-Deer Steed and Hind prefabs are excluded by components",()=>
  {
   for(int mode=0;mode<5;mode++)
   {
    var c=New(mode!=1);if(mode==0)c.useBiomeCritters=true;else if(mode==2)c.prefab.AddComponent<Steed>();else if(mode==3)c.prefab.AddComponent<Hind>();else if(mode==4)c.prefab=null;
    Run(c,()=>Original(c));foreach(int n in c.Writes)Eq(0,n,"excluded prefab no writes");
   }
  });
  Test("Inactive disabled different-scene and same-scene old-layer controllers are excluded",()=>
  {
   for(int mode=0;mode<4;mode++)
   {
    var c=New();if(mode==0)c.gameObject.activeInHierarchy=false;else if(mode==1)c.enabled=false;else if(mode==2)c.gameObject.scene=new(){handle=99};else c.transform.parent=new GameObject().transform;
    Run(c,()=>Original(c));foreach(int n in c.Writes)Eq(0,n,"out-of-scope controller no writes");
   }
  });
  Test("Missing world layer or playing context never adjusts inputs",()=>
  {
   for(int mode=0;mode<3;mode++)
   {
    Managers.Inst=new();var c=New();if(mode==0)Managers.Inst.world=null;else if(mode==1)Managers.Inst.world.gameLayer=null;else {Managers.Inst.game.state=Game.State.Loading;Managers.Inst.game.playingOrInMenuWithClient=false;}
    Run(c,()=>Original(c));foreach(int n in c.Writes)Eq(0,n,"missing context no writes");
   }
  });
  Test("Native menu-with-client eligibility is respected instead of a narrower state enum gate",()=>
  {
   foreach(var state in new[]{(Game.State.Menu,true),(Game.State.Menu,false),(Game.State.Loading,false)})
   {
    Managers.Inst.game.state=state.Item1;Managers.Inst.game.playingOrInMenuWithClient=state.Item2;var c=New();
    Run(c,()=>{if(state.Item2)Boosted(c);else Original(c);});Original(c);foreach(int n in c.Writes)Eq(state.Item2?2:0,n,"native playing/menu boolean gate");
   }
  });
  Test("Same-controller nested Update sees 3x not 9x and nested cleanup cannot restore outer lease",()=>
  {
   var c=New();Run(c,()=>{Boosted(c);Run(c,()=>Boosted(c));Boosted(c);});Original(c);Eq(2,c.NativeCalls,"both native bodies run");foreach(int n in c.Writes)Eq(2,n,"outer lease only");
  });
  Test("Different controllers have independent nested leases",()=>
  {
   var a=New();var b=New();Run(a,()=>{Run(b,()=>{Boosted(a);Boosted(b);});Original(b);Boosted(a);});Original(a);Original(b);
  });
  Test("Native exception is preserved and Finalizer restores fields then permits next Update",()=>
  {
   var c=New();var original=new InvalidOperationException("native Update fault");try{Run(c,()=>throw original);throw new Exception("expected native exception");}catch(Exception ex){Eq(original,ex,"original exception identity");}
   Original(c);Run(c,()=>Boosted(c));Original(c);Eq(2,c.NativeCalls,"next Update admitted after exception");
  });
  Test("External different field values survive both cleanup hooks while other fields restore",()=>
  {
   var c=New();Run(c,()=>{c.NativeSet(0,9);c.NativeSet(2,8);c.NativeSet(3,7);});Eq(9f,c.density,"external density preserved");Eq(0f,c.winterDensityDefault,"owned winter default restored");Eq(8f,c.winterDensitySpecial,"external winter preserved");Eq(7f,c._actualUpdateInterval,"external interval preserved");
  });
  Test("Cleanup restores exact live controller despite config authority world and active-state changes",()=>
  {
   var c=New();Run(c,()=>{ModConfig.Enabled.Value=false;NetworkBigBoss.HasWorldAuth=false;Managers.Inst.world=new();c.gameObject.activeInHierarchy=false;c.enabled=false;});Original(c);
  });
  Test("Controller or GO identity replacement makes cleanup retire without writing to replacement",()=>
  {
   for(int mode=0;mode<4;mode++)
   {
    var c=New();Run(c,()=>{if(mode==0)c.Id+=10000;else if(mode==1)c.Pointer=(IntPtr)777777;else if(mode==2)c.gameObject.Id+=10000;else c.gameObject.Pointer=(IntPtr)888888;});
    foreach(int n in c.Writes)Eq(1,n,"replaced identity has no restore writes");
   }
  });
  Test("All invalid inputs are rejected before any write including overflow and underflow",()=>
  {
   foreach(var bad in new[]{(0,float.NaN),(1,float.PositiveInfinity),(2,-.1f),(0,float.MaxValue),(3,0f),(3,-1f),(3,float.NaN),(3,float.Epsilon)})
   {var c=New();c.NativeSet(bad.Item1,bad.Item2);Run(c);foreach(int n in c.Writes)Eq(0,n,"invalid whole lease rejected");}
  });
  Test("Input read failure leaves every field untouched",()=>
  {
   var c=New();c.FailReadField=2;Run(c);foreach(int n in c.Writes)Eq(0,n,"all reads precede writes");Eq(1,c.NativeCalls,"patch failure does not suppress native Update");
  });
  Test("Partial setter failure rolls back own completed writes before native Update",()=>
  {
   foreach(bool committed in new[]{false,true})
   foreach(int failedField in new[]{0,1,2,3})
   {var c=New();c.FailWriteField=failedField;c.CommitBeforeWriteFault=committed;Run(c,()=>Original(c));Original(c);Eq(1,c.NativeCalls,"native runs after rolled-back patch fault");}
  });
  Test("One cleanup accessor failure does not skip other fields or swallow native exception",()=>
  {
   var c=New();var original=new Exception("native failure");try{Run(c,()=>{c.FailReadField=0;throw original;});}catch(Exception ex){Eq(original,ex,"native error unchanged");}
   c.FailReadField=-1;Eq(.375f,c.density,"unreadable field was not blindly overwritten");Eq(0f,c.winterDensityDefault,"other field cleaned");Eq(.0625f,c.winterDensitySpecial,"other winter cleaned");Eq(3f,c._actualUpdateInterval,"interval cleaned");
   Eq(1,Pending.Count,"only failed invocation retained");Run(c,()=>Boosted(c));Original(c); // Must see .375, not compounded 1.125.
  });
  Test("Persistent cleanup failure cannot compound density and mod-off Update retries restoration",()=>
  {
   var c=New();Run(c,()=>c.FailReadField=0);int writes=c.Writes[0];
   for(int i=0;i<4;i++)Run(c,()=>Eq(.375f,c.NativeInspect(0),"remaining input stays 3x not 9x"));
   Eq(writes,c.Writes[0],"no new application during unresolved cleanup");Eq(1,Pending.Count,"one pending receipt");
   ModConfig.Enabled.Value=false;NetworkBigBoss.HasWorldAuth=false;c.FailReadField=-1;Run(c,()=>Original(c));Original(c);Eq(writes+1,c.Writes[0],"cleanup retried before disabled/client eligibility");
  });
  Test("Pending external field takeover clears ownership without overriding writer",()=>
  {
   var c=New();Run(c,()=>c.FailReadField=0);c.FailReadField=-1;c.NativeSet(0,9f);ModConfig.Enabled.Value=false;int writes=c.Writes[0];
   Run(c,()=>Eq(9f,c.density,"external takeover retained"));Eq(writes,c.Writes[0],"no pending restore over external field");
  });
  Test("Pending receipt for a reused controller identity is discarded without a restore write",()=>
  {
   var c=New();Run(c,()=>c.FailReadField=0);c.FailReadField=-1;c.Id+=10000;c.NativeSet(0,.125f);ModConfig.Enabled.Value=false;int writes=c.Writes[0];
   Run(c,()=>Original(c));Eq(writes,c.Writes[0],"old generation never restores into reused identity");
  });
  Test("Old pending finalizer cannot restore a newer active lease",()=>
  {
   var c=New();Patch.Prefix(c,out var old);c.FailReadField=0;Patch.Postfix(old);Eq(1,Pending.Count,"old cleanup pending");c.FailReadField=-1;
   Patch.Prefix(c,out var current);Boosted(c);Eq(1,Active.Count,"new invocation active");Patch.Finalizer(null,old);Boosted(c);Eq(1,Active.Count,"old finalizer cannot clear new token");
   Patch.Postfix(current);Patch.Finalizer(null,current);Original(c);
  });
  Test("Same-token finalizer can immediately finish a failed postfix cleanup",()=>
  {
   var c=New();Patch.Prefix(c,out var state);c.FailReadField=0;Patch.Postfix(state);Eq(1,Pending.Count,"postfix left failed bit");
   c.FailReadField=-1;Patch.Finalizer(null,state);Original(c);foreach(int writes in c.Writes)Eq(2,writes,"successful fields not restored twice");
  });
  Test("Temporary pending identity accessor fault retains ownership until identity can be checked",()=>
  {
   var c=New();Run(c,()=>c.FailReadField=0);c.FailReadField=-1;c.ThrowIdentity=true;int writes=c.Writes[0];
   Run(c);Eq(1,Pending.Count,"unverifiable identity keeps original receipt");Eq(writes,c.Writes[0],"identity fault prevents new borrowing");
   c.ThrowIdentity=false;Run(c,()=>Boosted(c));Original(c);
  });
  Test("Applied evidence log is bounded and identifies prefab plus density and interval change",()=>
  {
   var c=New();c.prefab.name="OrdinaryDeer";Run(c);Run(c);var messages=KingdomEnhancedPlugin.Instance.LogSource.Info;Eq(1,messages.Count,"one application log");Eq(true,messages[0].Contains("prefab=OrdinaryDeer"),"prefab evidence");Eq(true,messages[0].Contains("density=0.125->0.375"),"density evidence");Eq(true,messages[0].Contains("interval=3->1"),"interval evidence");
  });
  Console.WriteLine($"RESULT: {passed} passed, {failed} failed");Environment.ExitCode=failed==0?0:1;
 }
}
