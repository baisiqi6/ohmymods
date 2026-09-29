// Coin courier visuals regression suite (managed doubles; no Unity runtime, no game).
// Verifies the frame table / phase math, the view lifecycle, the teleport-FX handle/generation
// contract, injected resource-failure cleanup, and cross-checks the runtime tables against the
// generated manifest.json. Passing this suite does not establish real rendering or in-game
// behaviour; the contact sheet / GIF previews and the in-game pass own that evidence.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using KingdomEnhancedMod;
using UnityEngine;

internal static class Program
{
    private static int passed, failed;
    private static string[] arguments = Array.Empty<string>();
    private static readonly BindingFlags StaticFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

    private static int Main(string[] args)
    {
        arguments = args;
        KingdomEnhancedPlugin.Instance = new KingdomEnhancedPlugin();

        Test("pose table covers the approved layout", PoseTableCoversLayout);
        Test("loop poses wrap on authored durations", LoopPosesWrap);
        Test("one-shot poses clamp and reject invalid phases", OneShotPosesClamp);
        Test("phase sweep stays inside each pose range", PhaseSweepStaysInRange);
        Test("view lifecycle keeps absolute mod sorting and reuses reference material", ViewLifecycle);
        Test("render placement, facing and visibility", RenderPlacementFacingVisibility);
        Test("render frame changes are idempotent per phase", RenderFrameIdempotence);
        Test("teleport fx group fades, shrinks and releases", TeleportFxGroupLifecycle);
        Test("teleport fx stripes stand over the foot anchor and cover the body", TeleportFxStripeGeometry);
        Test("teleport fx vertical stripes run down the body over the same foot anchor", TeleportFxVerticalStripeGeometry);
        Test("teleport fx stripes taper through one static four-key width curve", TeleportFxCurveContract);
        Test("teleport fx stripes enter staggered inside the reveal window", TeleportFxStaggeredEntrance);
        Test("teleport fx light slant is fixed, bidirectional and midpoint-stable", TeleportFxSlantContract);
        Test("teleport fx pool reuse switches geometry between styles", TeleportFxPoolReuseSwitchesStyle);
        Test("teleport fx refuses an unknown style without consuming a slot", TeleportFxRejectsUnknownStyle);
        Test("teleport fx tick is once per frame", TeleportFxFrameDedup);
        Test("teleport fx cancel respects generations", TeleportFxCancellation);
        Test("teleport fx generations survive clear", TeleportFxGenerationSurvivesClear);
        Test("teleport fx keeps a fixed group cap and surface", TeleportFxComponentSurface);
        Test("teleport fx failure leaves no leaked objects", TeleportFxFailureCleanup);
        Test("visuals create failure leaves no leaked objects", VisualsCreateFailureCleanup);
        Test("atlas decode failure cleans sprites and texture", DecodeAtlasCleanup);
        Test("manifest matches the runtime tables", ManifestCrossCheck);

        Console.WriteLine("coin-courier-visuals: " + passed + " passed, " + failed + " failed");
        return failed == 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------- checks

    private static void Check(bool condition, string why)
    {
        if (!condition) throw new Exception(why);
    }

    private static void Equal(int expected, int actual, string why)
    {
        if (expected != actual) throw new Exception(why + ": expected " + expected + ", got " + actual);
    }

    private static void Equal(string expected, string actual, string why)
    {
        if (expected != actual) throw new Exception(why + ": expected " + expected + ", got " + actual);
    }

    private static void Equal(float expected, float actual, string why)
    {
        if (expected != actual) throw new Exception(why + ": expected " + expected + ", got " + actual);
    }

    private static void Near(float expected, float actual, float tolerance, string why)
    {
        if (MathF.Abs(expected - actual) > tolerance)
            throw new Exception(why + ": expected " + expected + ", got " + actual);
    }

    private static void Test(string name, Action body)
    {
        Cleanup();
        try
        {
            body();
            passed++;
            Console.WriteLine("PASS " + name);
        }
        catch (Exception error)
        {
            failed++;
            Console.WriteLine("FAIL " + name + ": " + error.GetBaseException().Message);
        }
        finally
        {
            Cleanup();
        }
    }

    private static void Cleanup()
    {
        try { CoinCourierVisuals.ShutdownModule(); } catch { }
        try { CoinCourierTeleportFx.Clear(); } catch { }
        SetStatic(typeof(CoinCourierVisuals), "_sprites", null);
        SetStatic(typeof(CoinCourierVisuals), "_atlasState", 0);
        SetStatic(typeof(CoinCourierTeleportFx), "_lastTickFrame", int.MinValue);
        ClearWarned(typeof(CoinCourierVisuals));
        ClearWarned(typeof(CoinCourierTeleportFx));
        GameObject.All.Clear();
        GameObject.ResetCounters();
        SpriteRenderer.SpriteWrites = 0;
        UnityEngine.Object.DestroyCalls = 0;
        Time.frameCount = 0;
        ImageConversion.Reset();
        Sprite.ResetCounters();
        Texture2D.ResetCounters();
        Texture2D.FillAlpha = 0;
    }

    private static void SetStatic(Type type, string field, object value)
    {
        var info = type.GetField(field, StaticFlags);
        if (info == null) throw new Exception("reflection target missing: " + type.Name + "." + field);
        info.SetValue(null, value);
    }

    private static object GetStatic(Type type, string field)
    {
        var info = type.GetField(field, StaticFlags);
        if (info == null) throw new Exception("reflection target missing: " + type.Name + "." + field);
        return info.GetValue(null);
    }

    private static void ClearWarned(Type type)
    {
        var info = type.GetField("Warned", StaticFlags);
        if (info != null) ((HashSet<string>)info.GetValue(null)).Clear();
    }

    private static int Frame(CoinCourierPose pose, float phase) => CoinCourierPoseTable.FrameIndex(pose, phase);

    private static LineRenderer Dash(int index)
    {
        var gameObject = GameObject.All.First(go => !go.Destroyed && go.name == "KEM_CoinCourierTeleportDash" + index);
        return gameObject.GetComponent<LineRenderer>();
    }

    private static GameObject DashObject(int index)
        => GameObject.All.First(go => !go.Destroyed && go.name == "KEM_CoinCourierTeleportDash" + index);

    private static int EnabledDashCount() => GameObject.All.Count(go => !go.Destroyed && go.name.StartsWith("KEM_CoinCourierTeleportDash")
        && go.GetComponent<LineRenderer>() is { enabled: true });

    private static int EffectGroupCount() => GameObject.All.Count(go => !go.Destroyed && go.name == "KEM_CoinCourierTeleportFxEffect");

    // ---------------------------------------------------------------- tests

    private static void PoseTableCoversLayout()
    {
        Equal(56, CoinCourierPoseTable.CellPixels, "cell px");
        Equal(32f, CoinCourierPoseTable.PixelsPerUnit, "ppu");
        Equal(28, CoinCourierPoseTable.PivotPixelX, "pivot x");
        Equal(12, CoinCourierPoseTable.PivotPixelY, "pivot y");
        Equal(32, CoinCourierPoseTable.FrameCount, "frame count");
        var spans = new (CoinCourierPose pose, int first, int last, float spf, bool loop)[]
        {
            (CoinCourierPose.Idle, 28, 31, 0.50f, true),
            (CoinCourierPose.Leisure, 8, 11, 0.60f, true),
            (CoinCourierPose.Run, 0, 7, 0.075f, true),
            (CoinCourierPose.Collect, 12, 15, 0.18f, false),
            (CoinCourierPose.Deliver, 16, 19, 0.20f, false),
            (CoinCourierPose.Jump, 20, 23, 0.10f, false),
            (CoinCourierPose.Fall, 24, 25, 0.12f, false),
            (CoinCourierPose.Land, 26, 27, 0.12f, false),
        };
        var covered = new List<int>();
        foreach (var (pose, first, last, spf, loop) in spans)
        {
            Equal(first, CoinCourierPoseTable.FirstFrame(pose), pose + " first");
            Equal(last, CoinCourierPoseTable.LastFrame(pose), pose + " last");
            Equal(last - first + 1, CoinCourierPoseTable.PoseFrameCount(pose), pose + " frames");
            Near(spf, CoinCourierPoseTable.PoseSecondsPerFrame(pose), 1e-6f, pose + " seconds per frame");
            Check(loop == CoinCourierPoseTable.IsLooping(pose), pose + " looping policy");
            for (int index = first; index <= last; index++) covered.Add(index);
        }
        Equal(32, covered.Count, "spanned frames");
        Check(covered.Distinct().Count() == 32, "no frame is claimed twice");
        Check(covered.Min() == 0 && covered.Max() == 31, "frames 0..31 fully covered");
    }

    private static void LoopPosesWrap()
    {
        Equal(0, Frame(CoinCourierPose.Run, 0f), "run start");
        Equal(0, Frame(CoinCourierPose.Run, 0.03f), "run first bin");
        Equal(1, Frame(CoinCourierPose.Run, 0.10f), "run second frame");
        Equal(3, Frame(CoinCourierPose.Run, 0.23f), "run mid frame");
        Equal(5, Frame(CoinCourierPose.Run, 0.40f), "run late frame");
        Equal(6, Frame(CoinCourierPose.Run, 0.52f), "run penultimate frame");
        Equal(7, Frame(CoinCourierPose.Run, 0.58f), "run last frame");
        Equal(0, Frame(CoinCourierPose.Run, 0.60f), "run wraps exactly at the authored duration");
        Equal(0, Frame(CoinCourierPose.Run, 0.605f), "run wraps shortly after the duration");
        Equal(1, Frame(CoinCourierPose.Run, 0.68f), "run keeps phase after a wrap");
        Equal(0, Frame(CoinCourierPose.Run, 1.20f), "run double wrap");

        Equal(28, Frame(CoinCourierPose.Idle, 0f), "idle start");
        Equal(29, Frame(CoinCourierPose.Idle, 0.55f), "idle second frame");
        Equal(30, Frame(CoinCourierPose.Idle, 1.10f), "idle third frame");
        Equal(31, Frame(CoinCourierPose.Idle, 1.60f), "idle last frame");
        Equal(28, Frame(CoinCourierPose.Idle, 2.00f), "idle wraps at 2s");
        Equal(28, Frame(CoinCourierPose.Idle, 6.05f), "idle multi-wrap");

        Equal(8, Frame(CoinCourierPose.Leisure, 0f), "leisure start");
        Equal(9, Frame(CoinCourierPose.Leisure, 0.70f), "leisure second frame");
        Equal(11, Frame(CoinCourierPose.Leisure, 2.30f), "leisure last frame");
        Equal(8, Frame(CoinCourierPose.Leisure, 2.40f), "leisure wraps at 2.4s");

        Equal(0, Frame(CoinCourierPose.Run, float.NaN), "NaN phase falls back to the first frame");
        Equal(0, Frame(CoinCourierPose.Run, -3f), "negative phase falls back to the first frame");
        Equal(0, Frame(CoinCourierPose.Run, float.PositiveInfinity), "infinite phase falls back to the first frame");

        int previous = -1;
        for (int step = 0; step < 600; step++)
        {
            int frame = Frame(CoinCourierPose.Run, 0.6f * step / 600f);
            Check(frame >= previous, "run sweep is non-decreasing inside one cycle");
            previous = frame;
        }
        Equal(7, previous, "run sweep reaches the last frame");
    }

    private static void OneShotPosesClamp()
    {
        Equal(12, Frame(CoinCourierPose.Collect, 0f), "collect start");
        Equal(12, Frame(CoinCourierPose.Collect, 0.10f), "collect first bin");
        Equal(13, Frame(CoinCourierPose.Collect, 0.20f), "collect second frame");
        Equal(14, Frame(CoinCourierPose.Collect, 0.40f), "collect third frame");
        Equal(15, Frame(CoinCourierPose.Collect, 0.60f), "collect last frame");
        Equal(15, Frame(CoinCourierPose.Collect, 0.72f), "collect clamps at the authored duration");
        Equal(15, Frame(CoinCourierPose.Collect, 50f), "collect stays on the last frame");
        Equal(15, Frame(CoinCourierPose.Collect, float.PositiveInfinity), "collect infinite phase clamps");
        Equal(12, Frame(CoinCourierPose.Collect, float.NaN), "collect NaN falls back to the first frame");
        Equal(12, Frame(CoinCourierPose.Collect, -1f), "collect negative falls back to the first frame");

        Equal(19, Frame(CoinCourierPose.Deliver, 0.79f), "deliver clamps to the last frame");
        Equal(19, Frame(CoinCourierPose.Deliver, 10f), "deliver holds the last frame");
        Equal(23, Frame(CoinCourierPose.Jump, 0.35f), "jump reaches the tuck frame");
        Equal(23, Frame(CoinCourierPose.Jump, 5f), "jump holds the tuck frame");
        Equal(25, Frame(CoinCourierPose.Fall, 0.13f), "fall reaches the last frame");
        Equal(25, Frame(CoinCourierPose.Fall, 5f), "fall holds the last frame");
        Equal(27, Frame(CoinCourierPose.Land, 0.13f), "land reaches the stand-up frame");
        Equal(27, Frame(CoinCourierPose.Land, 5f), "land holds the stand-up frame");

        Check(Frame(CoinCourierPose.Collect, 0.3f) == Frame(CoinCourierPose.Collect, 0.3f), "same phase is idempotent");
        int weird = CoinCourierPoseTable.FrameIndex((CoinCourierPose)99, 0.5f);
        Check(weird >= 0 && weird < 32, "out-of-range pose value fails safely");
    }

    private static void PhaseSweepStaysInRange()
    {
        foreach (CoinCourierPose pose in Enum.GetValues<CoinCourierPose>())
        {
            int first = CoinCourierPoseTable.FirstFrame(pose);
            int last = CoinCourierPoseTable.LastFrame(pose);
            bool looping = CoinCourierPoseTable.IsLooping(pose);
            int previous = Frame(pose, 0f);
            Check(previous >= first && previous <= last, pose + " start inside range");
            for (int step = 1; step <= 1200; step++)
            {
                int frame = Frame(pose, 2.5f * step / 1200f);
                Check(frame >= first && frame <= last, pose + " sweep inside range");
                int delta = frame - previous;
                Check(delta <= 1, pose + " never skips a frame forward");
                Check(delta >= 0 || looping, pose + " non-looping poses never jump backwards");
                previous = frame;
            }
        }
    }

    private static void ViewLifecycle()
    {
        var parent = new GameObject("world-root");
        var reference = new GameObject("reference").AddComponent<SpriteRenderer>();
        reference.sortingLayerID = 37;
        reference.sortingOrder = 12;
        var shared = new Material();
        reference.sharedMaterial = shared;

        var view = CoinCourierVisuals.Create(parent.transform, reference);
        Check(view != null, "view created");
        Equal(1, CoinCourierVisuals.LiveViewCount, "one live view");
        Equal(0, view.Renderer.sortingLayerID, "own sorting layer is the absolute numeric 0");
        Equal(1, view.Renderer.sortingOrder, "own sorting order is the absolute 1 (mod front decision)");
        Check(ReferenceEquals(shared, view.Renderer.sharedMaterial), "shared material reused, not cloned");
        Check(ReferenceEquals(parent.transform, view.Transform.parent), "parented under the caller transform");
        Check(!view.Renderer.enabled, "hidden until the first Render");
        // 共用视觉节点缩放：Create 起就是批准的 0.625（X/Y 同值、Z 恒 1）。
        Check(view.Transform.localScale == new Vector3(0.625f, 0.625f, 1f),
            "view node carries the shared appearance scale from Create");

        Check(reference.sprite == null, "reference sprite untouched");
        Equal(37, reference.sortingLayerID, "reference sorting layer untouched");
        Equal(12, reference.sortingOrder, "reference sorting order untouched");
        Check(ReferenceEquals(shared, reference.sharedMaterial), "reference material untouched");

        // 参考排序任意变化都不能进入自有 view（绝对前置、无来源累加）。
        reference.sortingLayerID = -9;
        reference.sortingOrder = 32767;
        var second = CoinCourierVisuals.Create(parent.transform, reference);
        Equal(0, second.Renderer.sortingLayerID, "a different reference never changes the own layer");
        Equal(1, second.Renderer.sortingOrder, "a different reference never changes the own order");
        CoinCourierVisuals.Destroy(second);

        // 无参考时同样绝对，不依赖 Banker/商人/参考存在。
        var bare = CoinCourierVisuals.Create(parent.transform, null);
        Equal(0, bare.Renderer.sortingLayerID, "no reference still sorts on layer 0");
        Equal(1, bare.Renderer.sortingOrder, "no reference still sorts at order 1");
        CoinCourierVisuals.Destroy(bare);

        CoinCourierVisuals.Destroy(view);
        Equal(0, CoinCourierVisuals.LiveViewCount, "destroy removes the view");
        Check(view.Root == null, "root reference released");
        CoinCourierVisuals.Destroy(view);
        CoinCourierVisuals.Destroy(null);
        Equal(0, CoinCourierVisuals.LiveViewCount, "destroy is idempotent");

        var orphan = CoinCourierVisuals.Create(null, null);
        Check(orphan == null, "missing parent is rejected, not thrown");
    }

    private static void RenderPlacementFacingVisibility()
    {
        var parent = new GameObject("world-root");
        var view = CoinCourierVisuals.Create(parent.transform, null);
        Check(view != null, "view created without a reference");

        CoinCourierVisuals.Render(view, new Vector3(5f, 6f, 0f), true, CoinCourierPose.Run, 0f, true);
        Check(view.Transform.position == new Vector3(5f, 6f, 0f), "world position applied");
        Check(view.Transform.localScale == new Vector3(0.625f, 0.625f, 1f),
            "facing right applies +appearance scale with z 1");
        Check(view.Renderer.enabled, "visible enables the renderer");

        CoinCourierVisuals.Render(view, new Vector3(5f, 6f, 0f), false, CoinCourierPose.Run, 0f, true);
        Check(view.Transform.localScale == new Vector3(-0.625f, 0.625f, 1f),
            "facing left mirrors x only and keeps the appearance scale");
        // 绝对赋值幂等：同一朝向重复渲染、来回翻转都不累乘/不漂移。
        CoinCourierVisuals.Render(view, new Vector3(5f, 6f, 0f), false, CoinCourierPose.Run, 0f, true);
        Check(view.Transform.localScale == new Vector3(-0.625f, 0.625f, 1f),
            "repeated render keeps the exact mirrored scale");
        CoinCourierVisuals.Render(view, new Vector3(5f, 6f, 0f), true, CoinCourierPose.Run, 0f, true);
        Check(view.Transform.localScale == new Vector3(0.625f, 0.625f, 1f),
            "flipping back writes the same absolute scale");

        CoinCourierVisuals.Render(view, new Vector3(7f, 8f, 0f), false, CoinCourierPose.Run, 0f, false);
        Check(!view.Renderer.enabled, "invisible disables the renderer");
        Check(view.Transform.position == new Vector3(7f, 8f, 0f), "position still applied while hidden");

        CoinCourierVisuals.Destroy(view);
    }

    private static void RenderFrameIdempotence()
    {
        var sprites = new Sprite[32];
        var texture = new Texture2D(448, 224, TextureFormat.RGBA32, false);
        for (int index = 0; index < sprites.Length; index++) sprites[index] = new Sprite { texture = texture };
        SetStatic(typeof(CoinCourierVisuals), "_sprites", sprites);
        SetStatic(typeof(CoinCourierVisuals), "_atlasState", 1);

        var parent = new GameObject("world-root");
        var view = CoinCourierVisuals.Create(parent.transform, null);
        SpriteRenderer.SpriteWrites = 0;

        CoinCourierVisuals.Render(view, Vector3.zero, true, CoinCourierPose.Run, 0f, true);
        Equal(1, SpriteRenderer.SpriteWrites, "first render assigns a sprite");
        Check(ReferenceEquals(sprites[0], view.Renderer.sprite), "run phase 0 shows frame 0");

        CoinCourierVisuals.Render(view, Vector3.zero, true, CoinCourierPose.Run, 0f, true);
        Equal(1, SpriteRenderer.SpriteWrites, "same phase does not reassign");

        CoinCourierVisuals.Render(view, Vector3.zero, true, CoinCourierPose.Run, 0.23f, true);
        Equal(2, SpriteRenderer.SpriteWrites, "phase advance assigns once");
        Check(ReferenceEquals(sprites[3], view.Renderer.sprite), "run 0.23s shows frame 3");

        CoinCourierVisuals.Render(view, Vector3.zero, true, CoinCourierPose.Idle, 0f, true);
        Equal(3, SpriteRenderer.SpriteWrites, "pose change assigns once");
        Check(ReferenceEquals(sprites[28], view.Renderer.sprite), "idle starts at frame 28");

        CoinCourierVisuals.Render(view, Vector3.zero, true, CoinCourierPose.Idle, 1.5f, false);
        Equal(3, SpriteRenderer.SpriteWrites, "hidden render does not assign");
        Check(!view.Renderer.enabled, "hidden render disables the renderer");

        CoinCourierVisuals.Render(view, Vector3.zero, true, CoinCourierPose.Idle, 1.5f, true);
        Equal(4, SpriteRenderer.SpriteWrites, "re-shown render resumes frame updates");
        Check(ReferenceEquals(sprites[31], view.Renderer.sprite), "idle 1.5s shows frame 31");

        CoinCourierVisuals.Destroy(view);
    }

    private static void TeleportFxGroupLifecycle()
    {
        var handle = CoinCourierTeleportFx.Begin(new Vector3(3f, 4f, 0f), new Color(1f, 0.5f, 0.25f, 0.8f), 1f);
        Check(handle.IsValid, "begin returns a valid handle");
        Equal(1, CoinCourierTeleportFx.ActiveCount, "one active effect group");
        Equal(10, CoinCourierTeleportFx.DashesPerEffect, "table length pins the approved stripe count");
        Equal(CoinCourierTeleportFx.DashesPerEffect, EnabledDashCount(), "every stripe of the group is enabled");
        for (int index = 0; index < CoinCourierTeleportFx.DashesPerEffect; index++)
            Check(DashObject(index).activeInHierarchy, "dash " + index + " is active in hierarchy");

        var dash0 = Dash(0);
        var dash2 = Dash(2);
        var dash5 = Dash(5);
        Near(2.71f, dash0.Positions[0].x, 1e-5f, "dash 0 left end");
        Near(3.33f, dash0.Positions[3].x, 1e-5f, "dash 0 right end");
        Near(4.635f, dash0.Positions[0].y, 1e-5f, "dash 0 top stripe above the foot anchor");
        Near(2.95f, dash2.Positions[0].x, 1e-5f, "dash 2 left end");
        Near(3.19f, dash2.Positions[3].x, 1e-5f, "dash 2 right end");
        Near(4.415f, dash2.Positions[0].y, 1e-5f, "dash 2 low stripe above the foot anchor");
        for (int index = 0; index < CoinCourierTeleportFx.DashesPerEffect; index++)
        {
            var line = Dash(index);
            Near(0f, line.startColor.a, 1e-6f, "dash " + index + " starts transparent for the fade-in");
            Near(0f, line.endColor.a, 1e-6f, "dash " + index + " keeps both ends transparent at begin");
        }
        float initialSpan = dash2.Positions[3].x - dash2.Positions[0].x;

        CoinCourierTeleportFx.TickForFrame(0.10f, 1);   // .10s：全部线已达自身峰值且仍在 .12 显形窗口内
        var peakAlphas = new float[CoinCourierTeleportFx.DashesPerEffect];
        for (int index = 0; index < peakAlphas.Length; index++)
        {
            var line = Dash(index);
            Check(line.startColor.a > 0.18f && line.endColor.a > 0.18f,
                "dash " + index + " is visible by the reveal window");
            Check(MathF.Abs(line.startColor.a - line.endColor.a) > 0.15f,
                "dash " + index + " colours its two ends differently");
            peakAlphas[index] = MathF.Max(line.startColor.a, line.endColor.a);
        }
        float peakSpan = dash2.Positions[3].x - dash2.Positions[0].x;
        Check(peakSpan < initialSpan, "dash contracted by the shrink portion");

        CoinCourierTeleportFx.TickForFrame(0.10f, 2);   // age 0.20 -> 逐线淡出尾段
        for (int index = 0; index < peakAlphas.Length; index++)
        {
            var line = Dash(index);
            Check(MathF.Max(line.startColor.a, line.endColor.a) < peakAlphas[index],
                "dash " + index + " fades out inside its own window");
        }
        Check(dash2.Positions[3].x - dash2.Positions[0].x < peakSpan, "dash keeps contracting while fading");

        float frozenAlpha = dash0.startColor.a;
        float frozenEnd = dash0.Positions[3].x;
        CoinCourierTeleportFx.TickForFrame(0f, 3);
        CoinCourierTeleportFx.TickForFrame(float.NaN, 4);
        CoinCourierTeleportFx.TickForFrame(-1f, 5);
        Equal(1, CoinCourierTeleportFx.ActiveCount, "pause and invalid deltas change nothing");
        Near(frozenAlpha, dash0.startColor.a, 1e-6f, "no alpha change on paused ticks");
        Near(frozenEnd, dash0.Positions[3].x, 1e-6f, "no state change on paused ticks");

        CoinCourierTeleportFx.TickForFrame(0.05f, 6);   // age 0.25 >= lifetime
        Equal(0, CoinCourierTeleportFx.ActiveCount, "released after the lifetime");
        Check(!dash0.enabled && !dash5.enabled, "released dashes disable their renderers");
        Check(!DashObject(0).activeInHierarchy && !DashObject(5).activeInHierarchy,
            "released dashes leave the hierarchy");
    }

    private static void TeleportFxStripeGeometry()
    {
        // Begin 的 worldPosition 是角色脚锚（actor 根 / S.Position），不是身体中心：
        // 横纹组应整体立在脚锚上方、覆盖 ~.7 高的身体，而不是在脚边摊一个小扁圈。
        var foot = new Vector3(3f, 4f, 0f);
        var unit = SampleStripeGeometry(foot, 1f);
        for (int index = 0; index < CoinCourierTeleportFx.DashesPerEffect; index++)
        {
            Check(unit.Offsets[index] > 0.05f, "dash " + index + " rises above the foot anchor");
            Check(unit.Halves[index] <= 0.32f, "dash " + index + " half-length stays compact");
            Check(unit.Widths[index] >= 0.012f - 1e-4f && unit.Widths[index] <= 0.026f + 1e-4f,
                "dash " + index + " world width sits inside the approved .012-.026 band");
            float slope = MathF.Abs(unit.Rises[index] / unit.Halves[index]);
            Check(slope >= 0.07f && slope <= 0.15f,
                "dash " + index + " leans by the approved ~4.6-7.8 degrees");
            Check(MathF.Abs(unit.Rises[index]) < unit.Halves[index],
                "dash " + index + " stays mostly horizontal");
        }
        Check(unit.Rises.Any(value => value > 0f) && unit.Rises.Any(value => value < 0f),
            "horizontal stripes lean in both directions across the group");
        Check(unit.Tops.Max() - unit.Tops.Min() > 0.5f, "stripes span more than half a unit of body height");
        Check(MathF.Abs(unit.Tops.Max() - 0.7f) < 0.05f, "the top stripe sits near the ~.7 body height");
        Check(unit.Halves.Distinct().Count() == CoinCourierTeleportFx.DashesPerEffect,
            "all six horizontal stripes have distinct lengths");
        Check(unit.Centers.Distinct().Count() == CoinCourierTeleportFx.DashesPerEffect,
            "all six horizontal stripes have distinct centres");
        Check(unit.Widths.Distinct().Count() == CoinCourierTeleportFx.DashesPerEffect,
            "all six stripe widths differ");

        // 空中脚锚：传入 Y 原样参与几何，不投影/吸附到地面，横纹照相对脚点展开。
        var air = new Vector3(5f, 7.5f, 0f);
        var airborne = SampleStripeGeometry(air, 1f);
        for (int index = 0; index < CoinCourierTeleportFx.DashesPerEffect; index++)
        {
            Check(airborne.Offsets[index] > 0f, "airborne dash " + index + " stays above the passed y");
            Check(airborne.Ys[index] > 1f, "airborne dash " + index + " is not projected to the ground");
            Check(airborne.Zs[index] == air.z, "airborne dash " + index + " keeps the passed z");
        }

        // 相似形：scale .5 / 2 时 y 偏移、rise、半长、线宽都按 scale 线性缩放，z 不变。
        foreach (float scale in new[] { 0.5f, 2f })
        {
            var scaled = SampleStripeGeometry(foot, scale);
            for (int index = 0; index < CoinCourierTeleportFx.DashesPerEffect; index++)
            {
                Near(unit.Offsets[index] * scale, scaled.Offsets[index], 1e-4f,
                    "scale " + scale + " dash " + index + " y offset scales linearly");
                Near(unit.Rises[index] * scale, scaled.Rises[index], 1e-4f,
                    "scale " + scale + " dash " + index + " rise scales linearly");
                Near(unit.Halves[index] * scale, scaled.Halves[index], 1e-4f,
                    "scale " + scale + " dash " + index + " half-length scales linearly");
                Near(unit.Widths[index] * scale, scaled.Widths[index], 1e-4f,
                    "scale " + scale + " line width scales linearly");
                Check(scaled.Zs[index] == foot.z, "scale " + scale + " keeps z unchanged");
            }
        }
    }

    private static void TeleportFxVerticalStripeGeometry()
    {
        // 竖纹共用同一脚锚：两点同 x、线轴竖直，x 偏移/中心 y/半长一套平行表；
        // 六条并集覆盖身体 ~.02~.84，方向固定（不做镜像/旋转）。
        var foot = new Vector3(3f, 4f, 0f);
        var unit = SampleVerticalStripeGeometry(foot, 1f);
        for (int index = 0; index < CoinCourierTeleportFx.DashesPerEffect; index++)
        {
            Check(unit.Xs[index] >= foot.x - 0.31f && unit.Xs[index] <= foot.x + 0.31f,
                "vertical dash " + index + " stays inside the body width");
            Check(unit.Halves[index] >= 0.09f - 1e-4f, "vertical dash " + index + " spans a body-scale segment");
            Check(unit.CenterYs[index] > foot.y, "vertical dash " + index + " is anchored above the foot");
            Check(unit.Widths[index] >= 0.012f - 1e-4f && unit.Widths[index] <= 0.026f + 1e-4f,
                "vertical dash " + index + " world width sits inside the approved .012-.026 band");
            float slope = MathF.Abs(unit.Leans[index] / unit.Halves[index]);
            Check(slope >= 0.07f && slope <= 0.15f,
                "vertical dash " + index + " leans by the approved ~4.6-7.8 degrees");
            Check(MathF.Abs(unit.Leans[index]) < unit.Halves[index],
                "vertical dash " + index + " stays mostly upright");
        }
        Check(unit.Leans.Any(value => value > 0f) && unit.Leans.Any(value => value < 0f),
            "vertical stripes lean in both directions across the group");
        Near(foot.y + 0.04f, unit.Bottoms.Min(), 1e-4f, "lowest stripe reaches the ankle");
        Near(foot.y + 0.84f, unit.Tops.Max(), 1e-4f, "tallest stripe reaches the head");
        Check(unit.Halves.Max() - unit.Halves.Min() >= 0.25f, "vertical stripe lengths are clearly uneven");
        Check(unit.Xs.Distinct().Count() == CoinCourierTeleportFx.DashesPerEffect,
            "all six vertical stripes have distinct x offsets");

        // 空中脚锚不做落地投影；排序/z 原样透传。
        var air = new Vector3(5f, 7.5f, 0f);
        var sorted = SampleVerticalStripeGeometry(air, 1f, 7, 42);
        foreach (var line in sorted.Lines)
        {
            Check(line.sortingLayerID == 7 && line.sortingOrder == 42, "vertical stripes keep the passed sorting");
            Check(line.Positions[0].z == air.z && line.Positions[3].z == air.z, "vertical stripes keep the passed z");
            Check(line.Positions[0].y > 1f && line.Positions[3].y > 1f,
                "airborne vertical stripes are not projected to the ground");
        }

        // 相似形：x 偏移、中心 y、半长、线宽按 scale 线性缩放。
        foreach (float scale in new[] { 0.5f, 2f })
        {
            var scaled = SampleVerticalStripeGeometry(foot, scale);
            for (int index = 0; index < CoinCourierTeleportFx.DashesPerEffect; index++)
            {
                Near(foot.x + (unit.Xs[index] - foot.x) * scale, scaled.Xs[index], 1e-4f,
                    "scale " + scale + " vertical dash " + index + " x offset scales linearly");
                Near(foot.y + (unit.CenterYs[index] - foot.y) * scale, scaled.CenterYs[index], 1e-4f,
                    "scale " + scale + " vertical dash " + index + " center y scales linearly");
                Near(unit.Halves[index] * scale, scaled.Halves[index], 1e-4f,
                    "scale " + scale + " vertical dash " + index + " half-length scales linearly");
                Near(unit.Leans[index] * scale, scaled.Leans[index], 1e-4f,
                    "scale " + scale + " vertical dash " + index + " lean scales linearly");
                Near(unit.Widths[index] * scale, scaled.Widths[index], 1e-4f,
                    "scale " + scale + " vertical line width scales linearly");
            }
        }
    }

    private static void TeleportFxCurveContract()
    {
        Equal(10, CoinCourierTeleportFx.DashesPerEffect, "table length pins the approved stripe count");
        var handle = CoinCourierTeleportFx.Begin(new Vector3(2f, 3f, 0f), Color.white, 1f);
        var widths = new List<int>();
        var lengths = new List<int>();
        var centers = new List<int>();
        var rows = new List<int>();
        var curves = new AnimationCurve[CoinCourierTeleportFx.DashesPerEffect];
        for (int index = 0; index < CoinCourierTeleportFx.DashesPerEffect; index++)
        {
            var line = Dash(index);
            AssertFourEvenVertices(line, "stripe " + index);
            var keys = line.widthCurve.keys;
            Equal(4, keys.Length, "stripe " + index + " carries four width keys");
            for (int key = 0; key < 4; key++)
                Near(key / 3f, keys[key].time, 1e-6f,
                    "stripe " + index + " key " + key + " time matches its vertex layout");
            float peak = MathF.Max(keys[1].value, keys[2].value);
            Check(keys[0].value <= peak * 0.1f + 1e-6f, "stripe " + index + " start tip is <=10% of its centre width");
            Check(keys[3].value <= peak * 0.1f + 1e-6f, "stripe " + index + " end tip is <=10% of its centre width");
            float world = peak * line.widthMultiplier;
            Check(world >= 0.012f - 1e-4f && world <= 0.026f + 1e-4f,
                "stripe " + index + " world width sits inside .012-.026");
            if (index >= 6)
                Check(world <= 0.0135f + 1e-4f, "aux stripe " + index + " stays thinner than the medium skeleton rows");
            widths.Add((int)MathF.Round(world * 100000f));
            lengths.Add((int)MathF.Round((line.Positions[3].x - line.Positions[0].x) * 100000f));
            centers.Add((int)MathF.Round((line.Positions[0].x + line.Positions[3].x) * 50000f));
            rows.Add((int)MathF.Round(line.Positions[0].y * 100000f));
            curves[index] = line.widthCurve;
        }
        Check(widths.Distinct().Count() == CoinCourierTeleportFx.DashesPerEffect, "all ten world widths differ");
        Check(lengths.Distinct().Count() == CoinCourierTeleportFx.DashesPerEffect, "all ten horizontal lengths differ");
        Check(centers.Distinct().Count() == CoinCourierTeleportFx.DashesPerEffect, "all ten centres differ");
        Check(rows.Distinct().Count() == CoinCourierTeleportFx.DashesPerEffect, "all ten rows differ");

        // .103s：最晚入场的线(.098 完成淡入)与最早入场的线(.108 开始淡出)之间，全组处于各自峰值。
        CoinCourierTeleportFx.TickForFrame(0.103f, 77);
        int neutral = 0;
        int gold = 0;
        for (int index = 0; index < CoinCourierTeleportFx.DashesPerEffect; index++)
        {
            var line = Dash(index);
            var start = line.startColor;
            var end = line.endColor;
            Check(start.r != end.r || start.g != end.g || start.b != end.b || start.a != end.a,
                "stripe " + index + " colours its two ends differently");
            Check(MathF.Abs(start.a - end.a) > 0.15f, "stripe " + index + " head/tail alpha differ");
            float plateau = MathF.Max(start.a, end.a);
            if (index >= 6)
                Check(plateau >= 0.4f - 1e-4f && plateau <= 0.65f + 1e-4f,
                    "aux stripe " + index + " keeps its approved .4-.65 opacity");
            else if (index == 0 || index == 3)
                Check(plateau >= 0.9f - 1e-4f, "main skeleton stripe " + index + " stays clearly brighter than the aux lines");
            float spread = MathF.Max(start.r, MathF.Max(start.g, start.b))
                - MathF.Min(start.r, MathF.Min(start.g, start.b));
            if (spread <= 0.15f) neutral++;
            if (start.r - start.b >= 0.2f) gold++;
        }
        Check(neutral >= 8, "palette stays white/grey-dominant across the ten stripes");
        Check(gold >= 1 && gold <= 2, "pale gold stays at one or two accents, never more");

        CoinCourierTeleportFx.Cancel(handle);
        var reuse = CoinCourierTeleportFx.Begin(new Vector3(2f, 3f, 0f), Color.white, 1f, 0, 0,
            CoinCourierTeleportStyle.Vertical);
        for (int index = 0; index < CoinCourierTeleportFx.DashesPerEffect; index++)
            Check(ReferenceEquals(curves[index], Dash(index).widthCurve),
                "stripe " + index + " keeps its one-time curve through pool reuse");
        for (int index = 6; index < CoinCourierTeleportFx.DashesPerEffect; index++)
            Check(MathF.Abs(Dash(index).Positions[3].y - Dash(index).Positions[0].y)
                    > MathF.Abs(Dash(index).Positions[3].x - Dash(index).Positions[0].x),
                "aux stripe " + index + " is fully re-laid as vertical after reuse");
        CoinCourierTeleportFx.Cancel(reuse);
    }

    private static void TeleportFxStaggeredEntrance()
    {
        CoinCourierTeleportFx.Begin(new Vector3(0f, 0f, 0f), Color.white, 1f);
        int count = CoinCourierTeleportFx.DashesPerEffect;
        var onsets = new float[count];
        for (int i = 0; i < count; i++) onsets[i] = -1f;
        int frame = 1;
        float age = 0f;
        while (age < 0.2f)
        {
            age += 0.001f;
            CoinCourierTeleportFx.TickForFrame(0.001f, frame++);
            for (int i = 0; i < count; i++)
            {
                if (onsets[i] >= 0f) continue;
                var line = Dash(i);
                if (line.startColor.a > 0f || line.endColor.a > 0f) onsets[i] = age;
            }
        }
        for (int i = 0; i < count; i++)
        {
            Check(onsets[i] >= 0f, "stripe " + i + " enters inside the .2s sampling window");
            Check(onsets[i] <= 0.12f, "stripe " + i + " is visible by the .12 reveal");
        }
        var sorted = onsets.OrderBy(value => value).ToArray();
        Check(sorted.Distinct().Count() == count, "every stripe enters at its own time");
        for (int i = 1; i < sorted.Length; i++)
        {
            float gap = sorted[i] - sorted[i - 1];
            Check(gap >= 0.003f && gap <= 0.013f, "entry spacing " + i + " stays inside the compressed .004-.011s window");
        }

        int guard = 0;
        while (CoinCourierTeleportFx.ActiveCount > 0 && guard++ < 40)
            CoinCourierTeleportFx.TickForFrame(0.002f, frame++);
        Equal(0, CoinCourierTeleportFx.ActiveCount, "all staggered stripes end inside the .24s lifetime");
    }

    private static void TeleportFxSlantContract()
    {
        // 轻倾斜是固定表值：两端关于表中点对称 → 中点不随收缩漂移；两次 Begin 完全一致（无随机）。
        var anchor = new Vector3(1.5f, 2.5f, 0f);
        foreach (var style in new[] { CoinCourierTeleportStyle.Horizontal, CoinCourierTeleportStyle.Vertical })
        {
            var first = CoinCourierTeleportFx.Begin(anchor, Color.white, 1f, 0, 0, style);
            var mids = new Vector3[CoinCourierTeleportFx.DashesPerEffect];
            var endpoints = new Vector3[CoinCourierTeleportFx.DashesPerEffect * 2];
            for (int index = 0; index < mids.Length; index++)
            {
                var line = Dash(index);
                mids[index] = Midpoint(line);
                endpoints[index] = line.Positions[0];
                endpoints[index + mids.Length] = line.Positions[3];
            }
            CoinCourierTeleportFx.TickForFrame(0.13f, 501);
            for (int index = 0; index < mids.Length; index++)
            {
                var line = Dash(index);
                Near(mids[index].x, Midpoint(line).x, 1e-5f, style + " stripe " + index + " midpoint x never drifts");
                Near(mids[index].y, Midpoint(line).y, 1e-5f, style + " stripe " + index + " midpoint y never drifts");
            }
            CoinCourierTeleportFx.Cancel(first);

            var second = CoinCourierTeleportFx.Begin(anchor, Color.white, 1f, 0, 0, style);
            for (int index = 0; index < mids.Length; index++)
            {
                var line = Dash(index);
                Check(line.Positions[0] == endpoints[index] && line.Positions[3] == endpoints[index + mids.Length],
                    style + " stripe " + index + " geometry is deterministic, not random");
            }
            CoinCourierTeleportFx.Cancel(second);
        }
    }

    private static void TeleportFxPoolReuseSwitchesStyle()
    {
        // 池复用必须整体换几何：同一槽 Horizontal → Vertical → Horizontal，只查真实
        // LineRenderer 端点（两点同 y / 同 x + 首条已知坐标），旧样式不得残留。
        var foot = new Vector3(1f, 2f, 0f);
        var first = CoinCourierTeleportFx.Begin(foot, Color.white, 1f);
        Check(StripesAreHorizontal(), "the first begin draws horizontal stripes");
        for (int index = 0; index < CoinCourierTeleportFx.DashesPerEffect; index++)
            AssertFourEvenVertices(Dash(index), "first begin stripe " + index);
        var firstCurve = Dash(0).widthCurve;
        Near(foot.y + 0.66f - 0.025f, Dash(0).Positions[0].y, 1e-5f, "first begin uses the tilted top row");
        Near(foot.x + 0.02f - 0.31f, Dash(0).Positions[0].x, 1e-5f, "first begin uses the horizontal left end");
        Near(foot.y + 0.14f - 0.005f, Dash(9).Positions[0].y, 1e-5f, "first begin lays the fourth aux row");
        Near(foot.x + 0.045f - 0.06f, Dash(9).Positions[0].x, 1e-5f, "aux row keeps its own short span");
        CoinCourierTeleportFx.Cancel(first);

        var vertical = CoinCourierTeleportFx.Begin(foot, Color.white, 1f, 0, 0, CoinCourierTeleportStyle.Vertical);
        Check(vertical.IsValid, "the reused slot accepts a vertical begin");
        Check(StripesAreVertical(), "the reused slot switches to vertical geometry");
        for (int index = 0; index < CoinCourierTeleportFx.DashesPerEffect; index++)
            AssertFourEvenVertices(Dash(index), "reused stripe " + index + " after the style switch");
        Near(foot.x - 0.25f - 0.033f, Dash(0).Positions[0].x, 1e-5f, "reused slot switches to the vertical x offsets");
        Near(foot.y + 0.38f - 0.32f, Dash(0).Positions[0].y, 1e-5f, "reused slot uses the vertical bottom end");
        Near(foot.y + 0.38f + 0.32f, Dash(0).Positions[3].y, 1e-5f, "reused slot uses the vertical top end");
        Near(foot.x + 0.255f, Midpoint(Dash(9)).x, 1e-5f, "aux stripe 9 leaves no horizontal residue after reuse");
        Check(ReferenceEquals(firstCurve, Dash(0).widthCurve), "reuse never rebuilds the width curve");
        CoinCourierTeleportFx.Cancel(vertical);

        var again = CoinCourierTeleportFx.Begin(foot, Color.white, 1f, 0, 0, CoinCourierTeleportStyle.Horizontal);
        Check(again.IsValid, "the reused slot accepts a horizontal begin");
        Check(StripesAreHorizontal(), "the reused slot switches back to horizontal geometry");
        for (int index = 0; index < CoinCourierTeleportFx.DashesPerEffect; index++)
            AssertFourEvenVertices(Dash(index), "stripe " + index + " switched back to horizontal");
        Near(foot.x + 0.02f - 0.31f, Dash(0).Positions[0].x, 1e-5f, "switching back restores the horizontal extent");
        Check(ReferenceEquals(firstCurve, Dash(0).widthCurve), "the curve survives the full H/V/H cycle");
        CoinCourierTeleportFx.Cancel(again);
    }

    private static void TeleportFxRejectsUnknownStyle()
    {
        var invalid = CoinCourierTeleportFx.Begin(new Vector3(1f, 1f, 0f), Color.white, 1f, 0, 0,
            (CoinCourierTeleportStyle)99);
        Check(!invalid.IsValid, "an unknown style is refused");
        Equal(0, CoinCourierTeleportFx.ActiveCount, "no pool slot is consumed by an unknown style");
        var valid = CoinCourierTeleportFx.Begin(new Vector3(1f, 1f, 0f), Color.white, 1f, 0, 0,
            CoinCourierTeleportStyle.Vertical);
        Check(valid.IsValid, "the pool still works after the refusal");
        Check(StripesAreVertical(), "the refused style left no horizontal residue behind");
        CoinCourierTeleportFx.Cancel(valid);
    }

    private static bool StripesAreHorizontal()
    {
        for (int index = 0; index < CoinCourierTeleportFx.DashesPerEffect; index++)
        {
            var line = Dash(index);
            float dx = MathF.Abs(line.Positions[3].x - line.Positions[0].x);
            float dy = MathF.Abs(line.Positions[3].y - line.Positions[0].y);
            if (dx <= dy) return false;   // 轻倾斜不改变主轴：横纹仍以 x 为主
        }
        return true;
    }

    private static bool StripesAreVertical()
    {
        for (int index = 0; index < CoinCourierTeleportFx.DashesPerEffect; index++)
        {
            var line = Dash(index);
            float dx = MathF.Abs(line.Positions[3].x - line.Positions[0].x);
            float dy = MathF.Abs(line.Positions[3].y - line.Positions[0].y);
            if (dy <= dx) return false;   // 轻倾斜不改变主轴：竖纹仍以 y 为主
        }
        return true;
    }

    /// <summary>真正读生产 LineRenderer：4 顶点共线且等分 0、1/3、2/3、1（与 curve key time 对齐）。</summary>
    private static void AssertFourEvenVertices(LineRenderer line, string why)
    {
        Equal(4, line.positionCount, why + ": four vertices");
        var first = line.Positions[0];
        var last = line.Positions[3];
        for (int vertex = 1; vertex < 3; vertex++)
        {
            float fraction = vertex / 3f;
            Near(first.x + (last.x - first.x) * fraction, line.Positions[vertex].x, 1e-5f,
                why + ": vertex " + vertex + " sits at the " + vertex + "/3 mark (x)");
            Near(first.y + (last.y - first.y) * fraction, line.Positions[vertex].y, 1e-5f,
                why + ": vertex " + vertex + " sits at the " + vertex + "/3 mark (y)");
            Near(first.z, line.Positions[vertex].z, 1e-5f, why + ": vertex " + vertex + " keeps z");
        }
    }

    /// <summary>该线中部的世界宽度 = widthCurve 两中键峰值 × widthMultiplier（生产只在创建时建 curve）。</summary>
    private static float PeakWidth(LineRenderer line)
    {
        var keys = line.widthCurve.keys;
        return MathF.Max(keys[1].value, keys[2].value) * line.widthMultiplier;
    }

    /// <summary>端点中点（生产端点关于表中点对称，故中点 = 表中线心，且不随收缩/缩放漂移）。</summary>
    private static Vector3 Midpoint(LineRenderer line)
        => new Vector3(
            (line.Positions[0].x + line.Positions[3].x) / 2f,
            (line.Positions[0].y + line.Positions[3].y) / 2f,
            0f);

    private sealed class StripeGeometrySample
    {
        internal float[] Ys;
        internal float[] Offsets;
        internal float[] Halves;
        internal float[] Rises;
        internal float[] Tops;
        internal float[] Centers;
        internal float[] Zs;
        internal float[] Widths;
    }

    /// <summary>采样一组真实 LineRenderer 输出（相对脚锚的 y 偏移/半长/中心/宽度），从不读生产常量；端点取第 0/3 顶点。</summary>
    private static StripeGeometrySample SampleStripeGeometry(Vector3 anchor, float scale)
    {
        var handle = CoinCourierTeleportFx.Begin(anchor, Color.white, scale);
        Check(handle.IsValid, "begin valid at scale " + scale);
        var sample = new StripeGeometrySample
        {
            Ys = new float[CoinCourierTeleportFx.DashesPerEffect],
            Offsets = new float[CoinCourierTeleportFx.DashesPerEffect],
            Halves = new float[CoinCourierTeleportFx.DashesPerEffect],
            Rises = new float[CoinCourierTeleportFx.DashesPerEffect],
            Tops = new float[CoinCourierTeleportFx.DashesPerEffect],
            Centers = new float[CoinCourierTeleportFx.DashesPerEffect],
            Zs = new float[CoinCourierTeleportFx.DashesPerEffect],
            Widths = new float[CoinCourierTeleportFx.DashesPerEffect]
        };
        for (int index = 0; index < CoinCourierTeleportFx.DashesPerEffect; index++)
        {
            var line = Dash(index);
            AssertFourEvenVertices(line, "sampled dash " + index + " at scale " + scale);
            sample.Ys[index] = line.Positions[0].y;
            sample.Offsets[index] = line.Positions[0].y - anchor.y;
            sample.Halves[index] = (line.Positions[3].x - line.Positions[0].x) / 2f;
            sample.Rises[index] = (line.Positions[3].y - line.Positions[0].y) / 2f;
            sample.Tops[index] = MathF.Max(line.Positions[0].y, line.Positions[3].y) - anchor.y;
            sample.Centers[index] = (line.Positions[0].x + line.Positions[3].x) / 2f;
            sample.Zs[index] = line.Positions[0].z;
            sample.Widths[index] = PeakWidth(line);
        }
        CoinCourierTeleportFx.Cancel(handle);
        return sample;
    }

    private sealed class VerticalStripeGeometrySample
    {
        internal float[] Xs;
        internal float[] CenterYs;
        internal float[] Halves;
        internal float[] Leans;
        internal float[] Bottoms;
        internal float[] Tops;
        internal LineRenderer[] Lines;
        internal float[] Widths;
    }

    /// <summary>采样一组真实竖向 LineRenderer 输出（x 偏移/中心 y/半长/lean/线宽与排序），从不读生产常量；端点取第 0/3 顶点。</summary>
    private static VerticalStripeGeometrySample SampleVerticalStripeGeometry(Vector3 anchor, float scale,
        int sortingLayerID = 0, int sortingOrder = 0)
    {
        var handle = CoinCourierTeleportFx.Begin(anchor, Color.white, scale, sortingLayerID, sortingOrder,
            CoinCourierTeleportStyle.Vertical);
        Check(handle.IsValid, "vertical begin valid at scale " + scale);
        var sample = new VerticalStripeGeometrySample
        {
            Xs = new float[CoinCourierTeleportFx.DashesPerEffect],
            CenterYs = new float[CoinCourierTeleportFx.DashesPerEffect],
            Halves = new float[CoinCourierTeleportFx.DashesPerEffect],
            Leans = new float[CoinCourierTeleportFx.DashesPerEffect],
            Bottoms = new float[CoinCourierTeleportFx.DashesPerEffect],
            Tops = new float[CoinCourierTeleportFx.DashesPerEffect],
            Lines = new LineRenderer[CoinCourierTeleportFx.DashesPerEffect],
            Widths = new float[CoinCourierTeleportFx.DashesPerEffect]
        };
        for (int index = 0; index < CoinCourierTeleportFx.DashesPerEffect; index++)
        {
            var line = Dash(index);
            AssertFourEvenVertices(line, "sampled vertical dash " + index + " at scale " + scale);
            sample.Xs[index] = line.Positions[0].x;
            sample.Bottoms[index] = line.Positions[0].y;
            sample.Tops[index] = line.Positions[3].y;
            sample.CenterYs[index] = (line.Positions[0].y + line.Positions[3].y) / 2f;
            sample.Halves[index] = (line.Positions[3].y - line.Positions[0].y) / 2f;
            sample.Leans[index] = (line.Positions[3].x - line.Positions[0].x) / 2f;
            sample.Lines[index] = line;
            sample.Widths[index] = PeakWidth(line);
        }
        CoinCourierTeleportFx.Cancel(handle);
        return sample;
    }

    private static void TeleportFxFrameDedup()
    {
        CoinCourierTeleportFx.Begin(new Vector3(0f, 0f, 0f), Color.white, 1f);
        var dash = Dash(0);

        CoinCourierTeleportFx.TickForFrame(0.05f, 100);
        float span = dash.Positions[3].x - dash.Positions[0].x;
        CoinCourierTeleportFx.TickForFrame(0.05f, 100);
        Near(span, dash.Positions[3].x - dash.Positions[0].x, 1e-6f, "same frame does not double-advance");

        CoinCourierTeleportFx.TickForFrame(0.05f, 101);
        float advanced = dash.Positions[3].x - dash.Positions[0].x;
        Check(advanced < span, "next frame advances the effect");

        Time.frameCount = 200;
        CoinCourierTeleportFx.Tick(0.05f);
        float wrapped = dash.Positions[3].x - dash.Positions[0].x;
        Check(wrapped < advanced, "Tick wrapper advances once per new frame");
        CoinCourierTeleportFx.Tick(0.05f);
        Near(wrapped, dash.Positions[3].x - dash.Positions[0].x, 1e-6f, "Tick wrapper dedups the same frame");
    }

    private static void TeleportFxCancellation()
    {
        var handle1 = CoinCourierTeleportFx.Begin(Vector3.zero, Color.white, 1f);
        var handle2 = CoinCourierTeleportFx.Begin(Vector3.zero, Color.white, 1f);
        Equal(2, CoinCourierTeleportFx.ActiveCount, "two active groups");

        CoinCourierTeleportFx.Cancel(handle1);
        Equal(1, CoinCourierTeleportFx.ActiveCount, "cancel removes only its own group");
        CoinCourierTeleportFx.Cancel(new CoinCourierFxHandle(-1, 0));
        Equal(1, CoinCourierTeleportFx.ActiveCount, "invalid handle is a no-op");

        var handle3 = CoinCourierTeleportFx.Begin(Vector3.zero, Color.white, 1f);
        Equal(2, CoinCourierTeleportFx.ActiveCount, "inactive slot is reused");
        Check(DashObject(0).activeInHierarchy, "reused slot reactivates its dash children");
        CoinCourierTeleportFx.Cancel(handle1);
        Equal(2, CoinCourierTeleportFx.ActiveCount, "stale handle cannot cancel the reused effect");
        CoinCourierTeleportFx.Cancel(handle3);
        Equal(1, CoinCourierTeleportFx.ActiveCount, "fresh handle cancels the reused effect");
        CoinCourierTeleportFx.Cancel(handle3);
        Equal(1, CoinCourierTeleportFx.ActiveCount, "double cancel is a no-op");
        CoinCourierTeleportFx.Cancel(handle2);
        Equal(0, CoinCourierTeleportFx.ActiveCount, "pool drains to zero");

        var handles = new List<CoinCourierFxHandle>();
        for (int index = 0; index < CoinCourierTeleportFx.MaxConcurrent + 1; index++)
            handles.Add(CoinCourierTeleportFx.Begin(Vector3.zero, Color.white, 1f));
        Equal(CoinCourierTeleportFx.MaxConcurrent, CoinCourierTeleportFx.ActiveCount, "pool is capped by group");
        CoinCourierTeleportFx.Cancel(handles[0]);
        Equal(CoinCourierTeleportFx.MaxConcurrent, CoinCourierTeleportFx.ActiveCount,
            "oldest handle is stale after its slot was recycled");
        CoinCourierTeleportFx.Cancel(handles[handles.Count - 1]);
        Equal(CoinCourierTeleportFx.MaxConcurrent - 1, CoinCourierTeleportFx.ActiveCount,
            "newest handle cancels its own group");
    }

    private static void TeleportFxGenerationSurvivesClear()
    {
        var stale = CoinCourierTeleportFx.Begin(Vector3.zero, Color.white, 1f);
        Check(stale.IsValid, "first handle valid");
        CoinCourierTeleportFx.Clear();   // world end
        var fresh = CoinCourierTeleportFx.Begin(new Vector3(1f, 0f, 0f), Color.white, 1f);
        Check(fresh.IsValid, "fresh handle after clear");
        Equal(1, CoinCourierTeleportFx.ActiveCount, "fresh effect is active");
        Check(DashObject(0).activeInHierarchy, "fresh effect dashes are active in hierarchy");
        CoinCourierTeleportFx.Cancel(stale);
        Equal(1, CoinCourierTeleportFx.ActiveCount, "stale handle cannot cancel the post-clear effect");
        CoinCourierTeleportFx.Cancel(fresh);
        Equal(0, CoinCourierTeleportFx.ActiveCount, "fresh handle cancels its own effect");
    }

    private static void TeleportFxComponentSurface()
    {
        CoinCourierTeleportFx.Begin(Vector3.zero, Color.white, 1f);
        var group = GameObject.All.First(go => !go.Destroyed && go.name == "KEM_CoinCourierTeleportFxEffect");
        Equal(1, group.Components.Count, "group carries only its transform");
        Equal(CoinCourierTeleportFx.DashesPerEffect, group.transform.Children.Count, "group owns one dash child per table entry");
        foreach (var child in group.transform.Children)
        {
            Equal(2, child.gameObject.Components.Count, "dash carries transform + line renderer only");
            Check(child.gameObject.Components[0] is Transform, "dash component 0 is the transform");
            Check(child.gameObject.Components[1] is LineRenderer, "dash component 1 is the line renderer");
        }

        for (int index = 0; index < CoinCourierTeleportFx.MaxConcurrent; index++)
            CoinCourierTeleportFx.Begin(new Vector3(index, 0f, 0f), Color.white, 1f);
        Equal(CoinCourierTeleportFx.MaxConcurrent, CoinCourierTeleportFx.ActiveCount, "groups are capped");
        Equal(CoinCourierTeleportFx.MaxConcurrent, EffectGroupCount(), "no group beyond the cap");
        Equal(CoinCourierTeleportFx.MaxConcurrent * CoinCourierTeleportFx.DashesPerEffect, EnabledDashCount(), "dash count stays table-derived per group");

        CoinCourierTeleportFx.Clear();
        Equal(0, CoinCourierTeleportFx.ActiveCount, "clear releases everything");
        Equal(0, EnabledDashCount(), "clear disables every dash");
    }

    private static void TeleportFxFailureCleanup()
    {
        GameObject.FailLineRendererAdd = 3;   // the third dash renderer throws
        var handle = CoinCourierTeleportFx.Begin(Vector3.zero, Color.white, 1f);
        Check(!handle.IsValid, "failed begin yields an invalid handle");
        Equal(0, CoinCourierTeleportFx.ActiveCount, "no group stayed active");
        Equal(0, EffectGroupCount(), "failed group object was destroyed");
        Equal(0, EnabledDashCount(), "no dash renderer leaked");
        Check(!GameObject.All.Any(go => !go.Destroyed && go.name.StartsWith("KEM_CoinCourierTeleportDash")),
            "no dash object leaked");

        GameObject.FailLineRendererAdd = -1;
        var recovered = CoinCourierTeleportFx.Begin(Vector3.zero, Color.white, 1f);
        Check(recovered.IsValid, "pool recovers after the injected failure");
        Equal(1, CoinCourierTeleportFx.ActiveCount, "recovered group is active");
    }

    private static void VisualsCreateFailureCleanup()
    {
        var parent = new GameObject("world-root");
        GameObject.FailSpriteRendererAdd = 1;
        var view = CoinCourierVisuals.Create(parent.transform, null);
        Check(view == null, "failed create returns null");
        Equal(0, CoinCourierVisuals.LiveViewCount, "nothing was registered");
        Check(!GameObject.All.Any(go => !go.Destroyed && go.name == "KEM_CoinCourierView"),
            "failed view object was destroyed");

        GameObject.FailSpriteRendererAdd = -1;
        var recovered = CoinCourierVisuals.Create(parent.transform, null);
        Check(recovered != null, "create recovers after the injected failure");
        Equal(1, CoinCourierVisuals.LiveViewCount, "recovered view is registered");
        CoinCourierVisuals.Destroy(recovered);
    }

    private static void DecodeAtlasCleanup()
    {
        var decode = typeof(CoinCourierVisuals).GetMethod("DecodeAtlas", StaticFlags);
        Check(decode != null, "decode method present");

        ImageConversion.Result = true;
        Texture2D.FillAlpha = 255;
        Texture2D.ResetCounters();

        Sprite.ResetCounters();
        Sprite.ThrowAtCreateIndex = 6;   // six created, then the seventh throws
        bool failedDecode = (bool)decode.Invoke(null, new object[] { new byte[8] });
        Check(!failedDecode, "decode reports failure");
        Check(GetStatic(typeof(CoinCourierVisuals), "_sprites") == null, "no sprites were handed over");
        Check(GetStatic(typeof(CoinCourierVisuals), "_texture") == null, "no texture was handed over");
        Equal(5, Sprite.CreatedCount, "five sprites were created before the failure");
        Equal(5, Sprite.DestroyedCount, "all created sprites were destroyed");
        Equal(1, Texture2D.DestroyedCount, "the texture was destroyed");

        Sprite.ResetCounters();
        Texture2D.ResetCounters();
        bool okDecode = (bool)decode.Invoke(null, new object[] { new byte[8] });
        Check(okDecode, "decode succeeds without injection");
        var sprites = (Sprite[])GetStatic(typeof(CoinCourierVisuals), "_sprites");
        Equal(32, sprites.Length, "all 32 sprites handed over");
        Equal(32, Sprite.CreatedCount, "32 sprites created");
        Equal(0, Sprite.DestroyedCount, "nothing destroyed on success");
        Check(GetStatic(typeof(CoinCourierVisuals), "_texture") != null, "texture handed over on success");

        CoinCourierVisuals.ShutdownModule();
        Equal(32, Sprite.DestroyedCount, "shutdown releases sprites");
        Equal(1, Texture2D.DestroyedCount, "shutdown releases the texture");
    }

    private static string ResolveManifest()
    {
        if (arguments.Length > 0 && File.Exists(arguments[0])) return arguments[0];
        string fromEnvironment = Environment.GetEnvironmentVariable("COIN_COURIER_MANIFEST");
        if (!string.IsNullOrEmpty(fromEnvironment) && File.Exists(fromEnvironment)) return fromEnvironment;
        foreach (string start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var directory = new DirectoryInfo(start);
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName,
                    "artifacts", "coin-courier-blueprint-b-20260927", "animation-draft", "manifest.json");
                if (File.Exists(candidate)) return candidate;
                directory = directory.Parent;
            }
        }
        return null;
    }

    private static void ManifestCrossCheck()
    {
        if (Environment.GetEnvironmentVariable("COIN_COURIER_SKIP_MANIFEST") == "1")
        {
            Console.WriteLine("  SKIP manifest cross-check (COIN_COURIER_SKIP_MANIFEST=1)");
            return;
        }
        string path = ResolveManifest();
        Check(path != null,
            "manifest.json not found - run tools/prepare_coin_courier_b_atlas.py first, "
            + "or set COIN_COURIER_MANIFEST / COIN_COURIER_SKIP_MANIFEST=1");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;
        JsonElement atlas = root.GetProperty("atlas");
        Equal(8, atlas.GetProperty("columns").GetInt32(), "manifest atlas columns");
        Equal(4, atlas.GetProperty("rows").GetInt32(), "manifest atlas rows");
        Equal(56, atlas.GetProperty("cell")[0].GetInt32(), "manifest cell width");
        Equal(56, atlas.GetProperty("cell")[1].GetInt32(), "manifest cell height");
        Near(32f, atlas.GetProperty("pixels_per_unit").GetSingle(), 1e-6f, "manifest ppu");
        Equal(28, atlas.GetProperty("pivot_pixels")[0].GetInt32(), "manifest pivot x");
        Equal(12, atlas.GetProperty("pivot_pixels")[1].GetInt32(), "manifest pivot y");
        Equal(448, atlas.GetProperty("size")[0].GetInt32(), "manifest atlas width");
        Equal(224, atlas.GetProperty("size")[1].GetInt32(), "manifest atlas height");
        Equal(44, atlas.GetProperty("ground_row_from_top").GetInt32(), "manifest ground row");

        JsonElement frames = root.GetProperty("frames");
        Equal(32, frames.GetArrayLength(), "manifest frame count");

        var byName = new Dictionary<string, JsonElement>();
        foreach (JsonElement pose in root.GetProperty("poses").EnumerateArray())
            byName[pose.GetProperty("name").GetString()] = pose;

        var spans = new (CoinCourierPose pose, int first, int last, float spf, bool loop)[]
        {
            (CoinCourierPose.Idle, 28, 31, 0.50f, true),
            (CoinCourierPose.Leisure, 8, 11, 0.60f, true),
            (CoinCourierPose.Run, 0, 7, 0.075f, true),
            (CoinCourierPose.Collect, 12, 15, 0.18f, false),
            (CoinCourierPose.Deliver, 16, 19, 0.20f, false),
            (CoinCourierPose.Jump, 20, 23, 0.10f, false),
            (CoinCourierPose.Fall, 24, 25, 0.12f, false),
            (CoinCourierPose.Land, 26, 27, 0.12f, false),
        };
        foreach (var (pose, first, last, spf, loop) in spans)
        {
            string name = pose.ToString();
            Check(byName.ContainsKey(name), "manifest pose " + name + " present");
            JsonElement entry = byName[name];
            Equal(first, entry.GetProperty("first").GetInt32(), name + " first frame");
            Equal(last, entry.GetProperty("last").GetInt32(), name + " last frame");
            Equal(last - first + 1, entry.GetProperty("frames").GetInt32(), name + " frame count");
            Near(spf, entry.GetProperty("seconds_per_frame").GetSingle(), 1e-6f, name + " seconds per frame");
            Check(entry.GetProperty("looping").GetBoolean() == loop, name + " looping flag");
            int offset = entry.GetProperty("cell_offset_x").GetInt32();
            foreach (JsonElement frame in frames.EnumerateArray())
            {
                if (frame.GetProperty("pose").GetString() != name) continue;
                Equal(offset, frame.GetProperty("cell_pos")[0].GetInt32(), name + " frame honours the common x offset");
            }
        }

        foreach (JsonElement frame in frames.EnumerateArray())
        {
            int index = frame.GetProperty("index").GetInt32();
            string poseName = frame.GetProperty("pose").GetString();
            CoinCourierPose expected = spans.First(entry => entry.first <= index && index <= entry.last).pose;
            Equal(expected.ToString(), poseName, "frame " + index + " pose mapping");
            Equal((index % 8) * 56, frame.GetProperty("atlas_pos")[0].GetInt32(), "frame " + index + " atlas x");
            Equal((index / 8) * 56, frame.GetProperty("atlas_pos")[1].GetInt32(), "frame " + index + " atlas y");
            Check(frame.GetProperty("sha256").GetString().Length == 64, "frame " + index + " carries a sha256");
        }

        string atlasPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)), "atlas", "CoinCourierBAtlas.png");
        Check(File.Exists(atlasPath), "atlas file exists next to the manifest");
        using var sha = SHA256.Create();
        string digest = Convert.ToHexString(sha.ComputeHash(File.ReadAllBytes(atlasPath))).ToLowerInvariant();
        Equal(atlas.GetProperty("sha256").GetString(), digest, "manifest atlas sha256 matches the file");
        Console.WriteLine("  manifest: " + Path.GetFullPath(path));
    }
}
