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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

        HookSurface();
        NativeAcceptance();
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

    private static void Same(Vector3 want, Vector3 got, string why)
    {
        if (MathF.Abs(want.x - got.x) > .001f || MathF.Abs(want.y - got.y) > .001f || MathF.Abs(want.z - got.z) > .001f)
            throw new Exception($"{why}: expected ({want.x},{want.y},{want.z}), got ({got.x},{got.y},{got.z})");
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
        UnityEngine.Object.DestroyedObjects.Clear();
        UnityEngine.Object.DestroyRequests = 0;
        UnityEngine.Object.DoubleDestroys = 0;
        GameObject.Activations.Clear();
        GameObject.SetActiveTrueCalls = 0;
        LineRenderer.SetPositionCalls = 0;
        LineRenderer.PositionCountWrites = 0;
        LineRenderer.ThrowOnPositionCountWrite = false;
        LineRenderer.ThrowOnPositionWrite = false;
        Renderer.SortingWrites = 0;
        Renderer.ThrowOnSortingWrite = false;
        Transform.ThrowOnSetParent = false;
        Material.CreatedCount = 0;
        Shader.Finds = 0;
        Shader.Available = true;
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
        LineRenderer.SetPositionCalls = 0;
        LineRenderer.PositionCountWrites = 0;
        Renderer.SortingWrites = 0;
        Material.CreatedCount = 0;
        Shader.Finds = 0;
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
        object[] prefixArgs = { arrow, null };
        try { PrefixMethod.Invoke(null, prefixArgs); }
        catch (TargetInvocationException e) { throw e.InnerException ?? e; }
        object state = prefixArgs[1];
        NativeHitObject(arrow, target, physicalHit); // native throw propagates: postfix skipped, as in Harmony
        try { PostfixMethod.Invoke(null, new object[] { arrow, state }); }
        catch (TargetInvocationException e) { throw e.InnerException ?? e; }
    }

    // -------- mirrored native Arrow.HitObject (reference/Arrow.cs, observation only) --------

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

    private static List<GameObject> BuiltRoots() =>
        UnityEngine.Object.Created.OfType<GameObject>().Where(g => g.name == "KEM_ArcherImpact").ToList();

    private static List<GameObject> EffectRoots() => BuiltRoots().Where(g => !g.Destroyed).ToList();

    private static List<GameObject> KEMObjects() =>
        UnityEngine.Object.Created.OfType<GameObject>().Where(g => g.name.StartsWith("KEM_")).ToList();

    private static List<LineRenderer> LayersOf(GameObject root) =>
        root.transform.children.Select(t => t.gameObject.GetComponent<LineRenderer>()).ToList();

    private static LineRenderer LayerOf(GameObject root, string name) =>
        root.transform.children.First(t => t.gameObject.name == name).gameObject.GetComponent<LineRenderer>();

    private static GameObject LayerGo(GameObject root, string name) =>
        root.transform.children.First(t => t.gameObject.name == name).gameObject;

    private static Material OwnedMaterial() =>
        UnityEngine.Object.Created.OfType<Material>().FirstOrDefault(m => !ReferenceEquals(m, TrailMaterial));

    private static float MaxRadius(LineRenderer lr) => lr.Positions.Max(p => MathF.Sqrt(p.x * p.x + p.y * p.y));
    private static float MinRadius(LineRenderer lr) => lr.Positions.Min(p => MathF.Sqrt(p.x * p.x + p.y * p.y));

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
        Test("Accepted hit spawns one three-layer burst at the hit point with the arrow renderer sorting", () =>
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
            Check(root.transform.parent == null, "burst is not parented into the arrow");
            Eq(6, root.layer, "burst copies the arrow GameObject layer");
            Eq(1, BuiltRoots().Count, "exactly one pool root built");

            var lrs = LayersOf(root);
            Eq(PatchArcher_Impact.LayerCount, lrs.Count, "three fire layers");
            foreach (LineRenderer lr in lrs)
            {
                Eq(PatchArcher_Impact.RingPoints, lr.Positions.Count, "13 indexed ring points per layer");
                Eq(true, lr.loop, "rings are closed");
                Eq(false, lr.useWorldSpace, "rings follow their own transform");
                Eq(true, lr.enabled, "layer renderer enabled");
                Check(ReferenceEquals(lr.sharedMaterial, TrailMaterial), "shared arrow line material reused");
                Eq(6, lr.gameObject.layer, "layer GameObject follows the arrow layer");
            }
            Eq(7, lrs[0].sortingLayerID, "sorting layer from arrow renderer");
            Eq(125, lrs[0].sortingOrder, "core draws above the arrow order");
            Eq(124, lrs[1].sortingOrder, "mid layer order");
            Eq(123, lrs[2].sortingOrder, "outer layer keeps the arrow order");
            Check(lrs.All(l => l.sortingOrder != 30000), "no hardcoded 30000 sorting order");
            Eq(0, Material.CreatedCount, "no material created when the arrow shares a line material");
            Eq(0, Shader.Finds, "no shader probe when a shared material exists");
            Eq(39, LineRenderer.SetPositionCalls, "geometry built once (13 points x 3 layers)");
            Check(lrs[0].ColorHistory.Count > 0 && lrs[2].ColorHistory.Count > 0, "vertex colors written on spawn");
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
            Eq(39, LineRenderer.SetPositionCalls, "geometry built for the new pool slot");
        });

        Test("Replacing the live world identity releases the old pool while the scope stays active", () =>
        {
            var archerA = NewArcher();
            var arrowA = NewArrow(archerA, new Vector3(1f, 1f, 0f), trailMaterial: false);
            var (targetA, _) = NewTarget(new Vector3(1.2f, 1f, 0f));
            NativeHit(arrowA, targetA);
            GameObject firstRoot = EffectRoots().Single();
            Material owned = OwnedMaterial();
            Eq(1, Material.CreatedCount, "world A material created");
            Check(owned != null, "world A material tracked");
            Check(ArcherOptionsScope.IsCurrent(arrowA), "fixture arrow starts current");

            // World A -> B with the scope still active: real identity replacement.
            ArcherOptionsScope.World = new GameObject("WorldB");
            ArcherOptionsScope.Layer = new GameObject("LayerB");
            ArcherOptionsScope.Scene = 7;
            ResetCounts();
            Tick();

            Check(!firstRoot, "old-world burst destroyed on identity change");
            Check(!owned, "old-world material destroyed on identity change");
            Eq(0, EffectRoots().Count, "no burst survives the world replacement");
            Eq(0, UnityEngine.Object.DoubleDestroys, "no duplicate destroy requests");

            arrowA._hasHit = false;
            NativeHit(arrowA, targetA);
            Eq(0, EffectRoots().Count, "stale-world arrow draws no flame in the new world");

            var archerB = NewArcher();
            var arrowB = NewArrow(archerB, new Vector3(3f, 3f, 0f), trailMaterial: false);
            var (targetB, _) = NewTarget(new Vector3(3.2f, 3f, 0f));
            ResetCounts();
            NativeHit(arrowB, targetB);

            Eq(1, EffectRoots().Count, "burst in the new world");
            Eq(1, Material.CreatedCount, "fresh material for the new world pool");
            Eq(39, LineRenderer.SetPositionCalls, "fresh geometry for the new world pool");
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
            Advance(PatchArcher_Impact.EffectLifetime + .1f);
            arrow._hasHit = false; // Arrow.OnEnable pool reuse
            ResetCounts();

            NativeHit(arrow, targetGo);

            Eq(1, EffectRoots().Count, "pooled arrow hit again spawns one burst");
            Eq(1, BuiltRoots().Count, "slot reused instead of a new pool root");
            Eq(0, LineRenderer.SetPositionCalls, "reused slot keeps its static geometry");
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
        });

        Test("Disabling destroys live bursts and own material; re-enabling rebuilds fresh", () =>
        {
            var archer = NewArcher();
            var arrow = NewArrow(archer, new Vector3(1f, 1f, 0f), trailMaterial: false); // forces the owned fallback material
            var (targetGo, _) = NewTarget(new Vector3(1.2f, 1f, 0f));
            NativeHit(arrow, targetGo);
            GameObject firstRoot = EffectRoots().Single();
            Eq(1, Material.CreatedCount, "one fallback material created");
            Material owned = OwnedMaterial();
            Check(owned != null, "fallback material tracked");
            Check(!ReferenceEquals(owned, TrailMaterial), "fallback material is module owned");

            ModConfig.ArcherImpactEnabled.Value = false;
            Tick();

            Check(!firstRoot, "live burst destroyed on disable");
            Check(!owned, "own material destroyed on disable");
            Eq(0, EffectRoots().Count, "no live burst remains");
            Eq(0, UnityEngine.Object.DoubleDestroys, "no duplicate destroy requests");
            Check(!TrailMaterial.Destroyed, "borrowed materials are never destroyed");

            ModConfig.ArcherImpactEnabled.Value = true;
            arrow._hasHit = false;
            ResetCounts();
            NativeHit(arrow, targetGo);

            var rebuilt = EffectRoots();
            Eq(1, rebuilt.Count, "fresh burst after re-enable");
            Check(!ReferenceEquals(rebuilt[0], firstRoot), "a new pool root was built");
            Eq(1, Material.CreatedCount, "one fresh material for the new enable");
            Eq(39, LineRenderer.SetPositionCalls, "geometry rebuilt once for the new pool slot");
        });
    }

    // ============================================================
    // Budgets and pool bounds
    // ============================================================

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

        Test("At most 24 bursts start within any rolling second", () =>
        {
            var archer = NewArcher();
            var (targetGo, _) = NewTarget(new Vector3(1f, 1f, 0f));
            var arrows = new List<Arrow>();
            for (int i = 0; i < 30; i++) arrows.Add(NewArrow(archer, new Vector3(1f + i * .01f, 1f, 0f)));
            ResetCounts();

            foreach (Arrow arrow in arrows)
            {
                NativeHit(arrow, targetGo);
                Tick(1f / 30f);
            }

            List<(int Frame, float Time, string Name)> spawns = GameObject.Activations;
            Eq(PatchArcher_Impact.MaxSpawnsPerSecond, spawns.Count, "rolling-second spawn cap");
            foreach (var spawn in spawns)
            {
                int withinWindow = spawns.Count(s => s.Time >= spawn.Time && s.Time < spawn.Time + 1f);
                Check(withinWindow <= PatchArcher_Impact.MaxSpawnsPerSecond, "no rolling second exceeds 24 spawns");
            }

            Advance(1.1f); // quiet second empties the window
            int before = GameObject.Activations.Count;
            foreach (Arrow arrow in arrows.Take(10))
            {
                arrow._hasHit = false;
                NativeHit(arrow, targetGo);
                Tick(1f / 30f);
            }
            Eq(10, GameObject.Activations.Count - before, "window recovers after a quiet second");
        });

        Test("Pool is bounded: at most 16 live bursts and 48 ring renderers, extra hits are dropped", () =>
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
            Check(renderers <= PatchArcher_Impact.MaxEffects * PatchArcher_Impact.LayerCount, "at most 48 ring renderers");
            Check(UnityEngine.Object.Created.OfType<LineRenderer>().Count() <= PatchArcher_Impact.MaxEffects * PatchArcher_Impact.LayerCount,
                "bounded LineRenderer count");

            Advance(PatchArcher_Impact.EffectLifetime + .1f);
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

        Test("Burst lifetime stays within the contract bound and fades out", () =>
        {
            var archer = NewArcher();
            var arrow = NewArrow(archer, new Vector3(1f, 1f, 0f));
            var (targetGo, _) = NewTarget(new Vector3(1.2f, 1f, 0f));
            NativeHit(arrow, targetGo);
            GameObject root = EffectRoots().Single();
            LineRenderer core = LayerOf(root, "KEM_ImpactCore");

            Check(PatchArcher_Impact.EffectLifetime <= .6f, "lifetime within 0.6s");
            float scaleAtSpawn = LayerGo(root, "KEM_ImpactCore").transform.localScale.x;
            float alphaAtSpawn = core.ColorHistory[^1].a;

            Tick(.2f);
            Check(LayerGo(root, "KEM_ImpactCore").transform.localScale.x > scaleAtSpawn, "ring grows after spawn");
            Check(core.WidthHistory[^1] > core.WidthHistory[0], "core line thickens during growth");

            Tick(.24f);
            Check(core.ColorHistory[^1].a < alphaAtSpawn, "fade reduces alpha");
            Check(root.activeSelf, "still visible just before the lifetime ends");

            Tick(.1f);
            Check(!root.activeSelf, "hidden after the lifetime");
            Check(root.transform.children.All(t => ReferenceEquals(t.gameObject.GetComponent<LineRenderer>().sharedMaterial, TrailMaterial)),
                "geometry and material retained for reuse after fading");
        });

        Test("Outer ring stays thinner, wider and more jagged than the bright core", () =>
        {
            var archer = NewArcher();
            var arrow = NewArrow(archer, new Vector3(1f, 1f, 0f));
            var (targetGo, _) = NewTarget(new Vector3(1.2f, 1f, 0f));
            NativeHit(arrow, targetGo);
            GameObject root = EffectRoots().Single();
            LineRenderer core = LayerOf(root, "KEM_ImpactCore");
            LineRenderer outer = LayerOf(root, "KEM_ImpactOuter");

            Check(core.WidthHistory[^1] > outer.WidthHistory[^1], "core is thicker than the outer ring");
            Check(core.ColorHistory[^1].Luminance > outer.ColorHistory[^1].Luminance, "core is brighter than the outer ring");
            Check(MaxRadius(outer) > MaxRadius(core), "outer ring reaches further");

            float coreSpread = (MaxRadius(core) - MinRadius(core)) / MaxRadius(core);
            float outerSpread = (MaxRadius(outer) - MinRadius(outer)) / MaxRadius(outer);
            Check(outerSpread > coreSpread, "outer ring is more jagged than the core");
        });
    }

    // ============================================================
    // Pool reuse, resources and failure handling
    // ============================================================

    private static void PoolAndFailures()
    {
        Test("Slots are reused across hits: no per-hit objects, materials or geometry writes", () =>
        {
            var archer = NewArcher();
            var (targetGo, _) = NewTarget(new Vector3(1f, 1f, 0f));
            var arrow = NewArrow(archer, new Vector3(1f, 1f, 0f));
            NativeHit(arrow, targetGo); // warm the single-slot pool
            Advance(PatchArcher_Impact.EffectLifetime + .1f);
            ResetCounts();

            for (int i = 0; i < 10; i++)
            {
                arrow._hasHit = false;
                arrow.transform.position = new Vector3(1f + i, 2f, 0f);
                NativeHit(arrow, targetGo);
                Advance(PatchArcher_Impact.EffectLifetime + .1f);
            }

            Eq(1, BuiltRoots().Count, "single slot serves all hits");
            Eq(0, Material.CreatedCount, "no material per hit");
            Eq(0, LineRenderer.SetPositionCalls, "no geometry rebuild per hit");
            Eq(0, LineRenderer.PositionCountWrites, "no positionCount writes per hit");
        });

        Test("Reused slot follows the new hit position and renderer sorting", () =>
        {
            var archer = NewArcher();
            var (targetGo, _) = NewTarget(new Vector3(1f, 1f, 0f));
            var arrow = NewArrow(archer, new Vector3(1f, 1f, 0f), sortingLayer: 4, sortingOrder: 10);
            NativeHit(arrow, targetGo);
            Advance(PatchArcher_Impact.EffectLifetime + .1f);

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

            Eq(0, UnityEngine.Object.DestroyRequests, "no duplicate destroy request for the already-dead root");
            Eq(0, UnityEngine.Object.DoubleDestroys, "no double destroy");
            arrow._hasHit = false;
            NativeHit(arrow, targetGo);
            Eq(1, EffectRoots().Count, "next hit rebuilds the slot");
            Eq(2, BuiltRoots().Count, "the destroyed root is replaced by a new one");
        });

        Test("Shader and arrow material both missing: burst skipped, backed off, then recovered", () =>
        {
            var archer = NewArcher();
            var arrow = NewArrow(archer, new Vector3(1f, 1f, 0f), trailMaterial: false);
            var (targetGo, damageable) = NewTarget(new Vector3(1.2f, 1f, 0f));
            Shader.Available = false;
            ResetCounts();

            NativeHit(arrow, targetGo);
            arrow._hasHit = false;
            NativeHit(arrow, targetGo);

            Eq(2, damageable.HitCount, "native damage kept while the material is unavailable");
            Eq(0, EffectRoots().Count, "no burst without a usable material");
            Eq(0, BuiltRoots().Count, "no pool allocation without a material");
            Eq(1, Log.Warnings.Count(w => w.Contains("impact burst disabled")), "warning logged once");
            Eq(1, Shader.Finds, "not probed again inside the backoff window");

            Shader.Available = true;
            Tick(1f);
            arrow._hasHit = false;
            NativeHit(arrow, targetGo);
            Eq(0, EffectRoots().Count, "no retry inside the backoff window");
            Eq(1, Shader.Finds, "no shader probing during backoff");

            Tick(5f);
            arrow._hasHit = false;
            NativeHit(arrow, targetGo);
            Eq(1, EffectRoots().Count, "burst appears once the shader is available again");
            Eq(1, Material.CreatedCount, "exactly one fallback material created");
        });

        Test("Fallback material is created once and shared by every pool slot", () =>
        {
            var archer = NewArcher();
            var (targetGo, _) = NewTarget(new Vector3(1f, 1f, 0f));
            var arrows = new List<Arrow>();
            for (int i = 0; i < 18; i++) arrows.Add(NewArrow(archer, new Vector3(1f + i * .01f, 1f, 0f), trailMaterial: false));
            ResetCounts();

            for (int frame = 0; frame < 5; frame++)
            {
                foreach (Arrow arrow in arrows.Skip(frame * 4).Take(4)) NativeHit(arrow, targetGo);
                Tick(1f / 60f);
            }

            Eq(PatchArcher_Impact.MaxEffects, EffectRoots().Count(r => r.activeSelf), "sixteen live bursts share the fallback");
            Eq(1, Material.CreatedCount, "exactly one material for the whole pool");
            Material material = OwnedMaterial();
            Check(material != null, "fallback material exists");
            Check(EffectRoots().SelectMany(LayersOf).All(l => ReferenceEquals(l.sharedMaterial, material)),
                "all ring renderers share one material");
        });

        Test("Pool build failure cleans up partial objects, keeps the native path intact, recovers later", () =>
        {
            var archer = NewArcher();
            var arrow = NewArrow(archer, new Vector3(1f, 1f, 0f));
            var (targetGo, damageable) = NewTarget(new Vector3(1.2f, 1f, 0f));
            LineRenderer.ThrowOnPositionCountWrite = true;
            ResetCounts();

            NativeHit(arrow, targetGo);

            Eq(1, damageable.HitCount, "native damage unaffected by the visual failure");
            Eq(1, Pool.DespawnCalls, "native despawn unaffected");
            Eq(0, EffectRoots().Count, "no live burst after a build failure");
            Eq(0, BuiltRoots().Count(g => !g.Destroyed), "partial pool root destroyed");
            Eq(0, KEMObjects().Count(g => !g.Destroyed), "no orphan KEM object survives the failure");
            Eq(2, UnityEngine.Object.DestroyRequests, "partial root and its orphaned child destroyed individually");
            Eq(1, Log.Warnings.Count(w => w.Contains("pool build failed")), "build failure logged once");

            arrow._hasHit = false;
            NativeHit(arrow, targetGo);
            Eq(1, Log.Warnings.Count(w => w.Contains("pool build failed")), "repeated failure does not spam the log");
            Eq(0, EffectRoots().Count, "still no burst while the failure persists");

            LineRenderer.ThrowOnPositionCountWrite = false;
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
            Eq(1, UnityEngine.Object.DestroyRequests, "own partial objects cleaned up exactly once");
            Eq(1, Log.Warnings.Count(w => w.Contains("spawn failed")), "spawn failure logged once");

            Renderer.ThrowOnSortingWrite = false;
            arrow._hasHit = false;
            Tick(5f); // reuse failures back off as well; recovery must wait out the window
            ResetCounts();
            NativeHit(arrow, targetGo);
            Eq(1, EffectRoots().Count, "next hit spawns normally after the write failure clears");
            Eq(0, UnityEngine.Object.DoubleDestroys, "no double destroy requests");
        });

        Test("Build failure with a borrowed material cannot rebuild repeatedly inside one frame", () =>
        {
            var archer = NewArcher();
            var arrow = NewArrow(archer, new Vector3(1f, 1f, 0f)); // trail material -> borrowed shared material
            var (targetGo, damageable) = NewTarget(new Vector3(1.2f, 1f, 0f));
            NativeHit(arrow, targetGo);
            Eq(1, EffectRoots().Count, "first burst");
            Eq(0, Material.CreatedCount, "borrowed material, nothing created");
            int hitsBefore = damageable.HitCount;
            UnityEngine.Object.Destroy(EffectRoots().Single()); // geometry lost (scene unload)
            LineRenderer.ThrowOnPositionCountWrite = true;
            ResetCounts();

            for (int i = 0; i < 6; i++)
            {
                arrow._hasHit = false;
                NativeHit(arrow, targetGo);
            }

            Eq(6, damageable.HitCount - hitsBefore, "native damage kept through every failed attempt");
            Eq(0, EffectRoots().Count, "no live burst while the build fails");
            Eq(2, BuiltRoots().Count, "single failed rebuild attempt: frame budget plus backoff hold");
            Eq(1, Log.Warnings.Count(w => w.Contains("pool build failed")), "failure logged once");

            ModConfig.ArcherImpactEnabled.Value = false;
            Tick();
            Check(!TrailMaterial.Destroyed, "pool release never destroys a borrowed material");
            ModConfig.ArcherImpactEnabled.Value = true;

            LineRenderer.ThrowOnPositionCountWrite = false;
            Tick(5f); // past the failure backoff
            arrow._hasHit = false;
            NativeHit(arrow, targetGo);
            Eq(1, EffectRoots().Count, "recovers after the backoff");
            Check(!TrailMaterial.Destroyed, "borrowed material still intact after recovery");
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
            Eq(2, UnityEngine.Object.DestroyRequests, "orphan child and partial root destroyed individually");
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
            Advance(PatchArcher_Impact.EffectLifetime + .1f);
            Tick();

            Check(arrow._hasHit, "_hasHit owned by native");
            Check(!arrow._orientToVelocity, "orientation flag only written by native");
            Check(arrow._rigidbody.isKinematic, "rigidbody state owned by native");
            Check(!root.activeSelf, "no orphaned visible burst after the fade");
            Eq(0, EffectRoots().Count(r => r.activeSelf), "module leaves nothing visible behind");
        });
    }
}
