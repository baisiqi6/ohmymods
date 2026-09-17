// HeroArcherVisuals 原生跟随控制流测试（替身 Unity + 替身游戏类型；真实 interop 由 operator 编译复核）。
//
// 覆盖：接管/挂起/再接管的交接、未知状态与不可读不丢凭据、池/换 life/功能关的归还、第三方隐藏不被覆盖、
// 写失败不持有并重试、同帧双入口只执行一次、放箭事件不重启相位、诊断有界（上限 120 且不随帧数增长）、
// Prepare 窗口（桥记录 pending/active、进入与回绕锁入、进行中不改、非法/陌生/life 拒绝、重建不沿用）、
// 事件锚定释放表现（同帧散射去重、隔帧重启、Walk/Run/Shoot/新 Prepare 取消、暂停冻结、0.18s 有界、双英雄独立）。
// 运行： C:/Users/ADMIN/dotnet8/dotnet.exe run --project tests/visuals-native-stub/Tests.csproj
//
// 不覆盖（须由 operator 真机复核）：真实 Animator 的 shortNameHash/相位语义、IL2CPP interop 注入与
// forceRenderingOff 的真实行为、渲染层排序与视觉观感。

using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomEnhancedMod;

internal static class Program
{
    private static int _failed;
    private static int _passed;
    private static int _frame = 1000;

    private static void Check(string name, bool ok, string detail = "")
    {
        if (ok) { _passed++; return; }
        _failed++;
        Console.WriteLine("FAIL " + name + (detail.Length > 0 ? " :: " + detail : ""));
        for (int i = 0; i < LogSourceStub.Lines.Count; i++)
        {
            Console.WriteLine("    log: " + LogSourceStub.Lines[i]);
        }
    }

    private static int Main()
    {
        ApplyTakesOverSupportedState();
        SyncFollowsNativePhaseWithoutOwnClock();
        ShotEventDoesNotRestartOrOverridePhase();
        UnknownStateSuspendsThenRetakesWithoutRebuild();
        UnknownAtApplyKeepsNativeVisible();
        NativeHiddenIsFollowedWithoutFighting();
        ThirdPartyHideIsNotOverridden();
        TakeoverWriteFailureKeepsStateAndRetries();
        ReleaseFailureKeepsOwnership();
        PoolLifeChangeRemovesAndRestores();
        DoubleEntrySameFrameRunsOnceAndPanelWins();
        DiagnosticsBoundedAndResetOnClear();
        PrepareWindowComesOnlyFromRecordedCadence();
        ReleasePresentationFollowsShotEvent();
        VisitAndLifecycleDiagnosticsAreBoundedAndEventTriggered();

        Console.WriteLine();
        Console.WriteLine(_failed == 0
            ? "ALL VISUALS-NATIVE-STUB TESTS PASSED (" + _passed + ")"
            : _failed + " VISUALS-NATIVE-STUB TEST(S) FAILED, " + _passed + " passed");
        return _failed == 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------- harness

    private sealed class Fixture
    {
        internal GameObject Body;
        internal SpriteRenderer Native;
        internal Archer Archer;
        internal Animator Animator;
    }

    private static void AdvanceFrame(float dt = 1f / 60f)
    {
        Time.frameCount = ++_frame;   // 帧号必须推进：Sync 按帧号去重
        Time.deltaTime = dt;
        Time.time += dt;
    }

    private static Fixture CreateFixture(int life = 1)
    {
        Managers.Inst = new Managers { world = new World { gameLayer = new GameObject("gameLayer").transform } };
        Fixture fixture = new Fixture();
        fixture.Body = new GameObject("ArcherBody");
        fixture.Native = fixture.Body.AddComponent<SpriteRenderer>();
        fixture.Archer = fixture.Body.AddComponent<Archer>();
        fixture.Archer.Pointer = new IntPtr(0x1234);
        fixture.Animator = fixture.Body.AddComponent<Animator>();
        fixture.Archer._animator = fixture.Animator;
        HeroArcherRuntime.Life = life;
        HeroArcherRuntime.Hero = true;
        HeroArcherRuntime.LifeReadable = true;
        SetState(fixture, "Stand", 0f);
        return fixture;
    }

    private static void SetState(Fixture fixture, string stateName, float normalizedTime, float clipLength = 0f)
        => fixture.Animator.Current = new AnimatorStateInfo(Animator.StringToHash(stateName), normalizedTime, clipLength);

    private static void FreshWorld()
    {
        GameObject.ResetRegistry();
        HeroArcherVisuals.Clear();
        HeroArcherCloth.ResetCounters();
        HeroVisualPriority.Fail = false;
        LogSourceStub.Reset();
        Managers.Inst = null;
    }

    private static GameObject FindLive(string name)
    {
        for (int i = 0; i < GameObject.All.Count; i++)
        {
            GameObject candidate = GameObject.All[i];
            if (!candidate.Destroyed && candidate.name == name) return candidate;
        }
        return null;
    }

    private static int CountLive(string name)
    {
        int count = 0;
        for (int i = 0; i < GameObject.All.Count; i++)
        {
            if (!GameObject.All[i].Destroyed && GameObject.All[i].name == name) count++;
        }
        return count;
    }

    private static SpriteRenderer OwnBody()
    {
        GameObject root = FindLive("KEM_HeroArcherSprite");
        return root != null ? root.GetComponent<SpriteRenderer>() : null;
    }

    /// <summary>按挂点父对象定位某个 fixture 的自有 renderer（两个英雄共存时 OwnBody 不够用）。</summary>
    private static SpriteRenderer OwnOf(Fixture fixture)
    {
        if (fixture == null || fixture.Native == null) return null;
        for (int i = 0; i < GameObject.All.Count; i++)
        {
            GameObject root = GameObject.All[i];
            if (root == null || root.Destroyed || root.name != "KEM_HeroArcherSprite") continue;
            if (root.transform.parent == fixture.Native.transform) return root.GetComponent<SpriteRenderer>();
        }
        return null;
    }

    private static GameObject OwnRoot() => FindLive("KEM_HeroArcherSprite");

    /// <summary>从 sprite 的 atlas rect 反推帧号（测试独立重算 8x4 / 48x32 布局，不复用生产帧映射）。</summary>
    private static int FrameOf(SpriteRenderer renderer)
    {
        if (renderer == null || renderer.sprite == null) return -1;
        Rect rect = renderer.sprite.rect;
        int column = (int)Math.Round(rect.x / 48f);
        int rowFromBottom = (int)Math.Round(rect.y / 32f);
        int row = 3 - rowFromBottom;
        return row * 8 + column;
    }

    private static int NativeLines() => LogSourceStub.CountContaining("[HeroArcherNative]");

    private static int VisitLines() => LogSourceStub.CountContaining("[HeroArcherPoseVisit]");

    private static int LifeLines() => LogSourceStub.CountContaining("[HeroArcherVisualLife]");

    private static string LastNativeLine() => LastLine("[HeroArcherNative]");

    private static string LastLine(string tag)
    {
        for (int i = LogSourceStub.Lines.Count - 1; i >= 0; i--)
        {
            if (LogSourceStub.Lines[i].Contains(tag)) return LogSourceStub.Lines[i];
        }
        return "";
    }

    // ---------------------------------------------------------------- tests

    private static void ApplyTakesOverSupportedState()
    {
        FreshWorld();
        Fixture fixture = CreateFixture();
        SetState(fixture, "Stand", 0.05f);
        HeroArcherVisuals.Apply(fixture.Archer);

        SpriteRenderer own = OwnBody();
        Check("apply.registered", HeroArcherVisuals.Count == 1 && HeroArcherVisuals.HasVisual(fixture.Archer));
        if (own == null)
        {
            Check("apply.ownExists", false, "自有 renderer 未创建（见日志）");
            return;
        }
        Check("apply.nativeHidden", fixture.Native.forceRenderingOff, "接管后必须隐藏原生 renderer");
        Check("apply.ownVisible", own != null && own.enabled, "自有 renderer 必须显示");
        Check("apply.frame", FrameOf(own) == 0, "Stand 相位 0.05 → 帧 0，got " + FrameOf(own));
        Check("apply.anchor", own != null && own.transform.parent == fixture.Native.transform,
            "自有对象必须挂在原生 renderer 的 Transform 下（位置/朝向/缩放继承）");
        Check("apply.scale", Math.Abs(own.transform.localScale.x - 0.9f) < 1e-5f
            && Math.Abs(own.transform.localScale.y - 0.9f) < 1e-5f && Math.Abs(own.transform.localScale.z - 1f) < 1e-5f,
            "0.9 表现缩放（z 保持 1），got " + own.transform.localScale);
        Check("apply.cloth", HeroArcherCloth.Created == 1);
    }

    private static void SyncFollowsNativePhaseWithoutOwnClock()
    {
        FreshWorld();
        Fixture fixture = CreateFixture();
        HeroArcherVisuals.Apply(fixture.Archer);

        SetState(fixture, "Walk", 0.3f);
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        SpriteRenderer own = OwnBody();
        Check("sync.ownExists", own != null);
        if (own == null) return;
        Check("sync.walkFrame", FrameOf(own) == 11, "Walk 相位 0.3 → 帧 11，got " + FrameOf(own));

        // 自有时钟推进（游戏时间 +5s）但原生相位冻结：帧必须不动（相位只来自原生 normalizedTime）。
        Time.time += 5f;
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        Check("sync.frozenNativePhaseHoldsFrame", FrameOf(own) == 11, "相位冻结时不得按自有时钟重算，got " + FrameOf(own));

        // 原生多圈：13.3 → 小数 0.3 → 帧 11（不是从 0 重启）。
        SetState(fixture, "Walk", 13.3f);
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        Check("sync.multiTurnFraction", FrameOf(own) == 11, "多圈相位取小数，got " + FrameOf(own));

        SetState(fixture, "Run", 0.875f);
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        Check("sync.runSwitch", FrameOf(own) == 21, "Run 相位 0.875 → 帧 21，got " + FrameOf(own));

        // 单次动作：相位 >1 停在末帧。
        SetState(fixture, "Shoot", 3.5f);
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        Check("sync.singleClamp", FrameOf(own) == 30, "Shoot 相位 3.5 → 末帧 30，got " + FrameOf(own));

        // 非法相位：确定性退回该动作首帧，不抛、不黑帧。
        SetState(fixture, "Prepare", float.NaN);
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        Check("sync.illegalPhase", !own.enabled && !fixture.Native.forceRenderingOff && HeroArcherVisuals.HasVisual(fixture.Archer),
            "NaN 相位 → 显式交还原生但保持VisualState，got " + FrameOf(own) + " enabled=" + own.enabled);
    }

    private static void ShotEventDoesNotRestartOrOverridePhase()
    {
        FreshWorld();
        Fixture fixture = CreateFixture();
        HeroArcherVisuals.Apply(fixture.Archer);
        SetState(fixture, "Walk", 0.3f);
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        SpriteRenderer own = OwnBody();
        int before = FrameOf(own);

        HeroArcherVisuals.NotifyRelease(fixture.Archer);   // 原生放箭事件
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        Check("shot.doesNotOverrideFrame", FrameOf(own) == before && before == 11,
            "OnShot 不得覆盖原生姿势，got " + FrameOf(own) + " before=" + before);

        // 事件只并入下一条有界诊断行（shot=1），不产生额外行。
        int linesBefore = NativeLines();
        SetState(fixture, "Run", 0.5f);
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        Check("shot.markedInDiagnostics", NativeLines() == linesBefore + 1 && LastNativeLine().Contains("shot=1"),
            "放箭标记应并入下一条转场诊断行，line=" + LastNativeLine());
    }

    private static void UnknownStateSuspendsThenRetakesWithoutRebuild()
    {
        FreshWorld();
        Fixture fixture = CreateFixture();
        HeroArcherVisuals.Apply(fixture.Archer);
        SpriteRenderer own = OwnBody();
        GameObject ownRoot = OwnRoot();
        Check("unknown.precondition", own.enabled && fixture.Native.forceRenderingOff);

        SetState(fixture, "Ghost Die", 0.4f);
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        Check("unknown.suspendsOwn", !own.enabled, "未绘状态必须隐藏自有 body");
        Check("unknown.restoresNative", !fixture.Native.forceRenderingOff, "未绘状态必须归还原生 renderer");
        Check("unknown.keepsState", HeroArcherVisuals.Count == 1 && HeroArcherVisuals.HasVisual(fixture.Archer),
            "未知状态不得移除/重建凭据");
        Check("unknown.sameRoot", ReferenceEquals(OwnRoot(), ownRoot) && CountLive("KEM_HeroArcherSprite") == 1,
            "未知状态期间不得重建自有对象");
        Check("unknown.clothKept", HeroArcherCloth.Destroyed == 0 && HeroArcherCloth.LastTickVisible == 0,
            "布料必须保留句柄并随 body 隐藏（Tick 仍每帧一次）");

        SetState(fixture, "Spawn", 0.2f);
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        Check("unknown.spawnAlsoSuspended", !own.enabled && !fixture.Native.forceRenderingOff);

        SetState(fixture, "Stand", 0f);
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        Check("unknown.retakes", own.enabled && fixture.Native.forceRenderingOff,
            "回到支持状态必须安全接管（隐藏原生 + 显示自有）");
        Check("unknown.noRebuildOnRetake", ReferenceEquals(OwnRoot(), ownRoot) && CountLive("KEM_HeroArcherSprite") == 1);

        // 换皮 controller 的其它状态名（Norse 系）同样按未知处理。
        SetState(fixture, "Defend", 0.5f);
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        Check("unknown.foreignControllerState", !own.enabled && !fixture.Native.forceRenderingOff);

        // 不可读（读取抛错）同样挂起但保留凭据。
        fixture.Animator.ThrowOnRead = true;
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        Check("unreadable.suspends", !own.enabled && HeroArcherVisuals.Count == 1,
            "读取失败必须挂起而不是丢凭据");
        fixture.Animator.ThrowOnRead = false;
        SetState(fixture, "Walk", 0.25f);
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        Check("unreadable.recovers", own.enabled && fixture.Native.forceRenderingOff && FrameOf(own) == 11);
    }

    private static void UnknownAtApplyKeepsNativeVisible()
    {
        FreshWorld();
        Fixture fixture = CreateFixture();
        SetState(fixture, "Spawn", 0.5f);
        HeroArcherVisuals.Apply(fixture.Archer);
        SpriteRenderer own = OwnBody();
        Check("apply.unknownKeepsNative", !fixture.Native.forceRenderingOff && !own.enabled,
            "未绘状态起步时：原生显现、自有隐藏");
        Check("apply.unknownRegistered", HeroArcherVisuals.Count == 1 && HeroArcherVisuals.HasVisual(fixture.Archer));

        SetState(fixture, "Stand", 0f);
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        Check("apply.unknownThenTakesOver", own.enabled && fixture.Native.forceRenderingOff);
    }

    private static void NativeHiddenIsFollowedWithoutFighting()
    {
        FreshWorld();
        Fixture fixture = CreateFixture();
        HeroArcherVisuals.Apply(fixture.Archer);
        SpriteRenderer own = OwnBody();
        int writes = fixture.Native.ForceRenderingOffWrites;

        fixture.Native.enabled = false;   // 原生 SetHideStatus：必须跟随隐藏
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        Check("nativeHidden.follows", !own.enabled, "原生隐藏时必须隐藏自有 renderer，绝不强行显示");
        Check("nativeHidden.keepsCredential", fixture.Native.forceRenderingOff && fixture.Native.ForceRenderingOffWrites == writes,
            "原生本就不可见时保留凭据、不来回抢放");

        fixture.Native.enabled = true;
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        Check("nativeVisible.resumes", own.enabled && fixture.Native.forceRenderingOff
            && fixture.Native.ForceRenderingOffWrites == writes,
            "原生恢复可见时直接还原自有显示，不需重新写 forceRenderingOff（无闪烁窗口）");
    }

    private static void ThirdPartyHideIsNotOverridden()
    {
        FreshWorld();
        Fixture fixture = CreateFixture();
        HeroArcherVisuals.Apply(fixture.Archer);
        SpriteRenderer own = OwnBody();

        SetState(fixture, "Ghost Die", 0.5f);
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");   // 挂起 → 归还我们的隐藏
        Check("thirdParty.releasedOnSuspend", !fixture.Native.forceRenderingOff && !own.enabled);

        fixture.Native.forceRenderingOff = true;   // 第三方（其它 mod / 原生清理）设的隐藏
        int writes = fixture.Native.ForceRenderingOffWrites;
        SetState(fixture, "Stand", 0f);
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        Check("thirdParty.notOverridden", !own.enabled && fixture.Native.ForceRenderingOffWrites == writes,
            "不得覆盖第三方设置的隐藏（自有不得比本体更显眼）");

        fixture.Native.forceRenderingOff = false;   // 第三方归还
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        Check("thirdParty.retakeAfterRelease", own.enabled && fixture.Native.forceRenderingOff,
            "第三方归还后我们才能再接管");
    }

    private static void TakeoverWriteFailureKeepsStateAndRetries()
    {
        FreshWorld();
        Fixture fixture = CreateFixture();
        fixture.Native.FailNextForceRenderingOffWrite = true;
        HeroArcherVisuals.Apply(fixture.Archer);
        SpriteRenderer own = OwnBody();
        Check("takeoverFail.keepsState", HeroArcherVisuals.Count == 1 && !fixture.Native.forceRenderingOff,
            "写失败不得回滚状态（没写成功就不持有，下一帧重试）");
        Check("takeoverFail.staysHidden", !own.enabled, "写失败帧不得显示自有 renderer");

        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        Check("takeoverFail.retries", own.enabled && fixture.Native.forceRenderingOff,
            "下一帧必须重试接管成功");
        SetState(fixture, "Ghost Die", .2f);
        AdvanceFrame(); HeroArcherVisuals.Sync("panel");
        fixture.Native.FailAfterForceRenderingOffWrite = true;
        SetState(fixture, "Walk", .3f);
        AdvanceFrame(); HeroArcherVisuals.Sync("panel");
        Check("takeoverPartial.restoresNative", !fixture.Native.forceRenderingOff && !own.enabled);
        AdvanceFrame(); HeroArcherVisuals.Sync("panel");
        Check("takeoverPartial.recovers", fixture.Native.forceRenderingOff && own.enabled);
    }

    private static void ReleaseFailureKeepsOwnership()
    {
        FreshWorld();
        Fixture fixture = CreateFixture();
        HeroArcherVisuals.Apply(fixture.Archer);
        SpriteRenderer own = OwnBody();

        fixture.Native.FailNextForceRenderingOffWrite = true;
        SetState(fixture, "Ghost Die", 0.5f);
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        Check("releaseFail.keepsCredential", fixture.Native.forceRenderingOff && !own.enabled,
            "归还失败必须保留凭据（绝不丢归还责任）");

        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        Check("releaseFail.retries", !fixture.Native.forceRenderingOff, "下一帧必须完成归还，让原生显现");
    }

    private static void PoolLifeChangeRemovesAndRestores()
    {
        FreshWorld();
        Fixture fixture = CreateFixture();
        HeroArcherVisuals.Apply(fixture.Archer);
        GameObject ownRoot = OwnRoot();

        HeroArcherRuntime.Life = 2;   // 池复用/换 life：不再是同一个 live 生命
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        Check("pool.removed", HeroArcherVisuals.Count == 0 && !HeroArcherVisuals.HasVisual(fixture.Archer));
        Check("pool.nativeRestored", !fixture.Native.forceRenderingOff);
        Check("pool.ownDestroyed", ownRoot.Destroyed && OwnRoot() == null);
        Check("pool.clothDestroyed", HeroArcherCloth.Destroyed == 1);
    }

    private static void DoubleEntrySameFrameRunsOnceAndPanelWins()
    {
        FreshWorld();
        Fixture fixture = CreateFixture();
        HeroArcherVisuals.Apply(fixture.Archer);

        SetState(fixture, "Run", 0.5f);
        AdvanceFrame();
        int ticks = HeroArcherCloth.TickCalls;
        int lines = NativeLines();
        HeroArcherVisuals.Sync("panel");   // 面板 LateUpdate 入口（operator 迁移后的入口）
        HeroArcherVisuals.Sync("driver");  // 驱动 LateUpdate 入口（同帧第二入口）
        Check("doubleEntry.singleExecution", HeroArcherCloth.TickCalls == ticks + 1,
            "同帧双入口必须只执行一次，ticks=" + (HeroArcherCloth.TickCalls - ticks));
        Check("doubleEntry.singleEvent", NativeLines() == lines + 1 && LastNativeLine().Contains("src=panel"),
            "事件行只记一次且记录赢得去重的入口，line=" + LastNativeLine());
        Check("doubleEntry.frameFollowed", FrameOf(OwnBody()) == 19, "Run 0.5 → 帧 19，got " + FrameOf(OwnBody()));
    }

    private static void DiagnosticsBoundedAndResetOnClear()
    {
        FreshWorld();
        Fixture keep = CreateFixture();
        HeroArcherVisuals.Apply(keep.Archer);

        // 稳定转场重复采样不重复记录（20 帧同一状态/相位）。
        int stable = NativeLines();
        for (int i = 0; i < 20; i++)
        {
            AdvanceFrame();
            HeroArcherVisuals.Sync("panel");
        }
        Check("diagnostics.dedupedWhenStable", NativeLines() == stable,
            "同一转场不得逐帧写日志，got " + (NativeLines() - stable) + " 行");

        // actor churn（池化抖动）必须停在全局上限，不能随事件数增长。
        for (int i = 0; i < 80; i++)
        {
            Fixture churn = CreateFixture();
            HeroArcherVisuals.Apply(churn.Archer);
            SetState(churn, "Run", 0.5f);
            AdvanceFrame();
            HeroArcherVisuals.Sync("panel");
            HeroArcherVisuals.Remove(churn.Archer);
        }
        Check("diagnostics.capacity", NativeLines() == HeroArcherNativeEventLog.Capacity,
            "全局事件上限必须恰好为 " + HeroArcherNativeEventLog.Capacity + "，got " + NativeLines());

        // 用尽后仍不增长，且渲染继续跟随（日志失败/上限不破坏渲染）。
        SetState(keep, "Prepare", 0.5f);
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        Check("diagnostics.exhaustedStopsWriting", NativeLines() == HeroArcherNativeEventLog.Capacity);
        Check("diagnostics.renderStillWorks", FrameOf(OwnBody()) == 24 && OwnBody().enabled,
            "日志用尽后渲染必须照常，got frame=" + FrameOf(OwnBody()));

        // 换 world / 关功能：归还并重置诊断预算。
        HeroArcherVisuals.Clear();
        Check("clear.restores", !keep.Native.forceRenderingOff && HeroArcherVisuals.Count == 0);
        Fixture next = CreateFixture();
        HeroArcherVisuals.Apply(next.Archer);
        Check("clear.resetsBudget", NativeLines() == HeroArcherNativeEventLog.Capacity + 1,
            "Clear 后诊断预算必须重置（新事件可再记一行），got " + NativeLines());
    }

    /// <summary>
    /// Prepare 窗口只来自 operator 桥的 RecordPrepareWindow（原生同一步 MoveNext 读到的临时 shootPrepTime）：
    /// pending 记录、Prepare 进入/同状态回绕时锁入 active、进行中的拉弓不改窗口、缺失回退原生相位、
    /// 非法/陌生/life 不符被忽略、池/换世界重建后不沿用。
    /// </summary>
    private static void PrepareWindowComesOnlyFromRecordedCadence()
    {
        FreshWorld();
        Fixture fixture = CreateFixture();
        HeroArcherVisuals.Apply(fixture.Archer);
        SpriteRenderer own = OwnBody();
        if (own == null) { Check("window.ownExists", false); return; }

        // 未记录窗口 → 原生相位回退（ct 0.108 但无窗口：nt 0.05 → 帧 22）。
        SetState(fixture, "Prepare", 0.05f, 2.1666665f);
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        Check("window.missingFallsBack", FrameOf(own) == 22, "无窗口必须回退原生相位，got " + FrameOf(own));

        // 记录 0.2s → 下一次 Prepare 进入时锁入（锁定发生在采样之后，故进入帧本身还用旧窗口）。
        SetState(fixture, "Stand", 0f, 1.6666665f);
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        HeroArcherVisuals.RecordPrepareWindow(fixture.Archer, 0.2f);
        SetState(fixture, "Prepare", 0.05f, 2.1666665f);
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        Check("window.entryFrameUsesNewWindow", FrameOf(own) == 24,
            "进入帧在锁定之前采样（仍回退原生相位），got " + FrameOf(own));
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        Check("window.recordedLocksAtEntry", FrameOf(own) == 24, "记录 0.2 → ct 0.108/0.2=0.54 → 帧 24，got " + FrameOf(own));

        // 进行中的拉弓不被中途改窗口（pending 0.5 不影响 active 0.2）。
        HeroArcherVisuals.RecordPrepareWindow(fixture.Archer, 0.5f);
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        Check("window.activeLockedMidPrepare", FrameOf(own) == 24,
            "进行中的拉弓必须保持锁定窗口 0.2（否则会跳到 22），got " + FrameOf(own));

        // 下一次 Prepare 进入才用新 pending（进入帧仍用旧 active，次帧起 0.5 生效：ct 0.108/0.5=0.217 → 22）。
        SetState(fixture, "Shoot", 0f, 0.3f);
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        SetState(fixture, "Prepare", 0.05f, 2.1666665f);
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        Check("window.nextPrepareUsesPending", FrameOf(own) == 22, "新 pending 0.5 → 帧 22，got " + FrameOf(own));

        // 同状态回绕（nt 从 0.10 递减到 0.05）→ 重新锁入 pending 0.2（回绕帧本身仍用旧窗口）。
        HeroArcherVisuals.RecordPrepareWindow(fixture.Archer, 0.2f);
        SetState(fixture, "Prepare", 0.10f, 2.1666665f);
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        SetState(fixture, "Prepare", 0.05f, 2.1666665f);
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        Check("window.rollbackUsesNewWindowImmediately", FrameOf(own) == 24,
            "回绕帧仍用旧窗口（锁定发生在采样之后），got " + FrameOf(own));
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        Check("window.rollbackRelocks", FrameOf(own) == 24, "回绕后必须锁入 pending 0.2 → 帧 24，got " + FrameOf(own));

        // 非法秒数 / 陌生 archer / life 不符全部忽略（仍用 0.2 → 帧 24）。
        HeroArcherVisuals.RecordPrepareWindow(fixture.Archer, float.NaN);
        HeroArcherVisuals.RecordPrepareWindow(fixture.Archer, 0f);
        HeroArcherVisuals.RecordPrepareWindow(fixture.Archer, -1f);
        Fixture foreign = CreateFixture();
        HeroArcherVisuals.RecordPrepareWindow(foreign.Archer, 0.9f);
        HeroArcherRuntime.Life = 7;
        HeroArcherVisuals.RecordPrepareWindow(fixture.Archer, 0.9f);
        HeroArcherRuntime.Life = 1;
        SetState(fixture, "Stand", 0f, 1.6666665f);
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        SetState(fixture, "Prepare", 0.05f, 2.1666665f);
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        Check("window.invalidForeignAndLifeIgnored", FrameOf(own) == 24,
            "非法/陌生/life 不符的记录必须被忽略（仍 0.2 → 24），got " + FrameOf(own));

        // 池复用/换世界：重建后 pending/active 归零，绝不沿用旧窗口。
        HeroArcherRuntime.Life = 2;
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        Check("window.poolRemoved", HeroArcherVisuals.Count == 0 && !fixture.Native.forceRenderingOff);
        HeroArcherRuntime.Life = 3;
        HeroArcherVisuals.Apply(fixture.Archer);
        SpriteRenderer fresh = OwnOf(fixture);
        SetState(fixture, "Prepare", 0.05f, 2.1666665f);
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        Check("window.notReusedAfterPool", fresh != null && FrameOf(fresh) == 22,
            "重建后必须回退原生相位（不沿用 0.2），got " + (fresh != null ? FrameOf(fresh) : -1));
    }

    /// <summary>
    /// 事件锚定释放表现：真实放箭事件驱动自有帧 26..30（0.18s 有界），覆盖 perfect 弹跳过原生 Shoot 状态的
    /// 观感；同帧散射多发不重启、隔帧放箭重启；Walk/Run/Shoot/Unknown/新 Prepare 立即取消；
    /// 暂停冻结、超时回原生；关闭/池复用后不残留；两个英雄独立。
    /// </summary>
    private static void ReleasePresentationFollowsShotEvent()
    {
        FreshWorld();
        Fixture fixture = CreateFixture();
        HeroArcherVisuals.Apply(fixture.Archer);
        SpriteRenderer own = OwnBody();
        if (own == null) { Check("release.ownExists", false); return; }

        // perfect→Stand：原生直接回 Stand（跳过 Shoot），自有释放仍必须可见。
        SetState(fixture, "Prepare", 0.107f, 2.1666665f);
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        HeroArcherVisuals.NotifyRelease(fixture.Archer);
        HeroArcherVisuals.NotifyRelease(fixture.Archer);   // 同帧散射多发：不得重启
        SetState(fixture, "Stand", 0f, 1.6666665f);
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        Check("release.firstFrame", FrameOf(own) == 26, "放箭帧必须显示 26，got " + FrameOf(own));

        // 每 Sync = 1/60s：放箭帧起走 26,26,27,27,28,28,29,29,30,30，第 11 帧超时回原生 Stand(0)。
        // 同一帧里的两次 NotifyRelease 必须给出与单次完全一致的进度（不重启、不跳帧）。
        int[] expected = { 26, 26, 27, 27, 28, 28, 29, 29, 30, 30 };
        for (int i = 0; i < expected.Length; i++)
        {
            AdvanceFrame();
            HeroArcherVisuals.Sync("panel");
            Check("release.step" + i, FrameOf(own) == expected[i],
                "释放第 " + (i + 1) + " 帧应为 " + expected[i] + "，got " + FrameOf(own));
        }
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        Check("release.boundedEnd", FrameOf(own) == 0,
            "0.18s 上限后必须结束释放并回原生 Stand，got " + FrameOf(own));
        HeroArcherVisuals.NotifyRelease(fixture.Archer);
        AdvanceFrame(0.2f); HeroArcherVisuals.Sync("panel");
        Check("release.slowFrameStillShowsKeyPose", FrameOf(own) == 26);
        AdvanceFrame(0.2f); HeroArcherVisuals.Sync("panel");
        Check("release.slowFrameExpires", FrameOf(own) == 0);

        // 隔帧的真实放箭：重新开始（1 帧 → 26）。
        HeroArcherVisuals.NotifyRelease(fixture.Archer);
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        Check("release.restartAfterEnd", FrameOf(own) == 26, "got " + FrameOf(own));

        // 释放进行到 27 时再来一发（隔帧）：必须回到 26（若继续推进会停在 27）。
        AdvanceFrame(); HeroArcherVisuals.Sync("panel");   // 0.033 → 26
        AdvanceFrame(); HeroArcherVisuals.Sync("panel");   // 0.05 → 27
        HeroArcherVisuals.NotifyRelease(fixture.Archer);
        AdvanceFrame(); HeroArcherVisuals.Sync("panel");   // 重启 → 0.017 → 26
        Check("release.restartFromMidRelease", FrameOf(own) == 26, "重启第一帧必须是 26，got " + FrameOf(own));

        // 原生 Shoot 可靠存在：优先原生相位（不走自有释放时钟）。
        AdvanceFrame(); HeroArcherVisuals.Sync("panel");   // 让上个释放继续一帧
        SetState(fixture, "Prepare", 0.107f, 2.1666665f);
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        HeroArcherVisuals.NotifyRelease(fixture.Archer);
        SetState(fixture, "Shoot", 0.9f, 0.3f);
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        Check("release.nativeShootWins", FrameOf(own) == 30,
            "原生 Shoot nt 0.9 → 帧 30（自有释放时钟若生效会给 26），got " + FrameOf(own));
        SetState(fixture, "Stand", 0f, 1.6666665f);
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        Check("release.noReplayAfterNativeShoot", FrameOf(own) == 0,
            "原生已播完释放：不得再冒出 26..30，got " + FrameOf(own));

        // 放箭后进入 Walk：立即取消，绝不冻结 locomotion。
        SetState(fixture, "Prepare", 0.107f, 2.1666665f);
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        HeroArcherVisuals.NotifyRelease(fixture.Archer);
        SetState(fixture, "Walk", 0.3f, 1.5f);
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        Check("release.walkCancels", FrameOf(own) == 11, "Walk 必须立即取消释放并跟原生相位，got " + FrameOf(own));

        // 新的 Prepare 进入：立即取消（新一次拉弓接管）。
        SetState(fixture, "Prepare", 0.107f, 2.1666665f);
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        HeroArcherVisuals.NotifyRelease(fixture.Archer);
        SetState(fixture, "Stand", 0f, 1.6666665f);
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        SetState(fixture, "Prepare", 0.05f, 2.1666665f);
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        Check("release.newPrepareCancels", FrameOf(own) == 22,
            "新 Prepare 必须取消释放并显示拉弓帧（若释放仍生效会给 26），got " + FrameOf(own));

        // 暂停（dt=0）：释放冻结，不推进也不超时。
        SetState(fixture, "Prepare", 0.107f, 2.1666665f);
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        HeroArcherVisuals.NotifyRelease(fixture.Archer);
        SetState(fixture, "Stand", 0f, 1.6666665f);
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        Check("release.pausePrecondition", FrameOf(own) == 26);
        for (int i = 0; i < 30; i++) { AdvanceFrame(0f); HeroArcherVisuals.Sync("panel"); }
        Check("release.pauseFreezes", FrameOf(own) == 26, "dt=0 时释放不得推进/超时，got " + FrameOf(own));
        AdvanceFrame(); HeroArcherVisuals.Sync("panel");
        Check("release.resumesAfterPause", FrameOf(own) == 26, "恢复第一帧仍 26（0.033s），got " + FrameOf(own));
        AdvanceFrame(); HeroArcherVisuals.Sync("panel");
        Check("release.resumesProgress", FrameOf(own) == 26, "恢复两帧累计0.033s，仍为26，got " + FrameOf(own));
        AdvanceFrame(); HeroArcherVisuals.Sync("panel");
        Check("release.resumesThirdFrame", FrameOf(own) == 27, "恢复三帧累计0.05s，进入27，got " + FrameOf(own));

        // 关闭：视觉被移除，释放随之消失且不残留。
        HeroArcherRuntime.Hero = false;
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        Check("release.offRemovesVisual", HeroArcherVisuals.Count == 0 && !fixture.Native.forceRenderingOff);
        HeroArcherRuntime.Hero = true;
        HeroArcherVisuals.Apply(fixture.Archer);
        SetState(fixture, "Stand", 0f, 1.6666665f);
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        SpriteRenderer fresh = OwnOf(fixture);
        Check("release.noStaleAfterReapply", fresh != null && FrameOf(fresh) == 0,
            "重新挂载后不得带旧释放状态（旧对象已销毁，引用作废），got " + (fresh != null ? FrameOf(fresh) : -1));

        // 两个英雄独立：A 释放时 B 仍跟自身原生相位。
        Fixture other = CreateFixture();
        HeroArcherVisuals.Apply(other.Archer);
        SetState(other, "Walk", 0.3f, 1.5f);
        SetState(fixture, "Prepare", 0.107f, 2.1666665f);
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        HeroArcherVisuals.NotifyRelease(fixture.Archer);
        SetState(fixture, "Stand", 0f, 1.6666665f);
        AdvanceFrame();
        HeroArcherVisuals.Sync("panel");
        SpriteRenderer otherOwn = OwnOf(other);
        Check("release.twoHeroesIndependent", FrameOf(OwnOf(fixture)) == 26 && otherOwn != null && FrameOf(otherOwn) == 11,
            "A 释放(26) 与 B 原生 Walk(11) 必须互不影响，got A=" + FrameOf(OwnOf(fixture))
            + " B=" + (otherOwn != null ? FrameOf(otherOwn) : -1));
    }

    /// <summary>访问行（相位推进证据）与生命周期行的有界性/事件触发契约。</summary>
    private static void VisitAndLifecycleDiagnosticsAreBoundedAndEventTriggered()
    {
        FreshWorld();
        Fixture fixture = CreateFixture();
        Check("visit.noLinesBeforeApply", LifeLines() == 0 && VisitLines() == 0);

        HeroArcherVisuals.Apply(fixture.Archer);
        Check("lifecycle.attachLogged", LifeLines() == 1 && LastLine("[HeroArcherVisualLife]").Contains("attach"),
            "line=" + LastLine("[HeroArcherVisualLife]"));

        // 转场行现在带 clip 长度/clip 时间/朝向证据/世界 y（实测诊断的字段契约）。
        string native = LastNativeLine();
        Check("diagnostics.clipAndFacingFields", native.Contains(" len=") && native.Contains(" ct=")
            && native.Contains(" face=") && native.Contains(" y="), "line=" + native);

        // Stand 停留 5 帧：不产生访问行（事件触发，非逐帧）。
        int visits = VisitLines();
        for (int i = 0; i < 5; i++) { AdvanceFrame(); HeroArcherVisuals.Sync("panel"); }
        Check("visit.eventTriggeredNotPerFrame", VisitLines() == visits);

        // Prepare 访问 → 动作结束时一行，含时长/clip 时间峰值/帧区间/窗口。
        SetState(fixture, "Prepare", 0.05f, 2.1666665f);
        AdvanceFrame(); HeroArcherVisuals.Sync("panel");
        AdvanceFrame(); HeroArcherVisuals.Sync("panel");
        SetState(fixture, "Stand", 0f, 1.6666665f);
        AdvanceFrame(); HeroArcherVisuals.Sync("panel");
        string visit = LastLine("[HeroArcherPoseVisit]");
        Check("visit.prepareLineLogged", VisitLines() == visits + 1
            && visit.Contains("action=Prepare") && visit.Contains("ctMax=")
            && visit.Contains("frames=") && visit.Contains("win=") && visit.Contains("next=Stand"),
            "line=" + visit);

        // 卸载：detach 行带原因（与 attach 配对暴露重建节奏）。
        HeroArcherVisuals.Remove(fixture.Archer);
        string detach = LastLine("[HeroArcherVisualLife]");
        Check("lifecycle.detachLogged", LifeLines() == 2 && detach.Contains("detach") && detach.Contains("reason=remove"),
            "line=" + detach);

        // 上限：70 次挂载/卸载（140 个事件）必须恰好停在 LifeLogCapacity，不随次数增长。
        for (int i = 0; i < 70; i++)
        {
            Fixture churn = CreateFixture();
            HeroArcherVisuals.Apply(churn.Archer);
            HeroArcherVisuals.Remove(churn.Archer);
        }
        Check("lifecycle.capacity", LifeLines() == HeroArcherVisuals.LifeLogCapacity,
            "生命周期行必须恰好停在上限 " + HeroArcherVisuals.LifeLogCapacity + "，got " + LifeLines());
        Check("visit.capacityNotExceeded", VisitLines() <= HeroArcherVisuals.VisitLogCapacity,
            "got " + VisitLines());
    }
}
