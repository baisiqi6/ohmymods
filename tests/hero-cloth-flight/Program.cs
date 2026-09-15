// 英雄弓手红飘带：动态验收测试（operator 2026-09-15 目标）。
// 断言全部落在可观察坐标上（世界/链 local 的节点位置、离地间隙、段长、时间），不镜像实现细节。
//
// 运行： C:/Users/ADMIN/dotnet8/dotnet.exe run --project tests/DynamicsSuite/DynamicsSuite.csproj
// 期望：全部 PASS、退出码 0；失败打印 expected/actual 并返回 1。
// 同输入新旧预览独立存于 artifacts/hero-archer/20260915-cloth-flight；本项目只做行为断言。

using System;

namespace KingdomEnhancedMod.Dynamics;

internal static class Program
{
    private const float Tick = 1f / 60f;
    private const float Pixels = 32f;
    private const float PrimaryLength = 24f / 32f;      // 24px
    private const float SecondaryLength = 22f / 32f;    // 22px
    private const float PrimaryGround = -14f / 32f;     // 肩部离地 14px
    private const float SecondaryGround = -17f / 32f;   // 当前副链锚点高3px，与实际双尾围巾一致

    // operator 合成速度：0.65 走、1.2/1.6/2.0 跑
    private const float WalkSpeed = 0.65f;
    private const float RunSlow = 1.2f;
    private const float RunSpeed = 1.6f;
    private const float RunFast = 2f;

    /// <summary>跑动时要求：尾部相对肩部的下降 <= 8px、横向延伸 >= 长度 80%、离地间隙 > 0 且明显（>= 2px）。</summary>
    private const float MaxRunDropPixels = 8f;
    private const float MinRunExtensionRatio = 0.8f;
    private const float MinRunClearancePixels = 2f;
    /// <summary>单步（1/30s）节点位移上限：起跑/停下都不许瞬跳。</summary>
    private const float MaxStepPixels = 2f;

    private static int _failed;
    private static int _passed;
    private static float _clock;

    private static int Main()
    {
        IdleHangsAndFoldsOnGround();
        IdleRibbonAliveAndNeverFloating();
        IdleWindNeverFlipsTailForward();
        RunLiftsTailClearOffGround();
        RunReachesExtensionAndDropTargets();
        RunTailLiftsMonotonicallyWithSpeed();
        RunNeverLiftsAboveShoulder();
        RunWindCannotBlowTailInFront();
        WindShapesLessWhileRunningButStillShapes();
        StartRampIsGradualNoJump();
        StopKeepsInertiaThenSettlesBack();
        TurnRebasePreservesWorldShapeAndKeepsTrailing();
        BackpedalMirrorsTrailAndStaysBounded();
        SamplingRateIndependence();
        InvalidInputsNeverCorrupt();
        TeleportAndResetFallBackToRest();
        SegmentLengthAndGroundAlwaysHold();
        LongMixedRunStaysBounded();

        Console.WriteLine();
        Console.WriteLine(_failed == 0 ? "ALL TESTS PASSED (" + _passed + ")" : _failed + " TEST(S) FAILED, " + _passed + " passed");


        return _failed == 0 ? 0 : 1;
    }

    // ============================================================
    // 静止：自然下垂/折地、细波、风翻不到身前
    // ============================================================

    private static void IdleHangsAndFoldsOnGround()
    {
        foreach (IChain chain in new[] { Primary(), Secondary() })
        {
            string tag = chain.SegmentLength > 0.1f ? "primary" : "secondary";
            IChain rest = chain.SegmentLength > 0.1f ? Primary() : Secondary();
            float restTipX = -rest.PointsX[rest.NodeCount - 1] * Pixels;
            Run(chain, 8f, 0f, 0f);
            bool aboveGround = true;
            float highestNode = float.MinValue;
            float tipClearanceSum = 0f;
            for (int n = 0; n < chain.NodeCount; n++)
            {
                aboveGround &= chain.PointsY[n] >= FloorAt(chain, n) - 1e-3f;
                highestNode = Math.Max(highestNode, chain.PointsY[n] * Pixels);
            }
            float tipDrop = -chain.PointsY[chain.NodeCount - 1] * Pixels;
            float tipX = -chain.PointsX[chain.NodeCount - 1] * Pixels;
            tipClearanceSum = (chain.PointsY[chain.NodeCount - 1] - chain.FloorY) * Pixels;
            Check("idle.hangs-and-folds." + tag,
                aboveGround && highestNode <= 0.5f && tipClearanceSum <= 3f && tipX >= restTipX - 1f,
                "tip=(" + tipX + "," + tipDrop + ")px 离地=" + tipClearanceSum + "px 最高节点=" + highestNode +
                "px 期望 不穿地/不高于肩/尾部贴地折在身后（静止基准 " + restTipX + "px）");
        }
    }

    private static void IdleRibbonAliveAndNeverFloating()
    {
        IChain chain = Primary();
        float maxClearance = float.MinValue;
        float clearanceSum = 0f;
        int samples = 0;
        float tipMinX = float.MaxValue, tipMaxX = float.MinValue;
        Run(chain, 6f, 0f, 0f, _ =>
        {
            float clearance = (chain.PointsY[chain.NodeCount - 1] - chain.FloorY) * Pixels;
            maxClearance = Math.Max(maxClearance, clearance);
            clearanceSum += clearance;
            samples++;
            float x = HeroArcherClothMath.Quantize(chain.PointsX[chain.NodeCount - 1]);
            tipMinX = Math.Min(tipMinX, x);
            tipMaxX = Math.Max(tipMaxX, x);
        });
        float meanClearance = clearanceSum / samples;
        Note("metric.idle-clearance", "尾部离地 均值=" + meanClearance + "px 峰值=" + maxClearance + "px（肩高 " +
            HeroArcherClothMath.ShoulderHeightPixels + "px / 链长 " + chain.LengthPixels() + "px）");
        Check("idle.never-floating", meanClearance <= 3f && maxClearance <= 6f,
            "静止尾部离地 均值=" + meanClearance + "px 峰值=" + maxClearance + "px 期望 均值 <= 3px、峰值 <= 6px（贴地自然下垂，不是悬空）");
        Check("idle.ribbon-alive", tipMaxX - tipMinX >= 1f / Pixels,
            "静止尾部量化摆动=" + (tipMaxX - tipMinX) + "u 期望 >= 1px（细波，非冻结）");
    }

    private static void IdleWindNeverFlipsTailForward()
    {
        foreach (float wind in new[] { -1f, 1f })
        {
            IChain chain = Primary();
            chain.SetWind(wind);
            float worstTipX = float.MinValue, maxClearance = float.MinValue;
            float clearanceSum = 0f;
            int samples = 0;
            Run(chain, 12f, 0f, wind, _ =>
            {
                worstTipX = Math.Max(worstTipX, chain.PointsX[chain.NodeCount - 1]);
                float clearance = (chain.PointsY[chain.NodeCount - 1] - chain.FloorY) * Pixels;
                maxClearance = Math.Max(maxClearance, clearance);
                clearanceSum += clearance;
                samples++;
            });
            float meanClearance = clearanceSum / samples;
            Check("idle.wind-never-front." + (int)wind,
                worstTipX <= 0.02f && meanClearance <= 3f && maxClearance <= 6f,
                "wind=" + wind + " 尾部最前 x=" + worstTipX + "u 离地 均值=" + meanClearance + "px 峰值=" + maxClearance +
                "px 期望 永不进入身前且不悬空");
        }
    }

    // ============================================================
    // 跑动：离地、横向延伸、相对肩部下降
    // ============================================================

    private static void RunLiftsTailClearOffGround()
    {
        foreach (float speed in new[] { RunSlow, RunSpeed, RunFast })
        {
            foreach (IChain chain in new[] { Primary(), Secondary() })
            {
                string tag = (chain.SegmentLength > 0.1f ? "primary" : "secondary") + "@" + speed;
                float minClearance = float.MaxValue;
                Run(chain, 6f, speed, 0f, t =>
                {
                    if (t < 2.5f) return; // 2.5s 后的稳定窗口
                    minClearance = Math.Min(minClearance, (chain.PointsY[chain.NodeCount - 1] - chain.FloorY) * Pixels);
                });
                Check("run.lifts-off-ground." + tag, minClearance >= MinRunClearancePixels,
                    "跑 " + speed + "u/s 稳定后最小离地=" + minClearance + "px 期望 >= " + MinRunClearancePixels + "px");
            }
        }
    }

    private static void RunReachesExtensionAndDropTargets()
    {
        foreach (IChain chain in new[] { Primary(), Secondary() })
        {
            string tag = chain.SegmentLength > 0.1f ? "primary" : "secondary";
            float lengthPixels = chain.LengthPixels();
            float minExtension = float.MaxValue;
            float dropSum = 0f;
            int samples = 0;
            float dropWorst = float.MinValue;
            Run(chain, 6f, RunSpeed, 0f, t =>
            {
                if (t < 2.5f) return;
                float extension = -chain.PointsX[chain.NodeCount - 1] * Pixels;
                float drop = -chain.PointsY[chain.NodeCount - 1] * Pixels;
                minExtension = Math.Min(minExtension, extension);
                dropWorst = Math.Max(dropWorst, drop);
                dropSum += drop;
                samples++;
            });
            float meanDrop = dropSum / samples;
            Check("run1.6.extension." + tag, minExtension >= lengthPixels * MinRunExtensionRatio,
                "横向延伸最小=" + minExtension + "px / 长度 " + lengthPixels + "px = " +
                (minExtension / lengthPixels * 100f) + "% 期望 >= " + (MinRunExtensionRatio * 100f) + "%");
            Check("run1.6.drop." + tag, meanDrop <= MaxRunDropPixels && dropWorst <= MaxRunDropPixels + 2f,
                "尾部相对肩部下降 均值=" + meanDrop + "px 峰值=" + dropWorst + "px 期望 均值 <= " + MaxRunDropPixels + "px");
        }
    }

    private static void RunTailLiftsMonotonicallyWithSpeed()
    {
        float idleExt = SteadyTipExtension(0f, 6f);
        float walkExt = SteadyTipExtension(WalkSpeed, 6f);
        float runSlowExt = SteadyTipExtension(RunSlow, 6f);
        float runExt = SteadyTipExtension(RunSpeed, 6f);
        float runFastExt = SteadyTipExtension(RunFast, 6f);
        float idle = SteadyTipDrop(0f, 6f);
        float walk = SteadyTipDrop(WalkSpeed, 6f);
        float runSlow = SteadyTipDrop(RunSlow, 6f);
        float run = SteadyTipDrop(RunSpeed, 6f);
        float runFast = SteadyTipDrop(RunFast, 6f);
        Note("metric.speed-ordering",
            "drop idle=" + idle + " walk=" + walk + " run1.2=" + runSlow + " run1.6=" + run + " run2.0=" + runFast +
            " px | ext idle=" + idleExt + " walk=" + walkExt + " run1.2=" + runSlowExt + " run1.6=" + runExt + " run2.0=" + runFastExt + " px");
        Check("run.tail-lifts-monotonically",
            walkExt > idleExt + 2f && runSlowExt > walkExt + 2f && runExt >= runSlowExt - 0.5f && runFastExt >= runExt - 0.5f
            && walk > runSlow + 1f && runSlow > run + 0.5f && run >= runFast - 1f,
            "期望 横向延伸随速度单调增大（idle<walk<run），且下降随速度单调减小：ext " +
            idleExt + "/" + walkExt + "/" + runSlowExt + "/" + runExt + "/" + runFastExt +
            " drop " + idle + "/" + walk + "/" + runSlow + "/" + run + "/" + runFast);
    }

    private static void RunNeverLiftsAboveShoulder()
    {
        foreach (float speed in new[] { RunSlow, RunSpeed, RunFast })
        {
            IChain chain = Primary();
            float highest = float.MinValue;
            Run(chain, 6f, speed, 0f, _ =>
            {
                for (int n = 0; n < chain.NodeCount; n++) highest = Math.Max(highest, chain.PointsY[n] * Pixels);
            });
            Check("run.never-above-shoulder." + speed, highest <= 0.5f,
                "跑 " + speed + " 全程最高节点=" + highest + "px（>0 即在肩部之上）期望 <= 0.5px：展开由气流托起，不是甩过肩");
        }
    }

    private static void RunWindCannotBlowTailInFront()
    {
        foreach (float wind in new[] { -1f, 1f })
        {
            foreach (IChain chain in new[] { Primary(), Secondary() })
            {
                string tag = (chain.SegmentLength > 0.1f ? "primary" : "secondary") + "." + (int)wind;
                float worstX = float.MinValue;
                float minExtension = float.MaxValue;
                chain.SetWind(wind);
                Run(chain, 6f, RunSpeed, float.NaN, t =>
                {
                    if (t < 2.5f) return;
                    worstX = Math.Max(worstX, chain.PointsX[chain.NodeCount - 1]);
                    minExtension = Math.Min(minExtension, -chain.PointsX[chain.NodeCount - 1]);
                });
                Check("run.wind-cannot-blow-front." + tag,
                    worstX <= -chain.LengthPixels() * MinRunExtensionRatio / Pixels,
                    "跑 " + RunSpeed + " + 强风 " + wind + " 尾部最前 x=" + (worstX * Pixels) + "px 最小延伸=" +
                    (minExtension * Pixels) + "px 期望 仍充分拖在身后");
            }
        }
    }

    private static void WindShapesLessWhileRunningButStillShapes()
    {
        float restBend = WindBendDifference(0f);
        float runBend = WindBendDifference(RunSpeed);
        Note("metric.wind-bend", "静止 ±1 风的平均弯曲差=" + restBend + "u  跑动 ±1 风的平均弯曲差=" + runBend + "u");
        Check("wind.modulates-less-at-speed", runBend < restBend * 0.6f && runBend > 0f,
            "期望 跑动时风只轻微调形（< 静止的 60%）但仍非零：rest=" + restBend + " run=" + runBend);
    }

    // ============================================================
    // 过渡：起跑不瞬跳、停下保留惯性后落回
    // ============================================================

    private static void StartRampIsGradualNoJump()
    {
        IChain chain = Primary();
        IChain rest = Primary();
        float restDrop = -rest.PointsY[rest.NodeCount - 1] * Pixels;

        float[] previous = Snapshot(chain);
        float worstStep = 0f;
        float targetTime = float.NaN;
        Run(chain, 3f, RunSpeed, 0f, t =>
        {
            float step = MaxDelta(previous, Snapshot(chain));
            previous = Snapshot(chain);
            worstStep = Math.Max(worstStep, step * Pixels);
            if (float.IsNaN(targetTime) && -chain.PointsY[chain.NodeCount - 1] * Pixels <= MaxRunDropPixels) targetTime = t;
        });
        Note("metric.start", "起跑单步最大位移=" + worstStep + "px 达到 drop<=8px=" + targetTime + "s（静止下降 " + restDrop + "px）");

        Check("start.no-instant-jump", worstStep <= MaxStepPixels,
            "起跑后单步最大位移=" + worstStep + "px（期望 <= " + MaxStepPixels + "px/步，即 <= " +
            (MaxStepPixels * 30f) + "px/s：展开是连续过程，不是一帧跳到最终形）");
        Check("start.ramps-within-window", targetTime >= 0.4f && targetTime <= 2.5f,
            "达到 drop<=8px 用时=" + targetTime + "s（期望 0.4-2.5s：既非瞬跳，也在 operator 验收窗口 2-4s 内）");
    }

    private static void StopKeepsInertiaThenSettlesBack()
    {
        IChain chain = Primary();
        Run(chain, 4f, RunSpeed, 0f);
        float runningDrop = -chain.PointsY[chain.NodeCount - 1] * Pixels;
        float fallTime = float.NaN, settledTime = float.NaN, groundedTime = float.NaN;
        float[] previous = Snapshot(chain);
        float worstStep = 0f;
        Run(chain, 5f, 0f, 0f, t =>
        {
            float step = MaxDelta(previous, Snapshot(chain));
            previous = Snapshot(chain);
            worstStep = Math.Max(worstStep, step * Pixels);
            float drop = -chain.PointsY[chain.NodeCount - 1] * Pixels;
            float clearance = (chain.PointsY[chain.NodeCount - 1] - chain.FloorY) * Pixels;
            if (float.IsNaN(fallTime) && drop >= runningDrop + 3f) fallTime = t;
            if (float.IsNaN(groundedTime) && clearance <= 1f) groundedTime = t;
            if (float.IsNaN(settledTime) && clearance <= 1f && drop >= 11f) settledTime = t;
        });
        Note("metric.stop", "跑动下降=" + runningDrop + "px 明显回落=" + fallTime + "s 恢复贴地=" + groundedTime + "s 回到静止下降=" + settledTime + "s");
        Check("stop.inertia-then-settle",
            fallTime >= 0.3f && fallTime <= 2.5f && settledTime <= 3.5f && worstStep <= MaxStepPixels,
            "期望 停下后先保留惯性（>=0.3s 才明显回落，实测 " + fallTime + "s）、3.5s 内落回贴地静止（实测 " + settledTime +
            "s）、单步位移 <= " + MaxStepPixels + "px（实测 " + worstStep + "px）");
    }

    // ============================================================
    // 转向 / 后退：Rebase 连续性、镜像拖尾
    // ============================================================

    private static void TurnRebasePreservesWorldShapeAndKeepsTrailing()
    {
        IChain chain = Primary();
        Run(chain, 3f, RunSpeed, 0f);
        float[] before = Snapshot(chain);
        // 世界坐标 = 肩锚点(±5/32) ∓ 链 local x：Rebase(-1, +10/32) 后必须逐节点保持世界形状
        chain.Rebase(-1f, 10f / 32f);
        bool continuous = true;
        float worst = 0f;
        for (int n = 1; n < chain.NodeCount; n++)
        {
            float worldBefore = before[n * 2] - 5f / 32f;
            float worldAfter = 5f / 32f - chain.PointsX[n];
            worst = Math.Max(worst, Math.Abs(worldBefore - worldAfter));
            continuous &= Math.Abs(worldBefore - worldAfter) < 1e-5f;
        }
        Check("turn.rebase-continuous", continuous, "worst world shape error=" + worst + " 期望 < 1e-5（转向是重基，不是瞬间镜像）");

        // 转向后继续朝原世界方向跑：local 速度 = -1.6 → 尾部必须重新充分拖在 local +x（= 世界身后）
        float localVelocity = HeroArcherClothMath.LocalVelocityXFromWorld(RunSpeed, -1f);
        float trailSign = Math.Sign(-localVelocity);
        float leastExtension = float.MaxValue;
        Run(chain, 4f, localVelocity, 0f, t =>
        {
            if (t < 2f) return;
            leastExtension = Math.Min(leastExtension, chain.PointsX[chain.NodeCount - 1] * trailSign * Pixels);
        });
        Check("turn.trails-again-after-rebase", leastExtension >= chain.LengthPixels() * MinRunExtensionRatio,
            "转向后跑动尾部最小延伸=" + leastExtension + "px 期望 >= " +
            (chain.LengthPixels() * MinRunExtensionRatio) + "px（世界身后）");
    }

    private static void BackpedalMirrorsTrailAndStaysBounded()
    {
        IChain chain = Primary();
        Run(chain, 4f, -RunSpeed, 0f);
        float tipX = chain.PointsX[chain.NodeCount - 1] * Pixels;
        float drop = -chain.PointsY[chain.NodeCount - 1] * Pixels;
        Check("backpedal.mirrors-trail", tipX >= chain.LengthPixels() * 0.5f && drop <= MaxRunDropPixels + 2f
            && chain.PointsY[chain.NodeCount - 1] >= chain.FloorY - 1e-3f,
            "后退时尾部 tip=(" + tipX + "," + drop + ")px 期望 镜像到 +x、仍离地且不穿地（拖在运动反方向）");
    }

    // ============================================================
    // 数值：采样率一致、非法输入、瞬移/Reset、段长与地面
    // ============================================================

    private static void SamplingRateIndependence()
    {
        float[] reference = null;
        foreach (float rate in new[] { 60f, 30f, 10f, 144f })
        {
            IChain chain = Primary();
            float dt = 1f / rate;
            int ticks = (int)Math.Round(3f * rate);
            // 固定风钟锚点（与调用频率无关）：验证固定步账目，而不是把首帧锚点差算成不一致
            for (int i = 0; i < ticks; i++) chain.Step(dt, RunSpeed, 1.5f);
            float[] pose = Snapshot(chain);
            if (reference == null) { reference = pose; continue; }
            float worst = 0f;
            for (int i = 0; i < pose.Length; i++) worst = Math.Max(worst, Math.Abs(pose[i] - reference[i]));
            Check("sampling-rate-independent." + (int)rate + "Hz", worst < 1e-4f,
                "worst=" + worst + "（同速度同经过时间必须同姿态，固定 30Hz 内部步）");
        }
    }

    private static void InvalidInputsNeverCorrupt()
    {
        IChain chain = Primary();
        Run(chain, 2f, RunSpeed, 0f);
        float[] snapshot = Snapshot(chain);
        chain.Step(float.NaN, RunSpeed, 5f);
        chain.Step(float.PositiveInfinity, RunSpeed, 5f);
        chain.Step(-1f, RunSpeed, 5f);
        chain.Step(0f, RunSpeed, 5f);
        Check("invalid.dt-ignored", Identical(snapshot, Snapshot(chain)), "NaN/Inf/<=0 dt 必须完全不改变状态");

        chain.Step(Tick, float.NaN, float.NaN);
        chain.Step(Tick, float.PositiveInfinity, float.NaN);
        chain.Step(Tick, float.NegativeInfinity, float.PositiveInfinity);
        for (int i = 0; i < 240; i++) { _clock += Tick; chain.Step(Tick, float.NaN, float.NaN); }
        chain.SetWind(float.NaN);
        chain.SetWind(float.PositiveInfinity);
        Check("invalid.nan-inputs-safe", AllFinite(chain) && AboveGround(chain) && chain.PointsY[chain.NodeCount - 1] >= chain.FloorY - 1e-3f,
            "NaN/Inf 速度与风钟不得留下 NaN/穿地/爆坐标");

        IChain winded = Primary();
        winded.SetWind(float.NaN);
        winded.SetWind(float.PositiveInfinity);
        winded.SetWind(1f);
        IChain unit = Primary();
        unit.SetWind(1f);
        for (int i = 0; i < 240; i++)
        {
            _clock += Tick;
            winded.Step(Tick, 0f, _clock);
            unit.Step(Tick, 0f, _clock);
        }
        Check("invalid.wind-nan-ignored", MaxDifference(winded, unit) == 0f,
            "NaN/Inf 风输入必须被忽略（等价于最后一次合法输入）");
    }

    private static void TeleportAndResetFallBackToRest()
    {
        IChain chain = Primary();
        Run(chain, 3f, RunSpeed, 0f);
        chain.Step(1f / 30f, 200f, _clock += 1f / 30f);
        Check("teleport.positive-spike-restores", MaxDifference(chain, Primary()) == 0f,
            "200u/s 瞬移尖峰必须回落到静止姿态（不是继续展开）");
        Run(chain, 2f, RunSpeed, 0f);
        chain.Step(1f / 30f, -500f, _clock += 1f / 30f);
        Check("teleport.negative-spike-restores", MaxDifference(chain, Primary()) == 0f, "-500u/s 同样回落");

        IChain reused = Primary();
        Run(reused, 5f, RunSpeed, 0f);
        reused.Reset();
        Check("reset.returns-to-rest-pose", MaxDifference(reused, Primary()) == 0f, "Reset 必须逐节点等于新建链的静止姿态");

        IChain fresh = Primary();
        for (int i = 0; i < 180; i++)
        {
            _clock += Tick;
            reused.Step(Tick, WalkSpeed, _clock);
            fresh.Step(Tick, WalkSpeed, _clock);
        }
        Check("reset.pool-reuse-matches-fresh", MaxDifference(reused, fresh) == 0f,
            "池复用（Reset 后重跑）必须与新建链逐位一致（无残留尾迹/展开包络）");
    }

    private static void SegmentLengthAndGroundAlwaysHold()
    {
        IChain primary = Primary();
        IChain secondary = Secondary();
        float worstError = 0f;
        bool aboveGround = true;
        bool bounded = true;
        for (int i = 0; i < 3600; i++) // 60s：静止→走→跑 1.2→1.6→2.0→后退→停→瞬移尖峰
        {
            float phase = i % 1200;
            float velocity = phase < 180 ? 0f : phase < 360 ? WalkSpeed : phase < 540 ? RunSlow : phase < 720 ? RunSpeed
                : phase < 900 ? RunFast : phase < 1080 ? -RunSpeed : 0f;
            if (i % 900 == 899) velocity = 300f;
            _clock += Tick;
            primary.Step(Tick, velocity, _clock);
            secondary.Step(Tick, velocity, _clock);
            foreach (IChain chain in new[] { primary, secondary })
            {
                float segment = chain.SegmentLength;
                for (int n = 1; n < chain.NodeCount; n++)
                {
                    float dx = chain.PointsX[n] - chain.PointsX[n - 1];
                    float dy = chain.PointsY[n] - chain.PointsY[n - 1];
                    worstError = Math.Max(worstError, Math.Abs((float)Math.Sqrt(dx * dx + dy * dy) - segment));
                }
                aboveGround &= AboveGround(chain);
                bounded &= AllFinite(chain) && WithinBounds(chain);
            }
        }
        Check("invariant.segment-length", worstError < 1e-3f, "worst segment error=" + worstError + " 期望 < 1e-3（含地面折叠段）");
        Check("invariant.never-below-ground", aboveGround, "60s 混合序列任何采样点都不得穿地");
        Check("invariant.bounded-and-finite", bounded, "60s 混合序列（含瞬移尖峰）必须恒有限且在坐标上限内");
    }

    private static void LongMixedRunStaysBounded()
    {
        IChain chain = Primary();
        float worstSegmentError = 0f;
        float highest = float.MinValue;
        bool ok = true;
        int ticks = (int)Math.Round(120f * 60f);
        for (int i = 0; i < ticks; i++)
        {
            float phase = i % 3600;
            float velocity = phase < 600 ? RunSpeed : phase < 1200 ? 0f : phase < 1800 ? (i % 60 < 30 ? RunFast : -RunSpeed) : 0f;
            _clock += Tick;
            chain.Step(Tick, velocity, _clock);
            for (int n = 1; n < chain.NodeCount; n++)
            {
                float dx = chain.PointsX[n] - chain.PointsX[n - 1];
                float dy = chain.PointsY[n] - chain.PointsY[n - 1];
                worstSegmentError = Math.Max(worstSegmentError, Math.Abs((float)Math.Sqrt(dx * dx + dy * dy) - chain.SegmentLength));
            }
            for (int n = 0; n < chain.NodeCount; n++) highest = Math.Max(highest, chain.PointsY[n] * Pixels);
            if (!AllFinite(chain) || !AboveGround(chain) || !WithinBounds(chain)) { ok = false; break; }
        }
        Check("long-run.stable", ok && worstSegmentError < 1e-3f && highest <= 0.5f,
            "120s 混合跑动：ok=" + ok + " worstSegmentError=" + worstSegmentError + " 最高节点=" + highest + "px");
    }

    // ============================================================
    // 工具
    // ============================================================

    internal static IChain Primary() => new NewChainView(new HeroArcherClothChain(PrimaryLength, 0f, 1f, 1f, PrimaryGround));

    internal static IChain Secondary() => new NewChainView(new HeroArcherClothChain(SecondaryLength, 1.1f, 0.9f, 1.15f, SecondaryGround));

    internal static float LengthPixels(this IChain chain) => chain.SegmentLength * (chain.NodeCount - 1) * Pixels;

    internal static float FloorAt(IChain chain, int node)
        => chain.FloorY + HeroArcherClothMath.HalfWidthUnits(node) - HeroArcherClothMath.HalfWidthUnits(chain.NodeCount - 1);

    internal static bool AboveGround(IChain chain)
    {
        for (int n = 0; n < chain.NodeCount; n++) if (chain.PointsY[n] < FloorAt(chain, n) - 1e-3f) return false;
        return true;
    }

    internal static bool AllFinite(IChain chain)
    {
        for (int n = 0; n < chain.NodeCount; n++)
        {
            if (!HeroArcherClothMath.IsFinite(chain.PointsX[n])) return false;
            if (!HeroArcherClothMath.IsFinite(chain.PointsY[n])) return false;
        }
        return true;
    }

    internal static bool WithinBounds(IChain chain)
    {
        const float max = 8f;
        for (int n = 0; n < chain.NodeCount; n++)
        {
            if (Math.Abs(chain.PointsX[n]) > max || Math.Abs(chain.PointsY[n]) > max) return false;
        }
        return true;
    }

    internal static float[] Snapshot(IChain chain)
    {
        float[] copy = new float[chain.NodeCount * 2];
        for (int n = 0; n < chain.NodeCount; n++)
        {
            copy[n * 2] = chain.PointsX[n];
            copy[n * 2 + 1] = chain.PointsY[n];
        }
        return copy;
    }

    internal static float MaxDelta(float[] a, float[] b)
    {
        float worst = 0f;
        for (int i = 0; i < a.Length; i++) worst = Math.Max(worst, Math.Abs(a[i] - b[i]));
        return worst;
    }

    internal static float MaxDifference(IChain a, IChain b)
    {
        float worst = 0f;
        for (int n = 0; n < a.NodeCount; n++)
        {
            worst = Math.Max(worst, Math.Abs(a.PointsX[n] - b.PointsX[n]));
            worst = Math.Max(worst, Math.Abs(a.PointsY[n] - b.PointsY[n]));
        }
        return worst;
    }

    private static bool Identical(float[] a, float[] b)
    {
        for (int i = 0; i < a.Length; i++)
        {
            if (float.IsNaN(a[i]) != float.IsNaN(b[i])) return false;
            if (a[i] != b[i]) return false;
        }
        return true;
    }

    /// <summary>以 60Hz 采样跑 seconds；wind 为 NaN 时按链自身 SetWind 设定值（不注入风钟）。</summary>
    private static void Run(IChain chain, float seconds, float velocity, float wind, Action<float> onTick = null)
    {
        bool useWind = !float.IsNaN(wind);
        int ticks = (int)Math.Round(seconds / Tick);
        for (int i = 0; i < ticks; i++)
        {
            _clock += Tick;
            if (useWind) chain.SetWind(wind);
            chain.Step(Tick, velocity, _clock);
            onTick?.Invoke((i + 1) * Tick);
        }
    }

    private static float SteadyTipDrop(float velocity, float seconds)
    {
        IChain chain = Primary();
        float sum = 0f;
        int samples = 0;
        Run(chain, seconds, velocity, 0f, t =>
        {
            if (t < seconds * 0.5f) return;
            sum += -chain.PointsY[chain.NodeCount - 1] * Pixels;
            samples++;
        });
        return sum / samples;
    }

    private static float SteadyTipExtension(float velocity, float seconds)
    {
        IChain chain = Primary();
        float sum = 0f;
        int samples = 0;
        Run(chain, seconds, velocity, 0f, t =>
        {
            if (t < seconds * 0.5f) return;
            sum += -chain.PointsX[chain.NodeCount - 1] * Pixels;
            samples++;
        });
        return sum / samples;
    }

    /// <summary>静止 / 跑动下，风 ±1 造成的平均横向弯曲差（u，带符号）：跑动时必须只剩「轻微调形」。</summary>
    private static float WindBendDifference(float velocity)
    {
        float positive = WindBend(velocity, 1f);
        float negative = WindBend(velocity, -1f);
        return positive - negative;
    }

    /// <summary>固定风钟锚点（0）→ 两条链相位完全一致，差异只来自风的符号；返回带符号的平均弯曲。</summary>
    private static float WindBend(float velocity, float wind)
    {
        IChain chain = Primary();
        chain.SetWind(wind);
        int ticks = (int)Math.Round(6f / Tick);
        for (int i = 0; i < ticks; i++) chain.Step(Tick, velocity, 0f);
        float total = 0f;
        for (int n = 1; n < chain.NodeCount; n++) total += chain.PointsX[n];
        return total / (chain.NodeCount - 1);
    }

    internal static void Check(string name, bool condition, string detail)
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

    private static void Note(string name, string detail) => Console.WriteLine("METRIC " + name + " :: " + detail);
}
