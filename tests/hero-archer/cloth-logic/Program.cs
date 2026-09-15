// 英雄弓手红飘带纯逻辑测试，链接当前生产布料核心。
// 运行：dotnet run --project tests/hero-archer/cloth-logic/Tests.csproj
// 2026-09-15调整两项速度表现断言：走/跑对照使用合成.65/1.6；
// 波纹在展开稳定后测量，区分起跑位移与持续摆动。其余既有不变量断言保留。
// 旧版与新稿均应满足波纹单调性，不把此模拟测试称为实机验证。
using System;

namespace KingdomEnhancedMod;

internal static class Program
{
    private const float Tick = 1f / 60f;      // 采样 60Hz（模拟内部固定 30Hz）
    private const float Primary = 24f / 32f;  // 主飘带 24px
    private const float Secondary = 22f / 32f;// 副飘带 22px
    private const float PrimaryGround = -14f / 32f;   // 肩部离地 14px → 肩部 local 地面 -14/32
    private const float SecondaryGround = -13f / 32f; // 副链锚点低 1px
    private const float RunSpeed = 3.5f;
    private const float WalkSpeed = 1.3f;
    // operator 2026-09-15 合成验收速度：0.65 走、1.2/1.6/2.0 跑；旧的 WalkSpeed/RunSpeed 保留给其余用例。
    private const float SpecWalkSpeed = 0.65f;
    private const float SpecRunSpeed = 1.6f;

    private static int _failed;
    private static int _passed;
    private static float _clock;

    private static int Main()
    {
        FacingChangePreservesWorldShapeAndRunningStaysBehind();
        ResetShapeSitsOnGroundAndFoldsBehind();
        NodesStayAboveGroundOnIdleWalkRunStop();
        GroundFoldNeverFlipsForwardAndPreservesLength();
        LightWindSwaysAtRestButStaysGroundedAndBehind();
        WalkTrailsLessThanRun();
        RunTrailsBehindAndNeverLocksHorizontal();
        StopSettlesBackToGroundWithinSeconds();
        ReverseVelocityMirrorsTrailAndNeverLeads();
        TeleportVelocityResetsToRestPose();
        InvalidDeltaAndWindNeverCorrupt();
        ZeroAndNegativeDeltaKeepStateIdentical();
        FixedStepIs30HzAndCatchUpIsBounded();
        FixedStepIsSamplingRateIndependent();
        SegmentLengthsAndTotalLengthStayFixed();
        ResetClearsPreviousTrail();
        ReusedChainStartsFromRestPose();
        WorldVelocityConvertsToParentLocalFacing();
        SecondaryChainIsShorterAndOutOfPhase();
        QuantizeSnapsToGridWithoutTouchingSimulation();
        LongRandomRunStaysFiniteAndBounded();
        SwingSettlesToGroundedRestWhenWindUnavailable();
        IdleWaveKeepsQuantizedRibbonAliveWithoutJitter();
        WaveTravelsAlongLengthInsteadOfTranslatingWholeLine();
        WaveAmplitudeGrowsWithSpeedAndSettlesOnStop();
        WindOppositeSignsBendOppositeWithoutExplosion();
        WindInputIsClampedAndFiniteSafe();

        Console.WriteLine();
        Console.WriteLine(_failed == 0 ? "ALL TESTS PASSED (" + _passed + ")" : _failed + " TEST(S) FAILED, " + _passed + " passed");
        return _failed == 0 ? 0 : 1;
    }

    // ============================================================
    // 用例
    // ============================================================

    /// <summary>Reset：先垂直下垂、到地后折叠到身后，全程不穿地。</summary>
    private static void FacingChangePreservesWorldShapeAndRunningStaysBehind()
    {
        var c=NewChain(Primary,PrimaryGround);
        for(int i=0;i<180;i++) c.Step(1f/60f,3.5f,i/60f);
        float[] old=(float[])c.PointsX.Clone(); c.Rebase(-1f,10f/32f);
        bool continuous=true;
        for(int i=1;i<HeroArcherClothChain.NodeCount;i++) continuous &= Math.Abs((-5f/32f+old[i])-(5f/32f-c.PointsX[i]))<1e-5f;
        Check("turn.world-shape-continuous",continuous,"turn must rebase, not instantly mirror the old world shape");
        for(int sign=-1;sign<=1;sign+=2) {
            c.Reset();c.SetWind(sign);
            for(int i=0;i<240;i++)c.Step(1f/60f,3.5f,i/60f);
            Check("run.wind-cannot-push-ahead."+sign,c.PointsX[7]<-0.1f,"steady running must trail under either bounded wind direction");
        }
        c.Rebase(float.NaN,0);Check("turn.invalid-rebase-safe",AllFinite(c),"invalid transform resets safely");
    }

    private static void ResetShapeSitsOnGroundAndFoldsBehind()
    {
        HeroArcherClothChain chain = NewChain(Primary, PrimaryGround);
        float floor = chain.FloorY;
        bool aboveGround = true;
        for (int i = 0; i < HeroArcherClothChain.NodeCount; i++) aboveGround &= chain.PointsY[i] >= FloorAt(chain, i) - 1e-4f;
        float tipX = chain.PointsX[7];
        float tipY = chain.PointsY[7];

        Check("ResetShapeSitsOnGroundAndFoldsBehind.aboveGround", aboveGround, "reset pose must not penetrate the ground");
        Check("ResetShapeSitsOnGroundAndFoldsBehind.startsVertical",
            Math.Abs(chain.PointsX[1]) < 1e-6f && Math.Abs(chain.PointsX[2]) < 1e-6f,
            "first segments must hang straight down (x=" + chain.PointsX[1] + ")");
        Check("ResetShapeSitsOnGroundAndFoldsBehind.foldsBehind", tipX <= -0.3f && tipY <= floor + 1e-3f,
            "tip=" + tipX + "," + tipY + " expected folded behind along the ground y=" + floor);
        Check("ResetShapeSitsOnGroundAndFoldsBehind.zeroVelocity",
            MaxDifference(chain, Fresh(Primary, PrimaryGround)) == 0f, "reset must zero the implicit velocity");
    }

    /// <summary>Reset / 长静止 / 走 / 跑 / 停：任何采样点所有节点都在地面之上，且段长保持。</summary>
    private static void NodesStayAboveGroundOnIdleWalkRunStop()
    {
        HeroArcherClothChain primary = NewChain(Primary, PrimaryGround);
        HeroArcherClothChain secondary = NewChain2(Secondary, SecondaryGround);
        float segment = Primary / 7f;
        bool aboveGround = true;
        float worstSegmentError = 0f;

        AssertSample(primary, ref aboveGround, ref worstSegmentError, segment);
        AssertSample(secondary, ref aboveGround, ref worstSegmentError, Secondary / 7f);

        for (int i = 0; i < 1800; i++) // 30s：静止 5s → 走 5s → 跑 5s → 停 15s
        {
            float velocity = i < 300 ? 0f : i < 600 ? WalkSpeed : i < 900 ? RunSpeed : 0f;
            _clock += Tick;
            primary.Step(Tick, velocity, _clock);
            secondary.Step(Tick, velocity, _clock);
            AssertSample(primary, ref aboveGround, ref worstSegmentError, segment);
            AssertSample(secondary, ref aboveGround, ref worstSegmentError, Secondary / 7f);
        }

        Check("NodesStayAboveGroundOnIdleWalkRunStop.ground", aboveGround,
            "every node of every chain must stay on/above its floor across idle/walk/run/stop");
        Check("NodesStayAboveGroundOnIdleWalkRunStop.segments", worstSegmentError < 1e-3f,
            "worstSegmentError=" + worstSegmentError + " expected < 1e-3 (地面折叠保持段长)");
    }

    /// <summary>地面折叠：符号持久、绝不把末端翻到身前；x 方向不在 ±0 之间随机跳。</summary>
    private static void GroundFoldNeverFlipsForwardAndPreservesLength()
    {
        HeroArcherClothChain chain = NewChain(Primary, PrimaryGround);
        bool everForward = false;
        float worstSegmentError = 0f;
        float segment = Primary / 7f;
        for (int i = 0; i < 1800; i++)
        {
            _clock += Tick;
            chain.Step(Tick, 0f, _clock); // 静止 + 风：折叠符号必须始终朝后
            if (chain.PointsX[7] > 0.02f) everForward = true;
            for (int n = 1; n < HeroArcherClothChain.NodeCount; n++)
            {
                float dx = chain.PointsX[n] - chain.PointsX[n - 1];
                float dy = chain.PointsY[n] - chain.PointsY[n - 1];
                worstSegmentError = Math.Max(worstSegmentError,
                    Math.Abs((float)Math.Sqrt(dx * dx + dy * dy) - segment));
            }
        }
        Check("GroundFoldNeverFlipsForwardAndPreservesLength.behind", !everForward,
            "grounded rest tip must never fold to the front (+x)");
        Check("GroundFoldNeverFlipsForwardAndPreservesLength.segments", worstSegmentError < 1e-3f,
            "worstSegmentError=" + worstSegmentError);
        Check("GroundFoldNeverFlipsForwardAndPreservesLength.noRandomFlipAfterVertical",
            NewChain(Primary, PrimaryGround).PointsX[2] == 0f, "竖直段之后第一折必须朝身后（-x）");
    }

    /// <summary>静止轻摆：仍贴地/在身后、摆动幅度可见但不悬浮。</summary>
    private static void LightWindSwaysAtRestButStaysGroundedAndBehind()
    {
        HeroArcherClothChain chain = NewChain(Primary, PrimaryGround);
        float minTipX = float.MaxValue;
        float maxTipX = float.MinValue;
        float minTipY = float.MaxValue;
        float floor = chain.FloorY;
        for (int i = 0; i < 300; i++)
        {
            chain.Step(Tick, 0f, _clock += Tick);
            minTipX = Math.Min(minTipX, chain.PointsX[7]);
            maxTipX = Math.Max(maxTipX, chain.PointsX[7]);
            minTipY = Math.Min(minTipY, chain.PointsY[7]);
        }
        Check("LightWindSwaysAtRestButStaysGroundedAndBehind.sway", (maxTipX - minTipX) >= 0.005f,
            "sway=" + (maxTipX - minTipX) + " expected >= 0.005 (量化后的可见性见 IdleWave* 用例)");
        Check("LightWindSwaysAtRestButStaysGroundedAndBehind.grounded", minTipY >= floor - 1e-3f && maxTipX <= 0.02f,
            "tip range [" + minTipX + "," + maxTipX + "] yMin=" + minTipY + " floor=" + floor);
    }

    private static void WalkTrailsLessThanRun()
    {
        HeroArcherClothChain walk = NewChain(Primary, PrimaryGround);
        Advance(walk, 4f, SpecWalkSpeed);
        HeroArcherClothChain run = NewChain(Primary, PrimaryGround);
        Advance(run, 4f, SpecRunSpeed);
        // 用悬空段（节点 1..4）的横向偏移衡量展开角：地面折叠段的长度本身不区分走/跑
        float walkTrail = -MeanAirborneX(walk);
        float runTrail = -MeanAirborneX(run);
        // [worker 2026-09-15 适配：速度→形状映射已明确改变（operator 合成速度 0.65 走 / 1.2-2.0 跑）。
        //  旧版用 1.3 当「走」、3.5 当「跑」—— 1.2-1.6 正是本次要修的常见跑速，两档都属「跑」，
        //  该配对已不再表达「走 vs 跑」。不变量本身（走比跑的横向拖尾小、且差 >= 0.05u）原样保留，
        //  只把两档换成语义正确的 SpecWalkSpeed/SpecRunSpeed。实测 new: 0.17 vs 0.24（差 0.07u）。]
        const float CoordinateTolerance = 1e-4f; // 0.0032px, far below the one-pixel presentation grid.
        Check("WalkTrailsLessThanRun", walkTrail > 0.02f && runTrail + CoordinateTolerance > walkTrail + 0.05f,
            "walkAirborneX=" + walkTrail + " runAirborneX=" + runTrail);
    }

    private static void RunTrailsBehindAndNeverLocksHorizontal()
    {
        HeroArcherClothChain chain = NewChain(Primary, PrimaryGround);
        Advance(chain, 4f, RunSpeed);
        float tipX = chain.PointsX[7];
        float tipY = chain.PointsY[7];
        Check("RunTrailsBehindAndNeverLocksHorizontal.behind", tipX <= -0.35f,
            "tipX=" + tipX + " expected <= -0.35 (身后)");
        Check("RunTrailsBehindAndNeverLocksHorizontal.aboveGround", tipY <= -0.15f && tipY >= chain.FloorY - 1e-3f,
            "tipY=" + tipY + " floor=" + chain.FloorY + " expected 明显低于肩但在地面之上");
    }

    /// <summary>停下：数秒内平滑回落到贴地静止姿态（不再保持跑动展开）。</summary>
    private static void StopSettlesBackToGroundWithinSeconds()
    {
        HeroArcherClothChain chain = NewChain(Primary, PrimaryGround);
        Advance(chain, 4f, RunSpeed);
        float trailingTipX = chain.PointsX[7];
        Advance(chain, 6f, 0f);
        HeroArcherClothChain rest = Fresh(Primary, PrimaryGround);
        float settledTipX = chain.PointsX[7];
        float settledTipY = chain.PointsY[7];
        Check("StopSettlesBackToGroundWithinSeconds.horizontal",
            Math.Abs(settledTipX - rest.PointsX[7]) <= 0.2f && settledTipX <= 0.02f,
            "trailing=" + trailingTipX + " settled=" + settledTipX + " rest=" + rest.PointsX[7]);
        Check("StopSettlesBackToGroundWithinSeconds.grounded",
            settledTipY <= rest.PointsY[7] + 0.05f && settledTipY >= rest.PointsY[7] - 0.05f,
            "settledTipY=" + settledTipY + " restTipY=" + rest.PointsY[7]);
    }

    private static void ReverseVelocityMirrorsTrailAndNeverLeads()
    {
        HeroArcherClothChain chain = NewChain(Primary, PrimaryGround);
        Advance(chain, 4f, RunSpeed);
        float rightwardTipX = chain.PointsX[7];
        Advance(chain, 4f, -RunSpeed);
        float leftwardTipX = chain.PointsX[7];
        Check("ReverseVelocityMirrorsTrailAndNeverLeads",
            rightwardTipX <= -0.35f && leftwardTipX >= 0.15f && rightwardTipX * leftwardTipX < 0f,
            "positiveV tipX=" + rightwardTipX + " negativeV tipX=" + leftwardTipX + " expected mirror");
    }

    private static void TeleportVelocityResetsToRestPose()
    {
        HeroArcherClothChain chain = NewChain(Primary, PrimaryGround);
        Advance(chain, 4f, RunSpeed);
        chain.Step(1f / 30f, 200f, _clock += 1f / 30f);
        Check("TeleportVelocityResetsToRestPose.positive",
            MaxDifference(chain, Fresh(Primary, PrimaryGround)) == 0f, "must reset to the grounded rest pose");
        chain.Step(1f / 30f, -500f, _clock += 1f / 30f);
        Check("TeleportVelocityResetsToRestPose.negative",
            MaxDifference(chain, Fresh(Primary, PrimaryGround)) == 0f, "negative spike must reset too");
        chain.Step(1f / 30f, float.NaN, _clock += 1f / 30f);
        Check("TeleportVelocityResetsToRestPose.nanVelocitySafe", AllFinite(chain), "NaN velocity must stay finite");
    }

    private static void InvalidDeltaAndWindNeverCorrupt()
    {
        HeroArcherClothChain probe = NewChain(Primary, PrimaryGround);
        Advance(probe, 2f, RunSpeed);
        float[] snapshot = Snapshot(probe);
        probe.Step(float.NaN, RunSpeed, 5f);
        probe.Step(float.PositiveInfinity, RunSpeed, 5f);
        probe.Step(-1f, RunSpeed, 5f);
        Check("InvalidDeltaAndWindNeverCorrupt.ignored", Identical(snapshot, Snapshot(probe)),
            "NaN/Inf/negative dt must leave the state untouched");

        for (int i = 0; i < 120; i++) probe.Step(1f / 30f, 0f, float.NaN);
        Check("InvalidDeltaAndWindNeverCorrupt.noNaN", AllFinite(probe), "NaN wind clock must not poison the chain");
        Check("InvalidDeltaAndWindNeverCorrupt.stillGrounded", probe.PointsY[7] >= probe.FloorY - 1e-3f,
            "tipY=" + probe.PointsY[7] + " floor=" + probe.FloorY);

        HeroArcherClothChain zero = NewChain(Primary, PrimaryGround);
        float[] zeroBefore = Snapshot(zero);
        zero.Step(0f, RunSpeed, 3f);
        Check("InvalidDeltaAndWindNeverCorrupt.zeroDelta", Identical(zeroBefore, Snapshot(zero)), "dt=0 must be a no-op");
    }

    private static void ZeroAndNegativeDeltaKeepStateIdentical()
    {
        HeroArcherClothChain chain = NewChain(Primary, PrimaryGround);
        Advance(chain, 1.5f, WalkSpeed);
        float[] before = Snapshot(chain);
        for (int i = 0; i < 8; i++) chain.Step(0f, WalkSpeed, _clock);
        Check("ZeroAndNegativeDeltaKeepStateIdentical", Identical(before, Snapshot(chain)), "dt=0 must not move anything");
    }

    private static void FixedStepIs30HzAndCatchUpIsBounded()
    {
        HeroArcherClothChain single = NewChain(Primary, PrimaryGround);
        HeroArcherClothChain stepped = NewChain(Primary, PrimaryGround);
        single.Step(1f, RunSpeed, 7f);
        for (int i = 0; i < 3; i++) stepped.Step(1f / 30f, RunSpeed, 7f);
        Check("FixedStepIs30HzAndCatchUpIsBounded.bounded", MaxDifference(single, stepped) < 1e-4f,
            "maxdiff=" + MaxDifference(single, stepped) + " expected < 1e-4 (catch-up 有界)");

        HeroArcherClothChain coarse = NewChain(Primary, PrimaryGround);
        coarse.Step(0.1f, RunSpeed, 7f);
        Check("FixedStepIs30HzAndCatchUpIsBounded.clamp", MaxDifference(coarse, stepped) < 1e-4f,
            "0.1s clamp must equal exactly 3 fixed steps");

        HeroArcherClothChain slow = NewChain(Primary, PrimaryGround);
        HeroArcherClothChain reference = NewChain(Primary, PrimaryGround);
        for (int i = 0; i < 3; i++) slow.Step(1f / 120f, 0f, 1f);
        Check("FixedStepIs30HzAndCatchUpIsBounded.quantized", MaxDifference(slow, reference) == 0f,
            "sub-step sampling must not advance the simulation");
        slow.Step(1f / 120f, 0f, 1f);
        reference.Step(1f / 30f, 0f, 1f);
        Check("FixedStepIs30HzAndCatchUpIsBounded.quantizedAdvance", MaxDifference(slow, reference) < 1e-5f,
            "maxdiff=" + MaxDifference(slow, reference) + " expected < 1e-5 after the 4th 1/120s sample");
    }

    /// <summary>采样率无关：同速度、同经过时间（同风钟锚点）在 60/30/10Hz 下得到同一姿态。</summary>
    private static void FixedStepIsSamplingRateIndependent()
    {
        float[] sampled = null;
        foreach (float rate in new[] { 60f, 30f, 10f, 144f })
        {
            HeroArcherClothChain chain = NewChain(Primary, PrimaryGround);
            int ticks = (int)Math.Round(3f * rate);
            for (int i = 0; i < ticks; i++) chain.Step(1f / rate, RunSpeed, 1.5f);
            float[] state = Snapshot(chain);
            if (sampled == null)
            {
                sampled = state;
                continue;
            }
            float worst = 0f;
            for (int i = 0; i < state.Length; i++) worst = Math.Max(worst, Math.Abs(state[i] - sampled[i]));
            Check("FixedStepIsSamplingRateIndependent." + (int)rate + "Hz", worst < 1e-4f,
                "worst=" + worst + " expected < 1e-4 （内部 simulationTime，与调用频率无关）");
        }
    }

    private static void SegmentLengthsAndTotalLengthStayFixed()
    {
        HeroArcherClothChain chain = NewChain(Primary, PrimaryGround);
        float segment = Primary / 7f;
        float worstSegmentError = 0f;
        float worstReach = 0f;
        for (int i = 0; i < 600; i++)
        {
            chain.Step(Tick, i % 120 < 60 ? RunSpeed : -WalkSpeed, _clock += Tick);
            for (int n = 1; n < HeroArcherClothChain.NodeCount; n++)
            {
                float dx = chain.PointsX[n] - chain.PointsX[n - 1];
                float dy = chain.PointsY[n] - chain.PointsY[n - 1];
                worstSegmentError = Math.Max(worstSegmentError, Math.Abs((float)Math.Sqrt(dx * dx + dy * dy) - segment));
            }
            float tipDx = chain.PointsX[7];
            float tipDy = chain.PointsY[7];
            worstReach = Math.Max(worstReach, (float)Math.Sqrt(tipDx * tipDx + tipDy * tipDy));
        }
        Check("SegmentLengthsAndTotalLengthStayFixed", worstSegmentError < 1e-3f,
            "worstSegmentError=" + worstSegmentError);
        Check("SegmentLengthsAndTotalLengthStayFixed.reach", worstReach <= Primary + 1e-3f,
            "worstReach=" + worstReach + " expected <= " + Primary);
    }

    private static void ResetClearsPreviousTrail()
    {
        HeroArcherClothChain used = NewChain(Primary, PrimaryGround);
        Advance(used, 4f, RunSpeed);
        used.Reset();
        Check("ResetClearsPreviousTrail", MaxDifference(used, Fresh(Primary, PrimaryGround)) == 0f,
            "maxdiff=" + MaxDifference(used, Fresh(Primary, PrimaryGround)) + " expected 0 (无上一条尾迹)");
    }

    private static void ReusedChainStartsFromRestPose()
    {
        HeroArcherClothChain used = NewChain(Primary, PrimaryGround);
        Advance(used, 6f, -RunSpeed);
        used.Reset();
        HeroArcherClothChain fresh = NewChain(Primary, PrimaryGround);
        for (int i = 0; i < 120; i++)
        {
            _clock += Tick;
            used.Step(Tick, WalkSpeed, _clock);
            fresh.Step(Tick, WalkSpeed, _clock);
        }
        Check("ReusedChainStartsFromRestPose", MaxDifference(used, fresh) == 0f,
            "maxdiff=" + MaxDifference(used, fresh) + " expected 0");
    }

    /// <summary>世界 dx → 父 local：除以父链真实 scale.x（同时完成朝向符号翻转），每帧一次。</summary>
    private static void WorldVelocityConvertsToParentLocalFacing()
    {
        Check("WorldVelocityConvertsToParentLocalFacing.right", HeroArcherClothMath.LocalVelocityXFromWorld(2f, 1f) == 2f, "facing right");
        Check("WorldVelocityConvertsToParentLocalFacing.left", HeroArcherClothMath.LocalVelocityXFromWorld(-2f, -1f) == 2f, "facing left, moving left");
        Check("WorldVelocityConvertsToParentLocalFacing.leftBackpedal", HeroArcherClothMath.LocalVelocityXFromWorld(2f, -1f) == -2f, "facing left, moving right");
        Check("WorldVelocityConvertsToParentLocalFacing.scaledParent",
            HeroArcherClothMath.LocalVelocityXFromWorld(4f, 2f) == 2f
            && HeroArcherClothMath.LocalVelocityXFromWorld(-4f, -2f) == 2f
            && HeroArcherClothMath.LocalVelocityXFromWorld(4f, -2f) == -2f,
            "must use the real parent scale, not just its sign");
        Check("WorldVelocityConvertsToParentLocalFacing.invalid",
            HeroArcherClothMath.LocalVelocityXFromWorld(float.NaN, -1f) == 0f
            && HeroArcherClothMath.LocalVelocityXFromWorld(2f, float.NaN) == 0f
            && HeroArcherClothMath.LocalVelocityXFromWorld(2f, float.PositiveInfinity) == 0f,
            "NaN/Inf must not produce a direction");
        Check("WorldVelocityConvertsToParentLocalFacing.degenerateScale",
            HeroArcherClothMath.LocalVelocityXFromWorld(2f, 0f) == 2f
            && HeroArcherClothMath.LocalVelocityXFromWorld(2f, -0f) == 2f,
            "degenerate scale must fall back to the facing sign");

        // 朝左的英雄向左跑（worldVx<0, scale.x=-1）→ local 速度 +x → 飘带拖向 local -x
        // 父 scale.x=-1 时 local -x 映射回世界 +x = 身后（绝不是面前）。
        HeroArcherClothChain chain = NewChain(Primary, PrimaryGround);
        float localVelocity = HeroArcherClothMath.LocalVelocityXFromWorld(-RunSpeed, -1f);
        for (int i = 0; i < 240; i++) chain.Step(Tick, localVelocity, _clock += Tick);
        float localTipX = chain.PointsX[7];
        Check("WorldVelocityConvertsToParentLocalFacing.trailsWorldBehind", localTipX <= -0.35f && -localTipX >= 0.35f,
            "localTipX=" + localTipX + " worldTipX=" + (-localTipX) + " expected 身后（世界 +x 侧）");
    }

    private static void SecondaryChainIsShorterAndOutOfPhase()
    {
        HeroArcherClothChain primary = NewChain(Primary, PrimaryGround);
        HeroArcherClothChain secondary = NewChain2(Secondary, SecondaryGround);
        Check("SecondaryChainIsShorterAndOutOfPhase.length", Math.Abs(primary.Length - Primary) < 1e-6f
            && Math.Abs(secondary.Length - Secondary) < 1e-6f && secondary.Length < primary.Length,
            "lengths=" + primary.Length + "/" + secondary.Length);
        for (int i = 0; i < 150; i++)
        {
            _clock += Tick;
            primary.Step(Tick, WalkSpeed, _clock);
            secondary.Step(Tick, WalkSpeed, _clock);
        }
        float difference = Math.Abs(primary.PointsX[7] - secondary.PointsX[7]) + Math.Abs(primary.PointsY[7] - secondary.PointsY[7]);
        Check("SecondaryChainIsShorterAndOutOfPhase.phase", difference >= 0.01f,
            "difference=" + difference + " expected >= 0.01 (不同相位/比例)");
    }

    private static void QuantizeSnapsToGridWithoutTouchingSimulation()
    {
        Check("QuantizeSnapsToGridWithoutTouchingSimulation.snap",
            Math.Abs(HeroArcherClothMath.Quantize(0.9f) - 29f / 32f) < 1e-6f
            && Math.Abs(HeroArcherClothMath.Quantize(-0.75f) + 0.75f) < 1e-6f
            && HeroArcherClothMath.Quantize(float.NaN) == 0f,
            "expected 29/32, -24/32, 0");
        bool onGrid = true;
        bool withinHalf = true;
        for (int i = -200; i <= 200; i++)
        {
            float value = i * 0.007f;
            float quantized = HeroArcherClothMath.Quantize(value);
            onGrid &= Math.Abs(quantized * 32f - (float)Math.Round(quantized * 32f)) < 1e-4f;
            withinHalf &= Math.Abs(quantized - value) <= 1f / 64f + 1e-6f;
        }
        Check("QuantizeSnapsToGridWithoutTouchingSimulation.grid", onGrid, "quantized values must be multiples of 1/32");
        Check("QuantizeSnapsToGridWithoutTouchingSimulation.halfCell", withinHalf, "error must stay <= half a cell");

        HeroArcherClothChain chain = NewChain(Primary, PrimaryGround);
        Advance(chain, 3f, RunSpeed);
        float[] before = Snapshot(chain);
        for (int i = 0; i < HeroArcherClothChain.NodeCount; i++)
        {
            HeroArcherClothMath.Quantize(chain.PointsX[i]);
            HeroArcherClothMath.Quantize(chain.PointsY[i]);
        }
        Check("QuantizeSnapsToGridWithoutTouchingSimulation.noFeedback", Identical(before, Snapshot(chain)),
            "quantizing for the view must not change simulation state");

        Check("QuantizeSnapsToGridWithoutTouchingSimulation.halfWidth",
            Math.Abs(HeroArcherClothMath.HalfWidthUnits(0) - 2f / 32f) < 1e-6f
            && Math.Abs(HeroArcherClothMath.HalfWidthUnits(7) - 1.5f / 32f) < 1e-6f
            && HeroArcherClothMath.HalfWidthUnits(4) > HeroArcherClothMath.HalfWidthUnits(5),
            "ground envelope must cover 4px body / 3px tail on the integer-pixel floor");
    }

    private static void LongRandomRunStaysFiniteAndBounded()
    {
        HeroArcherClothChain chain = NewChain(Primary, PrimaryGround);
        uint state = 20260914u;
        bool bounded = true;
        for (int i = 0; i < 3000; i++)
        {
            state = state * 1664525u + 1013904223u;
            float velocity = ((state >> 8) % 2001 - 1000) / 200f; // ±5 u/s
            if (i % 700 == 699) velocity = 250f;                  // 周期性瞬移尖峰
            chain.Step(Tick, velocity, _clock += Tick);
            if (!AllFinite(chain)) { bounded = false; break; }
            for (int n = 0; n < HeroArcherClothChain.NodeCount; n++)
            {
                if (Math.Abs(chain.PointsX[n]) > HeroArcherClothChain.MaxCoordinate
                    || Math.Abs(chain.PointsY[n]) > HeroArcherClothChain.MaxCoordinate
                    || chain.PointsY[n] < FloorAt(chain, n) - 1e-3f)
                {
                    bounded = false;
                    break;
                }
            }
            if (!bounded) break;
        }
        Check("LongRandomRunStaysFiniteAndBounded", bounded, "3000 步随机速度（含瞬移尖峰）必须恒有限、有界、不穿地");
    }

    private static void SwingSettlesToGroundedRestWhenWindUnavailable()
    {
        HeroArcherClothChain chain = NewChain(Primary, PrimaryGround);
        Advance(chain, 4f, RunSpeed);
        for (int i = 0; i < 300; i++) chain.Step(Tick, 0f, float.NaN);
        HeroArcherClothChain rest = Fresh(Primary, PrimaryGround);
        Check("SwingSettlesToGroundedRestWhenWindUnavailable",
            Math.Abs(chain.PointsX[7] - rest.PointsX[7]) <= 0.05f
            && Math.Abs(chain.PointsY[7] - rest.PointsY[7]) <= 0.05f,
            "settled=" + chain.PointsX[7] + "," + chain.PointsY[7] + " rest=" + rest.PointsX[7] + "," + rest.PointsY[7]);
    }

    // ============================================================
    // 波纹（reviewer 第二轮：沿线行波，不是整条同相位弱 sin）
    // ============================================================

    /// <summary>量化后的呈现代理：view 写顶点时对每个顶点做 1/32 量化，
    /// 线段中点与 Quantize(模拟点) 之差 ≤ 1/64（半格），故测试用 Quantize(点) 作呈现代理。</summary>
    private static float RenderX(HeroArcherClothChain chain, int node) => HeroArcherClothMath.Quantize(chain.PointsX[node]);

    private static float RenderY(HeroArcherClothChain chain, int node) => HeroArcherClothMath.Quantize(chain.PointsY[node]);

    /// <summary>静止（无风化）：4 秒内量化后的尾部/内部节点必须出现 ≥1px 的柔和变化，且不抖。</summary>
    private static void IdleWaveKeepsQuantizedRibbonAliveWithoutJitter()
    {
        HeroArcherClothChain chain = NewChain(Primary, PrimaryGround);
        chain.SetWind(0f);
        chain.Step(Tick, 0f, _clock += Tick);
        float tailX0 = RenderX(chain, 7), tailY0 = RenderY(chain, 7);
        float midX0 = RenderX(chain, 5), midY0 = RenderY(chain, 5);
        float tailMoved = 0f, midMoved = 0f, worstStep = 0f;
        float previousTailX = tailX0, previousTailY = tailY0;
        for (int i = 0; i < 240; i++) // 4s @60Hz
        {
            chain.Step(Tick, 0f, _clock += Tick);
            float tailX = RenderX(chain, 7), tailY = RenderY(chain, 7);
            float midX = RenderX(chain, 5), midY = RenderY(chain, 5);
            tailMoved = Math.Max(tailMoved, Math.Max(Math.Abs(tailX - tailX0), Math.Abs(tailY - tailY0)));
            midMoved = Math.Max(midMoved, Math.Max(Math.Abs(midX - midX0), Math.Abs(midY - midY0)));
            if (i % 12 == 11)
            {
                worstStep = Math.Max(worstStep, Math.Max(Math.Abs(tailX - previousTailX), Math.Abs(tailY - previousTailY)));
                previousTailX = tailX;
                previousTailY = tailY;
            }
        }
        Check("IdleWaveKeepsQuantizedRibbonAliveWithoutJitter.visible",
            tailMoved >= 1f / 32f || midMoved >= 1f / 32f,
            "idle quantized movement tail=" + tailMoved + " mid=" + midMoved + " expected >= 1px");
        Check("IdleWaveKeepsQuantizedRibbonAliveWithoutJitter.soft", worstStep <= 2f / 32f,
            "worst change per 0.2s = " + worstStep + " expected <= 2px (不抖)");
        Check("IdleWaveKeepsQuantizedRibbonAliveWithoutJitter.contained",
            chain.PointsY[7] >= chain.FloorY - 1e-3f && AllFinite(chain), "仍不穿地/无 NaN");
    }

    /// <summary>行波：各节点相位错开 → 带子起弯；末端与中段相对位移不同（不是整条平移）。</summary>
    private static void WaveTravelsAlongLengthInsteadOfTranslatingWholeLine()
    {
        HeroArcherClothChain chain = NewChain(Primary, PrimaryGround);
        chain.SetWind(0f);
        float worstBend = 0f;
        float worstRelative = 0f;
        for (int i = 0; i < 240; i++)
        {
            chain.Step(Tick, 0f, _clock += Tick);
            if (i % 8 != 0) continue;
            float bend = 0f;
            for (int n = 2; n < HeroArcherClothChain.NodeCount - 2; n++)
            {
                float ax = chain.PointsX[n] - chain.PointsX[n - 1];
                float ay = chain.PointsY[n] - chain.PointsY[n - 1];
                float bx = chain.PointsX[n + 1] - chain.PointsX[n];
                float by = chain.PointsY[n + 1] - chain.PointsY[n];
                float cross = ax * by - ay * bx;
                bend = Math.Max(bend, Math.Abs(cross) / (chain.SegmentLength * chain.SegmentLength));
            }
            worstBend = Math.Max(worstBend, bend);
            float relativeX = Math.Abs((chain.PointsX[7] - chain.PointsX[5]) - (chain.PointsX[5] - chain.PointsX[3]));
            worstRelative = Math.Max(worstRelative, relativeX);
        }
        Check("WaveTravelsAlongLengthInsteadOfTranslatingWholeLine.bend", worstBend >= 0.02f,
            "worst per-joint bend (sin of relative angle) = " + worstBend + " expected >= 0.02 (沿线弯曲)");
        Check("WaveTravelsAlongLengthInsteadOfTranslatingWholeLine.travel", worstRelative >= 0.3f / 32f,
            "worst tail-vs-mid relative offset = " + worstRelative + " expected >= 0.3px (不是整条平移)");
    }

    /// <summary>幅度随走/跑平滑增大；停下平滑回落但仍保留静止波纹（无硬开关）。</summary>
    private static void WaveAmplitudeGrowsWithSpeedAndSettlesOnStop()
    {
        // [worker 2026-09-15 适配：原度量把「起跑瞬态」也算进摆动（从静止姿态甩到展开姿态本身就有几 px），
        //  于是走/跑的差异被瞬态淹没，量化后撞到同一格（1.3 与 3.5 都是 0.234375）。速度→形状映射本次
        //  明确改变（跑动被气流展开并吸收振动），因此改为在包络稳定后测量「整条带子的可见摆动」：
        //  不变量（波纹随速度增强、且跑动仍有可见波纹）原样保留且更强。该度量在旧实现上同样单调
        //  （旧：2/5/8px），不是只给新实现开的后门。]
        float idle = SteadyRibbonSwing(0f, 4f);
        float walk = SteadyRibbonSwing(WalkSpeed, 4f);
        float run = SteadyRibbonSwing(RunSpeed, 4f);
        Check("WaveAmplitudeGrowsWithSpeedAndSettlesOnStop.grows",
            walk > idle && run > walk && run >= idle + 1f / 32f,
            "idle=" + idle + " walk=" + walk + " run=" + run + " expected idle < walk < run");

        HeroArcherClothChain chain = NewChain(Primary, PrimaryGround);
        chain.SetWind(0f);
        Advance(chain, 4f, RunSpeed);
        Advance(chain, 8f, 0f);            // 平滑减幅
        float settled = QuantizedSwing(chain, 4f);
        Check("WaveAmplitudeGrowsWithSpeedAndSettlesOnStop.settles",
            settled <= run && settled >= 1f / 32f,
            "run=" + run + " settled=" + settled + " expected 1px <= settled <= run (停下仍有细波)");
        Check("WaveAmplitudeGrowsWithSpeedAndSettlesOnStop.noHardSwitch",
            Math.Abs(chain.PointsX[7] - Fresh(Primary, PrimaryGround).PointsX[7]) <= 0.25f,
            "停下后末端应回落接近静止姿态而没有跳变");
    }

    /// <summary>原生风符号：反向吹 6 秒让平均弯曲反向，且不爆炸/不穿地。</summary>
    private static void WindOppositeSignsBendOppositeWithoutExplosion()
    {
        float positive = WindMeanBend(1f);
        float negative = WindMeanBend(-1f);
        Check("WindOppositeSignsBendOppositeWithoutExplosion.direction",
            positive > negative && positive - negative >= 0.3f / 32f,
            "wind +1 meanBend=" + positive + " wind -1 meanBend=" + negative + " expected 反向且差 >= 0.5px");
        Check("WindOppositeSignsBendOppositeWithoutExplosion.stable", Math.Abs(positive) < 0.5f && Math.Abs(negative) < 0.5f,
            "meanBend=" + positive + "/" + negative + " expected < 0.5u (无爆炸/无永久水平)");
    }

    private static void WindInputIsClampedAndFiniteSafe()
    {
        HeroArcherClothChain clamped = NewChain(Primary, PrimaryGround);
        HeroArcherClothChain unit = NewChain(Primary, PrimaryGround);
        for (int i = 0; i < 240; i++)
        {
            _clock += Tick;
            clamped.Step(Tick, 0f, _clock);
            unit.Step(Tick, 0f, _clock);
            clamped.SetWind(i % 60 == 0 ? (i % 120 == 0 ? 50f : -50f) : 0f);
            unit.SetWind(i % 60 == 0 ? (i % 120 == 0 ? 1f : -1f) : 0f);
        }
        Check("WindInputIsClampedAndFiniteSafe.clamp", MaxDifference(clamped, unit) == 0f,
            "wind ±50 必须等价于 ±1（clamp），maxdiff=" + MaxDifference(clamped, unit));

        HeroArcherClothChain ignored = NewChain(Primary, PrimaryGround);
        ignored.SetWind(float.NaN);
        ignored.SetWind(float.PositiveInfinity);
        ignored.SetWind(1f);
        HeroArcherClothChain reference = NewChain(Primary, PrimaryGround);
        reference.SetWind(1f);
        for (int i = 0; i < 120; i++)
        {
            _clock += Tick;
            ignored.Step(Tick, 0f, _clock);
            reference.Step(Tick, 0f, _clock);
        }
        Check("WindInputIsClampedAndFiniteSafe.nanIgnored", MaxDifference(ignored, reference) == 0f,
            "NaN/Inf 风输入必须被忽略，maxdiff=" + MaxDifference(ignored, reference));
    }

    private static float WindMeanBend(float wind)
    {
        HeroArcherClothChain chain = NewChain(Primary, PrimaryGround);
        chain.SetWind(wind);
        for (int i = 0; i < 360; i++) chain.Step(Tick, 0f, 0f); // 固定风钟锚点：两条链相位完全一致，差异只来自风符号
        float total = 0f;
        for (int n = 1; n < HeroArcherClothChain.NodeCount; n++) total += chain.PointsX[n];
        bool sane = AllFinite(chain) && chain.PointsY[7] >= chain.FloorY - 1e-3f;
        return sane ? total / (HeroArcherClothChain.NodeCount - 1) : float.NaN;
    }

    private static float MeanAirborneX(HeroArcherClothChain chain)
    {
        float total = 0f;
        for (int n = 1; n <= 4; n++) total += chain.PointsX[n];
        return total / 4f;
    }

    /// <summary>
    /// 「整条带子可见摆动」：先按 velocity 跑 4s 让展开包络稳定，再取相同长度窗口内
    /// 节点 1..7 的量化 Y 摆动最大值（u）。衡量「波纹随速度增强」时不混入起跑瞬态。
    /// </summary>
    private static float SteadyRibbonSwing(float velocity, float seconds)
    {
        HeroArcherClothChain chain = NewChain(Primary, PrimaryGround);
        chain.SetWind(0f);
        int ticks = (int)Math.Round(seconds / Tick);
        for (int i = 0; i < ticks; i++) chain.Step(Tick, velocity, _clock += Tick);
        float[] min = new float[HeroArcherClothChain.NodeCount];
        float[] max = new float[HeroArcherClothChain.NodeCount];
        for (int n = 0; n < HeroArcherClothChain.NodeCount; n++) { min[n] = float.MaxValue; max[n] = float.MinValue; }
        for (int i = 0; i < ticks; i++)
        {
            chain.Step(Tick, velocity, _clock += Tick);
            for (int n = 1; n < HeroArcherClothChain.NodeCount; n++)
            {
                float y = RenderY(chain, n);
                min[n] = Math.Min(min[n], y);
                max[n] = Math.Max(max[n], y);
            }
        }
        float worst = 0f;
        for (int n = 1; n < HeroArcherClothChain.NodeCount; n++) worst = Math.Max(worst, max[n] - min[n]);
        return worst;
    }

    private static float QuantizedSwing(HeroArcherClothChain chain, float seconds)
    {
        float min = float.MaxValue, max = float.MinValue;
        int ticks = (int)Math.Round(seconds / Tick);
        for (int i = 0; i < ticks; i++)
        {
            chain.Step(Tick, 0f, _clock += Tick);
            float fingerprint = RenderX(chain, 6) + 0.5f * RenderY(chain, 6);
            min = Math.Min(min, fingerprint);
            max = Math.Max(max, fingerprint);
        }
        return max - min;
    }

    // ============================================================
    // 工具
    // ============================================================

    private static HeroArcherClothChain NewChain(float length, float ground) => new HeroArcherClothChain(length, 0f, 1f, 1f, ground);

    private static HeroArcherClothChain NewChain2(float length, float ground) => new HeroArcherClothChain(length, 1.1f, 0.9f, 1.15f, ground);

    private static HeroArcherClothChain Fresh(float length, float ground) => new HeroArcherClothChain(length, 0f, 1f, 1f, ground);

    private static float FloorAt(HeroArcherClothChain chain, int node) => chain.FloorY + HeroArcherClothMath.HalfWidthUnits(node) - HeroArcherClothMath.HalfWidthUnits(HeroArcherClothChain.NodeCount - 1);

    private static void AssertSample(HeroArcherClothChain chain, ref bool aboveGround, ref float worstSegmentError, float segment)
    {
        for (int i = 0; i < HeroArcherClothChain.NodeCount; i++)
        {
            if (chain.PointsY[i] < FloorAt(chain, i) - 1e-3f) aboveGround = false;
        }
        for (int n = 1; n < HeroArcherClothChain.NodeCount; n++)
        {
            float dx = chain.PointsX[n] - chain.PointsX[n - 1];
            float dy = chain.PointsY[n] - chain.PointsY[n - 1];
            worstSegmentError = Math.Max(worstSegmentError, Math.Abs((float)Math.Sqrt(dx * dx + dy * dy) - segment));
        }
    }

    private static void Advance(HeroArcherClothChain chain, float seconds, float velocity)
    {
        int ticks = (int)Math.Round(seconds / Tick);
        for (int i = 0; i < ticks; i++) chain.Step(Tick, velocity, _clock += Tick);
    }

    private static float[] Snapshot(HeroArcherClothChain chain)
    {
        float[] copy = new float[HeroArcherClothChain.NodeCount * 2];
        for (int i = 0; i < HeroArcherClothChain.NodeCount; i++)
        {
            copy[i * 2] = chain.PointsX[i];
            copy[i * 2 + 1] = chain.PointsY[i];
        }
        return copy;
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

    private static float MaxDifference(HeroArcherClothChain a, HeroArcherClothChain b)
    {
        float worst = 0f;
        for (int i = 0; i < HeroArcherClothChain.NodeCount; i++)
        {
            worst = Math.Max(worst, Math.Abs(a.PointsX[i] - b.PointsX[i]));
            worst = Math.Max(worst, Math.Abs(a.PointsY[i] - b.PointsY[i]));
        }
        return worst;
    }

    private static bool AllFinite(HeroArcherClothChain chain)
    {
        for (int i = 0; i < HeroArcherClothChain.NodeCount; i++)
        {
            if (!HeroArcherClothMath.IsFinite(chain.PointsX[i])) return false;
            if (!HeroArcherClothMath.IsFinite(chain.PointsY[i])) return false;
        }
        return true;
    }

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
