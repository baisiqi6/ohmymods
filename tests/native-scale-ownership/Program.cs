using System.Collections;
using System.Reflection;
using System.Text.Json;
using UnityEngine;
using KingdomEnhancedMod;
using Scope=KingdomEnhancedMod.GreekScaleScope;

static class Tests
{
 const BindingFlags Static=BindingFlags.Static|BindingFlags.NonPublic;
 const BindingFlags Instance=BindingFlags.Instance|BindingFlags.NonPublic;
 static int checks;static readonly List<object> results=new();
 static object Field(Type t,string name)=>t.GetField(name,Static).GetValue(null);
 static IDictionary Requests=>(IDictionary)Field(typeof(Scope),"Requests");
 static void Reset()
 {
  Requests.Clear();((IList)Field(typeof(Scope),"LateSnapshot")).Clear();
  typeof(Scope).GetField("_maintainingRegisteredY",Static).SetValue(null,false);
  BiomeHolder.Inst=new();ModConfig.Enabled=new(){Value=true};Time.frameCount+=1000;Time.time=0;
  typeof(ScaleRegistryHolder).GetField("<Instance>k__BackingField",Static).SetValue(null,null);
  typeof(ScaleRegistryHolder).GetField("_creationPending",Static).SetValue(null,false);
  typeof(ScaleRegistryHolder).GetField("_nextCreationRetryFrame",Static).SetValue(null,0);
  GameObject.HolderCreates=GameObject.HolderFailures=GameObject.HolderNullResults=0;
  ((IDictionary)Field(typeof(PatchRoles_KnightStyle),"States")).Clear();
  var names=(string[])Field(typeof(PatchRoles_KnightStyle),"SoldierControllerNames");
  var controllers=(RuntimeAnimatorController[])Field(typeof(PatchRoles_KnightStyle),"SoldierControllers");
  for(int i=0;i<controllers.Length;i++)controllers[i]=new(){name=names[i]};
  PatchRoles_Crossbowman.ApplyCalls=PatchRoles_Crossbowman.RestoreCalls=0;
  Scope.Tick();
 }
 static void Equal(float expected,float actual,string label)
 {checks++;if(MathF.Abs(expected-actual)>.000001f)throw new Exception($"{label}: expected {expected}, actual {actual}");}
 static void True(bool value,string label){checks++;if(!value)throw new Exception(label);}
 static void Case(string name,Action action)
 {Reset();try{action();results.Add(new{name,status="PASS"});Console.WriteLine("PASS "+name);}catch(Exception e){results.Add(new{name,status="FAIL",error=e.ToString()});Console.WriteLine("FAIL "+name+": "+e.Message);}}
 static Mover Actor(float nativeY=1)
 {var go=new GameObject();go.transform.localScale=new(-1,nativeY,1.4f);return go.AddComponent<Mover>();}
 static void Register(Mover m,float y){Scope.ApplyY(m.transform,y);ScaleRegistryHolder.Register(m,y);}
 static void NativeReset(Mover m,int direction=1)=>m.transform.localScale=new(direction,1,1);
 static void NoLateWrite(Mover m,Action change)
 {NativeReset(m);change();int writes=m.transform.Writes;Scope.MaintainRegisteredY();Equal(writes,m.transform.Writes,"late made no write");Equal(1,m.transform.localScale.y,"native value preserved");}
 static object RequestFor(Mover m)=>Requests.Values.Cast<object>().Single(r=>ReferenceEquals(r.GetType().GetField("Target",Instance).GetValue(r),m.transform));
 static void SetRequest(object r,string n,object v)=>r.GetType().GetField(n,Instance).SetValue(r,v);
 static Archer Follower(int style,float originalY=1)
 {
  var mover=Actor(originalY);var a=mover.gameObject.AddComponent<Archer>();a._mover=mover;a._animator=mover.gameObject.AddComponent<Animator>();a._animator.runtimeAnimatorController=PatchRoles_Crossbowman.BaseSoldierAnimator;a.soldierAnimator=PatchRoles_Crossbowman.BaseSoldierAnimator;
  var k=new GameObject("knight").AddComponent<Knight>();a._knight=k;
  SetStyle(k,style);return a;
 }
 static void SetStyle(Knight k,int style,bool hasStyle=true)
 {
  Type type=typeof(PatchRoles_KnightStyle).GetNestedType("KnightStyleState",BindingFlags.NonPublic);object state=Activator.CreateInstance(type);
  type.GetField("Knight",Instance).SetValue(state,k);type.GetField("StyleIndex",Instance).SetValue(state,style);type.GetField("HasStyle",Instance).SetValue(state,hasStyle);
  ((IDictionary)Field(typeof(PatchRoles_KnightStyle),"States"))[k.gameObject.GetInstanceID()]=state;
 }
 static void Convert(Archer a,bool hunter=false)
 {Type t=hunter?typeof(Archer_ConvertToHunter_KnightStyleSkin_Patch):typeof(Archer_ConvertToSoldier_KnightStyleSkin_Patch);t.GetMethod("Postfix",Static).Invoke(null,new object[]{a});}
 static void Main()
 {
  Case("late direction reset repaired through actual holder LateUpdate",()=>{var m=Actor();Register(m,1.15f);NativeReset(m);Mover_Update_Patch.Mover_Update_Postfix(m);NativeReset(m,-1);typeof(ScaleRegistryHolder).GetMethod("LateUpdate",Instance).Invoke(ScaleRegistryHolder.Instance,null);Equal(1.15f,m.transform.localScale.y,"Y repaired");Equal(-1,m.transform.localScale.x,"facing");Equal(1,m.transform.localScale.z,"native z");});
  Case("RecvAbsFace fixture repaired preserving current XZ",()=>{var m=Actor();Register(m,.896f);m.transform.localScale=new(1,1,4);Scope.MaintainRegisteredY();Equal(.896f,m.transform.localScale.y,"Y repaired");Equal(1,m.transform.localScale.x,"x");Equal(4,m.transform.localScale.z,"z");int n=m.transform.Writes;Scope.MaintainRegisteredY();Equal(n,m.transform.Writes,"stable no redundant setter");});
  Case("Register retains worker immediate1.175 deferred1.2 sequence",()=>{var m=Actor();Scope.ApplyY(m.transform,1.175f);ScaleRegistryHolder.Register(m,1.2f);Equal(1.175f,m.transform.localScale.y,"Register not immediate");Mover_Update_Patch.Mover_Update_Postfix(m);Equal(1.2f,m.transform.localScale.y,"existing postfix applies target");Equal(1,GameObject.HolderCreates,"one holder");ScaleRegistryHolder.Register(m,1.2f);Equal(1,GameObject.HolderCreates,"no duplicate holder");});
  Case("driver creation failure keeps registration and bounded Tick retry",()=>{var m=Actor();GameObject.HolderFailures=1;ScaleRegistryHolder.Register(m,1.2f);True(Scope.TryGet(m,out float y)&&y==1.2f,"request survives failed AddComponent");Equal(1,m.transform.localScale.y,"still deferred");True(ScaleRegistryHolder.Instance==null,"driver pending");Time.frameCount+=29;Scope.Tick();Equal(1,GameObject.HolderCreates,"no early retry");Time.frameCount++;Scope.Tick();Equal(2,GameObject.HolderCreates,"one retry at30");True(ScaleRegistryHolder.Instance!=null,"driver created");Scope.MaintainRegisteredY();Equal(1.2f,m.transform.localScale.y,"retained request applied");});
  Case("AddComponent null result remains pending and retries",()=>{var m=Actor();GameObject.HolderNullResults=1;ScaleRegistryHolder.Register(m,1.15f);True(ScaleRegistryHolder.Instance==null,"null result not success");True(Scope.TryGet(m,out _),"request retained");Time.frameCount+=30;Scope.Tick();True(ScaleRegistryHolder.Instance!=null,"null result retried");Equal(2,GameObject.HolderCreates,"one null one success");});
  Case("scope foreign skips late and existing Tick restores ownership",()=>{var m=Actor(.8f);Register(m,1.15f);BiomeHolder.Inst.BiomeIndex=0;int n=m.transform.Writes;Scope.MaintainRegisteredY();Equal(n,m.transform.Writes,"foreign no late writes");Scope.Tick();Equal(.8f,m.transform.localScale.y,"Tick restore remains");});
  Case("scope disabled skips late and existing Mover restores",()=>{var m=Actor(.8f);Register(m,1.15f);ModConfig.Enabled.Value=false;int n=m.transform.Writes;Scope.MaintainRegisteredY();Equal(n,m.transform.Writes,"off no late writes");Mover_Update_Patch.Mover_Update_Postfix(m);Equal(.8f,m.transform.localScale.y,"Mover restoration remains");});
  Case("unknown scope retains ownership with zero late write",()=>{var m=Actor(.8f);Register(m,1.15f);NoLateWrite(m,()=>BiomeHolder.Inst=null);True(Scope.NativeScale(m.transform).y==1,"external reset not overwritten");BiomeHolder.Inst=new();Scope.MaintainRegisteredY();Equal(1.15f,m.transform.localScale.y,"knownGreek resumes");});
  Case("paused native Update bypass still allows active registered late guard",()=>{var m=Actor();Register(m,1.064f);NativeReset(m);Scope.MaintainRegisteredY();Equal(1.064f,m.transform.localScale.y,"no native Update prerequisite");});
  Case("disabled Mover skipped and same-life reenable resumes",()=>{var m=Actor();Register(m,1.15f);NoLateWrite(m,()=>m.enabled=false);m.enabled=true;Scope.MaintainRegisteredY();Equal(1.15f,m.transform.localScale.y,"same life reenabled");});
  Case("inactive pooled root skipped without deleting request",()=>{var m=Actor();Register(m,1.15f);NoLateWrite(m,()=>m.gameObject.activeInHierarchy=false);True(Requests.Count==1,"ownership retained");m.gameObject.activeInHierarchy=true;Scope.MaintainRegisteredY();Equal(1.15f,m.transform.localScale.y,"same-life reactivation");});
  Case("embarked actor is not rescaled; returning ashore resumes",()=>{var m=Actor();var e=m.gameObject.AddComponent<Embarkee>();Register(m,1.15f);NoLateWrite(m,()=>e.IsEmbarked=true);e.IsEmbarked=false;Scope.MaintainRegisteredY();Equal(1.15f,m.transform.localScale.y,"ashore target");});
  Case("targeting embarkable is not rescaled",()=>{var m=Actor();var e=m.gameObject.AddComponent<Embarkee>();Register(m,1.15f);NoLateWrite(m,()=>e.IsTargetingEmbarkable=true);});
  Case("fresh component lookup sees Embarkee added after registration",()=>{var m=Actor();Register(m,1.15f);NoLateWrite(m,()=>m.gameObject.AddComponent<Embarkee>().IsEmbarked=true);});
  Case("invalid scene zero write",()=>{var m=Actor();Register(m,1.15f);NoLateWrite(m,()=>m.gameObject.scene=new(){valid=false});});
  Case("stale transform pointer zero write",()=>{var m=Actor();Register(m,1.15f);NoLateWrite(m,()=>m.transform.Pointer=IntPtr.Zero);});
  Case("stale Mover pointer zero write",()=>{var m=Actor();Register(m,1.15f);NoLateWrite(m,()=>m.Pointer=(IntPtr)987654);});
  Case("reused Mover ID zero write",()=>{var m=Actor();Register(m,1.15f);NoLateWrite(m,()=>m.Id++);});
  Case("changed Transform or GameObject identity cannot inherit registered ownership",()=>{var m=Actor();Register(m,1.15f);NoLateWrite(m,()=>m.transform.Id++);var next=Actor();Register(next,1.15f);NoLateWrite(next,()=>next.gameObject.Id++);});
  Case("native identity exception defers only failing request",()=>{var bad=Actor();var good=Actor();Register(bad,1.15f);Register(good,1.064f);NativeReset(bad);NativeReset(good);bad.FailIdentity=true;Scope.MaintainRegisteredY();Equal(1,bad.transform.localScale.y,"failed identity no write");Equal(1.064f,good.transform.localScale.y,"healthy request continues");bad.FailIdentity=false;Time.frameCount+=30;Scope.MaintainRegisteredY();Equal(1.15f,bad.transform.localScale.y,"identity retry");});
  Case("foreign child Mover cannot authorize registered root",()=>{var m=Actor();Register(m,1.15f);var root=m.transform;NativeReset(m);m.gameObject=new GameObject("different-child");int n=root.Writes;Scope.MaintainRegisteredY();Equal(n,root.Writes,"oldroot not touched");Equal(1,m.transform.localScale.y,"child not touched");});
  Case("multi-axis currency request excluded even with Mover registration",()=>{var m=Actor();Scope.ApplyScale(m.transform,new(2,3,4));ScaleRegistryHolder.Register(m,1.15f);NoLateWrite(m,()=>{});});
  Case("existing XZ ownership excluded from strict Y guard",()=>{var m=Actor();Scope.ApplyScale(m.transform,new(2,3,4));ScaleRegistryHolder.Register(m,1.15f);SetRequest(RequestFor(m),"Axes",2);NoLateWrite(m,()=>{});});
  Case("pending extra-axis responsibility excluded",()=>{var m=Actor();m.transform.FailWrite=true;m.transform.CommitBeforeFailure=true;Scope.ApplyScale(m.transform,new(2,3,4));ScaleRegistryHolder.Register(m,1.15f);SetRequest(RequestFor(m),"Axes",2);NoLateWrite(m,()=>{});});
  Case("late setter commit-then-throw retains pending and original per-axis",()=>{var m=Actor(.8f);Register(m,1.15f);NativeReset(m);m.transform.FailWrite=true;m.transform.CommitBeforeFailure=true;Scope.MaintainRegisteredY();Equal(1.15f,m.transform.localScale.y,"setter committed");m.transform.localScale=new(-1,1.15f,5);Time.frameCount+=30;Scope.MaintainRegisteredY();Scope.Restore(m.transform);Equal(.8f,m.transform.localScale.y,"baseline retained");Equal(-1,m.transform.localScale.x,"foreign facing retained");Equal(5,m.transform.localScale.z,"foreign z retained");});
  Case("late setter noncommit retry backs off without blocking healthy actors",()=>{var bad=Actor();var good=Actor();Register(bad,1.15f);Register(good,1.064f);NativeReset(bad);NativeReset(good);bad.transform.FailWrite=true;Scope.MaintainRegisteredY();Equal(1,bad.transform.localScale.y,"bad setter uncommitted");Equal(1.064f,good.transform.localScale.y,"healthy repaired");int n=bad.transform.Reads;Scope.MaintainRegisteredY();Equal(n,bad.transform.Reads,"late backs off");Time.frameCount+=30;Scope.MaintainRegisteredY();Equal(1.15f,bad.transform.localScale.y,"retry repair");});
  Case("unregister cancels late binding without losing scope responsibility",()=>{var m=Actor(.8f);Register(m,1.15f);ScaleRegistryHolder.Unregister(m);NativeReset(m);int n=m.transform.Writes;Scope.MaintainRegisteredY();Equal(n,m.transform.Writes,"unbound no write");True(!Scope.TryGet(m,out _),"binding cleared");Scope.Restore(m.transform);True(Requests.Count==0,"explicitRestore release");});
  Case("snapshot ignores a replaced lease; reentrant late returns safely",()=>{var first=Actor();var later=Actor();Register(first,1.15f);Register(later,1.064f);NativeReset(first);NativeReset(later);first.transform.OnWrite=()=>{first.transform.OnWrite=null;Scope.Restore(later.transform);Register(later,1.05f);NativeReset(later);Scope.MaintainRegisteredY();};Scope.MaintainRegisteredY();Equal(1,later.transform.localScale.y,"old snapshot did not maintain replacement");Scope.MaintainRegisteredY();Equal(1.05f,later.transform.localScale.y,"next pass current lease");});
  Case("external Y survives CAS scope release",()=>{var m=Actor(.8f);Register(m,1.15f);m.transform.localScale=new(1,2,3);Scope.Restore(m.transform);Equal(2,m.transform.localScale.y,"foreign Y retained");Equal(1,m.transform.localScale.x,"x retained");Equal(3,m.transform.localScale.z,"z retained");});
  Case("medieval Convert event applies current production1.064 immediately",()=>{var a=Follower(0);Convert(a);Equal(1.064f,a.transform.localScale.y,"eventY");NativeReset(a._mover);Scope.MaintainRegisteredY();Equal(1.064f,a.transform.localScale.y,"late uses new event target");});
  Case("norse Convert event retains current production1.15",()=>{var a=Follower(4);Convert(a,true);Equal(1.15f,a.transform.localScale.y,"norse eventY");});
  Case("deadlands package remains scale owner and transition to med releases first",()=>{var a=Follower(1);Convert(a);Equal(1.15f,a.transform.localScale.y,"dead package");True(a.Package&&PatchRoles_Crossbowman.ApplyCalls==1,"existingpackage owner");SetStyle(a._knight,0);a.transform.OnWrite=()=>True(!a.Package,"oldpackage released before next scale");Convert(a);a.transform.OnWrite=null;Equal(1.064f,a.transform.localScale.y,"new medY");True(!a.Package,"oldpackage cleared");});
  Case("shogun/greece event immediately restores prior custom follower size",()=>{foreach(int style in new[]{2,3}){var a=Follower(0);Convert(a);SetStyle(a._knight,style);Convert(a);Equal(1,a.transform.localScale.y,"nonscaled style restores");True(!Scope.TryGet(a._mover,out _),"bindingremoved");}});
  Case("Norse prefab effective style overrides medieval Knight target",()=>{var a=Follower(0);a.Norse=true;Convert(a);Equal(1.15f,a.transform.localScale.y,"effectiveNorseY");});
  Case("leave Knight ConvertToHunter restores original and cancels old target",()=>{var a=Follower(0,.8f);Convert(a);a._knight=null;Convert(a,true);Equal(.8f,a.transform.localScale.y,"leave restore");NativeReset(a._mover);Scope.MaintainRegisteredY();Equal(1,a.transform.localScale.y,"old target cannot bleed");});
  Case("missing Knight style event immediately restores stale request",()=>{var a=Follower(4);Convert(a);((IDictionary)Field(typeof(PatchRoles_KnightStyle),"States")).Clear();Convert(a);Equal(1,a.transform.localScale.y,"missingstate restoration");});
  Case("unresolved Knight style event immediately restores stale request",()=>{var a=Follower(0);Convert(a);SetStyle(a._knight,0,false);Convert(a);Equal(1,a.transform.localScale.y,"nostyle restoration");});
  Case("true crossbow marker early return retains its scale/package",()=>{var a=Follower(0);a.Marker=true;Register(a._mover,1.15f);int n=a.transform.Writes;Convert(a);Equal(n,a.transform.Writes,"marker untouched");Equal(0,PatchRoles_Crossbowman.RestoreCalls,"markerpackage not dismantled");Equal(1.15f,a.transform.localScale.y,"marker scale retained");});
  Case("pool new-life Convert removes old med target while same-life hide keeps it",()=>{var a=Follower(0);Convert(a);a.gameObject.activeInHierarchy=false;NoLateWrite(a._mover,()=>{});a.gameObject.activeInHierarchy=true;a._knight=null;Convert(a,true);NativeReset(a._mover);Scope.MaintainRegisteredY();Equal(1,a.transform.localScale.y,"new plainlife no old target");});
  Case("after warmup registered traversal allocates no managed collection storage",()=>{for(int i=0;i<200;i++)Register(Actor(),1.15f);for(int i=0;i<100;i++)Scope.MaintainRegisteredY();long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<1000;i++)Scope.MaintainRegisteredY();long bytes=GC.GetAllocatedBytesForCurrentThread()-before;True(bytes==0,"managed stub traversal allocated "+bytes+" bytes");});
  string summary=JsonSerializer.Serialize(new{scope="three complete production files directly linked; native boundaries are managed stubs",checks,cases=results.Count,results},new JsonSerializerOptions{WriteIndented=true});Console.WriteLine(summary);
  if(results.Any(r=>r.GetType().GetProperty("status").GetValue(r)?.ToString()=="FAIL"))Environment.Exit(1);
 }
}
