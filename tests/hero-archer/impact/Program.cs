// Regression suite for the archer impact visual module (PatchArcher_Impact).
//
// The production source is linked in via -p:ProductionSource. The harness drives
// the real Harmony wrapper methods by reflection and mirrors native
// Arrow.HitObject between prefix and postfix, so the acceptance evidence the
// module relies on is the native one:
//   * native early-return  -> _hasHit stays false -> postfix sees no acceptance
//   * native return        -> _hasHit true        -> burst allowed
//   * native throw         -> postfix is skipped  -> no burst, no leak
// Unity types are stubbed (Stubs.cs); nothing here is a real-game assertion.
// The visual contract under test is the real DLL's pixel-fire (5x5 grid mesh +
// Perlin pixel snapping + growth/fade), not the older LineRenderer ring.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using KingdomEnhancedMod;
using UnityEngine;

internal static class Program
{
    private static int Passed, Failed;
    private static Type Production => typeof(PatchArcher_Impact);
    private static Type HookType;
    private static MethodInfo PrefixMethod, PostfixMethod;
    private static Dictionary<FieldInfo, object> InitialFields;
    private static Material TrailMaterial;
    private static int NativeRuns;
    private static Action<GameObject> DuringNativeHit;
    private static readonly List<string> Failures = new();

    private static void Main()
    {
        InitialFields = Production.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .ToDictionary(f => f, f => f.GetValue(null));
        HookType = Production.Assembly.GetTypes().FirstOrDefault(t => t.GetCustomAttributes<HarmonyLib.HarmonyPatch>()
            .Any(a => a.TargetType == typeof(Arrow) && a.MethodName == "HitObject"));
        if (HookType == null) throw new Exception("missing Arrow.HitObject impact hook host");
        PrefixMethod = HookType.GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
        PostfixMethod = HookType.GetMethod("Postfix", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
        TrailMaterial = new Material(Shader.Find("Sprites/Default"));
        TrailMaterial.color = new Color(.25f, .5f, .75f, 1f);
        TrailMaterial.mainTexture = null;

        HookSurface();
        NativeAcceptance();
        PixelFireVisuals();
        HeroOriginFx();
        Budgets();
        PoolAndFailures();

        Console.WriteLine($"RESULT {Passed} passed, {Failed} failed");
        foreach (string failure in Failures) Console.WriteLine("FAILURE: " + failure);
        Environment.Exit(Failed == 0 ? 0 : 1);
    }

    // ============================================================
    // Harness
    // ============================================================

    private static void Eq<T>(T want, T got, string why)
    {
        if (!EqualityComparer<T>.Default.Equals(want, got)) throw new Exception($"{why}: expected {want}, got {got}");
    }

    private static void Check(bool condition, string why) { if (!condition) throw new Exception(why); }

    private static void Near(float want, float got, string why, float tolerance = .001f)
    {
        if (MathF.Abs(want - got) > tolerance)
            throw new Exception($"{why}: expected {want}, got {got}");
    }

    private static void Same(Vector3 want, Vector3 got, string why)
    {
        if (MathF.Abs(want.x - got.x) > .001f || MathF.Abs(want.y - got.y) > .001f || MathF.Abs(want.z - got.z) > .001f)
            throw new Exception($"{why}: expected ({want.x},{want.y},{want.z}), got ({got.x},{got.y},{got.z})");
    }

    private static void SameColor(Color want, Color got, string why)
    {
        if (MathF.Abs(want.r - got.r) > .0001f || MathF.Abs(want.g - got.g) > .0001f
            || MathF.Abs(want.b - got.b) > .0001f || MathF.Abs(want.a - got.a) > .0001f)
            throw new Exception($"{why}: expected ({want.r},{want.g},{want.b},{want.a}), got ({got.r},{got.g},{got.b},{got.a})");
    }

    private static void Test(string name, Action action)
    {
        Setup();
        try
        {
            action();
            Passed++;
            Console.WriteLine("PASS " + name);
        }
        catch (Exception e)
        {
            Failed++;
            Failures.Add(name + ": " + e.GetBaseException().Message);
            Console.WriteLine("FAIL " + name + ": " + e.GetBaseException().Message);
        }
    }

    private static KingdomEnhancedPlugin.LogSink Log => KingdomEnhancedPlugin.Instance.LogSource;

    private static void Setup()
    {
        PatchArcher_GreekImpact.Eligible = true;
        PatchArcher_GreekImpact.HeroOrigin = false;
        PatchArcher_GreekImpact.HeroEnabled = false;
        // Leave the previous test through the module's own disable path, then restore module statics.
        ModConfig.ArcherImpactEnabled.Value = false;
        try { PatchArcher_Impact.Tick(); } catch { }
        foreach (FieldInfo field in Production.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
        {
            object value = field.GetValue(null);
            if (value is System.Collections.IDictionary dictionary) dictionary.Clear();
            else if (value is HashSet<string> strings) strings.Clear();
            else if (!field.IsLiteral && !field.IsInitOnly) field.SetValue(null, InitialFields[field]);
        }

        foreach (GameObject go in UnityEngine.Object.Created.OfType<GameObject>()) go.DestroyDeep();
        UnityEngine.Object.Created.Clear();
        TrailMaterial.Destroyed = false;
        TrailMaterial.InstanceWrites = 0;
        UnityEngine.Object.DestroyedObjects.Clear();
        UnityEngine.Object.DestroyRequests = 0;
        UnityEngine.Object.DoubleDestroys = 0;
        GameObject.Activations.Clear();
        GameObject.SetActiveTrueCalls = 0;
        Renderer.SortingWrites = 0;
        Renderer.ThrowOnSortingWrite = false;
        Transform.ThrowOnSetParent = false;
        Material.CreatedCount = 0;
        Shader.Finds = 0;
        Shader.FindLog.Clear();
        Shader.Available = true;
        Shader.BlockPrimary = false;
        Quaternion.EulerCalls = 0;
        Texture2D.CreatedCount = 0;
        Texture2D.SetPixelCalls = 0;
        Texture2D.ApplyCalls = 0;
        Texture2D.ThrowOnSetPixel = false;
        Texture2D.ThrowOnApply = false;
        Mesh.RejectedGeometryWrites = 0;
        DuringNativeHit = null;
        Mesh.CreatedCount = 0;
        Mesh.VertexUploads = 0;
        Mesh.UvWrites = 0;
        Mesh.TriangleWrites = 0;
        Mesh.BoundsRecalculations = 0;
        Mesh.NormalRecalculations = 0;
        Mesh.ThrowOnVertexWrite = false;
        Mesh.ThrowOnUvWrite = false;
        Mesh.ThrowOnTriangleWrite = false;
        MeshFilter.MeshInstantiations = 0;
        Il2CppStructArray<Vector3>.Allocations = 0;
        Il2CppStructArray<Vector3>.ManagedCopies = 0;
        ParticleSystem.Plays = 0;
        ParticleSystem.MainAccesses = 0;
        ParticleSystem.ThrowOnPlay = false;
        Rigidbody2D.TorqueCalls = 0;
        Pool.DespawnCalls = 0;
        Pool.LastDespawnDelay = 0f;
        UnityEngine.Random.Calls = 0;
        Time.time = 1f;
        Time.frameCount = 1;
        Time.timeScale = 1f;
        NativeRuns = 0;
        Log.Warnings.Clear();
        Log.Infos.Clear();
        Log.Errors.Clear();
        UnityEngine.Object.Created.Add(TrailMaterial);

        NetworkBigBoss.HasWorldAuth = true;
        ModConfig.ArcherImpactEnabled.Value = true;
        ArcherOptionsScope.Active = true;
        ArcherOptionsScope.World = new GameObject("World");
        ArcherOptionsScope.Scene = 1;
        ArcherOptionsScope.Layer = new GameObject("WorldLayer");
        PatchArcher_Impact.Tick(); // idle tick: silent, allocation free
        ResetCounts();
    }

    /// <summary>Zero observability counters after fixture construction so assertions measure production work only.</summary>
    private static void ResetCounts()
    {
        GameObject.Activations.Clear();
        GameObject.SetActiveTrueCalls = 0;
        UnityEngine.Object.DestroyRequests = 0;
        UnityEngine.Object.DoubleDestroys = 0;
        UnityEngine.Object.DestroyedObjects.Clear();
        Renderer.SortingWrites = 0;
        Material.CreatedCount = 0;
        Shader.Finds = 0;
        Shader.FindLog.Clear();
        Quaternion.EulerCalls = 0;
        Texture2D.CreatedCount = 0;
        Texture2D.SetPixelCalls = 0;
        Texture2D.ApplyCalls = 0;
        Mesh.RejectedGeometryWrites = 0;
        Mesh.CreatedCount = 0;
        Mesh.VertexUploads = 0;
        Mesh.UvWrites = 0;
        Mesh.TriangleWrites = 0;
        Mesh.BoundsRecalculations = 0;
        MeshFilter.MeshInstantiations = 0;
        Il2CppStructArray<Vector3>.Allocations = 0;
        Il2CppStructArray<Vector3>.ManagedCopies = 0;
        ParticleSystem.Plays = 0;
        ParticleSystem.MainAccesses = 0;
        Rigidbody2D.TorqueCalls = 0;
        Pool.DespawnCalls = 0;
        UnityEngine.Random.Calls = 0;
        NativeRuns = 0;
        Log.Warnings.Clear();
    }

    private static void Tick(float delta = 0f)
    {
        if (delta > 0f) { Time.time += delta; Time.frameCount++; }
        PatchArcher_Impact.Tick();
    }

    private static void Advance(float delta, float step = .05f)
    {
        float target = Time.time + delta;
        while (Time.time < target - .0001f)
        {
            Time.time = MathF.Min(target, Time.time + step);
            Time.frameCount++;
            PatchArcher_Impact.Tick();
        }
    }

    /// <summary>Prefix -> native Arrow.HitObject -> postfix, with Harmony's skip-on-throw semantics.</summary>
    private static void NativeHit(Arrow arrow, GameObject target, bool physicalHit = false)
    {
        object[] prefixArgs = { arrow, target, physicalHit, null };
        try { PrefixMethod.Invoke(null, prefixArgs); }
        catch (TargetInvocationException e) { throw e.InnerException ?? e; }
        object state = prefixArgs[3];
        NativeHitObject(arrow, target, physicalHit); // native throw propagates: postfix skipped, as in Harmony
        try { PostfixMethod.Invoke(null, new object[] { arrow, state }); }
        catch (TargetInvocationException e) { throw e.InnerException ?? e; }
    }

    // -------- mirrored native Arrow.HitObject (author-Arrow.cs, observation only) --------

    private static void NativeHitObject(Arrow arrow, GameObject target, bool physicalHit)
    {
        NativeRuns++;
        if (arrow._hasHit || !NetworkBigBoss.HasWorldAuth) return;
        float despawnDelay = 2f;
        bool bounce = false, crusherHit = false;
        Wall wall = target.GetComponentInParent<Wall>();
        Damageable damageable = target.GetComponentInParent<Damageable>();
        Crusher crusher = target.GetComponentInParent<Crusher>();
        if (physicalHit && wall != null)
        {
            arrow.wallHitSound.Play(arrow.transform.position, false, false, false);
            if (wall.isIntact) bounce = UnityEngine.Random.value < .5f;
            TryDamage(arrow, damageable);
        }
        else if (crusher != null && !crusher.IsStunned)
        {
            arrow.wallHitSound.Play(arrow.transform.position, false, false, false);
            crusherHit = true;
        }
        else if (damageable != null && damageable.IsDamagedBy(arrow._damageSource))
        {
            TryDamage(arrow, damageable);
            if (damageable.surface == DamageSurface.Structure) { arrow.wallHitSound.Play(arrow.transform.position, false, false, false); bounce = true; }
            else despawnDelay = .2f;
        }
        else
        {
            if (!target.CompareTag("Ground")) return;
            arrow.groundHitSound.Play(arrow.transform.position, false, false, false);
        }

        if (bounce && arrow.canBounce)
        {
            if (!physicalHit)
            {
                arrow._rigidbody.velocity = new Vector2(arrow._rigidbody.velocity.x * -.4f, arrow._rigidbody.velocity.y * -.4f);
                arrow._rigidbody.AddTorque(0f, ForceMode2D.Impulse);
            }
        }
        else if (crusherHit)
        {
            arrow._rigidbody.velocity = new Vector2(arrow._rigidbody.velocity.x * -.4f, arrow._rigidbody.velocity.y * -.4f);
            arrow._rigidbody.AddTorque(0f, ForceMode2D.Impulse);
        }
        else
        {
            arrow._rigidbody.isKinematic = true;
            arrow._rigidbody.velocity = Vector2.zero;
            arrow._rigidbody.angularVelocity = Vector2.zero;
        }

        DuringNativeHit?.Invoke(target); // modelled native side effects on the target (kill / pool / component loss)
        arrow._hasHit = true;
        arrow._orientToVelocity = false;
        if (arrow._impactSpawner != null)
        {
            arrow._impactSpawner.Play();
            arrow._spriteRenderer.enabled = false;
            despawnDelay = arrow._impactSpawner.main.startLifetime.constantMax;
        }
        if (arrow.authorityActive) Pool.Despawn(arrow.gameObject, despawnDelay, false);
    }

    private static void TryDamage(Arrow arrow, Damageable damageable)
    {
        if (!arrow.authorityActive || damageable == null) return;
        if (!damageable.IsDamagedBy(arrow._damageSource)) return;
        int amount = arrow._perfect ? arrow.hitDamage * arrow.perfectDamageMultiplier : arrow.hitDamage;
        damageable.ReceiveDamage(amount, arrow.gameObject, arrow._damageSource);
    }

    // -------- fixtures --------

    private static Archer NewArcher(GameObject layer = null)
    {
        var go = new GameObject("Archer");
        go.transform.SetParent((layer ?? ArcherOptionsScope.Layer).transform, false);
        return go.AddComponent<Archer>();
    }

    private static Arrow NewArrow(Archer owner, Vector3 at, int sortingLayer = 7, int sortingOrder = 123,
        bool sprite = true, bool trailMaterial = true, bool impactSpawner = true, int goLayer = 0,
        GameObject parent = null)
    {
        var go = new GameObject("Arrow") { layer = goLayer };
        go.transform.SetParent((parent ?? ArcherOptionsScope.Layer).transform, false);
        var arrow = go.AddComponent<Arrow>();
        arrow.transform.position = at;
        arrow.archer = owner != null ? owner.gameObject : null;
        arrow._rigidbody = go.AddComponent<Rigidbody2D>();
        arrow.wallHitSound = go.AddComponent<AudioEmitter>();
        arrow.groundHitSound = go.AddComponent<AudioEmitter>();
        if (sprite)
        {
            arrow._spriteRenderer = go.AddComponent<SpriteRenderer>();
            arrow._spriteRenderer.sortingLayerID = sortingLayer;
            arrow._spriteRenderer.sortingOrder = sortingOrder;
        }
        if (trailMaterial)
        {
            arrow._trail = go.AddComponent<TrailRenderer>();
            arrow._trail.sharedMaterial = TrailMaterial;
        }
        if (impactSpawner) arrow._impactSpawner = go.AddComponent<ParticleSystem>();
        return arrow;
    }

    private static (GameObject go, Damageable damageable) NewTarget(Vector3 at)
    {
        var go = new GameObject("Enemy") { tag = "Enemy" };
        go.transform.position = at;
        return (go, go.AddComponent<Damageable>());
    }

    private static GameObject NewGround(Vector3 at)
    {
        var go = new GameObject("Ground") { tag = "Ground" };
        go.transform.position = at;
        return go;
    }

    private static List<GameObject> BuiltRoots() =>
        UnityEngine.Object.Created.OfType<GameObject>().Where(g => g.name == "KEM_ArcherImpact").ToList();

    private static List<GameObject> EffectRoots() => BuiltRoots().Where(g => !g.Destroyed).ToList();

    private static List<GameObject> KEMObjects() =>
        UnityEngine.Object.Created.OfType<GameObject>().Where(g => g.name.StartsWith("KEM_")).ToList();

    private static List<MeshRenderer> LayersOf(GameObject root) =>
        root.transform.children.Select(t => t.gameObject.GetComponent<MeshRenderer>()).ToList();

    private static MeshRenderer LayerRenderer(GameObject root, string name) =>
        root.transform.children.First(t => t.gameObject.name == name).gameObject.GetComponent<MeshRenderer>();

    private static Mesh LayerMesh(GameObject root, string name) =>
        root.transform.children.First(t => t.gameObject.name == name).gameObject.GetComponent<MeshFilter>().sharedMesh;

    private static GameObject LayerGo(GameObject root, string name) =>
        root.transform.children.First(t => t.gameObject.name == name).gameObject;

    /// <summary>Live module-owned materials (every material except the borrowed trail reference).</summary>
    private static List<Material> OwnedMaterials() =>
        UnityEngine.Object.Created.OfType<Material>().Where(m => !ReferenceEquals(m, TrailMaterial) && !m.Destroyed).ToList();

    private static List<Mesh> OwnedMeshes() => UnityEngine.Object.Created.OfType<Mesh>().Where(m => !m.Destroyed).ToList();

    private static List<Texture2D> OwnedTextures() =>
        UnityEngine.Object.Created.OfType<Texture2D>().Where(t => !t.Destroyed).ToList();

    private static float Luminance(Color color) => (color.r + color.g + color.b) / 3f;

    private static bool IsPixelAligned(float value) =>
        MathF.Abs(value / PatchArcher_Impact.PixelSize - MathF.Round(value / PatchArcher_Impact.PixelSize)) < .001f;

    private static int ActivationsInFrame(int frame) => GameObject.Activations.Count(a => a.Frame == frame);

    /// <summary>Observable native state: everything the module must leave alone.</summary>
    private static string Snapshot(Arrow arrow, Damageable damageable) => string.Join("|",
        arrow._hasHit, arrow._orientToVelocity, arrow._rigidbody.isKinematic,
        arrow._rigidbody.velocity.x, arrow._rigidbody.velocity.y, arrow._rigidbody.angularVelocity.x,
        arrow._spriteRenderer.enabled, arrow._trail.enabled, arrow._trail.time,
        arrow.transform.position.x, arrow.transform.position.y, arrow.transform.localScale.x,
        damageable.HitCount, damageable.TotalDamage, damageable.LastSource,
        ParticleSystem.Plays, ParticleSystem.MainAccesses, Pool.DespawnCalls, Pool.LastDespawnDelay,
        Rigidbody2D.TorqueCalls, UnityEngine.Random.Calls);

    // ============================================================
    // Hook surface
    // ============================================================

    private static void HookSurface()
    {
        Test("Hook is an explicit non-generic Arrow.HitObject wrapper without bare helper names", () =>
        {
            Check(!HookType.IsGenericType, "hook host must not be generic");
            Check(HookType.IsAbstract && HookType.IsSealed, "hook host must be static");
            Check(PrefixMethod != null && PostfixMethod != null, "prefix and postfix present");
            Check(PrefixMethod.GetParameters().Any(p => p.Name == "__state" && p.IsOut), "prefix captures __state");
            Check(PostfixMethod.GetParameters().Any(p => p.Name == "__state"), "postfix consumes __state");
            Check(PrefixMethod.GetParameters().Any(p => p.Name == "target"), "prefix sees the native target");
            Check(PrefixMethod.GetParameters().Any(p => p.Name == "physicalHit"), "prefix sees physicalHit");
            Eq(2, PostfixMethod.GetParameters().Length, "postfix reads no live target: category comes from the cached state");
            Check(Production.GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static) == null,
                "module type must not expose a bare Prefix helper");
            Check(Production.GetMethod("Postfix", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static) == null,
                "module type must not expose a bare Postfix helper");
            Check(Production.GetMethod("Finalizer", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static) == null,
                "module type must not expose a bare Finalizer helper");
            Eq(1, HookType.GetCustomAttributes<HarmonyLib.HarmonyPatch>()
                .Count(a => a.TargetType == typeof(Arrow) && a.MethodName == "HitObject"), "exactly one HitObject hook");
        });
    }

    // ============================================================
    // Native acceptance boundary
    // ============================================================

    private static void NativeAcceptance()
    {
        Test("Unqualified projectile keeps native damage without a Greek impact visual", () =>
        {
            var arrow = NewArrow(NewArcher(), new Vector3(3f, 2f, 0f));
            var (target, damageable) = NewTarget(new Vector3(3.2f, 2f, 0f));
            PatchArcher_GreekImpact.Eligible = false;
            NativeHit(arrow, target);
            Eq(1, damageable.TotalDamage, "native damage remains");
            Eq(0, EffectRoots().Count, "no unqualified visual");
        });
        Test("Accepted hit spawns one three-layer pixel burst at the hit point with arrow renderer sorting", () =>
        {
            var archer = NewArcher();
            var arrow = NewArrow(archer, new Vector3(3f, 2f, 0f), sortingLayer: 7, sortingOrder: 123, goLayer: 6);
            var (targetGo, damageable) = NewTarget(new Vector3(3.2f, 2f, 0f));
            ResetCounts();

            NativeHit(arrow, targetGo);

            Eq(1, NativeRuns, "native HitObject invoked once");
            Eq(1, damageable.HitCount, "native damage applied exactly once");
            Eq(1, damageable.TotalDamage, "native damage amount untouched");
            Eq(1, Pool.DespawnCalls, "native despawn requested once");
            Eq(.4f, Pool.LastDespawnDelay, "native despawn delay untouched");
            Check(arrow._hasHit, "native accepted the hit");
            Eq(1, ParticleSystem.Plays, "native particle effect played once");

            var roots = EffectRoots();
            Eq(1, roots.Count, "exactly one burst");
            GameObject root = roots[0];
            Check(root.activeSelf, "burst visible without waiting for a tick");
            Same(new Vector3(3f, 2f, 0f), root.transform.position, "burst at the native hit position");
            Eq(0, Quaternion.EulerCalls, "pixel grid stays axis aligned (author random z-rotation not migrated)");
            Check(root.transform.parent == null, "burst is not parented into the arrow");
            Eq(6, root.layer, "burst copies the arrow GameObject layer");
            Eq(1, BuiltRoots().Count, "exactly one pool root built");

            var renderers = LayersOf(root);
            Eq(PatchArcher_Impact.LayerCount, renderers.Count, "three fire layers");
            Eq(3, Mesh.CreatedCount, "one mesh per layer, built with the slot");
            Eq(3, Material.CreatedCount, "one owned material per layer");
            Eq(1, Texture2D.CreatedCount, "exactly one shared white texture");
            Eq(1, Texture2D.SetPixelCalls, "white texture pixel written once");
            Eq(1, Texture2D.ApplyCalls, "white texture applied once");
            Eq(0, MeshFilter.MeshInstantiations, "sharedMesh used: no mesh clone");

            foreach (MeshRenderer renderer in renderers)
            {
                Check(renderer.enabled, "layer renderer enabled");
                Check(!ReferenceEquals(renderer.sharedMaterial, TrailMaterial), "trail material never borrowed");
                Check(OwnedMaterials().Contains(renderer.sharedMaterial), "renderer uses a module-owned material");
                Check(ReferenceEquals(renderer.sharedMaterial.mainTexture, OwnedTextures().Single()),
                    "all layers sample the single owned white texture");
                Eq(6, renderer.gameObject.layer, "layer GameObject follows the arrow layer");
            }
            Eq(7, renderers[0].sortingLayerID, "sorting layer from arrow renderer");
            Eq(125, renderers[0].sortingOrder, "core draws above the arrow order");
            Eq(124, renderers[1].sortingOrder, "mid layer order");
            Eq(123, renderers[2].sortingOrder, "outer layer keeps the arrow order");
            Check(renderers.All(l => l.sortingOrder != 30000), "no hardcoded 30000 sorting order");
            Eq(0, TrailMaterial.InstanceWrites, "borrowed trail material never written");
            Check(!TrailMaterial.Destroyed, "borrowed trail material never destroyed");

            Eq(1, Shader.Finds, "shader resolved once for the pool");
            Eq("Sprites/Default", Shader.FindLog[0], "primary shader probed first");
            Eq(1, Log.Warnings.Count(w => w.Contains("Sprites/Default")), "resolved shader recorded once for root");
            Check(Log.Infos.Any(i => i.Contains("pixel fire ready") && i.Contains("indices=150")
                && i.Contains("vertexCount=36") && i.Contains("texture=1x1") && i.Contains("Point")),
                "one-shot log records shader, uploaded vertex/index counts and the 1x1 Point texture");

            Mesh core = LayerMesh(root, "KEM_ImpactCore");
            Eq(PatchArcher_Impact.VertexCount, core.vertices.Length, "36 grid vertices per layer");
            Eq(PatchArcher_Impact.IndexCount, core.triangles.Length, "150 triangle indices per layer");
            Eq(6, Mesh.VertexUploads, "3 build writes (empty-mesh seed) + 3 spawn writes");
            Eq(0, Mesh.RejectedGeometryWrites, "no uv/index write rejected for a vertex-less mesh");
            Eq(PatchArcher_Impact.VertexCount, core.vertexCount, "native vertexCount is 36 after the seed write");
            Eq(PatchArcher_Impact.VertexCount, core.uv.Length, "uv uploaded for all 36 vertices");
            Eq(3, Il2CppStructArray<Vector3>.Allocations, "one cached native vertex array per layer");
            Eq(0, Il2CppStructArray<Vector3>.ManagedCopies, "no managed array copied into a native array");
        });

        Test("Duplicate HitObject call on the same arrow cannot spawn a second burst", () =>
        {
            var archer = NewArcher();
            var arrow = NewArrow(archer, new Vector3(1f, 1f, 0f));
            var (targetGo, damageable) = NewTarget(new Vector3(1.2f, 1f, 0f));
            NativeHit(arrow, targetGo);
            ResetCounts();

            NativeHit(arrow, targetGo); // native early-returns on _hasHit

            Eq(1, NativeRuns, "second native invocation reached its early return");
            Eq(1, damageable.HitCount, "damage not repeated");
            Eq(0, Pool.DespawnCalls, "despawn not repeated");
            Eq(1, EffectRoots().Count, "still exactly one burst");
            Eq(0, GameObject.Activations.Count, "no second spawn");
        });

        Test("Client without world authority gets no burst and no native acceptance", () =>
        {
            NetworkBigBoss.HasWorldAuth = false;
            var archer = NewArcher();
            var arrow = NewArrow(archer, new Vector3(1f, 1f, 0f));
            var (targetGo, damageable) = NewTarget(new Vector3(1.2f, 1f, 0f));
            ResetCounts();

            NativeHit(arrow, targetGo);

            Check(!arrow._hasHit, "native client path stops before _hasHit");
            Eq(0, damageable.HitCount, "client deals no authoritative damage");
            Eq(0, EffectRoots().Count, "no client burst (no duplicated flame)");
        });

        Test("Arrow without an archer owner keeps native damage but draws no burst", () =>
        {
            var archer = NewArcher();
            var arrow = NewArrow(archer, new Vector3(2f, 0f, 0f));
            arrow.archer = null; // unrelated projectile (crossbow bolt / ballista / debris)
            var (targetGo, damageable) = NewTarget(new Vector3(2.2f, 0f, 0f));
            ResetCounts();

            NativeHit(arrow, targetGo);

            Eq(1, damageable.HitCount, "native projectile damage unchanged");
            Eq(0, EffectRoots().Count, "unrelated projectile draws no flame");
        });

        Test("Arrow owned by a GameObject without an Archer component draws no burst", () =>
        {
            var archer = NewArcher();
            var arrow = NewArrow(archer, new Vector3(2f, 0f, 0f));
            arrow.archer = new GameObject("SomeOtherOwner");
            var (targetGo, damageable) = NewTarget(new Vector3(2.2f, 0f, 0f));
            ResetCounts();

            NativeHit(arrow, targetGo);

            Eq(1, damageable.HitCount, "native damage unchanged");
            Eq(0, EffectRoots().Count, "no burst for non-archer owners");
        });

        Test("Archer outside the current world scope is ignored while native damage lands", () =>
        {
            var foreignLayer = new GameObject("OtherWorldLayer");
            var archer = NewArcher(foreignLayer);
            var arrow = NewArrow(archer, new Vector3(4f, 0f, 0f));
            var (targetGo, damageable) = NewTarget(new Vector3(4.2f, 0f, 0f));
            ResetCounts();

            NativeHit(arrow, targetGo);

            Check(!ArcherOptionsScope.IsCurrent(archer), "fixture archer is out of scope");
            Eq(1, damageable.HitCount, "native damage unchanged for out-of-scope archer");
            Eq(0, EffectRoots().Count, "out-of-scope archer draws no burst");
        });

        Test("Inactive scope refuses bursts and a later world transition rebuilds fresh", () =>
        {
            var archer = NewArcher();
            var arrow = NewArrow(archer, new Vector3(1f, 1f, 0f));
            var (targetGo, damageable) = NewTarget(new Vector3(1.2f, 1f, 0f));

            ArcherOptionsScope.Active = false; // main menu / world change
            NativeHit(arrow, targetGo);
            Eq(0, EffectRoots().Count, "no burst while the scope is inactive");
            Tick();
            Eq(0, EffectRoots().Count, "nothing to release");

            ArcherOptionsScope.Active = true; // new world, same current layer
            arrow._hasHit = false;
            ResetCounts();
            NativeHit(arrow, targetGo);

            Eq(2, damageable.HitCount, "second native hit accepted");
            Eq(1, EffectRoots().Count, "burst after the world transition");
            Eq(3, Mesh.CreatedCount, "meshes built for the new pool slot");
            Eq(1, Texture2D.CreatedCount, "one white texture for the new pool");
        });

        Test("Replacing the live world identity releases the old pool while the scope stays active", () =>
        {
            var archerA = NewArcher();
            var arrowA = NewArrow(archerA, new Vector3(1f, 1f, 0f));
            var (targetA, _) = NewTarget(new Vector3(1.2f, 1f, 0f));
            NativeHit(arrowA, targetA);
            GameObject firstRoot = EffectRoots().Single();
            List<Mesh> ownedMeshes = OwnedMeshes();
            List<Material> ownedMaterials = OwnedMaterials();
            Texture2D ownedTexture = OwnedTextures().Single();
            Eq(PatchArcher_Impact.LayerCount, ownedMeshes.Count, "one owned mesh per layer");
            Check(ArcherOptionsScope.IsCurrent(arrowA), "fixture arrow starts current");

            // World A -> B with the scope still active: real identity replacement.
            ArcherOptionsScope.World = new GameObject("WorldB");
            ArcherOptionsScope.Layer = new GameObject("LayerB");
            ArcherOptionsScope.Scene = 7;
            ResetCounts();
            Tick();

            Check(!firstRoot, "old-world burst destroyed on identity change");
            Check(ownedMeshes.All(m => !m), "old-world meshes destroyed on identity change");
            Check(ownedMaterials.All(m => !m), "old-world materials destroyed on identity change");
            Check(!ownedTexture, "old-world white texture destroyed on identity change");
            Eq(0, EffectRoots().Count, "no burst survives the world replacement");
            Eq(0, UnityEngine.Object.DoubleDestroys, "no duplicate destroy requests");
            Eq(0, OwnedMeshes().Count, "no orphan mesh survives the release");
            Eq(0, OwnedMaterials().Count, "no orphan material survives the release");

            arrowA._hasHit = false;
            NativeHit(arrowA, targetA);
            Eq(0, EffectRoots().Count, "stale-world arrow draws no flame in the new world");

            var archerB = NewArcher();
            var arrowB = NewArrow(archerB, new Vector3(3f, 3f, 0f));
            var (targetB, _) = NewTarget(new Vector3(3.2f, 3f, 0f));
            ResetCounts();
            NativeHit(arrowB, targetB);

            Eq(1, EffectRoots().Count, "burst in the new world");
            Eq(3, Mesh.CreatedCount, "fresh meshes for the new world pool");
            Eq(1, Texture2D.CreatedCount, "fresh white texture for the new world pool");
            Eq(0, UnityEngine.Object.DoubleDestroys, "no double destroy across the rebuild");
        });

        Test("Replacing only the layer inside the same world also releases the pool", () =>
        {
            var archer = NewArcher();
            var arrow = NewArrow(archer, new Vector3(1f, 1f, 0f));
            var (targetGo, _) = NewTarget(new Vector3(1.2f, 1f, 0f));
            NativeHit(arrow, targetGo);
            GameObject firstRoot = EffectRoots().Single();

            ArcherOptionsScope.Layer = new GameObject("LayerA2"); // same world pointer, new layer
            ResetCounts();
            Tick();

            Check(!firstRoot, "pool released when only the layer identity changes");
            Eq(0, EffectRoots().Count, "no burst survives the layer replacement");
        });

        Test("Unresolvable context releases the pool and refuses new bursts", () =>
        {
            var archer = NewArcher();
            var arrow = NewArrow(archer, new Vector3(1f, 1f, 0f));
            var (targetGo, damageable) = NewTarget(new Vector3(1.2f, 1f, 0f));
            NativeHit(arrow, targetGo);
            GameObject firstRoot = EffectRoots().Single();

            ArcherOptionsScope.World = null; // trusted context gone while the scope still reports active
            ResetCounts();
            Tick();
            Check(!firstRoot, "pool released when no trusted context resolves");

            arrow._hasHit = false;
            NativeHit(arrow, targetGo);
            Eq(2, damageable.HitCount, "native damage still applied");
            Eq(0, EffectRoots().Count, "no burst without a trusted context");
        });

        Test("Arrow left in a stale scene draws no burst even with a current owner", () =>
        {
            var archer = NewArcher();
            var staleLayer = new GameObject("StaleLayer");
            var arrow = NewArrow(archer, new Vector3(1f, 1f, 0f), parent: staleLayer);
            var (targetGo, damageable) = NewTarget(new Vector3(1.2f, 1f, 0f));
            ResetCounts();

            NativeHit(arrow, targetGo);

            Check(ArcherOptionsScope.IsCurrent(archer), "owner is current");
            Check(!ArcherOptionsScope.IsCurrent(arrow), "fixture arrow is stale");
            Eq(1, damageable.HitCount, "native damage unchanged for the stale arrow");
            Eq(0, EffectRoots().Count, "no burst for an arrow outside the current scene");
        });

        Test("Disabled archer owner draws no burst while native damage still lands", () =>
        {
            var archer = NewArcher();
            archer.enabled = false;
            var arrow = NewArrow(archer, new Vector3(1f, 1f, 0f));
            var (targetGo, damageable) = NewTarget(new Vector3(1.2f, 1f, 0f));
            ResetCounts();

            NativeHit(arrow, targetGo);

            Check(!archer.isActiveAndEnabled, "fixture owner is disabled");
            Eq(1, damageable.HitCount, "native damage unchanged for a disabled owner");
            Eq(0, EffectRoots().Count, "no burst from a disabled owner");
        });

        Test("Recycled arrow with _hasHit cleared by OnEnable spawns a fresh burst from the same slot", () =>
        {
            var archer = NewArcher();
            var arrow = NewArrow(archer, new Vector3(1f, 1f, 0f));
            var (targetGo, _) = NewTarget(new Vector3(1.2f, 1f, 0f));
            NativeHit(arrow, targetGo);
            Advance(PatchArcher_Impact.MaxEffectLifetime + .1f);
            arrow._hasHit = false; // Arrow.OnEnable pool reuse
            ResetCounts();

            NativeHit(arrow, targetGo);

            Eq(1, EffectRoots().Count, "pooled arrow hit again spawns one burst");
            Eq(1, BuiltRoots().Count, "slot reused instead of a new pool root");
            Eq(0, Mesh.CreatedCount, "reused slot keeps its meshes");
            Eq(0, Material.CreatedCount, "reused slot keeps its materials");
            Eq(3, Mesh.VertexUploads, "reused slot rewrites its grid for the new hit");
        });

        Test("Native exception skips the postfix and leaves no partial burst, later hits recover", () =>
        {
            var archer = NewArcher();
            var arrow = NewArrow(archer, new Vector3(1f, 1f, 0f));
            var (targetGo, damageable) = NewTarget(new Vector3(1.2f, 1f, 0f));
            ParticleSystem.ThrowOnPlay = true;
            ResetCounts();

            bool threw = false;
            try { NativeHit(arrow, targetGo); } catch (InvalidOperationException) { threw = true; }

            Check(threw, "native failure surfaced to the caller");
            Check(arrow._hasHit, "native set _hasHit before failing (postfix skipped)");
            Eq(1, damageable.HitCount, "native damage already applied");
            Eq(0, EffectRoots().Count, "no burst when the postfix is skipped");
            Eq(0, BuiltRoots().Count, "no pool allocation at all");
            Eq(0, UnityEngine.Object.DestroyRequests, "nothing to clean up");

            ParticleSystem.ThrowOnPlay = false;
            var nextArrow = NewArrow(archer, new Vector3(5f, 1f, 0f));
            var (nextTarget, _) = NewTarget(new Vector3(5.2f, 1f, 0f));
            NativeHit(nextArrow, nextTarget);
            Eq(1, EffectRoots().Count, "next hit recovers");
        });

        Test("Burst leaves the native arrow state identical to a disabled-module run", () =>
        {
            var archer = NewArcher();
            ModConfig.ArcherImpactEnabled.Value = false;
            var offArrow = NewArrow(archer, new Vector3(1f, 1f, 0f), sortingLayer: 5, sortingOrder: -321);
            offArrow._perfect = true;
            var (offTarget, offDamageable) = NewTarget(new Vector3(1.2f, 1f, 0f));
            ResetCounts();

            NativeHit(offArrow, offTarget);
            string baseline = Snapshot(offArrow, offDamageable);
            Eq(1, offDamageable.HitCount, "baseline native damage");
            Eq(0, EffectRoots().Count, "baseline run draws nothing");

            ModConfig.ArcherImpactEnabled.Value = true;
            var onArrow = NewArrow(archer, new Vector3(1f, 1f, 0f), sortingLayer: 5, sortingOrder: -321);
            onArrow._perfect = true;
            var (onTarget, onDamageable) = NewTarget(new Vector3(1.2f, 1f, 0f));
            ResetCounts();

            NativeHit(onArrow, onTarget);

            Eq(1, EffectRoots().Count, "enabled run draws the burst");
            Eq(baseline, Snapshot(onArrow, onDamageable), "native arrow/damage/particle state identical with the burst");
            Eq(5, LayersOf(EffectRoots().Single())[0].sortingLayerID, "sorting layer follows the arrow renderer");
            Eq(-319, LayersOf(EffectRoots().Single())[0].sortingOrder, "sorting order offset from the arrow renderer");
            Eq(0, UnityEngine.Random.Calls, "module consumed no UnityEngine.Random");
        });

        Test("Sorting order saturates instead of overflowing at int.MaxValue", () =>
        {
            var archer = NewArcher();
            var arrow = NewArrow(archer, new Vector3(0f, 0f, 0f), sortingOrder: int.MaxValue);
            var (targetGo, _) = NewTarget(new Vector3(.2f, 0f, 0f));
            ResetCounts();

            NativeHit(arrow, targetGo);

            Check(LayersOf(EffectRoots().Single()).All(l => l.sortingOrder == int.MaxValue), "saturated sorting order");
        });

        Test("Missing arrow sprite renderer is ignored with one bounded warning and no allocation", () =>
        {
            var archer = NewArcher();
            var arrow = NewArrow(archer, new Vector3(1f, 1f, 0f), sprite: false, impactSpawner: false);
            var (targetGo, damageable) = NewTarget(new Vector3(1.2f, 1f, 0f));
            ResetCounts();

            for (int i = 0; i < 6; i++)
            {
                arrow._hasHit = false;
                NativeHit(arrow, targetGo);
            }

            Eq(6, damageable.HitCount, "native damage kept for all ignored hits");
            Eq(0, EffectRoots().Count, "no burst without a renderer depth source");
            Eq(0, BuiltRoots().Count, "no pool allocation");
            Eq(1, Log.Warnings.Count(w => w.Contains("renderer unavailable")), "warning logged once");
        });

        Test("Config off keeps accepted hits completely free of module work", () =>
        {
            var archer = NewArcher();
            var arrow = NewArrow(archer, new Vector3(1f, 1f, 0f));
            var (targetGo, damageable) = NewTarget(new Vector3(1.2f, 1f, 0f));
            ModConfig.ArcherImpactEnabled.Value = false;
            ResetCounts();

            NativeHit(arrow, targetGo);
            Tick();

            Eq(1, damageable.HitCount, "native hit unaffected by the disabled module");
            Eq(0, EffectRoots().Count, "no burst while disabled");
            Eq(0, UnityEngine.Object.DestroyRequests, "no allocation or destroy work");
            Eq(0, GameObject.Activations.Count, "no activations");
            Eq(0, Shader.Finds, "no shader probes while disabled");
        });

        Test("Idle ticks are no-ops: no allocation, no destroy, no exception", () =>
        {
            ResetCounts();
            for (int i = 0; i < 120; i++) Tick(.02f);

            Eq(0, BuiltRoots().Count, "nothing built while idle");
            Eq(0, UnityEngine.Object.DestroyRequests, "nothing destroyed while idle");
            Eq(0, Material.CreatedCount, "no material while idle");
            Eq(0, Mesh.CreatedCount, "no mesh while idle");
        });

        Test("Disabling destroys live bursts, owned meshes/materials and the texture; re-enabling rebuilds fresh", () =>
        {
            var archer = NewArcher();
            var arrow = NewArrow(archer, new Vector3(1f, 1f, 0f));
            var (targetGo, _) = NewTarget(new Vector3(1.2f, 1f, 0f));
            NativeHit(arrow, targetGo);
            GameObject firstRoot = EffectRoots().Single();
            List<Mesh> ownedMeshes = OwnedMeshes();
            List<Material> ownedMaterials = OwnedMaterials();
            Texture2D ownedTexture = OwnedTextures().Single();
            Eq(PatchArcher_Impact.LayerCount, ownedMeshes.Count, "one owned mesh per layer");

            ModConfig.ArcherImpactEnabled.Value = false;
            Tick();

            Check(!firstRoot, "live burst destroyed on disable");
            Check(ownedMeshes.All(m => !m), "owned meshes destroyed on disable");
            Check(ownedMaterials.All(m => !m), "owned materials destroyed on disable");
            Check(!ownedTexture, "owned white texture destroyed on disable");
            Eq(0, EffectRoots().Count, "no live burst remains");
            Eq(0, UnityEngine.Object.DoubleDestroys, "no duplicate destroy requests");
            Check(!TrailMaterial.Destroyed, "borrowed material never destroyed");

            ModConfig.ArcherImpactEnabled.Value = true;
            arrow._hasHit = false;
            ResetCounts();
            NativeHit(arrow, targetGo);

            var rebuilt = EffectRoots();
            Eq(1, rebuilt.Count, "fresh burst after re-enable");
            Check(!ReferenceEquals(rebuilt[0], firstRoot), "a new pool root was built");
            Eq(3, Mesh.CreatedCount, "fresh meshes for the new enable");
            Eq(3, Material.CreatedCount, "fresh owned materials for the new enable");
            Eq(1, Texture2D.CreatedCount, "one fresh white texture");
        });
    }

    // ============================================================
    // Real-DLL pixel fire visual contract
    // ============================================================

    private static void PixelFireVisuals()
    {
        Test("Layer mesh is the author's 5x5 pixel grid: 36 vertices, 150 indices, uv=(k/5,j/5) fan of quads", () =>
        {
            var archer = NewArcher();
            var arrow = NewArrow(archer, new Vector3(1f, 1f, 0f));
            var (targetGo, _) = NewTarget(new Vector3(1.2f, 1f, 0f));

            NativeHit(arrow, targetGo);

            GameObject root = EffectRoots().Single();
            foreach (string layerName in new[] { "KEM_ImpactCore", "KEM_ImpactMid", "KEM_ImpactOuter" })
            {
                Mesh mesh = LayerMesh(root, layerName);
                Eq(PatchArcher_Impact.VertexCount, mesh.vertices.Length, layerName + ": (5+1)^2 vertices");
                Eq(PatchArcher_Impact.IndexCount, mesh.triangles.Length, layerName + ": 25 cells x 6 indices");

                // UV=(k/5, j/5) and the exact grid coordinates the DLL writes at spawn
                for (int j = 0; j <= PatchArcher_Impact.GridCells; j++)
                {
                    for (int k = 0; k <= PatchArcher_Impact.GridCells; k++)
                    {
                        int vertex = j * PatchArcher_Impact.GridPoints + k;
                        Near((float)k / PatchArcher_Impact.GridCells, mesh.uv[vertex].x, "uv.x", .0001f);
                        Near((float)j / PatchArcher_Impact.GridCells, mesh.uv[vertex].y, "uv.y", .0001f);
                        // the author grid sits on half-pixel lines, so Round() moves each vertex
                        // by up to half a pixel onto the nearest lattice line.
                        Near(k * PatchArcher_Impact.PixelSize - PatchArcher_Impact.HalfExtent, mesh.vertices[vertex].x,
                            "grid x for (" + k + "," + j + ")", PatchArcher_Impact.PixelSize * .5f + .0001f);
                        Near(j * PatchArcher_Impact.PixelSize - PatchArcher_Impact.HalfExtent, mesh.vertices[vertex].y,
                            "grid y for (" + k + "," + j + ")", PatchArcher_Impact.PixelSize * .5f + .0001f);
                        Check(IsPixelAligned(mesh.vertices[vertex].x), "grid x snapped to the pixel lattice");
                        Check(IsPixelAligned(mesh.vertices[vertex].y), "grid y snapped to the pixel lattice");
                        Near(0f, mesh.vertices[vertex].z, "grid is flat on z");
                    }
                }

                // author winding per cell: (v00,v10,v01) then (v01,v10,v11)
                for (int cell = 0; cell < PatchArcher_Impact.GridCells * PatchArcher_Impact.GridCells; cell++)
                {
                    int l = cell / PatchArcher_Impact.GridCells, m = cell % PatchArcher_Impact.GridCells;
                    int v00 = l * PatchArcher_Impact.GridPoints + m;
                    Eq(v00, mesh.triangles[cell * 6], "cell " + cell + " first index");
                    Eq(v00 + PatchArcher_Impact.GridPoints, mesh.triangles[cell * 6 + 1], "cell " + cell + " second index");
                    Eq(v00 + 1, mesh.triangles[cell * 6 + 2], "cell " + cell + " third index");
                    Eq(v00 + 1, mesh.triangles[cell * 6 + 3], "cell " + cell + " fourth index");
                    Eq(v00 + PatchArcher_Impact.GridPoints, mesh.triangles[cell * 6 + 4], "cell " + cell + " fifth index");
                    Eq(v00 + PatchArcher_Impact.GridPoints + 1, mesh.triangles[cell * 6 + 5], "cell " + cell + " sixth index");
                }
            }
            Eq(3, Mesh.UvWrites, "uv written once per layer build");
            Eq(3, Mesh.TriangleWrites, "indices written once per layer build");
        });

        Test("Live burst rewrites its grid every tick, pixel snapping to 0.22 keeps it aligned", () =>
        {
            var archer = NewArcher();
            var arrow = NewArrow(archer, new Vector3(-2f, 4f, 0f));
            var (targetGo, _) = NewTarget(new Vector3(-1.8f, 4f, 0f));

            NativeHit(arrow, targetGo);
            GameObject root = EffectRoots().Single();
            Mesh core = LayerMesh(root, "KEM_ImpactCore");
            int uploadsAtSpawn = Mesh.VertexUploads;

            Tick(.05f);
            Check(Mesh.VertexUploads > uploadsAtSpawn, "vertex grid animated on the production tick");
            Eq(3, Mesh.VertexUploads - uploadsAtSpawn, "all three layers rewrite their grid");
            Check(Mesh.BoundsRecalculations >= 3, "bounds refreshed after the vertex rewrite");

            for (int i = 0; i < core.vertices.Length; i++)
            {
                Check(IsPixelAligned(core.vertices[i].x), "snapped x stays on the pixel grid");
                Check(IsPixelAligned(core.vertices[i].y), "snapped y stays on the pixel grid");
                Check(MathF.Abs(core.vertices[i].x) <= PatchArcher_Impact.HalfExtent + PatchArcher_Impact.PixelSize,
                    "vertex stays inside the pixel envelope");
                Check(MathF.Abs(core.vertices[i].y) <= PatchArcher_Impact.HalfExtent + PatchArcher_Impact.PixelSize,
                    "vertex stays inside the pixel envelope");
            }
            Eq(3, Il2CppStructArray<Vector3>.Allocations, "animation allocates no native vertex arrays");
            Eq(PatchArcher_Impact.LayerCount, Mesh.CreatedCount, "animation allocates no meshes");
            Eq(PatchArcher_Impact.LayerCount, Material.CreatedCount, "animation allocates no materials");

            // A quiet tick well past the core duration must stop rewriting the mesh.
            Advance(PatchArcher_Impact.MaxEffectLifetime + .1f);
            int uploadsAfterLife = Mesh.VertexUploads;
            Tick(.05f);
            Eq(uploadsAfterLife, Mesh.VertexUploads, "no vertex writes after the burst returned to the pool");
        });

        Test("A frame that skips past a layer's duration cannot leave that layer rendering", () =>
        {
            var archer = NewArcher();
            var arrow = NewArrow(archer, new Vector3(1f, 1f, 0f));
            GameObject ground = NewGround(new Vector3(1.2f, 1f, 0f));

            NativeHit(arrow, ground);
            GameObject root = EffectRoots().Single();
            MeshRenderer core = LayerRenderer(root, "KEM_ImpactCore");
            MeshRenderer mid = LayerRenderer(root, "KEM_ImpactMid");
            MeshRenderer outer = LayerRenderer(root, "KEM_ImpactOuter");
            Check(core.enabled && mid.enabled && outer.enabled, "all three layers start lit");
            float outerAlphaAtSpawn = outer.sharedMaterial.color.a;
            Check(outerAlphaAtSpawn > .5f, "outer layer spawns with the author alpha");

            Tick(1.2f); // single long frame: crosses the outer (<=0.8s) and mid (1.0s) durations

            Check(!outer.enabled, "outer layer switched off although the frame jumped past its duration");
            Check(!mid.enabled, "mid layer switched off although the frame jumped past its duration");
            Check(core.enabled, "core layer keeps burning (author animSpeed 1.5-2)");
            Check(root.activeSelf, "burst stays alive while the core layer burns");
            Check(outer.sharedMaterial.color.a > .5f,
                "outer layer still carried alpha: the switch-off came from the renderer state, not from a fade");

            Tick(1.2f); // crosses the core duration as well
            Check(!core.enabled, "core layer switched off once its own duration elapsed");
            Check(!root.activeSelf, "burst returned to the pool");

            arrow._hasHit = false;
            NativeHit(arrow, ground);
            Check(LayersOf(root).All(l => l.enabled), "reused slot re-lights every layer on spawn");
        });

        Test("Hit category is cached by the prefix: a target killed during the native hit keeps the enemy timing", () =>
        {
            var archer = NewArcher();
            var arrow = NewArrow(archer, new Vector3(1f, 1f, 0f));
            var (targetGo, damageable) = NewTarget(new Vector3(1.2f, 1f, 0f));
            DuringNativeHit = go => go.RemoveComponent<Damageable>(); // native kill / pool release drops the component
            NativeHit(arrow, targetGo);
            DuringNativeHit = null;

            Eq(1, damageable.HitCount, "native damage applied before the target lost its component");
            Check(targetGo.GetComponent<Damageable>() == null, "postfix-time classification could no longer see an enemy");
            GameObject root = EffectRoots().Single();
            Check(LayersOf(root).All(l => l.enabled), "burst spawned for the cached enemy category");

            Advance(PatchArcher_Impact.DurationEnemy * 2f + .05f);
            Check(!root.activeSelf, "0.5s enemy base honoured from the prefix cache, not the post-hit Ground fallback");
        });

        Test("Author growth then fade: scale grows to 80% of life, then alpha and scale shrink", () =>
        {
            var archer = NewArcher();
            var arrow = NewArrow(archer, new Vector3(1f, 1f, 0f));
            GameObject ground = NewGround(new Vector3(1.2f, 1f, 0f));

            NativeHit(arrow, ground);
            GameObject root = EffectRoots().Single();
            MeshRenderer coreRenderer = LayerRenderer(root, "KEM_ImpactCore");
            Transform coreTransform = LayerGo(root, "KEM_ImpactCore").transform;
            float spawnScale = coreTransform.localScale.x;
            float spawnAlpha = coreRenderer.sharedMaterial.color.a;
            Check(spawnScale > 0f, "spawn scale is non-zero (author startSize)");
            Check(spawnAlpha > 0f, "spawn tint carries the layer alpha");

            var scales = new List<float>();
            var alphas = new List<float>();
            for (int i = 0; i < 80 && root.activeSelf; i++)
            {
                Tick(.05f);
                scales.Add(coreTransform.localScale.x);
                alphas.Add(coreRenderer.sharedMaterial.color.a);
            }

            Check(scales.Count > 10, "burst lived long enough to sample");
            Check(!root.activeSelf, "burst returned to the pool when its core duration elapsed");
            Check(scales[0] > spawnScale, "scale grows away from the spawn size");
            float peak = scales.Max();
            int peakAt = scales.IndexOf(peak);
            Check(peakAt > 0 && peakAt < scales.Count - 1, "scale peaks before the burst ends");
            for (int i = peakAt + 1; i < scales.Count; i++)
                Check(scales[i] <= scales[i - 1] + .0001f, "scale never grows again after the peak (fade shrink)");
            Check(scales[^1] < peak, "fade shrinks the burst after the growth peak");
            for (int i = 1; i < alphas.Count; i++)
                Check(alphas[i] <= alphas[i - 1] + .0001f, "alpha never rises again (EaseOutCubic fade)");
            Check(alphas[^1] < alphas[0] * .25f, "alpha collapses towards 0 by the end of the duration");
        });

        Test("Author layer parameters: core burns longest and brightest, outer layer stops first", () =>
        {
            var archer = NewArcher();
            var arrow = NewArrow(archer, new Vector3(1f, 1f, 0f));
            GameObject ground = NewGround(new Vector3(1.2f, 1f, 0f));

            NativeHit(arrow, ground);
            GameObject root = EffectRoots().Single();
            MeshRenderer coreRenderer = LayerRenderer(root, "KEM_ImpactCore");
            MeshRenderer midRenderer = LayerRenderer(root, "KEM_ImpactMid");
            MeshRenderer outerRenderer = LayerRenderer(root, "KEM_ImpactOuter");

            // author tint * intensity ordering is deterministic: core > mid > outer
            Check(Luminance(coreRenderer.sharedMaterial.color) > Luminance(midRenderer.sharedMaterial.color),
                "core glow is brighter than the mid layer (intensity 4-6 vs 2.5-3.5)");
            Check(Luminance(midRenderer.sharedMaterial.color) > Luminance(outerRenderer.sharedMaterial.color),
                "mid glow is brighter than the outer layer (intensity 2.5-3.5 vs 1.8-2.5)");
            Check(coreRenderer.sharedMaterial.color.r >= coreRenderer.sharedMaterial.color.b
                && coreRenderer.sharedMaterial.color.g >= coreRenderer.sharedMaterial.color.b,
                "warm flame tint (no purple statue hue)");
            Eq(0, UnityEngine.Object.DestroyedObjects.Count, "nothing destroyed while the burst lives");

            // author animSpeed [1.5,2] core > 1.0 mid > [0.6,0.8] outer: layers freeze in that order.
            Advance(.9f); // ground base 1s: past the outer duration, inside mid and core
            Color outerBefore = outerRenderer.sharedMaterial.color;
            Color midBefore = midRenderer.sharedMaterial.color;
            Color coreBefore = coreRenderer.sharedMaterial.color;
            Tick(.05f);
            SameColor(outerBefore, outerRenderer.sharedMaterial.color, "outer layer stops rewriting first");
            Check(midRenderer.sharedMaterial.color.a < midBefore.a, "mid layer still fading");
            Check(coreRenderer.sharedMaterial.color.a < coreBefore.a, "core layer still fading");

            Advance(.35f); // past the mid duration (1.0s), before the core duration (1.5s min)
            Color midLate = midRenderer.sharedMaterial.color;
            Color coreLate = coreRenderer.sharedMaterial.color;
            Check(root.activeSelf, "core layer keeps the slot alive last");
            Tick(.05f);
            SameColor(midLate, midRenderer.sharedMaterial.color, "mid layer stops second");
            Check(coreRenderer.sharedMaterial.color.a < coreLate.a, "core layer keeps fading longest");
        });

        Test("Enemy hits use the author's 0.5s delay base, ground hits use 1s", () =>
        {
            var archer = NewArcher();
            var enemyArrow = NewArrow(archer, new Vector3(1f, 1f, 0f));
            var (enemyTarget, _) = NewTarget(new Vector3(1.2f, 1f, 0f));
            NativeHit(enemyArrow, enemyTarget);
            GameObject enemyRoot = EffectRoots().Single();

            Advance(PatchArcher_Impact.DurationEnemy * 2f + .05f); // past the enemy core duration (0.5 * 2.0)
            Check(!enemyRoot.activeSelf, "enemy burst finished inside the 0.5s delay window");

            var groundArrow = NewArrow(archer, new Vector3(3f, 1f, 0f));
            GameObject groundTarget = NewGround(new Vector3(3.2f, 1f, 0f));
            NativeHit(groundArrow, groundTarget);
            GameObject groundRoot = EffectRoots().Single(r => r.activeSelf);

            Tick(.05f);
            Check(groundRoot.activeSelf, "ground burst still burning after the enemy lifetime (1s base)");
            Advance(PatchArcher_Impact.DurationDefault * 2f + .1f);
            Check(!groundRoot.activeSelf, "ground burst finishes inside the 1s delay window");
        });
    }

    // ============================================================
    // Budgets and pool bounds
    // ============================================================

    /// <summary>
    /// 英雄来源的 FX 交接：只由「合资格箭 + 其来源」决定是否画；关英雄只清英雄特效，关希腊只清希腊特效。
    /// （真实资格判定由 greek-impact 套件的真实 PatchArcher_GreekImpact 覆盖；本套件覆盖 FX 侧的消费与清理。）
    /// </summary>
    private static void HeroOriginFx()
    {
        Test("Hero-only (Greek switch off) draws the burst for its own arrow", () =>
        {
            ModConfig.ArcherImpactEnabled.Value = false;      // 希腊总门关闭
            PatchArcher_GreekImpact.HeroEnabled = true;       // 英雄门打开
            PatchArcher_GreekImpact.HeroOrigin = true;
            var arrow = NewArrow(NewArcher(), new Vector3(2f, 1f, 0f));
            var (target, damageable) = NewTarget(new Vector3(2.2f, 1f, 0f));
            ResetCounts();

            NativeHit(arrow, target);

            Eq(1, EffectRoots().Count, "hero hit draws exactly one burst without the Greek switch");
            Eq(1, damageable.HitCount, "native hit path untouched by the FX gate");
        });

        Test("Hero switch does not enable a Greek-origin arrow", () =>
        {
            ModConfig.ArcherImpactEnabled.Value = false;
            PatchArcher_GreekImpact.HeroEnabled = true;       // 只开英雄
            PatchArcher_GreekImpact.HeroOrigin = false;       // 但这一发是希腊来源（非英雄）
            var arrow = NewArrow(NewArcher(), new Vector3(2f, 1f, 0f));
            var (target, _) = NewTarget(new Vector3(2.2f, 1f, 0f));
            ResetCounts();

            NativeHit(arrow, target);

            Eq(0, EffectRoots().Count, "origin gates never OR-enable the other source");
        });

        Test("Closing the hero switch clears only hero effects while Greek effects keep running", () =>
        {
            var heroRoot = SpawnOriginBurst(true, 2f);
            var greekRoot = SpawnOriginBurst(false, 5f);
            Eq(2, EffectRoots().Count, "both origins drawn while both switches are on");

            PatchArcher_GreekImpact.HeroEnabled = false;      // 只关英雄
            Tick();

            Check(heroRoot.Destroyed, "hero effect released");
            Check(!greekRoot.Destroyed, "greek effect untouched");
            Eq(1, EffectRoots().Count, "exactly the greek effect remains");
        });

        Test("Closing the Greek switch clears only Greek effects while hero effects keep running", () =>
        {
            var heroRoot = SpawnOriginBurst(true, 2f);
            var greekRoot = SpawnOriginBurst(false, 5f);
            Eq(2, EffectRoots().Count, "both origins drawn while both switches are on");

            ModConfig.ArcherImpactEnabled.Value = false;      // 只关希腊
            Tick();

            Check(greekRoot.Destroyed, "greek effect released");
            Check(!heroRoot.Destroyed, "hero effect untouched");
            Eq(1, EffectRoots().Count, "exactly the hero effect remains");
        });

        Test("Closing both switches releases the pool (resources freed, not kept for a dead world)", () =>
        {
            SpawnOriginBurst(true, 2f);
            SpawnOriginBurst(false, 5f);
            Eq(2, EffectRoots().Count, "both origins drawn");

            PatchArcher_GreekImpact.HeroEnabled = false;
            ModConfig.ArcherImpactEnabled.Value = false;
            Tick();

            Eq(0, EffectRoots().Count, "every effect released");
            Eq(0, BuiltRoots().Count(g => !g.Destroyed), "pool roots freed as before the hero layer existed");
        });
    }

    /// <summary>命中一次并返回其特效 root（按命中点区分来源）。</summary>
    private static GameObject SpawnOriginBurst(bool heroOrigin, float x)
    {
        PatchArcher_GreekImpact.Eligible = true;
        PatchArcher_GreekImpact.HeroOrigin = heroOrigin;
        PatchArcher_GreekImpact.HeroEnabled = true;
        var arrow = NewArrow(NewArcher(), new Vector3(x, 1f, 0f));
        var (target, _) = NewTarget(new Vector3(x + .2f, 1f, 0f));
        NativeHit(arrow, target);
        return EffectRoots().First(g => MathF.Abs(g.transform.position.x - x) < .01f);
    }

    private static void Budgets()
    {
        Test("At most four bursts are started per frame under a same-frame volley", () =>
        {
            var archer = NewArcher();
            var (targetGo, _) = NewTarget(new Vector3(1f, 1f, 0f));
            var arrows = new List<Arrow>();
            for (int i = 0; i < 10; i++) arrows.Add(NewArrow(archer, new Vector3(1f + i * .01f, 1f, 0f)));
            ResetCounts();

            int frame = Time.frameCount;
            foreach (Arrow arrow in arrows) NativeHit(arrow, targetGo);

            Eq(PatchArcher_Impact.MaxSpawnsPerFrame, ActivationsInFrame(frame), "per-frame spawn cap");
            Tick(.02f);
            Tick(.02f);
            Tick(.02f);
            Check(ActivationsInFrame(frame + 3) == 0, "no deferred catch-up spawning on later frames");
        });

        Test("Spawn window stays under the 24/s ceiling; author durations make the pool bind first", () =>
        {
            var archer = NewArcher();
            var (targetGo, _) = NewTarget(new Vector3(1f, 1f, 0f));
            var arrows = new List<Arrow>();
            for (int i = 0; i < 30; i++) arrows.Add(NewArrow(archer, new Vector3(1f + i * .01f, 1f, 0f)));
            ResetCounts();

            for (int i = 0; i < 30; i++)
            {
                NativeHit(arrows[i], targetGo);
                Tick(1f / 30f);
            }

            List<(int Frame, float Time, string Name)> spawns = GameObject.Activations;
            Check(spawns.Count < 30, "hits beyond pool turnover are dropped, never queued");
            foreach (var spawn in spawns)
            {
                int withinWindow = spawns.Count(s => s.Time >= spawn.Time && s.Time < spawn.Time + 1f);
                Check(withinWindow <= PatchArcher_Impact.MaxSpawnsPerSecond, "no rolling second exceeds 24 spawns");
            }
            // Author layer durations (>= 0.75s per enemy hit) cap turnover near 16 slots / 0.75s,
            // so the pool bound is reached before the 24/s window bound in this scenario.
            Check(EffectRoots().Count(r => r.activeSelf) <= PatchArcher_Impact.MaxEffects, "live bursts stay inside the pool");

            Advance(PatchArcher_Impact.MaxEffectLifetime + .1f); // quiet stretch empties the pool and the window
            int before = GameObject.Activations.Count;
            foreach (Arrow arrow in arrows.Take(10))
            {
                arrow._hasHit = false;
                NativeHit(arrow, targetGo);
                Tick(1f / 30f);
            }
            Eq(10, GameObject.Activations.Count - before, "window recovers after a quiet second");
        });

        Test("Pool is bounded: at most 16 live bursts and 48 pixel layers, extra hits are dropped", () =>
        {
            var archer = NewArcher();
            var (targetGo, _) = NewTarget(new Vector3(1f, 1f, 0f));
            var arrows = new List<Arrow>();
            for (int i = 0; i < 24; i++) arrows.Add(NewArrow(archer, new Vector3(1f + i * .01f, 1f, 0f)));
            ResetCounts();

            // Six frames of four hits: 24 accepted hits inside the rolling second, all still alive.
            for (int frame = 0; frame < 6; frame++)
            {
                foreach (Arrow arrow in arrows.Skip(frame * 4).Take(4)) NativeHit(arrow, targetGo);
                Tick(1f / 60f);
            }

            Eq(PatchArcher_Impact.MaxEffects, GameObject.Activations.Count, "sixteenth-slot cap reached, extras dropped");
            Check(BuiltRoots().Count <= PatchArcher_Impact.MaxEffects, "pool never grows beyond 16 slots");
            int renderers = BuiltRoots().Sum(r => r.transform.children.Count);
            Check(renderers <= PatchArcher_Impact.MaxEffects * PatchArcher_Impact.LayerCount, "at most 48 pixel layers");
            Check(Mesh.CreatedCount <= PatchArcher_Impact.MaxEffects * PatchArcher_Impact.LayerCount, "bounded mesh count");
            Check(Material.CreatedCount <= PatchArcher_Impact.MaxEffects * PatchArcher_Impact.LayerCount, "bounded material count");
            Eq(1, Texture2D.CreatedCount, "single shared white texture for the whole pool");
            Eq(0, Il2CppStructArray<Vector3>.ManagedCopies, "no managed geometry arrays copied");

            Advance(PatchArcher_Impact.MaxEffectLifetime + .1f);
            Eq(0, EffectRoots().Count(r => r.activeSelf), "all bursts finished");
            int rootsBefore = BuiltRoots().Count;
            int before = GameObject.Activations.Count;
            foreach (Arrow arrow in arrows.Take(8))
            {
                arrow._hasHit = false;
                NativeHit(arrow, targetGo);
                Tick(1f / 60f);
            }
            Eq(8, GameObject.Activations.Count - before, "freed slots serve new hits");
            Eq(rootsBefore, BuiltRoots().Count, "no new pool roots after reuse");
        });

        Test("Burst lifetime never exceeds the author bound and the mesh is recycled, not destroyed", () =>
        {
            var archer = NewArcher();
            var arrow = NewArrow(archer, new Vector3(1f, 1f, 0f));
            GameObject ground = NewGround(new Vector3(1.2f, 1f, 0f));
            NativeHit(arrow, ground);
            GameObject root = EffectRoots().Single();
            Mesh core = LayerMesh(root, "KEM_ImpactCore");
            Material coreMaterial = LayerRenderer(root, "KEM_ImpactCore").sharedMaterial;

            Check(PatchArcher_Impact.MaxEffectLifetime <= 2f, "lifetime inside the author bound (delay2 1 x animSpeed 2)");
            Advance(PatchArcher_Impact.MaxEffectLifetime + .1f);

            Check(!root.activeSelf, "hidden after the author duration");
            Eq(0, UnityEngine.Object.DestroyedObjects.OfType<Mesh>().Count(), "geometry kept for reuse after fading");
            Eq(0, UnityEngine.Object.DestroyedObjects.OfType<Material>().Count(), "material kept for reuse after fading");
            Check(ReferenceEquals(LayerMesh(root, "KEM_ImpactCore"), core), "same mesh instance reused");
            Check(ReferenceEquals(LayerRenderer(root, "KEM_ImpactCore").sharedMaterial, coreMaterial), "same material reused");
        });
    }

    // ============================================================
    // Pool reuse, resources and failure handling
    // ============================================================

    private static void PoolAndFailures()
    {
        Test("Slots are reused across hits: no per-hit meshes, materials, textures or geometry rebuild", () =>
        {
            var archer = NewArcher();
            var (targetGo, _) = NewTarget(new Vector3(1f, 1f, 0f));
            var arrow = NewArrow(archer, new Vector3(1f, 1f, 0f));
            NativeHit(arrow, targetGo); // warm the single-slot pool
            Advance(PatchArcher_Impact.MaxEffectLifetime + .1f);
            ResetCounts();

            for (int i = 0; i < 10; i++)
            {
                arrow._hasHit = false;
                arrow.transform.position = new Vector3(1f + i, 2f, 0f);
                NativeHit(arrow, targetGo);
                Advance(PatchArcher_Impact.MaxEffectLifetime + .1f);
            }

            Eq(1, BuiltRoots().Count, "single slot serves all hits");
            Eq(0, Mesh.CreatedCount, "no mesh per hit");
            Eq(0, Material.CreatedCount, "no material per hit");
            Eq(0, Texture2D.CreatedCount, "no texture per hit");
            Eq(0, Il2CppStructArray<Vector3>.Allocations, "no native geometry arrays per hit");
            Eq(0, Mesh.UvWrites, "no uv rebuild per hit");
            Eq(0, Mesh.TriangleWrites, "no index rebuild per hit");
            Check(Mesh.VertexUploads > 0, "reused slot still animates its grid");
        });

        Test("Reused slot follows the new hit position and renderer sorting", () =>
        {
            var archer = NewArcher();
            var (targetGo, _) = NewTarget(new Vector3(1f, 1f, 0f));
            var arrow = NewArrow(archer, new Vector3(1f, 1f, 0f), sortingLayer: 4, sortingOrder: 10);
            NativeHit(arrow, targetGo);
            Advance(PatchArcher_Impact.MaxEffectLifetime + .1f);

            arrow._hasHit = false;
            arrow.transform.position = new Vector3(-6f, 5f, 0f);
            arrow._spriteRenderer.sortingLayerID = 9;
            arrow._spriteRenderer.sortingOrder = 77;
            NativeHit(arrow, targetGo);

            GameObject root = EffectRoots().Single();
            Same(new Vector3(-6f, 5f, 0f), root.transform.position, "slot moved to the new hit");
            Check(LayersOf(root).All(l => l.sortingLayerID == 9 && l.sortingOrder >= 77 && l.sortingOrder <= 79),
                "sorting refreshed per hit");
        });

        Test("Externally destroyed burst (scene unload) is released without double destroy and rebuilt", () =>
        {
            var archer = NewArcher();
            var arrow = NewArrow(archer, new Vector3(1f, 1f, 0f));
            var (targetGo, _) = NewTarget(new Vector3(1.2f, 1f, 0f));
            NativeHit(arrow, targetGo);
            UnityEngine.Object.Destroy(EffectRoots().Single()); // scene unload takes the burst with it
            ResetCounts();

            Tick();

            Eq(0, UnityEngine.Object.DoubleDestroys, "no double destroy");
            Eq(0, OwnedMeshes().Count, "orphan meshes reclaimed with the lost slot");
            Eq(0, OwnedMaterials().Count, "orphan materials reclaimed with the lost slot");
            arrow._hasHit = false;
            NativeHit(arrow, targetGo);
            Eq(1, EffectRoots().Count, "next hit rebuilds the slot");
            Eq(2, BuiltRoots().Count, "the destroyed root is replaced by a new one");
        });

        Test("Both candidate shaders missing: burst skipped, backed off, then recovered", () =>
        {
            var archer = NewArcher();
            var arrow = NewArrow(archer, new Vector3(1f, 1f, 0f));
            var (targetGo, damageable) = NewTarget(new Vector3(1.2f, 1f, 0f));
            Shader.Available = false;
            ResetCounts();

            NativeHit(arrow, targetGo);
            arrow._hasHit = false;
            NativeHit(arrow, targetGo);

            Eq(2, damageable.HitCount, "native damage kept while no shader resolves");
            Eq(0, EffectRoots().Count, "no burst without a usable shader");
            Eq(0, BuiltRoots().Count, "no pool allocation without a shader");
            Eq(2, Shader.Finds, "both author shader names probed once");
            Eq(1, Log.Warnings.Count(w => w.Contains("impact fire disabled")), "warning logged once");
            Eq(0, Material.CreatedCount, "no material without a shader");
            Eq(0, Texture2D.CreatedCount, "no texture without a shader");
            Eq(0, TrailMaterial.InstanceWrites, "borrowed trail material untouched");

            Shader.Available = true;
            Tick(1f);
            arrow._hasHit = false;
            NativeHit(arrow, targetGo);
            Eq(0, EffectRoots().Count, "no retry inside the backoff window");
            Eq(2, Shader.Finds, "no shader probing during backoff");

            Tick(5f);
            arrow._hasHit = false;
            NativeHit(arrow, targetGo);
            Eq(1, EffectRoots().Count, "burst appears once a shader is available again");
            Eq(PatchArcher_Impact.LayerCount, Mesh.CreatedCount, "geometry built after recovery");
        });

        Test("White texture failure leaks no owned resource and recovers after the backoff", () =>
        {
            var archer = NewArcher();
            var arrow = NewArrow(archer, new Vector3(1f, 1f, 0f));
            var (targetGo, damageable) = NewTarget(new Vector3(1.2f, 1f, 0f));
            Texture2D.ThrowOnSetPixel = true;
            ResetCounts();

            NativeHit(arrow, targetGo);

            Eq(1, damageable.HitCount, "native damage unaffected by the texture failure");
            Eq(0, EffectRoots().Count, "no burst without a usable texture");
            Eq(0, KEMObjects().Count(g => !g.Destroyed), "no orphan KEM objects");
            Eq(0, OwnedMeshes().Count, "no orphan mesh from the aborted build");
            Eq(0, OwnedMaterials().Count, "no orphan material from the aborted build");
            Eq(1, Texture2D.CreatedCount, "one white texture attempted");
            Eq(0, OwnedTextures().Count, "half-initialised white texture destroyed instead of leaked");
            Eq(1, Log.Warnings.Count(w => w.Contains("white texture creation failed")), "failure logged once");

            Texture2D.ThrowOnSetPixel = false;
            Tick(5f);
            arrow._hasHit = false;
            NativeHit(arrow, targetGo);
            Eq(1, EffectRoots().Count, "recovers once SetPixel succeeds");
            Eq(1, OwnedTextures().Count, "single live white texture after recovery");
            Eq(0, UnityEngine.Object.DoubleDestroys, "no double destroy requests");

            // Apply failure through the same path: still nothing may leak.
            ArcherOptionsScope.World = new GameObject("WorldB");
            ArcherOptionsScope.Layer = new GameObject("LayerB");
            ArcherOptionsScope.Scene = 8;
            Tick(); // identity change releases the pool (and its texture)
            Eq(0, OwnedTextures().Count, "world change released the live white texture");
            var archerB = NewArcher();
            var arrowB = NewArrow(archerB, new Vector3(3f, 1f, 0f));
            Texture2D.ThrowOnApply = true;
            NativeHit(arrowB, targetGo);
            Eq(0, EffectRoots().Count, "no burst while Apply fails");
            Eq(0, OwnedTextures().Count, "failed Apply texture destroyed instead of leaked");
            Eq(0, KEMObjects().Count(g => !g.Destroyed), "no orphan geometry from the Apply failure");
            Texture2D.ThrowOnApply = false;
            Tick(5f);
            arrowB._hasHit = false;
            NativeHit(arrowB, targetGo);
            Eq(1, EffectRoots().Count, "recovers after the Apply failure clears");
            Eq(0, UnityEngine.Object.DoubleDestroys, "no double destroy requests after recovery");
        });

        Test("Sprites/Default unavailable: Unlit/Texture fallback is used and recorded", () =>
        {
            var archer = NewArcher();
            var arrow = NewArrow(archer, new Vector3(1f, 1f, 0f));
            var (targetGo, _) = NewTarget(new Vector3(1.2f, 1f, 0f));
            Shader.Available = true;
            Shader.BlockPrimary = true;
            ResetCounts();

            NativeHit(arrow, targetGo);

            Eq(1, EffectRoots().Count, "fallback shader still draws the burst");
            Eq(2, Shader.Finds, "primary probed, then the author's fallback");
            Eq("Sprites/Default", Shader.FindLog[0], "primary probed first");
            Eq("Unlit/Texture", Shader.FindLog[1], "author fallback probed second");
            Eq(1, Log.Warnings.Count(w => w.Contains("Unlit/Texture")), "fallback shader name recorded for root");
            Check(LayersOf(EffectRoots().Single()).All(l => ReferenceEquals(l.sharedMaterial.shader, Shader.Find("Unlit/Texture"))),
                "layers use the fallback shader");
        });

        Test("Owned materials and the single white texture are shared, never the arrow trail material", () =>
        {
            var archer = NewArcher();
            var (targetGo, _) = NewTarget(new Vector3(1f, 1f, 0f));
            var arrows = new List<Arrow>();
            for (int i = 0; i < 18; i++) arrows.Add(NewArrow(archer, new Vector3(1f + i * .01f, 1f, 0f)));
            ResetCounts();

            for (int frame = 0; frame < 5; frame++)
            {
                foreach (Arrow arrow in arrows.Skip(frame * 4).Take(4)) NativeHit(arrow, targetGo);
                Tick(1f / 60f);
            }

            Eq(PatchArcher_Impact.MaxEffects, EffectRoots().Count(r => r.activeSelf), "sixteen live bursts");
            Eq(1, Texture2D.CreatedCount, "exactly one white texture for the whole pool");
            Check(Material.CreatedCount > 1, "each layer owns its own tinted material");
            Check(EffectRoots().SelectMany(LayersOf).All(l => !ReferenceEquals(l.sharedMaterial, TrailMaterial)),
                "no layer borrows the arrow trail material");
            Check(EffectRoots().SelectMany(LayersOf)
                .All(l => ReferenceEquals(l.sharedMaterial.mainTexture, OwnedTextures().Single())),
                "every layer samples the single owned texture");
            Eq(0, TrailMaterial.InstanceWrites, "borrowed trail material never written");
            Eq(1, Texture2D.ApplyCalls, "texture applied once");
        });

        Test("Pool build failure cleans up partial objects, keeps the native path intact, recovers later", () =>
        {
            var archer = NewArcher();
            var arrow = NewArrow(archer, new Vector3(1f, 1f, 0f));
            var (targetGo, damageable) = NewTarget(new Vector3(1.2f, 1f, 0f));
            Mesh.ThrowOnUvWrite = true;
            ResetCounts();

            NativeHit(arrow, targetGo);

            Eq(1, damageable.HitCount, "native damage unaffected by the visual failure");
            Eq(1, Pool.DespawnCalls, "native despawn unaffected");
            Eq(0, EffectRoots().Count, "no live burst after a build failure");
            Eq(0, KEMObjects().Count(g => !g.Destroyed), "no orphan KEM object survives the failure");
            Eq(0, OwnedMeshes().Count, "partial meshes released");
            Eq(0, OwnedMaterials().Count, "partial materials released");
            Eq(1, Log.Warnings.Count(w => w.Contains("pool build failed")), "build failure logged once");

            arrow._hasHit = false;
            NativeHit(arrow, targetGo);
            Eq(1, Log.Warnings.Count(w => w.Contains("pool build failed")), "repeated failure does not spam the log");
            Eq(0, EffectRoots().Count, "still no burst while the failure persists");

            Mesh.ThrowOnUvWrite = false;
            Tick(5f);
            arrow._hasHit = false;
            NativeHit(arrow, targetGo);
            Eq(1, EffectRoots().Count, "burst after the failure clears");
            Eq(0, UnityEngine.Object.DoubleDestroys, "no double destroy requests");
        });

        Test("Spawn-time renderer write failure cleans the half-spawned slot and drops the burst", () =>
        {
            var archer = NewArcher();
            var arrow = NewArrow(archer, new Vector3(1f, 1f, 0f));
            var (targetGo, damageable) = NewTarget(new Vector3(1.2f, 1f, 0f));
            Renderer.ThrowOnSortingWrite = true;
            ResetCounts();

            NativeHit(arrow, targetGo);

            Eq(1, damageable.HitCount, "native damage unaffected");
            Eq(1, Pool.DespawnCalls, "native despawn unaffected");
            Eq(0, EffectRoots().Count, "half-spawned burst destroyed");
            Eq(0, OwnedMeshes().Count, "owned mesh from the aborted spawn released");
            Eq(0, OwnedMaterials().Count, "owned material from the aborted spawn released");
            Eq(1, Log.Warnings.Count(w => w.Contains("spawn failed")), "spawn failure logged once");

            Renderer.ThrowOnSortingWrite = false;
            arrow._hasHit = false;
            Tick(5f); // reuse failures back off as well; recovery must wait out the window
            ResetCounts();
            NativeHit(arrow, targetGo);
            Eq(1, EffectRoots().Count, "next hit spawns normally after the write failure clears");
            Eq(0, UnityEngine.Object.DoubleDestroys, "no double destroy requests");
        });

        Test("Vertex upload failure during animation releases the slot instead of leaving a frozen burst", () =>
        {
            var archer = NewArcher();
            var arrow = NewArrow(archer, new Vector3(1f, 1f, 0f));
            var (targetGo, damageable) = NewTarget(new Vector3(1.2f, 1f, 0f));
            NativeHit(arrow, targetGo);
            Eq(1, EffectRoots().Count, "burst alive");
            Mesh.ThrowOnVertexWrite = true;
            ResetCounts();

            Tick(.05f);

            Eq(0, EffectRoots().Count, "failed animation releases the slot");
            Eq(0, OwnedMeshes().Count, "mesh released with the failed slot");
            Eq(1, Log.Warnings.Count(w => w.Contains("animation failed")), "animation failure logged once");
            Eq(1, damageable.HitCount, "native damage untouched by the visual failure");

            Mesh.ThrowOnVertexWrite = false;
            Tick(5f);
            arrow._hasHit = false;
            NativeHit(arrow, targetGo);
            Eq(1, EffectRoots().Count, "recovers after the backoff");
            Eq(0, UnityEngine.Object.DoubleDestroys, "no double destroy requests");
        });

        Test("Pool build that fails while parenting destroys the unparented orphan child", () =>
        {
            var archer = NewArcher();
            var arrow = NewArrow(archer, new Vector3(1f, 1f, 0f));
            var (targetGo, damageable) = NewTarget(new Vector3(1.2f, 1f, 0f));
            Transform.ThrowOnSetParent = true;
            ResetCounts();

            NativeHit(arrow, targetGo);

            Eq(1, damageable.HitCount, "native damage unaffected");
            Eq(1, Pool.DespawnCalls, "native despawn unaffected");
            Eq(0, EffectRoots().Count, "no live burst after the parenting failure");
            Eq(0, KEMObjects().Count(g => !g.Destroyed), "the never-parented child and root are destroyed");
            Eq(0, OwnedMeshes().Count, "meshes from the aborted build released");
            Eq(0, OwnedMaterials().Count, "materials from the aborted build released");
            Eq(1, Log.Warnings.Count(w => w.Contains("pool build failed")), "failure logged once");

            Transform.ThrowOnSetParent = false;
            Tick(5f);
            arrow._hasHit = false;
            NativeHit(arrow, targetGo);
            Eq(1, EffectRoots().Count, "recovers once parenting succeeds again");
        });

        Test("A burst never mutates arrow state or leaves an orphaned visible slot", () =>
        {
            var archer = NewArcher();
            var arrow = NewArrow(archer, new Vector3(1f, 1f, 0f));
            var (targetGo, _) = NewTarget(new Vector3(1.2f, 1f, 0f));

            NativeHit(arrow, targetGo);
            GameObject root = EffectRoots().Single();
            Advance(PatchArcher_Impact.MaxEffectLifetime + .1f);
            Tick();

            Check(arrow._hasHit, "_hasHit owned by native");
            Check(!arrow._orientToVelocity, "orientation flag only written by native");
            Check(arrow._rigidbody.isKinematic, "rigidbody state owned by native");
            Check(!root.activeSelf, "no orphaned visible burst after the fade");
            Eq(0, EffectRoots().Count(r => r.activeSelf), "module leaves nothing visible behind");
            Eq(1, ParticleSystem.Plays, "native particle effect still owned by the native path");
            Check(!arrow._spriteRenderer.enabled, "arrow sprite hidden by the native path, not by the module");
        });
    }
}
