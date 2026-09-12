using System.Reflection;
using KingdomEnhancedMod;
using UnityEngine;
using MN=KingdomEnhancedMod.PatchRoles_MedievalNorsePowers;

static partial class Program
{
 static MN.KnightPowerState ArcState(Knight knight)
 {
  var states=(System.Collections.IDictionary)typeof(MN).GetField("States",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
  return (MN.KnightPowerState)states[knight.gameObject.Id];
 }

 static void Near(float expected,float actual,string label)
 {
  if(float.IsNaN(actual)||MathF.Abs(expected-actual)>0.000001f)
   throw new Exception($"{label}: expected {expected}, got {actual}");
 }

 static void AssertArcPoints(LineRenderer renderer,float start,float front)
 {
  Eq(13,renderer.positionCount,"13 vertices allocated before indexed writes");
  Eq(1,renderer.PositionCountWrites,"one vertex allocation");
  Eq(13,renderer.SingleCalls,"13 indexed native boundary calls");
  Eq(0,LineRenderer.BulkCalls,"bulk Span boundary forbidden");
  for(int i=0;i<13;i++)
  {
   float t=i/12f;
   Near(start+(front-start)*t,renderer.positions[i].x,$"vertex {i} x");
   Near(-.05f+.55f*MathF.Sin(MathF.PI*t),renderer.positions[i].y,$"vertex {i} y");
   Eq(0f,renderer.positions[i].z,$"vertex {i} z");
  }
 }

 static void RunWindArcTests()
 {
  Test("Wind arc real builder avoids injected bulk fault and preserves all 13 vertices",()=>
  {
   LineRenderer.ResetProbe();
   var knight=NewKnight(0);
   MN.Reconcile(knight);
   MN.OnKnightSlashAnim(knight);
   var state=ArcState(knight);
   Eq(true,state.ArcBehaviour!=null,"arc survives forbidden old interop boundary");
   AssertArcPoints(state.ArcBehaviour.Renderer,.25f,3f);
   Eq(0,UnityEngine.Object.DestroyRequests.Count,"successful build needs no cleanup");
  });

  Test("Wind arc builder preserves positive start and slash-range fallback geometry",()=>
  {
   foreach(var bounds in new[]{(.8f,4f,2f,.8f,6f),(-2f,0f,5f,.25f,7.5f)})
   {
    LineRenderer.ResetProbe();
    var knight=NewKnight(0);
    var state=new MN.KnightPowerState{Knight=knight,BaseCaptured=true,BaseHitBox=Rect.MinMaxRect(bounds.Item1,-1,bounds.Item2,1),BaseSlashRange=bounds.Item3};
    Hook(typeof(MN),"EnsureArcBuilt",knight,state);
    Eq(true,state.ArcBehaviour!=null,"real production builder completes");
    AssertArcPoints(state.ArcBehaviour.Renderer,bounds.Item4,bounds.Item5);
   }
  });

  Test("Wind arc repeated attacks reuse geometry after fade and disable",()=>
  {
   LineRenderer.ResetProbe();
   var knight=NewKnight(0);
   MN.Reconcile(knight);
   MN.OnKnightSlashAnim(knight);
   var state=ArcState(knight);var original=state.ArcObject;var behaviour=state.ArcBehaviour;
   Eq(true,behaviour!=null,"initial build succeeds");
   for(int i=0;i<20;i++)
   {
    Time.deltaTime=.21f;MN.TickArc(behaviour);
    Eq(false,original.activeSelf,"fade hides cached child");
    MN.OnKnightSlashAnim(knight);
    Eq(original,state.ArcObject,"same child after repeated attack");
    Eq(behaviour,state.ArcBehaviour,"same behaviour after repeated attack");
    Eq(true,original.activeSelf,"attack reactivates child");
   }
   MN.OnKnightDisabled(knight);MN.OnKnightDisabled(knight);
   Eq(false,original.activeSelf,"repeated disable remains hidden");
   MN.Reconcile(knight);MN.OnKnightSlashAnim(knight);
   Eq(original,state.ArcObject,"cached child reused after disable");
   Eq(1,LineRenderer.Created.Count,"one renderer throughout");
   AssertArcPoints(behaviour.Renderer,.25f,3f);
  });

  Test("Wind arc partial indexed failure destroys only new child and permits retry",()=>
  {
   foreach(int failedIndex in new[]{0,6,12})
   {
    LineRenderer.ResetProbe();
    ((HashSet<string>)typeof(MN).GetField("LoggedOnce",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null)).Remove("arc build failed");
    var knight=NewKnight(0);MN.Reconcile(knight);
    LineRenderer.FailAtIndex=failedIndex;
    MN.OnKnightSlashAnim(knight);
    var state=ArcState(knight);
    Eq(1,LineRenderer.Created.Count,"one partial renderer");
    var partial=LineRenderer.Created[0];
    Eq(failedIndex+1,partial.SingleCalls,"failure reached selected indexed boundary");
    Eq(0,LineRenderer.BulkCalls,"fault did not use bulk path");
    Eq(1,UnityEngine.Object.DestroyRequests.Count,"exactly one cleanup request");
    Eq(partial.gameObject,UnityEngine.Object.DestroyRequests[0],"only partial child scheduled for destruction");
    Eq(knight.transform,partial.transform.parent,"failed object belongs to this knight");
    Eq(false,partial.gameObject.activeSelf,"partial geometry never displayed");
    Eq<GameObject>(null,state.ArcObject,"no cached child after failure");
    Eq<KnightWindArcBehaviour>(null,state.ArcBehaviour,"no cached behaviour after failure");
    Eq<KnightWindArcBehaviour>(null,partial.gameObject.GetComponent<KnightWindArcBehaviour>(),"no attached behaviour before geometry completes");
    Eq(1,KingdomEnhancedPlugin.Logger.Errors.Count,"existing handler reports failure");
    Eq(true,KingdomEnhancedPlugin.Logger.Errors[0].Contains("Injected SetPosition fault at "+failedIndex),"original fault remains visible");
    KingdomEnhancedPlugin.Logger.Errors.Clear();
    LineRenderer.FailAtIndex=-1;
    MN.OnKnightSlashAnim(knight);
    Eq(true,state.ArcBehaviour!=null,"next attack rebuilds");
    Eq(false,ReferenceEquals(partial.gameObject,state.ArcObject),"retry owns fresh child");
    Eq(2,LineRenderer.Created.Count,"retry creates exactly one replacement");
    Eq(1,UnityEngine.Object.DestroyRequests.Count,"retry does not destroy unrelated objects");
    AssertArcPoints(state.ArcBehaviour.Renderer,.25f,3f);
    MN.OnKnightDisabled(knight);MN.OnKnightDisabled(knight);
    Eq(1,UnityEngine.Object.DestroyRequests.Count,"repeated cleanup has no extra destruction");
   }
  });
 }
}
