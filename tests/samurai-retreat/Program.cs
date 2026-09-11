using System.Reflection;
using KingdomEnhancedMod;
using UnityEngine;
internal static class Program
{
 static int passed,failed;
 static void Check(bool condition,string why){if(!condition)throw new Exception(why);}
 static void SameFloat(float expected,float actual,string why){if(BitConverter.SingleToInt32Bits(expected)!=BitConverter.SingleToInt32Bits(actual))throw new Exception($"{why}: expected {expected}, got {actual}");}
 static void Test(string name,Action body)
 {
  ModConfig.Enabled.Value=true;NetworkBigBoss.HasWorldAuth=true;Managers.Inst=new();Time.timeScale=1;Mover.Intercept=null;
  UnityEngine.Object.Forbidden=0;PatchWorld_DefenseSpacing.KnightBranches=PatchWorld_DefenseSpacing.ArcherBranches=0;
  KingdomEnhancedPlugin.Instance.LogSource.Throw=false;KingdomEnhancedPlugin.Instance.LogSource.Lines.Clear();
  try{body();Check(UnityEngine.Object.Forbidden==0,"no scene scans");passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e.GetBaseException().Message);}
 }
 static (Knight k,Mover m) Ready()
 {
  var go=new GameObject();var k=go.AddComponent<Knight>();k.Style=2;k.isRetreating=true;k._fsm.Current=Knight.State.GoToWall;k._character=go.AddComponent<Character>();k._damageable=go.AddComponent<Damageable>();k._mover=go.AddComponent<Mover>();return(k,k._mover);
 }
 static float Apply(Knight k,Mover m,float input)
 {float before=k?._retreatSpeed??0;SamuraiRetreatSpeed.Adjust(k,m,ref input);if(k!=null)SameFloat(before,k._retreatSpeed,"never mutate native retreat-speed stat");return input;}
 static void Main()
 {
  Test("Native style2 GoToWall retreat input becomes exactly three times baseline",()=>{var(k,m)=Ready();SameFloat(6,Apply(k,m,2),"local argument boost");});
  foreach(int style in new[]{0,1,3,4})Test("Other style "+style+" remains unchanged",()=>{var(k,m)=Ready();k.Style=style;SameFloat(2,Apply(k,m,2),"Greek/other style baseline unchanged");});
  Test("Unknown resolved style remains unchanged",()=>{var(k,m)=Ready();k.KnownStyle=false;SameFloat(2,Apply(k,m,2),"unknown style excluded");});
  foreach(var input in new[]{6f,18f,4f})Test("Different ordinary run or dash speed "+input+" stays unchanged",()=>{var(k,m)=Ready();SameFloat(input,Apply(k,m,input),"non-retreat input remains native");});
  Test("Reapplying helper to an already boosted speed does not stack",()=>{var(k,m)=Ready();float speed=Apply(k,m,2);SameFloat(6,speed,"first application");SameFloat(6,Apply(k,m,speed),"no x9 reapplication");});
  foreach(var invalid in new[]{0f,-2f,float.NaN,float.PositiveInfinity,float.NegativeInfinity})Test("Invalid input speed "+invalid+" is untouched",()=>{var(k,m)=Ready();SameFloat(invalid,Apply(k,m,invalid),"invalid input preserved exactly");});
  foreach(var invalid in new[]{0f,-2f,float.NaN,float.PositiveInfinity,float.MaxValue})Test("Invalid or overflowing native baseline "+invalid+" is not multiplied",()=>{var(k,m)=Ready();k._retreatSpeed=invalid;SameFloat(invalid,Apply(k,m,invalid),"invalid baseline not boosted");});
  foreach(var item in new (string,Action<Knight>)[]{
   ("not retreating",k=>k.isRetreating=false),("Stand",k=>k._fsm.Current=Knight.State.Stand),("Assemble",k=>k._fsm.Current=Knight.State.Assemble),
   ("formation",k=>k.Formation=new()),("embarked",k=>k._embarkee.IsEmbarked=true),("boat targeting",k=>k._embarkee.IsTargetingEmbarkable=true),("boat target",k=>k._embarkee.EmbarkableTarget=new object()),
   ("manual request",k=>k.ControlRequested=true),("controlled",k=>k._beingControlled=true),("charging",k=>k.isCharging=true),("pending charge",k=>k._shouldCharge=true),
   ("dead",k=>k._damageable.isDead=true),("inactive",k=>k.gameObject.activeInHierarchy=false),("inert",k=>k._character.inert=true),("grabbed",k=>k._character.grabbed=true),("stationary",k=>k._character.isStationary=true),("pillar",k=>k.helPuzzlePillar=new())})
   Test("Exclude "+item.Item1,()=>{var(k,m)=Ready();item.Item2(k);SameFloat(2,Apply(k,m,2),"outside native defensive retreat");});
  Test("Mismatched mover cannot receive another knight's speed boost",()=>{var(k,m)=Ready();SameFloat(2,Apply(k,new GameObject().AddComponent<Mover>(),2),"mover ownership");});
  Test("Missing actor or mover is harmless",()=>{var(k,m)=Ready();SameFloat(2,Apply(null,m,2),"null knight");SameFloat(2,Apply(k,null,2),"null mover");});
  Test("Config disabled leaves arguments unchanged",()=>{var(k,m)=Ready();ModConfig.Enabled.Value=false;SameFloat(2,Apply(k,m,2),"config gate");});
  Test("Non-authoritative client leaves arguments unchanged",()=>{var(k,m)=Ready();NetworkBigBoss.HasWorldAuth=false;SameFloat(2,Apply(k,m,2),"authority gate");});
  Test("Real Harmony float prefix propagates ref speed and preserves native goal and Wait",()=>{
   var(k,m)=Ready();var prefix=typeof(Mover_DefenseSpacing_DayAssemble_Spread_Patch).GetMethod("Prefix",BindingFlags.Static|BindingFlags.NonPublic);
   var pars=prefix.GetParameters();Check(pars[2].ParameterType==typeof(float).MakeByRefType(),"Harmony speed parameter is ref");
   object[] args={m,123f,2f};bool run=(bool)prefix.Invoke(null,args);Check(run,"retreat native call continues");SameFloat(123,(float)args[1],"goal unchanged");SameFloat(6,(float)args[2],"ref speed reaches Harmony caller");SameFloat(6,PatchWorld_DefenseSpacing.BranchSpeed,"downstream receives boosted local speed");
   var wait=m.SetGoal((float)args[1],(float)args[2]);Check(ReferenceEquals(wait,m.LastWait),"native Wait preserved");SameFloat(123,m._goalPosition,"native destination unchanged");SameFloat(2,k._retreatSpeed,"no permanent stat edit");
  });
  Test("Real cached Archer branch never receives knight retreat adjustment",()=>{
   var go=new GameObject();go.AddComponent<Archer>();var mover=go.AddComponent<Mover>();float speed=2;Check(PatchWorld_DefenseSpacing.DayAssembleSpreadPrefix(mover,100,ref speed),"native archer dispatch continues");
   SameFloat(2,speed,"archer speed unchanged");Check(PatchWorld_DefenseSpacing.ArcherBranches==1&&PatchWorld_DefenseSpacing.KnightBranches==0,"correct cached unit dispatch");
  });
  Console.WriteLine($"RESULT {passed} passed, {failed} failed");Environment.ExitCode=failed==0?0:1;
 }
}
