using System;
using MelonLoader;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
[assembly: MelonInfo(typeof(OhMyMods.AndroidProbe.Probe), "OhMyMods Android Probe", "0.0.23", "OhMyMods")]
namespace OhMyMods.AndroidProbe;
public sealed class Probe : MelonMod
{
 private bool setupAttempted;
 public override void OnInitializeMelon()
 {
  KingdomEnhancedMod.KingdomEnhancedPlugin.Initialize();
  KingdomEnhancedMod.ModConfig.Initialize(static enabled =>
  {
   Il2Cpp.Wallet.InfiniteMoney=enabled;
   MelonLogger.Msg("ANDROID_MONEY_FLAG enabled="+Il2Cpp.Wallet.InfiniteMoney);
  });
  LoggerInstance.Msg("OHMYMODS_ANDROID_PROBE_LOADED version=0.0.23 gameplay_features=optional_player_qol,hold_purchase,enemy_parameters,native_money,farm_cats,fast_build,steed_cooldown,boat_capacity,map_width,staff_base_cooldown,fast_forest_recede,deer_population,dense_thickets,night_departure");
  LoggerInstance.Msg("ANDROID_REGISTRATION autoPatchDisabled="+MelonAssembly.HarmonyDontPatchAll);
  if(!MelonAssembly.HarmonyDontPatchAll)throw new InvalidOperationException("Android assembly must disable automatic patch scanning");
  var touch = AccessTools.Method(typeof(Il2Cpp.InputHelper), "GetTouches")
    ?? throw new MissingMethodException("InputHelper.GetTouches");
  HarmonyInstance.Patch(touch, postfix: new HarmonyMethod(typeof(FloatInput), nameof(FloatInput.FilterTouchesResult)));
  var playerTouch=AccessTools.Method(typeof(Il2Cpp.TouchPlayerHandler),"InputUpdate");
  HarmonyInstance.Patch(playerTouch,prefix:new HarmonyMethod(typeof(FloatInput),nameof(FloatInput.FilterPlayerTouches)));
  LoggerInstance.Msg("OHMYMODS_FLOAT_INPUT_GUARD_INSTALLED");
  foreach(string name in new[]{"AddCharacter","RemoveCharacter"})
  {
   var method=AccessTools.Method(typeof(Il2Cpp.Kingdom),name,new[]{typeof(Il2Cpp.Character)}) ?? throw new MissingMethodException(name);
   HarmonyInstance.Patch(method,postfix:new HarmonyMethod(typeof(MobilePopulation),nameof(MobilePopulation.RosterChanged)));
  }
  LoggerInstance.Msg("ANDROID_POPULATION_ROSTER_HOOKS_INSTALLED");
  var speedType=typeof(KingdomEnhancedMod.PatchWorld_Mover);
  foreach(var targetSpeed in new[]{AccessTools.Method(typeof(Il2Cpp.Mover),"SetSpeed",new[]{typeof(float)}),AccessTools.Method(typeof(Il2Cpp.Mover),"SetSpeed",new[]{typeof(float),typeof(int)}),AccessTools.Method(typeof(Il2Cpp.Mover),"SetSpeedToGoal",new[]{typeof(float)})})
  {
   HarmonyInstance.Patch(targetSpeed,prefix:new HarmonyMethod(speedType,"ScalePlayerSpeed"));
   LogHookCounts(targetSpeed);
  }
  // Register cooldown before stamina reports the final shared-target counts.
  var cooldownType=typeof(KingdomEnhancedMod.PatchRide_SteedCooldown);
  var cooldownTargets=new[]{
   (typeof(Il2Cpp.SteedAbility),"Activate","BasePrefix"),
   (typeof(Il2Cpp.BuffUnitsSteedAbility),"Activate","BuffPrefix"),
   (typeof(Il2Cpp.GlideMovementSteedAbility),"Activate","GlidePrefix"),
   (typeof(Il2Cpp.SpeedBoostSteedAbility),"Deactivate","SpeedPrefix")};
  foreach(var entry in cooldownTargets)
  {
   var target=AccessTools.Method(entry.Item1,entry.Item2,Type.EmptyTypes)??throw new MissingMethodException(entry.Item1.Name+"."+entry.Item2+"()");
   HarmonyInstance.Patch(target,prefix:new HarmonyMethod(cooldownType,entry.Item3),finalizer:new HarmonyMethod(cooldownType,"Finalizer"));
   // Base and Glide are reported once by the existing stamina registrations below.
   if(entry.Item1==typeof(Il2Cpp.BuffUnitsSteedAbility)||entry.Item1==typeof(Il2Cpp.SpeedBoostSteedAbility))LogHookCounts(target);
  }
  LoggerInstance.Msg("ANDROID_STEED_COOLDOWN_HOOKS_INSTALLED consumers=4 currentFieldRelative=true");
  var staffCooldownType=typeof(KingdomEnhancedMod.PatchDivine_StaffCooldown);
  var staffRoutine=AccessTools.Method(typeof(Il2Cpp.HermesStaff._StartAbilityRoutine_d__17),"MoveNext",Type.EmptyTypes)??throw new MissingMethodException("HermesStaff._StartAbilityRoutine_d__17.MoveNext()");
  var staffCanCancel=AccessTools.Method(typeof(Il2Cpp.ItemOfPower),"CanCancel",Type.EmptyTypes)??throw new MissingMethodException("ItemOfPower.CanCancel()");
  HarmonyInstance.Patch(staffRoutine,prefix:new HarmonyMethod(staffCooldownType,"RoutinePrefix"),finalizer:new HarmonyMethod(staffCooldownType,"Finalizer"));
  HarmonyInstance.Patch(staffCanCancel,prefix:new HarmonyMethod(staffCooldownType,"CanCancelPrefix"),finalizer:new HarmonyMethod(staffCooldownType,"Finalizer"));
  LogHookCounts(staffRoutine);
  LogHookCounts(staffCanCancel);
  LoggerInstance.Msg("ANDROID_STAFF_BASE_COOLDOWN_HOOKS_INSTALLED consumers=2 currentFieldRelative=true additiveTimeNative=true");
  PatchStamina(typeof(Il2Cpp.Player),"UpdateActionState",typeof(KingdomEnhancedMod.PatchRide_InfiniteStamina),"UpdateActionState_Prefix","UpdateActionState_Postfix","UpdateActionState_Finalizer");
  PatchStamina(typeof(Il2Cpp.SteedAbility),"Activate",typeof(KingdomEnhancedMod.PatchRide_InfiniteStaminaAbility),"Prefix","Postfix");
  PatchStamina(typeof(Il2Cpp.GlideMovementSteedAbility),"Activate",typeof(KingdomEnhancedMod.PatchRide_InfiniteStaminaGlide),"Prefix","Postfix");
  PatchStamina(typeof(Il2Cpp.RunningAttackSteedAbility),"OnPushedObjects",typeof(KingdomEnhancedMod.PatchRide_InfiniteStaminaRunningAttack),"Prefix","Postfix");
  LoggerInstance.Msg($"ANDROID_PLAYER_QOL_HOOKS_INSTALLED speed={KingdomEnhancedMod.ModConfig.SpeedMultiplier.Value} stamina={KingdomEnhancedMod.ModConfig.InfiniteSteedStamina.Value}");
  var payState=AccessTools.Method(typeof(Il2Cpp.Player),"UpdatePayState",new[]{typeof(bool),typeof(bool),typeof(bool)})??throw new MissingMethodException("Player.UpdatePayState(bool,bool,bool)");
  var holdType=typeof(KingdomEnhancedMod.PatchPlayer_HoldPurchase);
  HarmonyInstance.Patch(payState,prefix:new HarmonyMethod(holdType,"UpdatePayState_Prefix"),postfix:new HarmonyMethod(holdType,"UpdatePayState_Postfix"),finalizer:new HarmonyMethod(holdType,"UpdatePayState_Finalizer"));
  var performPay=AccessTools.Method(typeof(Il2Cpp.Payable),"PerformPay",Type.EmptyTypes)??throw new MissingMethodException("Payable.PerformPay()");
  HarmonyInstance.Patch(performPay,postfix:new HarmonyMethod(holdType,"PerformPay_Postfix"));
  LogHookCounts(payState);
  LogHookCounts(performPay);
  LoggerInstance.Msg("ANDROID_HOLD_PURCHASE_HOOKS_INSTALLED enabled="+KingdomEnhancedMod.ModConfig.HoldPurchaseEnabled.Value+" sharedSource=true");
  var enemyPatchType=typeof(KingdomEnhancedMod.PatchWorld_EnemyManager);
  var addEnemies=AccessTools.Method(typeof(Il2Cpp.EnemyManager),"AddEnemies",new[]{typeof(Il2Cpp.EnemyType),typeof(AnimationCurve),typeof(int),typeof(float),typeof(Il2CppSystem.Collections.Generic.List<Il2Cpp.EnemyBlueprint>),typeof(string).MakeByRefType()})??throw new MissingMethodException("EnemyManager.AddEnemies(EnemyType,AnimationCurve,int,float,List<EnemyBlueprint>,ref string)");
  var getEnemies=AccessTools.Method(typeof(Il2Cpp.EnemyManager),"GetEnemies",new[]{typeof(Il2Cpp.Wave),typeof(int),typeof(int),typeof(int),typeof(bool)})??throw new MissingMethodException("EnemyManager.GetEnemies(Wave,int,int,int,bool)");
  HarmonyInstance.Patch(addEnemies,prefix:new HarmonyMethod(enemyPatchType,"AddEnemies_Prefix"));
  HarmonyInstance.Patch(getEnemies,prefix:new HarmonyMethod(enemyPatchType,"GetEnemies_Prefix"));
  LogHookCounts(addEnemies);
  LogHookCounts(getEnemies);
  LoggerInstance.Msg("ANDROID_ENEMY_PARAMETER_HOOKS_INSTALLED sharedSource=true");
  var initializeBuild=AccessTools.Method(typeof(Il2Cpp.ConstructionBuildingComponent),"InitializeBuild",Type.EmptyTypes)??throw new MissingMethodException("ConstructionBuildingComponent.InitializeBuild()");
  HarmonyInstance.Patch(initializeBuild,prefix:new HarmonyMethod(typeof(KingdomEnhancedMod.PatchWorld_Construction),nameof(KingdomEnhancedMod.PatchWorld_Construction.Prefix)));
  LogHookCounts(initializeBuild);
  LoggerInstance.Msg("ANDROID_FAST_BUILD_HOOK_INSTALLED sharedSource=true perInitializeBuildCall=true");
  var boatOnEnable=AccessTools.Method(typeof(Il2Cpp.Boat),"OnEnable",Type.EmptyTypes)??throw new MissingMethodException("Boat.OnEnable()");
  var boatPatchType=typeof(KingdomEnhancedMod.PatchWorld_BoatCapacity);
  HarmonyInstance.Patch(boatOnEnable,prefix:new HarmonyMethod(boatPatchType,"Prefix"),finalizer:new HarmonyMethod(boatPatchType,"Finalizer"));
  LogHookCounts(boatOnEnable);
  LoggerInstance.Msg("ANDROID_BOAT_CAPACITY_HOOK_INSTALLED sharedPolicy=true nativeSlotInitialization=true");
  var generateInternal=AccessTools.Method(typeof(Il2Cpp.Level),"GenerateInternal",new[]{typeof(Il2Cpp.LevelConfig),typeof(int)})??throw new MissingMethodException("Level.GenerateInternal(LevelConfig,int)");
  var mapPatchType=typeof(KingdomEnhancedMod.PatchWorld_Level);
  HarmonyInstance.Patch(generateInternal,prefix:new HarmonyMethod(mapPatchType,"Prefix"),postfix:new HarmonyMethod(mapPatchType,"Postfix"),finalizer:new HarmonyMethod(mapPatchType,"Finalizer"));
  LogHookCounts(generateInternal);
  var getBlocks=AccessTools.Method(typeof(Il2Cpp.LevelLayout),"GetBlocks",Type.EmptyTypes)??throw new MissingMethodException("LevelLayout.GetBlocks()");
  HarmonyInstance.Patch(getBlocks,postfix:new HarmonyMethod(typeof(KingdomEnhancedMod.PatchWorld_Level_GetBlocks),"Postfix"));
  LogHookCounts(getBlocks);
  LoggerInstance.Msg("ANDROID_MAP_WIDTH_HOOKS_INSTALLED sharedSource=true newIslandGeneration=true");
  var forestFadeAndRemove=AccessTools.Method(typeof(Il2Cpp.ForestItem),"FadeAndRemove",new[]{typeof(float)})??throw new MissingMethodException("ForestItem.FadeAndRemove(float)");
  HarmonyInstance.Patch(forestFadeAndRemove,prefix:new HarmonyMethod(typeof(KingdomEnhancedMod.ForestItem_FadeAndRemove_OptionalVegetation_Patch),"Prefix"));
  LogHookCounts(forestFadeAndRemove);
  LoggerInstance.Msg("ANDROID_FAST_FOREST_RECEDE_HOOK_INSTALLED sharedSource=true nativeEffects=true");
  var deerUpdate=AccessTools.Method(typeof(Il2Cpp.PopulationController),"Update",Type.EmptyTypes)??throw new MissingMethodException("PopulationController.Update()");
  var deerPatchType=typeof(KingdomEnhancedMod.PopulationController_Update_DeerPopulation_Patch);
  HarmonyInstance.Patch(deerUpdate,prefix:new HarmonyMethod(deerPatchType,"Prefix"),postfix:new HarmonyMethod(deerPatchType,"Postfix"),finalizer:new HarmonyMethod(deerPatchType,"Finalizer"));
  LogHookCounts(deerUpdate);
  LoggerInstance.Msg("ANDROID_DEER_POPULATION_HOOK_INSTALLED sharedSource=true nativeSpawn=true enabled="+KingdomEnhancedMod.ModConfig.DeerPopulationEnabled.Value);
  var thicketCanSpawn=AccessTools.Method(typeof(Il2Cpp.World),"CanSpawnThicket",new[]{typeof(Il2Cpp.Grass)})??throw new MissingMethodException("World.CanSpawnThicket(Grass)");
  var thicketCanSpawnPatch=typeof(KingdomEnhancedMod.World_CanSpawnThicket_OptionalVegetation_Patch);
  HarmonyInstance.Patch(thicketCanSpawn,prefix:new HarmonyMethod(thicketCanSpawnPatch,"Prefix"),postfix:new HarmonyMethod(thicketCanSpawnPatch,"Postfix"),finalizer:new HarmonyMethod(thicketCanSpawnPatch,"Finalizer"));
  LogHookCounts(thicketCanSpawn);
  var thicketAdded=AccessTools.Method(typeof(Il2Cpp.World),"AddThicket",new[]{typeof(Il2Cpp.Grass)})??throw new MissingMethodException("World.AddThicket(Grass)");
  HarmonyInstance.Patch(thicketAdded,postfix:new HarmonyMethod(typeof(KingdomEnhancedMod.World_AddThicket_OptionalVegetation_Patch),"Postfix"));
  LogHookCounts(thicketAdded);
  var thicketRemove=AccessTools.Method(typeof(Il2Cpp.Grass),"RemoveThicket",Type.EmptyTypes)??throw new MissingMethodException("Grass.RemoveThicket()");
  var thicketRemovePatch=typeof(KingdomEnhancedMod.Grass_RemoveThicket_OptionalVegetation_Patch);
  HarmonyInstance.Patch(thicketRemove,prefix:new HarmonyMethod(thicketRemovePatch,"Prefix"),postfix:new HarmonyMethod(thicketRemovePatch,"Postfix"));
  LogHookCounts(thicketRemove);
  LoggerInstance.Msg("ANDROID_DENSE_THICKETS_HOOKS_INSTALLED sharedSource=true nativeSpawn=true nativeRemove=true enabled="+KingdomEnhancedMod.ModConfig.DenseThicketsEnabled.Value);
  var nightSchedule=AccessTools.Method(typeof(Il2Cpp.Director),"ScheduleWaveToday",new[]{typeof(Il2Cpp.Wave),typeof(Il2Cpp.Side),typeof(float)})??throw new MissingMethodException("Director.ScheduleWaveToday(Wave,Side,float)");
  var nightSchedulePatch=typeof(KingdomEnhancedMod.PatchWorld_NightDeparture);
  HarmonyInstance.Patch(nightSchedule,prefix:new HarmonyMethod(nightSchedulePatch,"ScheduleWaveToday_Prefix"),finalizer:new HarmonyMethod(nightSchedulePatch,"ScheduleWaveToday_Finalizer"));
  LogHookCounts(nightSchedule);
  var nightTravel=AccessTools.Method(typeof(Il2Cpp.EnemyManager),"GetWaveTravelTime",new[]{typeof(Il2Cpp.Wave),typeof(float),typeof(int)})??throw new MissingMethodException("EnemyManager.GetWaveTravelTime(Wave,float,int)");
  HarmonyInstance.Patch(nightTravel,postfix:new HarmonyMethod(typeof(KingdomEnhancedMod.PatchWorld_NightDepartureTravel),"GetWaveTravelTime_Postfix"));
  LogHookCounts(nightTravel);
  LoggerInstance.Msg("ANDROID_NIGHT_DEPARTURE_HOOKS_INSTALLED sharedSource=true nativeSchedule=true enabled="+KingdomEnhancedMod.ModConfig.NightDepartureEnabled.Value);
  var worldLoaded=AccessTools.Method(typeof(Il2Cpp.World),"OnLevelLoaded",Type.EmptyTypes)??throw new MissingMethodException("World.OnLevelLoaded()");
  HarmonyInstance.Patch(worldLoaded,postfix:new HarmonyMethod(typeof(Probe),nameof(FarmCatsWorldLoaded)));
  LogHookCounts(worldLoaded);
  foreach(var entry in new[]{("OnEnable","Cat_OnEnable_FarmMovement_Patch",true), ("OnDisable","Cat_OnDisable_FarmMovement_Patch",false), ("Update","Cat_Update_FarmMovement_Patch",true)})
  {
   var target=AccessTools.Method(typeof(Il2Cpp.Cat),entry.Item1,Type.EmptyTypes)??throw new MissingMethodException("Cat."+entry.Item1);
   var patchType=typeof(KingdomEnhancedMod.FarmCatMovement).Assembly.GetType("KingdomEnhancedMod."+entry.Item2,true);
   var handler=new HarmonyMethod(patchType,entry.Item3?"Postfix":"Prefix");
   HarmonyInstance.Patch(target,prefix:entry.Item3?null:handler,postfix:entry.Item3?handler:null);
   LogHookCounts(target);
  }
  LoggerInstance.Msg("ANDROID_FARM_CATS_HOOKS_INSTALLED modernOwner=true");
  try { LoggerInstance.Msg($"OHMYMODS_GRAPHICS api={SystemInfo.graphicsDeviceType} device={SystemInfo.graphicsDeviceName} maxTexture={SystemInfo.maxTextureSize}"); }
  catch(Exception ex) { LoggerInstance.Warning("Graphics info unavailable: "+ex.Message); }
 }
 private static void FarmCatsWorldLoaded(Il2Cpp.World __instance)
 {
  if(KingdomEnhancedMod.ModConfig.Enabled.Value&&KingdomEnhancedMod.ModConfig.FarmCatsEnabled.Value&&__instance!=null)
   KingdomEnhancedMod.PatchWorld_FarmCats.Schedule(__instance);
 }
 public override void OnLateUpdate()=>KingdomEnhancedMod.GreekScaleScope.MaintainRegisteredY();
 private void PatchStamina(Type targetType,string name,Type patchType,string before,string after,string finish=null)
 {
  var target=AccessTools.Method(targetType,name)??throw new MissingMethodException(name);
  HarmonyInstance.Patch(target,prefix:new HarmonyMethod(patchType,before),postfix:new HarmonyMethod(patchType,after),finalizer:finish==null?null:new HarmonyMethod(patchType,finish));
  LogHookCounts(target);
 }
 private void LogHookCounts(System.Reflection.MethodBase target)
 {
  var info=HarmonyLib.Harmony.GetPatchInfo(target);
  var owner=target.DeclaringType;
  var typeName=owner.IsNested?owner.DeclaringType.Name+"."+owner.Name:owner.Name;
  LoggerInstance.Msg($"ANDROID_HOOK_COUNTS {typeName}.{target.Name} parameters={target.GetParameters().Length} prefixes={info.Prefixes.Count} postfixes={info.Postfixes.Count} finalizers={info.Finalizers.Count}");
 }
 public override void OnUpdate()
 {
  KingdomEnhancedMod.GreekScaleScope.Tick();
  MobileCalendar.Tick();
  MobilePopulation.Tick();
  KingdomEnhancedMod.PatchPlayer_HoldPurchase.Tick();
  KingdomEnhancedMod.PatchWorld_OptionalVegetation.Tick();
  if (setupAttempted) return;
  setupAttempted = true;
  try
  {
   ClassInjector.RegisterTypeInIl2Cpp<ProbeTicker>();
   var go = new GameObject("OhMyMods.AndroidProbe");
   UnityEngine.Object.DontDestroyOnLoad(go);
   go.AddComponent<ProbeTicker>();
   LoggerInstance.Msg("OHMYMODS_ANDROID_COMPONENT_CREATED");
  }
  catch (Exception ex) { LoggerInstance.Error("OHMYMODS_ANDROID_COMPONENT_FAILED " + ex); }
 }
 public override void OnSceneWasLoaded(int index, string name) => LoggerInstance.Msg("OHMYMODS_ANDROID_SCENE " + index + " " + name);
}
public sealed class ProbeTicker : MonoBehaviour
{
 private readonly MobileUiInputSurface inputSurface = new();
 private readonly MobileModPanel panel = new();
 private bool guiLogged;
 private int actionLogs;
 private int ownControlId;
 private bool renderFailed;
 private Texture2D orb;
 private GUIContent orbContent;
 private GUIStyle orbStyle;
 internal static bool UiReady;
 internal static readonly FloatLayout Layout = new();
 public ProbeTicker(IntPtr ptr) : base(ptr) { }
 public void OnDisable() { CancelGesture(); }
 public void OnDestroy() { CancelGesture(); panel.Dispose(); inputSurface.Dispose(); }
 public void OnApplicationFocus(bool focused) { if (!focused) CancelGesture(); }
 private void CancelGesture() { panel.CancelGesture(); UiReady=false; FloatInput.Reset(); if (ownControlId!=0 && GUIUtility.hotControl==ownControlId) GUIUtility.hotControl=0; Layout.Cancel(); inputSurface.Hide(); }
 public void OnGUI()
 {
  if (renderFailed) return;
  if (Il2Cpp.ProgramDirector.state!=Il2Cpp.ProgramDirector.State.RunningGame) { CancelGesture(); return; }
  string stage="color"; Color oldColor=Color.white; var oldMatrix=GUI.matrix;
  var oldSkin=GUI.skin; var oldBackground=GUI.backgroundColor; var oldContent=GUI.contentColor;
  bool oldEnabled=GUI.enabled,oldChanged=GUI.changed;
  try
  {
   oldColor=GUI.color; GUI.matrix=Matrix4x4.identity; stage="layout";
   var safe=Screen.safeArea;
   Layout.Resize(Screen.width,Screen.height,safe.xMin,Screen.height-safe.yMax,safe.xMax,Screen.height-safe.yMin);
   if (!Layout.Captured && ownControlId!=0 && GUIUtility.hotControl==ownControlId) GUIUtility.hotControl=0;
   if (!guiLogged) { guiLogged=true; MelonLogger.Msg($"OHMYMODS_FLOAT_GUI size={Screen.width}x{Screen.height} diameter={Layout.Diameter}"); }
   stage="texture"; if (orb == null) CreateOrb();
   stage="control";
   int id=GUIUtility.GetControlID(0,FocusType.Passive,new Rect(Layout.X-Layout.TouchSize/2,Layout.Y-Layout.TouchSize/2,Layout.TouchSize,Layout.TouchSize)); ownControlId=id;
   stage="events"; var e=Event.current;
   if (e.type == EventType.MouseDown && e.button==0 && Layout.Begin(e.mousePosition.x,e.mousePosition.y))
   { GUIUtility.hotControl=id; e.Use(); }
   else if (GUIUtility.hotControl==id && Layout.Captured && e.type==EventType.MouseDrag)
   { Layout.Move(e.mousePosition.x,e.mousePosition.y); e.Use(); }
   else if (GUIUtility.hotControl==id && Layout.Captured && e.type==EventType.MouseUp)
   {
    bool clicked=Layout.End(e.mousePosition.x,e.mousePosition.y); GUIUtility.hotControl=0;
    if(clicked) panel.CancelGesture();
    if (actionLogs++ < 10) MelonLogger.Msg($"OHMYMODS_FLOAT_{(clicked ? "TOGGLE" : "DRAG")} open={Layout.Expanded} x={Layout.X:0.0} y={Layout.Y:0.0}");
    e.Use();
   }
   stage="draw"; GUI.color=new Color(1,1,1,Layout.Expanded || Layout.Captured ? .95f : .62f);
   GUI.Label(new Rect(Layout.X-Layout.Diameter/2,Layout.Y-Layout.Diameter/2,Layout.Diameter,Layout.Diameter),orbContent,orbStyle);
   MobileCalendar.Draw(Layout.Scale);
   UiReady=true;
   GUI.color=oldColor;
   if (Layout.Expanded)
   {
    GUI.color=GUI.backgroundColor=GUI.contentColor=Color.white;
    GUI.enabled=true;
    panel.Draw(Layout);
   }
   stage="input surface"; inputSurface.Sync(gameObject,Layout,UiReady);
  }
  catch (Exception ex) { renderFailed=true; UiReady=false; CancelGesture(); MelonLogger.Error("OHMYMODS_FLOAT_RENDER_FAILED stage="+stage+" "+ex); }
  finally
  {
   if (stage!="color") GUI.color=oldColor;
   GUI.skin=oldSkin; GUI.backgroundColor=oldBackground; GUI.contentColor=oldContent;
   GUI.matrix=oldMatrix; GUI.enabled=oldEnabled; GUI.changed=oldChanged;
  }
 }
 private void CreateOrb()
 {
  orb=new Texture2D(48,48,TextureFormat.RGBA32,false);
  orb.filterMode=FilterMode.Bilinear;
  for(int y=0;y<48;y++) for(int x=0;x<48;x++)
  {
   float dx=x-23.5f,dy=y-23.5f,dist=(float)Math.Sqrt(dx*dx+dy*dy);
   Color c=dist>23 ? new Color(0,0,0,0) : dist>20 ? new Color(.78f,.63f,.29f,Math.Min(1,24-dist)) : new Color(.10f,.12f,.14f,1);
   bool crown=(y>=14&&y<=18&&x>=12&&x<=35) || (y>=19&&y<=30&&x>=13&&x<=34&&((x<=17)||(x>=30)||(x>=21&&x<=26)||(y<=24)));
   if(crown) c=new Color(.95f,.83f,.48f,1);
   orb.SetPixel(x,y,c);
  }
  orb.Apply(false,false);
  orbStyle=new GUIStyle(); orbContent=new GUIContent(""); orbContent.image=orb;
 }
}
