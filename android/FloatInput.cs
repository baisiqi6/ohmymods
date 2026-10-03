using System;
using MelonLoader;
using UnityEngine;
namespace OhMyMods.AndroidProbe;
public static class FloatInput
{
 private static readonly TouchClaims claims=new();
 private static bool failed;
 private static int logCount;
 public static void Reset(){claims.Reset();}
 public static void FilterTouchesResult(ref Il2CppSystem.Collections.Generic.List<Touch> __result) { Filter(ref __result); }
 public static void FilterPlayerTouches(ref Il2CppSystem.Collections.Generic.List<Touch> __0) { Filter(ref __0); }
 private static void Filter(ref Il2CppSystem.Collections.Generic.List<Touch> __0)
 {
  if(failed||__0==null||!ProbeTicker.UiReady)return;
  try
  {
   int count=__0.Count;
   if(count>16)return;
   var layout=ProbeTicker.Layout;
   layout.Resize(Screen.width,Screen.height);
   Il2CppSystem.Collections.Generic.List<Touch> filtered=null;
   for(int i=0;i<count;i++)
   {
    var t=__0[i];float x=t.position.x,y=Screen.height-t.position.y;
    bool keepOut=claims.Claim(Time.frameCount,t.fingerId,t.phase==TouchPhase.Began,t.phase==TouchPhase.Ended||t.phase==TouchPhase.Canceled,layout.HitBall(x,y)||layout.HitPanel(x,y));
    if(keepOut)
    {
     if(filtered==null) { filtered=new();for(int j=0;j<i;j++)filtered.Add(__0[j]); }
    }
    else if(filtered!=null) filtered.Add(t);
   }
   if(filtered!=null)
   {
    __0=filtered;
    if(logCount++<5) MelonLogger.Msg("OHMYMODS_FLOAT_INPUT_FILTER original="+count+" gameplay="+filtered.Count);
   }
  }
  catch(Exception ex) { failed=true;claims.Reset();MelonLogger.Error("OHMYMODS_FLOAT_INPUT_GUARD_DISABLED "+ex.Message); }
 }
}
