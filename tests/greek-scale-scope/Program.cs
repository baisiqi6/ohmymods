using System.Collections;
using System.Reflection;
using KingdomEnhancedMod;
using UnityEngine;
using Scope=KingdomEnhancedMod.GreekScaleScope;

static class Program
{
 static int passed,failed;
 static void Eq(float expected,float actual,string label="scale"){if(expected!=actual)throw new Exception($"{label}: {actual}, expected {expected}");}
 static void True(bool value,string label){if(!value)throw new Exception(label);}
 static void Test(string label,Action test)
 {
  ((IDictionary)typeof(Scope).GetField("Requests",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null)).Clear();
  BiomeHolder.Inst=new();ModConfig.Enabled=new();ModConfig.Enabled.Value=true;NetworkBigBoss.HasWorldAuth=true;Time.frameCount+=500;Scope.Tick();
  try{test();passed++;Console.WriteLine("PASS "+label);}catch(Exception e){failed++;Console.WriteLine("FAIL "+label+": "+e.Message);}
 }
 static Mover New(float y=.8f,string name="actor",float x=-1,float z=1.4f)
 {var go=new GameObject(name);go.transform.localScale=new(x,y,z);return go.AddComponent<Mover>();}
 static void Retry(){Time.frameCount+=30;Scope.Tick();}
 static void World(int index){BiomeHolder.Inst=new(){BiomeIndex=index};Scope.Tick();}
 static void Hook(Type type,string method,params object[] args)=>type.GetMethod(method,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);
 static void Y(Mover m,float desired){Scope.ApplyY(m.transform,desired);Scope.Register(m,desired);}
 static void Main()
 {
  Test("Greek y only preserves facing and z",()=>{var m=New();Y(m,1.2f);Eq(1.2f,m.transform.localScale.y);Eq(-1,m.transform.localScale.x);Eq(1.4f,m.transform.localScale.z);});
  Test("foreign Ninja never touched",()=>{World(1);var m=New(.8f,"Ninja");var n=m.gameObject.AddComponent<Ninja>();int writes=m.transform.Writes;Hook(typeof(NinjaStyleScale_Patch),"OnStyleSwap_Postfix",n,0);Scope.Maintain(m);Eq(.8f,m.transform.localScale.y);Eq(writes,m.transform.Writes);True(!Scope.TryGet(m,out _),"foreign registry inactive");});
  Test("foreign imported Ninja scales in Greek",()=>{var m=New(.8f,"Ninja_shogun");var n=m.gameObject.AddComponent<Ninja>();Hook(typeof(NinjaStyleScale_Patch),"OnStyleSwap_Postfix",n,0);Eq(1.1f,m.transform.localScale.y);});
  Test("Greek to foreign restores nonunit baseline",()=>{var m=New();Y(m,1.2f);World(1);Eq(.8f,m.transform.localScale.y);Scope.Maintain(m);Eq(.8f,m.transform.localScale.y);});
  Test("Greek foreign Greek desired survives",()=>{var m=New();Y(m,1.2f);World(1);World(3);Eq(1.2f,m.transform.localScale.y);World(1);Eq(.8f,m.transform.localScale.y);});
  Test("disabled enabled restores and reapplies",()=>{var m=New(1.4f);Y(m,1.2f);ModConfig.Enabled.Value=false;Scope.Tick();Eq(1.4f,m.transform.localScale.y);ModConfig.Enabled.Value=true;Scope.Tick();Eq(1.2f,m.transform.localScale.y);});
  Test("spawn while disabled queues desired",()=>{ModConfig.Enabled.Value=false;var m=New();Y(m,1.2f);Eq(.8f,m.transform.localScale.y);ModConfig.Enabled.Value=true;Scope.Tick();Scope.Maintain(m);Eq(1.2f,m.transform.localScale.y);});
  Test("foreign first request retained for Greek",()=>{World(1);var m=New();Y(m,1.2f);Eq(.8f,m.transform.localScale.y);World(3);Eq(1.2f,m.transform.localScale.y);World(1);Eq(.8f,m.transform.localScale.y);});
  Test("unknown first request defers",()=>{BiomeHolder.Inst=null;Scope.Tick();var m=New();Y(m,1.2f);Eq(.8f,m.transform.localScale.y);World(3);Eq(1.2f,m.transform.localScale.y);});
  Test("negative biome unknown defers",()=>{World(-1);var m=New();Y(m,1.2f);Eq(.8f,m.transform.localScale.y);World(3);Eq(1.2f,m.transform.localScale.y);});
  Test("unknown retains owned baseline and target",()=>{var m=New();Y(m,1.2f);BiomeHolder.Inst=null;Scope.Tick();Scope.Maintain(m);Eq(1.2f,m.transform.localScale.y);Scope.ApplyY(m.transform,1.3f);Eq(1.2f,m.transform.localScale.y);World(3);Eq(1.3f,m.transform.localScale.y);World(1);Eq(.8f,m.transform.localScale.y);});
  Test("unknown then foreign restores",()=>{var m=New();Y(m,1.2f);BiomeHolder.Inst=null;Scope.Tick();World(1);Eq(.8f,m.transform.localScale.y);});
  Test("unknown native write survives foreign exit",()=>{var m=New();Y(m,1.2f);BiomeHolder.Inst=null;Scope.Tick();m.transform.localScale=new(-1,1.4f,1.4f);Scope.Maintain(m);Eq(1.4f,m.transform.localScale.y);World(1);Eq(1.4f,m.transform.localScale.y);});
  Test("paused Mover restores without native setter",()=>{var m=New();Y(m,1.2f);BiomeHolder.Inst.BiomeIndex=1;Scope.Maintain(m);Eq(.8f,m.transform.localScale.y);});
  Test("paused disabled Mover restores",()=>{var m=New();Y(m,1.2f);ModConfig.Enabled.Value=false;Scope.Maintain(m);Eq(.8f,m.transform.localScale.y);});
  Test("native active Mover reset does not replace original baseline",()=>{var m=New();Y(m,1.2f);m.transform.localScale=new(1,1,1);Scope.Maintain(m);Eq(1.2f,m.transform.localScale.y);World(1);Eq(.8f,m.transform.localScale.y);Eq(1,m.transform.localScale.x);});
  Test("external y survives restoration",()=>{var m=New();Y(m,1.2f);m.transform.localScale=new(-1,1.4f,2);Scope.Restore(m.transform);Eq(1.4f,m.transform.localScale.y);Eq(2,m.transform.localScale.z);});
  Test("external changed native baseline captured on reentry",()=>{var m=New();Y(m,1.2f);World(1);m.transform.localScale=new(-1,1.4f,2);World(3);World(1);Eq(1.4f,m.transform.localScale.y);});
  Test("ApplyY does not compound or recapture custom scale",()=>{var m=New();for(int i=0;i<100;i++)Y(m,1.2f);World(1);Eq(.8f,m.transform.localScale.y);});
  Test("style swap original retained and fisher exactly one",()=>{var m=New();var n=m.gameObject.AddComponent<Ninja>();Hook(typeof(NinjaStyleScale_Patch),"OnStyleSwap_Postfix",n,0);Hook(typeof(NinjaStyleScale_Patch),"OnStyleSwap_Postfix",n,1);Scope.Maintain(m);Eq(1,m.transform.localScale.y);World(1);Eq(.8f,m.transform.localScale.y);});
  Test("client style sync applies without authority",()=>{NetworkBigBoss.HasWorldAuth=false;var m=New();var n=m.gameObject.AddComponent<Ninja>();n._animator.Fisher=false;Hook(typeof(NinjaStyleScale_Patch),"SetAnimation_Postfix",n,Ninja.APIsFisher);Eq(1.1f,m.transform.localScale.y);});
  Test("explicit Restore releases pending desired",()=>{var m=New();Y(m,1.2f);Scope.Restore(m.transform);World(1);World(3);Scope.Maintain(m);Eq(.8f,m.transform.localScale.y);True(!Scope.TryGet(m,out _),"released registry");});
  Test("Unregister only releases Mover binding",()=>{var m=New();Y(m,1.2f);Scope.ApplyY(m.transform,1);Scope.Unregister(m);Eq(1,m.transform.localScale.y);True(!Scope.TryGet(m,out _),"unbound");World(1);Eq(.8f,m.transform.localScale.y);World(3);Eq(1,m.transform.localScale.y);});
  Test("Worker historical immediate and registry sequence",()=>{var m=New();Scope.ApplyY(m.transform,1.175f);Scope.Register(m,1.2f);Eq(1.175f,m.transform.localScale.y);Scope.Maintain(m);Eq(1.2f,m.transform.localScale.y);World(1);Eq(.8f,m.transform.localScale.y);});
  Test("instance ID reuse cannot inherit old registry",()=>{var old=New();Y(old,1.2f);var m=New(1.4f);m.gameObject.Id=old.gameObject.Id;m.transform.Id=old.transform.Id;m.Id=old.Id;Scope.Maintain(m);Eq(1.4f,m.transform.localScale.y);True(!Scope.TryGet(m,out _),"new pointer differs");});
  Test("pointer reuse cannot inherit old registry",()=>{var old=New();Y(old,1.2f);var m=New(1.4f);m.Pointer=old.Pointer;m.transform.Pointer=old.transform.Pointer;m.gameObject.Pointer=old.gameObject.Pointer;Scope.Maintain(m);Eq(1.4f,m.transform.localScale.y);True(!Scope.TryGet(m,out _),"new IDs differ");});
  Test("replacement Mover on same object cannot inherit registration",()=>{var old=New();Y(old,1.2f);var m=old.gameObject.AddComponent<Mover>();True(!Scope.TryGet(m,out _),"Mover identity differs");});
  Test("destroyed identity prunes without touching replacement",()=>{var m=New();Y(m,1.2f);m.transform.Pointer=IntPtr.Zero;Time.frameCount+=500;Scope.Tick();True(((IDictionary)typeof(Scope).GetField("Requests",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null)).Count==0,"pruned");});
  Test("shared asset never written or registered",()=>{var m=New();m.gameObject.scene=new(){valid=false};int writes=m.transform.Writes;Y(m,1.2f);Scope.ApplyScale(m.transform,new(2,2,2));Eq(writes,m.transform.Writes);True(!Scope.TryGet(m,out _),"asset not registered");});
  Test("runtime inactive prefab eligible and native clone baseline returned",()=>{var m=New();Y(m,1.2f);var native=Scope.NativeScale(m.transform);Eq(.8f,native.y);Eq(-1,native.x);Eq(1.4f,native.z);});
  Test("NativeScale external axes remain current",()=>{var m=New();Y(m,1.2f);m.transform.localScale=new(2,1.2f,3);var n=Scope.NativeScale(m.transform);Eq(2,n.x);Eq(.8f,n.y);Eq(3,n.z);});
  Test("NativeScale external y remains current",()=>{var m=New();Y(m,1.2f);m.transform.localScale=new(2,1.4f,3);Eq(1.4f,Scope.NativeScale(m.transform).y);});
  Test("vector axes ownership restores individually",()=>{var m=New();Scope.ApplyScale(m.transform,new(2,3,4));m.transform.localScale=new(2,5,4);World(1);Eq(-1,m.transform.localScale.x);Eq(5,m.transform.localScale.y);Eq(1.4f,m.transform.localScale.z);});
  Test("vector foreign target never written",()=>{World(1);var m=New();int writes=m.transform.Writes;Scope.ApplyScale(m.transform,new(2,3,4));Eq(writes,m.transform.Writes);});
  Test("stable Tick does not write or reassert nonactor transforms",()=>{var m=New();Scope.ApplyScale(m.transform,new(2,3,4));Scope.Tick();m.transform.localScale=new(2,9,4);int writes=m.transform.Writes;for(int i=0;i<600;i++){Time.frameCount++;Scope.Tick();}Eq(writes,m.transform.Writes);Eq(9,m.transform.localScale.y);});
  Test("Banker nonGreek .8 preserved Greek 1.075 restored",()=>{World(1);var m=New();var b=m.gameObject.AddComponent<Banker>();Hook(typeof(GreeceBankerScale_Patch),"OnEnable_Postfix",b);Eq(.8f,m.transform.localScale.y);World(3);Eq(1.075f,m.transform.localScale.y);World(1);Eq(.8f,m.transform.localScale.y);});
  Test("Dog Greek target and foreign native",()=>{var m=New();var dog=m.gameObject.AddComponent<Dog>();Hook(typeof(DogScale_Patch),"OnEnable_Postfix",dog);Eq(1.3f,m.transform.localScale.y);World(1);Eq(.8f,m.transform.localScale.y);});
  foreach(var pair in new[]{(Hermit.HermitType.Baker,1.15f),(Hermit.HermitType.Horn,1.15f),(Hermit.HermitType.Horse,1.10f),(Hermit.HermitType.Ballista,1.20f),(Hermit.HermitType.Knight,1.05f),(Hermit.HermitType.Fire,1.25f)})
   Test("Hermit target preserved "+pair.Item1,()=>{var m=New(1.4f);var h=m.gameObject.AddComponent<Hermit>();h.Type=pair.Item1;Hook(typeof(HermitScale_Patch),"OnEnable_Postfix",h);Eq(pair.Item2,m.transform.localScale.y);World(1);Eq(1.4f,m.transform.localScale.y);});
  Test("identity read fault retains ownership for retry",()=>{var m=New();Y(m,1.2f);m.transform.Invalid=true;World(1);m.transform.Invalid=false;Retry();Eq(.8f,m.transform.localScale.y);});
  Test("scale read fault on scope exit retries",()=>{var m=New();Y(m,1.2f);m.transform.FailReads=1;World(1);Eq(1.2f,m.transform.localScale.y);Retry();Eq(.8f,m.transform.localScale.y);});
  Test("apply setter failure preserves original on retry",()=>{var m=New();m.transform.FailWrite=true;Y(m,1.2f);Eq(.8f,m.transform.localScale.y);Retry();Eq(1.2f,m.transform.localScale.y);World(1);Eq(.8f,m.transform.localScale.y);});
  Test("restore setter failure retains ownership on retry",()=>{var m=New();Y(m,1.2f);m.transform.FailWrite=true;World(1);Eq(1.2f,m.transform.localScale.y);Retry();Eq(.8f,m.transform.localScale.y);});
  Test("explicit Restore setter failure retries cancellation",()=>{var m=New();Y(m,1.2f);m.transform.FailWrite=true;Scope.Restore(m.transform);Eq(1.2f,m.transform.localScale.y);Retry();Eq(.8f,m.transform.localScale.y);World(1);World(3);Eq(.8f,m.transform.localScale.y);});
  Test("apply setter commits then throws keeps baseline",()=>{var m=New();m.transform.FailWrite=true;m.transform.CommitBeforeFailure=true;Y(m,1.2f);Eq(1.2f,m.transform.localScale.y);Retry();World(1);Eq(.8f,m.transform.localScale.y);});
  Test("uncertain apply followed by read fault retains original",()=>{var m=New();m.transform.FailWrite=true;m.transform.CommitBeforeFailure=true;Y(m,1.2f);m.transform.FailReads=1;Retry();Retry();World(1);Eq(.8f,m.transform.localScale.y);});
  Test("uncertain restore commits then throws preserves future reentry",()=>{var m=New();Y(m,1.2f);m.transform.FailWrite=true;m.transform.CommitBeforeFailure=true;World(1);Eq(.8f,m.transform.localScale.y);Retry();World(3);World(1);Eq(.8f,m.transform.localScale.y);});
  Test("NativeScale resolves uncertain committed write",()=>{var m=New();m.transform.FailWrite=true;m.transform.CommitBeforeFailure=true;Y(m,1.2f);Eq(.8f,Scope.NativeScale(m.transform).y);});
  Test("null config scope is unknown and nonthrowing",()=>{var m=New();Y(m,1.2f);ModConfig.Enabled=null;Retry();Scope.Maintain(m);Eq(1.2f,m.transform.localScale.y);ModConfig.Enabled=new(){Value=false};Retry();Eq(.8f,m.transform.localScale.y);});
  Test("biome read fault defers without ownership loss",()=>{var m=New();Y(m,1.2f);BiomeHolder.Inst.FailRead=true;Retry();Scope.Maintain(m);Eq(1.2f,m.transform.localScale.y);World(1);Eq(.8f,m.transform.localScale.y);});
  Test("uncertain cancel during unknown retains pending restoration",()=>{var m=New();m.transform.FailWrite=true;m.transform.CommitBeforeFailure=true;Y(m,1.2f);m.transform.FailReads=1;Scope.Restore(m.transform);BiomeHolder.Inst=null;Retry();World(1);Eq(.8f,m.transform.localScale.y);World(3);Eq(.8f,m.transform.localScale.y);});
  Test("NativeScale identity fault does not bake custom scale",()=>{var m=New();Y(m,1.2f);m.transform.Invalid=true;bool threw=false;try{Scope.NativeScale(m.transform);}catch(InvalidOperationException){threw=true;}True(threw,"deferred rather than custom baseline");m.transform.Invalid=false;Eq(.8f,Scope.NativeScale(m.transform).y);});
  Test("healthy ownership transaction is a value type",()=>{True(typeof(Scope).GetNestedType("PendingWrite",BindingFlags.NonPublic).IsValueType,"no per-Mover transaction allocation");});
  Test("uncertain applied y survives facing change before readback",()=>{var m=New();m.transform.FailWrite=true;m.transform.CommitBeforeFailure=true;Y(m,1.2f);m.transform.localScale=new(1,1.2f,2);var baseline=Scope.NativeScale(m.transform);Eq(.8f,baseline.y);Eq(1,baseline.x);Eq(2,baseline.z);World(1);Eq(.8f,m.transform.localScale.y);});
  Test("explicit coin native baseline replaces inherited custom parent scale",()=>{var m=New(2);Scope.ApplyScale(m.transform,new(1,1,1),new(1,1,1));World(1);Eq(1,m.transform.localScale.y);});
  Test("explicit baseline never writes untouched foreign coin",()=>{World(1);var m=New(2);int writes=m.transform.Writes;Scope.ApplyScale(m.transform,new(1,1,1),new(1,1,1));Eq(writes,m.transform.Writes);Eq(2,m.transform.localScale.y);});
  Test("explicit baseline survives unknown until first Greek write",()=>{World(-1);var m=New(2);Scope.ApplyScale(m.transform,new(1,1,1),new(.8f,.8f,.8f));Eq(2,m.transform.localScale.y);World(3);Eq(1,m.transform.localScale.y);World(1);Eq(.8f,m.transform.localScale.y);});
  Test("explicit repeated baseline does not replace owned original",()=>{var m=New(2);Scope.ApplyScale(m.transform,new(1,1,1),new(.8f,.8f,.8f));Scope.ApplyScale(m.transform,new(1,1,1),new(1.4f,1.4f,1.4f));World(1);Eq(.8f,m.transform.localScale.y);});
  Test("explicit baseline remains available for neutral copy",()=>{var m=New(2);Scope.ApplyScale(m.transform,new(1,1,1),new(.8f,.8f,.8f));Eq(.8f,Scope.NativeScale(m.transform).y);});
  Test("explicit baseline respects external surviving axes",()=>{var m=New(2);Scope.ApplyScale(m.transform,new(1,1,1),new(.8f,.8f,.8f));m.transform.localScale=new(1,3,1);World(1);Eq(3,m.transform.localScale.y);Eq(.8f,m.transform.localScale.x);});
  Test("ordinary request resets pending explicit baseline selection",()=>{World(1);var m=New(2);Scope.ApplyScale(m.transform,new(1,1,1),new(.8f,.8f,.8f));Scope.ApplyScale(m.transform,new(1,1,1));World(3);World(1);Eq(2,m.transform.localScale.y);});
  Test("indirect parent scaling during unknown restored in foreign",()=>{World(-1);var m=New(2);int writes=m.transform.Writes;Scope.ApplyInheritedScale(m.transform,new(1,1,1),new(1,1,1));Eq(writes,m.transform.Writes);Eq(2,m.transform.localScale.y);World(1);Eq(1,m.transform.localScale.y);});
  Test("indirect scale restores nonunit native baseline",()=>{var m=New(2.8f);Scope.ApplyInheritedScale(m.transform,new(1,1,1),new(.8f,1.4f,1.3f));Eq(1,m.transform.localScale.y);World(1);Eq(1.4f,m.transform.localScale.y);Eq(.8f,m.transform.localScale.x);Eq(1.3f,m.transform.localScale.z);});
  Test("indirect repeated ownership preserves first original",()=>{var m=New(2);Scope.ApplyInheritedScale(m.transform,new(1,1,1),new(.8f,.8f,.8f));Scope.ApplyInheritedScale(m.transform,new(1,1,1),new(1.4f,1.4f,1.4f));World(1);Eq(.8f,m.transform.localScale.y);});
  Console.WriteLine($"{passed} passed, {failed} failed");if(failed!=0)Environment.Exit(1);
 }
}
