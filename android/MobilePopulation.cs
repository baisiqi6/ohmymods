using System;
using MelonLoader;
using UnityEngine;
using KingdomEnhancedMod;
namespace OhMyMods.AndroidProbe;
internal static class MobilePopulation
{
 static readonly string[] Names={"Workers","Archers","Farmers","Pikemen","Ninjas","Berserkers","Villagers","Beggars"};
 static readonly string[] Rows=new string[8];static string knights="",status="";static long version=-1;static bool ready;static int snapshots,failures;static string last="";
 public static void RosterChanged()=>PopulationCounts.NotifyRosterChanged();
 internal static void Tick()
 {
  bool enabled=ProbeTicker.Layout.Expanded && ProbeTicker.Layout.PopulationPage && ProbeTicker.UiReady;
  try
  {
   ready=PopulationCounts.Refresh(enabled ? Il2Cpp.Managers.Inst : null,enabled,Time.unscaledTime);
   status=PopulationCounts.ClientUnavailable ? "Host roster unavailable" : ready ? "Current island" : "Waiting for roster";
   if(!ready || version==PopulationCounts.Version)return;version=PopulationCounts.Version;
   for(int i=0;i<8;i++)Rows[i]=Names[i]+": "+PopulationCounts.Role(i);
   knights="Knights: "+PopulationCounts.Knights;
   string snapshot=string.Join(";",Rows)+";"+knights;
   if(snapshot!=last){last=snapshot;if(snapshots++<12)MelonLogger.Msg("ANDROID_POPULATION_READY "+snapshot+" nativeOnly=true");}
  }catch(Exception e){ready=false;status="Population unavailable";if(failures++<3)MelonLogger.Warning("ANDROID_POPULATION_FAILED "+e.Message);}
 }
 internal static void DrawPanel(float x,float y,float u,GUIStyle style)
 {
  GUI.Label(new Rect(x+16*u,y+48*u,248*u,40*u),status,style);
  if(!ready)return;
  for(int i=0;i<8;i++)GUI.Label(new Rect(x+(16+(i%2)*130)*u,y+(88+(i/2)*40)*u,130*u,40*u),Rows[i],style);
  GUI.Label(new Rect(x+16*u,y+248*u,248*u,40*u),knights,style);
 }
}
