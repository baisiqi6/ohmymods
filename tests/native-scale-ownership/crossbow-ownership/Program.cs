using System.Collections;
using System.Reflection;
using System.Text.Json;
using Il2CppInterop.Runtime.Injection;
using KingdomEnhancedMod;
using UnityEngine;
using Scope=KingdomEnhancedMod.GreekScaleScope;

static class Tests
{
 const BindingFlags Static=BindingFlags.NonPublic|BindingFlags.Static;
 const BindingFlags Instance=BindingFlags.NonPublic|BindingFlags.Instance;
 static readonly List<object> Results=new();static int Checks;
 static object Field(Type t,string n)=>t.GetField(n,Static).GetValue(null);
 static void Reset()
 {
  ((IDictionary)Field(typeof(Scope),"Requests")).Clear();((IList)Field(typeof(Scope),"LateSnapshot")).Clear();
  ((IList)Field(typeof(CrossbowmanLifecycle),"_owned")).Clear();
  foreach(string n in new[]{"_applyErrorLogs","_stripErrorLogs","_readerErrorLogs","_scanErrorLogs","_poolSpawnDepth"})typeof(CrossbowmanLifecycle).GetField(n,Static).SetValue(null,0);
  typeof(CrossbowmanLifecycle).GetField("_markerRegistered",Static).SetValue(null,false);
  typeof(ScaleRegistryHolder).GetField("<Instance>k__BackingField",Static).SetValue(null,null);
  typeof(ScaleRegistryHolder).GetField("_creationPending",Static).SetValue(null,false);
  typeof(ScaleRegistryHolder).GetField("_nextCreationRetryFrame",Static).SetValue(null,0);
  ((IDictionary)Field(typeof(PatchRoles_KnightStyle),"States")).Clear();
  ((HashSet<string>)Field(typeof(PatchRoles_KnightStyle),"LoggedErrors")).Clear();KingdomEnhancedPlugin.Logger.Errors.Clear();
  var names=(string[])Field(typeof(PatchRoles_KnightStyle),"SoldierControllerNames");var controllers=(RuntimeAnimatorController[])Field(typeof(PatchRoles_KnightStyle),"SoldierControllers");
  for(int i=0;i<controllers.Length;i++)controllers[i]=new(){name=names[i]};
  ClassInjector.MarkerRegistered=true;ClassInjector.FailMarkerRead=false;BiomeHolder.Inst=new();ModConfig.Enabled=new();Time.frameCount+=1000;
  GameObject.HolderCreates=GameObject.HolderFailures=GameObject.HolderNullResults=0;PatchRoles_Crossbowman.ApplyCalls=PatchRoles_Crossbowman.RestoreCalls=0;PatchRoles_Crossbowman.ProbeCrossbowAttack=null;
  Scope.Tick();
 }
 static void Equal(float wanted,float actual,string label){Checks++;if(MathF.Abs(wanted-actual)>.000001f)throw new Exception($"{label}: wanted={wanted}, actual={actual}");}
 static void True(bool value,string label){Checks++;if(!value)throw new Exception(label);}
 static void Case(string name,Action action,bool nativeFaultExpected=false)
 {Reset();try{action();if(nativeFaultExpected)True(KingdomEnhancedPlugin.Logger.Errors.Count==1&&KingdomEnhancedPlugin.Logger.Errors.All(e=>e.StartsWith("[Crossbowman/strip]")||e.StartsWith("[Crossbowman/identity]")),"exact intentional native fault recorded");else True(KingdomEnhancedPlugin.Logger.Errors.Count==0,"no unexpected production errors");Results.Add(new{name,status="PASS"});Console.WriteLine("PASS "+name);}catch(Exception e){Results.Add(new{name,status="FAIL",error=e.ToString()});Console.WriteLine("FAIL "+name+": "+e.Message);}}
 static (Archer a,CrossbowmanProfile p) Create()
 {
  var go=new GameObject("archer");var a=go.AddComponent<Archer>();a._mover=go.AddComponent<Mover>();a._animator=go.AddComponent<Animator>();a._animator.runtimeAnimatorController=new(){name="native-hunter"};a.hunterAnimator=a._animator.runtimeAnimatorController;a.soldierAnimator=PatchRoles_Crossbowman.BaseSoldierAnimator;a._arrowAttack=new();a._fireArrowAttack=new();a.ActiveArrowAttack=a._arrowAttack;
  var p=new CrossbowmanProfile{Attack=new(),Skin=new(){name="crossbow"},ReapplyBanner=null,BaseShootRange=8,BaseShootRangeKnown=true,BaseInterval=new(1,2),BaseIntervalKnown=true,BaseIntervalFormation=new(1,2),BaseIntervalFormationKnown=true,BaseSkin=a.hunterAnimator,BaseSoldierAnimator=a.soldierAnimator};
  PatchRoles_Crossbowman.ProbeCrossbowAttack=p.Attack;return(a,p);
 }
 static CrossbowmanMarker Apply(Archer a,CrossbowmanProfile p)
 {True(CrossbowmanLifecycle.Apply(a,p),"real lifecycle Apply");return a.GetComponent<CrossbowmanMarker>();}
 static void Convert(Archer a,bool hunter=true)
 {Type t=hunter?typeof(Archer_ConvertToHunter_KnightStyleSkin_Patch):typeof(Archer_ConvertToSoldier_KnightStyleSkin_Patch);t.GetMethod("Postfix",Static).Invoke(null,new object[]{a});}
 static void GuardOnly(Archer a,float target)=>typeof(PatchRoles_KnightStyle).GetMethod("EnsureFollowerEventScale",Static).Invoke(null,new object[]{a,target});
 static void Style(Archer a,int style)
 {
  var k=new GameObject("knight").AddComponent<Knight>();a._knight=k;Type t=typeof(PatchRoles_KnightStyle).GetNestedType("KnightStyleState",BindingFlags.NonPublic);object state=Activator.CreateInstance(t);t.GetField("Knight",Instance).SetValue(state,k);t.GetField("StyleIndex",Instance).SetValue(state,style);t.GetField("HasStyle",Instance).SetValue(state,true);((IDictionary)Field(typeof(PatchRoles_KnightStyle),"States"))[k.gameObject.GetInstanceID()]=state;
 }
 static void Registered(Archer a,float expected)
 {True(ScaleRegistryHolder.TryGet(a._mover,out float target),"registration remains");Equal(expected,target,"registered target");Equal(expected,a.transform.localScale.y,"current Y");}
 static void Seed(Archer a,float y){Scope.ApplyY(a.transform,y);ScaleRegistryHolder.Register(a._mover,y);}
 static void Main()
 {
  Case("real Fire-SO same-life disable Convert and reopen preserves crossbow scale",()=>{var(a,p)=Create();var m=Apply(a,p);a.ActiveArrowAttack=a._fireArrowAttack;CrossbowmanLifecycle.OnArcherDisablePrefix(a);a.gameObject.activeInHierarchy=false;a._knight=null;Convert(a);True(m.Selected&&!m.Active&&!m.Residue,"same-life selection retained");Registered(a,1.15f);a.ActiveArrowAttack=a._arrowAttack;a.gameObject.activeInHierarchy=true;CrossbowmanLifecycle.OnArcherEnablePrefix(a,p);CrossbowmanLifecycle.OnArcherEnablePostfix(a,p);Mover_Update_Patch.Mover_Update_Postfix(a._mover);Scope.MaintainRegisteredY();True(CrossbowmanLifecycle.IsCrossbowman(a),"real execution eligibility resumes");Registered(a,1.15f);});
  Case("real Active career and new wrapper both preserve its request",()=>{var(a,p)=Create();var m=Apply(a,p);a.ActiveArrowAttack=a._fireArrowAttack;int writes=a.transform.Writes;Convert(a);GuardOnly(a,1);Equal(writes,a.transform.Writes,"Active no new scale write");True(m.Active&&m.Selected,"flags unchanged");Registered(a,1.15f);});
  Case("unreturned Residue blocks only new scale action",()=>{var(a,p)=Create();var m=Apply(a,p);m.Selected=false;m.Active=false;m.Residue=true;a.ActiveArrowAttack=a._fireArrowAttack;int writes=a.transform.Writes;Convert(a);Equal(writes,a.transform.Writes,"Residue scale preserved");True(m.Residue,"responsibility not erased");Registered(a,1.15f);Equal(1,PatchRoles_Crossbowman.RestoreCalls,"old cleanup still reached");});
  Case("Selected inactive blocks no-style Restore and nondead target Apply",()=>{var(a,p)=Create();var m=Apply(a,p);CrossbowmanLifecycle.OnArcherDisablePrefix(a);a.ActiveArrowAttack=a._fireArrowAttack;a._knight=new GameObject("unresolved").AddComponent<Knight>();Convert(a);Registered(a,1.15f);Style(a,0);Convert(a,false);Registered(a,1.15f);True(m.Selected&&!m.Active,"same ownership retained");var controllers=(RuntimeAnimatorController[])Field(typeof(PatchRoles_KnightStyle),"SoldierControllers");True(a.soldierAnimator.Pointer==controllers[0].Pointer,"guard did not early-return whole skin pipeline");});
  Case("empty reused marker permits ordinary current follower targets",()=>{foreach(int style in new[]{0,4}){var(a,p)=Create();var m=a.gameObject.AddComponent<CrossbowmanMarker>();Style(a,style);Convert(a,false);Registered(a,style==0?1.064f:1.15f);True(!m.Selected&&!m.Active&&!m.Residue,"empty marker stays ordinary");}});
  Case("real pool cleanup clears true career and permits new follower target",()=>{var(a,p)=Create();var m=Apply(a,p);a.ActiveArrowAttack=a._fireArrowAttack;CrossbowmanLifecycle.OnArcherDisablePrefix(a);a._knight=null;CrossbowmanLifecycle.BeginPoolSpawnScope();try{CrossbowmanLifecycle.OnArcherEnablePrefix(a,p);}finally{CrossbowmanLifecycle.EndPoolSpawnScope();}True(!m.Selected&&!m.Active&&!m.Residue&&!m.PendingPoolHandoff,"real pool boundary cleared ownership");True(!ScaleRegistryHolder.TryGet(a._mover,out _),"old scale lease released by real Strip");a.ActiveArrowAttack=a._arrowAttack;Style(a,0);Convert(a,false);CrossbowmanLifecycle.OnArcherEnablePostfix(a,p);Registered(a,1.064f);True(!CrossbowmanLifecycle.IsCrossbowman(a),"new life still ordinary");});
  Case("real failed pool cleanup hands off old Residue to new Knight scale",()=>{var(a,p)=Create();var m=Apply(a,p);CrossbowmanLifecycle.OnArcherDisablePrefix(a);a._knight=null;a.FailAttackRead=true;CrossbowmanLifecycle.BeginPoolSpawnScope();try{CrossbowmanLifecycle.OnArcherEnablePrefix(a,p);}finally{CrossbowmanLifecycle.EndPoolSpawnScope();}a.FailAttackRead=false;True(m.PendingPoolHandoff&&!m.Selected&&m.Residue,"real failure created handoff receipt");a.ActiveArrowAttack=a._arrowAttack;Style(a,0);Convert(a,false);Registered(a,1.064f);True(m.Residue&&m.PendingPoolHandoff,"scale guard did not mutate old receipt");CrossbowmanLifecycle.OnArcherEnablePostfix(a,p);True(!m.Residue&&!m.PendingPoolHandoff&&!m.Active,"actual Strip retired handed-off receipt");Registered(a,1.064f);},true);
  Case("pending cleanup without Knight retains ownership",()=>{var(a,p)=Create();var m=Apply(a,p);m.Selected=false;m.Active=false;m.Residue=true;m.PendingPoolHandoff=true;a.ActiveArrowAttack=a._fireArrowAttack;a._knight=null;Convert(a);Registered(a,1.15f);True(m.PendingPoolHandoff&&m.Residue,"no proven Knight assignment");});
  Case("pending marker with Selected true cannot hand off scale",()=>{var(a,p)=Create();var m=Apply(a,p);CrossbowmanLifecycle.OnArcherDisablePrefix(a);m.Residue=true;m.PendingPoolHandoff=true;a.ActiveArrowAttack=a._fireArrowAttack;Style(a,0);Convert(a,false);Registered(a,1.15f);True(m.Selected&&m.Residue,"selection blocks handoff");});
  Case("ordinary no-marker follower and leave restore are preserved",()=>{var(a,p)=Create();Style(a,0);Convert(a,false);Registered(a,1.064f);a._knight=null;Convert(a);Equal(1,a.transform.localScale.y,"ordinary leave restored");True(!ScaleRegistryHolder.TryGet(a._mover,out _),"ordinary lease cancelled");True(a.GetComponent<CrossbowmanMarker>()==null,"guard adds no marker");});
  Case("unregistered marker type defers new scale without changing registration",()=>{var(a,p)=Create();Seed(a,1.15f);typeof(CrossbowmanLifecycle).GetField("_markerRegistered",Static).SetValue(null,true);ClassInjector.MarkerRegistered=false;int writes=a.transform.Writes;Convert(a);Equal(writes,a.transform.Writes,"unregistered zero new scale write");Registered(a,1.15f);});
  Case("marker type getter failure defers new scale",()=>{var(a,p)=Create();Seed(a,1.15f);ClassInjector.FailMarkerRead=true;int writes=a.transform.Writes;Convert(a);Equal(writes,a.transform.Writes,"type failure zero scale write");ClassInjector.FailMarkerRead=false;Registered(a,1.15f);},true);
  Case("component getter failure defers new scale",()=>{var(a,p)=Create();Seed(a,1.15f);a.gameObject.FailComponentRead=typeof(CrossbowmanMarker);int writes=a.transform.Writes;Convert(a);Equal(writes,a.transform.Writes,"component failure zero scale write");a.gameObject.FailComponentRead=null;Registered(a,1.15f);},true);
  Case("different-root marker cannot authorize new scale action",()=>{var(a,p)=Create();Seed(a,1.15f);var foreign=new GameObject("different-root").AddComponent<CrossbowmanMarker>();a.gameObject.MarkerOverride=foreign;int writes=a.transform.Writes;Convert(a);Equal(writes,a.transform.Writes,"foreign marker zero new scale write");Registered(a,1.15f);});
  Case("same root pointer but different GO ID is deferred",()=>{var(a,p)=Create();Seed(a,1.15f);var foreign=new GameObject("reused-pointer").AddComponent<CrossbowmanMarker>();foreign.gameObject.Pointer=a.gameObject.Pointer;a.gameObject.MarkerOverride=foreign;int writes=a.transform.Writes;Convert(a);Equal(writes,a.transform.Writes,"GO ID guard");Registered(a,1.15f);});
  Case("root identity getter exception retains ownership",()=>{var(a,p)=Create();Seed(a,1.15f);a.gameObject.AddComponent<CrossbowmanMarker>();a.gameObject.FailIdentity=true;int writes=a.transform.Writes;Convert(a);Equal(writes,a.transform.Writes,"identity failure zero new scale write");a.gameObject.FailIdentity=false;Registered(a,1.15f);});
  Case("null native marker pointer is deferred",()=>{var(a,p)=Create();Seed(a,1.15f);var marker=a.gameObject.AddComponent<CrossbowmanMarker>();marker.Pointer=IntPtr.Zero;int writes=a.transform.Writes;Convert(a);Equal(writes,a.transform.Writes,"zero marker pointer skipped");Registered(a,1.15f);});
  Console.WriteLine(JsonSerializer.Serialize(new{scope="four complete production files linked including real CrossbowmanLifecycle and CrossbowmanMarker",cases=Results.Count,checks=Checks,results=Results},new JsonSerializerOptions{WriteIndented=true}));
  if(Results.Any(r=>r.GetType().GetProperty("status").GetValue(r)?.ToString()=="FAIL"))Environment.Exit(1);
 }
}
