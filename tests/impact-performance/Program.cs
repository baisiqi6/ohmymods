// Performance-contract suite for PatchArcher_Impact's sustained pixel-flame computation.
//
// The production source is linked in via -p:ProductionSource; the Unity surface is the
// shared tests/archer-impact/Stubs.cs (Compile Include), so both suites observe the same
// call counters. What this suite proves:
//   * one Time.time recomputes exactly 36 noise pairs (72 Perlin calls) and every effect
//     and layer in the frame shares them — 16 effects x 3 layers no longer cost 3456;
//   * a new Time.time (same frame, FixedUpdate vs Update shape) recomputes once, and
//     switching back never silently reuses the other time's values;
//   * a rebuilt pool poses its first frame with a fresh computation, not a stale cache;
//   * no Unity Mathf.Round call remains on the vertex path;
//   * a pose whose quantized grid did not change uploads nothing while material colours
//     keep being written;
//   * local bounds are written once per mesh, never recalculated, and cover every
//     sampled vertex (jitter + drift + rounding included);
//   * posing sixteen effects allocates no meshes/materials/textures/native arrays.
// This is a call-count/behavior contract only: no real-game FPS claim is made or implied.

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
    private static Dictionary<FieldInfo, object> InitialFields;
    private static readonly List<string> Failures = new();
    private static readonly string[] LayerNames = { "KEM_ImpactCore", "KEM_ImpactMid", "KEM_ImpactOuter" };

    private static void Main()
    {
        InitialFields = typeof(PatchArcher_Impact)
            .GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .ToDictionary(f => f, f => f.GetValue(null));

        NoiseBudget();
        CacheKeying();
        Rounding();
        Uploads();
        BoundsContract();
        Allocations();

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
        if (MathF.Abs(want - got) > tolerance) throw new Exception($"{why}: expected {want}, got {got}");
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
        ModConfig.ArcherImpactEnabled.Value = false;
        PatchArcher_GreekImpact.Eligible = true;
        PatchArcher_GreekImpact.HeroOrigin = false;
        PatchArcher_GreekImpact.HeroEnabled = false;
        try { PatchArcher_Impact.Tick(); } catch { } // 走模块自己的关闭路径释放上一测试的池

        foreach (FieldInfo field in typeof(PatchArcher_Impact).GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
        {
            object value = field.GetValue(null);
            if (value is System.Collections.IDictionary dictionary) dictionary.Clear();
            else if (value is HashSet<string> strings) strings.Clear();
            else if (!field.IsLiteral && !field.IsInitOnly) field.SetValue(null, InitialFields[field]);
        }

        foreach (GameObject go in UnityEngine.Object.Created.OfType<GameObject>()) go.DestroyDeep();
        UnityEngine.Object.Created.Clear();
        SceneCounters();
        Time.time = 1f;
        Time.frameCount = 1;
        Time.timeScale = 1f;
        Log.Warnings.Clear();
        Log.Infos.Clear();
        Log.Errors.Clear();

        ModConfig.ArcherImpactEnabled.Value = true;
        ArcherOptionsScope.Active = true;
        ArcherOptionsScope.World = new GameObject("World");
        ArcherOptionsScope.Scene = 1;
        ArcherOptionsScope.Layer = new GameObject("WorldLayer");
        PatchArcher_Impact.Tick(); // 空转：不应有任何分配
        ResetCounts();
    }

    /// <summary>把全部可观测计数器归零；Setup 与逐测试重置共用。</summary>
    private static void SceneCounters()
    {
        GameObject.Activations.Clear();
        GameObject.SetActiveTrueCalls = 0;
        UnityEngine.Object.DestroyedObjects.Clear();
        UnityEngine.Object.DestroyRequests = 0;
        UnityEngine.Object.DoubleDestroys = 0;
        Renderer.SortingWrites = 0;
        Renderer.ThrowOnSortingWrite = false;
        Shader.Available = true;
        Shader.BlockPrimary = false;
        Shader.Finds = 0;
        Shader.FindLog.Clear();
        Transform.ThrowOnSetParent = false;
        Material.CreatedCount = 0;
        Texture2D.CreatedCount = 0;
        Texture2D.SetPixelCalls = 0;
        Texture2D.ApplyCalls = 0;
        Texture2D.ThrowOnSetPixel = false;
        Texture2D.ThrowOnApply = false;
        Mesh.CreatedCount = 0;
        Mesh.VertexUploads = 0;
        Mesh.UvWrites = 0;
        Mesh.TriangleWrites = 0;
        Mesh.BoundsRecalculations = 0;
        Mesh.BoundsWrites = 0;
        Mesh.RejectedGeometryWrites = 0;
        Mesh.ThrowOnVertexWrite = false;
        Mesh.ThrowOnUvWrite = false;
        Mesh.ThrowOnTriangleWrite = false;
        Il2CppStructArray<Vector3>.Allocations = 0;
        Il2CppStructArray<Vector3>.ManagedCopies = 0;
        Mathf.RoundCalls = 0;
        Mathf.PerlinCalls = 0;
        UnityEngine.Random.Calls = 0;
    }

    /// <summary>只归零观测计数，保留本测试已构造的夹具与生产缓存。</summary>
    private static void ResetCounts()
    {
        Mesh.CreatedCount = 0;
        Mesh.VertexUploads = 0;
        Mesh.UvWrites = 0;
        Mesh.TriangleWrites = 0;
        Mesh.BoundsRecalculations = 0;
        Mesh.BoundsWrites = 0;
        Mesh.RejectedGeometryWrites = 0;
        Material.CreatedCount = 0;
        Texture2D.CreatedCount = 0;
        Texture2D.SetPixelCalls = 0;
        Texture2D.ApplyCalls = 0;
        Il2CppStructArray<Vector3>.Allocations = 0;
        Il2CppStructArray<Vector3>.ManagedCopies = 0;
        UnityEngine.Object.DestroyRequests = 0;
        GameObject.Activations.Clear();
        GameObject.SetActiveTrueCalls = 0;
        Mathf.RoundCalls = 0;
        Mathf.PerlinCalls = 0;
        UnityEngine.Random.Calls = 0;
    }

    private static void Tick(float delta = 0f)
    {
        if (delta > 0f) { Time.time += delta; Time.frameCount++; }
        PatchArcher_Impact.Tick();
    }

    // -------- fixtures（直接走模块自己的 Capture/OnNativeHit 入口） --------

    private static void SpawnEffects(int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (i > 0 && i % PatchArcher_Impact.MaxSpawnsPerFrame == 0) { Time.time += .001f; Time.frameCount++; }
            SpawnEffect(i);
        }
    }

    private static void SpawnEffect(int index)
    {
        var go = new GameObject("Arrow") { layer = 6 };
        go.transform.SetParent(ArcherOptionsScope.Layer.transform, false);
        var arrow = go.AddComponent<Arrow>();
        arrow.transform.position = new Vector3(1f + index * .01f, 1f, 0f);
        var archerGo = new GameObject("Archer");
        archerGo.transform.SetParent(ArcherOptionsScope.Layer.transform, false);
        arrow.archer = archerGo.AddComponent<Archer>().gameObject;
        arrow._spriteRenderer = go.AddComponent<SpriteRenderer>();
        arrow._spriteRenderer.sortingLayerID = 7;
        arrow._spriteRenderer.sortingOrder = 123;

        var target = new GameObject("Enemy") { tag = "Enemy" };
        target.transform.position = new Vector3(1f + index * .01f, 1.01f, 0f);

        PatchArcher_Impact.HitCandidate candidate = PatchArcher_Impact.Capture(arrow, target, false);
        if (!candidate.Valid) throw new Exception("perf fixture: Capture rejected the arrow");
        arrow._hasHit = true; // Capture 之后置位：等价于原生已接受该次命中
        PatchArcher_Impact.OnNativeHit(arrow, candidate);
    }

    private static List<GameObject> Roots() =>
        UnityEngine.Object.Created.OfType<GameObject>().Where(g => g.name == "KEM_ArcherImpact" && !g.Destroyed).ToList();

    private static List<MeshRenderer> LayersOf(GameObject root) =>
        root.transform.children.Select(t => t.gameObject.GetComponent<MeshRenderer>()).ToList();

    private static Mesh LayerMesh(GameObject root, string name) =>
        root.transform.children.First(t => t.gameObject.name == name).gameObject.GetComponent<MeshFilter>().sharedMesh;

    private static bool IsPixelAligned(float value) =>
        MathF.Abs(value / PatchArcher_Impact.PixelSize - MathF.Round(value / PatchArcher_Impact.PixelSize)) < .001f;

    // ============================================================
    // 计算量：同一 Time.time 只算一次 36 对噪声
    // ============================================================

    private static void NoiseBudget()
    {
        Test("Sixteen effects x three layers at one Time.time cost exactly 72 Perlin calls (was 3456)", () =>
        {
            SpawnEffects(PatchArcher_Impact.MaxEffects);
            Eq(PatchArcher_Impact.MaxEffects, Roots().Count, "sixteen live bursts");
            ResetCounts();

            Tick(); // 同一 Time.time 驱动全部 16 x 3 层

            Eq(72, Mathf.PerlinCalls, "36 pairs x 2 = 72 Perlin calls for the whole frame");
            Check(Mesh.VertexUploads <= PatchArcher_Impact.MaxEffects * PatchArcher_Impact.LayerCount,
                "at most one vertex upload per layer per frame");

            ResetCounts();
            Tick(); // 同一 Time.time 再次驱动：缓存命中
            Eq(0, Mathf.PerlinCalls, "the same Time.time reuses the cached 36 pairs");
        });

        Test("A single effect at one Time.time costs the same 72 calls as sixteen", () =>
        {
            SpawnEffects(1);
            ResetCounts();

            Tick();

            Eq(72, Mathf.PerlinCalls, "the cache is keyed by time, not by effect or layer count");
        });
    }

    // ============================================================
    // 缓存 key：实际 time 值；换时/重建绝不串用
    // ============================================================

    private static void CacheKeying()
    {
        Test("A new Time.time recomputes once for all effects; switching times never cross-uses values", () =>
        {
            SpawnEffects(PatchArcher_Impact.MaxEffects);
            Tick();                 // T1：填充缓存
            float t1 = Time.time;
            ResetCounts();

            Tick(.0005f);           // T2：同一帧内的不同 Time.time（FixedUpdate/Update 形态）
            Eq(72, Mathf.PerlinCalls, "T2 recomputed the 36 pairs exactly once for all sixteen effects");

            ResetCounts();
            Time.time = t1;         // 同一帧切回 T1：不得复用 T2 的缓存值
            PatchArcher_Impact.Tick();
            Eq(72, Mathf.PerlinCalls, "switching back to T1 recomputes instead of reusing T2 values");
        });

        Test("Rebuild after close/world change recomputes on the first pose instead of reusing stale values", () =>
        {
            SpawnEffects(1);
            Tick();                 // 填充缓存（同一个 Time.time 下）

            ModConfig.ArcherImpactEnabled.Value = false;
            Tick();                 // 关闭 → ReleaseAll：噪声缓存必须失效
            Eq(0, Roots().Count, "pool released on disable");

            ModConfig.ArcherImpactEnabled.Value = true;
            SpawnEffect(9);         // 同一 Time.time 重新命中并重建
            ResetCounts();
            Tick();                 // 重建后的首次驱动

            Eq(72, Mathf.PerlinCalls, "first pose after the rebuild recomputes the 36 pairs (no stale reuse)");
        });
    }

    // ============================================================
    // 量化：System.MathF.Round 取代 Unity Mathf.Round
    // ============================================================

    private static void Rounding()
    {
        Test("Zero Unity Mathf.Round calls on the vertex path while every grid stays pixel-quantized", () =>
        {
            SpawnEffects(PatchArcher_Impact.MaxEffects);
            ResetCounts();
            for (int i = 0; i < 20; i++) Tick(.01f);

            Check(Mesh.VertexUploads > 0, "grids were re-quantized and uploaded during the run");
            Eq(0, Mathf.RoundCalls, "no Unity Mathf.Round call remains on the vertex path");
            foreach (GameObject root in Roots())
                foreach (string name in LayerNames)
                {
                    Mesh mesh = LayerMesh(root, name);
                    for (int i = 0; i < mesh.vertices.Length; i++)
                    {
                        Check(IsPixelAligned(mesh.vertices[i].x), name + ": x stays on the .22 pixel lattice");
                        Check(IsPixelAligned(mesh.vertices[i].y), name + ": y stays on the .22 pixel lattice");
                    }
                }
        });
    }

    // ============================================================
    // 上传：量化未变不上传，颜色照常写
    // ============================================================

    private static void Uploads()
    {
        Test("A pose with unchanged quantization uploads nothing while colours keep being written", () =>
        {
            SpawnEffects(4);
            Tick(); // 同帧首次驱动（drift 从 0 变化，仍可能上传）
            var materials = Roots().SelectMany(LayersOf).Where(l => l != null).Select(l => l.sharedMaterial).ToList();
            int writes = materials.Sum(m => m.InstanceWrites);

            ResetCounts();
            Tick(); // 同一 Time.time：量化必然未变

            Eq(0, Mesh.VertexUploads, "unchanged quantization: zero mesh uploads");
            Check(materials.Sum(m => m.InstanceWrites) > writes, "material colours are still written every pose");
            Eq(0, Mathf.PerlinCalls, "same Time.time: the noise cache is reused");
        });
    }

    // ============================================================
    // 包围盒：一次设定、涵盖全部局部极值
    // ============================================================

    private static void BoundsContract()
    {
        Test("Bounds are written once per mesh (never recalculated) and cover every sampled vertex of sixteen effects", () =>
        {
            SpawnEffects(PatchArcher_Impact.MaxEffects);
            List<GameObject> roots = Roots();
            Eq(PatchArcher_Impact.MaxEffects, roots.Count, "sixteen live bursts");
            Eq(roots.Count * PatchArcher_Impact.LayerCount, Mesh.BoundsWrites, "bounds written once per layer mesh at build");
            Eq(0, Mesh.BoundsRecalculations, "no RecalculateBounds");

            int samples = 0;
            for (int step = 0; step < 130; step++)
            {
                Tick(.02f);
                foreach (GameObject root in roots)
                {
                    if (!root.activeSelf) continue;
                    foreach (string name in LayerNames) { CheckBounds(root, name); samples++; }
                }
            }

            Check(samples > 100, "sampled the sixteen bursts across the full drift range");
            Eq(0, roots.Count(r => r.activeSelf), "all bursts finished inside the sampled window");
            Eq(roots.Count * PatchArcher_Impact.LayerCount, Mesh.BoundsWrites, "bounds are never rewritten while animating");
            Eq(0, Mesh.BoundsRecalculations, "still no RecalculateBounds");
        });
    }

    /// <summary>一次性设定的局部包围盒必须涵盖该层此刻的全部顶点（含 z 平面）。</summary>
    private static void CheckBounds(GameObject root, string name)
    {
        Mesh mesh = LayerMesh(root, name);
        Bounds bounds = mesh.bounds;
        Check(bounds.size.x * .5f >= PatchArcher_Impact.PixelSize * 3f - .0001f && bounds.size.z > 0f,
            name + ": conservative envelope (base+jitter+drift+quantization) with a valid Z thickness");
        for (int i = 0; i < mesh.vertices.Length; i++)
        {
            Vector3 vertex = mesh.vertices[i];
            Check(MathF.Abs(vertex.x - bounds.center.x) <= bounds.size.x * .5f + .0001f, name + ": vertex x inside static local bounds");
            Check(MathF.Abs(vertex.y - bounds.center.y) <= bounds.size.y * .5f + .0001f, name + ": vertex y inside static local bounds");
            Check(MathF.Abs(vertex.z - bounds.center.z) <= bounds.size.z * .5f + .0001f, name + ": vertex z inside static local bounds");
        }
    }

    // ============================================================
    // 分配：持续计算不产生任何新对象
    // ============================================================

    private static void Allocations()
    {
        Test("Posing sixteen effects allocates no meshes, materials, textures or native arrays", () =>
        {
            SpawnEffects(PatchArcher_Impact.MaxEffects);
            ResetCounts();
            for (int i = 0; i < 30; i++) Tick(.01f);

            Eq(0, Mesh.CreatedCount, "no mesh allocation while posing");
            Eq(0, Material.CreatedCount, "no material allocation while posing");
            Eq(0, Texture2D.CreatedCount, "no texture allocation while posing");
            Eq(0, Il2CppStructArray<Vector3>.Allocations, "no native vertex array allocation while posing");
            Eq(0, Il2CppStructArray<Vector3>.ManagedCopies, "no managed->native array copies");
            Eq(0, Mesh.UvWrites + Mesh.TriangleWrites, "no uv/index rebuild while posing");
            Eq(0, UnityEngine.Object.DestroyRequests, "no destroy requests while posing");
        });
    }
}
