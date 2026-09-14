using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>Owns only our scale writes. Scope follows the current world, never an actor's origin.</summary>
internal static class GreekScaleScope
{
    private enum Scope { Unknown, Inactive, Active }
    private enum Liveness { Alive, Missing, Deferred }
    private readonly record struct Identity(IntPtr Pointer, int Id, IntPtr ObjectPointer, int ObjectId);
    private sealed class Request
    {
        internal Transform Target;
        internal Identity Key;
        internal Vector3 Desired, Original, Written;
        internal int Axes, Owned;
        internal Vector3 NativeBaseline;
        internal int NativeBaselineAxes;
        internal IntPtr MoverPointer;
        internal int MoverId;
        internal PendingWrite? Pending;
    }
    private struct PendingWrite
    {
        internal Vector3 Result, Original, Written;
        internal int Owned;
    }

    private static readonly Dictionary<Identity, Request> Requests = new();
    private static Scope _lastScope = Scope.Unknown;
    private static int _nextPruneFrame;
    private static int _nextRetryFrame;
    private static bool _pending;
    private static bool _loggedFailure, _loggedWrite;
    public static bool IsActive => CurrentScope() == Scope.Active;

    private static Scope CurrentScope()
    {
        try
        {
            if (ModConfig.Enabled == null) return Scope.Unknown;
            if (!ModConfig.Enabled.Value) return Scope.Inactive;
            var biome = BiomeHolder.Inst;
            if (biome == null) return Scope.Unknown;
            int index = biome.BiomeIndex;
            if (index < 0) return Scope.Unknown;
            return index == BiomeHolder.GreeceBiomeIndex ? Scope.Active : Scope.Inactive;
        }
        catch (Exception e) { ReportFailure(e); return Scope.Unknown; }
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

    private static Request Get(Transform target)
    {
        if (!Identify(target, out var key)) return null;
        if (!Requests.TryGetValue(key, out var request))
        {
            request = new Request { Target = target, Key = key };
            Requests.Add(key, request);
        }
        return request;
    }

    public static void ApplyY(Transform target, float y)
    {
        var request = Get(target);
        if (request == null) return;
        request.Desired.y = y;
        request.Axes |= 2;
        request.NativeBaselineAxes &= ~2;
        Reconcile(request, CurrentScope());
    }

    public static void ApplyScale(Transform target, Vector3 scale)
    {
        var request = Get(target);
        if (request == null) return;
        request.Desired = scale;
        request.Axes = 7;
        request.NativeBaselineAxes = 0;
        Reconcile(request, CurrentScope());
    }

    /// <summary>
    /// For coin visuals whose initial local scale already inherited our enlarged bag.
    /// The caller supplies a verified native prefab/container baseline; existing owned
    /// axes retain their original. Foreign requests never write this baseline directly.
    /// </summary>
    public static void ApplyScale(Transform target, Vector3 scale, Vector3 nativeBaseline)
    {
        var request = Get(target);
        if (request == null) return;
        request.Desired = scale;
        request.Axes = 7;
        request.NativeBaseline = nativeBaseline;
        request.NativeBaselineAxes = 7;
        Reconcile(request, CurrentScope());
    }

    /// <summary>
    /// The currency adapter proves that an owned parent scale already changed this
    /// child's local scale during reparenting. Adopt only that proven indirect write,
    /// so a loading-to-foreign transition can restore it without a new Greek write.
    /// </summary>
    public static void ApplyInheritedScale(Transform target, Vector3 scale, Vector3 nativeBaseline)
    {
        var request = Get(target);
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
        Reconcile(request, CurrentScope());
    }

    // Register is deliberately deferred: Worker historically writes 1.175 at enable,
    // then its existing Mover guard uses 1.2. Keep that sequence unchanged.
    internal static void Register(Mover mover, float y)
    {
        if (mover == null) return;
        var request = Get(mover.transform);
        if (request == null) return;
        request.Desired.y = y;
        request.Axes |= 2;
        request.MoverPointer = mover.Pointer;
        request.MoverId = mover.GetInstanceID();
        if (CurrentScope() != Scope.Active) Reconcile(request, CurrentScope());
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
        if (Find(mover, out var request)) Reconcile(request, CurrentScope());
    }

    internal static void Unregister(Mover mover)
    {
        if (!Find(mover, out var request)) return;
        request.MoverPointer = IntPtr.Zero;
        request.MoverId = 0;
    }

    /// <summary>Explicit removal cancels the request; scope suspension retains it.</summary>
    public static void Restore(Transform target)
    {
        if (!Identify(target, out var key) || !Requests.TryGetValue(key, out var request)) return;
        request.Axes = 0;
        request.MoverPointer = IntPtr.Zero;
        request.MoverId = 0;
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
        if (scope == Scope.Unknown) return true; // Loading is not evidence that ownership ended.
        try { return ReconcileCore(request, scope); }
        catch (Exception e) { ReportFailure(e); _pending = true; return false; } // Native teardown/write faults retry on the existing Tick.
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
        Scope scope = CurrentScope();
        bool changed = scope != _lastScope;
        bool prune = Time.frameCount >= _nextPruneFrame;
        bool retry = _pending && (prune || Time.frameCount >= _nextRetryFrame);
        if (!changed && !prune && !retry) return;
        // A persistently inaccessible wrapper must not turn this into an every-frame
        // registry traversal. Scope changes remain immediate; failed work backs off.
        _pending = false;
        _nextRetryFrame = Time.frameCount + 30;
        _lastScope = scope;
        if (prune) _nextPruneFrame = Time.frameCount + 300;
        var dead = new List<Identity>();
        foreach (var pair in Requests)
        {
            Liveness life = Inspect(pair.Value.Target, out var key);
            if (life == Liveness.Missing || (life == Liveness.Alive && key != pair.Key)) dead.Add(pair.Key);
            else if (life == Liveness.Deferred) _pending = true;
            else if (changed || retry)
            {
                if (Reconcile(pair.Value, scope) && scope != Scope.Unknown && pair.Value.Axes == 0
                    && pair.Value.Owned == 0 && pair.Value.Pending == null)
                    dead.Add(pair.Key);
            }
        }
        foreach (var key in dead) Requests.Remove(key);
    }
}
