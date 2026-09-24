using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>Eight ghost renderers plus one burst body per knight; no original renderer/material writes or gameplay components.</summary>
internal static class SamuraiDashVisuals
{
    // 残影持续 1 秒（2026-09-24，用户裁定）：幻影淡出总窗从 .2 s 拉到 1 s，与拖尾的
    // PatchRoles_SamuraiPowerDash.SamuraiTrailLifetime 同步。采样密度（间隔/距离）不动。
    private const float Lifetime = 1f, SampleInterval = .04f, SampleDistance = .4f;
    // 2026-09-25 用户裁定（八道不同姿态白光）：8 槽全部用已实机验证可见的标准白配方
// （recipe C），沿冲刺路径逐格定格不同拔刀姿态，1s 内保持并淡出。
private const int GhostSlots = 8;
private static float GhostOpacity(int rankFromNewest)
{
    // newest ~0.55 → oldest ~0.10：清晰的近端、可辨的远端
    return Mathf.Max(.10f, .55f - .0625f * rankFromNewest);
}
    private static readonly Dictionary<int, OwnerState> Owners = new();
    private static readonly List<int> Retire = new();
    private static readonly HashSet<string> Logged = new();
    private static SamuraiDashVisualsDriver Driver;
    private static int OverlayId, FlashId;
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
        internal Ghost[] Ghosts = new Ghost[GhostSlots];
        internal Token Current;
        internal SamuraiDashDiagnostics.Trace Diagnostics;
        internal string RetireReason;
        internal bool Emitting, Tail;
        internal int NextSlot;
        internal int Samples;
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
    // 2026-09-25 审查 P1-2：着色器降级的一次性日志（镜像上面 Logged 门），每级一条、
    // 内容标明降级到了哪级（unlit-texture / source-clone）。
    private static void LogShaderFallback(string level, string detail)
    {
        if (!Logged.Add("shader-fallback:" + level)) return;
        try { KingdomEnhancedPlugin.Instance?.LogSource.LogWarning("[SamuraiDashVisuals] shader-fallback level=" + level + ": " + detail); }
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
    // 2026-09-24 残影盲审的 A/B/C 三配方诊断已定案（2026-09-25 用户裁定八道白光）：C 胜出，
    // 8 槽全部+body 走 recipe 2（C=Sprites/Default 标准材质+原贴图+纯白顶点色，ZWrite 关）
    // = 当前生产路径。A=源材质拷贝+_Overlay 白（旧线上配方）、B=A+Flash 浮点 1+FLASH_ON
    // 关键字，两臂代码保留未调用，仅作历史诊断记录。
    private static SpriteRenderer MakeRenderer(GameObject root, SpriteRenderer source, string name, int recipe)
    {
        var go = new GameObject(name);
        try
        {
            go.transform.SetParent(root.transform, false);
            go.layer = source.gameObject.layer;
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.enabled = false;
            if (recipe == 2)
            {
                // 2026-09-25 审查 P1-2：三级降级链——IL2CPP 剥离未引用着色器时不再静默退化成
                // 源调色板克隆。Sprites/Default → Unlit/Texture → 源材质克隆（保底，行为同旧两级链）。
                // Unlit/Texture 无 _Overlay/顶点色乘法能力，第三级本就是最后手段，不做额外补偿。
                // 每级降级各一条一次性日志（LogShaderFallback，内容含降级到了哪级）。
                Shader standard = Shader.Find("Sprites/Default");
                Material plain;
                if (standard != null)
                    plain = new Material(standard);
                else
                {
                    Shader unlit = Shader.Find("Unlit/Texture");
                    if (unlit != null)
                    {
                        LogShaderFallback("unlit-texture",
                            "Sprites/Default missing; ghost material degraded to Unlit/Texture (no _Overlay/vertex-color tint)");
                        plain = new Material(unlit);
                    }
                    else
                    {
                        LogShaderFallback("source-clone",
                            "Sprites/Default and Unlit/Texture missing; ghost material degraded to a source-palette clone");
                        plain = new Material(source.sharedMaterial);
                    }
                }
                renderer.sharedMaterial = plain;
                return renderer;
            }
            Material overlay = new Material(source.sharedMaterial);
            overlay.SetColor(OverlayId, Color.white);
            if (recipe == 1)
            {
                overlay.SetFloat(FlashId, 1f);
                overlay.EnableKeyword("FLASH_ON");
            }
            renderer.sharedMaterial = overlay;
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
            for (int i = 0; i < GhostSlots; i++)
                s.Ghosts[i] = new Ghost { Renderer = MakeRenderer(s.Root, source, "Ghost" + i, 2) };
            s.Body = MakeRenderer(s.Root, source, "BurstWhite", 2);
            return s;
        }
        catch { if (s.Root != null) UnityEngine.Object.Destroy(s.Root); throw; }
    }
    private static void Pose(SpriteRenderer renderer, SpriteRenderer source, bool body)
    {
        renderer.sprite = source.sprite;
        // 勿再重指 sharedMaterial：会顶掉 MakeRenderer 实例化材质上的 _Overlay 白色叠加
        //（OwnerState 每武士构建一次，创建时已随源拷贝调色板，材质实例稳定）。
        renderer.flipX = source.flipX;
        renderer.flipY = source.flipY;
        renderer.gameObject.layer = source.gameObject.layer;
        renderer.sortingLayerID = source.sortingLayerID;
        int order = source.sortingOrder;
        // 2026-09-24 盲审 P2②：残影原画在 order-1=人群之下，即使白色也被后绘单位盖住。
        // 抬升：幻影 order+2（人群上、本体闪白之下）、body order+3——A/B/C 诊断臂全部
        // 换到可见层级，定案后终版沿用。
        renderer.sortingOrder = body ? (order == int.MaxValue ? order : order + 3)
            : (order == int.MinValue ? order : order + 2);
        renderer.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
        renderer.transform.localScale = source.transform.lossyScale; // Root is world identity; keep signed facing.
    }
    private static void Emit(OwnerState s, float now)
    {
        var ghost = s.Ghosts[s.NextSlot];
        Pose(ghost.Renderer, s.Source, false);
        ghost.Born = now;
        ghost.Alive = s.Tail = true;
        ghost.Renderer.color = new Color(1, 1, 1, GhostOpacity(0));
        ghost.Renderer.enabled = true;
        s.Samples++; // Count only samples whose renderer was successfully enabled.
        s.NextSlot = (s.NextSlot + 1) % GhostSlots;
        s.LastX = s.Source.transform.position.x;
        s.NextSample = now + SampleInterval;
    }
    private static void Hide(OwnerState s, string reason = "replaced")
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
        LogVisual(s, "visual-cleared", reason);
        s.Diagnostics = null;
    }
    private static void Remove(int id, OwnerState s)
    {
        if (Owners.TryGetValue(id, out var current) && ReferenceEquals(current, s)) Owners.Remove(id);
        Hide(s, s.RetireReason ?? "owner-or-renderer-replaced");
        if (s.Root != null) UnityEngine.Object.Destroy(s.Root);
    }

    private static void CompleteTailDiagnostic(OwnerState s)
    {
        if (s.Diagnostics == null || s.Emitting || s.Tail) return;
        if (!s.Diagnostics.TailLogged)
        { s.Diagnostics.TailLogged = true; LogVisual(s, "tail-cleared", "all-ghosts-expired"); }
        s.Diagnostics = null;
    }

    private static void LogSourceSkip(SamuraiDashDiagnostics.Trace trace, SpriteRenderer source)
    {
        if (trace == null) return;
        try
        {
            string reason = source == null ? "missing-source" : source.gameObject == null ? "missing-source-object"
                : !source.gameObject.activeInHierarchy ? "source-inactive" : !source.enabled ? "source-disabled"
                : source.sprite == null ? "missing-sprite" : source.sharedMaterial == null ? "missing-material"
                : !source.sharedMaterial.HasProperty(OverlayId) ? "missing-overlay-property" : "source-changed";
            SamuraiDashDiagnostics.Write(trace, "visual-skipped", "reason=" + reason);
        }
        catch (Exception e) { SamuraiDashDiagnostics.Write(trace, "visual-skipped", "reason=state-read-failed type=" + e.GetType().Name); }
    }

    private static void LogVisual(OwnerState s, string eventName, string reason)
    {
        if (s.Diagnostics == null) return;
        try
        {
            int enabledGhosts = 0;
            foreach (var ghost in s.Ghosts)
                if (ghost != null && ghost.Renderer != null && ghost.Renderer.enabled) enabledGhosts++;
            var body = s.Body;
            var source = s.Source;
            string details = "reason=" + reason + " elapsed=" + (Time.time - s.Diagnostics.StartedAt).ToString("0.###")
                + " ghostSamples=" + s.Samples + " emitting=" + s.Emitting + " ghostSlots=" + GhostSlots + " enabledGhosts=" + enabledGhosts
                + " whitePresent=" + (body != null) + " whiteEnabled=" + (body != null && body.enabled);
            if (source != null)
            {
                details += " sourceEnabled=" + source.enabled + " sprite=" + (source.sprite != null ? source.sprite.name : "null")
                    + " sourceLayer=" + source.sortingLayerID + " sourceOrder=" + source.sortingOrder;
                var material = source.sharedMaterial;
                details += " material=" + (material != null ? material.name : "null")
                    + " shader=" + (material != null && material.shader != null ? material.shader.name : "null");
            }
            if (eventName == "visual-first-update" && s.Owner != null)
            {
                var trail = s.Owner._trail;
                details += " trailPresent=" + (trail != null);
                if (trail != null) details += " trailEnabled=" + trail.enabled + " trailEmitting=" + trail.emitting + " trailPoints=" + trail.positionCount;
            }
            SamuraiDashDiagnostics.Write(s.Diagnostics, eventName, details);
        }
        catch (Exception e) { SamuraiDashDiagnostics.Write(s.Diagnostics, eventName, "state-read-failed=" + e.GetType().Name); }
    }

    internal static Token Begin(Knight owner, SamuraiDashDiagnostics.Trace diagnostics = null)
    {
        OwnerState state = null;
        int id = 0;
        try
        {
            if (!Valid(owner)) { SamuraiDashDiagnostics.Write(diagnostics, "visual-skipped", "reason=invalid-owner"); return null; }
            if (Time.timeScale <= 0) { SamuraiDashDiagnostics.Write(diagnostics, "visual-skipped", "reason=paused"); return null; }
            if (Time.time < RetryAt) { SamuraiDashDiagnostics.Write(diagnostics, "visual-skipped", "reason=retry-backoff"); return null; }
            if (!OverlayIdReady) { OverlayId = Shader.PropertyToID("_Overlay"); FlashId = Shader.PropertyToID("_Flash"); OverlayIdReady = true; }
            id = owner.gameObject.GetInstanceID();
            var source = owner.GetComponent<SpriteRenderer>();
            if (!SourceReady(source))
            {
                RetryAt = Time.time + 5f;
                Log("source-overlay-unavailable", new InvalidOperationException());
                LogSourceSkip(diagnostics, source);
                return null;
            }
            EnsureDriver();
            if (Owners.TryGetValue(id, out state) && (!Same(state.Owner, owner) || state.Root == null || !Same(state.Source, source)))
            { Remove(id, state); state = null; }
            if (state == null) { state = Build(owner, source); Owners[id] = state; }
            Hide(state);
            state.Diagnostics = diagnostics;
            state.Samples = 0;
            state.RetireReason = null;
            var token = new Token { Owner = owner, Id = id };
            state.Current = token;
            state.Emitting = true;
            Emit(state, Time.time);
            Pose(state.Body, source, true);
            state.Body.color = new Color(1, 1, 1, .85f);
            state.Body.enabled = true;
            LogVisual(state, "visual-ready", "created-or-reused; renderer-state-not-screen-proof");
            if (Logged.Add("ready"))
            {
                try { KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[SamuraiVisuals] ready: 8 ghosts alpha=.55->.10 (-.0625/rank, floor .10) lifetime=" + Lifetime.ToString("0.##") + "s; body overlay follows burst"); }
                catch { }
            }
            return token;
        }
        catch (Exception e)
        {
            RetryAt = Time.time + 5f;
            if (state != null) { try { Remove(id, state); } catch { } }
            SamuraiDashDiagnostics.Write(diagnostics, "visual-skipped", "reason=exception type=" + e.GetType().Name);
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
            if (immediate) Hide(s, "immediate");
            else
            {
                LogVisual(s, "visual-stop", "burst-ended; remaining-ghosts-fade");
                CompleteTailDiagnostic(s);
            }
        }
        catch (Exception e) { Log("end", e); }
    }
    internal static void Clear(Knight owner)
    {
        try
        {
            if (owner == null || owner.gameObject == null) return;
            int id = owner.gameObject.GetInstanceID();
            if (Owners.TryGetValue(id, out var s) && Same(s.Owner, owner))
            { s.RetireReason = "owner-disabled-or-cleared"; Remove(id, s); }
        }
        catch (Exception e) { Log("clear", e); }
    }
    internal static void ClearAll()
    {
        var snapshot = new List<OwnerState>(Owners.Values);
        Owners.Clear();
        foreach (var s in snapshot)
        {
            try { Hide(s, "driver-disabled-or-destroyed"); if (s.Root != null) UnityEngine.Object.Destroy(s.Root); }
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
                if (!Valid(s.Owner) || s.Root == null) { s.RetireReason = "owner-invalid-or-root-gone"; Retire.Add(pair.Key); continue; }
                if (!s.Emitting && !s.Tail) continue; // No renderer/material/property reads for an idle owner.
                if (!SourceReady(s.Source)) { s.RetireReason = "source-unavailable"; Retire.Add(pair.Key); continue; }
                if (Time.timeScale <= 0) continue;
                float now = Time.time;
                if (s.Emitting)
                {
                    Pose(s.Body, s.Source, true);
                    if (now >= s.NextSample && Mathf.Abs(s.Source.transform.position.x - s.LastX) >= SampleDistance) Emit(s, now);
                }
                s.Tail = false;
                int rank = 0;
                for (int n = 0; n < GhostSlots; n++)
                {
                    var ghost = s.Ghosts[(s.NextSlot + GhostSlots - 1 - n) % GhostSlots];
                    if (!ghost.Alive) continue;
                    float remaining = Mathf.Clamp01(1f - (now - ghost.Born) / Lifetime);
                    ghost.Renderer.color = new Color(1, 1, 1, GhostOpacity(rank++) * remaining);
                    if (remaining <= 0) { ghost.Alive = false; ghost.Renderer.enabled = false; }
                    else s.Tail = true;
                }
                if (s.Diagnostics != null)
                {
                    if (!s.Diagnostics.FirstUpdateLogged)
                    { s.Diagnostics.FirstUpdateLogged = true; LogVisual(s, "visual-first-update", "existing-LateUpdate-ran"); }
                    CompleteTailDiagnostic(s);
                }
            }
            catch (Exception e) { s.RetireReason = "tick-exception:" + e.GetType().Name; Retire.Add(pair.Key); Log("tick", e); }
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
