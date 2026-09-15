// 红飘带 view 控制流冒烟测试（替身 Unity；真实 interop 由 ../interop-compile 负责）。
// 运行： C:/Users/ADMIN/dotnet8/dotnet.exe run --project tests/view-stub/HeroArcherCloth.ViewStub.Tests.csproj

using System;
using UnityEngine;

namespace KingdomEnhancedMod;

internal static class Program
{
    private const float Tick = 1f / 60f;
    private const float RunSpeed = 3.5f;
    private const float WalkSpeed = 1.3f;

    private static int _failed;
    private static int _passed;

    private static bool EdgesOnGrid(HeroArcherCloth.Handle h, int chain, int node)
    {
        int start = node == 7 ? 6 * 8 + 2 : node * 8;
        var a = h.Vertices[chain][start]; var b = h.Vertices[chain][start + 5];
        return Near(a.x*32,(float)Math.Round(a.x*32)) && Near(a.y*32,(float)Math.Round(a.y*32))
            && Near(b.x*32,(float)Math.Round(b.x*32)) && Near(b.y*32,(float)Math.Round(b.y*32))
            && Math.Abs(a.x-b.x)+Math.Abs(a.y-b.y)>=2f/32f-1e-6f;
    }

    private static int Main()
    {
        CreateBuildsOwnRibbonsAndHidesUntilVisible();
        AnchorsAreOffsetAndRibbonsStayDistinctAfterQuantization();
        DrawnRibbonsNeverPenetrateGround();
        SharedMaterialAndWhiteTextureAreBuiltOnce();
        HiddenFreezesSimulationAndRenderers();
        VisibleTickDrawsQuantizedTrailBehindTheBody();
        ReferenceLookIsMirroredAlphaLayerAndFlip();
        DestroyReleasesOwnObjectsAndReuseStartsClean();
        NullAndDeadHandlesAreSafe();

        Console.WriteLine();
        Console.WriteLine(_failed == 0 ? "ALL VIEW TESTS PASSED (" + _passed + ")" : _failed + " VIEW TEST(S) FAILED, " + _passed + " passed");
        return _failed == 0 ? 0 : 1;
    }

    private static void CreateBuildsOwnRibbonsAndHidesUntilVisible()
    {
        Fixture fixture = Fixture.Create();

        HeroArcherCloth.Handle handle = HeroArcherCloth.Create(fixture.Parent, fixture.Reference);
        Check("CreateBuildsOwnRibbonsAndHidesUntilVisible.handle", handle != null, "Create returned null");
        if (handle == null) return;

        Check("CreateBuildsOwnRibbonsAndHidesUntilVisible.parented", handle.RootTransform.parent == fixture.Parent,
            "cloth root must be a child of the same parent as the sprite");
        Vector3 expected = new Vector3(fixture.PivotLocal.x - 5f / 32f, fixture.PivotLocal.y + 14f / 32f, fixture.PivotLocal.z);
        Check("CreateBuildsOwnRibbonsAndHidesUntilVisible.shoulderOffset",
            Near(handle.RootTransform.localPosition.x, expected.x) && Near(handle.RootTransform.localPosition.y, expected.y),
            "root=" + handle.RootTransform.localPosition + " expected " + expected);

        Check("CreateBuildsOwnRibbonsAndHidesUntilVisible.twoRenderers",
            handle.Renderers[0] != null && handle.Renderers[1] != null && handle.Meshes[0] != handle.Meshes[1],
            "expected two own renderers with distinct meshes");
        Check("CreateBuildsOwnRibbonsAndHidesUntilVisible.meshShape",
            handle.Meshes[0].vertices.Length == 56 && handle.Meshes[0].triangles.Length == 84 // 7 segments x 2 flat faces
            && handle.Meshes[0].colors.Length == 56 && handle.Meshes[0].bounds.size.x > 0f,
            "verts=" + handle.Meshes[0].vertices.Length + " indices=" + handle.Meshes[0].triangles.Length);
        Check("CreateBuildsOwnRibbonsAndHidesUntilVisible.sortingBehindBody",
            handle.Renderers[0].sortingOrder == fixture.Reference.sortingOrder - 1
            && handle.Renderers[1].sortingOrder == fixture.Reference.sortingOrder - 2
            && handle.Renderers[0].sortingLayerID == fixture.Reference.sortingLayerID,
            "orders=" + handle.Renderers[0].sortingOrder + "/" + handle.Renderers[1].sortingOrder
            + " expected " + (fixture.Reference.sortingOrder - 1) + "/" + (fixture.Reference.sortingOrder - 2));
        Check("CreateBuildsOwnRibbonsAndHidesUntilVisible.startsHidden",
            !handle.Renderers[0].enabled && !handle.Renderers[1].enabled, "own renderers must start disabled");
        Vector3 restTip = Midpoint(handle, 0, 7);
        Check("CreateBuildsOwnRibbonsAndHidesUntilVisible.restPose",
            Math.Abs(Midpoint(handle, 0, 1).x) <= 0.5f / 32f && restTip.x <= -0.3f && restTip.y <= -0.3f
            && restTip.y >= -14f / 32f - 1e-4f,
            "first drawn pose must hang from the shoulder then fold behind on the ground, got " + restTip);

        HeroArcherCloth.Destroy(handle);
    }

    /// <summary>两条链锚点错开（后 1px、上 3px），量化到 1/32 格后仍是两条可见的带子。</summary>
    private static void AnchorsAreOffsetAndRibbonsStayDistinctAfterQuantization()
    {
        Fixture fixture = Fixture.Create();
        HeroArcherCloth.Handle handle = HeroArcherCloth.Create(fixture.Parent, fixture.Reference);
        if (handle == null) { Check("AnchorsAreOffsetAndRibbonsStayDistinctAfterQuantization", false, "Create returned null"); return; }

        for (int i = 0; i < 180; i++) HeroArcherCloth.Tick(handle, Tick, WalkSpeed, true);

        // 锚点偏移挂在链自己的 Transform 上（模拟点仍以锚点为原点）
        Vector3 primaryAnchor = handle.Renderers[0].transform.localPosition;
        Vector3 secondaryAnchor = handle.Renderers[1].transform.localPosition;
        Check("AnchorsAreOffsetAndRibbonsStayDistinctAfterQuantization.anchors",
            Near(primaryAnchor.x, 0f) && Near(primaryAnchor.y, 0f)
            && Near(secondaryAnchor.x, -1f / 32f) && Near(secondaryAnchor.y, 3f / 32f),
            "anchors=" + primaryAnchor + "/" + secondaryAnchor + " expected 0,0 and -1/32,3/32");

        float worstGap = 0f;
        bool allOnGrid = true;
        int worstNode = -1;
        for (int node = 0; node < HeroArcherClothChain.NodeCount; node++)
        {
            Vector3 a = Midpoint(handle, 0, node);
            Vector3 b = Midpoint(handle, 1, node);
            float gap = Math.Abs(a.x - b.x) + Math.Abs(a.y - b.y);
            if (gap > worstGap) { worstGap = gap; worstNode = node; }
            allOnGrid &= EdgesOnGrid(handle, 0, node);
            allOnGrid &= EdgesOnGrid(handle, 1, node);
        }
        Check("AnchorsAreOffsetAndRibbonsStayDistinctAfterQuantization.distinct",
            worstGap >= 2f / 32f,
            "worst quantized gap=" + worstGap + " at node " + worstNode + " expected >= 2px（不重叠成一条）");
        Check("AnchorsAreOffsetAndRibbonsStayDistinctAfterQuantization.visible",
            handle.Renderers[0].enabled && handle.Renderers[1].enabled && allOnGrid,
            "both ribbons must be drawn and stay on the 1/32 grid");

        HeroArcherCloth.Destroy(handle);
    }

    /// <summary>画出来的带子（顶点下缘）不穿地：模拟已按半宽离地，量化误差 ≤ 1/64 ≤ 最细半宽。</summary>
    private static void DrawnRibbonsNeverPenetrateGround()
    {
        Fixture fixture = Fixture.Create();
        HeroArcherCloth.Handle handle = HeroArcherCloth.Create(fixture.Parent, fixture.Reference);
        if (handle == null) { Check("DrawnRibbonsNeverPenetrateGround", false, "Create returned null"); return; }

        bool above = true;
        float worst = 1f;
        for (int i = 0; i < 600; i++) // 10s：静止 → 走 → 跑 → 停
        {
            float velocity = i < 150 ? 0f : i < 300 ? WalkSpeed : i < 450 ? RunSpeed : 0f;
            HeroArcherCloth.Tick(handle, Tick, velocity, true);
            for (int chain = 0; chain < HeroArcherCloth.ChainCount; chain++)
            {
                float groundY = handle.Chains[chain].FloorY - HeroArcherClothMath.HalfWidthUnits(HeroArcherClothChain.NodeCount - 1);
                Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3> vertices = handle.Meshes[chain].vertices;
                for (int v = 0; v < vertices.Length; v++)
                {
                    float bottom = vertices[v].y; // 顶点已含 ±半宽偏移
                    if (bottom < groundY - 1e-4f) { above = false; worst = Math.Min(worst, bottom - groundY); }
                }
            }
        }
        Check("DrawnRibbonsNeverPenetrateGround", above,
            "worst dip below ground = " + worst + " (期望 >= 0：飘带下缘不穿地)");

        HeroArcherCloth.Destroy(handle);
    }

    private static void SharedMaterialAndWhiteTextureAreBuiltOnce()
    {
        Fixture first = Fixture.Create();
        Fixture second = Fixture.Create(flipX: true);
        HeroArcherCloth.Handle a = HeroArcherCloth.Create(first.Parent, first.Reference);
        HeroArcherCloth.Handle b = HeroArcherCloth.Create(second.Parent, second.Reference);
        Check("SharedMaterialAndWhiteTextureAreBuiltOnce.handles", a != null && b != null, "two handles expected");
        if (a == null || b == null) return;

        Material materialA = a.Renderers[0].sharedMaterial;
        Check("SharedMaterialAndWhiteTextureAreBuiltOnce.singleMaterial",
            materialA != null && ReferenceEquals(materialA, b.Renderers[0].sharedMaterial)
            && ReferenceEquals(materialA, a.Renderers[1].sharedMaterial),
            "all cloth renderers must share exactly one material instance");
        Check("SharedMaterialAndWhiteTextureAreBuiltOnce.whiteTexture",
            materialA.mainTexture != null && materialA.mainTexture.Pixel.r == 1f && materialA.mainTexture.Pixel.g == 1f
            && materialA.mainTexture.Pixel.b == 1f,
            "material must use the module's own 1x1 white texture (never the hero atlas)");
        Check("SharedMaterialAndWhiteTextureAreBuiltOnce.queue", materialA.renderQueue == 3000,
            "queue=" + materialA.renderQueue);

        HeroArcherCloth.Destroy(a);
        HeroArcherCloth.Destroy(b);
    }

    private static void HiddenFreezesSimulationAndRenderers()
    {
        Fixture fixture = Fixture.Create();
        HeroArcherCloth.Handle handle = HeroArcherCloth.Create(fixture.Parent, fixture.Reference);
        if (handle == null) { Check("HiddenFreezesSimulationAndRenderers", false, "Create returned null"); return; }

        HeroArcherCloth.Tick(handle, Tick, RunSpeed, true);
        Vector3[] before = CopyVertices(handle, 0);
        for (int i = 0; i < 120; i++) HeroArcherCloth.Tick(handle, Tick, RunSpeed, false);
        Vector3[] after = CopyVertices(handle, 0);

        Check("HiddenFreezesSimulationAndRenderers.frozen", SameVertices(before, after),
            "hidden ticks must not advance the simulation");
        Check("HiddenFreezesSimulationAndRenderers.hidden", !handle.Renderers[0].enabled && !handle.Renderers[1].enabled,
            "hidden ticks must disable the own renderers");
        Check("HiddenFreezesSimulationAndRenderers.simUntouched", handle.Chains[0].PointsX[7] == handle.Chains[0].PointsX[7],
            "chain must stay finite");

        HeroArcherCloth.Destroy(handle);
    }

    private static void VisibleTickDrawsQuantizedTrailBehindTheBody()
    {
        Fixture fixture = Fixture.Create();
        HeroArcherCloth.Handle handle = HeroArcherCloth.Create(fixture.Parent, fixture.Reference);
        if (handle == null) { Check("VisibleTickDrawsQuantizedTrailBehindTheBody", false, "Create returned null"); return; }

        for (int i = 0; i < 120; i++) HeroArcherCloth.Tick(handle, Tick, RunSpeed, true);

        Check("VisibleTickDrawsQuantizedTrailBehindTheBody.enabled",
            handle.Renderers[0].enabled && handle.Renderers[1].enabled, "visible tick must enable own renderers");

        bool onGrid = true;
        bool matchesSimulation = true;
        for (int chain = 0; chain < HeroArcherCloth.ChainCount; chain++)
        {
            HeroArcherClothChain sim = handle.Chains[chain];
            for (int node = 0; node < HeroArcherClothChain.NodeCount; node++)
            {
                Vector3 center = Midpoint(handle, chain, node);
                onGrid &= EdgesOnGrid(handle, chain, node);
                float bridge = chain == 1 && node < 2 ? (2 - node) / 32f : 0f;
                matchesSimulation &= Math.Abs(center.x - sim.PointsX[node]) <= 1f / 32f + 1e-5f
                    && Math.Abs(center.y - (sim.PointsY[node] - bridge)) <= 1f / 32f + 1e-5f;
            }
        }
        Check("VisibleTickDrawsQuantizedTrailBehindTheBody.onGrid", onGrid,
            "drawn edges must land exactly on the 1/32 grid");
        Check("VisibleTickDrawsQuantizedTrailBehindTheBody.matchesSimulation", matchesSimulation,
            "view quantization must stay within one cell of the simulation point plus the explicit secondary attachment bridge");

        Vector3 primaryTip = Midpoint(handle, 0, 7);
        Vector3 secondaryTip = Midpoint(handle, 1, 7);
        Check("VisibleTickDrawsQuantizedTrailBehindTheBody.trailsBehind",
            primaryTip.x <= -0.2f && primaryTip.y <= -0.1f,
            "run tip=" + primaryTip + " expected behind (-x) and below the shoulder");
        Check("VisibleTickDrawsQuantizedTrailBehindTheBody.twoDistinctRibbons",
            Math.Abs(primaryTip.x - secondaryTip.x) + Math.Abs(primaryTip.y - secondaryTip.y) >= 0.01f,
            "the two ribbons must not collapse onto the same path");

        HeroArcherCloth.Destroy(handle);
    }

    private static void ReferenceLookIsMirroredAlphaLayerAndFlip()
    {
        Fixture fixture = Fixture.Create(alpha: 0.35f);
        HeroArcherCloth.Handle handle = HeroArcherCloth.Create(fixture.Parent, fixture.Reference);
        if (handle == null) { Check("ReferenceLookIsMirroredAlphaLayerAndFlip", false, "Create returned null"); return; }

        HeroArcherCloth.Tick(handle, Tick, RunSpeed, true);
        int writesAfterFirst = handle.Renderers[0].BlockWrites; // 首次 Tick 写入 alpha
        HeroArcherCloth.Tick(handle, Tick, RunSpeed, true);

        Check("ReferenceLookIsMirroredAlphaLayerAndFlip.alpha",
            handle.Renderers[0].lastBlock != null
            && Near(GetAlpha(handle.Renderers[0]), 0.35f) && Near(GetAlpha(handle.Renderers[1]), 0.35f),
            "reference color alpha must reach the cloth property block");
        Check("ReferenceLookIsMirroredAlphaLayerAndFlip.alphaWrittenOnce",
            handle.Renderers[0].BlockWrites == writesAfterFirst
            && ReferenceEquals(handle.Renderers[0].lastBlock, handle.Renderers[1].lastBlock),
            "unchanged alpha must not rewrite the property block (writes=" + handle.Renderers[0].BlockWrites + ")");

        fixture.Mirror();
        HeroArcherCloth.Tick(handle, Tick, RunSpeed, true);
        Check("ReferenceLookIsMirroredAlphaLayerAndFlip.flip",
            Near(handle.RootTransform.localScale.x, -1f) && Near(handle.RootTransform.localScale.y, 1f),
            "flipX must mirror the cloth root: scale=" + handle.RootTransform.localScale);
        Check("ReferenceLookIsMirroredAlphaLayerAndFlip.layer", handle.Root.layer == fixture.Reference.gameObject.layer,
            "cloth must follow the reference game object layer");

        HeroArcherCloth.Destroy(handle);
    }

    private static void DestroyReleasesOwnObjectsAndReuseStartsClean()
    {
        Fixture fixture = Fixture.Create();
        HeroArcherCloth.Handle first = HeroArcherCloth.Create(fixture.Parent, fixture.Reference);
        if (first == null) { Check("DestroyReleasesOwnObjectsAndReuseStartsClean", false, "Create returned null"); return; }
        for (int i = 0; i < 120; i++) HeroArcherCloth.Tick(first, Tick, RunSpeed, true);

        GameObject root = first.Root;
        Mesh meshA = first.Meshes[0];
        Mesh meshB = first.Meshes[1];
        HeroArcherCloth.Destroy(first);

        Check("DestroyReleasesOwnObjectsAndReuseStartsClean", root.destroyed && meshA.destroyed && meshB.destroyed,
            "root and own meshes must be destroyed (meshes do not follow GameObjects)");

        HeroArcherCloth.Handle second = HeroArcherCloth.Create(fixture.Parent, fixture.Reference);
        Vector3 tip = second == null ? new Vector3(99f, 99f, 0f) : Midpoint(second, 0, 7);
        Check("DestroyReleasesOwnObjectsAndReuseStartsClean.freshPose",
            second != null && tip.x <= -0.3f && tip.y <= -0.3f && Math.Abs(Midpoint(second, 0, 1).x) <= 0.5f / 32f,
            "reused slot must start from the grounded rest pose without the previous trail, tip=" + tip);

        HeroArcherCloth.Destroy(second);
    }

    private static void NullAndDeadHandlesAreSafe()
    {
        HeroArcherCloth.Tick(null, Tick, RunSpeed, true);
        HeroArcherCloth.Destroy(null);
        Check("NullAndDeadHandlesAreSafe.null", true, "");

        Fixture fixture = Fixture.Create();
        HeroArcherCloth.Handle handle = HeroArcherCloth.Create(fixture.Parent, fixture.Reference);
        if (handle == null) { Check("NullAndDeadHandlesAreSafe.dead", false, "Create returned null"); return; }
        HeroArcherCloth.Destroy(handle);
        HeroArcherCloth.Tick(handle, Tick, RunSpeed, true);
        HeroArcherCloth.Tick(handle, float.NaN, float.NaN, true);
        HeroArcherCloth.Destroy(handle);
        Check("NullAndDeadHandlesAreSafe.dead", true, "");

        Fixture empty = Fixture.Create();
        Check("NullAndDeadHandlesAreSafe.missingReference",
            HeroArcherCloth.Create(empty.Parent, null) == null && HeroArcherCloth.Create(null, empty.Reference) == null,
            "Create without parent/reference must fail closed");
    }

    // ============================================================
    // 工具
    // ============================================================

    private sealed class Fixture
    {
        internal GameObject Hero;
        internal GameObject PivotObject;
        internal SpriteRenderer Reference;
        internal Transform Parent;
        internal Vector3 PivotLocal;

        internal static Fixture Create(bool flipX = false, float alpha = 1f)
        {
            Fixture fixture = new Fixture();
            fixture.Hero = new GameObject("KEM_HeroArcherClothTestHero");
            fixture.PivotObject = new GameObject("KEM_HeroArcherClothTestPivot");
            fixture.Parent = fixture.Hero.transform;
            fixture.PivotObject.transform.SetParent(fixture.Parent, false);
            fixture.PivotLocal = new Vector3(0.2f, -1.5f, 0f);
            fixture.PivotObject.transform.localPosition = fixture.PivotLocal;

            SpriteRenderer reference = fixture.PivotObject.AddComponent<SpriteRenderer>();
            Material spriteMaterial = new Material(Shader.Find("Sprites/Default"));
            spriteMaterial.mainTexture = new Texture2D(48, 32);
            reference.Initialize(new Color(1f, 1f, 1f, alpha), flipX, 7, 12, spriteMaterial);
            reference.gameObject.layer = 9;
            fixture.Reference = reference;
            return fixture;
        }

        internal void Mirror() => Reference.Initialize(new Color(1f, 1f, 1f, 0.35f), true, 7, 12, Reference.sharedMaterial);
    }

    private static float GetAlpha(Renderer renderer)
    {
        return renderer.lastBlock != null
            && renderer.lastBlock.TryGetColor(Shader.PropertyToID("_Color"), out Color color) ? color.a : -1f;
    }

    private static Vector3 Midpoint(HeroArcherCloth.Handle handle, int chain, int node)
    {
        Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3> vertices = handle.Meshes[chain].vertices;
        int start = node == 7 ? 6 * 8 + 2 : node * 8;
        Vector3 a = vertices[start];
        Vector3 b = vertices[start + 5];
        return new Vector3((a.x + b.x) * 0.5f, (a.y + b.y) * 0.5f, 0f);
    }

    private static Vector3[] CopyVertices(HeroArcherCloth.Handle handle, int chain)
    {
        Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3> vertices = handle.Meshes[chain].vertices;
        Vector3[] copy = new Vector3[vertices.Length];
        for (int i = 0; i < copy.Length; i++) copy[i] = vertices[i];
        return copy;
    }

    private static bool SameVertices(Vector3[] a, Vector3[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (!Near(a[i].x, b[i].x) || !Near(a[i].y, b[i].y)) return false;
        }
        return true;
    }

    private static bool Near(float a, float b) => Math.Abs(a - b) <= 1e-4f;

    private static void Check(string name, bool condition, string detail)
    {
        if (condition)
        {
            _passed++;
            Console.WriteLine("PASS  " + name);
            return;
        }
        _failed++;
        Console.WriteLine("FAIL  " + name + " :: " + detail);
    }
}
