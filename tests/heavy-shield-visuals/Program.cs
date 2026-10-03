using System.Reflection;
using KingdomEnhancedMod;

int assertions = 0;
void Check(bool ok, string label) { assertions++; if (!ok) throw new Exception(label); }
HeavyShieldSequence Clip(string name)
{
    Check(HeavyShieldArtLayout.TryGetSequence(HeavyShieldAtlasId.Soldier, name, out var seq), name + " exists");
    return seq;
}
void Frame(HeavyShieldPoseMachine p, string name, int offset = 0)
{
    var seq = Clip(name);
    Check(p.TryFrame(out int actual) && actual == seq.First + offset, $"{name}/{offset}: got {actual}");
}
void Advance(HeavyShieldPoseMachine p, float seconds)
{ while (seconds > 0f) { float step = Math.Min(.125f, seconds); p.Tick(step); seconds -= step; } }

var stances = new (HeavyShieldStance Stance, string Name)[] {
    (HeavyShieldStance.BackIdle,"back_idle"), (HeavyShieldStance.BackWalk,"back_walk"),
    (HeavyShieldStance.BackRun,"back_run"), (HeavyShieldStance.GuardIdle,"guard_idle"), (HeavyShieldStance.Advance,"defense_advance") };
var actions = new (HeavyShieldAction Action, string Name)[] {
    (HeavyShieldAction.Equip,"equip"), (HeavyShieldAction.Stow,"stow"), (HeavyShieldAction.Block,"block"), (HeavyShieldAction.Bash,"bash"),
    (HeavyShieldAction.WalkStart,"walk_start"), (HeavyShieldAction.WalkStartAlt,"walk_start_alt"),
    (HeavyShieldAction.WalkStop,"walk_stop"), (HeavyShieldAction.WalkStopAlt,"walk_stop_alt"),
    (HeavyShieldAction.RunStart,"run_start"), (HeavyShieldAction.RunStop,"run_stop"), (HeavyShieldAction.Relax,"relax_idle"),
    (HeavyShieldAction.RestEnter,"rest_enter"), (HeavyShieldAction.Rest,"rest_idle"), (HeavyShieldAction.RestExit,"rest_exit") };
string Prefix(HeavyShieldWear wear) => wear switch { HeavyShieldWear.Worn => "worn_", HeavyShieldWear.Critical => "critical_", HeavyShieldWear.Half => "half_", _ => "" };

var capped = new HeavyShieldPoseMachine(); capped.SetStance(HeavyShieldStance.BackWalk); capped.Tick(.34f);
Check(capped.Elapsed == .25f, "large delta keeps original 0.25 clock cap"); Frame(capped, "back_walk", 2);
capped.Tick(-1f); Check(capped.Elapsed == .25f, "negative delta cannot rewind phase");
capped.SetWear(HeavyShieldWear.Half); Check(capped.Elapsed == .25f, "changed wear preserves movement phase"); Frame(capped, "half_back_walk", 2);

// Exact production clip selection and frame clocks for every one of the final 19x4 wear clips.
foreach (var wear in Enum.GetValues<HeavyShieldWear>())
{
    foreach (var row in stances)
    {
        var p = new HeavyShieldPoseMachine(); p.SetWear(wear); p.SetStance(row.Stance);
        string name = Prefix(wear) + row.Name; var seq = Clip(name); Frame(p, name);
        p.Tick(.125f); int offset = (int)Math.Floor(.125 * seq.Fps); Frame(p, name, offset);
        float elapsed = p.Elapsed; p.SetStance(row.Stance); p.SetWear(wear);
        Check(p.Elapsed == elapsed, name + " repeated stance/wear keeps phase");
        p.Tick(0f); p.Tick(float.NaN); p.Tick(float.PositiveInfinity); Check(p.Elapsed == elapsed, name + " paused/invalid dt keeps phase");
        Advance(p, 2.5f);
        Frame(p, name, (int)Math.Floor((double)p.Elapsed * seq.Fps) % seq.Count);
    }
    foreach (var row in actions)
    {
        var p = new HeavyShieldPoseMachine(); p.SetWear(wear); p.SetStance(HeavyShieldStance.GuardIdle);
        string name = Prefix(wear) + row.Name; var seq = Clip(name);
        Check(p.Trigger(row.Action), name + " event accepted"); Frame(p, name);
        float preEnd = Math.Min(.125f, seq.Count / seq.Fps - .001f); p.Tick(preEnd);
        Frame(p, name, (int)Math.Floor((double)p.Elapsed * seq.Fps));
        float phase = p.Elapsed; p.SetWear(wear); Check(p.Elapsed == phase, name + " wear update keeps action phase");
        Advance(p, seq.Count / seq.Fps + .01f);
        if (seq.Loop) Check(p.Action == row.Action, name + " loop retained");
        else if (row.Action == HeavyShieldAction.RestEnter) Check(p.Action == HeavyShieldAction.Rest, name + " enters rest loop");
        else Check(p.Action == HeavyShieldAction.None, name + " completes using manifest fps");
        if (row.Action == HeavyShieldAction.Equip) Check(p.Stance == HeavyShieldStance.GuardIdle, name + " finishes guard");
        if (row.Action == HeavyShieldAction.Stow) Check(p.Stance == HeavyShieldStance.BackIdle, name + " finishes back");
    }
    var broken = new HeavyShieldPoseMachine(); broken.SetWear(wear); Check(broken.Trigger(HeavyShieldAction.Break), "break starts");
    Frame(broken, "break"); Advance(broken, 2f); Frame(broken, "break", Clip("break").Count - 1);
    foreach (var row in actions) Check(!broken.Trigger(row.Action), $"terminal break refuses {wear}/{row.Action}");
    broken.SetPresentation(HeavyShieldStance.BackRun, false, true); Advance(broken, 4f);
    Check(broken.Action == HeavyShieldAction.Break, "motion/leisure never reopens broken life");
}

// Locomotion edges are events. Stable samples advance phase without replaying start clips.
var motion = new HeavyShieldPoseMachine();
motion.SetPresentation(HeavyShieldStance.BackWalk, false, true); Check(motion.Action == HeavyShieldAction.WalkStart, "idle to actual walk starts");
motion.Tick(.125f); motion.SetPresentation(HeavyShieldStance.BackWalk, false, true);
Check(motion.Elapsed == .125f, "steady actual walk does not restart");
Advance(motion, .5f); Check(motion.Action == HeavyShieldAction.None, "walk start finishes");
motion.SetPresentation(HeavyShieldStance.BackIdle, false, true); Check(motion.Action == HeavyShieldAction.WalkStop, "walk to actual idle brakes");
Advance(motion, .5f); motion.SetPresentation(HeavyShieldStance.BackWalk, false, true);
Check(motion.Action == HeavyShieldAction.WalkStartAlt, "next real walk edge uses alternate start");
motion.SetPresentation(HeavyShieldStance.BackRun, false, true); Check(motion.Action == HeavyShieldAction.RunStart, "walk to actual run starts");
motion.Tick(.125f); motion.SetPresentation(HeavyShieldStance.BackRun, false, true); Check(motion.Elapsed == .125f, "steady run keeps start phase");
Advance(motion, .25f); Frame(motion, "back_run", (int)Math.Floor((double)motion.Elapsed * Clip("back_run").Fps));
motion.SetPresentation(HeavyShieldStance.BackWalk, false, true); Check(motion.Action == HeavyShieldAction.RunStop, "run to walk brakes once");
Advance(motion, .5f); motion.SetPresentation(HeavyShieldStance.BackIdle, false, false);
Check(motion.Action == HeavyShieldAction.None, "unknown sample cancels motion transition"); Advance(motion, 10f);
Check(motion.Action == HeavyShieldAction.None, "unknown sample never starts leisure");

foreach (var stance in new[] { HeavyShieldStance.BackIdle, HeavyShieldStance.GuardIdle })
{
    var p = new HeavyShieldPoseMachine(); p.SetPresentation(stance, false, true);
    Advance(p, HeavyShieldPoseMachine.QuietDelay - .125f); Check(p.Action == HeavyShieldAction.None, "quiet delay required");
    p.Tick(.125f); Check(p.Action == (stance == HeavyShieldStance.BackIdle ? HeavyShieldAction.Relax : HeavyShieldAction.RestEnter), "quiet variant selected by shield stance");
    if (stance == HeavyShieldStance.GuardIdle)
    {
        Advance(p, .5f); Check(p.Action == HeavyShieldAction.Rest, "front enter completes into rest");
        Advance(p, HeavyShieldPoseMachine.RestDuration); Check(p.Action == HeavyShieldAction.RestExit, "rest calmly exits after bounded cycle");
        Advance(p, .5f); Check(p.Action == HeavyShieldAction.None, "quiet rest exit completes");
    }
    p.InterruptLeisure(); Advance(p, 5f); Check(p.Action == HeavyShieldAction.None, "threat interrupt clears quiet authorization");
    p.SetPresentation(stance, false, true); Advance(p, HeavyShieldPoseMachine.QuietDelay + .5f);
    p.SetPresentation(stance == HeavyShieldStance.BackIdle ? HeavyShieldStance.BackRun : HeavyShieldStance.Advance, false, true);
    Check(p.Action != HeavyShieldAction.Relax && p.Action != HeavyShieldAction.Rest && p.Action != HeavyShieldAction.RestEnter && p.Action != HeavyShieldAction.RestExit, "real movement immediately replaces leisure");
}
foreach (var leisure in new[] { HeavyShieldAction.Relax, HeavyShieldAction.RestEnter, HeavyShieldAction.Rest, HeavyShieldAction.RestExit })
foreach (var urgent in new[] { HeavyShieldAction.Equip, HeavyShieldAction.Stow, HeavyShieldAction.Block, HeavyShieldAction.Bash, HeavyShieldAction.Break })
{
    var p = new HeavyShieldPoseMachine(); p.Trigger(leisure); p.Tick(.125f); Check(p.Trigger(urgent) && p.Action == urgent && p.Elapsed == 0f, $"{urgent} immediately preempts {leisure}");
}

// Inject malformed timing into the linked table to verify the actual clock, then restore it.
var table = (HeavyShieldSequence[])typeof(HeavyShieldArtLayout).GetField("SoldierSequenceTable", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
int breakIndex = Array.FindIndex(table, s => s.Name == "break"); var original = table[breakIndex];
try
{
    foreach (float bad in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
    {
        table[breakIndex] = new("break", original.First, original.Count, bad, false);
        var p = new HeavyShieldPoseMachine(); p.Trigger(HeavyShieldAction.Break); p.Tick(.25f); Frame(p, "break", 3);
        p.Tick(.25f); Frame(p, "break", original.Count - 1);
        Check(HeavyShieldPoseMachine.SequenceDuration(table[breakIndex]) == original.Count / 12f, "invalid FPS gives finite terminal duration");
    }
}
finally { table[breakIndex] = original; }
Console.WriteLine($"HeavyShieldVisuals: {assertions} assertions passed");
