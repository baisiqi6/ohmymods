using System;
using MelonLoader;
using UnityEngine;
using KingdomEnhancedMod;
namespace OhMyMods.AndroidProbe;
internal static class MobileCalendar
{
 internal static bool Enabled => ModConfig.CalendarEnabled.Value;
 internal static string PanelText => Enabled ? (renderBlocked ? "日历显示不可用" : valid ? "日期与季节已连接" : "等待游戏数据") : "日期与季节显示";
 static bool valid, logged, renderBlocked;static int faults;static float next;static IntPtr worldId, sceneId;static string line="";static GUIStyle style;
 internal static void Toggle(){ModConfig.CalendarEnabled.Value=!ModConfig.CalendarEnabled.Value;renderBlocked=false;Clear();MelonLogger.Msg("ANDROID_CALENDAR_TOGGLE enabled="+Enabled);ModConfig.Save();}
 static void Clear(){valid=false;worldId=sceneId=IntPtr.Zero;next=0;line="";}
 internal static void Tick()
 {
  if(!Enabled){if(valid)Clear();return;}
  try
  {
   var m=Il2Cpp.Managers.Inst;var world=m?.world;var game=m?.game;var director=m?.director;var scene=world?.gameLayer;
   if(world==null || scene==null || game==null || director==null){Clear();return;}
   bool same=worldId==world.Pointer && sceneId==scene.Pointer;
   if(game.state!=Il2Cpp.Game.State.Playing && game.state!=Il2Cpp.Game.State.NetworkClientPlaying){if(game.state==Il2Cpp.Game.State.Menu && same && valid)return;Clear();return;}
   if(!same){Clear();worldId=world.Pointer;sceneId=scene.Pointer;}
   if(Time.unscaledTime<next)return;next=Time.unscaledTime+.5f;
   valid=CalendarReader.TryRead(director,out var data);
   if(!valid){line="";return;}
   line=$"第 {data.TotalDay} 天  |  {data.Hour} 点  |  {SeasonName(data.CurrentSeason)} 第 {data.SeasonDay} 天";
   if(!logged){logged=true;MelonLogger.Msg($"ANDROID_CALENDAR_READY day={data.TotalDay} hour={data.Hour} season={data.CurrentSeason} seasonDay={data.SeasonDay} nextSeason={data.NextSeason} nextDay={data.NextSeasonDay} nativeTotalDay={director.TotalDaysInReign} nativeSeasonDay={director.CurrentSeasonDay} nativeTime={director.currentTime}");}
  }catch(Exception e){Clear();next=Time.unscaledTime+1;if(faults++<3)MelonLogger.Warning("ANDROID_CALENDAR_UNAVAILABLE "+e.GetType().Name);}
 }
 internal static void Draw(float scale)
 {
  if(!Enabled || !valid || renderBlocked || Event.current.type!=EventType.Repaint)return;
  var color=GUI.color;var content=GUI.contentColor;
  try
  {
   if(style==null)style=new GUIStyle(GUI.skin.label);
   style.fontSize=Math.Max(8,(int)(18*scale));style.alignment=TextAnchor.MiddleCenter;
   float width=Math.Min(580*scale,Screen.width-16*scale);var rect=new Rect((Screen.width-width)/2,6*scale,width,30*scale);
   GUI.color=Color.white;GUI.contentColor=new Color(.93f,.78f,.47f,1);GUI.Label(rect,line,style);
  }
  catch(Exception e){renderBlocked=true;MelonLogger.Warning("ANDROID_CALENDAR_DRAW_BLOCKED "+e.Message);}
  finally {GUI.color=color;GUI.contentColor=content;}
 }
 // 与 PC CalendarHud.SeasonName 相同的四段中文名（仅展示层；其余 clockdriver 不动）。
 private static string SeasonName(Il2Cpp.Season season)
 {
  switch(season)
  {
   case Il2Cpp.Season.Spring: return "春";
   case Il2Cpp.Season.Summer: return "夏";
   case Il2Cpp.Season.Autumn: return "秋";
   case Il2Cpp.Season.Winter: return "冬";
   default: return "季";
  }
 }
}
