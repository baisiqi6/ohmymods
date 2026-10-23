#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace KingdomEnhancedMod;

// Preparation-attempt accounting. WaitingForOwner is the only result that leaves every
// preparation field and the caller's per-game marker untouched: it is limited to the existing
// owner/Playing guard before any attempt, so the already-bounded readiness window may ask again
// on its next tick. Attempted covers an accepted attempt, a cached same-owner result and every
// terminal failure (including exceptions); the caller consumes its one attempt marker exactly
// once and no later tick may retry that game.
internal enum PalacePaidPreparationOutcome
{
    WaitingForOwner,
    Attempted
}

// Opaque session ownership; no caller can supply a successful-native flag or mutate journal state.
internal sealed partial class PalacePaidHandle
{
    internal PalacePaidScope Owner { get; }
    internal Guid TripId { get; }
    internal int SourceLand { get; }
    internal PalaceLayoutRecipe? DestinationLayout => Envelope?.Current.TripId == TripId ? Envelope.Current.DestinationLayout : null;
    // Strict old44 evidence is acquired by D at the explicit cold/return restore boundary.
    // It never changes the legacy journal's unknown layout or authorizes a new departure.
    private bool LegacyLayoutVerified;
    internal bool MatchesDestination(PalaceTripRecord trip)
    {
        if (trip == null) return false;
        if (trip.Version == 4)
            return PalaceLayoutRecipe.IsKnown(DestinationLayout!) && ReferenceEquals(DestinationLayout, trip.DestinationLayout);
        return trip.Version == 3 && DestinationLayout == null && LegacyLayoutVerified
            && trip.DestinationLayoutId == null && trip.DestinationLayoutVersion == null;
    }
    internal PalaceEntryAnchorData Anchor => new() { EntryX=SavedAnchor.EntryX, EntryY=SavedAnchor.EntryY,
        EntryZ=SavedAnchor.EntryZ, SourcePlayerX=SavedAnchor.SourcePlayerX, StandX=SavedAnchor.StandX, Side=SavedAnchor.Side };
    internal PalacePaidStage Stage => Envelope?.Current.Stage ?? PalacePaidStage.Unknown;
    internal bool SettlementObserved => Envelope?.Current.SettlementObserved == true;
    internal bool HasFault => Fault.Length!=0;
    internal PalaceSourceClockReceipt? SourceClockReceipt { get; private set; }
    private readonly PalacePaidJournalStore Store;
    private readonly PalacePaidAnchor SavedAnchor;
    private PalacePaidEnvelope? Envelope;
    private string? RawSha, PayerKey;
    private Game? Game;
    private Kingdom? Kingdom;
    private GlobalSaveData? Global;
    private CampaignSaveData? Campaign;
    private Player? Payer;
    private Wallet? Wallet;
    private PayableComponent? Payable;
    private Il2CppObjectBase? Agent;
    private string Folder="", Filename="", Fault="";
    private int Scene, OnPayFrame=-1;
#if PALACE_PAID_CAPTURE_DIAGNOSTICS
    internal readonly PalacePaidCaptureDiagnostics CaptureDiagnostic;
#endif
    private bool Consumed, CancelNormal, RequiresNativeVerification;
    private Engine.InputObservation? PaidCall;
    private CaptureState? PendingCapture, ReturnCapture, VerificationCapture;
    private PalacePaidHandle(PalacePaidScope owner, Guid trip, int source, PalacePaidAnchor anchor,
        PalacePaidJournalStore store, PalacePaidEnvelope? envelope, string? raw)
    { Owner=owner; TripId=trip; SourceLand=source; SavedAnchor=anchor; Store=store; Envelope=envelope; RawSha=raw;
      PayerKey=envelope!=null&&envelope.Current.TripId==trip ? envelope.Current.CaptureProof?.Payer : null;
#if PALACE_PAID_CAPTURE_DIAGNOSTICS
      CaptureDiagnostic=new PalacePaidCaptureDiagnostics(trip,PalaceVisibleEntry.Log);
#endif
    }
    private void SetClock(PalaceSourceClockReceipt receipt) { SourceClockReceipt=receipt; }
}

// Tokens carry an unnameable private state object. Root may retain/pass them, never edit proof flags.
internal sealed class PalacePaidCaptureToken
{
    private readonly object _state;
    internal object State => _state;
    internal PalacePaidCaptureToken(object state) { _state=state; }
}
internal sealed class PalacePaidPrefsToken
{
    private readonly object _state;
    internal object State => _state;
    internal PalacePaidPrefsToken(object state) { _state=state; }
}

internal sealed partial class PalacePaidHandle
{
private sealed class CaptureState
{
    internal readonly PalacePaidCaptureToken Opaque;
    internal readonly CaptureState? Previous;
    internal readonly PalacePaidHandle? Handle;
    internal readonly long Sequence;
    internal bool Valid, Closed, MarkerSeen, WalletSeen, Faulted, Returned, Verification;
    internal IslandSaveData? Island;
    internal int[]? WalletCurrencies;
    internal string? IslandJson;
    internal PalacePaidSourceValues? Values;
    internal CaptureState(CaptureState? previous, PalacePaidHandle? handle, long sequence)
    { Previous=previous; Handle=handle; Sequence=sequence; Opaque=new PalacePaidCaptureToken(this); }
}
private sealed class PrefsState
{
    internal readonly PalacePaidPrefsToken Opaque;
    internal readonly CaptureState? Capture;
    internal readonly PrefsSaveData? Prefs;
    internal readonly PrefsState? Previous;
    internal PrefsState(CaptureState? capture, PrefsSaveData? prefs, PrefsState? previous)
    { Capture=capture; Prefs=prefs; Previous=previous; Opaque=new PalacePaidPrefsToken(this); }
}


internal static class Engine
{
    private static string? _hookOwner;
    private static bool _installed, _installAttempted;
    private static PalacePaidJournalStore? _store;
    private static PalacePaidScope? _preparedOwner;
    private static IntPtr _preparedGame;
    private static PalacePaidStoreReadResult? _prepared;
    private static string _prepareFault="not-prepared", _directory="";
    private static PalacePaidHandle? _handle;
    private static long _captureSequence;
#if PALACE_PAID_CAPTURE_DIAGNOSTICS
    private static PalacePaidCaptureDiagnostics? _nullCaptureDiagnostics;
#endif
    [ThreadStatic] private static InputObservation? _input;
    [ThreadStatic] private static CaptureState? _capture;
    [ThreadStatic] private static PrefsState? _prefs;
    private static readonly string[] PrefKeys={"KEM.CoinCourier.Campaigns.v1","KEM.HeavyShield.Campaigns.v1",
        "KingdomEnhancedMod_MusketeerRights_v1","KingdomEnhancedMod_HeroRights_v1","MyMod_SharedBankNative_v1"};

    internal sealed class InputObservation
    {
        internal readonly Player Player;
        internal readonly InputObservation? Previous;
        internal readonly int Frame;
        internal readonly bool PrefixEligible, PayKey, PayKeyDown, Touch;
        internal bool PostfixSeen, ActualRan, Finished, Faulted, OnPaySeen;
        internal InputObservation(Player p, bool eligible, bool payKey, bool payKeyDown, bool touch, InputObservation? previous)
        { Player=p; PrefixEligible=eligible; PayKey=payKey;PayKeyDown=payKeyDown;Touch=touch;Previous=previous; Frame=Time.frameCount; }
    }
    private sealed class CancelObservation
    {
        internal PalacePaidHandle? Handle;
        internal Player? Player;
        internal bool PrefixEligible, PostfixSeen, ActualRan;
    }
    private sealed class WalletObservation
    {
        internal CaptureState? Capture;
        internal Wallet? Wallet;
        internal bool PrefixEligible, PostfixSeen, ActualRan;
        internal int[]? Currencies;
    }

    internal static bool InstallOnce(Harmony harmony)
    {
        if (_installAttempted) return _installed;
        _installAttempted=true;
        try
        {
            if (harmony==null) return false;
            _hookOwner=harmony.Id;
            var targets=new[] {
                AccessTools.Method(typeof(Player),"UpdatePayState",new[]{typeof(bool),typeof(bool),typeof(bool)}),
                AccessTools.Method(typeof(Wallet),"Persistent_IBehaviour_RetrieveData",Type.EmptyTypes),
                AccessTools.Method(typeof(Player),"CancelTransaction",Type.EmptyTypes) };
            string[] names={"Input","Wallet","Cancel"};
            if (targets.Any(t=>t==null)) return false;
            for(int i=0;i<targets.Length;i++)
            {
                var pre=Method(names[i]+"Prefix"); var post=Method(names[i]+"Postfix"); var fin=Method(names[i]+"Finalizer");
                harmony.Patch(targets[i],new HarmonyMethod(pre){priority=Priority.Last},
                    new HarmonyMethod(post){priority=Priority.Last},finalizer:new HarmonyMethod(fin){priority=Priority.Last});
                var info=Harmony.GetPatchInfo(targets[i]);
                if(info==null || !Owned(info.Prefixes,pre) || !Owned(info.Postfixes,post) || !Owned(info.Finalizers,fin)) return false;
            }
            _installed=true;
            return true;
        }
        catch(Exception) { return false; } // Partial observers remain native-transparent; no broad unpatch.
    }
    private static MethodInfo Method(string name)=>AccessTools.Method(typeof(Engine),name);
    private static bool Owned(IEnumerable<Patch> patches,MethodInfo method)=>patches.Count(p=>p.owner==_hookOwner&&p.PatchMethod==method)==1;

    internal static PalacePaidPreparationOutcome PrepareAtPlaying(Game game, Kingdom kingdom)
    {
        try
        {
            if(!TryOwner(game,kingdom,out var owner,out _) || game.state!=Game.State.Playing)
                return PalacePaidPreparationOutcome.WaitingForOwner;
            if(_preparedGame==game.Pointer && _preparedOwner?.ContentEquals(owner)==true)
                return PalacePaidPreparationOutcome.Attempted;
            _preparedGame=game.Pointer; _preparedOwner=owner; _prepared=null; _prepareFault="preparation-incomplete";
            if(!_installed) { _prepareFault="hooks-unverified"; return PalacePaidPreparationOutcome.Attempted; }
            if(!EnsureStore(out var store,out var reason)) { _prepareFault=reason; return PalacePaidPreparationOutcome.Attempted; }
            _prepared=store.Read(owner); _prepareFault=_prepared.Status==PalacePaidStoreReadStatus.Held ? "journal-held:"+_prepared.Error : "";
            var record=_prepared.Envelope?.Current;
            if(CanRestoreSourceAtPlaying(_handle!=null,HasTransientResponsibility(),
                _prepared.Status==PalacePaidStoreReadStatus.Valid,record?.Owner.ContentEquals(owner)==true,
                game.currentLand,record?.SourceLand ?? -1,record?.Stage ?? PalacePaidStage.Unknown))
            {
                if(!TryBuildColdHandle(owner,record!.TripId,game.currentLand,store,_prepared,out var restored,out var restoreReason))
                { _prepareFault="source-restore:"+restoreReason; return PalacePaidPreparationOutcome.Attempted; }
                Player p=kingdom.playerOne;
                if(!ExactPayer(kingdom,p)) { _prepareFault="source-restore-P1"; return PalacePaidPreparationOutcome.Attempted; }
                BindLive(restored,game,kingdom,p,null);
                if(_handle!=null||HasTransientResponsibility()) { _prepareFault="source-restore-responsibility-drift"; return PalacePaidPreparationOutcome.Attempted; }
                _handle=restored; // Only after strict physical validation and actual live binding succeed.
            }
            return PalacePaidPreparationOutcome.Attempted;
        }
        catch(Exception e) { _prepareFault="prepare:"+e.GetType().Name; return PalacePaidPreparationOutcome.Attempted; }
    }
    private static bool EnsureStore(out PalacePaidJournalStore store,out string reason)
    {
        store=null!; reason="";
        string config=Path.GetFullPath(Paths.ConfigPath);
        string fixedDir=Path.Combine(config,"KingdomEnhancedMod.PalacePaid.v1");
        if(!PhysicalDirectory(config)) { reason="config-not-physical"; return false; }
        if(_store!=null) { if(_directory!=fixedDir||!PhysicalDirectory(fixedDir)) {reason="store-path-drift";return false;} store=_store;return true; }
        Directory.CreateDirectory(fixedDir);
        if(!PhysicalDirectory(fixedDir)) {reason="store-not-physical";return false;}
        _directory=fixedDir; _store=store=new PalacePaidJournalStore(fixedDir);return true;
    }
    private static bool PhysicalDirectory(string path)
    {
        var dir=new DirectoryInfo(path);
        for(DirectoryInfo? p=dir;p!=null;p=p.Parent)
            if(!p.Exists || p.LinkTarget!=null || (p.Attributes&FileAttributes.ReparsePoint)!=0) return false;
        return true;
    }
    internal static bool CanBeginCharge(Game game,Kingdom kingdom,Player payer,PayableComponent payable,out string reason)
    {
        reason="";
        try
        {
            if(!_installed||_prepareFault.Length!=0||_store==null||_prepared==null) {reason="not-ready:"+_prepareFault;return false;}
            if(!TryOwner(game,kingdom,out var owner,out reason)||_preparedGame!=game.Pointer||_preparedOwner?.ContentEquals(owner)!=true) return false;
            if(_handle!=null&&!PalacePaidStages.IsTerminal(_handle.Stage)) {reason="outstanding-responsibility";return false;}
            if(_handle!=null&&_handle.Fault.Length!=0) {reason="latched:"+_handle.Fault;return false;}
            if(_prepared.Envelope!=null&&!PalacePaidStages.IsTerminal(_prepared.Envelope.Current.Stage)) {reason="journal-pending";return false;}
            if(game.currentLand<0||game.currentLand>11||IslandSaveData.isSavingGame||game.state!=Game.State.Playing) {reason="source-state";return false;}
            if(!ExactPayer(kingdom,payer)||!ExactEntry(game,payer,payable)) {reason="payer-entry";return false;}
            if(Managers.Inst.director==null||!Managers.Inst.director.IsDaytime) {reason="new-fee-daytime";return false;}
            return PalacePlayable.TryRequirePaidEntryInstallation(false,out reason);
        }
        catch(Exception e) {reason="admission:"+e.GetType().Name;return false;}
    }
    internal static bool TryBeginCharge(Game game,Kingdom kingdom,Player payer,PayableComponent payable,
        PalaceEntryAnchorData anchor,PalaceDestinationDecision observed,out PalacePaidHandle handle,out string reason)
    {
        handle=null!; reason="";
        try
        {
            InputObservation? call=_input;
            if(call==null||!call.PrefixEligible||call.Faulted||call.Finished||call.Player.Pointer!=payer.Pointer
                ||call.Frame!=Time.frameCount||payer.selectedPayable==null||payer.selectedPayable.Pointer!=payable.Pointer
                ||payer._floatingCurrency==null||payer._floatingCurrency.Count!=0)
            {reason="no-exact-native-input-window";return false;}
            if(!CanBeginCharge(game,kingdom,payer,payable,out reason)||!PalaceReturnAnchor.ValidateData(anchor,out reason)) return false;
            var a=new PalacePaidAnchor(anchor.EntryX,anchor.EntryY,anchor.EntryZ,anchor.SourcePlayerX,anchor.StandX,anchor.Side);
            if(!PalaceReturnAnchor.TryCapture(game,kingdom,payer,payable,out var observedAnchor,out reason)
                ||!a.ContentEquals(new PalacePaidAnchor(observedAnchor.EntryX,observedAnchor.EntryY,observedAnchor.EntryZ,
                    observedAnchor.SourcePlayerX,observedAnchor.StandX,observedAnchor.Side)))
            {reason="anchor-not-current-native-entry";return false;}
            if(!PalaceDestinationAdmission.TryFreezeForCharge(game,CampaignSaveData.current,observed,out var frozen,out reason)
                ||!PalaceLayoutRecipe.IsKnown(frozen.Recipe)) return false;
            var previous=_handle?.Owner.ContentEquals(_preparedOwner)==true ? _handle : null;
            handle=new PalacePaidHandle(_preparedOwner!,Guid.NewGuid(),game.currentLand,a,_store!,
                previous?.Envelope ?? _prepared!.Envelope,previous?.RawSha ?? _prepared!.RawSha256);
            _handle=handle; // Preclaim is sticky even if any subsequent IO/binding fails.
            BindLive(handle,game,kingdom,payer,payable);
            if(!Apply(handle,PalacePaidRequest.Begin(new(handle.Owner),handle.TripId,handle.SourceLand,a,frozen.Recipe),out reason)) return false;
            return true;
        }
        catch(Exception e) {if(handle!=null)Hold(handle,"begin:"+e.GetType().Name);reason="begin:"+e.GetType().Name;return false;}
    }
    internal static bool CanContinueCharge(PalacePaidHandle handle,Player payer,PayableComponent payable,out string reason)
    {
        reason="";
        try
        {
            // A failed journal write must not prevent an already-entered Completed native settlement.
            if(handle!=_handle||handle.Payer==null||payer.Pointer!=handle.Payer.Pointer||handle.Payable==null
                ||payable.Pointer!=handle.Payable.Pointer||payer.wallet==null||handle.Wallet==null||payer.wallet.Pointer!=handle.Wallet.Pointer)
            {reason="inflight-reference-drift";return false;}
            if(!SameLive(handle,payer,out reason))return false;
            if(handle.Envelope==null||handle.Envelope.Current.TripId!=handle.TripId||!handle.Envelope.Current.Owner.ContentEquals(handle.Owner))
            {reason="no-durable-intent";return false;}
            if(payer._payState==Player.PayState.Completed)
                return ExactCompletedReceipt(true,payer._completingPayable!=null&&payer._completingPayable.Pointer==payable.Pointer,
                    payer._floatingCurrency==null?-1:payer._floatingCurrency.Count);
            return !handle.Consumed && handle.Stage==PalacePaidStage.ChargeIntent && handle.Fault.Length==0;
        }
        catch(Exception e){reason="continue:"+e.GetType().Name;return false;}
    }
    internal static void ObserveOnPay(PalacePaidHandle handle,Player payer)
    {
        try
        {
            var call=_input;
            if(handle!=_handle||call==null||call.Player.Pointer!=payer.Pointer||!call.PrefixEligible||call.Faulted
                ||handle.Consumed||!CanContinueCharge(handle,payer,handle.Payable!,out _)||payer._payState!=Player.PayState.Completed)
            {Hold(handle,"onpay-causality");return;}
            handle.Consumed=true; handle.OnPayFrame=Time.frameCount; handle.PaidCall=call;call.OnPaySeen=true;
            Apply(handle,PalacePaidRequest.ObservePaid(Expectation(handle),handle.TripId),out _);
        }
        catch(Exception e){Hold(handle,"onpay:"+e.GetType().Name);}
    }
    internal static bool TryObserveSettled(PalacePaidHandle handle,Player payer,out string reason)
    {
        reason="";
        try
        {
            var call=handle.PaidCall;
            if(handle!=_handle||handle.Fault.Length!=0||!handle.Consumed||call==null||!call.Finished||!call.ActualRan||call.Faulted
                ||!call.PostfixSeen||!call.OnPaySeen||Time.frameCount<=handle.OnPayFrame||!SameLive(handle,payer,out reason)
                ||payer._payState!=Player.PayState.None||payer._floatingCurrency==null||payer._floatingCurrency.Count!=0)
            {if(reason.Length==0)reason="native-settlement-pending";return false;}
            return Apply(handle,PalacePaidRequest.ObserveSettlement(Expectation(handle),handle.TripId),out reason);
        }catch(Exception e){reason="settlement:"+e.GetType().Name;Hold(handle,reason);return false;}
    }
    internal static bool TryObserveCancelled(PalacePaidHandle handle,Player payer,out string reason)
    {
        reason="";
        try
        {
            if(handle!=_handle||handle.Fault.Length!=0||handle.Consumed||!handle.CancelNormal||!SameLive(handle,payer,out reason)
                ||payer._payState!=Player.PayState.None||payer._floatingCurrency==null||payer._floatingCurrency.Count!=0)
            {if(reason.Length==0)reason="cancel-not-proven";return false;}
            if(!Apply(handle,PalacePaidRequest.Cancel(Expectation(handle),handle.TripId),out reason))return false;
            _prepared=handle.Store.Read(handle.Owner);
            return _prepared.Status==PalacePaidStoreReadStatus.Valid;
        }catch(Exception e){reason="cancel:"+e.GetType().Name;Hold(handle,reason);return false;}
    }

    private static void InputPrefix(Player __instance,bool __0,bool __1,bool __2,bool __runOriginal,out InputObservation __state)
    {
        __state=null!;
        try { __state=new InputObservation(__instance,__runOriginal,__0,__1,__2,_input);
            if(_input!=null){__state.Faulted=true;if(_handle!=null)Hold(_handle,"input-reentry");} _input=__state; }
        catch(Exception){if(_handle!=null)Hold(_handle,"input-observer-fault");}
    }
    private static void InputPostfix(bool __runOriginal,InputObservation __state)
    { if(__state==null)return;__state.PostfixSeen=true;__state.ActualRan=__state.PrefixEligible&&__runOriginal; }
    private static Exception? InputFinalizer(Exception? __exception,InputObservation __state)
    {
        if(__state==null)return __exception;
        __state.Finished=true;__state.Faulted=!NormalNativeCall(__state.PrefixEligible,__state.PostfixSeen,__state.ActualRan,_input==__state,__exception!=null,__state.Faulted);
        if(__state.Faulted&&_handle!=null&&(_handle.PaidCall==__state
            ||(_handle.Stage==PalacePaidStage.ChargeIntent&&_handle.Payer!=null&&_handle.Payer.Pointer==__state.Player.Pointer)))
            Hold(_handle,"transaction-input-not-normal");
        if(_input==__state)_input=__state.Previous;
        return __exception;
    }
    private static void CancelPrefix(Player __instance,bool __runOriginal,out CancelObservation __state)
    {
        __state=new CancelObservation {Player=__instance,PrefixEligible=__runOriginal};
        var h=_handle;if(h?.Payer!=null&&h.Payer.Pointer==__instance.Pointer&&!h.Consumed)__state.Handle=h;
    }
    private static void CancelPostfix(bool __runOriginal,CancelObservation __state)
    {if(__state==null)return;__state.PostfixSeen=true;__state.ActualRan=__state.PrefixEligible&&__runOriginal;}
    private static Exception? CancelFinalizer(Exception? __exception,CancelObservation __state)
    {
        var h=__state?.Handle;
        try
        {
            if(h!=null)
            {
                if(__exception!=null||!__state!.PostfixSeen||!__state.ActualRan||h.Consumed||!SameLive(h,__state.Player!,out _)) Hold(h,"cancel-not-normal");
                else if(h.Fault.Length==0&&h.Stage==PalacePaidStage.ChargeIntent) h.CancelNormal=true;
            }
        }catch(Exception){if(h!=null)Hold(h,"cancel-observer-fault");}
        return __exception;
    }

    internal static PalacePaidCaptureToken BeginSourceCapture(int campaign,int land,int challenge)
    {
        var h=_handle; var token=new CaptureState(_capture,h,++_captureSequence); _capture=token;
        try
        {
#if PALACE_PAID_CAPTURE_DIAGNOSTICS
            if(h==null){DiagBegin(token,campaign,land,challenge,h,false,false,1);return token.Opaque;}
            if(h.Fault.Length!=0){DiagBegin(token,campaign,land,challenge,h,false,true,2);return token.Opaque;}
            if(h.Game==null){DiagBegin(token,campaign,land,challenge,h,false,false,3);return token.Opaque;}
            if(h.Kingdom==null){DiagBegin(token,campaign,land,challenge,h,false,false,4);return token.Opaque;}
            if(h.Payer==null){DiagBegin(token,campaign,land,challenge,h,false,false,5);return token.Opaque;}
            if(_capture.Previous!=null){DiagBegin(token,campaign,land,challenge,h,false,false,6);return token.Opaque;}
            if(campaign!=h.Owner.CampaignSlot){DiagBegin(token,campaign,land,challenge,h,false,false,7);return token.Opaque;}
            if(h.Global==null){DiagBegin(token,campaign,land,challenge,h,false,false,8);return token.Opaque;}
            if(h.Global.currentCampaign!=campaign){DiagBegin(token,campaign,land,challenge,h,false,false,9);return token.Opaque;}
            if(h.Global.currentChallenge!=challenge){DiagBegin(token,campaign,land,challenge,h,false,false,10);return token.Opaque;}
            if(land!=h.SourceLand){DiagBegin(token,campaign,land,challenge,h,false,false,11);return token.Opaque;}
            if(h.Game.currentLand!=land){DiagBegin(token,campaign,land,challenge,h,false,false,12);return token.Opaque;}
            if(!SameLive(h,h.Payer,out _)){DiagBegin(token,campaign,land,challenge,h,false,false,13);return token.Opaque;}
            if(h.Global==null){DiagBegin(token,campaign,land,challenge,h,false,false,14);return token.Opaque;}
            if(h.Global.InChallenge){DiagBegin(token,campaign,land,challenge,h,false,false,15);return token.Opaque;}
#else
            if(h==null||h.Fault.Length!=0||h.Game==null||h.Kingdom==null||h.Payer==null||_capture.Previous!=null
                ||campaign!=h.Owner.CampaignSlot||h.Global==null||h.Global.currentCampaign!=campaign
                ||h.Global.currentChallenge!=challenge||land!=h.SourceLand||h.Game.currentLand!=land
                ||!SameLive(h,h.Payer,out _)||h.Global==null||h.Global.InChallenge) return token.Opaque;
#endif
            token.Returned=h.Stage==PalacePaidStage.Returned;
            token.Verification=h.Stage==PalacePaidStage.SourceCaptured&&h.RequiresNativeVerification;
            token.Valid=token.Returned || token.Verification || (h.Stage==PalacePaidStage.PaidObserved && h.Envelope!.Current.SettlementObserved);
#if PALACE_PAID_CAPTURE_DIAGNOSTICS
            DiagBegin(token,campaign,land,challenge,h,true,false,0);
#endif
        }
        catch(Exception){token.Faulted=true;
#if PALACE_PAID_CAPTURE_DIAGNOSTICS
            DiagBegin(token,campaign,land,challenge,h,false,false,99);
#endif
        }
        return token.Opaque; // An invalid inner scope still shadows an otherwise valid outer scope.
    }
    internal static void ObserveCaptureMarker(IslandSaveData island,bool runOriginal)
    {
        var t=_capture;
        if(t==null)return;
        try
        {
            if(!t.Valid||t.Closed||!runOriginal||island==null||IslandSaveData.CurrentlySavingIsland==null
                ||IslandSaveData.CurrentlySavingIsland.Pointer!=island.Pointer||t.MarkerSeen)
            {t.Faulted=true;
#if PALACE_PAID_CAPTURE_DIAGNOSTICS
                DiagMarker(t,false);
#endif
                return;}
            t.Island=island;t.MarkerSeen=true;
#if PALACE_PAID_CAPTURE_DIAGNOSTICS
            DiagMarker(t,true);
#endif
        }catch(Exception){t.Faulted=true;
#if PALACE_PAID_CAPTURE_DIAGNOSTICS
            DiagMarker(t,false);
#endif
        }
    }
    internal static void EndSourceCapture(PalacePaidCaptureToken opaque,bool runOriginal,Exception? error)
    {
        var token=opaque?.State as CaptureState;
        if(token==null||token.Opaque!=opaque)return;
        try
        {
            if(token==null)return;
            if(_capture!=token){token.Faulted=true;
#if PALACE_PAID_CAPTURE_DIAGNOSTICS
                DiagEnd(token,false,runOriginal,error!=null);
#endif
                return;}
            token.Closed=true;
            var h=token.Handle;
            if(!token.Valid||token.Faulted||!runOriginal||error!=null||!token.MarkerSeen||!token.WalletSeen
                ||token.Island==null||h?.Payer==null||!SameLive(h,h.Payer,out _)) {token.Faulted=true;
#if PALACE_PAID_CAPTURE_DIAGNOSTICS
                DiagEnd(token,false,runOriginal,error!=null);
#endif
                return;}
            token.IslandJson=JsonUtility.ToJson(token.Island,false);
            if(string.IsNullOrEmpty(token.IslandJson)){token.Faulted=true;
#if PALACE_PAID_CAPTURE_DIAGNOSTICS
                DiagEnd(token,false,runOriginal,error!=null);
#endif
                return;}
            if(token.Verification) {if(h.VerificationCapture!=null) {token.Faulted=true;
#if PALACE_PAID_CAPTURE_DIAGNOSTICS
                    DiagEnd(token,false,runOriginal,error!=null);
#endif
                    return;}h.VerificationCapture=token;}
            else if(token.Returned) {if(h.ReturnCapture!=null) {token.Faulted=true;
#if PALACE_PAID_CAPTURE_DIAGNOSTICS
                DiagEnd(token,false,runOriginal,error!=null);
#endif
                return;}h.ReturnCapture=token;}
            else {if(h.PendingCapture!=null) {token.Faulted=true;
#if PALACE_PAID_CAPTURE_DIAGNOSTICS
                DiagEnd(token,false,runOriginal,error!=null);
#endif
                return;}h.PendingCapture=token;}
#if PALACE_PAID_CAPTURE_DIAGNOSTICS
            DiagEnd(token,true,runOriginal,error!=null);
#endif
        }
        catch(Exception){if(token!=null)token.Faulted=true;
#if PALACE_PAID_CAPTURE_DIAGNOSTICS
            if(token!=null)DiagEnd(token,false,runOriginal,error!=null);
#endif
        }
        finally {if(token!=null&&_capture==token)_capture=token.Previous;}
    }
    private static void WalletPrefix(Wallet __instance,bool __runOriginal,out WalletObservation __state)
    {
        __state=new WalletObservation{Capture=_capture,Wallet=__instance,PrefixEligible=__runOriginal};
    }
    private static void WalletPostfix(Il2CppSystem.Object __result,bool __runOriginal,WalletObservation __state)
    {
        if(__state==null)return;
        __state.PostfixSeen=true;__state.ActualRan=__state.PrefixEligible&&__runOriginal;
        var t=__state.Capture;
#if PALACE_PAID_CAPTURE_DIAGNOSTICS
        if(t?.Handle?.Wallet==null||!t.Valid||t.Closed||t.Faulted||_capture!=t||__state.Wallet==null
            ||__state.Wallet.Pointer!=t.Handle.Wallet.Pointer)return; // Foreign/absent wallet: silent skip, later unobserved.
#else
        if(t?.Handle?.Wallet==null||!t.Valid||t.Closed||t.Faulted||_capture!=t||__state.Wallet==null
            ||__state.Wallet.Pointer!=t.Handle.Wallet.Pointer)return;
#endif
        try
        {
            WalletData data=__result?.TryCast<WalletData>()!;
            if(data==null||!data.usesCurrencySystem||data.currency==null){t.Faulted=true;
#if PALACE_PAID_CAPTURE_DIAGNOSTICS
                DiagWallet(t,false);
#endif
                return;}
            var c=data.currency;
            // Runtime 4dae2520ea6d36701a92f622f62a339e9c4c58b7bf5bf4744e8154f7fa0c67e2 generic field getters double-box via PointerToValueGeneric(true,false); this same map's indexer (false,true) avoids it (interop d6b249a4c5d7c833bec733d172df9a6083e87e5fc203c466d9195f0b7bc7041f).
            __state.Currencies=PalacePaidCurrencies.CopyValidated(new[]{c[CurrencyType.Coins],c[CurrencyType.Gems],c[CurrencyType.Crown],c[CurrencyType.Skulls],c[CurrencyType.Shades],c[CurrencyType.Merchandise],c[CurrencyType.Candle],c[CurrencyType.Egg]},"actual-wallet");
        }catch(Exception){t.Faulted=true;
#if PALACE_PAID_CAPTURE_DIAGNOSTICS
            DiagWallet(t,false);
#endif
        }
    }
    private static Exception? WalletFinalizer(Exception? __exception,WalletObservation __state)
    {
        var t=__state?.Capture;
        if(t?.Handle?.Wallet!=null&&__state!.Wallet!=null&&__state.Wallet.Pointer==t.Handle.Wallet.Pointer&&t.Valid)
        {
#if PALACE_PAID_CAPTURE_DIAGNOSTICS
            if(__exception!=null||!__state.PostfixSeen||!__state.ActualRan||__state.Currencies==null||t.WalletSeen||_capture!=t)
            {t.Faulted=true;DiagWallet(t,false);}
            else {t.WalletSeen=true;t.WalletCurrencies=(int[])__state.Currencies.Clone();DiagWallet(t,true);}
#else
            if(__exception!=null||!__state.PostfixSeen||!__state.ActualRan||__state.Currencies==null||t.WalletSeen||_capture!=t)t.Faulted=true;
            else {t.WalletSeen=true;t.WalletCurrencies=(int[])__state.Currencies.Clone();}
#endif
        }
        return __exception;
    }
    internal static PalacePaidPrefsToken BeginPrefsPrepare(PrefsSaveData prefs)
    {
        var h=_handle;var candidate=h?.Stage==PalacePaidStage.Returned?h.ReturnCapture:
            h?.RequiresNativeVerification==true?h.VerificationCapture:h?.PendingCapture;
#if PALACE_PAID_CAPTURE_DIAGNOSTICS
        DiagPrefsBegin(candidate,h);
#endif
        var token=new PrefsState(candidate,prefs,_prefs);_prefs=token;return token.Opaque;
    }
    internal static void EndPrefsPrepare(PalacePaidPrefsToken opaque,bool runOriginal,Exception? error)
    {
        var token=opaque?.State as PrefsState;
        if(token==null||token.Opaque!=opaque)return;
#if PALACE_PAID_CAPTURE_DIAGNOSTICS
        string sourceReason="",applyReason="";
        bool sourceRead=false,sourceOk=false,applyAttempted=false,applyOk=false;
#endif
        try
        {
            if(token==null||_prefs!=token||token.Previous!=null)return;
            var t=token.Capture;var h=t?.Handle;
            if(t==null||h==null||!t.Valid||t.Faulted||!t.Closed||t.Values!=null||!runOriginal||error!=null
                ||token.Prefs==null||h.Global?.prefs==null||token.Prefs.Pointer!=h.Global.prefs.Pointer
                ||h.Payer==null||!SameLive(h,h.Payer,out _))return;
            var values=new Dictionary<string,string>(StringComparer.Ordinal);
            var seen=new HashSet<string>(StringComparer.Ordinal);
            if(token.Prefs.srzEntries==null){t.Faulted=true;return;}
            foreach(var row in token.Prefs.srzEntries)
            {
                if(row==null||row.key==null||!seen.Add(row.key)){t.Faulted=true;return;}
                if(Array.IndexOf(PrefKeys,row.key)>=0)
                {
                    if(row.val==null){t.Faulted=true;return;}
                    values.Add(row.key,row.val);
                }
            }
            if(token.Prefs.contents==null){t.Faulted=true;return;}
            foreach(string key in PrefKeys)
            {
                bool nativePresent=values.TryGetValue(key,out var nativeRaw);
                bool contentsPresent=token.Prefs.contents.TryGetValue(key,out string contentsRaw);
                if(nativePresent!=contentsPresent||(nativePresent&&nativeRaw!=contentsRaw)){t.Faulted=true;return;}
            }
#if PALACE_PAID_CAPTURE_DIAGNOSTICS
            sourceRead=true;
            sourceOk=PalacePaidSourceProof.TryReadCapturedIsland(t.IslandJson!,h.Owner,h.SourceLand,values,out var facts,out sourceReason);
            if(sourceOk)DiagCurrencyCompare(t,facts.Currencies,t.WalletCurrencies);
            if(!sourceOk||t.WalletCurrencies==null||!PalacePaidCurrencies.ContentEquals(facts.Currencies,t.WalletCurrencies))
#else
            if(!PalacePaidSourceProof.TryReadCapturedIsland(t.IslandJson!,h.Owner,h.SourceLand,values,out var facts,out _)
                ||t.WalletCurrencies==null||!PalacePaidCurrencies.ContentEquals(facts.Currencies,t.WalletCurrencies))
#endif
            {t.Faulted=true;return;}
            t.Values=facts;
            if(t.Returned)return; // New return facts are independent; original source proof is immutable.
            if(t.Verification)
            {
                var original=h.Envelope?.Current.CaptureProof;
                if(original==null||original.Payer!=facts.PayerKey||original.SourceIslandFingerprint!=facts.SourceFingerprint
                    ||!PalacePaidCurrencies.ContentEquals(original.Currencies,facts.Currencies))Hold(h,"verification-original-capture-mismatch");
                return; // No second PureCapture, original CaptureId/proof are preserved.
            }
            if(h.PayerKey!=null&&h.PayerKey!=facts.PayerKey){Hold(h,"capture-payer-conflict");return;}
            h.PayerKey=facts.PayerKey;
            var proof=new PalacePaidCaptureProof(t.Sequence,h.Owner,h.SourceLand,facts.PayerKey,t.WalletCurrencies,facts.SourceFingerprint,true,true,true);
#if PALACE_PAID_CAPTURE_DIAGNOSTICS
            applyAttempted=true;
            applyOk=Apply(h,PalacePaidRequest.Capture(Expectation(h),h.TripId,proof),out applyReason);
#else
            Apply(h,PalacePaidRequest.Capture(Expectation(h),h.TripId,proof),out _);
#endif
        }
        catch(Exception e){if(token?.Capture?.Handle!=null)Hold(token.Capture.Handle,"prefs:"+e.GetType().Name);}
        finally{
#if PALACE_PAID_CAPTURE_DIAGNOSTICS
            if(token!=null&&_prefs==token&&token.Previous==null&&token.Capture!=null)
                DiagPrefsResult(token.Capture,runOriginal,error!=null,sourceRead,sourceOk,sourceReason,applyAttempted,applyOk,applyReason);
#endif
            if(token!=null&&_prefs==token)_prefs=token.Previous;}
    }

    internal static bool TryResolvePhysicalSource(PalacePaidHandle handle,int actualRawResult,
        out PalaceSourceClockReceipt receipt,out string reason)
    {
        receipt=null!;reason="";
#if PALACE_PAID_CAPTURE_DIAGNOSTICS
        bool diagOk=false;
#endif
        try
        {
            if(!ExactSaveResult(actualRawResult)){reason="native-result-not-exact72";return false;}
            if(!PalaceLayoutRecipe.IsKnown(handle.DestinationLayout!)){reason="legacy-destination-layout-unknown";return false;}
            if(handle!=_handle||handle.Fault.Length!=0||handle.Envelope?.Current.CaptureProof==null
                ||(handle.Stage!=PalacePaidStage.SourceCaptured&&handle.Stage!=PalacePaidStage.Resumable)) {reason="no-durable-capture";return false;}
            if(handle.RequiresNativeVerification&&(handle.VerificationCapture?.Values==null||handle.VerificationCapture.Faulted))
            {reason="requires-real-source-verification-save";return false;}
            if(!ReadPhysical(handle,handle.SourceLand,out var native,out reason)){Hold(handle,"physical:"+reason);return false;}
            if(handle.RequiresNativeVerification&&!handle.VerificationCapture!.Values!.ContentEquals(native.Island))
            {reason="verification-physical-mismatch";Hold(handle,reason);return false;}
            var capture=handle.Envelope.Current.CaptureProof;
            var file=new PalacePaidFileProof(native.RawSaveSha256,handle.Owner,handle.SourceLand,native.Island.PayerKey,
                native.Island.Currencies.ToArray(),native.Island.SourceFingerprint,native.NativeVersion,actualRawResult);
            if(!file.Matches(capture)){reason="physical-capture-mismatch";Hold(handle,reason);return false;}
            if(!Apply(handle,PalacePaidRequest.Resolve(Expectation(handle),handle.TripId,file),out reason))return false;
            if(!TryClock(handle,native,out receipt,out reason)){Hold(handle,"clock-receipt:"+reason);return false;}
            handle.SetClock(receipt);handle.RequiresNativeVerification=false;
            // The legitimate paid source save has changed the global checkpoint. Refresh only here,
            // using the actual durable file proof; never from CanPay or a stale prepayment SHA.
            if(!PalaceDestinationAdmission.TryRefreshAfterPaidSourceSave(handle.Game!,handle.Campaign!,handle.DestinationLayout!,file,out _,out reason))
            {Hold(handle,"destination-refresh:"+reason);return false;}
#if PALACE_PAID_CAPTURE_DIAGNOSTICS
            diagOk=true;
#endif
            return true;
        }catch(Exception e){reason="resolve:"+e.GetType().Name;Hold(handle,reason);return false;}
#if PALACE_PAID_CAPTURE_DIAGNOSTICS
        finally{DiagResolve(handle,handle!=null,diagOk,reason);}
#endif
    }
    internal static bool TryValidateSourceCapturedBeforeVerification(PalacePaidHandle handle,out string reason)
    {
        reason="";
        try
        {
            var capture=handle.Envelope?.Current.CaptureProof;
            if(handle!=_handle||handle.Fault.Length!=0||handle.Stage!=PalacePaidStage.SourceCaptured||handle.Payer==null||capture==null
                ||!TryReadLiveCurrencies(handle,out var before,out reason)||!SameEightCurrencies(before,capture.Currencies)
                ||!ReadPhysical(handle,handle.SourceLand,out var current,out reason)||!MatchesOriginalSourceProof(handle,current)
                ||!TryReadLiveCurrencies(handle,out var after,out reason)
                ||!SourceVerificationBalancesMatch(before,current.Island.Currencies,after,capture.Currencies)
                ||!SameLive(handle,handle.Payer,out reason))
            {if(reason.Length==0)reason="source-verification-proof-mismatch";Hold(handle,reason);return false;}
            return true; // No Save, Resolve, journal write, capture replacement or callback result is manufactured.
        }catch(Exception e){reason="source-verification:"+e.GetType().Name;Hold(handle,reason);return false;}
    }
    private static bool SourceVerificationBalancesMatch(IReadOnlyList<int>? before,IReadOnlyList<int>? physical,
        IReadOnlyList<int>? after,IReadOnlyList<int>? capture)
        => SameEightCurrencies(before,capture) && SameEightCurrencies(before,physical)
            && SameEightCurrencies(after,before) && SameEightCurrencies(after,physical) && SameEightCurrencies(after,capture);
    internal static bool TryClaimDeparture(PalacePaidHandle handle,out string reason)
    {
        reason="";
        if(handle==null||!PalaceLayoutRecipe.IsKnown(handle.DestinationLayout!))
        {reason="legacy-destination-layout-unknown";return false;}
        if(handle!=_handle||handle.Fault.Length!=0||handle.Stage!=PalacePaidStage.Resumable||handle.SourceClockReceipt==null)
        {reason="claim-not-resumable";return false;}
        try
        {
            var capture=handle.Envelope?.Current.CaptureProof;var file=handle.Envelope?.Current.FileProof;
            if(capture==null||file==null||!TryReadLiveCurrencies(handle,out var before,out reason)
                ||!SameEightCurrencies(before,capture.Currencies)||!SameEightCurrencies(before,file.Currencies))
            {if(reason.Length==0)reason="claim-live-before-mismatch";Hold(handle,reason);return false;}
            if(!ReadPhysical(handle,handle.SourceLand,out var current,out reason)||!MatchesOriginalSourceProof(handle,current))
            {if(reason.Length==0)reason="claim-physical-mismatch";Hold(handle,reason);return false;}
            if(!TryReadLiveCurrencies(handle,out var after,out reason)||!SameEightCurrencies(after,before)
                ||!SameEightCurrencies(after,capture.Currencies)||!SameEightCurrencies(after,file.Currencies))
            {if(reason.Length==0)reason="claim-live-after-mismatch";Hold(handle,reason);return false;}
            if(!PalaceDestinationAdmission.TryDescribeDestination(handle.Campaign!,handle.DestinationLayout!,out var destination,out reason)
                ||!PalaceDestinationAdmission.TryFreezeForCharge(handle.Game!,handle.Campaign!,destination,out var frozen,out reason)
                ||!ReferenceEquals(frozen.Recipe,handle.DestinationLayout)) return false;
            return Apply(handle,PalacePaidRequest.ClaimDeparture(Expectation(handle),handle.TripId),out reason);
        }catch(Exception e){reason="claim-physical:"+e.GetType().Name;Hold(handle,reason);return false;}
    }
    private static bool TryReadLiveCurrencies(PalacePaidHandle h,out int[] values,out string reason)
    {
        values=Array.Empty<int>();reason="";
        if(h.Payer==null||h.Wallet==null||!SameLive(h,h.Payer,out reason))return false;
        var wallet=h.Wallet;
        values=new[]{wallet.GetCurrency(CurrencyType.Coins),wallet.GetCurrency(CurrencyType.Gems),
            wallet.GetCurrency(CurrencyType.Crown),wallet.GetCurrency(CurrencyType.Skulls),wallet.GetCurrency(CurrencyType.Shades),
            wallet.GetCurrency(CurrencyType.Merchandise),wallet.GetCurrency(CurrencyType.Candle),wallet.GetCurrency(CurrencyType.Egg)};
        if(!SameEightCurrencies(values,values)||h.Wallet.Pointer!=wallet.Pointer||!SameLive(h,h.Payer,out reason))
        {reason="live-eight-currency-unknown-or-reference-drift";return false;}
        return true;
    }
    private static bool SameEightCurrencies(IReadOnlyList<int>? a,IReadOnlyList<int>? b)
    {
        if(a==null||b==null||a.Count!=8||b.Count!=8)return false;
        for(int i=0;i<8;i++)if(a[i]<0||b[i]<0||a[i]!=b[i])return false;
        return true;
    }
    private static bool MatchesOriginalSourceProof(PalacePaidHandle h,PalacePaidNativeSourceValues actual)
    {
        var capture=h.Envelope?.Current.CaptureProof;var file=h.Envelope?.Current.FileProof;
        return capture!=null&&capture.Owner.ContentEquals(h.Owner)&&capture.SourceLand==h.SourceLand
            &&capture.Payer==actual.Island.PayerKey&&capture.SourceIslandFingerprint==actual.Island.SourceFingerprint
            &&SameEightCurrencies(capture.Currencies,actual.Island.Currencies)
            &&(file==null||(file.NativeRawResult==72&&file.Matches(capture)&&file.RawSaveSha256==actual.RawSaveSha256));
    }
    private static bool TryClock(PalacePaidHandle handle,PalacePaidNativeSourceValues native,
        out PalaceSourceClockReceipt receipt,out string reason)
    {
        var island=native.Island;
        var facts=new PalaceClockReceiptFacts(handle.TripId.ToString("D"),handle.Owner.CampaignGuid.ToString("D"),
            handle.Owner.CampaignSlot,handle.Owner.Biome,handle.Owner.Reign,handle.Owner.Start,handle.SourceLand,
            native.RawSaveSha256,native.CampaignTotalGameDays,native.CurrentReignDaysSurvived,island.IslandPlayTimeDays,
            island.IslandLastPlayedTimeDays,island.IslandTimePlayed,island.IslandLastPlayedReign,island.DirectorSummarySha256,island.ObjectsSummarySha256);
        return PalaceSourceClockReceipt.TryCreateFromVerifiedFacts(facts,out receipt,out reason);
    }
    private static bool ReadPhysical(PalacePaidHandle h,int currentLand,out PalacePaidNativeSourceValues native,out string reason)
    {
        native=null!;reason="";
        if(h.Payer==null||!SameLive(h,h.Payer,out reason)||IslandSaveData.isSavingGame||h.Game!.currentLand!=currentLand)return false;
        if(!PathSnapshot(h,out var path,out reason))return false;
        var info=new FileInfo(path);
        if(!info.Exists||info.LinkTarget!=null||(info.Attributes&FileAttributes.ReparsePoint)!=0||info.Length<=0
            ||info.Length>PalacePaidSourceProof.MaxNativeCompressedBytes){reason="physical-file-shape";return false;}
        byte[] a=ReadBounded(path);
        if(!PalacePaidSourceProof.TryReadNativeSnapshot(a,h.Owner,h.SourceLand,currentLand,out native,out reason))return false;
        byte[] b=ReadBounded(path);
        if(!a.AsSpan().SequenceEqual(b)||!PathSnapshot(h,out var again,out reason)||path!=again||!SameLive(h,h.Payer,out reason))
        {reason="physical-source-drift";return false;}
        return true;
    }
    private static byte[] ReadBounded(string path)
    {
        using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(stream.Length<1||stream.Length>PalacePaidSourceProof.MaxNativeCompressedBytes)throw new IOException("source length");
        byte[] bytes=new byte[checked((int)stream.Length)];int total=0;
        while(total<bytes.Length){int n=stream.Read(bytes,total,bytes.Length-total);if(n<=0)throw new EndOfStreamException();total+=n;}
        if(stream.ReadByte()!=-1)throw new IOException("source grew");return bytes;
    }
    private static bool PathSnapshot(PalacePaidHandle h,out string path,out string reason)
    {
        path="";reason="";
        var raw=Filer.agent as Il2CppObjectBase;var agent=raw?.TryCast<Filer.DotNetAgent>();
        if(raw==null||agent==null||h.Agent==null||raw.Pointer!=h.Agent.Pointer||agent.folder!=h.Folder
            ||Filer.folder!=h.Folder||GlobalSaveData.filename!=h.Filename||Path.GetFileName(h.Filename)!=h.Filename
            ||h.Filename=="."||h.Filename==".."||!PhysicalDirectory(h.Folder)) {reason="provider-path-drift";return false;}
        path=Path.Combine(h.Folder,h.Filename);return true;
    }

    internal static bool ObserveExactArrival(PalacePaidHandle handle,Game game,string tripId,int targetLand,out string reason)
    {
        reason="";
        try
        {
#if PALACE_PAID_CAPTURE_DIAGNOSTICS
            if(TryObserveArrivalFirstFailure(handle,game,tripId,targetLand))
#else
            // The historical Game gate is a pure CLR-reference existence check: a destroyed
            // Unity native object must not impersonate a missing historical identity.
            // <PALACE-PAID-ARRIVAL-DEFAULT-GUARD-BEGIN>
            if(handle!=_handle||handle.Fault.Length!=0||tripId!=handle.TripId.ToString("D")||game==null
                ||game.state!=Game.State.Playing||game.currentLand!=targetLand||ReferenceEquals(handle.Game,null)||game.Pointer==handle.Game!.Pointer)
            // <PALACE-PAID-ARRIVAL-DEFAULT-GUARD-END>
#endif
            {reason="arrival-owner-or-scene";Hold(handle,reason);return false;}
            var m=Managers.Inst;
            if(m?.game==null||m.kingdom==null||m.game.Pointer!=game.Pointer||!TryOwner(game,m.kingdom,out var owner,out reason)
                ||!owner.ContentEquals(handle.Owner)||handle.Global==null||handle.Campaign==null||!SameProviderInMemory(handle)
                ||!PalaceIslandTravel.TryDescribeCompletedPaidArrival(handle,game,tripId,targetLand,
                    out var oldGame,out var oldGlobal,out var oldCampaign,out var newGlobal,out var newCampaign,out reason)
                ||!ExactArrivalRoots(handle.Game!.Pointer,handle.Global.Pointer,handle.Campaign.Pointer,oldGame,oldGlobal,oldCampaign,
                    game.Pointer,newGlobal?.Pointer??IntPtr.Zero,newCampaign?.Pointer??IntPtr.Zero,
                    GlobalSaveData.loaded.Pointer,CampaignSaveData.current.Pointer)
                ||newGlobal==null||newCampaign==null)
            {if(reason.Length==0)reason="arrival-verified-epoch-mismatch";Hold(handle,reason);return false;}
            if(PalaceTripContext.Read(newGlobal,out var trip,out reason)!=PalaceTripContext.ReadStatus.Ok
                ||!TripMatches(handle,trip)||newCampaign.CurrentLand!=targetLand)
            {if(reason.Length==0)reason="arrival-trip-mismatch";Hold(handle,reason);return false;}
            PalacePaidAction action;
            if(targetLand==12&&trip.Phase==PalaceTripContext.PhasePalaceSelected
                &&(handle.Stage==PalacePaidStage.DepartureClaimed||handle.Stage==PalacePaidStage.Entered))action=PalacePaidAction.MarkEntered;
            else if(targetLand==handle.SourceLand&&trip.Phase==PalaceTripContext.PhaseReturnSelected&&handle.Stage==PalacePaidStage.Entered)
                action=PalacePaidAction.MarkReturned;
            else {reason="arrival-stage-phase";Hold(handle,reason);return false;}
            // Read every potentially throwing new-scene native fact into locals before the durable CAS.
            Player payer=m.kingdom.playerOne;
            if(!ExactPayer(m.kingdom,payer)||m.world?.gameLayer?.gameObject==null||m.level?.gameObject==null
                ||payer.gameObject.scene.handle!=m.world.gameLayer.gameObject.scene.handle
                ||m.kingdom.gameObject.scene.handle!=payer.gameObject.scene.handle||m.level.gameObject.scene.handle!=payer.gameObject.scene.handle)
            {reason="arrival-P1-scene";Hold(handle,reason);return false;}
            var newKingdom=m.kingdom;var newWallet=payer.wallet;int newScene=payer.gameObject.scene.handle;
            if(!Apply(handle,action==PalacePaidAction.MarkEntered?PalacePaidRequest.MarkEntered(Expectation(handle),handle.TripId)
                :PalacePaidRequest.MarkReturned(Expectation(handle),handle.TripId),out reason))return false;
            // CAS succeeded: publish only the verified epoch and already-read references, preserving payer/proofs/clock.
            handle.Game=game;handle.Kingdom=newKingdom;handle.Global=newGlobal;handle.Campaign=newCampaign;
            handle.Payer=payer;handle.Wallet=newWallet;handle.Payable=null;handle.Scene=newScene;
            return true;
        }catch(Exception e){reason="arrival:"+e.GetType().Name;Hold(handle,reason);return false;}
    }
    private static bool ExactArrivalRoots(IntPtr boundGame,IntPtr boundGlobal,IntPtr boundCampaign,
        IntPtr oldGame,IntPtr oldGlobal,IntPtr oldCampaign,IntPtr newGame,IntPtr newGlobal,IntPtr newCampaign,
        IntPtr currentGlobal,IntPtr currentCampaign)
        => boundGame!=IntPtr.Zero && boundGlobal!=IntPtr.Zero && boundCampaign!=IntPtr.Zero
            && boundGame==oldGame && boundGlobal==oldGlobal && boundCampaign==oldCampaign
            && newGame!=IntPtr.Zero && newGame!=boundGame && newGlobal!=IntPtr.Zero && newCampaign!=IntPtr.Zero
            && newGlobal==currentGlobal && newCampaign==currentCampaign;

    internal static bool ObserveExactColdArrival(PalacePaidHandle handle,Game game,out string reason)
    {
        reason="";
        try
        {
            if(handle!=_handle||handle.Fault.Length!=0||(handle.Stage!=PalacePaidStage.DepartureClaimed&&handle.Stage!=PalacePaidStage.Entered)
                ||game==null||game.state!=Game.State.Playing||game.currentLand!=12||handle.SourceClockReceipt==null
                ||!PalaceClockBootstrap.TryDescribeColdArrival(game,handle.TripId.ToString("D"),out var evidence,out reason))return false;
            var m=Managers.Inst;
            if(m?.game==null||m.kingdom==null||m.game.Pointer!=game.Pointer||evidence.Game.Pointer!=game.Pointer
                ||evidence.Global.Pointer!=GlobalSaveData.loaded.Pointer||evidence.Campaign.Pointer!=CampaignSaveData.current.Pointer
                ||evidence.Island==null||evidence.InitialState==Game.State.Playing||evidence.PopSequence<=0
                ||!evidence.Owner.ContentEquals(handle.Owner)||evidence.TripId!=handle.TripId.ToString("D")||evidence.SourceLand!=handle.SourceLand
                ||handle.Global==null||evidence.Global.Pointer!=handle.Global.Pointer||handle.Campaign==null
                ||evidence.Campaign.Pointer!=handle.Campaign.Pointer||!SameProviderInMemory(handle)
                ||evidence.SourceClockReceipt.WriteCanonicalStrict()!=handle.SourceClockReceipt.WriteCanonicalStrict()
                ||!TryOwner(game,m.kingdom,out var owner,out reason)||!owner.ContentEquals(handle.Owner)
                ||PalaceTripContext.Read(GlobalSaveData.loaded,out var trip,out reason)!=PalaceTripContext.ReadStatus.Ok
                ||!TripMatches(handle,trip)||trip.Phase!=PalaceTripContext.PhasePalaceSelected
                ||handle.Envelope?.Current.FileProof==null||handle.SourceClockReceipt.SourceCheckpointSha!=handle.Envelope.Current.FileProof.RawSaveSha256)
            {reason="cold-arrival-public-facts";return false;}
            Player payer=m.kingdom.playerOne;
            if(!ExactPayer(m.kingdom,payer)){reason="cold-arrival-P1";return false;}
            if(!Apply(handle,PalacePaidRequest.MarkEntered(Expectation(handle),handle.TripId),out reason))return false;
            BindLive(handle,game,m.kingdom,payer,null);return true;
        }catch(Exception e){reason="cold-arrival:"+e.GetType().Name;Hold(handle,reason);return false;}
    }

    internal static bool TryReadExistingForBootstrap(PalacePaidScope owner,string tripId,int sourceLand,
        out PalacePaidHandle handle,out string reason)
    {
        handle=null!; reason="";
        // This guard intentionally precedes directory access, store.Read, parsing and construction.
        if(_handle!=null||HasTransientResponsibility()) {reason="existing-session-responsibility";return false;}
        try
        {
            if(!Guid.TryParseExact(tripId,"D",out var id)||id==Guid.Empty||sourceLand<0||sourceLand>11
                ||!EnsureStore(out var store,out reason)) return false;
            var read=store.Read(owner);
            if(!TryBuildColdHandle(owner,id,sourceLand,store,read,out var restored,out reason)) return false;
            if(_handle!=null||HasTransientResponsibility()) {reason="existing-session-responsibility";return false;}
            _handle=restored;handle=restored;return true;
        }catch(Exception e){reason="bootstrap:"+e.GetType().Name;return false;}
    }
    private static bool TryBuildColdHandle(PalacePaidScope owner,Guid id,int sourceLand,PalacePaidJournalStore store,
        PalacePaidStoreReadResult read,out PalacePaidHandle handle,out string reason)
    {
        handle=null!;reason="";
        if(_handle!=null||HasTransientResponsibility()) {reason="existing-session-responsibility";return false;}
        try
        {
            if(read.Status!=PalacePaidStoreReadStatus.Valid||read.Envelope==null||read.RawSha256==null
                ||read.RawSha256!=read.Envelope.SourceBytesSha256) {reason="bootstrap-journal:"+read.Status;return false;}
            var record=read.Envelope.Current;
            if(record.TripId!=id||!record.Owner.ContentEquals(owner)||record.SourceLand!=sourceLand||record.FileProof?.NativeRawResult is int r&&r!=72)
            {reason="bootstrap-record";return false;}
            if(record.Stage!=PalacePaidStage.SourceCaptured&&record.Stage!=PalacePaidStage.Resumable
                &&record.Stage!=PalacePaidStage.DepartureClaimed&&record.Stage!=PalacePaidStage.Entered&&record.Stage!=PalacePaidStage.Returned)
            {reason="bootstrap-stage";return false;}
            var global=GlobalSaveData.loaded;var campaign=CampaignSaveData.current;
            if(!OwnerWithoutScene(owner,global,campaign,out reason))return false;
            handle=new PalacePaidHandle(owner,id,sourceLand,record.Anchor,store,read.Envelope,read.RawSha256);
            handle.Global=global;handle.Campaign=campaign;BindProvider(handle);
            if(campaign.CurrentLand==12)
            {
                if(record.Stage!=PalacePaidStage.DepartureClaimed&&record.Stage!=PalacePaidStage.Entered){reason="cold-palace-stage";return false;}
                if(PalaceTripContext.Read(global,out var trip,out reason)!=PalaceTripContext.ReadStatus.Ok||!TryVerifyTripDestination(handle,campaign,trip,out reason)||!TripMatches(handle,trip)
                    ||trip.Phase!=PalaceTripContext.PhasePalaceSelected
                    ||!PalaceSourceClockReceipt.TryParseStrict(trip.SourceClockReceipt,out var receipt,out reason))return false;
                if(!PathSnapshot(handle,out var path,out reason))return false;
                byte[] a=ReadBounded(path);
                if(!PalacePaidSourceProof.TryReadNativeSnapshot(a,owner,sourceLand,12,out var native,out reason))return false;
                byte[] b=ReadBounded(path);
                // Current palace progress may change whole gzip and rights maps; original proof remains authoritative.
                if(!a.AsSpan().SequenceEqual(b)||!PathSnapshot(handle,out var path2,out reason)||path2!=path
                    ||!OwnerWithoutScene(owner,global,campaign,out reason)||record.FileProof==null
                    ||receipt.SourceCheckpointSha!=record.FileProof.RawSaveSha256
                    ||receipt.SourceObjectsSha!=native.Island.ObjectsSummarySha256
                    ||receipt.SourceDirectorSha!=native.Island.DirectorSummarySha256
                    ||!receipt.SameRawAnchor(native.CampaignTotalGameDays,native.CurrentReignDaysSurvived))
                {reason="cold-palace-original-anchor";return false;}
                handle.SetClock(receipt);
            }
            else if(campaign.CurrentLand!=sourceLand){reason="cold-source-land";return false;}
            else if(record.Stage==PalacePaidStage.SourceCaptured||record.Stage==PalacePaidStage.Resumable)
            {
                if(!PathSnapshot(handle,out var path,out reason))return false;
                byte[] a=ReadBounded(path);
                if(!PalacePaidSourceProof.TryReadNativeSnapshot(a,owner,sourceLand,sourceLand,out var source,out reason))return false;
                byte[] b=ReadBounded(path);
                var proof=record.CaptureProof;
                if(!a.AsSpan().SequenceEqual(b)||!PathSnapshot(handle,out var path2,out reason)||path2!=path
                    ||!OwnerWithoutScene(owner,global,campaign,out reason)||proof==null||!MatchesOriginalSourceProof(handle,source))
                {reason="cold-source-capture-mismatch";return false;}
                if(record.Stage==PalacePaidStage.Resumable)
                {
                    if(record.FileProof==null||record.FileProof.RawSaveSha256!=source.RawSaveSha256
                        ||!TryClock(handle,source,out var sourceClock,out reason))return false;
                    handle.SetClock(sourceClock);
                }
                handle.RequiresNativeVerification=record.Stage==PalacePaidStage.SourceCaptured;
                // SourceCaptured remains requires-native-save: no callback72 is fabricated here.
            }
            if(record.Stage==PalacePaidStage.Returned)
            {
                if(PalaceTripContext.Read(global,out var trip,out reason)!=PalaceTripContext.ReadStatus.Ok
                    ||!TryVerifyTripDestination(handle,campaign,trip,out reason)||!TripMatches(handle,trip)||trip.Phase!=PalaceTripContext.PhaseReturnSelected
                    ||!PalaceSourceClockReceipt.TryParseStrict(trip.SourceClockReceipt,out var receipt,out reason)
                    ||record.FileProof==null||receipt.SourceCheckpointSha!=record.FileProof.RawSaveSha256)
                {reason="cold-returned-trip";return false;}
                handle.SetClock(receipt);
            }
            // No P1/Wallet access before those objects are created; Playing binds once below.
            if(_handle!=null||HasTransientResponsibility()){reason="existing-session-responsibility";return false;}
            return true;
        }catch(Exception e){reason="bootstrap:"+e.GetType().Name;handle=null!;return false;}
    }
    private static bool HasTransientResponsibility() => _input!=null||_capture!=null||_prefs!=null;
    private static bool CanRestoreSourceAtPlaying(bool hasHandle,bool hasTransient,bool validRead,bool sameOwner,
        int actualLand,int recordSource,PalacePaidStage stage)
        => !hasHandle && !hasTransient && validRead && sameOwner && actualLand>=0 && actualLand<=11 && actualLand==recordSource
            && (stage==PalacePaidStage.SourceCaptured||stage==PalacePaidStage.Resumable||stage==PalacePaidStage.Returned);
    internal static bool TryGetPreparedHandle(Game game,Kingdom kingdom,Player payer,out PalacePaidHandle handle,out string reason)
    {
        handle=null!;reason="";
        try
        {
            var h=_handle;
            if(h==null||h.Fault.Length!=0||!TryOwner(game,kingdom,out var owner,out reason)||!h.Owner.ContentEquals(owner)
                ||!ExactPayer(kingdom,payer)||game.state!=Game.State.Playing||IslandSaveData.isSavingGame){reason="prepared-live-context";return false;}
            if(h.Game==null) BindLive(h,game,kingdom,payer,null);
            if(!SameLive(h,payer,out reason))return false;
            if(game.currentLand==h.SourceLand && h.Stage!=PalacePaidStage.SourceCaptured&&h.Stage!=PalacePaidStage.Resumable
                &&h.Stage!=PalacePaidStage.Returned&&h.Stage!=PalacePaidStage.VisitCommitted){reason="source-stage-no-replay";return false;}
            if(game.currentLand==12 && h.Stage!=PalacePaidStage.DepartureClaimed&&h.Stage!=PalacePaidStage.Entered){reason="palace-stage";return false;}
            handle=h;return true;
        }catch(Exception e){reason="prepared:"+e.GetType().Name;return false;}
    }
    internal static bool TryConfirmReturnedSave(PalacePaidHandle handle,int actualRawResult,out string reason)
    {
        reason="";
        try
        {
            var capture=handle.ReturnCapture;
            if(!ExactSaveResult(actualRawResult)||handle!=_handle||handle.Stage!=PalacePaidStage.Returned||handle.Fault.Length!=0
                ||capture?.Values==null||capture.Faulted||!capture.Returned){reason="return-capture-not-complete";return false;}
            if(!ReadPhysical(handle,handle.SourceLand,out var native,out reason)){Hold(handle,"physical:"+reason);return false;}
            if(!capture.Values.ContentEquals(native.Island)){reason="returned-file-capture-mismatch";Hold(handle,reason);return false;}
            return Apply(handle,PalacePaidRequest.CommitVisit(Expectation(handle),handle.TripId),out reason);
        }catch(Exception e){reason="commit:"+e.GetType().Name;Hold(handle,reason);return false;}
    }
    internal static bool TryReadCommittedForNextVisit(PalacePaidHandle handle,out PalacePaidEnvelope envelope,out string reason)
        =>TryReadCommittedForNextVisit(handle,handle.TripId.ToString("D"),out envelope,out reason);
    internal static bool TryReadCommittedForNextVisit(PalacePaidHandle currentHandle,string completedTripId,
        out PalacePaidEnvelope envelope,out string reason)
    {
        envelope=null!;reason="";
        try
        {
            if(currentHandle!=_handle||currentHandle.Fault.Length!=0||!Guid.TryParseExact(completedTripId,"D",out var id))
            {reason="committed-identity";return false;}
            var read=currentHandle.Store.Read(currentHandle.Owner);
            if(read.Status!=PalacePaidStoreReadStatus.Valid||read.Envelope==null||read.RawSha256!=read.Envelope.SourceBytesSha256
                ||read.Envelope.Current.TripId!=currentHandle.TripId||!read.Envelope.Current.Owner.ContentEquals(currentHandle.Owner))
            {reason="committed-readback";return false;}
            int count=0;
            foreach(var record in new[]{read.Envelope.Current}.Concat(read.Envelope.History))
                if(record.TripId==id&&record.Owner.ContentEquals(currentHandle.Owner)&&record.Stage==PalacePaidStage.VisitCommitted)count++;
            if(count!=1){reason="committed-exact-record";return false;}
            envelope=read.Envelope;return true;
        }catch(Exception e){reason="committed:"+e.GetType().Name;return false;}
    }

    private static bool TryOwner(Game game,Kingdom kingdom,out PalacePaidScope owner,out string reason)
    {
        owner=null!;reason="";
        if(game==null||kingdom==null||NetworkBigBoss.IsOnline||!NetworkBigBoss.HasWorldAuth||!ModConfig.Enabled.Value)
        {reason="offline-authority-enabled";return false;}
        var m=Managers.Inst;var global=GlobalSaveData.loaded;var campaign=CampaignSaveData.current;
        if(m?.game==null||m.kingdom==null||m.game.Pointer!=game.Pointer||m.kingdom.Pointer!=kingdom.Pointer
            ||global==null||global.InChallenge||campaign==null||global.GetCurrentCampaign()==null
            ||global.GetCurrentCampaign().Pointer!=campaign.Pointer||BiomeHolder.Inst==null
            ||BiomeHolder.Inst.BiomeIndex!=5||campaign.biomeIndex!=5||game.currentLand!=campaign.CurrentLand)
        {reason="owner-context";return false;}
        if(!PalaceTripContext.TryBinding(global,campaign,out var guid,out int slot,out reason)||!Guid.TryParseExact(guid,"D",out var id))return false;
        owner=new PalacePaidScope(PalaceRuntimeProfile.Id,id,slot,campaign.biomeIndex,campaign.reign,campaign.realStartDateTime,1);
        return true;
    }
    private static bool OwnerWithoutScene(PalacePaidScope owner,GlobalSaveData global,CampaignSaveData campaign,out string reason)
    {
        reason="";
        if(global==null||campaign==null||GlobalSaveData.loaded==null||CampaignSaveData.current==null||global.InChallenge
            ||global.Pointer!=GlobalSaveData.loaded.Pointer||campaign.Pointer!=CampaignSaveData.current.Pointer
            ||global.GetCurrentCampaign()==null||global.GetCurrentCampaign().Pointer!=campaign.Pointer
            ||!PalaceTripContext.TryBinding(global,campaign,out var guid,out int slot,out reason))return false;
        if(owner.Profile!=PalaceRuntimeProfile.Id||guid!=owner.CampaignGuid.ToString("D")||slot!=owner.CampaignSlot
            ||campaign.biomeIndex!=owner.Biome||campaign.reign!=owner.Reign||campaign.realStartDateTime!=owner.Start)
        {reason="semantic-owner-drift";return false;}return true;
    }
    private static bool ExactPayer(Kingdom kingdom,Player payer)=>payer!=null&&payer.Pointer!=IntPtr.Zero&&kingdom.playerOne!=null
        &&kingdom.playerOne.Pointer==payer.Pointer&&payer.playerId==0&&payer.hasLocalAuthority&&payer.wallet!=null
        &&payer.gameObject!=null&&payer.gameObject.activeInHierarchy;
    private static bool ExactEntry(Game game,Player payer,PayableComponent payable)
        =>payable!=null&&payable.Price==10&&payable.Currency==CurrencyType.Coins&&payable.gameObject!=null
            &&payable.gameObject.activeInHierarchy&&payer.gameObject.scene.handle==payable.gameObject.scene.handle
            &&PalaceVisibleEntry.TryGetCurrentEntry(out _,out var current)&&current.Pointer==payable.Pointer;
    private static void BindProvider(PalacePaidHandle h)
    {
        var raw=Filer.agent as Il2CppObjectBase;var dot=raw?.TryCast<Filer.DotNetAgent>();
        if(raw==null||dot==null||string.IsNullOrEmpty(dot.folder)||Filer.folder!=dot.folder)throw new IOException("provider");
        h.Agent=raw;h.Folder=Path.GetFullPath(dot.folder);h.Filename=GlobalSaveData.filename;
        if(h.Folder!=dot.folder||string.IsNullOrEmpty(h.Filename)||Path.GetFileName(h.Filename)!=h.Filename)throw new IOException("provider path");
    }
    private static void BindLive(PalacePaidHandle h,Game game,Kingdom kingdom,Player payer,PayableComponent? payable)
    {
        if(h.Global!=null&&(GlobalSaveData.loaded==null||GlobalSaveData.loaded.Pointer!=h.Global.Pointer))throw new InvalidOperationException("global drift");
        if(h.Campaign!=null&&(CampaignSaveData.current==null||CampaignSaveData.current.Pointer!=h.Campaign.Pointer))throw new InvalidOperationException("campaign drift");
        h.Game=game;h.Kingdom=kingdom;h.Global=GlobalSaveData.loaded;h.Campaign=CampaignSaveData.current;
        h.Payer=payer;h.Wallet=payer.wallet;h.Payable=payable;h.Scene=payer.gameObject.scene.handle;
        if(h.Agent==null)BindProvider(h);
    }
    private static bool SameLive(PalacePaidHandle h,Player payer,out string reason)
    {
        reason="";
        if(h.Game==null||h.Kingdom==null||h.Payer==null||h.Wallet==null||h.Global==null||h.Campaign==null
            ||payer==null||payer.Pointer!=h.Payer.Pointer||payer.wallet==null||payer.wallet.Pointer!=h.Wallet.Pointer
            ||!ExactPayer(h.Kingdom,payer)||payer.gameObject.scene.handle!=h.Scene
            ||!TryOwner(h.Game,h.Kingdom,out var owner,out reason)||!owner.ContentEquals(h.Owner)
            ||GlobalSaveData.loaded.Pointer!=h.Global.Pointer||CampaignSaveData.current.Pointer!=h.Campaign.Pointer
            ||!SameProviderInMemory(h))
        {if(reason.Length==0)reason="live-reference-drift";return false;}return true;
    }
    private static bool SameProviderInMemory(PalacePaidHandle h)
    {
        var raw=Filer.agent as Il2CppObjectBase;var dot=raw?.TryCast<Filer.DotNetAgent>();
        return h.Agent!=null&&raw!=null&&dot!=null&&h.Agent.Pointer==raw.Pointer
            &&dot.folder==h.Folder&&Filer.folder==h.Folder&&GlobalSaveData.filename==h.Filename;
    }
    private static bool TripMatches(PalacePaidHandle h,PalaceTripRecord trip)
        =>h.MatchesDestination(trip)&&trip.TripId==h.TripId.ToString("D")&&trip.SourceLand==h.SourceLand&&trip.PalaceLand==12
          &&trip.CampaignGuid==h.Owner.CampaignGuid.ToString("D")&&trip.CampaignSlot==h.Owner.CampaignSlot
          &&trip.Biome==h.Owner.Biome&&trip.Reign==h.Owner.Reign&&trip.Start==h.Owner.Start
          &&PalaceReturnAnchor.ValidateData(trip.SourceEntryAnchor,out _)
          &&new PalacePaidAnchor(trip.SourceEntryAnchor.EntryX,trip.SourceEntryAnchor.EntryY,trip.SourceEntryAnchor.EntryZ,
              trip.SourceEntryAnchor.SourcePlayerX,trip.SourceEntryAnchor.StandX,trip.SourceEntryAnchor.Side).ContentEquals(h.SavedAnchor);
    private static bool TryVerifyTripDestination(PalacePaidHandle handle,CampaignSaveData campaign,PalaceTripRecord trip,out string reason)
    {
        reason="trip-destination-mismatch";
        if(trip.TripId!=handle.TripId.ToString("D")||trip.SourceLand!=handle.SourceLand
            ||trip.CampaignGuid!=handle.Owner.CampaignGuid.ToString("D")||trip.CampaignSlot!=handle.Owner.CampaignSlot
            ||trip.Biome!=handle.Owner.Biome||trip.Reign!=handle.Owner.Reign||trip.Start!=handle.Owner.Start
            ||!PalaceDestinationAdmission.TryResolveTripLayout(campaign,trip,out var resolved,out reason)) return false;
        if(trip.Version==4) return ReferenceEquals(handle.DestinationLayout,resolved)&&PalaceLayoutRecipe.IsKnown(resolved);
        if(trip.Version!=3||handle.DestinationLayout!=null||!ReferenceEquals(resolved,PalaceLayoutRecipe.Known44)) return false;
        handle.LegacyLayoutVerified=true;
        return true;
    }
    private static PalacePaidExpectation Expectation(PalacePaidHandle h)=>new(h.Owner,h.PayerKey);
    private static bool Apply(PalacePaidHandle h,PalacePaidRequest request,out string reason)
    {
        reason="";
        if(h.Fault.Length!=0){reason=h.Fault;return false;}
        var expectedLayout=request.Action==PalacePaidAction.Begin?request.DestinationLayout:h.DestinationLayout;
        var result=h.Store.TryApply(h.RawSha,request);
        if((result.Status!=PalacePaidStoreApplyStatus.Changed&&result.Status!=PalacePaidStoreApplyStatus.NoChange)
            ||result.Envelope==null||result.RawSha256==null||result.RawSha256!=result.Envelope.SourceBytesSha256
            ||result.Envelope.Current.TripId!=h.TripId||!result.Envelope.Current.Owner.ContentEquals(h.Owner)
            ||!ReferenceEquals(result.Envelope.Current.DestinationLayout,expectedLayout))
        {reason="journal:"+result.Status+":"+result.Error;Hold(h,reason);return false;}
        h.Envelope=result.Envelope;h.RawSha=result.RawSha256;
        if(_preparedOwner?.ContentEquals(h.Owner)==true)
            _prepared=PalacePaidStoreReadResult.Valid(result.Envelope,result.RawSha256);
        return true;
    }
    private static bool ExactSaveResult(int raw) => raw==72;
    private static bool ExactCompletedReceipt(bool completed,bool sameCompleting,int floating)
        => completed && sameCompleting && floating==10;
    private static bool NormalNativeCall(bool prefixEligible,bool postfixSeen,bool actualRan,bool sameCall,bool exceptionSeen,bool faulted)
        => prefixEligible && postfixSeen && actualRan && sameCall && !exceptionSeen && !faulted;
    // <PALACE-PAID-HOLD-METHOD-BEGIN>
    private static void Hold(PalacePaidHandle h,string reason){
#if PALACE_PAID_CAPTURE_DIAGNOSTICS
        // <PALACE-PAID-HOLD-DIAGNOSTIC-BEGIN>
        if(h!=null&&h.Fault.Length==0){h.Fault=reason;DiagFirstFault(h,reason);}
        // <PALACE-PAID-HOLD-DIAGNOSTIC-END>
#else
        // <PALACE-PAID-HOLD-DEFAULT-BEGIN>
        if(h!=null&&h.Fault.Length==0)h.Fault=reason;
        // <PALACE-PAID-HOLD-DEFAULT-END>
#endif
    }
    // <PALACE-PAID-HOLD-METHOD-END>
#if PALACE_PAID_CAPTURE_DIAGNOSTICS
    // Null-handle events live in their own bounded instance; they are never merged into a real trip.
    private static PalacePaidCaptureDiagnostics? NullCaptureDiagnostics
    {
        get
        {
            try { return _nullCaptureDiagnostics ??= new PalacePaidCaptureDiagnostics(Guid.Empty,PalaceVisibleEntry.Log); }
            catch(Exception) { return null; }
        }
    }
    private static PalacePaidCaptureDiagnostics? DiagnosticsFor(PalacePaidHandle? handle)
    {
        try { return handle!=null ? handle.CaptureDiagnostic : NullCaptureDiagnostics; }
        catch(Exception) { return null; }
    }
    private static void DiagBegin(CaptureState token,int campaign,int land,int challenge,PalacePaidHandle? handle,
        bool passed,bool faultPresent,int failCode)
    {
        try
        {
            DiagnosticsFor(handle)?.Begin(token.Sequence,campaign,land,challenge,handle!=null,faultPresent,
                token.Previous!=null,passed,token.Valid,token.Faulted,failCode,handle==null ? -1 : (int)handle.Stage);
        }
        catch(Exception) { }
    }
    private static void DiagMarker(CaptureState token,bool accepted)
    {
        try { DiagnosticsFor(token.Handle)?.Marker(accepted); }
        catch(Exception) { }
    }
    private static void DiagWallet(CaptureState token,bool accepted)
    {
        try { DiagnosticsFor(token.Handle)?.Wallet(accepted); }
        catch(Exception) { }
    }
    private static void DiagEnd(CaptureState token,bool accepted,bool runOriginal,bool errorPresent)
    {
        try
        {
            var h=token.Handle;
            DiagnosticsFor(h)?.End(runOriginal,errorPresent,token.Closed,token.Faulted,token.Valid,token.MarkerSeen,token.WalletSeen,
                token.Island!=null,h!=null&&h.PendingCapture==token,h!=null&&h.ReturnCapture==token,
                h!=null&&h.VerificationCapture==token,accepted);
        }
        catch(Exception) { }
    }
    private static void DiagPrefsBegin(CaptureState? candidate,PalacePaidHandle? handle)
    {
        try { DiagnosticsFor(candidate?.Handle ?? handle)?.PrefsBegin(candidate!=null,candidate?.Sequence ?? 0); }
        catch(Exception) { }
    }
    private static void DiagCurrencyCompare(CaptureState capture,IReadOnlyList<int>? captured,IReadOnlyList<int>? wallet)
    {
        try { DiagnosticsFor(capture.Handle)?.CurrencyComparison(captured,wallet,capture.Sequence); }
        catch(Exception) { }
    }
    private static void DiagPrefsResult(CaptureState capture,bool runOriginal,bool errorPresent,bool sourceRead,bool sourceOk,string? sourceReason,
        bool applyAttempted,bool applyOk,string? applyReason)
    {
        try
        {
            DiagnosticsFor(capture.Handle)?.PrefsResult(runOriginal,errorPresent,capture.Closed,capture.Values!=null,capture.Faulted,
                sourceRead,sourceOk,sourceReason,applyAttempted,applyOk,applyReason);
        }
        catch(Exception) { }
    }
    // <PALACE-PAID-ARRIVAL-DIAGNOSTIC-BEGIN>
    // Mirror of the original eight-leaf guard: same order, same short-circuit, each original
    // expression evaluated exactly once. Only values already computed for a leaf are logged;
    // a failing early leaf never reads Fault/TripId/Game or later leaves for the log. The
    // historical Game gate checks only the CLR reference (ReferenceEquals): identity stays
    // queryable through the strong GC handle after its Unity object is destroyed, and the
    // leaf reports historical-game-clr-null with hgnull left unobserved.
    private static bool TryObserveArrivalFirstFailure(PalacePaidHandle handle,Game game,string tripId,int targetLand)
    {
        const int U=-1;
        if(handle!=_handle){DiagArrivalGuard(handle,"handle-mismatch",U,U,U,U,U,U,U,U,U);return true;}
        int faultLength=handle.Fault.Length;
        if(faultLength!=0){DiagArrivalGuard(handle,"prior-fault",faultLength,U,U,U,U,U,U,U,U);return true;}
        string boundTrip=handle.TripId.ToString("D");
        if(tripId!=boundTrip){DiagArrivalGuard(handle,"trip-mismatch",U,1,U,U,U,U,U,U,U);return true;}
        if(game==null){DiagArrivalGuard(handle,"live-game-null",U,U,1,U,U,U,U,U,U);return true;}
        var state=game.state;
        if(state!=Game.State.Playing){DiagArrivalGuard(handle,"live-state",U,U,U,(int)state,U,U,U,U,U);return true;}
        int currentLand=game.currentLand;
        if(currentLand!=targetLand){DiagArrivalGuard(handle,"live-land",U,U,U,U,currentLand,targetLand,U,U,U);return true;}
        if(ReferenceEquals(handle.Game,null)){DiagArrivalGuard(handle,"historical-game-clr-null",U,U,U,U,U,U,U,1,U);return true;}
        bool samePointer=game.Pointer==handle.Game!.Pointer;
        if(samePointer){DiagArrivalGuard(handle,"same-game-pointer",U,U,U,U,U,U,U,U,1);return true;}
        return false;
    }
    private static void DiagArrivalGuard(PalacePaidHandle handle,string leaf,int faultLength,int tripMismatch,
        int liveGameNull,int liveState,int currentLand,int targetLand,int historicalUnityNull,int historicalClrNull,int pointerEqual)
    {
        try
        {
            DiagnosticsFor(handle)?.ArrivalGuard(leaf,faultLength,tripMismatch,liveGameNull,liveState,currentLand,targetLand,
                historicalUnityNull,historicalClrNull,pointerEqual);
        }
        catch(Exception) { }
    }
    private static void DiagFirstFault(PalacePaidHandle handle,string reason)
    {
        try { DiagnosticsFor(handle)?.FirstFault(reason); }
        catch(Exception) { }
    }
    // <PALACE-PAID-ARRIVAL-DIAGNOSTIC-END>
    private static void DiagResolve(PalacePaidHandle? handle,bool hasHandle,bool ok,string? reason)
    {
        try { DiagnosticsFor(handle)?.Resolve(hasHandle,ok,reason); }
        catch(Exception) { }
    }
#endif
}

}

#if PALACE_PAID_CAPTURE_DIAGNOSTICS
// <PALACE-PAID-CAPTURE-DIAGNOSTICS-BEGIN>
// Bounded scalar observer. It retains no Unity/native objects and performs no game reads;
// every value comes from a call site's already-computed arguments or managed flags.
internal sealed class PalacePaidCaptureDiagnostics
{
    internal const int MaxLines=32;
    private readonly Guid _trip;
    private readonly Action<string>? _sink;
    private int _lines;

    private int _beginSeen,_beginAccepted,_beginInvalid,_beginRefused,_beginFaulted;
    private long _beginSequence;
    private int _beginArg0,_beginArg1,_beginArg2,_beginFail,_beginValid;
    private int _beginStage=-1,_firstInvalidStage=-1;
    private int _markerSeen,_markerAccepted,_markerRefused;
    private int _walletSeen,_walletAccepted,_walletRefused;
    private int _endSeen,_endAccepted,_endRefused,_endClosed,_endFault,_endValid,_endMarker,_endWallet,_endIsland;
    private int _endOriginal=-1,_endError=-1,_prefsOriginal=-1,_prefsError=-1;
    private int _pending,_returned,_verification;
    private int _prefsSeen,_prefsCandidate,_prefsClosed,_prefsValues,_prefsFault,_prefsReported;
    private int _sourceReadSeen,_sourceReadOk,_applyAttempted,_applyOk,_captured,_currencyCompareReported;
    private int _arrivalGuardReported,_firstFaultReported;
    private int _resolveSeen,_resolveOk;
    private string _resolveReason="";
    private string _firstGroup="",_firstReason="";

    internal PalacePaidCaptureDiagnostics(Guid trip,Action<string>? sink) { _trip=trip;_sink=sink; }

    internal void Begin(long sequence,int arg0,int arg1,int arg2,bool handlePresent,bool faultPresent,
        bool previousPresent,bool passed,bool valid,bool faulted,int failCode,int stage)
    {
        _beginSeen++;
        _beginSequence=sequence;_beginArg0=arg0;_beginArg1=arg1;_beginArg2=arg2;_beginValid=valid?1:0;_beginStage=stage;
        if(!passed)
        {
            if(faulted)
            {
                _beginFaulted++;Refuse("begin","exception");
                if(_beginFaulted==1)Emit("phase=begin result=fault fail="+failCode+" seq="+sequence);
            }
            else
            {
                _beginRefused++;_beginFail=failCode;Refuse("begin","fail="+failCode);
                if(_beginRefused==1)
                    Emit("phase=begin result=refused fail="+failCode+" seq="+sequence+" args="+arg0+","+arg1+","+arg2
                        +" handle="+(handlePresent?1:0)+" bfault="+(faultPresent?1:0)+" prev="+(previousPresent?1:0));
            }
            return;
        }
        _beginFail=0;
        if(!valid||faulted)
        {
            if(_beginInvalid==0)_firstInvalidStage=stage;
            _beginInvalid++;
            if(_beginInvalid==1)
                Emit("phase=begin result=invalid-stage stage="+ObservedFlag(stage)+" valid="+_beginValid+" fault="+(faulted?1:0)+" seq="+sequence
                    +" args="+arg0+","+arg1+","+arg2+" handle="+(handlePresent?1:0));
            return;
        }
        _beginAccepted++;
        if(_beginAccepted==1)
            Emit("phase=begin result=accepted valid=1 fault=0 seq="+sequence+" args="+arg0+","+arg1+","+arg2
                +" handle="+(handlePresent?1:0)+" prev="+(previousPresent?1:0));
    }

    internal void Marker(bool accepted)
    {
        _markerSeen++;
        if(accepted) { _markerAccepted++; return; }
        _markerRefused++;Refuse("marker","refused");
        if(_markerRefused==1)Emit("phase=marker result=refused");
    }

    internal void Wallet(bool accepted)
    {
        _walletSeen++;
        if(accepted) { _walletAccepted++; return; }
        _walletRefused++;Refuse("wallet","fault");
        if(_walletRefused==1)Emit("phase=wallet result=fault");
    }

    internal void End(bool runOriginal,bool errorPresent,bool closed,bool faulted,bool valid,bool markerSeen,bool walletSeen,bool islandPresent,
        bool pending,bool returned,bool verification,bool accepted)
    {
        _endSeen++;
        _endOriginal=runOriginal?1:0;_endError=errorPresent?1:0;
        _endClosed=closed?1:0;_endFault=faulted?1:0;_endValid=valid?1:0;_endMarker=markerSeen?1:0;
        _endWallet=walletSeen?1:0;_endIsland=islandPresent?1:0;
        if(pending)_pending=1;
        if(returned)_returned=1;
        if(verification)_verification=1;
        if(accepted)_endAccepted++;
        else {_endRefused++;Refuse("end","closed="+_endClosed+",fault="+_endFault+" valid="+_endValid
            +",marker="+_endMarker+",wallet="+_endWallet+",island="+_endIsland);}
        if(_endSeen==1)
            Emit("phase=end result="+(accepted?"staged":"refused")+" closed="+_endClosed+" fault="+_endFault
                +" valid="+_endValid+" marker="+_endMarker+" wallet="+_endWallet+" island="+_endIsland
                +" original="+_endOriginal+" error="+_endError
                +" pend="+_pending+" ret="+_returned+" ver="+_verification);
    }

    internal void PrefsBegin(bool candidatePresent,long sequence)
    {
        _prefsSeen++;_prefsCandidate=candidatePresent?1:0;
        if(candidatePresent)_beginSequence=sequence;
    }

    internal void PrefsResult(bool runOriginal,bool errorPresent,bool closed,bool valuesPresent,bool faulted,bool sourceRead,bool sourceOk,
        string? sourceReason,bool applyAttempted,bool applyOk,string? applyReason)
    {
        _prefsOriginal=runOriginal?1:0;_prefsError=errorPresent?1:0;
        _prefsClosed=closed?1:0;_prefsValues=valuesPresent?1:0;_prefsFault=faulted?1:0;
        if(sourceRead)
        {
            _sourceReadSeen++;_sourceReadOk=sourceOk?1:0;
            if(!sourceOk)Refuse("source-read",sourceReason);
        }
        if(applyAttempted)
        {
            _applyAttempted=1;_applyOk=applyOk?1:0;
            if(applyOk)_captured++;
            else Refuse("apply",applyReason);
        }
        if(_prefsReported!=0)return;
        _prefsReported=1;
        string result=applyAttempted?(applyOk?"captured":"apply-refused")
            :sourceRead&&!sourceOk?"source-refused":"unobserved";
        Emit("phase=prefs result="+result+" closed="+_prefsClosed+" values="+_prefsValues+" fault="+_prefsFault
            +" original="+_prefsOriginal+" error="+_prefsError
            +" src="+(!sourceRead?"unobserved":sourceOk?"ok":"refused")
            +" apply="+(!applyAttempted?"unobserved":applyOk?"ok":"refused")
            +" captured="+_captured+" sourceReason="+Safe(sourceReason)+" applyReason="+Safe(applyReason));
    }

    internal void CurrencyComparison(System.Collections.Generic.IReadOnlyList<int>? captured,
        System.Collections.Generic.IReadOnlyList<int>? wallet,long sequence)
    {
        if(_currencyCompareReported!=0)return;
        _currencyCompareReported=1;
        bool capturedAvailable=captured!=null,walletAvailable=wallet!=null;
        int capturedCount=0,walletCount=0;
        bool equal=false;int index=-1,capturedValue=0,walletValue=0;bool values=false;
        try
        {
            capturedCount=capturedAvailable?captured!.Count:0;
            walletCount=walletAvailable?wallet!.Count:0;
            if(capturedAvailable&&walletAvailable&&capturedCount==8&&walletCount==8)
            {
                equal=true;
                for(int i=0;i<8;i++)
                {
                    if(captured![i]==wallet![i])continue;
                    equal=false;index=i;
                    capturedValue=captured![i];walletValue=wallet![i];values=true;
                    break;
                }
            }
        }
        catch(Exception) { equal=false;index=-1;values=false; }
        Emit("phase=currency seq="+sequence+" cpresent="+(capturedAvailable?1:0)+" ccount="+capturedCount
            +" wpresent="+(walletAvailable?1:0)+" wcount="+walletCount+" equal="+(equal?1:0)+" index="+index
            +(values?" capturedValue="+capturedValue+" observedWalletValue="+walletValue:""));
    }

    internal void ArrivalGuard(string leaf,int faultLength,int tripMismatch,int liveGameNull,int liveState,
        int currentLand,int targetLand,int historicalUnityNull,int historicalClrNull,int pointerEqual)
    {
        if(_arrivalGuardReported!=0)return;
        _arrivalGuardReported=1;
        Emit("phase=arrival-guard result=first-failure leaf="+Safe(leaf)
            +" bfault="+ObservedFlag(faultLength)+" tripmismatch="+ObservedFlag(tripMismatch)
            +" gnull="+ObservedFlag(liveGameNull)+" state="+ObservedFlag(liveState)
            +" land="+ObservedFlag(currentLand)+" target="+ObservedFlag(targetLand)
            +" hgnull="+ObservedFlag(historicalUnityNull)+" hgclr="+ObservedFlag(historicalClrNull)
            +" ptr="+ObservedFlag(pointerEqual));
    }

    internal void FirstFault(string? reason)
    {
        if(_firstFaultReported!=0)return;
        _firstFaultReported=1;
        Emit("phase=first-fault reason="+Safe(reason));
    }

    internal void Resolve(bool hasHandle,bool ok,string? reason)
    {
        _resolveSeen++;_resolveOk=ok?1:0;_resolveReason=reason??"";
        if(_resolveSeen!=1)return;
        // A terminal resolve outcome is reported on its own; it never rewrites the first upstream refusal.
        Emit(Summary());
    }

    private void Refuse(string group,string? reason)
    {
        if(_firstGroup.Length!=0)return;
        _firstGroup=group;_firstReason=reason??"";
    }

    private static string Safe(string? value)
    {
        if(string.IsNullOrEmpty(value))return "-";
        return value!.Replace("\r"," ").Replace("\n"," ").Replace(";",",");
    }

    private static string Count(int seen,int accepted,int invalid,int refused,int faulted)
    {
        if(seen==0)return "unobserved";
        return seen+"/"+accepted+"/"+invalid+"/"+refused+"/"+faulted;
    }

    private static string ObservedFlag(int value) => value<0 ? "unobserved" : value.ToString();

    private static string Seen(int seen,int accepted,int refused)
    {
        return seen==0?"unobserved":seen+"/"+accepted+"/"+refused;
    }

    private string ShortTrip() => _trip==Guid.Empty ? "null" : _trip.ToString("N").Substring(0,8);

    private string Summary()
    {
        return "summary trip="+ShortTrip()+" begin="+Count(_beginSeen,_beginAccepted,_beginInvalid,_beginRefused,_beginFaulted)
            +" seq="+_beginSequence+" args="+_beginArg0+","+_beginArg1+","+_beginArg2+" bfail="+_beginFail
            +" bvalid="+_beginValid+" stage="+ObservedFlag(_beginStage)+" invalidStage="+ObservedFlag(_firstInvalidStage)
            +" marker="+Seen(_markerSeen,_markerAccepted,_markerRefused)
            +" wallet="+Seen(_walletSeen,_walletAccepted,_walletRefused)
            +" end="+Seen(_endSeen,_endAccepted,_endRefused)+" eclosed="+_endClosed+" efault="+_endFault
            +" evalid="+_endValid+" emarker="+_endMarker+" ewallet="+_endWallet+" eisland="+_endIsland
            +" eorig="+ObservedFlag(_endOriginal)+" eerror="+ObservedFlag(_endError)
            +" pend="+_pending+" ret="+_returned+" ver="+_verification
            +" prefs="+(_prefsSeen==0?"unobserved":_prefsSeen+"/"+_prefsCandidate+"/"+_prefsClosed+"/"+_prefsValues)
            +" porig="+ObservedFlag(_prefsOriginal)+" perror="+ObservedFlag(_prefsError)
            +" src="+(_sourceReadSeen==0?"unobserved":_sourceReadSeen+"/"+_sourceReadOk)
            +" apply="+(_applyAttempted==0?"unobserved":_applyOk.ToString())+" captured="+_captured
            +" resolve="+(_resolveSeen==0?"unobserved":_resolveOk+":"+Safe(_resolveReason))
            +" first="+(_firstGroup.Length==0?"unobserved":_firstGroup+":"+Safe(_firstReason));
    }

    private void Emit(string text)
    {
        if(_lines>=MaxLines)return;
        _lines++;
        var sink=_sink;
        if(sink==null)return;
        try { sink("[PALACE-PAID-CAPTURE] trip="+ShortTrip()+" "+text); }
        catch(Exception) { }
    }
}
// <PALACE-PAID-CAPTURE-DIAGNOSTICS-END>
#endif

internal static class PalacePaidResponsibility
{
    internal static bool TryValidateSourceCapturedBeforeVerification(PalacePaidHandle handle,out string reason) => PalacePaidHandle.Engine.TryValidateSourceCapturedBeforeVerification(handle,out reason);
    internal static bool ObserveExactColdArrival(PalacePaidHandle handle,Game game,out string reason) => PalacePaidHandle.Engine.ObserveExactColdArrival(handle,game,out reason);
    internal static bool InstallOnce(Harmony harmony) => PalacePaidHandle.Engine.InstallOnce(harmony);
    internal static PalacePaidPreparationOutcome PrepareAtPlaying(Game game, Kingdom kingdom) => PalacePaidHandle.Engine.PrepareAtPlaying(game,kingdom);
    internal static bool CanBeginCharge(Game game,Kingdom kingdom,Player payer,PayableComponent payable,out string reason) => PalacePaidHandle.Engine.CanBeginCharge(game,kingdom,payer,payable,out reason);
    internal static bool TryBeginCharge(Game game,Kingdom kingdom,Player payer,PayableComponent payable,PalaceEntryAnchorData anchor,PalaceDestinationDecision observed,out PalacePaidHandle handle,out string reason) => PalacePaidHandle.Engine.TryBeginCharge(game,kingdom,payer,payable,anchor,observed,out handle,out reason);
    internal static bool CanContinueCharge(PalacePaidHandle handle,Player payer,PayableComponent payable,out string reason) => PalacePaidHandle.Engine.CanContinueCharge(handle,payer,payable,out reason);
    internal static void ObserveOnPay(PalacePaidHandle handle,Player payer) => PalacePaidHandle.Engine.ObserveOnPay(handle,payer);
    internal static bool TryObserveSettled(PalacePaidHandle handle,Player payer,out string reason) => PalacePaidHandle.Engine.TryObserveSettled(handle,payer,out reason);
    internal static bool TryObserveCancelled(PalacePaidHandle handle,Player payer,out string reason) => PalacePaidHandle.Engine.TryObserveCancelled(handle,payer,out reason);
    internal static PalacePaidCaptureToken BeginSourceCapture(int campaign,int land,int challenge) => PalacePaidHandle.Engine.BeginSourceCapture(campaign,land,challenge);
    internal static void ObserveCaptureMarker(IslandSaveData island,bool runOriginal) => PalacePaidHandle.Engine.ObserveCaptureMarker(island,runOriginal);
    internal static void EndSourceCapture(PalacePaidCaptureToken token,bool runOriginal,Exception? error) => PalacePaidHandle.Engine.EndSourceCapture(token,runOriginal,error);
    internal static PalacePaidPrefsToken BeginPrefsPrepare(PrefsSaveData prefs) => PalacePaidHandle.Engine.BeginPrefsPrepare(prefs);
    internal static void EndPrefsPrepare(PalacePaidPrefsToken token,bool runOriginal,Exception? error) => PalacePaidHandle.Engine.EndPrefsPrepare(token,runOriginal,error);
    internal static bool TryResolvePhysicalSource(PalacePaidHandle handle,int actualRawResult,out PalaceSourceClockReceipt receipt,out string reason) => PalacePaidHandle.Engine.TryResolvePhysicalSource(handle,actualRawResult,out receipt,out reason);
    internal static bool TryClaimDeparture(PalacePaidHandle handle,out string reason) => PalacePaidHandle.Engine.TryClaimDeparture(handle,out reason);
    internal static bool ObserveExactArrival(PalacePaidHandle handle,Game game,string tripId,int targetLand,out string reason) => PalacePaidHandle.Engine.ObserveExactArrival(handle,game,tripId,targetLand,out reason);
    internal static bool TryReadExistingForBootstrap(PalacePaidScope owner,string tripId,int sourceLand,out PalacePaidHandle handle,out string reason) => PalacePaidHandle.Engine.TryReadExistingForBootstrap(owner,tripId,sourceLand,out handle,out reason);
    internal static bool TryGetPreparedHandle(Game game,Kingdom kingdom,Player payer,out PalacePaidHandle handle,out string reason) => PalacePaidHandle.Engine.TryGetPreparedHandle(game,kingdom,payer,out handle,out reason);
    internal static bool TryConfirmReturnedSave(PalacePaidHandle handle,int actualRawResult,out string reason) => PalacePaidHandle.Engine.TryConfirmReturnedSave(handle,actualRawResult,out reason);
    internal static bool TryReadCommittedForNextVisit(PalacePaidHandle handle,out PalacePaidEnvelope envelope,out string reason) => PalacePaidHandle.Engine.TryReadCommittedForNextVisit(handle,out envelope,out reason);
    internal static bool TryReadCommittedForNextVisit(PalacePaidHandle handle,string completedTripId,out PalacePaidEnvelope envelope,out string reason) => PalacePaidHandle.Engine.TryReadCommittedForNextVisit(handle,completedTripId,out envelope,out reason);
}
