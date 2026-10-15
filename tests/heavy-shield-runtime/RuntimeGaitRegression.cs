using System.Reflection;
using KingdomEnhancedMod;
using UnityEngine;

// Presentation integration: move the real fixture carrier Transform, then execute
// the linked production Visual.Tick. No fake Animator/body speed is integrated.
internal static class RuntimeGaitRegression
{
    private static readonly System.Collections.IDictionary Visuals = (System.Collections.IDictionary)
        typeof(HeavyShieldActorVisuals).GetField("Active", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
    private static HeavyShieldPoseMachine Pose(Fixture f) => (HeavyShieldPoseMachine)
        Visuals[f.Handle.GoId].GetType().GetField("Pose", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(Visuals[f.Handle.GoId]);
    private static SpriteRenderer Own(Fixture f) => (SpriteRenderer)
        Visuals[f.Handle.GoId].GetType().GetField("Own", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(Visuals[f.Handle.GoId]);
    private static int AppliedFrame(Fixture f) => (int)
        Visuals[f.Handle.GoId].GetType().GetField("LastFrame", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(Visuals[f.Handle.GoId]);
    private static void Render(Fixture f, float dx = 0f, float dt = .05f, bool nextFrame = true)
    {
        Time.deltaTime = dt;
        Time.time += dt;
        if (nextFrame) { Time.frameCount++; Time.unscaledTimeAsDouble += dt; }
        Vector3 p = f.Go.transform.position;
        f.Go.transform.position = new(p.x + dx, p.y, p.z);
        HeavyShieldActorVisuals.Tick(f.Handle.GoId);
    }
    private static bool Near(double a, double b) => Math.Abs(a - b) < .00002;

    internal static void Run(Action<bool, string> check)
    {
        // First validate the test space: unlike the old independent world/local
        // fields, this stub supports actual translation and signed scales.
        var parent = new GameObject("gait-space").transform;
        parent.position = new(3, 2); parent.localScale = new(-2, .5f, 1);
        var child = new GameObject("gait-child").transform;
        child.SetParent(parent, false); child.localPosition = new(2, 4);
        check(child.position.x == -1 && child.position.y == 4, "gait fixture world equals TransformPoint(local)");
        child.position = new(5, 3);
        check(child.localPosition.x == -1 && child.localPosition.y == 2, "gait fixture world setter inverse projects");
        parent.position = new(4, 3);
        check(child.position.x == 6 && child.position.y == 4, "gait fixture parent motion carries world position");
        UnityEngine.Object.Destroy(child.gameObject); UnityEngine.Object.Destroy(parent.gameObject);

        // Exercise the real runtime presentation calls after phase is nonzero:
        // InterruptLeisure runs every moving tick and must not reset a moving loop.
        using (var f = new Fixture())
        {
            f.Go.transform.position = new(3.5f, 0);
            check(f.Attach(new(40, false, false)), "runtime moving gait attaches");
            var p = Pose(f); p.SetStance(HeavyShieldStance.BackWalk);
            f.Archer._animator.Speed = .6f;
            float step = (float)(HeavyShieldGaitClock.BackWalkStrideWorld * .1);
            Render(f); Render(f, step);
            check(Near(p.Gait.Phase, .1), "runtime moving regression begins with nonzero phase");
            for (int i = 0; i < 3; i++)
            {
                HeavyShieldRuntime.Tick();
                Render(f, step);
                check(p.Stance == HeavyShieldStance.BackWalk && p.Action == HeavyShieldAction.None
                    && Near(p.Gait.Phase, .2 + i * .1),
                    "real Runtime.Tick repeated moving presentation preserves stride " + i);
            }
        }

        var clips = new[] {
            (HeavyShieldStance.BackWalk, "back_walk", HeavyShieldGaitClock.BackWalkStrideWorld),
            (HeavyShieldStance.BackRun, "back_run", HeavyShieldGaitClock.BackRunStrideWorld),
            (HeavyShieldStance.Advance, "defense_advance", HeavyShieldGaitClock.AdvanceStrideWorld) };
        foreach (var row in clips)
        using (var f = new Fixture())
        {
            check(f.Attach(new(40, false, false)), "gait fixture attaches " + row.Item2);
            var p = Pose(f); p.SetStance(row.Item1);
            int commands = f.Archer._mover.CommandWrites;
            float scaleX = f.Go.transform.localScale.x;
            Render(f); Render(f, (float)(row.Item3 * .3));
            check(Near(p.Gait.Phase, .3), "actual carrier movement drives " + row.Item2);
            HeavyShieldArtLayout.TryGetSequence(HeavyShieldAtlasId.Soldier, row.Item2, out var seq);
            check(AppliedFrame(f) == seq.First + (int)Math.Floor(.3 * seq.Count), "distance frame reaches actual renderer " + row.Item2);
            double phase = p.Gait.Phase; int frame = AppliedFrame(f);
            // Commands still claim movement; only the stationary carrier is evidence.
            f.Archer._animator.Speed = 2;
            f.Archer._mover.ActualSpeedValue = 2;
            Render(f); Render(f);
            check(p.Gait.Phase == phase && AppliedFrame(f) == frame, "blocked carrier freezes loop despite claimed speed");
            HeavyShieldActorVisuals.SetWear(f.Archer, f.Handle.Life, HeavyShieldWear.Worn);
            Render(f);
            check(p.Gait.Phase == phase, "wear change preserves contact phase");
            foreach (var next in clips)
            {
                p.SetStance(next.Item1); Render(f);
                HeavyShieldArtLayout.TryGetSequence(HeavyShieldAtlasId.Soldier, "worn_" + next.Item2, out var target);
                check(p.Gait.Phase == phase && AppliedFrame(f) == target.First + (int)Math.Floor(phase * target.Count),
                    "real Tick maps canonical contact across six/eight frame clips " + next.Item2);
            }
            check(f.Archer._mover.CommandWrites == commands && f.Go.transform.localScale.x == scaleX,
                "gait writes neither Mover commands nor native scale");
        }

        using (var f = new Fixture())
        {
            check(f.Attach(new(40, false, false)), "duplicate/transport gait fixture");
            var p = Pose(f); p.SetStance(HeavyShieldStance.BackWalk);
            float tenth = (float)(HeavyShieldGaitClock.BackWalkStrideWorld * .1);
            Render(f); Render(f, tenth);
            double phase = p.Gait.Phase;
            Render(f, tenth, nextFrame: false);
            check(p.Gait.Phase == phase && Own(f).transform.position.x == f.Go.transform.position.x,
                "same frame consumes position without double stepping while anchor follows");
            Render(f, tenth);
            check(Near(p.Gait.Phase, phase + .1), "same-frame movement is not deferred into next frame");

            var movingParent = new GameObject("gait-parent").transform;
            movingParent.SetParent(Managers.Inst.world.gameLayer, false);
            movingParent.localScale = new(-2, .5f, 1);
            f.Go.transform.SetParent(movingParent, false);
            phase = p.Gait.Phase; Render(f);
            check(p.Gait.Phase == phase, "new parent discards cross-space span");
            movingParent.position = new(.4f, .2f);
            Render(f);
            check(p.Gait.Phase == phase, "parent-only movement is not a footstep");
            var local = f.Go.transform.localPosition;
            f.Go.transform.localPosition = new(local.x + tenth * .5f, local.y, local.z);
            Render(f);
            check(Near(p.Gait.Phase, phase - .1), "signed non-unit parent projects local stride into world units");
            phase = p.Gait.Phase;
            HeavyShieldActorVisuals.SetFacing(f.Archer, f.Handle.Life, -1);
            Render(f, -tenth);
            check(p.Gait.Phase == phase, "facing edge seeds instead of reinterpreting prior displacement");
            Render(f, -tenth);
            check(Near(p.Gait.Phase, phase + .1), "left-facing forward movement advances canonical phase");
            f.Go.transform.SetParent(Managers.Inst.world.gameLayer, true);
            UnityEngine.Object.Destroy(movingParent.gameObject);
        }

        using (var f = new Fixture())
        {
            check(f.Attach(new(40, false, false)), "pause/event gait fixture");
            var p = Pose(f); p.SetStance(HeavyShieldStance.BackWalk);
            float step = (float)(HeavyShieldGaitClock.BackWalkStrideWorld * .1);
            Render(f); Render(f, step); double phase = p.Gait.Phase;
            float seconds = p.Elapsed;
            Time.timeScale = 0; Render(f, .4f);
            check(p.Gait.Phase == phase && p.Elapsed == seconds && !p.Gait.HasBaseline, "paused positive callback dt clears gait baseline and freezes clocks");
            Time.timeScale = 1; Render(f);
            check(p.Gait.Phase == phase, "resume consumes paused displacement");
            Render(f, step); check(Near(p.Gait.Phase, phase + .1), "post-resume actual stride advances");

            check(p.Trigger(HeavyShieldAction.WalkStart), "inter-sample event starts");
            p.SetPresentation(HeavyShieldStance.BackWalk, false, false);
            Render(f, .4f);
            check(p.Action == HeavyShieldAction.None && p.Gait.Phase == 0, "trigger and cancellation between samples cannot retain old stride debt");
            Render(f, step); check(Near(p.Gait.Phase, .1), "fresh movement after cancellation resumes");

            p.Trigger(HeavyShieldAction.Bash);
            for (int i = 0; i < 6; i++) Render(f, .05f, .125f);
            check(p.Action == HeavyShieldAction.None && p.Gait.Phase == 0, "event completes with original seconds and no movement debt");
            Render(f, .2f);
            check(p.Gait.Phase == 0, "first loop sample after action completion seeds");
            Render(f, step); check(Near(p.Gait.Phase, .1), "next loop sample consumes only new movement");

            phase = p.Gait.Phase;
            Render(f, .2f, .3f);
            check(p.Gait.Phase == phase, "large unscaled gap discards whole movement span");
            Render(f, step); check(Near(p.Gait.Phase, phase + .1), "movement resumes from large-gap baseline");
            phase = p.Gait.Phase;
            Render(f, 1.1f);
            check(p.Gait.Phase == phase, "teleport-sized span is not clamped into a step");
            Render(f, step); check(Near(p.Gait.Phase, phase + .1), "teleport baseline carries no deferred debt");
            phase = p.Gait.Phase;
            check(HeavyShieldActorVisuals.Register(f.Archer, f.Handle.Life) && p.Gait.Phase == phase,
                "same-life registration remains idempotent");
            check(HeavyShieldActorVisuals.Unregister(f.Archer, f.Handle.Life), "old visual unregisters");
            check(HeavyShieldActorVisuals.Register(f.Archer, f.Handle.Life) && Pose(f).Gait.Phase == 0 && !Pose(f).Gait.HasBaseline,
                "new visual starts from zero without prior baseline");
        }

        using (var f = new Fixture())
        {
            check(f.Attach(new(40, false, false)), "suspended gait fixture");
            var p = Pose(f); p.SetStance(HeavyShieldStance.BackWalk);
            Render(f); Render(f, .1f);
            check(p.Gait.HasBaseline && p.Gait.Phase > 0, "suspend begins with a live stride baseline");
            double phase = p.Gait.Phase;
            HeavyShieldActorVisuals.Suspend(f.Archer, f.Handle.Life);
            Render(f, .2f);
            check(!p.Gait.HasBaseline && p.Gait.Phase == phase && !Own(f).enabled,
                "suspend discards baseline and cannot consume later movement");
        }

        using (var f = new Fixture())
        {
            check(f.Attach(new(40, false, false)), "pending visibility return gait fixture");
            var p = Pose(f); p.SetStance(HeavyShieldStance.BackWalk);
            Render(f); Render(f, .1f);
            check(p.Gait.HasBaseline, "pending return begins with a live stride baseline");
            var world = Managers.Inst.world.gameLayer;
            var foreign = new GameObject("foreign gait return world").transform;
            try
            {
                Managers.Inst.world.gameLayer = foreign;
                check(!HeavyShieldActorVisuals.Unregister(f.Archer, f.Handle.Life)
                    && HeavyShieldActorVisuals.HasVisual(f.Archer, f.Handle.Life),
                    "foreign world retains the visibility return credential");
                check(!p.Gait.HasBaseline && !Own(f).enabled,
                    "retained failed-return credential has no consumable stride baseline");
            }
            finally { Managers.Inst.world.gameLayer = world; UnityEngine.Object.Destroy(foreign.gameObject); }
            check(HeavyShieldActorVisuals.Unregister(f.Archer, f.Handle.Life),
                "same context can finish pending visibility return");
        }

        using (var f = new Fixture())
        {
            check(f.Attach(new(40, false, false)), "driver disable gait fixture");
            var p = Pose(f); p.SetStance(HeavyShieldStance.BackWalk);
            Render(f); Render(f, .1f);
            check(p.Gait.HasBaseline, "driver disable begins with a live stride baseline");
            HeavyShieldActorVisuals.OnDriverDisabled(Own(f).gameObject.GetInstanceID());
            check(!p.Gait.HasBaseline && !HeavyShieldActorVisuals.HasVisual(f.Archer, f.Handle.Life),
                "driver disable returns visibility and invalidates old stride baseline");
            check(HeavyShieldActorVisuals.Register(f.Archer, f.Handle.Life)
                && Pose(f).Gait.Phase == 0 && !Pose(f).Gait.HasBaseline,
                "driver replacement cannot borrow a disabled visual's stride");
        }
    }
}
