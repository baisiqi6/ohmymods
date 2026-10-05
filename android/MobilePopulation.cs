using System;
using MelonLoader;
using UnityEngine;
using KingdomEnhancedMod;
namespace OhMyMods.AndroidProbe;
internal static class MobilePopulation
{
 // 日志 payload 保持原英文名单；面板展示用 PC PopulationHud 同源的 8 个中文角色名（未移植角色不出现）。
 static readonly string[] Names={"Workers","Archers","Farmers","Pikemen","Ninjas","Berserkers","Villagers","Beggars"};
 static readonly string[] RoleNames={"工匠","弓箭手","农民","长枪兵","忍者","狂战士","无业村民","乞丐"};
 static string status="";static long version=-1;static bool ready;static int snapshots,failures;static string last="";
 public static void RosterChanged()=>PopulationCounts.NotifyRosterChanged();
 internal static void Tick()
 {
  bool enabled=ProbeTicker.Layout.Expanded && ProbeTicker.Layout.PopulationPage && ProbeTicker.UiReady;
  try
  {
   ready=PopulationCounts.Refresh(enabled ? Il2Cpp.Managers.Inst : null,enabled,Time.unscaledTime);
   status=PopulationCounts.ClientUnavailable ? "主机名单不可用" : ready ? "当前岛屿" : "等待人口数据";
   if(!ready || version==PopulationCounts.Version)return;version=PopulationCounts.Version;
   string snapshot=Names[0]+": "+PopulationCounts.Role(0);
   for(int i=1;i<8;i++)snapshot+=";"+Names[i]+": "+PopulationCounts.Role(i);
   snapshot+=";Knights: "+PopulationCounts.Knights;
   if(snapshot!=last){last=snapshot;if(snapshots++<12)MelonLogger.Msg("ANDROID_POPULATION_READY "+snapshot+" nativeOnly=true");}
  }catch(Exception e){ready=false;status="人口不可用";if(failures++<3)MelonLogger.Warning("ANDROID_POPULATION_FAILED "+e.Message);}
 }
 // 仅中文展示：八个原生角色（PC 同源命名）+ 骑士；面板负责滚动与样式，Tick/日志采样行为不变。
 internal static void Draw(MobileModPanel panel,float width,float scale)
 {
  panel.Info("人口",status,"");
  if(!ready)return;
  for(int i=0;i<8;i++)panel.Info(RoleNames[i],""+PopulationCounts.Role(i),"");
  panel.Info("骑士",""+PopulationCounts.Knights,"");
 }
}
