using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>Four owned renderers per knight; no original renderer/material writes or gameplay components.</summary>
internal static class SamuraiDashVisuals
{
    private const float Lifetime = .2f, SampleInterval = .04f, SampleDistance = .4f;
    private static readonly float[] Opacity = { .45f, .25f, .10f };
    private static readonly Dictionary<int, OwnerState> Owners = new();
    private static readonly List<int> Retire = new();
    private static readonly HashSet<string> Logged = new();
    private static SamuraiDashVisualsDriver Driver;
    private static int OverlayId;
    private static bool OverlayIdReady;
    private static float RetryAt;

    internal sealed class Token
    {
        internal Knight Owner;
        internal int Id;
    }
    private sealed class Ghost
    {
        internal SpriteRenderer Renderer;
        internal float Born;
        internal bool Alive;
    }
    private sealed class OwnerState
    {
        internal Knight Owner;
        internal SpriteRenderer Source, Body;
        internal GameObject Root;
        internal Ghost[] Ghosts = new Ghost[3];
        internal Token Current;
        internal bool Emitting, Tail;
        internal int NextSlot;
        internal float NextSample, LastX;
    }
    private static bool Same(UnityEngine.Object a, UnityEngine.Object b) => a != null && b != null && a.Pointer == b.Pointer;
    private static bool Valid(Knight owner) => ModConfig.Enabled.Value && owner != null && owner.gameObject != null &&
        owner.gameObject.activeInHierarchy && owner._damageable != null && !owner._damageable.isDead &&
        PatchRoles_KnightStyle.TryGetResolvedStyleIndex(owner, out int style) && style == 2;
    private static void Log(string key, Exception e)
    {
        if (!Logged.Add(key)) return;
        try { KingdomEnhancedPlugin.Instance?.LogSource.LogWarning("[SamuraiVisuals] " + key + ": " + e.GetType().Name); }
        catch { }
    }
    private static bool SourceReady(SpriteRenderer source) => source != null && source.gameObject != null &&
        source.gameObject.activeInHierarchy && source.enabled && source.sprite != null && source.sharedMaterial != null &&
        source.sharedMaterial.HasProperty(OverlayId);

    private static void EnsureDriver()
    {
        if (Driver != null && Driver.gameObject != null) return;
        GameObject go = null;
        try
        {
            if (!ClassInjector.IsTypeRegisteredInIl2Cpp(typeof(SamuraiDashVisualsDriver)))
                ClassInjector.RegisterTypeInIl2Cpp<SamuraiDashVisualsDriver>();
            go = new GameObject("KEM_SamuraiVisualDriver");
            var driver = go.AddComponent<SamuraiDashVisualsDriver>();
            UnityEngine.Object.DontDestroyOnLoad(go);
            Driver = driver;
        }
        catch { if (go != null) UnityEngine.Object.Destroy(go); throw; }
    }
    private static SpriteRenderer MakeRenderer(GameObject root, SpriteRenderer source, string name)
    {
        var go = new GameObject(name);
        try
        {
            go.transform.SetParent(root.transform, false);
            go.layer = source.gameObject.layer;
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.enabled = false;
            renderer.sharedMaterial = source.sharedMaterial;
            var block = new MaterialPropertyBlock();
            block.SetColor(OverlayId, Color.white);
            renderer.SetPropertyBlock(block); // Block is copied; no source block/material mutation.
            return renderer;
        }
        catch { UnityEngine.Object.Destroy(go); throw; }
    }
    private static OwnerState Build(Knight owner, SpriteRenderer source)
    {
        var s = new OwnerState { Owner = owner, Source = source };
        try
        {
            // Unparented scene object with identity transform: every child has an independent frozen world pose.
            // Unlike the small driver this root is not DontDestroyOnLoad.
            s.Root = new GameObject("KEM_SamuraiAfterimages");
            for (int i = 0; i < 3; i++) s.Ghosts[i] = new Ghost { Renderer = MakeRenderer(s.Root, source, "Ghost" + i) };
            s.Body = MakeRenderer(s.Root, source, "BurstWhite");
            return s;
        }
        catch { if (s.Root != null) UnityEngine.Object.Destroy(s.Root); throw; }
    }
    private static void Pose(SpriteRenderer renderer, SpriteRenderer source, bool body)
    {
        renderer.sprite = source.sprite;
        renderer.sharedMaterial = source.sharedMaterial;
        renderer.flipX = source.flipX;
        renderer.flipY = source.flipY;
        renderer.gameObject.layer = source.gameObject.layer;
        renderer.sortingLayerID = source.sortingLayerID;
        int order = source.sortingOrder;
        renderer.sortingOrder = body ? (order == int.MaxValue ? order : order + 1) : (order == int.MinValue ? order : order - 1);
        renderer.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
        renderer.transform.localScale = source.transform.lossyScale; // Root is world identity; keep signed facing.
    }
    private static void Emit(OwnerState s, float now)
    {
        var ghost = s.Ghosts[s.NextSlot];
        Pose(ghost.Renderer, s.Source, false);
        ghost.Born = now;
        ghost.Alive = s.Tail = true;
        ghost.Renderer.color = new Color(1, 1, 1, Opacity[0]);
        ghost.Renderer.enabled = true;
        s.NextSlot = (s.NextSlot + 1) % 3;
        s.LastX = s.Source.transform.position.x;
        s.NextSample = now + SampleInterval;
    }
    private static void Hide(OwnerState s)
    {
        s.Current = null;
        s.Emitting = s.Tail = false;
        if (s.Body != null) s.Body.enabled = false;
        foreach (var ghost in s.Ghosts)
        {
            if (ghost == null) continue;
            ghost.Alive = false;
            if (ghost.Renderer != null) ghost.Renderer.enabled = false;
        }
    }
    private static void Remove(int id, OwnerState s)
    {
        if (Owners.TryGetValue(id, out var current) && ReferenceEquals(current, s)) Owners.Remove(id);
        Hide(s);
        if (s.Root != null) UnityEngine.Object.Destroy(s.Root);
    }

    internal static Token Begin(Knight owner)
    {
        OwnerState state = null;
        int id = 0;
        try
        {
            if (!Valid(owner) || Time.timeScale <= 0 || Time.time < RetryAt) return null;
            if (!OverlayIdReady) { OverlayId = Shader.PropertyToID("_Overlay"); OverlayIdReady = true; }
            id = owner.gameObject.GetInstanceID();
            var source = owner.GetComponent<SpriteRenderer>();
            if (!SourceReady(source))
            {
                RetryAt = Time.time + 5f;
                Log("source-overlay-unavailable", new InvalidOperationException());
                return null;
            }
            EnsureDriver();
            if (Owners.TryGetValue(id, out state) && (!Same(state.Owner, owner) || state.Root == null || !Same(state.Source, source)))
            { Remove(id, state); state = null; }
            if (state == null) { state = Build(owner, source); Owners[id] = state; }
            Hide(state);
            var token = new Token { Owner = owner, Id = id };
            state.Current = token;
            state.Emitting = true;
            Emit(state, Time.time);
            Pose(state.Body, source, true);
            state.Body.color = new Color(1, 1, 1, .85f);
            state.Body.enabled = true;
            if (Logged.Add("ready"))
            {
                try { KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[SamuraiVisuals] ready: 3 ghosts alpha=.45/.25/.10 lifetime=.2s; body overlay follows burst"); }
                catch { }
            }
            return token;
        }
        catch (Exception e)
        {
            RetryAt = Time.time + 5f;
            if (state != null) { try { Remove(id, state); } catch { } }
            Log("begin", e);
            return null;
        }
    }
    internal static void End(Token token, bool immediate = false)
    {
        try
        {
            if (token == null || !Owners.TryGetValue(token.Id, out var s) ||
                !Same(s.Owner, token.Owner) || !ReferenceEquals(s.Current, token)) return;
            s.Current = null;
            s.Emitting = false;
            if (s.Body != null) s.Body.enabled = false;
            if (immediate) Hide(s);
        }
        catch (Exception e) { Log("end", e); }
    }
    internal static void Clear(Knight owner)
    {
        try
        {
            if (owner == null || owner.gameObject == null) return;
            int id = owner.gameObject.GetInstanceID();
            if (Owners.TryGetValue(id, out var s) && Same(s.Owner, owner)) Remove(id, s);
        }
        catch (Exception e) { Log("clear", e); }
    }
    internal static void ClearAll()
    {
        var snapshot = new List<OwnerState>(Owners.Values);
        Owners.Clear();
        foreach (var s in snapshot)
        {
            try { Hide(s); if (s.Root != null) UnityEngine.Object.Destroy(s.Root); }
            catch (Exception e) { Log("clear-all", e); }
        }
    }
    internal static void Tick()
    {
        if (Owners.Count == 0) return;
        Retire.Clear();
        foreach (var pair in Owners)
        {
            var s = pair.Value;
            try
            {
                // Cleanup gates precede pause: disabling the mod or despawning never leaves a white actor.
                if (!Valid(s.Owner) || s.Root == null) { Retire.Add(pair.Key); continue; }
                if (!s.Emitting && !s.Tail) continue; // No renderer/material/property reads for an idle owner.
                if (!SourceReady(s.Source)) { Retire.Add(pair.Key); continue; }
                if (Time.timeScale <= 0) continue;
                float now = Time.time;
                if (s.Emitting)
                {
                    Pose(s.Body, s.Source, true);
                    if (now >= s.NextSample && Mathf.Abs(s.Source.transform.position.x - s.LastX) >= SampleDistance) Emit(s, now);
                }
                s.Tail = false;
                int rank = 0;
                for (int n = 0; n < 3; n++)
                {
                    var ghost = s.Ghosts[(s.NextSlot + 2 - n + 3) % 3];
                    if (!ghost.Alive) continue;
                    float remaining = Mathf.Clamp01(1f - (now - ghost.Born) / Lifetime);
                    ghost.Renderer.color = new Color(1, 1, 1, Opacity[rank++] * remaining);
                    if (remaining <= 0) { ghost.Alive = false; ghost.Renderer.enabled = false; }
                    else s.Tail = true;
                }
            }
            catch (Exception e) { Retire.Add(pair.Key); Log("tick", e); }
        }
        foreach (int id in Retire)
            if (Owners.TryGetValue(id, out var s)) { try { Remove(id, s); } catch (Exception e) { Log("retire", e); } }
    }
}

public sealed class SamuraiDashVisualsDriver : MonoBehaviour
{
    public SamuraiDashVisualsDriver(IntPtr pointer) : base(pointer) { }
    private void LateUpdate() => SamuraiDashVisuals.Tick();
    private void OnDisable() => SamuraiDashVisuals.ClearAll();
    private void OnDestroy() => SamuraiDashVisuals.ClearAll();
}
