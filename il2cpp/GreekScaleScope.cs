using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>Owns only our scale writes. Scope follows the current world, never an actor's origin.</summary>
internal static class GreekScaleScope
{
    private enum Scope { Unknown, Inactive, Active }
    private enum Liveness { Alive, Missing, Deferred }
    // Default requests remain Greek-only. Wallet visuals are the only all-world owners.
    private enum RequestScope { GreekOnly, EnabledAllWorlds }
    private readonly record struct Identity(IntPtr Pointer, int Id, IntPtr ObjectPointer, int ObjectId);
    private sealed class Request
    {
        internal Transform Target;
        internal Identity Key;
        internal RequestScope Scope;
        internal Vector3 Desired, Original, Written;
        internal int Axes, Owned;
        internal Vector3 NativeBaseline;
        internal int NativeBaselineAxes;
        // Wallet coins use a one-shot correction: no lease survives success.
        internal bool OneShot;
        internal Vector3 OneShotBefore;
        internal IntPtr MoverPointer;
        internal int MoverId;
        internal Mover Mover;
        internal int NextLateRetryFrame;
        internal PendingWrite? Pending;
    }
    private struct PendingWrite
    {
        internal Vector3 Result, Original, Written;
        internal int Owned;
    }

    private static readonly Dictionary<Identity, Request> Requests = new();
    private static readonly List<Request> LateSnapshot = new();
    private static bool _maintainingRegisteredY;
    private static Scope _lastGreekScope = Scope.Unknown;
    private static Scope _lastEnabledScope = Scope.Unknown;
    private static int _nextPruneFrame;
    private static int _nextRetryFrame;
    private static bool _pending;
    private static bool _loggedFailure, _loggedWrite, _loggedScopeConflict;
    public static bool IsActive => CurrentScope(RequestScope.GreekOnly) == Scope.Active;
    public static bool IsEnabledAllWorldsActive => CurrentScope(RequestScope.EnabledAllWorlds) == Scope.Active;

    private static Scope CurrentScope(RequestScope requestScope)
    {
        try
        {
            if (ModConfig.Enabled == null) return Scope.Unknown;
            if (!ModConfig.Enabled.Value) return Scope.Inactive;
            // All-world ownership follows the mod switch only; loading worlds keep
            // their retained ownership while the switch is unknown.
            if (requestScope == RequestScope.EnabledAllWorlds) return Scope.Active;
            var biome = BiomeHolder.Inst;
            if (biome == null) return Scope.Unknown;
            int index = biome.BiomeIndex;
            if (index < 0) return Scope.Unknown;
            return index == BiomeHolder.GreeceBiomeIndex ? Scope.Active : Scope.Inactive;
        }
        catch (Exception e) { ReportFailure(e); return Scope.Unknown; }
    }

    private static void ReportScopeConflict(Transform target)
    {
        if (_loggedScopeConflict) return;
        _loggedScopeConflict = true;
        KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
            "[ScaleScope] Conflicting scope ignored for " + (target != null ? target.gameObject.name : "null"));
    }

    private static void ReportFailure(Exception error)
    {
        if (_loggedFailure) return;
        _loggedFailure = true;
        KingdomEnhancedPlugin.Instance?.LogSource.LogWarning("[ScaleScope] Native access deferred; retaining ownership for retry: " + error.GetType().Name);
    }

    private static bool Identify(Transform target, out Identity key)
        => Inspect(target, out key) == Liveness.Alive;

    private static Liveness Inspect(Transform target, out Identity key)
    {
        key = default;
        try
        {
            // Scene-invalid objects are shared resource assets, not runtime clones.
            if (target == null || target.Pointer == IntPtr.Zero || target.gameObject == null
                || !target.gameObject.scene.IsValid()) return Liveness.Missing;
            var go = target.gameObject;
            key = new Identity(target.Pointer, target.GetInstanceID(), go.Pointer, go.GetInstanceID());
            return Liveness.Alive;
        }
        catch (Exception e) { ReportFailure(e); return Liveness.Deferred; }
    }

    private static Request Get(Transform target, RequestScope requestScope)
    {
        if (!Identify(target, out var key)) return null;
        if (!Requests.TryGetValue(key, out var request))
        {
            request = new Request { Target = target, Key = key, Scope = requestScope };
            Requests.Add(key, request);
        }
        else if (request.Scope != requestScope)
        {
            // A transform owns exactly one scope. Never let an unrelated caller
            // silently widen or narrow an existing lease.
            ReportScopeConflict(target);
            return null;
        }
        return request;
    }

    public static void ApplyY(Transform target, float y)
    {
        var request = Get(target, RequestScope.GreekOnly);
        if (request == null) return;
        request.Desired.y = y;
        request.Axes |= 2;
        request.NativeBaselineAxes &= ~2;
        Reconcile(request, CurrentScope(RequestScope.GreekOnly));
    }

    public static void ApplyScale(Transform target, Vector3 scale)
        => ApplyScale(target, scale, RequestScope.GreekOnly, false, default);

    public static void ApplyScale(Transform target, Vector3 scale, Vector3 nativeBaseline)
        => ApplyScale(target, scale, RequestScope.GreekOnly, true, nativeBaseline);

    /// <summary>Wallet-only ownership: applies in every world whenever the mod is enabled.</summary>
    public static void ApplyScaleAllWorlds(Transform target, Vector3 scale)
        => ApplyScale(target, scale, RequestScope.EnabledAllWorlds, false, default);

    /// <summary>Wallet root/container: own X/Y only, leaving the authored Z untouched.</summary>
    public static void ApplyScaleXyAllWorlds(Transform target, Vector3 scale)
    {
        var request = Get(target, RequestScope.EnabledAllWorlds);
        if (request == null) return;
        request.Desired.x = scale.x;
        request.Desired.y = scale.y;
        request.Axes = 3;
        request.NativeBaselineAxes = 0;
        Reconcile(request, CurrentScope(RequestScope.EnabledAllWorlds));
    }

    private static void ApplyScale(Transform target, Vector3 scale, RequestScope requestScope,
        bool hasBaseline, Vector3 nativeBaseline)
    {
        var request = Get(target, requestScope);
        if (request == null) return;
        request.Desired = scale;
        request.Axes = 7;
        request.NativeBaseline = nativeBaseline;
        request.NativeBaselineAxes = hasBaseline ? 7 : 0;
        Reconcile(request, CurrentScope(requestScope));
    }

    /// <summary>
    /// One proven wallet-coin correction. It owns nothing after success: no desired
    /// value survives off/on, and each new proof supplies its own native baseline.
    /// </summary>
    public static void ApplyOneShotAllWorlds(Transform target, Vector3 scale, Vector3 nativeBaseline)
    {
        var request = Get(target, RequestScope.EnabledAllWorlds);
        if (request == null) return;
        if (!request.OneShot && (request.Axes != 0 || request.Owned != 0 || request.Pending != null))
        {
            // Bag root/container hold persistent leases; never convert one silently.
            ReportScopeConflict(target);
            return;
        }
        Vector3 current;
        try { current = target.localScale; }
        catch (Exception e)
        {
            // Without the incoming value there is no proof; never guess on retry.
            ReportFailure(e);
            FinishOneShot(request);
            RemoveIfFinished(request);
            return;
        }
        // A fresh complete proof replaces any previous one-shot baseline/target.
        request.OneShot = true;
        request.OneShotBefore = current;
        request.Desired = scale;
        request.Original = nativeBaseline;
        request.NativeBaseline = nativeBaseline;
        request.NativeBaselineAxes = 7;
        request.Axes = 7;
        request.Owned = 0;
        request.Pending = null;
        if (Reconcile(request, CurrentScope(RequestScope.EnabledAllWorlds))) RemoveIfFinished(request);
    }

    private static void RemoveIfFinished(Request request)
    {
        if (request.Axes == 0 && request.Owned == 0 && request.Pending == null
            && Requests.TryGetValue(request.Key, out var current) && ReferenceEquals(current, request))
            Requests.Remove(request.Key);
    }

    /// <summary>
    /// For coin visuals whose initial local scale already inherited our enlarged bag.
    /// The caller supplies a verified native prefab/container baseline; existing owned
    /// axes retain their original. Foreign requests never write this baseline directly.
    /// </summary>
    public static void ApplyInheritedScale(Transform target, Vector3 scale, Vector3 nativeBaseline)
    {
        var request = Get(target, RequestScope.GreekOnly);
        if (request == null) return;
        Vector3 current = target.localScale;
        ResolvePending(request, current);
        for (int axis = 0; axis < 3; axis++)
        {
            int bit = 1 << axis;
            if ((request.Owned & bit) != 0) continue;
            request.Original[axis] = nativeBaseline[axis];
            request.Written[axis] = current[axis];
            request.Owned |= bit;
        }
        request.Desired = scale;
        request.Axes = 7;
        request.NativeBaseline = nativeBaseline;
        request.NativeBaselineAxes = 7;
        Reconcile(request, CurrentScope(RequestScope.GreekOnly));
    }

    // Register is deliberately deferred: Worker historically writes 1.175 at enable,
    // then its existing Mover guard uses 1.2. Keep that sequence unchanged.
    internal static void Register(Mover mover, float y)
    {
        if (mover == null) return;
        var request = Get(mover.transform, RequestScope.GreekOnly);
        if (request == null) return;
        request.Desired.y = y;
        request.Axes |= 2;
        request.MoverPointer = mover.Pointer;
        request.MoverId = mover.GetInstanceID();
        request.Mover = mover;
        request.NextLateRetryFrame = 0;
        if (CurrentScope(RequestScope.GreekOnly) != Scope.Active) Reconcile(request, CurrentScope(RequestScope.GreekOnly));
    }

    internal static bool TryGet(Mover mover, out float y)
    {
        y = 1f;
        if (!IsActive || !Find(mover, out var request)) return false;
        y = request.Desired.y;
        return true;
    }

    private static bool Find(Mover mover, out Request request)
    {
        request = null;
        try
        {
            return mover != null && Identify(mover.transform, out var key)
                && Requests.TryGetValue(key, out request)
                && request.MoverPointer == mover.Pointer && request.MoverId == mover.GetInstanceID();
        }
        catch (Exception e) { ReportFailure(e); _pending = true; return false; }
    }

    internal static void Maintain(Mover mover)
    {
        if (Find(mover, out var request)) Reconcile(request, CurrentScope(RequestScope.GreekOnly));
    }

    /// <summary>Repairs late native facing writes on registered, active actor roots only.</summary>
    internal static void MaintainRegisteredY()
    {
        if (_maintainingRegisteredY || CurrentScope(RequestScope.GreekOnly) != Scope.Active) return;
        _maintainingRegisteredY = true;
        try
        {
            // Setters may reenter an adapter and change Requests. Reuse storage and
            // validate each snapshot entry again; a replacement request is a new lease.
            foreach (var request in Requests.Values)
                if (request.MoverPointer != IntPtr.Zero) LateSnapshot.Add(request);
            for (int i = 0; i < LateSnapshot.Count; i++)
            {
                Request request = LateSnapshot[i];
                if (!Requests.TryGetValue(request.Key, out var current) || !ReferenceEquals(current, request)
                    || request.Axes != 2 || (request.Owned & ~2) != 0
                    || (request.Pending != null && (request.Pending.Value.Owned & ~2) != 0)
                    || Time.frameCount < request.NextLateRetryFrame) continue;
                try
                {
                    Mover mover = request.Mover;
                    if (mover == null || mover.Pointer != request.MoverPointer
                        || mover.GetInstanceID() != request.MoverId) continue;
                    Liveness life = Inspect(request.Target, out var targetKey);
                    if (life == Liveness.Deferred)
                    {
                        DeferLate(request);
                        continue;
                    }
                    if (life != Liveness.Alive || targetKey != request.Key
                        || !request.Target.gameObject.activeInHierarchy || !mover.enabled) continue;
                    life = Inspect(mover.transform, out var moverKey);
                    if (life == Liveness.Deferred)
                    {
                        DeferLate(request);
                        continue;
                    }
                    if (life != Liveness.Alive || moverKey != request.Key) continue;
                    Embarkee embarkee = mover.GetComponent<Embarkee>();
                    if (embarkee != null && (embarkee.IsEmbarked || embarkee.IsTargetingEmbarkable)) continue;
                    // Recheck scope at the write boundary after native getters. X/Z
                    // remain the values read by the existing ownership transaction.
                    if (CurrentScope(RequestScope.GreekOnly) != Scope.Active) continue;
                    if (!Reconcile(request, Scope.Active)) DeferLate(request);
                }
                catch (Exception e) { ReportFailure(e); DeferLate(request); }
            }
        }
        finally
        {
            LateSnapshot.Clear();
            _maintainingRegisteredY = false;
        }
    }

    private static void DeferLate(Request request)
    {
        request.NextLateRetryFrame = Time.frameCount + 30;
        _pending = true;
    }

    internal static void Unregister(Mover mover)
    {
        if (!Find(mover, out var request)) return;
        request.MoverPointer = IntPtr.Zero;
        request.MoverId = 0;
        request.Mover = null;
    }

    /// <summary>Explicit removal cancels the request; scope suspension retains it.</summary>
    public static void Restore(Transform target)
    {
        if (!Identify(target, out var key) || !Requests.TryGetValue(key, out var request)) return;
        request.Axes = 0;
        request.MoverPointer = IntPtr.Zero;
        request.MoverId = 0;
        request.Mover = null;
        if (Reconcile(request, Scope.Inactive)) Requests.Remove(key);
    }

    public static Vector3 NativeScale(Transform target)
    {
        Vector3 scale = target.localScale;
        Liveness life = Inspect(target, out var key);
        if (life == Liveness.Deferred) throw new InvalidOperationException("Scale identity is temporarily unavailable");
        if (life != Liveness.Alive || !Requests.TryGetValue(key, out var request)) return scale;
        ResolvePending(request, scale);
        for (int axis = 0; axis < 3; axis++)
            if ((request.Owned & (1 << axis)) != 0 && scale[axis] == request.Written[axis])
                scale[axis] = request.Original[axis];
        return scale;
    }

    private static bool Same(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;

    private static void Commit(Request request, PendingWrite write)
    {
        request.Original = write.Original;
        request.Written = write.Written;
        request.Owned = write.Owned;
    }

    private static void ResolvePending(Request request, Vector3 current)
    {
        if (request.Pending == null) return;
        PendingWrite pending = request.Pending.Value;
        // Facing or another independently owned axis may change before readback.
        // Confirm surviving axes separately, just as normal restoration does.
        for (int axis = 0; axis < 3; axis++)
        {
            if (current[axis] != pending.Result[axis]) continue;
            int bit = 1 << axis;
            request.Original[axis] = pending.Original[axis];
            request.Written[axis] = pending.Written[axis];
            request.Owned = (request.Owned & ~bit) | (pending.Owned & bit);
        }
        request.Pending = null;
    }

    private static bool Reconcile(Request request, Scope scope)
    {
        try
        {
            if (request.OneShot) return ReconcileOneShot(request, scope);
            if (scope == Scope.Unknown) return true; // Loading is not evidence that ownership ended.
            return ReconcileCore(request, scope);
        }
        catch (Exception e) { ReportFailure(e); _pending = true; return false; } // Native teardown/write faults retry on the existing Tick.
    }

    /// <summary>
    /// Corrects one proven coin inheritance and owns nothing afterwards. A fresh
    /// proof replaces the previous baseline; external writes are never replayed.
    /// </summary>
    private static bool ReconcileOneShot(Request request, Scope scope)
    {
        if (scope == Scope.Unknown) return true; // Keep the proof; never write while loading.
        if (scope != Scope.Active) { FinishOneShot(request); return true; } // No restore to the wrong incoming.
        Liveness life = Inspect(request.Target, out var key);
        if (life == Liveness.Deferred) { _pending = true; return false; }
        if (life != Liveness.Alive || key != request.Key) { FinishOneShot(request); return true; }
        Vector3 current = request.Target.localScale;
        // After a failed setter the pending transaction holds the pre-write value.
        // A committed-then-throw write is recognized by current == desired.
        Vector3 original = request.Pending != null ? request.Pending.Value.Original : request.OneShotBefore;
        Vector3 result = current;
        int axes = 0;
        for (int axis = 0; axis < 3; axis++)
        {
            int bit = 1 << axis;
            if ((request.Axes & bit) == 0) continue;
            if (current[axis] == request.Desired[axis]) continue; // Correction already committed.
            if (current[axis] == original[axis])
            {
                axes |= bit;
                result[axis] = request.Desired[axis];
            }
            // Any other value is an external writer; keep it and stop owning this axis.
        }
        request.Axes = axes;
        if (axes == 0) { FinishOneShot(request); return true; }
        var write = new PendingWrite
        {
            Result = result, Original = current, Written = request.Desired, Owned = axes
        };
        request.Pending = write;
        request.Target.localScale = result;
        request.Pending = null;
        FinishOneShot(request);
        return true;
    }

    private static void FinishOneShot(Request request)
    {
        request.Axes = 0;
        request.Owned = 0;
        request.Pending = null;
        request.OneShot = false;
        request.OneShotBefore = default;
    }

    private static bool ReconcileCore(Request request, Scope scope)
    {
        Liveness life = Inspect(request.Target, out var key);
        if (life == Liveness.Deferred) { _pending = true; return false; }
        if (life != Liveness.Alive || key != request.Key) return true;
        Vector3 current = request.Target.localScale;
        // An interop setter can commit and then throw; resolve its uncertain transaction
        // before computing another baseline. A failed read leaves this evidence intact.
        ResolvePending(request, current);
        var write = new PendingWrite { Result = current, Original = request.Original,
            Written = request.Written, Owned = request.Owned };
        for (int axis = 0; axis < 3; axis++)
        {
            int bit = 1 << axis;
            if (scope == Scope.Active && (request.Axes & bit) != 0)
            {
                // Preserve the original before our first write, including across native
                // Mover resets. Never assume that original is one.
                if ((write.Owned & bit) == 0)
                    write.Original[axis] = (request.NativeBaselineAxes & bit) != 0
                        ? request.NativeBaseline[axis] : current[axis];
                write.Result[axis] = request.Desired[axis];
                write.Written[axis] = write.Result[axis];
                write.Owned |= bit;
            }
            else if ((request.Owned & bit) != 0)
            {
                if (current[axis] == request.Written[axis]) write.Result[axis] = request.Original[axis];
                write.Owned &= ~bit; // A surviving external/native write belongs to its writer.
            }
        }
        if (!Same(write.Result, current))
        {
            request.Pending = write;
            request.Target.localScale = write.Result;
            request.Pending = null;
            Commit(request, write);
            if (!_loggedWrite && scope == Scope.Active)
            {
                _loggedWrite = true;
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[ScaleScope] Greek scale applied; first target="
                    + request.Target.gameObject.name + " nativeY=" + write.Original.y + " targetY=" + write.Result.y);
            }
        }
        Commit(request, write);
        return true;
    }

    /// <summary>Called by the existing main-thread panel; no new driver or scene scans.</summary>
    public static void Tick()
    {
#if !ANDROID
        ScaleRegistryHolder.RetryPendingCreation();
#endif
        Scope greek = CurrentScope(RequestScope.GreekOnly);
        Scope enabled = CurrentScope(RequestScope.EnabledAllWorlds);
        bool greekChanged = greek != _lastGreekScope;
        bool enabledChanged = enabled != _lastEnabledScope;
        bool prune = Time.frameCount >= _nextPruneFrame;
        bool retry = _pending && (prune || Time.frameCount >= _nextRetryFrame);
        if (!greekChanged && !enabledChanged && !prune && !retry) return;
        // A persistently inaccessible wrapper must not turn this into an every-frame
        // registry traversal. Scope changes remain immediate; failed work backs off.
        _pending = false;
        _nextRetryFrame = Time.frameCount + 30;
        _lastGreekScope = greek;
        _lastEnabledScope = enabled;
        if (prune) _nextPruneFrame = Time.frameCount + 300;
        var dead = new List<Identity>();
        foreach (var pair in Requests)
        {
            Request request = pair.Value;
            bool allWorlds = request.Scope == RequestScope.EnabledAllWorlds;
            Scope scope = allWorlds ? enabled : greek;
            Liveness life = Inspect(request.Target, out var key);
            if (life == Liveness.Missing || (life == Liveness.Alive && key != pair.Key)) dead.Add(pair.Key);
            else if (life == Liveness.Deferred) _pending = true;
            else if ((allWorlds ? enabledChanged : greekChanged) || retry)
            {
                if (Reconcile(request, scope) && scope != Scope.Unknown && request.Axes == 0
                    && request.Owned == 0 && request.Pending == null)
                    dead.Add(pair.Key);
            }
        }
        foreach (var key in dead) Requests.Remove(key);
    }
}
