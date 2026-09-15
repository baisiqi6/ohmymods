using System;
using System.Collections.Generic;

namespace KingdomEnhancedMod.Tests;

/// <summary>
/// HeroArcherAnimation.cs 的无 Unity 测试：不依赖任何包、不触碰文件系统/网络/游戏。
/// 断言的是可观察契约：帧序号边界、循环/停帧语义、事件时钟、幂等、旧时间与非法时间防御。
/// 运行：C:/Users/ADMIN/dotnet8/dotnet.exe run --project tests/HeroArcherAnimation.Tests.csproj
/// </summary>
internal static class Program
{
    private static readonly List<string> Failures = new List<string>();
    private static string _currentTest = "<none>";
    private static int _checks;
    private static int _testChecks;

    private static int Main()
    {
        var tests = new List<KeyValuePair<string, Action>>
        {
            new KeyValuePair<string, Action>("AtlasLayoutMapsEveryFrameExactlyOnce", AtlasLayoutMapsEveryFrameExactlyOnce),
            new KeyValuePair<string, Action>("IdleLoopsAtSixFps", IdleLoopsAtSixFps),
            new KeyValuePair<string, Action>("WalkLoopsOnlyInsideItsOwnFrames", WalkLoopsOnlyInsideItsOwnFrames),
            new KeyValuePair<string, Action>("RunLoopsAtEightFps", RunLoopsAtEightFps),
            new KeyValuePair<string, Action>("FrameStepBoundariesDoNotStepEarly", FrameStepBoundariesDoNotStepEarly),
            new KeyValuePair<string, Action>("MotionChangeStartsClipAtItsFirstFrame", MotionChangeStartsClipAtItsFirstFrame),
            new KeyValuePair<string, Action>("RepeatedMotionEventsDoNotResetClip", RepeatedMotionEventsDoNotResetClip),
            new KeyValuePair<string, Action>("DrawPlaysToLastFrameAndHolds", DrawPlaysToLastFrameAndHolds),
            new KeyValuePair<string, Action>("RepeatedDrawEventsDoNotRestartDraw", RepeatedDrawEventsDoNotRestartDraw),
            new KeyValuePair<string, Action>("ReleaseClockHolds14ThenRecoveryThenMotion", ReleaseClockHolds14ThenRecoveryThenMotion),
            new KeyValuePair<string, Action>("ReleaseDuringVolleyReturnsToDrawHold", ReleaseDuringVolleyReturnsToDrawHold),
            new KeyValuePair<string, Action>("RepeatedReleaseInsideReleaseWindowIsIgnored", RepeatedReleaseInsideReleaseWindowIsIgnored),
            new KeyValuePair<string, Action>("BackToBackReleasesWithoutTickOpenFreshWindow", BackToBackReleasesWithoutTickOpenFreshWindow),
            new KeyValuePair<string, Action>("TickIsIdempotentPerTimestamp", TickIsIdempotentPerTimestamp),
            new KeyValuePair<string, Action>("FrameSequenceIsIndependentOfSamplingRate", FrameSequenceIsIndependentOfSamplingRate),
            new KeyValuePair<string, Action>("InvalidTimeSamplesKeepLastFrameAndChangeNothing", InvalidTimeSamplesKeepLastFrameAndChangeNothing),
            new KeyValuePair<string, Action>("BackwardTimeIsRejectedWithoutCorruption", BackwardTimeIsRejectedWithoutCorruption),
            new KeyValuePair<string, Action>("DisabledYieldsIdleFirstFrameAndIgnoresEvents", DisabledYieldsIdleFirstFrameAndIgnoresEvents),
            new KeyValuePair<string, Action>("ResetClearsActionAndAcceptsLowerClock", ResetClearsActionAndAcceptsLowerClock),
            new KeyValuePair<string, Action>("HugeElapsedTimeStaysBoundedInClipRange", HugeElapsedTimeStaysBoundedInClipRange),
            new KeyValuePair<string, Action>("ContinuousVolleysBeyondEightSecondsKeepDrawHold", ContinuousVolleysBeyondEightSecondsKeepDrawHold),
            new KeyValuePair<string, Action>("MonotonicClockRejectsBackwardEvents", MonotonicClockRejectsBackwardEvents),
            new KeyValuePair<string, Action>("RandomizedSequencesStayInAtlasBounds", RandomizedSequencesStayInAtlasBounds),
        };

        foreach (KeyValuePair<string, Action> test in tests)
        {
            _currentTest = test.Key;
            _testChecks = 0;
            int failuresBefore = Failures.Count;
            try
            {
                test.Value();
            }
            catch (Exception e)
            {
                Check(false, "threw " + e.GetType().Name + ": " + e.Message);
            }
            int produced = Failures.Count - failuresBefore;
            // 断言失败也算失败：只有本测试区间内 Failures 没增长才允许打印 PASS。
            Console.WriteLine((produced == 0 ? "[PASS] " : "[FAIL] ") + test.Key
                + " (" + _testChecks + " checks, " + produced + " failures)");
        }

        Console.WriteLine();
        Console.WriteLine(_checks + " checks, " + Failures.Count + " failures");
        if (Failures.Count == 0)
        {
            Console.WriteLine("ALL TESTS PASSED");
            return 0;
        }
        foreach (string failure in Failures) Console.WriteLine("  " + failure);
        return 1;
    }

    // ---------------------------------------------------------------- 断言

    private static void Check(bool condition, string what)
    {
        _checks++;
        _testChecks++;
        if (!condition) Failures.Add(_currentTest + ": " + what);
    }

    private static void Equal(int expected, int actual, string what)
        => Check(expected == actual, what + " expected=" + expected + " actual=" + actual);

    private static void ExpectMotionFrame(HeroArcherAnimation motion, double phase, int actual, string what)
    {
        HeroArcherAtlas.TryGetClip(motion, out HeroArcherClip clip);
        Equal(HeroArcherAtlas.FrameAt(clip, phase), actual, what + " (phase=" + phase + ")");
    }

    private static void InRange(int frame, int first, int last, string what)
        => Check(frame >= first && frame <= last, what + " frame=" + frame + " expected in [" + first + "," + last + "]");

    private static void ExpectAllFrames(int frame, string what)
        => InRange(frame, 0, HeroArcherAtlas.FrameCount - 1, what);

    private static HeroArcherAnimationState NewState() => new HeroArcherAnimationState();

    private static void EqualSequences(List<int> expected, List<int> actual, string what)
    {
        Equal(expected.Count, actual.Count, what + " length");
        int count = Math.Min(expected.Count, actual.Count);
        for (int i = 0; i < count; i++)
            if (expected[i] != actual[i]) Equal(expected[i], actual[i], what + " at sample " + i);
    }

    // ---------------------------------------------------------------- 布局

    private static void AtlasLayoutMapsEveryFrameExactlyOnce()
    {
        var claimed = new bool[HeroArcherAtlas.FrameCount];
        foreach (HeroArcherAnimation animation in Enum.GetValues(typeof(HeroArcherAnimation)))
        {
            Check(HeroArcherAtlas.TryGetClip(animation, out HeroArcherClip clip), "clip table has " + animation);
            if (clip.FrameCount <= 0) continue;
            for (int frame = clip.FirstFrame; frame <= clip.LastFrame; frame++)
            {
                InRange(frame, 0, claimed.Length - 1, "clip " + animation + " stays inside the atlas");
                if (frame < 0 || frame >= claimed.Length) continue;
                Check(!claimed[frame], "frame " + frame + " is claimed by exactly one clip (again by " + animation + ")");
                claimed[frame] = true;
            }
        }
        for (int frame = 0; frame < claimed.Length; frame++) Check(claimed[frame], "atlas frame " + frame + " is mapped");

        Equal(0, HeroArcherAtlas.IdleFirstFrame, "idle first frame");
        Equal(4, HeroArcherAtlas.WalkFirstFrame, "walk first frame");
        Equal(8, HeroArcherAtlas.RunFirstFrame, "run first frame");
        Equal(12, HeroArcherAtlas.DrawFirstFrame, "draw first frame");
        Equal(14, HeroArcherAtlas.ReleaseFrame, "release frame");
        Equal(15, HeroArcherAtlas.RecoveryFrame, "recovery frame");
        Equal(16, HeroArcherAtlas.FrameCount, "atlas frame count");

        HeroArcherAtlas.TryGetClip(HeroArcherAnimation.Walk, out HeroArcherClip walk);
        Check(walk.Loops, "walk loops");
        Equal(7, walk.LastFrame, "walk last frame");
        HeroArcherAtlas.TryGetClip(HeroArcherAnimation.Draw, out HeroArcherClip draw);
        Check(!draw.Loops, "draw does not loop");
        Equal(13, draw.LastFrame, "draw last frame");

        HeroArcherAtlas.FrameToCell(0, out int column, out int row);
        Equal(0, column, "cell(0).column");
        Equal(0, row, "cell(0).row");
        HeroArcherAtlas.FrameToCell(3, out column, out row);
        Equal(3, column, "cell(3).column");
        Equal(0, row, "cell(3).row");
        HeroArcherAtlas.FrameToCell(4, out column, out row);
        Equal(0, column, "cell(4).column");
        Equal(1, row, "cell(4).row");
        HeroArcherAtlas.FrameToCell(15, out column, out row);
        Equal(3, column, "cell(15).column");
        Equal(3, row, "cell(15).row");
    }

    // ---------------------------------------------------------------- 循环

    private static void IdleLoopsAtSixFps()
    {
        HeroArcherAnimationState state = NewState();
        state.SetMotion(HeroArcherAnimation.Idle, 100.0);
        var distinct = new HashSet<int>();
        for (int i = 0; i < 60; i++)
        {
            double t = 100.0 + i / 60.0;
            int frame = state.Tick(t);
            ExpectMotionFrame(HeroArcherAnimation.Idle, i / 60.0, frame, "idle sample " + i);
            InRange(frame, 0, 3, "idle sample " + i);
            distinct.Add(frame);
        }
        Equal(4, distinct.Count, "idle visits all four frames inside one second");
    }

    private static void WalkLoopsOnlyInsideItsOwnFrames()
    {
        HeroArcherAnimationState state = NewState();
        state.SetMotion(HeroArcherAnimation.Walk, 0.0);
        var distinct = new HashSet<int>();
        for (int i = 0; i < 60; i++)
        {
            double t = i / 60.0;
            int frame = state.Tick(t);
            ExpectMotionFrame(HeroArcherAnimation.Walk, t, frame, "walk sample " + i);
            InRange(frame, 4, 7, "walk sample " + i);
            distinct.Add(frame);
        }
        Equal(4, distinct.Count, "walk visits all four frames inside one second");
    }

    private static void RunLoopsAtEightFps()
    {
        HeroArcherAnimationState state = NewState();
        state.SetMotion(HeroArcherAnimation.Run, 5.0);
        var distinct = new HashSet<int>();
        for (int i = 0; i < 60; i++)
        {
            double t = 5.0 + i / 60.0;
            int frame = state.Tick(t);
            ExpectMotionFrame(HeroArcherAnimation.Run, i / 60.0, frame, "run sample " + i);
            InRange(frame, 8, 11, "run sample " + i);
            distinct.Add(frame);
        }
        Equal(4, distinct.Count, "run visits all four frames inside one second");
    }

    private static void FrameStepBoundariesDoNotStepEarly()
    {
        // 6 fps 的帧步进边界 k/6 秒不可精确表示；采样时刻取 100 + k/fps（再减 100 得相位）时，
        // 消减误差可达数个 ULP，曾把 floor 推低一帧（实际 Operator 跑出的 6 fps 边界失败）。
        // 这里双向钉住：边界前 1e-6 秒必须仍是上一帧（FrameEpsilon 只允许吸收 ULP，绝不允许
        // 把换帧提前到这个量级）；边界时刻必须且只能进入下一帧。
        var motions = new[]
        {
            HeroArcherAnimation.Idle,
            HeroArcherAnimation.Walk,
            HeroArcherAnimation.Run,
        };
        foreach (HeroArcherAnimation motion in motions)
        {
            HeroArcherAtlas.TryGetClip(motion, out HeroArcherClip clip);
            HeroArcherAnimationState state = NewState();
            const double anchor = 100.0;
            state.SetMotion(motion, anchor);
            Equal(clip.FirstFrame, state.Tick(anchor), motion + " starts on its first frame");

            for (int step = 1; step <= 3 * clip.FrameCount; step++)
            {
                double boundary = anchor + step / clip.Fps;
                Equal(clip.FirstFrame + (step - 1) % clip.FrameCount, state.Tick(boundary - 1e-6),
                    motion + " holds the previous frame 1e-6s before step " + step);
                Equal(clip.FirstFrame + step % clip.FrameCount, state.Tick(boundary),
                    motion + " advances at step " + step);
            }
        }
    }

    private static void MotionChangeStartsClipAtItsFirstFrame()
    {
        HeroArcherAnimationState state = NewState();
        state.SetMotion(HeroArcherAnimation.Walk, 50.0);
        Equal(4, state.Tick(50.0), "walk starts at its first frame");
        Equal(5, state.Tick(50.0 + 1.0 / 6.0), "walk advanced one step later");
        state.SetMotion(HeroArcherAnimation.Run, 50.5);
        Equal(8, state.Tick(50.5), "run restarts at its own first frame");
        state.SetMotion(HeroArcherAnimation.Idle, 51.0);
        Equal(0, state.Tick(51.0), "idle restarts at its own first frame");
    }

    private static void RepeatedMotionEventsDoNotResetClip()
    {
        HeroArcherAnimationState single = NewState();
        single.SetMotion(HeroArcherAnimation.Walk, 0.0);
        var singleFrames = new List<int>(60);
        for (int i = 0; i < 60; i++) singleFrames.Add(single.Tick(i / 60.0));

        HeroArcherAnimationState everyFrame = NewState();
        var repeatedFrames = new List<int>(60);
        for (int i = 0; i < 60; i++)
        {
            double t = i / 60.0;
            everyFrame.SetMotion(HeroArcherAnimation.Walk, t);
            repeatedFrames.Add(everyFrame.Tick(t));
        }

        EqualSequences(singleFrames, repeatedFrames, "per-frame SetMotion matches a single SetMotion");
        Equal(4, singleFrames[0], "walk frame at t=0");
        Equal(5, singleFrames[12], "walk advanced to frame 5 at t=0.2 (not restarted each frame)");
        Equal(6, singleFrames[24], "walk advanced to frame 6 at t=0.4");
        Equal(7, singleFrames[36], "walk advanced to frame 7 at t=0.6");
        Equal(4, singleFrames[48], "walk wrapped back to frame 4 at t=0.8");
    }

    // ---------------------------------------------------------------- 拉弓 / 放箭

    private static void DrawPlaysToLastFrameAndHolds()
    {
        HeroArcherAnimationState state = NewState();
        state.SetMotion(HeroArcherAnimation.Idle, 0.0);
        Equal(0, state.Tick(0.0), "idle before draw");

        state.BeginDraw(1.0);
        Equal(12, state.Tick(1.0), "draw first frame at draw start");
        Equal(12, state.Tick(1.124), "draw still first frame just before 1/8s");
        Equal(12, state.Tick(1.125 - 1e-6), "draw still first frame 1e-6s before the step boundary (no early step)");
        Equal(13, state.Tick(1.125), "draw second frame at 1/8s");
        Equal(13, state.Tick(1.5), "draw holds its last frame");
        for (double t = 1.125; t < 8.0; t += 0.25) Equal(13, state.Tick(t), "held last draw frame at t=" + t);
    }

    private static void RepeatedDrawEventsDoNotRestartDraw()
    {
        HeroArcherAnimationState state = NewState();
        state.SetMotion(HeroArcherAnimation.Idle, 0.0);
        state.BeginDraw(1.0);
        Equal(12, state.Tick(1.0), "draw first frame");
        state.BeginDraw(1.05);
        state.BeginDraw(2.0);
        Equal(13, state.Tick(2.0), "repeated draw events keep the original draw clock (still held last frame)");
    }

    private static void ReleaseClockHolds14ThenRecoveryThenMotion()
    {
        HeroArcherAnimationState state = NewState();
        state.SetMotion(HeroArcherAnimation.Walk, 0.0);
        state.BeginDraw(1.0);
        state.Tick(1.5);
        state.NotifyRelease(2.0);
        state.EndDraw(2.0);

        Equal(14, state.Tick(2.0), "release frame at the release event");
        Equal(14, state.Tick(2.1), "release frame still shown before 1/8s");
        Equal(14, state.Tick(2.124), "release frame just before 1/8s");
        Equal(14, state.Tick(2.125 - 1e-6), "release frame 1e-6s before its window ends (no early switch)");
        Equal(15, state.Tick(2.125), "recovery frame right after the release window");
        Equal(15, state.Tick(2.374), "recovery frame before its window ends");
        Equal(15, state.Tick(2.375 - 1e-6), "recovery frame 1e-6s before its window ends (no early switch)");
        Equal(4, state.Tick(2.375), "back to walk first frame at the exact recovery end");
        Equal(5, state.Tick(2.375 + 1.0 / 6.0), "walk clip advanced after returning to motion");
    }

    private static void ReleaseDuringVolleyReturnsToDrawHold()
    {
        HeroArcherAnimationState state = NewState();
        state.SetMotion(HeroArcherAnimation.Idle, 0.0);
        state.BeginDraw(1.0);
        state.NotifyRelease(2.0);

        Equal(14, state.Tick(2.05), "release frame while the draw request is still open");
        Equal(15, state.Tick(2.13), "recovery frame");
        Equal(13, state.Tick(2.375), "returns to held draw last frame, not idle");
        Equal(13, state.Tick(3.0), "keeps holding draw last frame while the volley is open");
        state.EndDraw(3.5);
        ExpectMotionFrame(HeroArcherAnimation.Idle, 3.5 - 2.375, state.Tick(3.5),
            "closing the draw request returns to the motion clip (phase anchored at the recovery end)");
    }

    private static void RepeatedReleaseInsideReleaseWindowIsIgnored()
    {
        HeroArcherAnimationState state = NewState();
        state.SetMotion(HeroArcherAnimation.Idle, 0.0);
        state.BeginDraw(1.0);
        state.NotifyRelease(2.0);
        Equal(14, state.Tick(2.05), "release frame");
        state.NotifyRelease(2.06);
        Equal(15, state.Tick(2.125), "duplicate release did not restart the 14 window");
        Equal(15, state.Tick(2.2), "still inside recovery after the duplicate release");

        state.NotifyRelease(2.3);
        Equal(14, state.Tick(2.3), "a release arriving after recovery opens a new release window");
    }

    private static void BackToBackReleasesWithoutTickOpenFreshWindow()
    {
        // 齐射回归（真实状态机 bug）：原生可能在同一帧/连续帧里再次 FireArrow，两箭之间没有 Tick。
        // 旧实现的 NotifyRelease 先判 _shot == Release 就提前 return，会直接吞掉第二箭。
        // 正确语义：先按当前 now 推进已过期的放箭窗口，再判「同一窗口内重复」。
        HeroArcherAnimationState expired = NewState();
        expired.SetMotion(HeroArcherAnimation.Idle, 0.0);
        expired.BeginDraw(1.0);
        expired.NotifyRelease(1.0);
        expired.NotifyRelease(1.25); // 第一箭窗口已在 1.125 到期（其间没有任何 Tick）
        Equal(14, expired.Tick(1.25), "second release without a tick re-shows 14");
        Equal(14, expired.Tick(1.374), "second window holds 14 until its own 1/8s boundary");
        Equal(15, expired.Tick(1.375), "second window rolls into recovery at its own boundary");
        Equal(13, expired.Tick(1.625), "after the second window the still-open draw resumes (not idle)");

        HeroArcherAnimationState duplicate = NewState();
        duplicate.SetMotion(HeroArcherAnimation.Idle, 0.0);
        duplicate.BeginDraw(1.0);
        duplicate.NotifyRelease(1.0);
        duplicate.NotifyRelease(1.05); // 同一窗口内的重复上报：不重置 14→15 时钟
        Equal(14, duplicate.Tick(1.05), "duplicate report inside the same window stays on 14");
        Equal(15, duplicate.Tick(1.125), "duplicate report did not reset the release window");
        Equal(13, duplicate.Tick(1.375), "the single window ends at its original boundary");

        HeroArcherAnimationState afterRecovery = NewState();
        afterRecovery.SetMotion(HeroArcherAnimation.Idle, 0.0);
        afterRecovery.BeginDraw(1.0);
        afterRecovery.NotifyRelease(1.0);
        afterRecovery.NotifyRelease(1.5); // 已越过 release+recovery：全新窗口（其间无 Tick）
        Equal(14, afterRecovery.Tick(1.5), "a later release without any tick opens a fresh window");
        Equal(15, afterRecovery.Tick(1.625), "the fresh window rolls into recovery 1/8s after it");
    }

    // ---------------------------------------------------------------- 时钟不变式

    private static void TickIsIdempotentPerTimestamp()
    {
        HeroArcherAnimationState once = NewState();
        HeroArcherAnimationState repeated = NewState();
        once.SetMotion(HeroArcherAnimation.Idle, 0.0);
        repeated.SetMotion(HeroArcherAnimation.Idle, 0.0);

        // 事件必须按真实顺序到达：先采完 0/0.25/0.5 的历史，再在 t=1.0 登记 BeginDraw，然后继续往未来采样。
        // 先登记未来事件再回读它之前的历史不属于契约（旧测试正是这样误报的）。
        const double drawStartTime = 1.0;
        double[] timeline = { 0.0, 0.25, 0.5, 1.0, 1.05, 1.125, 1.4, 1.5, 2.0, 3.0 };
        foreach (double t in timeline)
        {
            if (t == drawStartTime)
            {
                once.BeginDraw(t);
                repeated.BeginDraw(t);
            }
            int expected = once.Tick(t);
            for (int i = 0; i < 3; i++) Equal(expected, repeated.Tick(t), "repeat tick " + i + " at t=" + t);
        }

        HeroArcherAnimationState explicitState = NewState();
        explicitState.SetMotion(HeroArcherAnimation.Idle, 0.0);
        Equal(0, explicitState.Tick(0.0), "idle first frame");
        Equal(3, explicitState.Tick(0.5), "idle frame before draw");
        explicitState.BeginDraw(1.0);
        Equal(12, explicitState.Tick(1.0), "draw first frame");
        Equal(13, explicitState.Tick(1.125), "draw held last frame");
        Equal(13, explicitState.Tick(3.0), "draw still held");
    }

    private static void FrameSequenceIsIndependentOfSamplingRate()
    {
        HeroArcherAnimationState fine = NewState();
        HeroArcherAnimationState coarse = NewState();

        for (int i = 0; i <= 100; i++)
        {
            double t = i / 60.0;
            if (i == 0)
            {
                fine.SetMotion(HeroArcherAnimation.Walk, t);
                coarse.SetMotion(HeroArcherAnimation.Walk, t);
            }
            if (i == 30)
            {
                fine.BeginDraw(t);
                coarse.BeginDraw(t);
            }
            if (i == 60)
            {
                fine.NotifyRelease(t);
                coarse.NotifyRelease(t);
            }
            if (i == 61)
            {
                fine.EndDraw(t);
                coarse.EndDraw(t);
            }

            int fineFrame = fine.Tick(t);
            if (i % 6 == 0) Equal(fineFrame, coarse.Tick(t), "10Hz sampling agrees with 60Hz sampling at t=" + t);
        }
    }

    private static void InvalidTimeSamplesKeepLastFrameAndChangeNothing()
    {
        HeroArcherAnimationState state = NewState();
        state.SetMotion(HeroArcherAnimation.Walk, 0.0);
        int reference = state.Tick(0.5);
        Equal(7, reference, "walk frame at t=0.5");

        Equal(reference, state.Tick(double.NaN), "NaN keeps the last frame");
        Equal(reference, state.Tick(double.PositiveInfinity), "+Infinity keeps the last frame");
        Equal(reference, state.Tick(double.NegativeInfinity), "-Infinity keeps the last frame");
        Equal(reference, state.Tick(-1.0), "negative time keeps the last frame");

        state.BeginDraw(double.NaN);
        state.NotifyRelease(double.NaN);
        state.EndDraw(double.NaN);
        state.SetMotion(HeroArcherAnimation.Run, double.NaN);
        Equal(reference, state.Tick(0.5), "events with invalid time change nothing");

        state.BeginDraw(0.6);
        Equal(12, state.Tick(0.6), "machine still usable after invalid samples");
    }

    private static void BackwardTimeIsRejectedWithoutCorruption()
    {
        HeroArcherAnimationState state = NewState();
        state.SetMotion(HeroArcherAnimation.Walk, 0.0);
        int atFive = state.Tick(5.0);

        Equal(atFive, state.Tick(1.0), "backward sample keeps the frame from the last valid time");
        state.BeginDraw(4.0);
        state.NotifyRelease(4.0);
        Equal(atFive, state.Tick(5.0), "events with backward time are rejected");

        Equal(7, state.Tick(5.0 + 1.0 / 6.0), "clock continues normally after rejected samples");
    }

    private static void DisabledYieldsIdleFirstFrameAndIgnoresEvents()
    {
        HeroArcherAnimationState state = NewState();
        state.SetMotion(HeroArcherAnimation.Walk, 0.0);
        state.BeginDraw(1.0);
        state.Enabled = false;

        Equal(0, state.Tick(1.0), "disabled state shows idle first frame");
        Equal(0, state.Tick(50.0), "disabled state stays on idle first frame");

        state.BeginDraw(2.0);
        state.NotifyRelease(3.0);
        state.SetMotion(HeroArcherAnimation.Run, 4.0);
        Equal(0, state.Tick(4.0), "disabled state ignores events");

        state.Enabled = true;
        Check(state.Enabled, "enabled flag round-trips");
        Equal(0, state.Tick(10.0), "re-enabled state restarts on idle first frame (draw/shot were cleared)");
        state.SetMotion(HeroArcherAnimation.Walk, 10.5);
        Equal(4, state.Tick(10.5), "re-enabled state accepts new events");
    }

    private static void ResetClearsActionAndAcceptsLowerClock()
    {
        HeroArcherAnimationState state = NewState();
        state.SetMotion(HeroArcherAnimation.Run, 90.0);
        state.BeginDraw(91.0);
        state.NotifyRelease(91.5);
        state.Tick(92.0);
        state.Reset();

        Equal(0, state.Tick(1.0), "reset clears the action and accepts a lower clock");
        state.SetMotion(HeroArcherAnimation.Walk, 1.0);
        Equal(4, state.Tick(1.0), "post-reset motion starts at its first frame");

        HeroArcherAnimationState cleared = NewState();
        cleared.SetMotion(HeroArcherAnimation.Idle, 0.0);
        cleared.BeginDraw(1.0);
        cleared.NotifyRelease(1.5);
        cleared.Reset();
        Equal(0, cleared.Tick(2.0), "reset clears draw/release (no 13/14/15 residue)");
        ExpectAllFrames(cleared.Tick(9.0), "post-reset frame stays inside the atlas");
    }

    private static void HugeElapsedTimeStaysBoundedInClipRange()
    {
        HeroArcherAnimationState state = NewState();
        state.SetMotion(HeroArcherAnimation.Walk, 0.0);
        int huge = state.Tick(1e9);
        InRange(huge, HeroArcherAtlas.WalkFirstFrame, HeroArcherAtlas.WalkFirstFrame + HeroArcherAtlas.WalkFrames - 1,
            "huge elapsed time still lands on a walk frame");

        double clamped = HeroArcherAtlas.MaxClipPhaseSeconds;
        int expected = HeroArcherAtlas.WalkFirstFrame
            + (int)((long)(clamped * HeroArcherAtlas.WalkFps) % HeroArcherAtlas.WalkFrames);
        Equal(expected, huge, "huge elapsed time equals the clamped-phase frame");

        HeroArcherAnimationState drawState = NewState();
        drawState.SetMotion(HeroArcherAnimation.Idle, 0.0);
        drawState.BeginDraw(1.0);
        HeroArcherAtlas.TryGetClip(HeroArcherAnimation.Draw, out HeroArcherClip drawClip);
        Equal(HeroArcherAtlas.FrameAt(drawClip, HeroArcherAtlas.MaxClipPhaseSeconds),
            drawState.Tick(1.0 + HeroArcherAtlas.MaxClipPhaseSeconds),
            "huge draw phase stays bounded on the held draw frame (no numeric overflow)");
    }

    private static void MonotonicClockRejectsBackwardEvents()
    {
        // 评审给的回归例：Tick(0); BeginDraw(10); EndDraw(9); Tick(10.2) —— EndDraw 早于单调时钟，
        // 必须被拒（拉弓仍然开着 → 10.2 时是拉弓末帧 13），旧实现里时钟只在 Tick 里推进，会误接受 EndDraw(9)。
        HeroArcherAnimationState state = NewState();
        Equal(0, state.Tick(0.0), "idle before the draw event");
        state.BeginDraw(10.0);
        state.EndDraw(9.0);
        Equal(13, state.Tick(10.2), "backward EndDraw did not close the open draw");

        // 其它公开时间入口同样以单调时钟为准：BeginDraw 已经把时钟推到 10，早于它的 SetMotion/NotifyRelease 被拒。
        state.SetMotion(HeroArcherAnimation.Walk, 5.0);
        state.NotifyRelease(9.0);
        Equal(13, state.Tick(10.3), "backward SetMotion/NotifyRelease are rejected (draw still held)");

        // 时钟只增不减：合法事件把时钟继续推高，且事件本身也推进已过期的窗口。
        state.NotifyRelease(11.0);
        Equal(14, state.Tick(11.0), "forward release at the clock time is accepted");
        Equal(15, state.Tick(11.125), "release window rolls into recovery");
        state.EndDraw(11.4);
        ExpectMotionFrame(HeroArcherAnimation.Idle, 11.4 - 11.375, state.Tick(11.4),
            "a forward EndDraw after the volley closes the draw (motion anchored at the recovery end)");
    }

    private static void ContinuousVolleysBeyondEightSecondsKeepDrawHold()
    {
        // 原生协程在齐射里可以长时间保持 Prepare=true（多次尝试/编队间隔/射速缩放），因此不允许
        // 任何「8 秒看门狗」把拉弓末帧强制打回移动帧：只有显式 EndDraw（Prepare=false）才结束拉弓。
        HeroArcherAnimationState state = NewState();
        state.SetMotion(HeroArcherAnimation.Idle, 0.0);
        state.BeginDraw(1.0);
        Equal(12, state.Tick(1.0), "draw first frame");

        double t = 1.0;
        for (int shot = 0; shot < 24; shot++)
        {
            t += 0.5; // 每 0.5 秒一箭，持续 12 秒（远超旧的 8 秒上限）
            state.NotifyRelease(t);
            Equal(14, state.Tick(t), "release frame of shot " + shot + " at t=" + t);
            Equal(14, state.Tick(t + 0.124), "release window of shot " + shot + " holds 14 just before its boundary");
            Equal(15, state.Tick(t + 0.125), "recovery frame of shot " + shot);
            Equal(13, state.Tick(t + 0.375), "draw hold resumes after shot " + shot);
        }

        Equal(13, state.Tick(t + 20.0), "draw hold survives a long uninterrupted volley (no forced idle watchdog)");
        Equal(13, state.Tick(t + 3600.0), "draw hold still bounded and held after an hour of open Prepare");
        ExpectAllFrames(state.Tick(t + 3600.5), "long-open draw frame stays inside the atlas");

        double closeTime = t + 3601.0;
        state.EndDraw(closeTime);
        InRange(state.Tick(closeTime), 0, 3, "explicit EndDraw after a long volley returns to the idle clip");
    }

    // ---------------------------------------------------------------- 随机不变式

    private static void RandomizedSequencesStayInAtlasBounds()
    {
        var random = new Random(20260914);
        for (int sequence = 0; sequence < 40; sequence++)
        {
            HeroArcherAnimationState state = NewState();
            HeroArcherAnimation motion = HeroArcherAnimation.Idle;
            bool drawOpen = false;
            bool shotOpen = false;
            double shotStart = 0.0;
            double t = 0.0;

            for (int step = 0; step < 120; step++)
            {
                t += 0.01 + random.NextDouble() * 0.4;
                switch (random.Next(10))
                {
                    case 0:
                    case 1:
                        motion = (HeroArcherAnimation)random.Next(3);
                        state.SetMotion(motion, t);
                        break;
                    case 2:
                    case 3:
                        drawOpen = true;
                        state.BeginDraw(t);
                        break;
                    case 4:
                        drawOpen = false;
                        state.EndDraw(t);
                        break;
                    case 5:
                    case 6:
                        shotOpen = true;
                        shotStart = t;
                        state.NotifyRelease(t);
                        break;
                    case 7:
                        state.Tick(double.NaN);
                        break;
                    case 8:
                        state.SetMotion((HeroArcherAnimation)(random.Next(3) + 3), t); // 动作枚举不是合法 motion
                        break;
                }

                int frame = state.Tick(t);
                ExpectAllFrames(frame, "random frame at seq=" + sequence + " step=" + step);
                Equal(frame, state.Tick(t), "random frame is stable when re-ticked at the same time");

                if (shotOpen && t >= shotStart + HeroArcherAtlas.ReleaseSeconds + HeroArcherAtlas.RecoverySeconds)
                    shotOpen = false;

                HeroArcherAtlas.TryGetClip(motion, out HeroArcherClip motionClip);
                if (!shotOpen && !drawOpen)
                    InRange(frame, motionClip.FirstFrame, motionClip.LastFrame,
                        "no open action means the frame belongs to motion " + motion + " (seq=" + sequence + " step=" + step + ")");
                else if (!shotOpen)
                    InRange(frame, HeroArcherAtlas.DrawFirstFrame, HeroArcherAtlas.DrawFirstFrame + HeroArcherAtlas.DrawFrames - 1,
                        "open draw shows a draw frame (seq=" + sequence + " step=" + step + ")");
            }
        }
    }
}
