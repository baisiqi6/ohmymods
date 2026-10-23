// Land12 arrival placement (private QA): after native ApplyStartPosition has placed P1 at the wreck
// dock point (wreck.x + 0.5) for the single active PalaceSelected arrival, move P1 exactly once to
// the frozen layout gate minus 2; legacy added-region gateway remains unchanged.
// Patched current-interop overloads (exactly two):
//   CampaignSaveData.ApplyToScene()  prefix = open arrival window | finalizer = close own window
//   CampaignSaveData.ApplyStartPosition(Player,Player,Boat,WreckPlaceholder,bool,bool,bool)
//        prefix  = claim this window/trip budget from typed args __0..__6 only
//        postfix = one Transform.position write + readback
// Start.__instance is never read or written (2.4 correction: the old diagnostic misread it).
// No frame correction, no camera/boat/wreck/scene changes, no wallet/steed identity/Carry/RPC/save
// writes, no steed move. Cold load keeps its saved position, returns are untouched. Both private
// gates (PALACE_ARRIVAL_PRIVATE=1 + PalaceLand12Runtime.Enabled two roots) must hold, else no hook.
// Root registers InstallOnce(Harmony) in Main.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace KingdomEnhancedMod;

internal static class PalaceArrivalPlacement
{
    private const string EnableEnv = "PALACE_ARRIVAL_PRIVATE";
    private const float NativeTol = 0.25f;  // native arrival must still sit at wreck.x + 0.5
    private const float AnchorTol = 0.05f;  // campfire x and setter readback tolerance
    private static readonly object Sync = new object();
    private static readonly HashSet<string> Rejected = new HashSet<string>();
    private static readonly HashSet<string> AttemptedTrips = new HashSet<string>();
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static bool _installed;
    private static Window _window;  // exact arrival window; managed refs only (pointers/string/float)

    internal static bool Enabled => PalaceLand12Runtime.Enabled
        && PalaceRuntimeProfile.Capability(EnableEnv);

    internal static void InstallOnce(Harmony harmony)
    {
        if (_installed || !Enabled) return;
        _installed = true;
        try
        {
            MethodInfo scene = AccessTools.Method(typeof(CampaignSaveData), "ApplyToScene", Type.EmptyTypes);
            MethodInfo start = AccessTools.Method(typeof(CampaignSaveData), "ApplyStartPosition",
                new[] { typeof(Player), typeof(Player), typeof(Boat), typeof(WreckPlaceholder),
                        typeof(bool), typeof(bool), typeof(bool) });
            if (harmony == null || scene == null || start == null
                || scene.ReturnType != typeof(void) || start.ReturnType != typeof(void))
            { Log("ERROR exact interop overload missing; nothing patched"); return; }
            harmony.Patch(scene,
                prefix: new HarmonyMethod(typeof(PalaceArrivalPlacement), nameof(ScenePrefix)),
                finalizer: new HarmonyMethod(typeof(PalaceArrivalPlacement), nameof(SceneFinalizer)));
            harmony.Patch(start,
                prefix: new HarmonyMethod(typeof(PalaceArrivalPlacement), nameof(StartPrefix)),
                postfix: new HarmonyMethod(typeof(PalaceArrivalPlacement), nameof(StartPostfix)));
            Log("armed: ApplyToScene window + ApplyStartPosition typed-arg placement (once per exact window/trip)");
        }
        catch (Exception e) { Log("ERROR install failed: " + e.GetType().Name + " " + e.Message); }
    }

    // ------------------------------------------------- ApplyToScene: window open / close own window

    private static bool ScenePrefix(CampaignSaveData __instance, out Window __state)
    {
        __state = null;
        try
        {
            lock (Sync)
            {
                if (_window != null) { _window.Depth++; __state = _window; return true; } // nested Start cannot claim
                Window w = TryOpen(__instance, out string reason);
                if (w == null) { Reject("open:" + reason); return true; }
                _window = w;
                __state = w;
                Log("window open trip=" + w.TripId + " game=" + Hex(w.Game) + " kingdom=" + Hex(w.Kingdom)
                    + " campaign=" + Hex(w.Campaign) + " p1=" + Hex(w.PlayerOne) + " campfireX=" + F(w.CampfireX));
            }
        }
        catch (Exception e) { Log("window open fault: " + e.GetType().Name); }
        return true;  // original always continues
    }

    private static Exception SceneFinalizer(CampaignSaveData __instance, Window __state, Exception __exception)
    {
        try
        {
            if (__state != null && ReferenceEquals(_window, __state))
            {
                bool closed;
                lock (Sync) { closed = --__state.Depth == 0; if (closed) _window = null; }
                if (closed) Log("window closed trip=" + __state.TripId + " claimed=" + (__state.BudgetConsumed ? "1" : "0"));
            }
        }
        catch (Exception) { }
        return __exception;  // never change, never swallow the original exception
    }

    // ------------------------------ ApplyStartPosition: typed-arg capture / single position write

    private static bool StartPrefix(Player __0, Boat __2, WreckPlaceholder __3, bool __4, bool __5,
        out Window __state)
    {
        __state = null;
        try
        {
            Window w = _window;
            if (w == null || w.Depth != 1 || __0 == null || __0.Pointer == IntPtr.Zero) return true;
            Managers m = Managers.Inst;
            if (m == null || m.game == null || m.kingdom == null) return true;
            string bad = m.game.Pointer != w.Game || m.kingdom.Pointer != w.Kingdom ? "world"
                : m.kingdom.playerOne == null || m.kingdom.playerOne.Pointer != __0.Pointer
                    || m.kingdom.playerOne.Pointer != w.PlayerOne ? "player"
                : !(__4 && !__5) ? "flags:sailing=" + __4 + ",newReign=" + __5
                : __2 != null ? "boat-present"
                : __3 == null ? "wreck-null" : null;
            if (bad != null) { Reject("capture:" + bad); return true; }
            lock (Sync)
            {
                string placementKey = w.TripId + (w.Returning ? ":return" : ":enter");
                if (w.BudgetConsumed || AttemptedTrips.Count >= 8 || !AttemptedTrips.Add(placementKey)) return true;
                w.BudgetConsumed = true;            // one-way; later rejection never refunds
            }
            __state = w;
        }
        catch (Exception e) { Log("Start capture fault: " + e.GetType().Name); }
        return true;  // original always continues; other native positioning logic untouched
    }

    private static void StartPostfix(Player __0, WreckPlaceholder __3, Window __state)
    {
        if (__state == null) return;
        string reason="ok", original="unknown", reached="unknown";
        bool wrote=false, confirmed=false;
        try
        {
            Managers m=Managers.Inst;
            if (!ReferenceEquals(_window,__state)) reason="window-closed";
            else if (__0==null || __0.Pointer==IntPtr.Zero || __0.Pointer!=__state.PlayerOne) reason="player";
            else if (m==null || m.game==null || m.game.Pointer!=__state.Game || m.kingdom==null || m.kingdom.Pointer!=__state.Kingdom) reason="world";
            else if (__3==null || __3.transform==null) reason="wreck";
            else
            {
                Vector3 old=__0.transform.position;
                original=V(old);
                float targetX=0;
                if (!NativeNear(old.x,__3.transform.position.x+0.5f)) reason="native-x";
                else if (!Finite(old.y) || !Finite(old.z)) reason="native-yz";
                else if (__state.Returning)
                {
                    if (!ReturnRecordCurrent(__state,m.game)) reason="return-record/owner-drift";
                    else if (!PalaceIslandTravel.TryGetReturningArrivalAnchor(m.game,__state.TripId,out var live)
                        || !PalaceIslandTravel.SameAnchor(live,__state.EntryAnchor)) reason="return-claim";
                    else if (!PalaceReturnAnchor.TryValidateDestination(m.game,m.kingdom,live,old.y,out reason)) { }
                    else if (!GroundOk(m,live.StandX,old.y,out IntPtr ground) || ground!=__state.Ground) reason="return-ground";
                    else { targetX=live.StandX; reason="ok"; }
                }
                else
                {
                    if(__state.Layout!=null)
                    {
                        if(!EntryLayoutCurrent(__state,m)) reason="entry-layout/trip-drift";
                        else if(!NativeNear(__3.transform.position.x,4f+__state.Recipe.SpawnRootShiftX)) reason="recipe-wreck-x";
                        else if(!GroundOk(m,__state.Recipe.FreshStandX,old.y,out IntPtr ground) || ground!=__state.Ground) reason="ground";
                        else targetX=__state.Recipe.FreshStandX;
                    }
                    else
                    {
                        Campfire campfire=m.kingdom.campfire;
                        if(PalaceCourtArea.DedicatedWholeEnabled) reason="legacy-layout-mode-drift";
                        else if (campfire==null || campfire.gameObject==null || campfire.transform==null || !campfire.gameObject.activeInHierarchy) reason="campfire";
                        else
                        {
                            float anchorX=PalaceEntryAnchor(m,campfire);
                            if (!Finite(anchorX) || Math.Abs(anchorX-__state.CampfireX)>AnchorTol) reason="anchor-x";
                            else if (!GroundOk(m,anchorX-2f,old.y,out IntPtr ground) || ground!=__state.Ground) reason="ground";
                            else targetX=anchorX-2f;
                        }
                    }
                }
                if (reason=="ok")
                {
                    Vector3 target=new Vector3(targetX,old.y,old.z);
                    __0.transform.position=target; // single write after exact native Start; no subsequent correction
                    wrote=true;
                    Vector3 read=__0.transform.position;
                    reached=V(read);
                    confirmed=Math.Abs(read.x-target.x)<=AnchorTol && Math.Abs(read.y-target.y)<=AnchorTol && Math.Abs(read.z-target.z)<=AnchorTol;
                    if (!confirmed) reason="readback";
                    else if (!__state.Returning) PalaceReturnGate.ArmArrivalRelease(m.game,m.kingdom,__0);
                }
            }
        }
        catch(Exception e) { reason="fault:"+e.GetType().Name; }
        Log("place trip="+__state.TripId+" direction="+(__state.Returning?"return":"enter")+" result="+(confirmed?"ok":"reject")
            +" once="+(wrote?"1":"0")+" original="+original+" new="+reached+" reason="+reason);
    }

    // ------------------------------------------------------------------ window scope (fail-closed)

    private static Window TryOpen(CampaignSaveData instance,out string reason)
    {
        reason="";
        if (instance==null || instance.Pointer==IntPtr.Zero) { reason="instance"; return null; }
        if (!ExtensionIslandRuntime.TryScope(out CampaignSaveData current) || current.Pointer!=instance.Pointer) { reason="scope"; return null; }
        Managers m=Managers.Inst;
        if (m==null || m.game==null || m.kingdom==null) { reason="managers"; return null; }
        Game game=m.game;
        if (game.currentLand!=instance.CurrentLand || game.state!=Game.State.Loading) { reason="land/state"; return null; }
        GlobalSaveData global=GlobalSaveData.loaded;
        if (global==null || global.InChallenge) { reason="global"; return null; }
        CampaignSaveData owner=global.GetCurrentCampaign();
        if (owner==null || owner.Pointer!=instance.Pointer) { reason="current-campaign"; return null; }
        BiomeHolder biome=BiomeHolder.Inst;
        if (NetworkBigBoss.IsOnline || biome==null || biome.BiomeIndex!=BiomeHolder.GreeceBiomeIndex) { reason="offline/greek"; return null; }
        if (PalaceTripContext.Read(global,out PalaceTripRecord trip,out _)!=PalaceTripContext.ReadStatus.Ok) { reason="trip"; return null; }
        string tripId=PalaceIslandTravel.CurrentTripId;
        if (string.IsNullOrEmpty(tripId) || trip.TripId!=tripId) { reason="trip-id"; return null; }
        bool returning=trip.Phase==PalaceTripContext.PhaseReturnSelected;
        PalaceEntryAnchorData entry=null;
        if (returning)
        {
            if (!PalaceIslandTravel.ReturnAnchorEnabled || !ReturnVersionAllowed(game,trip) || trip.SourceEntryAnchor==null
                || instance.CurrentLand!=trip.SourceLand || !PalaceTripContext.TryBinding(global,instance,out string guid,out int slot,out reason)
                || !PalaceTripContext.RecordMatches(instance,guid,slot,trip,out reason)
                || !PalaceIslandTravel.TryGetReturningArrivalAnchor(game,tripId,out entry)
                || !PalaceIslandTravel.SameAnchor(entry,trip.SourceEntryAnchor)) { reason="return-anchor-scope"; return null; }
        }
        else if (trip.Phase!=PalaceTripContext.PhasePalaceSelected || instance.CurrentLand!=PalaceLand12Runtime.LandIndex
            || !PalaceIslandTravel.IsEnteringArrivalGame(game,tripId)) { reason="enter-arrival"; return null; }
        if (!AgentProved()) { reason="agent"; return null; }
        Il2CppObjectBase exactAgent=Filer.agent as Il2CppObjectBase;
        if (exactAgent==null || exactAgent.Pointer==IntPtr.Zero) { reason="agent-missing"; return null; }
        Player p1=m.kingdom.playerOne;
        if (p1==null || p1.Pointer==IntPtr.Zero) { reason="p1"; return null; }
        float anchorX;
        PalaceCourtLayoutView layout=null;
        if (returning) anchorX=entry.EntryX;
        else if(PalaceCourtArea.DedicatedWholeEnabled || trip.Version==4)
        {
            if(!PalaceCourtArea.TryGetLayout(m.level,out layout,out reason) || !EntryRecipeMatches(game,trip,layout))
            { reason="entry-layout-key:"+reason; return null; }
            anchorX=layout.GateWorldX;
        }
        else
        {
            Campfire campfire=m.kingdom.campfire;
            if (campfire==null || campfire.gameObject==null || campfire.transform==null || !campfire.gameObject.activeInHierarchy) { reason="campfire"; return null; }
            anchorX=PalaceEntryAnchor(m,campfire);
        }
        float standX=returning?entry.StandX:anchorX-2f;
        if (!Finite(anchorX) || !GroundOk(m,standX,null,out IntPtr ground)) { reason="ground/anchor"; return null; }
        return new Window { Game=game.Pointer,Kingdom=m.kingdom.Pointer,Campaign=instance.Pointer,
            PlayerOne=p1.Pointer,TripId=tripId,CampfireX=anchorX,Ground=ground,Returning=returning,EntryAnchor=entry,
            Global=global.Pointer,Agent=exactAgent.Pointer,Layout=layout,Recipe=layout!=null?layout.Recipe:null };
    }

    private static bool EntryRecipeMatches(Game game,PalaceTripRecord trip,PalaceCourtLayoutView view)
    {
        if(view==null || !PalaceLayoutRecipe.IsKnown(view.Recipe))return false;
        if(trip.Version==4)
            return ReferenceEquals(trip.DestinationLayout,view.Recipe)
                && PalaceIslandTravel.TryGetActiveArrivalLayout(game,trip.TripId,out var active)
                && ReferenceEquals(active,view.Recipe);
        return (trip.Version==1 || trip.Version==2 || trip.Version==3)
            && ReferenceEquals(view.Recipe,PalaceLayoutRecipe.Known44);
    }

    private static bool ReturnVersionAllowed(Game game,PalaceTripRecord trip)
        => trip.Version==2 || (trip.Version==4 && PalaceLayoutRecipe.IsKnown(trip.DestinationLayout)
            && PalaceIslandTravel.TryGetActiveArrivalLayout(game,trip.TripId,out var active)
            && ReferenceEquals(active,trip.DestinationLayout));

    private static bool EntryLayoutCurrent(Window w,Managers m)
    {
        GlobalSaveData g=GlobalSaveData.loaded;
        CampaignSaveData c=CampaignSaveData.current;
        Il2CppObjectBase agent=Filer.agent as Il2CppObjectBase;
        if(g==null || g.Pointer!=w.Global || g.InChallenge || c==null || c.Pointer!=w.Campaign
            || c.CurrentLand!=PalaceLand12Runtime.LandIndex || m.game.currentLand!=PalaceLand12Runtime.LandIndex
            || agent==null || agent.Pointer!=w.Agent || !AgentProved()
            || g.GetCurrentCampaign()==null || g.GetCurrentCampaign().Pointer!=w.Campaign
            || !PalaceIslandTravel.IsEnteringArrivalGame(m.game,w.TripId)
            || !PalaceTripContext.TryBinding(g,c,out string guid,out int slot,out _)
            || PalaceTripContext.Read(g,out var trip,out _)!=PalaceTripContext.ReadStatus.Ok
            || trip.TripId!=w.TripId || trip.Phase!=PalaceTripContext.PhasePalaceSelected
            || !PalaceTripContext.RecordMatches(c,guid,slot,trip,out _)
            || !PalaceCourtArea.TryGetLayout(m.level,out var current,out _)
            || !ReferenceEquals(current,w.Layout) || !ReferenceEquals(current.Recipe,w.Recipe)
            || Math.Abs(current.GateWorldX-w.CampfireX)>AnchorTol) return false;
        return EntryRecipeMatches(m.game,trip,current);
    }

    private static float PalaceEntryAnchor(Managers managers, Campfire campfire)
    {
        if (PalaceCourtArea.Enabled && PalaceCourtArea.TryGetSpan(managers.level, out float left, out float right, out _))
            return PalaceCourtArea.RenderAxis(left, right);
        return campfire.transform.position.x; // Existing safe base gateway if the new area failed; no art success claim.
    }

    private static bool GroundOk(Managers m, float x, float? y, out IntPtr pointer)
    {
        pointer = IntPtr.Zero;
        BoxCollider2D ground = m.level != null ? m.level.GroundCollider : null;
        if (ground == null || !ground.enabled || ground.isTrigger || !ground.gameObject.activeInHierarchy
            || m.world == null || m.world.gameLayer == null
            || ground.gameObject.scene.handle != m.world.gameLayer.gameObject.scene.handle) return false;
        Bounds b = ground.bounds;
        if (!Finite(b.min.x) || !Finite(b.max.x) || !Finite(b.max.y)
            || x - 0.5f < b.min.x || x + 0.5f > b.max.x) return false;
        Vector2 surface = ground.ClosestPoint(new Vector2(x, b.max.y + 2f));
        if (!Finite(surface.x) || !Finite(surface.y) || Math.Abs(surface.x - x) > AnchorTol
            || (y.HasValue && Math.Abs(y.Value - surface.y) > NativeTol)) return false;
        pointer = ground.Pointer;
        return true;
    }

    private static bool AgentProved()
    {
        try
        {
            Il2CppObjectBase first = Filer.agent as Il2CppObjectBase;
            if (first == null || first.Pointer == IntPtr.Zero) return false;
            Filer.DotNetAgent agent = first.TryCast<Filer.DotNetAgent>();
            if (agent == null || agent.Pointer != first.Pointer || !PalaceSaveBindingSource.AgentMatchesCurrent(agent, out _)) return false;
            Il2CppObjectBase second = Filer.agent as Il2CppObjectBase;
            return second != null && second.Pointer == first.Pointer;
        }
        catch (Exception) { return false; }
    }

    // ------------------------------------------------------------------ helpers

    private sealed class Window
    {
        internal IntPtr Game, Kingdom, Campaign, PlayerOne, Ground, Global, Agent;
        internal string TripId = "";
        internal float CampfireX; // Historical name; freezes the verified layout gate for whole-island arrivals.
        internal bool BudgetConsumed;
        internal bool Returning;
        internal PalaceEntryAnchorData EntryAnchor;
        internal PalaceCourtLayoutView Layout;
        internal PalaceLayoutRecipe Recipe;
        internal int Depth = 1;
    }

    private static bool NativeNear(float x, float native) => Finite(x) && Finite(native) && Math.Abs(x - native) <= NativeTol;
    private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    private static string Hex(IntPtr p) => "0x" + p.ToInt64().ToString("x");
    private static string F(float v) => v.ToString("0.###", Inv);
    private static string V(Vector3 v) => F(v.x) + "/" + F(v.y) + "/" + F(v.z);
    private static void Reject(string rule)
    {
        lock (Sync) { if (!Rejected.Add(rule)) return; }
        Log("window reject: " + rule);
    }
    private static void Log(string text) => PalaceVisibleEntry.Log("[ArrivalPlacement] " + text);
    // Exactly once after native Start; no re-arm or follow-up correction if a load writer changed identity.
    private static bool ReturnRecordCurrent(Window w,Game game)
    {
        if (!PalaceIslandTravel.ReturnAnchorEnabled || NetworkBigBoss.IsOnline || !AgentProved()) return false;
        Il2CppObjectBase agent=Filer.agent as Il2CppObjectBase;
        GlobalSaveData global=GlobalSaveData.loaded;
        CampaignSaveData campaign=CampaignSaveData.current;
        if (agent==null || agent.Pointer!=w.Agent || global==null || global.Pointer!=w.Global || global.InChallenge
            || campaign==null || campaign.Pointer!=w.Campaign || game.state!=Game.State.Loading) return false;
        CampaignSaveData owner=global.GetCurrentCampaign();
        if (owner==null || owner.Pointer!=w.Campaign || campaign.CurrentLand!=game.currentLand
            || !PalaceTripContext.TryBinding(global,campaign,out string guid,out int slot,out _)
            || PalaceTripContext.Read(global,out PalaceTripRecord trip,out _)!=PalaceTripContext.ReadStatus.Ok
            || !ReturnVersionAllowed(game,trip) || trip.Phase!=PalaceTripContext.PhaseReturnSelected || trip.TripId!=w.TripId
            || trip.SourceLand!=game.currentLand || !PalaceTripContext.RecordMatches(campaign,guid,slot,trip,out _)
            || !PalaceIslandTravel.SameAnchor(trip.SourceEntryAnchor,w.EntryAnchor)) return false;
        return true;
    }

}
