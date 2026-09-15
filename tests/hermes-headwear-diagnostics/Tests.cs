using System;
using KingdomEnhancedMod;

/// <summary>
/// 诊断契约回归：额度/快照上限、只读与零物化、归因（含销毁与池复用）、miss 语义、异常吞掉。
/// </summary>
internal static class Tests
{
    internal static void Run()
    {
        Program.Test("headwear budget admits eight samples then refuses the ninth without native reads", HeadwearBudget);
        Program.Test("miss budget is four and never consumes the headwear quota", MissBudget);
        Program.Test("headwear quota exhausted still admits miss samples up to twelve objects", ObjectCap);
        Program.Test("repeat Init on the same generation is not sampled twice", RepeatDecision);
        Program.Test("visual snapshots are capped at two per sample and later ticks read nothing", VisualCap);
        Program.Test("visual is refused for unsampled objects without touching renderer properties", VisualOnlyForSampled);
        Program.Test("visual line carries the compact field set and tolerates null own/head", VisualFields);
        Program.Test("field read failures degrade to unknown and keep the rest of the line", FieldFailures);
        Program.Test("diagnostics never write and never read the instantiating material property", ReadOnly);
        Program.Test("removed logs one line per sampled object and attributes destroyed wrappers", RemovedDestroyed);
        Program.Test("removed stays silent for unsampled objects and caps unattributed lines", RemovedUnattributed);
        Program.Test("decision reflects a chance miss and a disabled host as normal results", DecisionSemantics);
        Program.Test("null, throwing natives and a failing log sink never break the caller", NullAndThrowing);
        Program.Test("pool reuse on the same InstanceID samples anew and keeps the old pointer attributable", PoolReuse);
        Program.Test("a new life on the identical native identity is sampled again after removal", SameIdentityRevival);
        Program.Test("revived lives still count against the hard sample budget", RevivedLivesRespectBudget);
    }

    private static void HeadwearBudget()
    {
        for (int i = 0; i < 8; i++) HermesHeadwearDiagnostics.Decision(Unit.Make().Troll, i, true);
        Program.Eq(8, Program.Log.Count, "eight headwear samples recorded");
        Program.Check(Program.Contains(Program.Last(), "headwear=1"), "headwear flag");
        Program.Check(Program.Contains(Program.Last(), "hw=8/8"), "budget field");

        Unit extra = Unit.Make();
        int reads = Counter.NativeReads;
        HermesHeadwearDiagnostics.Decision(extra.Troll, 9, true);
        Program.Eq(8, Program.Log.Count, "ninth headwear sample refused");
        Program.Eq(reads, Counter.NativeReads, "refused sample reads nothing native");
    }

    private static void MissBudget()
    {
        for (int i = 0; i < 4; i++) HermesHeadwearDiagnostics.Decision(Unit.Make().Troll, -1, true);
        Program.Eq(4, Program.Log.Count, "four miss samples recorded");
        Program.Check(Program.Contains(Program.Last(), "kind=quota-miss"), "miss kind recorded as quota miss");

        HermesHeadwearDiagnostics.Decision(Unit.Make().Troll, -1, true);
        Program.Eq(4, Program.Log.Count, "fifth miss refused");

        for (int i = 0; i < 8; i++) HermesHeadwearDiagnostics.Decision(Unit.Make().Troll, i, true);
        Program.Eq(12, Program.Log.Count, "miss samples never consumed the headwear quota");
        Program.Check(Program.Contains(Program.Last(), "hw=8/8"), "headwear quota fully used");
    }

    private static void ObjectCap()
    {
        for (int i = 0; i < 8; i++) HermesHeadwearDiagnostics.Decision(Unit.Make().Troll, i, true);
        for (int i = 0; i < 4; i++) HermesHeadwearDiagnostics.Decision(Unit.Make().Troll, -1, true);
        Program.Eq(12, Program.Log.Count, "twelve objects sampled");

        Unit extra = Unit.Make();
        int reads = Counter.NativeReads;
        HermesHeadwearDiagnostics.Decision(extra.Troll, 1, true);
        Program.Eq(12, Program.Log.Count, "thirteenth sample refused");
        Program.Eq(reads, Counter.NativeReads, "refused sample reads nothing native");
    }

    private static void RepeatDecision()
    {
        Unit unit = Unit.Make();
        HermesHeadwearDiagnostics.Decision(unit.Troll, 3, true);
        HermesHeadwearDiagnostics.Decision(unit.Troll, 3, true);
        HermesHeadwearDiagnostics.Decision(unit.Troll, 3, false);
        Program.Eq(1, Program.Log.Count, "one line per generation");
    }

    private static void VisualCap()
    {
        Unit unit = Unit.Make();
        HermesHeadwearDiagnostics.Decision(unit.Troll, 4, true);
        for (int i = 0; i < 6; i++)
        {
            HermesHeadwearDiagnostics.Visual(unit.Troll, 4, i % 2 == 0, unit.Own, unit.Head, i == 0 ? "apply" : "retry");
        }

        Program.Eq(3, Program.Log.Count, "decision + exactly two snapshots");
        Program.Check(Program.Contains(Program.Line(1), "snap=1/2"), "first snapshot marked 1/2");
        Program.Check(Program.Contains(Program.Line(2), "snap=2/2"), "second snapshot marked 2/2");

        int reads = Counter.NativeReads;
        for (int i = 0; i < 8; i++) HermesHeadwearDiagnostics.Visual(unit.Troll, 4, true, unit.Own, unit.Head, "tick");
        Program.Eq(3, Program.Log.Count, "further ticks are not logged");
        Program.Eq(reads, Counter.NativeReads, "further ticks exit before any native read");
    }

    private static void VisualOnlyForSampled()
    {
        Unit unit = Unit.Make();
        HermesHeadwearDiagnostics.Visual(unit.Troll, 3, true, unit.Own, unit.Head, "apply");
        Program.Eq(0, Program.Log.Count, "unsampled object is silent");
        Program.Eq(0, Counter.RendererReads, "no renderer property reads");
        Program.Eq(0, Counter.NativeReads, "no identity reads");
    }

    private static void VisualFields()
    {
        Unit unit = Unit.Make();
        HermesHeadwearDiagnostics.Decision(unit.Troll, 4, true);
        HermesHeadwearDiagnostics.Visual(unit.Troll, 4, true, unit.Own, unit.Head, "apply");

        string line = Program.Last();
        Program.Check(line.StartsWith("[HermesHeadwearDiag] visual", StringComparison.Ordinal), "prefix and event");
        Program.Check(Program.Contains(line, "choice=4") && Program.Contains(line, "phase=apply") && Program.Contains(line, "applied=1"), "header fields");
        Program.Check(Program.Contains(Program.Block(line, "own"), "sprite=troll_masks_0"), "own renderer block");
        Program.Check(Program.Contains(Program.Block(line, "body"), "sprite=Troll_friendly"), "body renderer block");
        Program.Check(Program.Contains(Program.Block(line, "mask"), "sprite=troll_masks_0"), "native mask block");
        Program.Check(Program.Contains(Program.Block(line, "tmpl"), "sprite=troll_masks_0"), "template block");
        Program.Check(Program.Contains(line, "mat=Pow-Diffuse-Snow/Sprites/Default"), "material and shader names");
        Program.Check(Program.Contains(line, "rgba=1.00,1.00,1.00,1.00"), "color rgba");
        Program.Check(Program.Contains(line, "act=1/1"), "active self/in-hierarchy");
        Program.Check(Program.Contains(line, "layer=3"), "game object layer");
        Program.Check(Program.Contains(line, "srt=0/1"), "sorting layer/order");
        Program.Check(Program.Contains(line, "en=1"), "renderer enabled");
        Program.Check(Program.Contains(line, "pos=0.000,0.000,0.000"), "world position");
        Program.Check(Program.Contains(line, "scale=1.000,1.000,1.000"), "lossy scale");
        Program.Check(Program.Contains(line, "bnd=c0.000,0.000,0.000/s1.000,1.000,0.000"), "bounds center and size");
        Program.Check(Program.Contains(line, "head[act=1/1"), "head block");

        HermesHeadwearDiagnostics.Visual(unit.Troll, 4, false, null, null, "retry");
        line = Program.Last();
        Program.Check(Program.Contains(line, "own=null") && Program.Contains(line, "head=null"), "null own/head tolerated");
        Program.Check(Program.Contains(line, "applied=0"), "applied flag");
    }

    private static void FieldFailures()
    {
        Unit unit = Unit.Make();
        unit.Own.ThrowOnSprite = true;
        unit.Own.ThrowOnColor = true;
        unit.Own.ThrowOnMaterial = true;
        unit.Own.ThrowOnBounds = true;

        HermesHeadwearDiagnostics.Decision(unit.Troll, 2, true);
        HermesHeadwearDiagnostics.Visual(unit.Troll, 2, true, unit.Own, unit.Head, "apply");
        string line = Program.Last();
        Program.Check(Program.Contains(line, "sprite=unknown"), "failed sprite marked unknown");
        Program.Check(Program.Contains(line, "rgba=unknown"), "failed color marked unknown");
        Program.Check(Program.Contains(line, "mat=unknown"), "failed material marked unknown");
        Program.Check(Program.Contains(line, "bnd=unknown"), "failed bounds marked unknown");
        Program.Check(Program.Contains(line, "own[en=1"), "other own fields still read");
        Program.Check(Program.Contains(line, "sprite=Troll_friendly"), "body unaffected");
    }

    private static void ReadOnly()
    {
        Unit unit = Unit.Make();
        HermesHeadwearDiagnostics.Decision(unit.Troll, 2, true);
        HermesHeadwearDiagnostics.Visual(unit.Troll, 2, true, unit.Own, unit.Head, "apply");
        HermesHeadwearDiagnostics.Visual(unit.Troll, 2, false, unit.Own, unit.Head, "retry");
        HermesHeadwearDiagnostics.Removed(unit.Troll, 2, "pool");

        Program.Eq(0, Counter.Writes, "no renderer writes");
        Program.Eq(0, Counter.MaterialAccesses, "instantiating material property never read");
    }

    private static void RemovedDestroyed()
    {
        Unit unit = Unit.Make();
        HermesHeadwearDiagnostics.Decision(unit.Troll, 5, true);
        unit.Troll.Destroyed = true;

        HermesHeadwearDiagnostics.Removed(unit.Troll, 5, "reset");
        Program.Eq(2, Program.Log.Count, "destroyed wrapper attributed by stored pointer");
        string line = Program.Last();
        Program.Check(Program.Contains(line, "removed") && Program.Contains(line, "reason=reset"), "reason recorded");
        Program.Check(Program.Contains(line, "choice=5") && Program.Contains(line, "nativeMaskIndex=2"), "stored fields reused");
        Program.Check(Program.Contains(line, "visuals=0/2"), "visual budget recorded");

        HermesHeadwearDiagnostics.Removed(unit.Troll, 5, "reset");
        Program.Eq(2, Program.Log.Count, "one removed line per sample");

        HermesHeadwearDiagnostics.Visual(unit.Troll, 5, true, unit.Own, unit.Head, "tick");
        Program.Eq(2, Program.Log.Count, "no snapshots after removal");
    }

    private static void RemovedUnattributed()
    {
        Unit unit = Unit.Make();
        HermesHeadwearDiagnostics.Decision(unit.Troll, 1, true);
        HermesHeadwearDiagnostics.Removed(Unit.Make().Troll, 1, "pool");
        Program.Eq(1, Program.Log.Count, "unsampled object stays silent");

        for (int i = 0; i < 6; i++) HermesHeadwearDiagnostics.Removed(null, -1, "world");
        Program.Eq(5, Program.Log.Count, "unattributed removals capped at four");
        Program.Check(Program.Contains(Program.Last(), "removed id=unknown") && Program.Contains(Program.Last(), "reason=world"), "minimal unattributed line");
    }

    private static void DecisionSemantics()
    {
        HermesHeadwearDiagnostics.Decision(Unit.Make().Troll, -1, true);
        Program.Check(Program.Contains(Program.Last(), "headwear=0") && Program.Contains(Program.Last(), "kind=quota-miss"), "chance miss is a normal result");

        HermesHeadwearDiagnostics.Decision(Unit.Make().Troll, -1, false);
        Program.Check(Program.Contains(Program.Last(), "host=0") && Program.Contains(Program.Last(), "kind=host-disabled"), "host disabled recorded");

        Unit unit = Unit.Make();
        unit.Troll.ThrowOnMaskIndex = true;
        HermesHeadwearDiagnostics.Decision(unit.Troll, 6, true);
        Program.Check(Program.Contains(Program.Last(), "nativeMaskIndex=unknown"), "unreadable native index marked unknown");
    }

    private static void NullAndThrowing()
    {
        HermesHeadwearDiagnostics.Decision(null, 3, true);
        HermesHeadwearDiagnostics.Visual(null, 3, true, null, null, "apply");
        Program.Eq(0, Program.Log.Count, "null inputs are silent");

        Unit unit = Unit.Make();
        HermesHeadwearDiagnostics.Decision(unit.Troll, 3, true);
        unit.Troll.ThrowOnGameObject = true;
        HermesHeadwearDiagnostics.Visual(unit.Troll, 3, true, unit.Own, unit.Head, "apply");
        Program.Eq(2, Program.Log.Count, "throwing gameObject still logs via wrapper pointer");

        KingdomEnhancedPlugin.Instance.LogSource.OnInfo = _ => throw new InvalidOperationException("sink");
        HermesHeadwearDiagnostics.Decision(Unit.Make().Troll, 1, true);
        Program.Eq(2, Program.Log.Count, "failing sink leaves no line");
        KingdomEnhancedPlugin.Instance.LogSource.OnInfo = null;
        HermesHeadwearDiagnostics.Decision(Unit.Make().Troll, 2, true);
        Program.Eq(3, Program.Log.Count, "sink failure swallowed, later diagnostics continue");
    }

    private static void PoolReuse()
    {
        Unit first = Unit.Make();
        HermesHeadwearDiagnostics.Decision(first.Troll, 7, true);

        Unit second = Unit.Make();
        second.Go.Id = first.Go.Id; // 同一 InstanceID、新世代（新指针）
        HermesHeadwearDiagnostics.Decision(second.Troll, 8, true);
        Program.Eq(2, Program.Log.Count, "same InstanceID with a new pointer is a new sample");

        HermesHeadwearDiagnostics.Removed(first.Troll, 7, "pool");
        Program.Eq(3, Program.Log.Count, "old generation still attributable by its own pointer");
        Program.Check(Program.Contains(Program.Last(), "choice=7"), "removed line belongs to the old sample");
    }

    private static void SameIdentityRevival()
    {
        // 原生池复用完全相同的身份（GOid + GameObject 指针 + 组件指针都不变）
        Unit unit = Unit.Make();
        HermesHeadwearDiagnostics.Decision(unit.Troll, -1, true);
        HermesHeadwearDiagnostics.Removed(unit.Troll, -1, "pool");

        HermesHeadwearDiagnostics.Decision(unit.Troll, 9, true);
        Program.Eq(3, Program.Log.Count, "new life after removal is sampled again");
        Program.Check(Program.Contains(Program.Line(2), "choice=9") && Program.Contains(Program.Line(2), "headwear=1"), "new life is a headwear sample, not swallowed by the old miss");

        HermesHeadwearDiagnostics.Visual(unit.Troll, 9, true, unit.Own, unit.Head, "apply");
        Program.Eq(4, Program.Log.Count, "new life gets its own visual budget");
        Program.Check(Program.Contains(Program.Last(), "choice=9") && Program.Contains(Program.Last(), "snap=1/2"), "visual matches the newest generation");

        HermesHeadwearDiagnostics.Removed(unit.Troll, 9, "pool");
        Program.Eq(5, Program.Log.Count, "new life removal is separate");
        Program.Check(Program.Contains(Program.Last(), "choice=9") && Program.Contains(Program.Last(), "visuals=1/2"), "removal belongs to the new life");
    }

    private static void RevivedLivesRespectBudget()
    {
        Unit unit = Unit.Make();
        for (int life = 0; life < 4; life++)
        {
            HermesHeadwearDiagnostics.Decision(unit.Troll, -1, true);
            HermesHeadwearDiagnostics.Removed(unit.Troll, -1, "pool");
        }

        Program.Eq(8, Program.Log.Count, "four lives each sampled once");
        HermesHeadwearDiagnostics.Decision(unit.Troll, -1, true);
        Program.Eq(8, Program.Log.Count, "fifth life refused: miss budget is a hard cap across lives");
    }
}
