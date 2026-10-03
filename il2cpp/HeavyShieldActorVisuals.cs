// Heavy-shield actor presentation only. The career owner must explicitly register a
// carrier life and send stance/action/wear changes. No spawn/promotion/damage hooks,
// scene scans, native Animator writes, or currency/network operations live here.
using System;
#if !HEAVY_SHIELD_VISUALS_CORE_ONLY
using System.Collections.Generic;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
#endif

namespace KingdomEnhancedMod;

internal enum HeavyShieldStance { BackIdle, BackWalk, BackRun, GuardIdle, Advance }
internal enum HeavyShieldAction { None, Equip, Stow, Block, Bash, Break,
    WalkStart, WalkStartAlt, WalkStop, WalkStopAlt, RunStart, RunStop, Relax, RestEnter, Rest, RestExit }
internal enum HeavyShieldWear { Intact, Worn, Critical, Half }

/// <summary>Pure presentation clock using the final art's frame rate and loop metadata.
/// A repeated stance/wear update never resets phase. Leisure never owns native control.</summary>
internal sealed class HeavyShieldPoseMachine
{
    internal const float QuietDelay = 3f;
    internal const float RestDuration = 8f;
    private float _quietElapsed;
    private bool _quietAllowed, _alternateWalk;
    private static bool _loggedFrameRate;
    internal HeavyShieldStance Stance { get; private set; } = HeavyShieldStance.BackIdle;
    internal HeavyShieldWear Wear { get; private set; } = HeavyShieldWear.Intact;
    internal HeavyShieldAction Action { get; private set; }
    internal float Elapsed { get; private set; }

    internal void SetStance(HeavyShieldStance stance)
    {
        if (!Enum.IsDefined(typeof(HeavyShieldStance), stance) || Stance == stance) return;
        Stance = stance;
        if (Action == HeavyShieldAction.None) Elapsed = 0f;
    }

    internal void SetWear(HeavyShieldWear wear)
    {
        if (Enum.IsDefined(typeof(HeavyShieldWear), wear)) Wear = wear;
    }

    internal static bool IsPresentationAction(HeavyShieldAction action)
        => action == HeavyShieldAction.None || action >= HeavyShieldAction.WalkStart && action <= HeavyShieldAction.RestExit;

    private static bool IsLeisureAction(HeavyShieldAction action)
        => action == HeavyShieldAction.Relax || action == HeavyShieldAction.RestEnter
            || action == HeavyShieldAction.Rest || action == HeavyShieldAction.RestExit;

    internal void InterruptLeisure()
    {
        _quietElapsed = 0f; _quietAllowed = false;
        if (IsLeisureAction(Action)) { Action = HeavyShieldAction.None; Elapsed = 0f; }
    }

    // Called with a read-only native motion sample. Edges, not steady moving conditions, trigger clips.
    internal void SetPresentation(HeavyShieldStance stance, bool threatened, bool motionKnown)
    {
        if (!Enum.IsDefined(typeof(HeavyShieldStance), stance)) return;
        var before = Stance;
        bool still = stance == HeavyShieldStance.BackIdle || stance == HeavyShieldStance.GuardIdle;
        if (threatened || !motionKnown || !still) InterruptLeisure();
        if (!motionKnown && IsPresentationAction(Action) && Action != HeavyShieldAction.None)
        { Action = HeavyShieldAction.None; Elapsed = 0f; }
        SetStance(stance);
        _quietAllowed = motionKnown && still && !threatened && IsPresentationAction(Action);
        if (!_quietAllowed) _quietElapsed = 0f;
        if (!motionKnown || threatened || before == stance || !IsPresentationAction(Action)) return;
        bool wasBack = before == HeavyShieldStance.BackIdle || before == HeavyShieldStance.BackWalk || before == HeavyShieldStance.BackRun;
        bool isBack = stance == HeavyShieldStance.BackIdle || stance == HeavyShieldStance.BackWalk || stance == HeavyShieldStance.BackRun;
        if (!wasBack || !isBack) return;
        if (stance == HeavyShieldStance.BackRun) Trigger(HeavyShieldAction.RunStart);
        else if (before == HeavyShieldStance.BackRun) Trigger(HeavyShieldAction.RunStop);
        else if (stance == HeavyShieldStance.BackWalk)
        {
            Trigger(_alternateWalk ? HeavyShieldAction.WalkStartAlt : HeavyShieldAction.WalkStart);
            _alternateWalk = !_alternateWalk;
        }
        else if (before == HeavyShieldStance.BackWalk)
            Trigger(_alternateWalk ? HeavyShieldAction.WalkStop : HeavyShieldAction.WalkStopAlt);
    }

    /// <summary>Called once per actual game event, never from a steady per-frame condition.</summary>
    internal bool Trigger(HeavyShieldAction action)
    {
        if (!Enum.IsDefined(typeof(HeavyShieldAction), action) || action == HeavyShieldAction.None
            || Action == HeavyShieldAction.Break) return false;
        Action = action;
        Elapsed = 0f;
        if (!IsLeisureAction(action)) _quietElapsed = 0f;
        return true;
    }

    internal void Tick(float deltaTime)
    {
        if (!float.IsFinite(deltaTime) || deltaTime <= 0f) return;
        float step = Math.Min(deltaTime, 0.25f);
        Elapsed = Math.Min(Elapsed + step, 10000f);
        if (_quietAllowed && IsPresentationAction(Action)) _quietElapsed = Math.Min(_quietElapsed + step, 10000f);
        if (Action == HeavyShieldAction.None)
        {
            if (_quietAllowed && _quietElapsed >= QuietDelay)
                Trigger(Stance == HeavyShieldStance.GuardIdle ? HeavyShieldAction.RestEnter : HeavyShieldAction.Relax);
            return;
        }
        if (Action == HeavyShieldAction.Break || !TrySequence(out var seq)) return;
        if (Action == HeavyShieldAction.Rest && Elapsed >= RestDuration)
        { Trigger(HeavyShieldAction.RestExit); return; }
        if (seq.Loop || Elapsed < SequenceDuration(seq)) return;
        if (Action == HeavyShieldAction.Equip) Stance = HeavyShieldStance.GuardIdle;
        if (Action == HeavyShieldAction.Stow) Stance = HeavyShieldStance.BackIdle;
        if (Action == HeavyShieldAction.RestEnter) { Trigger(HeavyShieldAction.Rest); return; }
        if (Action == HeavyShieldAction.RestExit) _quietElapsed = 0f;
        Action = HeavyShieldAction.None;
        Elapsed = 0f;
    }

    internal bool TryFrame(out int frame)
    {
        frame = -1;
        if (!TrySequence(out var seq)) return false;
        double sample = Math.Floor((double)Elapsed * SafeFrameRate(seq.Fps));
        int offset = seq.Loop ? (int)(sample % seq.Count) : (int)Math.Min(sample, seq.Count - 1);
        if (seq.Loop) offset %= seq.Count;
        else if (offset >= seq.Count) offset = seq.Count - 1;
        if (offset < 0) offset = 0;
        frame = seq.First + offset;
        return HeavyShieldArtLayout.IsValidFrame(HeavyShieldAtlasId.Soldier, frame);
    }

    internal static float SafeFrameRate(float fps)
    {
        if (float.IsFinite(fps) && fps > 0f) return fps;
        if (!_loggedFrameRate)
        {
            _loggedFrameRate = true;
#if !HEAVY_SHIELD_VISUALS_CORE_ONLY
            try { KingdomEnhancedPlugin.Instance?.LogSource?.LogWarning("[HeavyShieldVisuals] invalid sequence FPS; using 12"); } catch { }
#endif
        }
        return 12f;
    }

    internal static float SequenceDuration(in HeavyShieldSequence seq) => Math.Max(1, seq.Count) / SafeFrameRate(seq.Fps);

    private bool TrySequence(out HeavyShieldSequence seq)
    {
        string name = Action switch
        {
            HeavyShieldAction.Equip => "equip", HeavyShieldAction.Stow => "stow",
            HeavyShieldAction.Block => "block", HeavyShieldAction.Bash => "bash", HeavyShieldAction.Break => "break",
            HeavyShieldAction.WalkStart => "walk_start", HeavyShieldAction.WalkStartAlt => "walk_start_alt",
            HeavyShieldAction.WalkStop => "walk_stop", HeavyShieldAction.WalkStopAlt => "walk_stop_alt",
            HeavyShieldAction.RunStart => "run_start", HeavyShieldAction.RunStop => "run_stop",
            HeavyShieldAction.Relax => "relax_idle", HeavyShieldAction.RestEnter => "rest_enter",
            HeavyShieldAction.Rest => "rest_idle", HeavyShieldAction.RestExit => "rest_exit",
            _ => Stance switch {
                HeavyShieldStance.BackIdle => "back_idle", HeavyShieldStance.BackWalk => "back_walk",
                HeavyShieldStance.BackRun => "back_run", HeavyShieldStance.GuardIdle => "guard_idle",
                HeavyShieldStance.Advance => "defense_advance", _ => null }
        };
        string prefix = Wear switch { HeavyShieldWear.Worn => "worn_", HeavyShieldWear.Critical => "critical_",
            HeavyShieldWear.Half => "half_", _ => "" };
        seq = default;
        return name != null && HeavyShieldArtLayout.TryGetSequence(HeavyShieldAtlasId.Soldier,
            Action == HeavyShieldAction.Break ? name : prefix + name, out seq) && seq.Count > 0;
    }
}

#if !HEAVY_SHIELD_VISUALS_CORE_ONLY
/// <summary>Owned sprite on the native Archer renderer's transform. The future shield career
/// calls Register/Unregister with its own positive life lease; a local driver only ticks that
/// registered actor. Native forceRenderingOff is returned only while our recorded write remains.</summary>
internal static class HeavyShieldActorVisuals
{
    private sealed class Visual
    {
        internal Archer Carrier;
        internal IntPtr Pointer;
        internal int GoId;
        internal long Life;
        internal SpriteRenderer Native;
        internal SpriteRenderer Own;
        internal GameObject Root;
        internal int RootId;
        internal HeavyShieldPoseMachine Pose = new HeavyShieldPoseMachine();
        internal bool HidNative;
        internal int LastFrame = -1;
        internal int Facing = 1;
        internal bool Suspended;
        internal bool Initialized;
        internal HeavyShieldCareerHandle Career;
        internal IntPtr NativePointer;
        internal int NativeId, Scene;
    }

    private static readonly Dictionary<int, Visual> Active = new Dictionary<int, Visual>();
    private static readonly Dictionary<int, int> RootOwner = new Dictionary<int, int>();
    private static readonly MaterialPropertyBlock PropertyBlock = new MaterialPropertyBlock();
    private static bool _driverRegistered;
    private static bool _loggedFailure;

    internal static bool Register(Archer carrier, long life)
    {
        if (carrier == null || life <= 0) return false;
        int id = Id(carrier);
        IntPtr pointer = Pointer(carrier);
        if (id == 0 || pointer == IntPtr.Zero) return false;
        try
        {
            if (!carrier.gameObject.activeInHierarchy || HeroArcherRuntime.IsHero(carrier)
                || MusketeerRuntime.IsMusketeer(carrier)) return false;
        }
        catch (Exception) { return false; }
        if (Active.TryGetValue(id, out var old))
        {
            if (Same(old, carrier, life)) return true;
            if (!Remove(id)) return false;
        }
        SpriteRenderer native = FindNative(carrier);
        if (native == null) return false;
        try { if (!native.enabled || native.forceRenderingOff) return false; }
        catch (Exception) { return false; }
        if (!HeavyShieldArt.TryGetSoldierSprite(0, out Sprite first) || first == null) return false;
        if (!EnsureDriver()) return false;

        if (!HeavyShieldIdentity.TryGetSoldier(carrier, out var career) || career.Life != life
            || !HeavyShieldIdentity.ValidateCareer(career)) return false;
        var state = new Visual { Carrier = carrier, Pointer = pointer, GoId = id, Life = life, Native = native,
            Career = career, NativePointer = native.Pointer, NativeId = native.GetInstanceID(), Scene = carrier.gameObject.scene.handle,
            Facing = career.Side == HeavyShieldQuota.Side.Left ? -1 : 1 };
        try
        {
            state.Root = new GameObject("KEM_HeavyShieldBody");
            state.RootId = state.Root.GetInstanceID();
            state.Root.transform.SetParent(native.transform, false);
            state.Root.transform.localPosition = Vector3.zero;
            state.Root.transform.localRotation = Quaternion.identity;
            state.Root.transform.localScale = Vector3.one;
            state.Own = state.Root.AddComponent<SpriteRenderer>();
            if (state.Own == null) throw new InvalidOperationException("owned renderer absent");
            state.Own.enabled = false;
            state.Own.sprite = first;
            CopyLook(state);
            var driver = state.Root.AddComponent<HeavyShieldVisualDriver>();
            if (driver == null)
                throw new InvalidOperationException("visual driver absent");
            Active.Add(id, state);
            RootOwner.Add(state.RootId, id);
            Tick(id); // renderer/sprite/driver all ready before we hide native
            if (!HasQualifiedVisual(carrier, life)) { Remove(id); return false; }
            return true;
        }
        catch (Exception e)
        {
            if (!Release(state))
            {
                Active[id] = state; // retain the restore credential for a later Clear/Unregister
                LogOnce("register rollback pending: " + e.GetType().Name);
                return false;
            }
            Active.Remove(id);
            RootOwner.Remove(state.RootId);
            Destroy(state.Root);
            LogOnce("register failed: " + e.GetType().Name);
            return false;
        }
    }

    internal static bool HasVisual(Archer carrier, long life)
        => carrier != null && Active.TryGetValue(Id(carrier), out var state) && Same(state, carrier, life);

    // Execution qualification is separate from the ledger used by teardown.
    internal static bool HasQualifiedVisual(Archer carrier, long life)
        => TryState(carrier, life, out var state) && Qualified(state);
    private static bool Qualified(Visual state)
    {
        try
        {
            return state.Initialized && !state.Suspended && VisibilityProof(state, false)
                && state.Root != null && state.Root.activeInHierarchy && state.Own != null && state.Own.enabled
                && !state.Own.forceRenderingOff && state.Own.gameObject.Pointer == state.Root.Pointer
                && state.Own.sprite != null && state.Own.sharedMaterial != null && state.LastFrame >= 0
                && state.Native.enabled && state.HidNative && state.Native.forceRenderingOff;
        }
        catch { return false; }
    }

    internal static bool GuardReady(Archer carrier, long life)
        => TryState(carrier, life, out var state) && Qualified(state)
            && (state.Pose.Stance == HeavyShieldStance.GuardIdle || state.Pose.Stance == HeavyShieldStance.Advance)
            && state.Pose.Action != HeavyShieldAction.Equip && state.Pose.Action != HeavyShieldAction.Stow
            && state.Pose.Action != HeavyShieldAction.Break;

    internal static bool TryPose(Archer carrier, long life, out HeavyShieldAction action, out float elapsed)
    {
        action = HeavyShieldAction.None; elapsed = 0;
        if (!TryState(carrier, life, out var state)) return false;
        action = state.Pose.Action; elapsed = state.Pose.Elapsed; return true;
    }

    internal static void SetFacing(Archer carrier, long life, int facing)
    { if ((facing == -1 || facing == 1) && TryState(carrier, life, out var state)) state.Facing = facing; }

    internal static void Suspend(Archer carrier, long life)
    { if (TryState(carrier, life, out var state)) { state.Suspended = true; if (state.Own != null) state.Own.enabled = false; } }

    internal static void SetStance(Archer carrier, long life, HeavyShieldStance stance)
    {
        if (TryState(carrier, life, out var state)) state.Pose.SetStance(stance);
    }

    internal static void SetPresentation(Archer carrier, long life, HeavyShieldStance stance, bool threatened, bool motionKnown)
    { if (TryState(carrier, life, out var state)) state.Pose.SetPresentation(stance, threatened, motionKnown); }

    internal static void InterruptLeisure(Archer carrier, long life)
    { if (TryState(carrier, life, out var state)) state.Pose.InterruptLeisure(); }

    internal static void SetWear(Archer carrier, long life, HeavyShieldWear wear)
    {
        if (TryState(carrier, life, out var state)) state.Pose.SetWear(wear);
    }

    internal static bool Trigger(Archer carrier, long life, HeavyShieldAction action)
        => TryState(carrier, life, out var state) && state.Pose.Trigger(action);

    internal static bool Unregister(Archer carrier, long life)
    {
        if (!TryState(carrier, life, out var state)) return false;
        return Remove(state.GoId);
    }

    internal static void Clear()
    {
        var keys = new List<int>(Active.Keys);
        for (int i = 0; i < keys.Count; i++) Remove(keys[i]);
    }

    internal static void OnDriverDisabled(int rootId)
    {
        if (RootOwner.TryGetValue(rootId, out int actorId)) Remove(actorId);
    }

    internal static void TickRoot(int rootId)
    {
        if (RootOwner.TryGetValue(rootId, out int actorId)) Tick(actorId);
    }

    // The driver is per registered actor: no all-scene or all-character walk.
    internal static void Tick(int id)
    {
        if (!Active.TryGetValue(id, out var state)) return;
        if (!Same(state, state.Carrier, state.Life) || !VisibilityProof(state, false) || state.Root == null || state.Own == null || state.Native == null
            || state.Carrier.gameObject == null || !state.Carrier.gameObject.activeInHierarchy)
        {
            Remove(id);
            return;
        }
        try
        {
            if (state.Suspended) { state.Own.enabled = false; return; }
            // Initialize one complete safe frame during Register. Later invalidation cannot be silently repaired by LateUpdate.
            if (state.Initialized && !Qualified(state)) { state.Suspended = true; state.Own.enabled = false; return; }
            if (HeavyShieldRuntime.Playing) state.Pose.Tick(Time.deltaTime);
            bool nativeVisible = state.Native.enabled && (!state.Native.forceRenderingOff || state.HidNative);
            if (!nativeVisible)
            {
                state.Own.enabled = false;
                return;
            }
            if (!state.Pose.TryFrame(out int frame)
                || !HeavyShieldArt.TryGetSoldierSprite(frame, out Sprite sprite) || sprite == null)
            {
                Remove(id);
                return;
            }
            CopyLook(state);
            if (state.LastFrame != frame) { state.Own.sprite = sprite; state.LastFrame = frame; }
            if (!state.HidNative)
            {
                if (state.Native.forceRenderingOff) { state.Own.enabled = false; return; }
                state.HidNative = true; // record obligation before setter can partially succeed
                state.Native.forceRenderingOff = true;
            }
            else if (!state.Native.forceRenderingOff)
            {
                // Another system restored native visibility: relinquish instead of fighting it.
                state.HidNative = false;
                Remove(id);
                return;
            }
            state.Own.enabled = true;
            state.Initialized = true;
        }
        catch (Exception e)
        {
            LogOnce("tick failed: " + e.GetType().Name);
            Remove(id);
        }
    }

    private static bool TryState(Archer carrier, long life, out Visual state)
    {
        state = null;
        return carrier != null && life > 0 && Active.TryGetValue(Id(carrier), out state)
            && Same(state, carrier, life);
    }

    private static bool Same(Visual state, Archer carrier, long life)
        => state != null && carrier != null && state.Life == life && state.Pointer == Pointer(carrier)
            && state.GoId == Id(carrier);

    private static bool Remove(int id)
    {
        if (!Active.TryGetValue(id, out var state)) return true;
        try { if (state.Own != null) state.Own.enabled = false; }
        catch (Exception) { }
        if (!Release(state)) return false; // keep the only restoration credential for retry
        Active.Remove(id);
        RootOwner.Remove(state.RootId);
        Destroy(state.Root);
        return true;
    }

    private static bool Release(Visual state)
    {
        if (!state.HidNative) return true;
        try
        {
            if (!VisibilityProof(state, true)) return false;
            if (state.Native != null && state.Native.forceRenderingOff)
                state.Native.forceRenderingOff = false;
            state.HidNative = false;
            return true;
        }
        catch (Exception) { return false; }
    }

    private static SpriteRenderer FindNative(Archer actor)
    {
        try
        {
            var r = actor.gameObject.GetComponent<SpriteRenderer>();
            return r != null ? r : actor.GetComponentInChildren<SpriteRenderer>();
        }
        catch (Exception) { return null; }
    }

    private static void CopyLook(Visual state)
    {
        SpriteRenderer native = state.Native, own = state.Own;
        own.sortingLayerID = native.sortingLayerID;
        own.sortingOrder = native.sortingOrder;
        // Art PPU already gives a 0.7 standing body. Cancel only inherited XY magnitude on our child;
        // never resize the native actor/Mover/colliders or move the native foot/pivot.
        Vector3 inherited = native.transform.lossyScale;
        if (!float.IsFinite(inherited.x) || !float.IsFinite(inherited.y) || inherited.x == 0f || inherited.y == 0f)
            throw new InvalidOperationException("unknown native sprite scale");
        float inverseX = 1f / Math.Abs(inherited.x), inverseY = 1f / Math.Abs(inherited.y);
        if (!float.IsFinite(inverseX) || !float.IsFinite(inverseY) || inverseX <= 0f || inverseY <= 0f)
            throw new InvalidOperationException("unrepresentable native sprite scale");
        own.transform.localScale = new Vector3(inverseX, inverseY, 1f);
        float scaleX = own.transform.lossyScale.x;
        if (!float.IsFinite(scaleX) || scaleX == 0f) throw new InvalidOperationException("unknown world sprite facing");
        own.flipX = (state.Facing < 0) != (scaleX < 0f); // compensate the native Mover's mirrored parent chain exactly once
        own.flipY = native.flipY;
        own.color = native.color;
        own.gameObject.layer = native.gameObject.layer;
        if (native.sharedMaterial != null) own.sharedMaterial = native.sharedMaterial;
        native.GetPropertyBlock(PropertyBlock);
        own.SetPropertyBlock(PropertyBlock);
    }

    private static bool EnsureDriver()
    {
        if (_driverRegistered) return true;
        try
        {
            if (!ClassInjector.IsTypeRegisteredInIl2Cpp(typeof(HeavyShieldVisualDriver)))
                ClassInjector.RegisterTypeInIl2Cpp(typeof(HeavyShieldVisualDriver));
            _driverRegistered = true;
            return true;
        }
        catch (Exception e) { LogOnce("driver registration failed: " + e.GetType().Name); return false; }
    }

    private static bool VisibilityProof(Visual state, bool allowInactive)
    {
        try
        {
            var root = state.Carrier?.gameObject; var world = Managers.Inst?.world?.gameLayer;
            var native = state.Native;
            return root != null && (allowInactive || root.activeInHierarchy) && world != null
                && root.Pointer == state.Career.Root && root.GetInstanceID() == state.Career.GoId
                && state.Career.Life == state.Life && world.Pointer.ToInt64() == state.Career.World
                && root.scene.handle == state.Scene && HeavyShieldIdentity.ValidateCareer(state.Career)
                && HeavyShieldRuntime.HasNativeSourceLife(state.Carrier, state.Life, allowInactive)
                && native != null && native.Pointer == state.NativePointer && native.GetInstanceID() == state.NativeId
                && native.transform.IsChildOf(root.transform) && FindNative(state.Carrier)?.Pointer == state.NativePointer;
        }
        catch { return false; }
    }

    private static int Id(Archer actor)
    {
        try { return actor?.gameObject != null ? actor.gameObject.GetInstanceID() : 0; }
        catch (Exception) { return 0; }
    }
    private static IntPtr Pointer(Archer actor)
    {
        try { return actor != null ? actor.Pointer : IntPtr.Zero; }
        catch (Exception) { return IntPtr.Zero; }
    }
    private static void Destroy(UnityEngine.Object obj)
    {
        try { if (obj != null) UnityEngine.Object.Destroy(obj); }
        catch (Exception) { }
    }
    private static void LogOnce(string message)
    {
        if (_loggedFailure) return;
        _loggedFailure = true;
        try { KingdomEnhancedPlugin.Instance?.LogSource?.LogWarning("[HeavyShieldVisual] " + message); }
        catch (Exception) { }
    }
}

internal sealed class HeavyShieldVisualDriver : MonoBehaviour
{
    public HeavyShieldVisualDriver(IntPtr pointer) : base(pointer) { }
    private void LateUpdate()
    {
        try { HeavyShieldActorVisuals.TickRoot(gameObject.GetInstanceID()); }
        catch (Exception) { }
    }
    private void OnDisable()
    {
        try { HeavyShieldActorVisuals.OnDriverDisabled(gameObject.GetInstanceID()); }
        catch (Exception) { }
    }
    private void OnDestroy()
    {
        try { HeavyShieldActorVisuals.OnDriverDisabled(gameObject.GetInstanceID()); }
        catch (Exception) { }
    }
}
#endif
