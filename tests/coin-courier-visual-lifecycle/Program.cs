// Coin courier visual-lifecycle regression suite: compiles the real CoinCourierVisuals.cs and
// CoinCourierRuntime.cs against local Unity doubles that model Unity's fake-null semantics and
// native cascade destruction; no Unity runtime, no game.
//
// Models the reported lifecycle ("after an in-process save switch the courier turned invisible
// while coin collection and the coin trail kept working; a full restart brought it back") by
// actively injecting candidate faults — a natively destroyed view root / shared-atlas object
// while managed handles and caches stay alive — and asserting the production paths recover;
// the actual field trigger remains unproven. A confirmed-bad atlas stays fail-closed. Passing
// this suite does not establish real rendering or in-game behaviour.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KingdomEnhancedMod;
using UnityEngine;

internal static class Program
{
    private static int passed, failed;
    private static readonly BindingFlags StaticFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

    private static int Main()
    {
        Test("destroyed atlas texture is replaced on the same animation frame", DestroyedTextureRebuildsOnSameFrame);
        Test("a destroyed frame sprite triggers exactly one bounded atlas rebuild", DestroyedFrameSpriteOneRebuild);
        Test("a failed re-decode keeps the fail-closed gate without retry", FailedRedecodeStaysClosed);
        Test("an existing view rebinds after another view rebuilds the shared atlas", AtlasRebuildRebindsExistingViews);
        Test("a natively destroyed view root is replaced without resetting the scene state", DestroyedViewRootRebuildsThroughScenePath);
        Test("a destroyed shared atlas recovers while the runtime keeps running", DestroyedAtlasRecoversUnderRuntime);
        Test("a mid-visit view root loss keeps the visit responsibility", MidVisitStateSurvivesRootLoss);
        Test("a destroyed renderer rebuilds the view and cleans the stale root", DestroyedRendererRebuildsAndCleansRoot);
        Test("a hidden teleport stays hidden through an in-place view rebuild", HiddenTeleportStaysHiddenThroughRebuild);
        Test("failed rebuilds are rate-limited and the next success keeps responsibility", FailedRebuildRetriesAreBoundedAndStateKept);
        Test("normal operation never rebuilds the view or the atlas", NormalOperationIsChurnFree);

        Console.WriteLine("coin-courier-visual-lifecycle: " + passed + " passed, " + failed + " failed");
        return failed == 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------- helpers

    private static void Check(bool condition, string why)
    {
        if (!condition) throw new Exception(why);
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
        try { CoinCourierRuntime.Bind(null); } catch { }
        try { Harness.ResetRecorders(); } catch { }
        Managers.Inst = null;
        KingdomEnhancedPlugin.Instance = new KingdomEnhancedPlugin();
        GameObject.All.Clear();
        GameObject.DestroyCalls = 0;
        GameObject.FailCourierProbeCreation = false;
        GameObject.FailViewRootCreation = 0;
        GameObject.ViewRootConstructorCalls = 0;
        SpriteRenderer.SpriteWrites = 0;
        Sprite.ResetCounters();
        Texture2D.ResetCounters();
        ImageConversion.Reset();
        Time.time = 0f;
        Time.unscaledTime = 0f;
        Time.deltaTime = 1f / 60f;
        Time.timeScale = 1f;
        Time.frameCount = 0;
        ClearWarnings();
        ClearRuntimeLogOnce();
    }

    private static void ClearRuntimeLogOnce()
    {
        FieldInfo info = typeof(CoinCourierRuntime).GetField("LoggedOnce", StaticFlags);
        if (info != null) ((HashSet<string>)info.GetValue(null)).Clear();
    }

    private static void ClearWarnings()
    {
        FieldInfo info = typeof(CoinCourierVisuals).GetField("Warned", StaticFlags);
        if (info != null) ((HashSet<string>)info.GetValue(null)).Clear();
    }

    private static int WarningCount(string fragment)
    {
        var sink = KingdomEnhancedPlugin.Instance.LogSource;
        int count = 0;
        foreach (string message in sink.Messages)
        {
            if (message.Contains("WARN") && message.Contains(fragment)) count++;
        }
        return count;
    }

    // ---- 业务状态观察（只读反射；Scene 为生产私有嵌套类型）----

    private static object SceneState()
    {
        FieldInfo info = typeof(CoinCourierRuntime).GetField("S", StaticFlags);
        if (info == null) throw new Exception("scene static missing");
        return info.GetValue(null);
    }

    private static T SceneValue<T>(string field)
    {
        FieldInfo info = SceneState().GetType().GetField(field,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (info == null) throw new Exception("scene field missing: " + field);
        return (T)info.GetValue(SceneState());
    }

    private static Dictionary<long, float> NextEligible()
    {
        FieldInfo info = typeof(CoinCourierRuntime).GetField("NextEligibleAt", StaticFlags);
        if (info == null) throw new Exception("NextEligibleAt missing");
        return (Dictionary<long, float>)info.GetValue(null);
    }

    private static CoinCourierPhase Phase() => SceneValue<CoinCourierPhase>("Phase");

    private static bool ActionConsumed() => SceneValue<bool>("ActionConsumed");

    private static bool CouriersVisible() => SceneValue<bool>("Visible");

    private static int LiveViewObjects()
    {
        int count = 0;
        for (int i = 0; i < GameObject.All.Count; i++)
        {
            if (!GameObject.All[i].Destroyed && GameObject.All[i].name == "KEM_CoinCourierView") count++;
        }
        return count;
    }

    /// <summary>真实解码路径（内嵌 atlas 资源 + ImageConversion 注入尺寸/大小），并渲染第一帧。</summary>
    private static CoinCourierView CreateRenderedView(out Sprite firstSprite)
    {
        ImageConversion.Reset();
        Texture2D.FillAlpha = 255;
        var parent = new GameObject("world-root");
        CoinCourierView view = CoinCourierVisuals.Create(parent.transform, null);
        Check(view != null, "view created");
        CoinCourierVisuals.Render(view, Vector3.zero, true, CoinCourierPose.Run, 0f, true);
        firstSprite = view.Renderer.sprite;
        Check(firstSprite != null, "the first render commits a live sprite");
        Check(firstSprite.texture != null, "the committed sprite carries a live atlas texture");
        return view;
    }

    // ---------------------------------------------------------------- visuals-level cases

    private static void DestroyedTextureRebuildsOnSameFrame()
    {
        CoinCourierView view = CreateRenderedView(out Sprite first);
        Texture2D atlas = first.texture;

        // 主动注入候选故障：解除共享图集纹理，托管静态缓存（_sprites/_atlasState）仍然存活。
        UnityEngine.Object.Destroy(atlas);
        Check(atlas == null, "a destroyed texture compares equal to null (Unity fake-null)");
        Check(first != null, "the sprite wrapper itself is still alive");
        Check(first.texture == null, "its texture is now a destroyed native object");

        int createsBefore = Sprite.CreateCalls;
        // 同一动画帧（Run 相位 0）：同帧去重绝不能靠旧帧号绕过恢复。
        CoinCourierVisuals.Render(view, Vector3.zero, true, CoinCourierPose.Run, 0f, true);
        Sprite rebuilt = view.Renderer.sprite;
        Check(rebuilt != null, "the renderer got a live sprite back on the same animation frame");
        Check(rebuilt.texture != null, "the rebuilt sprite carries a live texture");
        Check(!ReferenceEquals(rebuilt, first), "the rebuilt sprite is a fresh object");
        Check(!ReferenceEquals(rebuilt.texture, atlas), "the rebuilt atlas texture is fresh");
        Check(Sprite.CreateCalls - createsBefore == 32, "recovery decodes the atlas exactly once (32 frames)");
        Check(view.Renderer.enabled, "the renderer is left visible");
        Check(CoinCourierVisuals.LiveViewCount == 1, "no views leaked");
        Check(WarningCount("atlas cache invalidated (native release)") == 1,
            "the native-release recovery leaves exactly one bounded diagnostic");

        int createsAfter = Sprite.CreateCalls;
        CoinCourierVisuals.Render(view, Vector3.zero, true, CoinCourierPose.Run, 0f, true);
        Check(Sprite.CreateCalls == createsAfter, "the same frame stays deduplicated after recovery");
        Check(ReferenceEquals(view.Renderer.sprite, rebuilt), "no redundant sprite writes");
        Check(WarningCount("atlas cache invalidated (native release)") == 1, "the diagnostic stays one per recovery");

        CoinCourierVisuals.Destroy(view);
    }

    private static void DestroyedFrameSpriteOneRebuild()
    {
        CoinCourierView view = CreateRenderedView(out Sprite first);

        // 主动注入：当前帧切图被原生回收（纹理仍在）——代理仍存活但已 fake-null。
        UnityEngine.Object.Destroy(first);
        Check(first == null, "the destroyed frame sprite is fake-null");
        Check(view.Renderer.sprite == null, "the renderer's committed sprite is now fake-null");

        int createsBefore = Sprite.CreateCalls;
        CoinCourierVisuals.Render(view, Vector3.zero, true, CoinCourierPose.Run, 0f, true);
        Check(view.Renderer.sprite != null, "a live sprite is committed again");
        Check(view.Renderer.sprite.texture != null, "with a live atlas texture");
        Check(Sprite.CreateCalls - createsBefore == 32, "exactly one bounded atlas rebuild");
        Check(!ReferenceEquals(view.Renderer.sprite, first), "the stale sprite is not reused");

        CoinCourierVisuals.Destroy(view);
    }

    private static void FailedRedecodeStaysClosed()
    {
        CoinCourierView view = CreateRenderedView(out Sprite first);
        Texture2D atlas = first.texture;

        UnityEngine.Object.Destroy(atlas);
        ImageConversion.Result = false;   // 重建所需的素材此刻不可读

        int loadsBefore = ImageConversion.LoadImageCalls;
        CoinCourierVisuals.Render(view, Vector3.zero, true, CoinCourierPose.Run, 0f, true);
        Check(ImageConversion.LoadImageCalls - loadsBefore == 1, "one bounded rebuild attempt");
        Check(view.Renderer.sprite == null, "no sprite is committed when the rebuild fails");
        Check(WarningCount("ImageConversion.LoadImage returned false") == 1,
            "the decode failure is reported exactly once");

        int loadsAfter = ImageConversion.LoadImageCalls;
        CoinCourierVisuals.Render(view, Vector3.zero, true, CoinCourierPose.Run, 0f, true);
        CoinCourierVisuals.Render(view, Vector3.zero, true, CoinCourierPose.Idle, 1.5f, true);
        Check(ImageConversion.LoadImageCalls == loadsAfter, "no per-frame retry loop after the failure");

        ImageConversion.Result = true;
        CoinCourierVisuals.Render(view, Vector3.zero, true, CoinCourierPose.Run, 0f, true);
        Check(ImageConversion.LoadImageCalls == loadsAfter,
            "the confirmed-bad-asset gate holds even when the source becomes readable again");
        Check(WarningCount("ImageConversion.LoadImage returned false") == 1, "still exactly one warning");

        CoinCourierVisuals.Destroy(view);
    }

    private static void AtlasRebuildRebindsExistingViews()
    {
        ImageConversion.Reset();
        Texture2D.FillAlpha = 255;
        var parent = new GameObject("world-root");
        CoinCourierView first = CoinCourierVisuals.Create(parent.transform, null);
        CoinCourierView second = CoinCourierVisuals.Create(parent.transform, null);
        Check(first != null && second != null, "two views share the module atlas");
        CoinCourierVisuals.Render(first, Vector3.zero, true, CoinCourierPose.Idle, 0f, true);
        CoinCourierVisuals.Render(second, new Vector3(2f, 0f, 0f), true, CoinCourierPose.Idle, 0f, true);
        Sprite secondSprite = second.Renderer.sprite;
        Check(first.Renderer.sprite != null && secondSprite != null,
            "both views committed sprites from the shared atlas generation");

        // 主动注入图集回收后由视图 A 触发换代重建；视图 B 的旧切图同时失效，
        // 其下一次渲染（帧号不变）必须重绑到新一代，而不是继续持有已销毁切图。
        UnityEngine.Object.Destroy(first.Renderer.sprite.texture);
        CoinCourierVisuals.Render(first, Vector3.zero, true, CoinCourierPose.Idle, 0f, true);
        Check(first.Renderer.sprite != null && first.Renderer.sprite.texture != null, "view A recovered");
        CoinCourierVisuals.Render(second, new Vector3(2f, 0f, 0f), true, CoinCourierPose.Idle, 0f, true);
        Check(second.Renderer.sprite != null && second.Renderer.sprite.texture != null,
            "view B rebound on its unchanged animation frame");
        Check(!ReferenceEquals(second.Renderer.sprite, secondSprite), "view B dropped the stale sprite");
        Check(ReferenceEquals(second.Renderer.sprite.texture, first.Renderer.sprite.texture),
            "both views carry the same new atlas generation");

        CoinCourierVisuals.Destroy(first);
        CoinCourierVisuals.Destroy(second);
    }

    // ---------------------------------------------------------------- runtime-level cases

    private static void DestroyedViewRootRebuildsThroughScenePath()
    {
        var h = new Harness();
        h.Step(0.5f);

        GameObject root = h.LiveViewRoot();
        Check(root != null, "the runtime created a live view");
        var renderer = root.GetComponent<SpriteRenderer>();
        Check(renderer != null && renderer.sprite != null && renderer.sprite.texture != null,
            "the courier sprite is committed before the switch");
        IntPtr worldPointer = Managers.Inst.world.Pointer;

        // 主动注入候选故障：原生整树回收 view 根（组件一并失效），运行时托管句柄不知情，
        // world/layer 指针保持不变；实机切换存档时是否命中该形态（或其它卸载者）未证。
        UnityEngine.Object.Destroy(root);
        Check(root == null, "the destroyed view root is fake-null");
        Check(renderer == null, "its renderer component went with the native destroy");
        Check(Managers.Inst.world.Pointer == worldPointer, "the simulated teardown kept the same world pointer");

        CoinCourierPhase phaseBefore = Phase();
        bool consumedBefore = ActionConsumed();
        h.Step(1f / 60f);
        GameObject rebuilt = h.LiveViewRoot();
        Check(rebuilt != null, "the runtime rebuilt a live view in the same world");
        Check(!ReferenceEquals(rebuilt, root), "the rebuilt view is a fresh object");
        var rebuiltRenderer = rebuilt.GetComponent<SpriteRenderer>();
        Check(rebuiltRenderer != null, "the rebuilt view owns a renderer");
        Check(rebuiltRenderer.sprite != null && rebuiltRenderer.sprite.texture != null,
            "the rebuilt view is visible again (live sprite + live texture)");
        Check(rebuiltRenderer.enabled, "the rebuilt renderer is enabled");
        Check(Phase() == phaseBefore && ActionConsumed() == consumedBefore,
            "the in-place rebuild kept the wait-phase state instead of resetting it");
        Check(WarningCount("display view rebuilt in place") == 1,
            "the field log names the in-place rebuild exactly once");
        Check(CoinCourierVisuals.LiveViewCount == 1, "exactly one live view after the rebuild");
        Check(Harness.ViewObjectsCreated() == 2, "only one rebuild happened (two roots ever created)");
        Check(h.State.Owned && h.State.Purse.Coins == 0, "the rebuild never touches Owned/purse");
    }

    private static void DestroyedAtlasRecoversUnderRuntime()
    {
        var h = new Harness();
        h.Step(0.5f);

        GameObject root = h.LiveViewRoot();
        Check(root != null, "the runtime created a live view");
        var renderer = root.GetComponent<SpriteRenderer>();
        Sprite sprite = renderer.sprite;
        Check(sprite != null && sprite.texture != null, "atlas sprite committed");
        Texture2D atlas = sprite.texture;

        UnityEngine.Object.Destroy(atlas);
        Check(atlas == null, "the destroyed atlas is fake-null");
        int loadsBefore = ImageConversion.LoadImageCalls;

        h.Step(0.1f);
        Check(ReferenceEquals(h.LiveViewRoot(), root), "the view root itself was kept");
        Check(renderer.sprite != null, "a live sprite is committed again");
        Check(renderer.sprite.texture != null, "its texture is live");
        Check(!ReferenceEquals(renderer.sprite, sprite), "the stale sprite is not reused");
        Check(ImageConversion.LoadImageCalls - loadsBefore == 1, "the atlas is rebuilt exactly once");
        Check(CoinCourierVisuals.LiveViewCount == 1, "no views leaked");
    }

    private static void MidVisitStateSurvivesRootLoss()
    {
        var h = new Harness();
        CoinCourierEconomy.BagApplied = true;
        CoinCourierEconomy.DeliverApplied = true;
        h.State.Purse = LifecycleState.PurseWith(8);
        Knight first = h.MakeKnight(Side.Right, 10f);
        h.ArmVisit(first, 71, 1, 2);
        Check(h.RunUntil(() => Phase() == CoinCourierPhase.Deliver && ActionConsumed(), 8f),
            "visit 1 reaches Deliver with its handoff action consumed");
        Check(h.RunUntil(() => NextEligible().Count >= 1, 15f),
            "visit 1 ends and leaves a cooldown entry");
        Knight second = h.MakeKnight(Side.Left, -8f);
        h.ArmVisit(second, 72, -1, 2);
        Check(h.RunUntil(() => Phase() == CoinCourierPhase.Deliver && ActionConsumed()
                && NextEligible().Count >= 1, 20f),
            "visit 2 reaches Deliver with plan/sent/cooldown populated");

        CoinCourierRules.VisitPlan plan = SceneValue<CoinCourierRules.VisitPlan>("Plan");
        int sent = SceneValue<int>("Sent");
        long targetLife = SceneValue<long>("TargetLife");
        long[] cooldownKeys = NextEligible().Keys.OrderBy(key => key).ToArray();
        int purseCoins = h.State.Purse.AvailableCoins;
        Check(sent > 0 && plan.LifeId == 72 && cooldownKeys.Contains(71L),
            "the in-flight snapshot really carries a plan, delivered coins and a cooldown");

        GameObject root = h.LiveViewRoot();
        Check(root != null, "the view is live before the loss");
        UnityEngine.Object.Destroy(root);
        Check(root == null, "the destroyed root is fake-null");

        h.Step(1f / 60f);
        GameObject rebuilt = h.LiveViewRoot();
        Check(rebuilt != null && !ReferenceEquals(rebuilt, root), "the view was replaced in place");
        var renderer = rebuilt.GetComponent<SpriteRenderer>();
        Check(renderer != null && renderer.sprite != null && renderer.sprite.texture != null,
            "the replaced view renders a live sprite");
        Check(Phase() == CoinCourierPhase.Deliver, "the in-flight phase survived the visual-only rebuild");
        Check(ActionConsumed(), "ActionConsumed survived");
        Check(SceneValue<int>("Sent") == sent, "the delivered-coin count survived");
        Check(SceneValue<CoinCourierRules.VisitPlan>("Plan").LifeId == plan.LifeId,
            "the frozen visit plan survived");
        Check(SceneValue<long>("TargetLife") == targetLife, "the target life survived");
        Check(CouriersVisible(), "the visible state survived");
        Check(cooldownKeys.All(key => NextEligible().ContainsKey(key)), "the cooldown entries survived");
        Check(h.State.Purse.AvailableCoins == purseCoins, "the purse is untouched by the rebuild");
        Check(Harness.ViewObjectsCreated() == 2, "exactly one replacement view was created");
        Check(WarningCount("display view rebuilt in place") == 1,
            "the field log names the in-place rebuild exactly once");
    }

    private static void DestroyedRendererRebuildsAndCleansRoot()
    {
        var h = new Harness();
        CoinCourierEconomy.BagApplied = true;
        CoinCourierEconomy.DeliverApplied = true;
        h.State.Purse = LifecycleState.PurseWith(4);
        Knight knight = h.MakeKnight(Side.Right, 10f);
        h.ArmVisit(knight, 73, 1, 2);
        Check(h.RunUntil(() => Phase() == CoinCourierPhase.Deliver && ActionConsumed(), 8f),
            "the visit reaches Deliver");

        int sent = SceneValue<int>("Sent");
        GameObject root = h.LiveViewRoot();
        var renderer = root.GetComponent<SpriteRenderer>();
        Check(renderer != null && renderer.sprite != null, "production assigned a renderer sprite");
        UnityEngine.Object.Destroy(renderer);
        Check(renderer == null, "the destroyed renderer wrapper is fake-null");
        Check(root != null, "the stale root itself is still alive");

        h.Step(1f / 60f);
        GameObject rebuilt = h.LiveViewRoot();
        Check(rebuilt != null && !ReferenceEquals(rebuilt, root), "a fresh view replaced the broken one");
        Check(root == null, "the stale (still-alive) root was cleaned up on replacement");
        Check(LiveViewObjects() == 1, "no stale view object leaked");
        var rebuiltRenderer = rebuilt.GetComponent<SpriteRenderer>();
        Check(rebuiltRenderer != null && rebuiltRenderer.sprite != null && rebuiltRenderer.sprite.texture != null,
            "the replacement renders a live sprite");
        Check(Phase() == CoinCourierPhase.Deliver && SceneValue<int>("Sent") == sent,
            "the in-flight responsibility survived the renderer loss");
        Check(Harness.ViewObjectsCreated() == 2, "exactly one replacement view was created");
    }

    private static void HiddenTeleportStaysHiddenThroughRebuild()
    {
        var h = new Harness();
        h.State.Purse = LifecycleState.PurseWith(4);
        Knight knight = h.MakeKnight(Side.Right, 10f);
        h.ArmVisit(knight, 81, 1, 2);
        Check(h.RunUntil(() => Phase() == CoinCourierPhase.TeleportIn && !CouriersVisible(), 8f),
            "the courier enters the hidden teleport phase");

        GameObject root = h.LiveViewRoot();
        Check(root != null, "the view is live while hidden");
        UnityEngine.Object.Destroy(root);
        h.Step(1f / 60f);

        GameObject rebuilt = h.LiveViewRoot();
        Check(rebuilt != null && !ReferenceEquals(rebuilt, root), "the view was replaced while hidden");
        Check(Phase() == CoinCourierPhase.TeleportIn && !CouriersVisible(),
            "the hidden teleport phase survived the rebuild");
        Check(!rebuilt.GetComponent<SpriteRenderer>().enabled,
            "the replaced renderer stays disabled while the courier is hidden");
    }

    private static void FailedRebuildRetriesAreBoundedAndStateKept()
    {
        var h = new Harness();
        CoinCourierEconomy.BagApplied = true;
        CoinCourierEconomy.DeliverApplied = true;
        h.State.Purse = LifecycleState.PurseWith(6);
        Knight knight = h.MakeKnight(Side.Right, 10f);
        h.ArmVisit(knight, 91, 1, 2);
        Check(h.RunUntil(() => Phase() == CoinCourierPhase.Deliver && ActionConsumed(), 8f),
            "the visit reaches Deliver");

        CoinCourierRules.VisitPlan plan = SceneValue<CoinCourierRules.VisitPlan>("Plan");
        int sent = SceneValue<int>("Sent");
        long targetLife = SceneValue<long>("TargetLife");
        GameObject root = h.LiveViewRoot();
        UnityEngine.Object.Destroy(root);

        int attemptsBefore = GameObject.ViewRootConstructorCalls;
        GameObject.FailViewRootCreation = 60;   // 整个窗口内重建必失败
        h.Step(0.6f);
        int attempts = GameObject.ViewRootConstructorCalls - attemptsBefore;
        Check(h.LiveViewRoot() == null, "no live view while creation keeps failing");
        Check(attempts <= 3, "failed rebuilds are rate-limited, not retried every frame");
        Check(CoinCourierVisuals.LiveViewCount == 1,
            "the stale handle stays as the session marker (no fall-back to first-time init)");
        Check(Phase() == CoinCourierPhase.Deliver && SceneValue<int>("Sent") == sent
              && SceneValue<CoinCourierRules.VisitPlan>("Plan").LifeId == plan.LifeId
              && SceneValue<long>("TargetLife") == targetLife,
            "the responsibility survives the failed rebuilds");

        GameObject.FailViewRootCreation = 0;
        h.Step(1.0f);
        GameObject rebuilt = h.LiveViewRoot();
        Check(rebuilt != null, "the next successful rebuild restores a live view");
        var rebuiltRenderer = rebuilt.GetComponent<SpriteRenderer>();
        Check(rebuiltRenderer != null && rebuiltRenderer.sprite != null && rebuiltRenderer.sprite.texture != null,
            "the recovered view renders a live sprite");
        Check(Phase() == CoinCourierPhase.Deliver && SceneValue<int>("Sent") == sent
              && SceneValue<CoinCourierRules.VisitPlan>("Plan").LifeId == plan.LifeId
              && SceneValue<long>("TargetLife") == targetLife,
            "the responsibility is still kept after the recovery");
        Check(Harness.ViewObjectsCreated() == 2, "failed attempts leaked no view objects");
        Check(LiveViewObjects() == 1, "exactly one live view object remains");
        Check(WarningCount("display view rebuilt in place") == 1,
            "the field log names the in-place rebuild exactly once");
    }

    private static void NormalOperationIsChurnFree()
    {
        var h = new Harness();
        h.Step(3f);

        int views = Harness.ViewObjectsCreated();
        int creates = Sprite.CreateCalls;
        int loads = ImageConversion.LoadImageCalls;
        Check(views == 1, "normal operation creates exactly one view root");
        Check(creates == 32, "normal operation decodes the atlas exactly once (32 frames)");
        Check(loads == 1, "normal operation loads the embedded PNG exactly once");

        h.Step(3f);
        Check(Harness.ViewObjectsCreated() == views, "no hidden view churn over time");
        Check(Sprite.CreateCalls == creates, "no hidden atlas churn over time");
        Check(ImageConversion.LoadImageCalls == loads, "no hidden reload churn over time");
        Check(CoinCourierVisuals.LiveViewCount == 1, "still exactly one live view");
    }
}
