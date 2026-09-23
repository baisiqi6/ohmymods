using KingdomEnhancedMod;

namespace MusketeerRuntimeTests
{
    /// <summary>
    /// 纯动画时钟（atlas.json clocks 契约）：帧表/画面索引/循环、
    /// "unwrapped nt × clip 长度 再取模"（绝不先取 Fraction）、暂停冻结、
    /// 枪械 Fire→Reload→Aim 序列与零窗口跳过。
    /// </summary>
    internal static class AnimationTests
    {
        internal static void Run()
        {
            Case.Run("clip table matches the approved manifest (67 frames, holds, loops)", ClipTableMatchesManifest);
            Case.Run("atlas anchors carry all 67 keys incl. prepared muzzle [47,17]", AnchorsMatchManifest);
            Case.Run("muzzle pixel converts to actor-local coordinates", MuzzleAnchorConversion);
            Case.Run("idle clock uses authored holds and wraps at 4.81s", IdleHoldsAndWrap);
            Case.Run("locomotion phase uses unwrapped nt*clipLength, not Fraction first", LocomotionGreekWrap);
            Case.Run("stand clock freezes while the native phase is frozen (Idleness=0)", StandClockFreezes);
            Case.Run("pause (delta 0 / disabled) freezes the displayed frame", PauseFreezes);
            Case.Run("unknown native state suspends custom rendering (-1)", UnknownSuspends);
            Case.Run("confirmed shot drives Fire then Reload then holds Aim", FireReloadAim);
            Case.Run("reload window compresses/stretches with nextEligibleShotTime", ReloadWindowScaling);
            Case.Run("zero reload window skips straight to Aim", ZeroWindowSkipsReload);
            Case.Run("movement interrupts gun presentation but not the confirmed Fire", MovementInterrupts);
            Case.Run("aim stance resumes once movement stops (aiming still true)", AimResumesAfterMovement);
            Case.Run("target acquisition during walk never freezes a stationary pose", MidWalkAcquisition);
            Case.Run("invalid/unknown native sampling outranks the gun phase (incl. Fire)", InvalidSamplingOutranksGun);
            Case.Run("walk/run sample the native clip seconds modulo authored gait", LocomotionFrames);
        }

        private static void ClipTableMatchesManifest()
        {
            Check.Equal(67, MusketeerAtlas.FrameCount, "67 authored frames");
            Check.Equal(12, MusketeerAtlas.Columns, "columns");
            Check.Equal(6, MusketeerAtlas.Rows, "rows");
            Check.Equal(56, MusketeerAtlas.CellWidth, "cell width");
            Check.Equal(32, MusketeerAtlas.CellHeight, "cell height");
            Check.Near(32d, MusketeerAtlas.PixelsPerUnit, 0d, "ppu");
            Check.Near(31d, MusketeerAtlas.PivotPixelX, 0d, "pivot x");
            Check.Near(2d, MusketeerAtlas.PivotPixelY, 0d, "pivot y");

            AssertClip(MusketeerAction.Idle, 0, 12, 4.81d, true);
            AssertClip(MusketeerAction.Walk, 12, 8, 1.0d, true);
            AssertClip(MusketeerAction.Run, 20, 8, 0.75d, true);
            AssertClip(MusketeerAction.Raise, 28, 6, 0.33d, false);
            AssertClip(MusketeerAction.Aim, 34, 2, 0.3d, false);
            AssertClip(MusketeerAction.Fire, 36, 5, 0.25d, false);
            AssertClip(MusketeerAction.Reload, 41, 12, 1.4d, false);
            AssertClip(MusketeerAction.Lower, 53, 6, 0.39d, false);
            AssertClip(MusketeerAction.Retreat, 59, 8, 0.84d, true);

            int total = 0;
            for (int action = 0; action <= (int)MusketeerAction.Retreat; action++)
            {
                MusketeerAtlas.TryGetClip((MusketeerAction)action, out MusketeerClip clip);
                total += clip.FrameCount;
            }
            Check.Equal(67, total, "clip frame counts sum to 67");
        }

        private static void AssertClip(MusketeerAction action, int first, int count, double duration, bool loops)
        {
            Check.True(MusketeerAtlas.TryGetClip(action, out MusketeerClip clip), "clip exists: " + action);
            Check.Equal(first, clip.FirstFrame, action + " first frame");
            Check.Equal(count, clip.FrameCount, action + " frame count");
            Check.Near(duration, clip.Duration, 1e-9d, action + " authored duration");
            Check.Equal(loops, clip.Loops, action + " loop flag");
        }

        private static void AnchorsMatchManifest()
        {
            Check.Equal(67, MusketeerAtlas.Anchors.Length, "anchors cover every key");
            MusketeerAnchor aim = MusketeerAtlas.Anchors[35];
            Check.Equal((byte)47, aim.MuzzleX, "aim muzzle x");
            Check.Equal((byte)17, aim.MuzzleY, "aim muzzle y");
            MusketeerAnchor idle5 = MusketeerAtlas.Anchors[5];
            Check.Equal((byte)1, idle5.TorsoLift, "idle breath torso lift");
            Check.Equal((byte)32, idle5.RearGripX, "idle breath rear grip");
            MusketeerAnchor reload6 = MusketeerAtlas.Anchors[47];
            Check.Equal((byte)46, reload6.MuzzleX, "reload muzzle");
            Check.True(MusketeerAtlas.FrameToCell(66, out int column, out int row), "frame 66 maps to a cell");
            Check.Equal(6, column, "frame 66 column");
            Check.Equal(5, row, "frame 66 row");
            Check.False(MusketeerAtlas.FrameToCell(67, out _, out _), "frame 67 out of range");
        }

        private static void MuzzleAnchorConversion()
        {
            MusketeerAtlas.PreparedMuzzleLocal(out float x, out float y);
            Check.Near((47d + 0.5d - 31d) / 32d, x, 1e-6d, "muzzle local x");
            Check.Near((32d - 17d - 0.5d - 2d) / 32d, y, 1e-6d, "muzzle local y");
            Check.Near(0.515625d, x, 1e-6d, "muzzle local x value");
            Check.Near(0.390625d, y, 1e-6d, "muzzle local y value");
        }

        private static void IdleHoldsAndWrap()
        {
            MusketeerAtlas.TryGetClip(MusketeerAction.Idle, out MusketeerClip idle);
            Check.Equal(0, MusketeerAtlas.FrameAt(idle, 0d), "idle t=0");
            Check.Equal(1, MusketeerAtlas.FrameAt(idle, 1.2d), "idle t=1.2");
            Check.Equal(1, MusketeerAtlas.FrameAt(idle, 1.49d), "idle t=1.49 still frame 1");
            Check.Equal(2, MusketeerAtlas.FrameAt(idle, 1.5d), "idle t=1.5 frame 2");
            Check.Equal(11, MusketeerAtlas.FrameAt(idle, 3.71d), "idle t=3.71 last frame");
            Check.Equal(0, MusketeerAtlas.FrameAt(idle, 4.81d), "idle wraps");
            Check.Equal(0, MusketeerAtlas.FrameAt(idle, 9.62d), "idle wraps twice");
        }

        private static void LocomotionGreekWrap()
        {
            // 原生希腊世界拉弓 clip 长度 2.167s 是同一类坑：相位必须先乘 clip 长度。
            Check.True(MusketeerAtlas.TryLocomotionPhaseSeconds(1.5d, 2.167d, 1.0d, out double phase),
                "phase resolves");
            Check.Near(0.2505d, phase, 1e-9d, "phase = (1.5*2.167) mod 1.0");

            MusketeerAtlas.TryGetClip(MusketeerAction.Walk, out MusketeerClip walk);
            Check.Equal(14, MusketeerAtlas.FrameAt(walk, phase), "walk frame from unwrapped phase");
            // 反例（必须不同）：先取 Fraction(normalizedTime) 会得到 0.0835 → 首帧。
            double fractionFirst = 0.5d * 2.167d % 1.0d;
            Check.Near(0.0835d, fractionFirst, 1e-9d, "fraction-first phase differs");
            Check.Equal(12, MusketeerAtlas.FrameAt(walk, fractionFirst), "fraction-first would show frame 0");
        }

        private static void StandClockFreezes()
        {
            var state = new MusketeerAnimationState();
            state.SetMotion(MusketeerMotion.Idle, 0d, 0.0d, 0.5d, 0.016d);
            Check.Equal(0, state.Tick(0d), "idle first frame");
            // 原生 Stand 相位冻结（Idleness=0）：normalizedTime 不变 → 不再累计
            state.SetMotion(MusketeerMotion.Idle, 0.016d, 0.0d, 0.5d, 0.016d);
            Check.Equal(0, state.Tick(0.016d), "frozen stand stays frame 0");
            // 相位推进但 delta 为 0（暂停帧）：同样不累计
            state.SetMotion(MusketeerMotion.Idle, 0.032d, 0.1d, 0.5d, 0d);
            state.SetMotion(MusketeerMotion.Idle, 0.048d, 0.2d, 0.5d, 0d);
            Check.Equal(0, state.Tick(0.048d), "no delta no advance");
            // 真正推进：累计到 1.2s 后进入第 2 帧
            double now = 0.048d;
            for (int i = 0; i < 90; i++)
            {
                now += 0.016d;
                state.SetMotion(MusketeerMotion.Idle, now, 0.2d + (i + 1) * 0.01d, 0.5d, 0.016d);
                state.Tick(now);
            }
            Check.Equal(1, state.Tick(now), "stand seconds accumulate while the native phase advances");
        }

        private static void PauseFreezes()
        {
            var state = new MusketeerAnimationState();
            state.SetMotion(MusketeerMotion.Walk, 0d, 0.3d, 0.5d, 0d);
            int first = state.Tick(0d);
            state.SetMotion(MusketeerMotion.Walk, 0.016d, 0.3d, 0.5d, 0d);
            Check.Equal(first, state.Tick(0.016d), "paused walk keeps its frame");
            state.Enabled = false;
            Check.Equal(-1, state.Tick(0.032d), "disabled suspends");
            Check.True(state.Suspended, "suspended flag");
        }

        private static void UnknownSuspends()
        {
            var state = new MusketeerAnimationState();
            state.SetMotion(MusketeerMotion.Unknown, 0d, 0d, 0d, 0d);
            Check.Equal(-1, state.Tick(0d), "unknown native state never guesses a frame");
            Check.True(state.Suspended, "suspended");
            state.SetMotion(MusketeerMotion.Idle, 0.016d, 0d, 0.5d, 0.016d);
            Check.True(state.Tick(0.016d) >= 0, "returns after a supported state");
        }

        private static void FireReloadAim()
        {
            var state = new MusketeerAnimationState();
            state.SetMotion(MusketeerMotion.Idle, 0d, 0d, 0.5d, 0d);
            state.SetAiming(true, 0d);
            Check.Equal(28, state.Tick(0d), "raise starts");
            Check.Equal(33, state.Tick(0.3d), "raise last frame");
            Check.Equal(35, state.Tick(0.34d), "raise completes into aim");

            state.NotifyShot(1d, 6d);
            Check.Equal(36, state.Tick(1d), "fire frame 1 (ignite)");
            Check.Equal(36, state.Tick(1.049d), "fire hold 1");
            Check.Equal(37, state.Tick(1.05d), "fire frame 2 (jet)");
            Check.Equal(38, state.Tick(1.1d), "fire frame 3 (lengthen)");
            Check.Equal(39, state.Tick(1.15d), "fire frame 4 (contract)");
            Check.Equal(40, state.Tick(1.2d), "fire frame 5 (residual)");
            Check.Equal(41, state.Tick(1.3d), "reload begins after fire (0.25s)");
            Check.Equal(51, state.Tick(2.5d), "reload near end");
            Check.Equal(35, state.Tick(2.7d), "completed reload holds aim");
            Check.Equal(35, state.Tick(5.0d), "aim holds until the next shot");
            Check.Equal(MusketeerAction.Aim, state.CurrentAction, "action is aim");
        }

        private static void ReloadWindowScaling()
        {
            var state = new MusketeerAnimationState();
            state.SetMotion(MusketeerMotion.Idle, 0d, 0d, 0.5d, 0d);
            state.NotifyShot(0d, 0.75d);   // window = 0.5s → 1.4s 的装填被压缩
            Check.Equal(42, state.Tick(0.3d), "compressed reload first frames");
            Check.Equal(46, state.Tick(0.5d), "compressed reload mid frame");
            Check.Equal(35, state.Tick(0.76d), "window elapsed → aim");
        }

        private static void ZeroWindowSkipsReload()
        {
            var state = new MusketeerAnimationState();
            state.SetMotion(MusketeerMotion.Idle, 0d, 0d, 0.5d, 0d);
            state.NotifyShot(0d, 0.25d);   // window = 0
            Check.Equal(35, state.Tick(0.26d), "no reload window → straight to aim");
            Check.Equal(MusketeerAction.Aim, state.CurrentAction, "action is aim");
        }

        private static void MovementInterrupts()
        {
            var state = new MusketeerAnimationState();
            state.SetMotion(MusketeerMotion.Idle, 0d, 0d, 0.5d, 0d);
            state.SetAiming(true, 0d);
            Check.Equal(28, state.Tick(0d), "raise");
            // 走动打断举枪表现
            state.SetMotion(MusketeerMotion.Walk, 0.05d, 0d, 0.5d, 0.05d);
            Check.Equal(12, state.Tick(0.05d), "walk takes over");
            // 确认真实射击的 Fire 不被走动打断
            state.NotifyShot(0.1d, 4d);
            Check.Equal(36, state.Tick(0.1d), "fire shows while moving");
            state.SetMotion(MusketeerMotion.Walk, 0.2d, 0.5d, 0.5d, 0.1d);
            Check.Equal(38, state.Tick(0.2d), "fire continues through movement");
            int afterFire = state.Tick(0.4d);
            Check.True(afterFire >= 12 && afterFire <= 19, "fire ends back into locomotion while moving");
        }

        private static void AimResumesAfterMovement()
        {
            var state = new MusketeerAnimationState();
            state.SetMotion(MusketeerMotion.Idle, 0d, 0d, 0.5d, 0d);
            state.SetAiming(true, 0d);
            Check.Equal(28, state.Tick(0d), "raise from idle");
            state.SetMotion(MusketeerMotion.Walk, 0.1d, 0.1d, 0.5d, 0.1d);
            Check.Equal(12, state.Tick(0.1d), "walk interrupts the stance");
            state.SetMotion(MusketeerMotion.Idle, 0.2d, 0.1d, 0.5d, 0.1d);
            Check.Equal(28, state.Tick(0.2d), "stance resumes once standing again");
        }

        private static void MidWalkAcquisition()
        {
            var state = new MusketeerAnimationState();
            state.SetMotion(MusketeerMotion.Walk, 0d, 0d, 0.5d, 0d);
            state.SetAiming(true, 0d);                      // 走路中获得目标
            Check.Equal(12, state.Tick(0d), "walk frames stay while moving");
            state.SetMotion(MusketeerMotion.Walk, 0.1d, 0.1d, 0.5d, 0.1d);
            int frame = state.Tick(0.1d);
            Check.True(frame >= 12 && frame <= 19, "still locomotion, no frozen stance pose");
            state.SetMotion(MusketeerMotion.Idle, 0.2d, 0.1d, 0.5d, 0.1d);
            Check.Equal(28, state.Tick(0.2d), "the stance only starts once standing");
        }

        private static void InvalidSamplingOutranksGun()
        {
            var state = new MusketeerAnimationState();
            state.SetMotion(MusketeerMotion.Idle, 0d, 0d, 0.5d, 0d);
            state.NotifyShot(0d, 5d);
            Check.Equal(36, state.Tick(0d), "fire starts");

            // 非法采样（Walk 但没有 clip 长度）优先于 Fire：挂起，归还原生。
            state.SetMotion(MusketeerMotion.Walk, 0.05d, 0.2d, 0d, 0.05d);
            Check.Equal(-1, state.Tick(0.05d), "invalid sampling suspends even while firing");

            // 未知原生状态（Ghost Die/Spawn 等）同样优先。
            state.SetMotion(MusketeerMotion.Idle, 0.06d, 0d, 0.5d, 0d);
            Check.True(state.Tick(0.06d) >= 0, "recovers on a valid sample");
            state.SetMotion(MusketeerMotion.Unknown, 0.07d, 0d, 0d, 0d);
            Check.Equal(-1, state.Tick(0.07d), "unknown native state suspends even while firing");
        }

        private static void LocomotionFrames()
        {
            var state = new MusketeerAnimationState();
            state.SetMotion(MusketeerMotion.Walk, 0d, 0d, 1.0d, 0d);
            Check.Equal(12, state.Tick(0d), "walk frame 0");
            state.SetMotion(MusketeerMotion.Walk, 0.25d, 0.25d, 1.0d, 0.25d);
            Check.Equal(14, state.Tick(0.25d), "walk frame 2 at 0.25s of native clip");
            state.SetMotion(MusketeerMotion.Run, 0.3d, 0d, 1.0d, 0.3d);
            Check.Equal(20, state.Tick(0.3d), "run entry resets the sampling origin");
            state.SetMotion(MusketeerMotion.Run, 0.375d, 0.375d, 1.0d, 0.075d);
            Check.Equal(24, state.Tick(0.375d), "run frame 4 at 0.375s");
            state.SetMotion(MusketeerMotion.Run, 0.4d, 3.7d, 1.0d, 0.1d);
            Check.Equal(27, state.Tick(0.4d), "unwrapped nt=3.7 wraps inside the run gait");
            // 非法采样（无 clip 长度）→ 挂起
            state.SetMotion(MusketeerMotion.Walk, 0.5d, 0.2d, 0d, 0.1d);
            Check.Equal(-1, state.Tick(0.5d), "invalid clip length suspends");
        }
    }
}
