using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Fixture.Model;
using BaselineShell = Fixture.Baseline.Shell;
using CandidateShell = Fixture.Candidate.Shell;

internal enum ShellKind { Baseline, Candidate }

internal sealed class PointSpec
{
    internal bool IsNull;
    internal bool PayableNull;
    internal bool Busy;
    internal bool Disabled;
    internal bool PayerNull;
    internal HeavyShieldPurchaseKind Kind;
    internal bool CanPayAllow;
    internal Box.Mutation Mutation;
}

internal sealed class Scenario
{
    internal bool NullArray;
    internal PointSpec[] Points = Array.Empty<PointSpec>();
    internal bool EconomyFault, Retiring, MenuSuspended, Saving, CarrierReady, BowValue;
    internal bool[] Quota = new bool[5];
    internal int ThrowBowAt, ThrowReserveAt, ThrowCanPayAt, ThrowSetAt;
    internal long Life = 4242;
}

internal sealed class Snapshot
{
    internal string Values = "";
    internal string Writes = "";
    internal string Attempts = "";
    internal string Reserves = "";
    internal string CanPays = "";
    internal string PayableReads = "";
    internal int BowCalls, AnyBusyCalls, ReserveCallCount, CanPayCallCount, WriteCount, AttemptCount;
    internal string Exception = "none";
}

internal sealed class SnapshotPair
{
    internal Snapshot B, C;
    internal SnapshotPair(Snapshot b, Snapshot c) { B = b; C = c; }
}

internal static class Program
{
    private static int Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "all";
        try
        {
            switch (mode)
            {
                case "reduce": return Reduce();
                case "edge": return Edge();
                case "compare": return Compare(args.Length > 1 ? int.Parse(args[1]) : 275,
                                               args.Length > 2 ? int.Parse(args[2]) : 3000);
                case "alloc": return Alloc();
                default:
                    Console.Error.WriteLine("usage: Host <reduce|edge|compare [seed] [cases]|alloc>");
                    return 2;
            }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("HARNESS ERROR: " + e);
            return 2;
        }
    }

    private static Scenario Idle(int n, bool bow = true)
    {
        var s = new Scenario { BowValue = bow, CarrierReady = true };
        for (int k = 0; k < 5; k++) s.Quota[k] = true;
        s.Points = new PointSpec[n];
        for (int i = 0; i < n; i++)
            s.Points[i] = new PointSpec { Kind = (HeavyShieldPurchaseKind)(i % 5), CanPayAllow = true };
        return s;
    }

    private static Point[] BuildPoints(Scenario s)
    {
        if (s.NullArray) return null;
        var points = new Point[s.Points.Length];
        for (int i = 0; i < s.Points.Length; i++)
        {
            var spec = s.Points[i];
            if (spec.IsNull) continue;
            var point = new Point(i, spec.Kind) { Disabled = spec.Disabled };
            point.Payment.Busy = spec.Busy;
            point.Payer = spec.PayerNull ? null : new Player(100 + i);
            if (!spec.PayableNull) point.Payable = new Payable(i);
            points[i] = point;
        }
        return points;
    }

    private static void Configure(Scenario s, Point[] points)
    {
        Box.ResetRun();
        Box.BowValue = s.BowValue;
        Box.CarrierReady = s.CarrierReady;
        Box.Saving = s.Saving;
        for (int k = 0; k < 5; k++) Box.Quota[k] = s.Quota[k];
        for (int i = 0; i < Box.CanPayAllow.Length; i++)
        {
            Box.CanPayAllow[i] = false;
            Box.CanPayMutations[i] = default;
        }
        for (int i = 0; i < s.Points.Length && i < Box.CanPayAllow.Length; i++)
        {
            Box.CanPayAllow[i] = s.Points[i].CanPayAllow;
            Box.CanPayMutations[i] = s.Points[i].Mutation;
        }
        Box.ThrowBowAt = s.ThrowBowAt;
        Box.ThrowReserveAt = s.ThrowReserveAt;
        Box.ThrowCanPayAt = s.ThrowCanPayAt;
        Box.ThrowSetAt = s.ThrowSetAt;
        Box.Points = points;
    }

    private static void Apply(ShellKind which, Scenario s, Point[] points)
    {
        if (which == ShellKind.Baseline)
        {
            BaselineShell._points = points;
            BaselineShell._shopLife = s.Life;
            BaselineShell._economyFault = s.EconomyFault;
            BaselineShell._retiring = s.Retiring;
            BaselineShell._menuSuspended = s.MenuSuspended;
        }
        else
        {
            CandidateShell._points = points;
            CandidateShell._shopLife = s.Life;
            CandidateShell._economyFault = s.EconomyFault;
            CandidateShell._retiring = s.Retiring;
            CandidateShell._menuSuspended = s.MenuSuspended;
        }
    }

    private static void Invoke(ShellKind which)
    {
        if (which == ShellKind.Baseline) BaselineShell.RunForTest();
        else CandidateShell.RunForTest();
    }

    private static Snapshot Run(ShellKind which, Scenario s)
    {
        var points = BuildPoints(s);
        Configure(s, points);
        Apply(which, s, points);
        var snap = new Snapshot();
        try { Invoke(which); }
        catch (Exception e) { snap.Exception = e.GetType().Name + ":" + e.Message; }
        snap.Values = ValuesOf(points);
        snap.Writes = EntriesOf(Box.Writes, Box.WriteCount);
        snap.Attempts = EntriesOf(Box.Attempts, Box.AttemptCount);
        snap.Reserves = ReservesOf(Box.Reserves, Box.ReserveCount);
        snap.CanPays = CanPaysOf(Box.CanPays, Box.CanPayCount);
        snap.PayableReads = PayableReadsOf(Box.PayableReadIds, Box.PayableReadCount);
        snap.BowCalls = Box.BowCalls;
        snap.AnyBusyCalls = Box.AnyBusyCalls;
        snap.ReserveCallCount = Box.ReserveCount;
        snap.CanPayCallCount = Box.CanPayCount;
        snap.WriteCount = Box.WriteCount;
        snap.AttemptCount = Box.AttemptCount;
        return snap;
    }

    private static SnapshotPair RunPair(Scenario s) => new SnapshotPair(Run(ShellKind.Baseline, s), Run(ShellKind.Candidate, s));

    private static string ValuesOf(Point[] points)
    {
        if (points == null) return "null";
        var sb = new StringBuilder();
        for (int i = 0; i < points.Length; i++)
        {
            if (i > 0) sb.Append(';');
            var point = points[i];
            if (point == null) sb.Append('-');
            else { var v = point.SnapshotValue; sb.Append(v == null ? "P" : v.Value ? "T" : "F"); }
        }
        return sb.ToString();
    }

    private static string EntriesOf(Box.WriteEntry[] entries, int count)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < count; i++)
        {
            if (i > 0) sb.Append(';');
            sb.Append(entries[i].PointId).Append(':').Append(entries[i].Value ? 'T' : 'F');
        }
        return sb.ToString();
    }

    private static string ReservesOf(Box.ReserveEntry[] entries, int count)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < count; i++)
        {
            if (i > 0) sb.Append(';');
            sb.Append(entries[i].Kind);
        }
        return sb.ToString();
    }

    private static string CanPaysOf(Box.CanPayEntry[] entries, int count)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < count; i++)
        {
            if (i > 0) sb.Append(';');
            sb.Append(entries[i].Index).Append(':').Append(entries[i].Life).Append(':').Append(entries[i].PlayerId);
        }
        return sb.ToString();
    }

    private static string PayableReadsOf(int[] ids, int count)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < count; i++)
        {
            if (i > 0) sb.Append(';');
            sb.Append(ids[i]);
        }
        return sb.ToString();
    }

    private static string Diff(Snapshot b, Snapshot c, bool exactQueries)
    {
        var d = new StringBuilder();
        void Cmp(string name, string x, string y)
        {
            if (x != y) d.Append(name).Append(": baseline=").Append(x).Append(" candidate=").Append(y).Append('\n');
        }
        Cmp("values", b.Values, c.Values);
        Cmp("writes", b.Writes, c.Writes);
        Cmp("attempts", b.Attempts, c.Attempts);
        Cmp("reserves", b.Reserves, c.Reserves);
        Cmp("canpays", b.CanPays, c.CanPays);
        Cmp("payableReads", b.PayableReads, c.PayableReads);
        Cmp("exception", b.Exception, c.Exception);
        if (c.BowCalls > b.BowCalls) d.Append("bowCalls increased: baseline=").Append(b.BowCalls).Append(" candidate=").Append(c.BowCalls).Append('\n');
        if (c.AnyBusyCalls > b.AnyBusyCalls) d.Append("anyBusyCalls increased: baseline=").Append(b.AnyBusyCalls).Append(" candidate=").Append(c.AnyBusyCalls).Append('\n');
        if (exactQueries && (b.BowCalls != c.BowCalls || b.AnyBusyCalls != c.AnyBusyCalls))
            d.Append("query counts differ: baseline=").Append(b.BowCalls).Append('/').Append(b.AnyBusyCalls)
             .Append(" candidate=").Append(c.BowCalls).Append('/').Append(c.AnyBusyCalls).Append('\n');
        return d.ToString();
    }

    private static string ExpectedWrites(params bool[] values)
        => string.Join(";", values.Select((v, i) => i + ":" + (v ? "T" : "F")));

    private static int Reduce()
    {
        var s = Idle(5);
        var b = Run(ShellKind.Baseline, s);
        var c = Run(ShellKind.Candidate, s);
        Console.WriteLine($"baseline: bow={b.BowCalls} anyBusy={b.AnyBusyCalls} reserve={b.ReserveCallCount} writes={b.Writes} values={b.Values}");
        Console.WriteLine($"candidate: bow={c.BowCalls} anyBusy={c.AnyBusyCalls} reserve={c.ReserveCallCount} writes={c.Writes} values={c.Values}");
        if (b.BowCalls != 5 || b.AnyBusyCalls != 5)
        {
            Console.Error.WriteLine("harness sanity failed: unmodified baseline must query Bow/AnyBusy 5 times for 5 idle points");
            return 2;
        }
        if (b.Writes != ExpectedWrites(false, false, false, false, false) || b.ReserveCallCount != 5)
        {
            Console.Error.WriteLine("harness sanity failed: baseline permission values differ");
            return 2;
        }
        var d = Diff(b, c, false);
        if (d.Length > 0) { Console.Error.WriteLine("behavior diff:\n" + d); return 1; }
        if (c.BowCalls != 1 || c.AnyBusyCalls != 1)
        {
            Console.WriteLine($"RED: expected candidate BowUsable=1 AnyBusy=1 for 5 idle points; observed {c.BowCalls}/{c.AnyBusyCalls} (baseline {b.BowCalls}/{b.AnyBusyCalls}); all five values unchanged");
            return 1;
        }
        Console.WriteLine("GREEN: 5 idle points -> candidate BowUsable=1 AnyBusy=1; values/writes/reserve sequence match baseline");
        return 0;
    }

    private static int Edge()
    {
        var failures = new List<string>();
        int checks = 0, pairScenarios = 0;
        void Fail(string name, string detail) => failures.Add(name + ": " + detail);
        void Pair(string name, Scenario s, bool exactQueries = false)
        {
            pairScenarios++;
            checks++;
            var p = RunPair(s);
            var d = Diff(p.B, p.C, exactQueries);
            if (d.Length > 0) Fail(name, d.TrimEnd());
        }
        void Eq(string name, string expected, string actual)
        {
            checks++;
            if (expected != actual) Fail(name, $"expected={expected} actual={actual}");
        }
        void Num(string name, int expected, int actual)
        {
            checks++;
            if (expected != actual) Fail(name, $"expected={expected} actual={actual}");
        }

        // 1. five idle points: one query per kind per idle run.
        {
            var s = Idle(5);
            var p = RunPair(s);
            Pair("idle5", s);
            Num("idle5.bow", 5, p.B.BowCalls);
            Num("idle5.anybusy", 5, p.B.AnyBusyCalls);
            Num("idle5.c.bow", 1, p.C.BowCalls);
            Num("idle5.c.anybusy", 1, p.C.AnyBusyCalls);
            Eq("idle5.c.writes", ExpectedWrites(false, false, false, false, false), p.C.Writes);
            Eq("idle5.c.values", "F;F;F;F;F", p.C.Values);
            Num("idle5.c.reserve", 5, p.C.ReserveCallCount);
        }

        // 2. every pre-blocked flag short-circuits before Bow/Reserve/AnyBusy.
        foreach (var flag in new[] { "economyFault", "retiring", "menuSuspended", "saving", "carrier" })
        {
            var s = Idle(5);
            switch (flag)
            {
                case "economyFault": s.EconomyFault = true; break;
                case "retiring": s.Retiring = true; break;
                case "menuSuspended": s.MenuSuspended = true; break;
                case "saving": s.Saving = true; break;
                case "carrier": s.CarrierReady = false; break;
            }
            var p = RunPair(s);
            Pair("preblocked." + flag, s);
            Num("preblocked." + flag + ".bow", 0, p.C.BowCalls);
            Num("preblocked." + flag + ".anybusy", 0, p.C.AnyBusyCalls);
            Num("preblocked." + flag + ".reserve", 0, p.C.ReserveCallCount);
            Eq("preblocked." + flag + ".writes", ExpectedWrites(true, true, true, true, true), p.C.Writes);
            Eq("preblocked." + flag + ".values", "T;T;T;T;T", p.C.Values);
        }

        // 3. Bow unusable: one Bow query, no Reserve/AnyBusy, every point blocked.
        {
            var s = Idle(5, bow: false);
            var p = RunPair(s);
            Pair("bowfalse", s);
            Num("bowfalse.c.bow", 1, p.C.BowCalls);
            Num("bowfalse.c.anybusy", 0, p.C.AnyBusyCalls);
            Num("bowfalse.c.reserve", 0, p.C.ReserveCallCount);
            Eq("bowfalse.c.writes", ExpectedWrites(true, true, true, true, true), p.C.Writes);
        }

        // 4. per-kind quota: Reserve stays per point/kind, AnyBusy only after a true quota.
        {
            var s = Idle(5);
            s.Quota[(int)HeavyShieldPurchaseKind.ExtraLeft] = false;
            s.Quota[(int)HeavyShieldPurchaseKind.ShieldLeft] = false;
            var p = RunPair(s);
            Pair("kinds", s);
            Num("kinds.c.bow", 1, p.C.BowCalls);
            Num("kinds.c.anybusy", 1, p.C.AnyBusyCalls);
            Num("kinds.c.reserve", 5, p.C.ReserveCallCount);
            Eq("kinds.c.writes", ExpectedWrites(false, true, false, true, false), p.C.Writes);
            Eq("kinds.c.reserves", "Mold;ExtraLeft;ExtraRight;ShieldLeft;ShieldRight", p.C.Reserves);
        }

        // 5. point.Disabled is the last term: Bow/Reserve/AnyBusy are still queried once.
        {
            var s = Idle(5);
            foreach (var p in s.Points) p.Disabled = true;
            var p2 = RunPair(s);
            Pair("disabled", s);
            Num("disabled.c.bow", 1, p2.C.BowCalls);
            Num("disabled.c.anybusy", 1, p2.C.AnyBusyCalls);
            Num("disabled.c.reserve", 5, p2.C.ReserveCallCount);
            Eq("disabled.c.writes", ExpectedWrites(true, true, true, true, true), p2.C.Writes);
        }

        // 6. all five busy: no idle queries at all, CanPay per point, inverted continuation result.
        {
            var s = Idle(5);
            for (int i = 0; i < s.Points.Length; i++)
            {
                s.Points[i].Busy = true;
                s.Points[i].CanPayAllow = i % 2 == 0;
            }
            var p = RunPair(s);
            Pair("busy5", s);
            Num("busy5.c.bow", 0, p.C.BowCalls);
            Num("busy5.c.anybusy", 0, p.C.AnyBusyCalls);
            Num("busy5.c.reserve", 0, p.C.ReserveCallCount);
            Num("busy5.c.canpay", 5, p.C.CanPayCallCount);
            Eq("busy5.c.writes", ExpectedWrites(false, true, false, true, false), p.C.Writes);
        }

        // 7. mixed idle/busy: one query per idle run, split by the busy continuation.
        {
            var s = Idle(4);
            s.Points[1].Busy = true;
            s.Points[1].CanPayAllow = true;
            var p = RunPair(s);
            Pair("mixed", s);
            Num("mixed.c.bow", 2, p.C.BowCalls);
            Num("mixed.c.anybusy", 2, p.C.AnyBusyCalls);
            Num("mixed.c.reserve", 3, p.C.ReserveCallCount);
            Num("mixed.c.canpay", 1, p.C.CanPayCallCount);
            Eq("mixed.c.writes", ExpectedWrites(true, false, true, true), p.C.Writes);
        }

        // 8a. busy CanPay changes Bow + clears its own Busy; the next idle point must re-read Bow
        //     and must not reuse the pre-callback snapshot.
        {
            var s = Idle(4);
            s.Points[1].Busy = true;
            s.Points[1].CanPayAllow = true;
            s.Points[1].Mutation = new Box.Mutation(5, 1); // bow=false; point1.Busy=false
            var p = RunPair(s);
            Pair("callback-bow", s);
            Num("callback-bow.c.bow", 2, p.C.BowCalls);
            Num("callback-bow.c.anybusy", 1, p.C.AnyBusyCalls);
            Eq("callback-bow.c.writes", ExpectedWrites(true, false, true, true), p.C.Writes);
            Eq("callback-bow.c.values", "T;F;T;T", p.C.Values);
        }

        // 8b. busy CanPay clears Busy; the next idle point must re-read AnyBusy.
        {
            var s = Idle(4);
            s.Points[2].Busy = true;
            s.Points[2].CanPayAllow = true;
            s.Points[2].Mutation = new Box.Mutation(4, 2); // point2.Busy=false
            var p = RunPair(s);
            Pair("callback-anybusy", s);
            // Any Busy point clears both memos (conservative contract), so the final idle
            // point re-reads Bow as well as AnyBusy.
            Num("callback-anybusy.c.bow", 2, p.C.BowCalls);
            Num("callback-anybusy.c.anybusy", 2, p.C.AnyBusyCalls);
            Eq("callback-anybusy.c.writes", ExpectedWrites(true, true, false, false), p.C.Writes);
            Eq("callback-anybusy.c.values", "T;T;F;F", p.C.Values);
        }

        // 9. two independent UpdatePaymentGates calls must not share memo; feature/quota changes apply.
        {
            var s = Idle(1);
            var points = BuildPoints(s);
            Configure(s, points);
            Apply(ShellKind.Candidate, s, points);
            CandidateShell.RunForTest();
            Num("twocalls.call1.bow", 1, Box.BowCalls);
            Eq("twocalls.call1.write", "0:F", EntriesOf(Box.Writes, Box.WriteCount));
            Box.BowValue = false;
            CandidateShell.RunForTest();
            Num("twocalls.call2.bow", 2, Box.BowCalls);
            Eq("twocalls.call2.writes", "0:F;0:T", EntriesOf(Box.Writes, Box.WriteCount));
            Box.BowValue = true;
            Box.Quota[(int)HeavyShieldPurchaseKind.Mold] = false;
            CandidateShell.RunForTest();
            Num("twocalls.call3.bow", 3, Box.BowCalls);
            Num("twocalls.call3.reserve", 2, Box.ReserveCount);
            Eq("twocalls.call3.writes", "0:F;0:T;0:T", EntriesOf(Box.Writes, Box.WriteCount));
            CandidateShell._retiring = true;
            CandidateShell.RunForTest();
            Num("twocalls.call4.bow", 3, Box.BowCalls);
            Eq("twocalls.call4.writes", "0:F;0:T;0:T;0:T", EntriesOf(Box.Writes, Box.WriteCount));
            CandidateShell._retiring = false;
            Box.Quota[(int)HeavyShieldPurchaseKind.Mold] = true;

            // Baseline parity for the same four calls.
            var bpoints = BuildPoints(s);
            Configure(s, bpoints);
            Apply(ShellKind.Baseline, s, bpoints);
            BaselineShell.RunForTest();
            Box.BowValue = false;
            BaselineShell.RunForTest();
            Box.BowValue = true; Box.Quota[(int)HeavyShieldPurchaseKind.Mold] = false;
            BaselineShell.RunForTest();
            BaselineShell._retiring = true;
            BaselineShell.RunForTest();
            BaselineShell._retiring = false;
            Box.Quota[(int)HeavyShieldPurchaseKind.Mold] = true;
            Num("twocalls.baseline.bow", 3, Box.BowCalls);
            Num("twocalls.baseline.reserve", 2, Box.ReserveCount);
            Eq("twocalls.baseline.writes", "0:F;0:T;0:T;0:T", EntriesOf(Box.Writes, Box.WriteCount));
        }

        // 10. null array / empty / null points / null payable.
        {
            var s = Idle(0); s.NullArray = true;
            var p = RunPair(s);
            Pair("nullarray", s);
            Num("nullarray.c.bow", 0, p.C.BowCalls);
            Num("nullarray.c.anybusy", 0, p.C.AnyBusyCalls);
            Eq("nullarray.c.writes", "", p.C.Writes);
            Eq("nullarray.c.values", "null", p.C.Values);
        }
        {
            var s = Idle(0);
            var p = RunPair(s);
            Pair("empty", s);
            Num("empty.c.bow", 0, p.C.BowCalls);
            Num("empty.c.anybusy", 0, p.C.AnyBusyCalls);
            Eq("empty.c.writes", "", p.C.Writes);
            Eq("empty.c.values", "", p.C.Values);
        }
        {
            var s = Idle(5);
            foreach (var ps in s.Points) ps.IsNull = true;
            var p = RunPair(s);
            Pair("nullpoints", s);
            Num("nullpoints.c.bow", 0, p.C.BowCalls);
            Num("nullpoints.c.anybusy", 0, p.C.AnyBusyCalls);
            Num("nullpoints.c.reserve", 0, p.C.ReserveCallCount);
            Eq("nullpoints.c.writes", "", p.C.Writes);
            Eq("nullpoints.c.values", "-;-;-;-;-", p.C.Values);
        }
        {
            var s = Idle(5);
            foreach (var ps in s.Points) ps.PayableNull = true;
            var p = RunPair(s);
            Pair("nullpayable", s);
            Num("nullpayable.c.bow", 0, p.C.BowCalls);
            Num("nullpayable.c.anybusy", 0, p.C.AnyBusyCalls);
            Num("nullpayable.c.reserve", 0, p.C.ReserveCallCount);
            Eq("nullpayable.c.writes", "", p.C.Writes);
            Eq("nullpayable.c.values", "P;P;P;P;P", p.C.Values);
        }
        {
            var s = Idle(3);
            s.Points[0].IsNull = true;
            s.Points[2].IsNull = true;
            var p = RunPair(s);
            Pair("mixednull", s);
            Num("mixednull.c.bow", 1, p.C.BowCalls);
            Num("mixednull.c.reserve", 1, p.C.ReserveCallCount);
            Eq("mixednull.c.writes", "1:F", p.C.Writes);
            Eq("mixednull.c.values", "-;F;-", p.C.Values);
        }

        // 11. first necessary Bow/Reserve/CanPay/setter exceptions: identical prefix, exception propagates.
        {
            var s = Idle(5); s.ThrowBowAt = 1;
            var p = RunPair(s);
            Pair("throw-bow", s, exactQueries: true);
            Eq("throw-bow.c.exception", "InvalidOperationException:first-bow-call", p.C.Exception);
            Num("throw-bow.c.bow", 1, p.C.BowCalls);
            Num("throw-bow.c.reserve", 0, p.C.ReserveCallCount);
            Num("throw-bow.c.writes", 0, p.C.WriteCount);
        }
        {
            var s = Idle(5); s.ThrowReserveAt = 1;
            var p = RunPair(s);
            Pair("throw-reserve", s, exactQueries: true);
            Eq("throw-reserve.c.exception", "InvalidOperationException:first-reserve-call", p.C.Exception);
            Num("throw-reserve.c.bow", 1, p.C.BowCalls);
            Num("throw-reserve.c.reserve", 1, p.C.ReserveCallCount);
            Num("throw-reserve.c.writes", 0, p.C.WriteCount);
        }
        {
            var s = Idle(1); s.Points[0].Busy = true; s.ThrowCanPayAt = 1;
            var p = RunPair(s);
            Pair("throw-canpay", s, exactQueries: true);
            Eq("throw-canpay.c.exception", "InvalidOperationException:first-canpay-call", p.C.Exception);
            Num("throw-canpay.c.canpay", 1, p.C.CanPayCallCount);
            Num("throw-canpay.c.writes", 0, p.C.WriteCount);
        }
        {
            var s = Idle(5); s.ThrowSetAt = 1;
            var p = RunPair(s);
            Pair("throw-setter", s, exactQueries: true);
            Eq("throw-setter.c.exception", "InvalidOperationException:first-setter-call", p.C.Exception);
            Num("throw-setter.c.bow", 1, p.C.BowCalls);
            Num("throw-setter.c.reserve", 1, p.C.ReserveCallCount);
            Num("throw-setter.c.anybusy", 1, p.C.AnyBusyCalls);
            Num("throw-setter.c.attempts", 1, p.C.AttemptCount);
            Num("throw-setter.c.writes", 0, p.C.WriteCount);
        }
        {
            var s = Idle(2); s.ThrowSetAt = 2;
            var p = RunPair(s);
            var d = Diff(p.B, p.C, false);
            if (d.Length > 0) Fail("throw-setter2", d.TrimEnd());
            Eq("throw-setter2.c.exception", "InvalidOperationException:first-setter-call", p.C.Exception);
            Eq("throw-setter2.c.writes", "0:F", p.C.Writes);
            Eq("throw-setter2.c.attempts", "0:F;1:F", p.C.Attempts);
            Num("throw-setter2.b.bow", 2, p.B.BowCalls);
            Num("throw-setter2.c.bow", 1, p.C.BowCalls);
        }
        {
            var s = Idle(2);
            s.Points[1].Busy = true; s.Points[1].CanPayAllow = true; s.ThrowCanPayAt = 1;
            var p = RunPair(s);
            Pair("throw-canpay-after-cache", s, exactQueries: true);
            Eq("throw-canpay-after-cache.c.exception", "InvalidOperationException:first-canpay-call", p.C.Exception);
            Eq("throw-canpay-after-cache.c.writes", "0:T", p.C.Writes);
            Num("throw-canpay-after-cache.c.canpay", 1, p.C.CanPayCallCount);
        }

        // 12. busy continuation keeps index/life/payer identity and ignores idle gates.
        {
            var s = Idle(1);
            s.Life = 777;
            s.Points[0].Busy = true;
            var p = RunPair(s);
            Pair("busy-life", s);
            Eq("busy-life.c.canpays", "0:777:100", p.C.CanPays);
            Eq("busy-life.c.writes", "0:F", p.C.Writes);

            var n = Idle(1);
            n.Life = 778; n.Points[0].Busy = true; n.Points[0].PayerNull = true;
            var pn = RunPair(n);
            Pair("busy-nullpayer", n);
            Eq("busy-nullpayer.c.canpays", "0:778:-1", pn.C.CanPays);
        }

        // 13. Busy continuation unaffected by pause/save/feature-off/idle gates.
        {
            var s = Idle(1);
            s.Points[0].Busy = true;
            s.EconomyFault = true; s.Retiring = true; s.MenuSuspended = true; s.Saving = true;
            s.CarrierReady = false; s.BowValue = false;
            for (int k = 0; k < 5; k++) s.Quota[k] = false;
            var p = RunPair(s);
            Pair("busy-flags", s);
            Num("busy-flags.c.bow", 0, p.C.BowCalls);
            Num("busy-flags.c.anybusy", 0, p.C.AnyBusyCalls);
            Num("busy-flags.c.reserve", 0, p.C.ReserveCallCount);
            Num("busy-flags.c.canpay", 1, p.C.CanPayCallCount);
            Eq("busy-flags.c.writes", "0:F", p.C.Writes);
        }

        if (failures.Count == 0)
        {
            Console.WriteLine($"GREEN: edge matrix passed (preblock, quota per kind, disabled, busy, null/empty, callback invalidation, cross-call, first exceptions); checks={checks} pairScenarios={pairScenarios} customCallEntries=8.");
            return 0;
        }
        Console.Error.WriteLine($"edge failures: {failures.Count} checks={checks} pairScenarios={pairScenarios}");
        foreach (var f in failures.Take(20)) Console.Error.WriteLine("- " + f);
        return 1;
    }

    private static Scenario RandomScenario(Random rng)
    {
        var s = new Scenario
        {
            NullArray = rng.Next(25) == 0,
            BowValue = rng.Next(4) != 0,
            CarrierReady = rng.Next(10) != 0,
            Saving = rng.Next(8) == 0,
            EconomyFault = rng.Next(10) == 0,
            Retiring = rng.Next(10) == 0,
            MenuSuspended = rng.Next(10) == 0,
            Life = 42 + rng.Next(1000),
        };
        for (int k = 0; k < 5; k++) s.Quota[k] = rng.Next(4) != 0;
        int n = s.NullArray ? 0 : rng.Next(0, 6);
        s.Points = new PointSpec[n];
        for (int i = 0; i < n; i++)
        {
            var ps = new PointSpec
            {
                IsNull = rng.Next(9) == 0,
                Busy = rng.Next(3) == 0,
                Disabled = rng.Next(7) == 0,
                Kind = (HeavyShieldPurchaseKind)rng.Next(5),
                CanPayAllow = rng.Next(2) == 0,
                PayerNull = rng.Next(12) == 0,
            };
            ps.PayableNull = !ps.IsNull && rng.Next(9) == 0;
            byte mutation = rng.Next(12) == 0 ? (byte)(1 + rng.Next(5)) : (byte)0;
            ps.Mutation = new Box.Mutation(mutation, rng.Next(-1, n));
            s.Points[i] = ps;
        }
        int exception = rng.Next(12);
        if (exception == 0) s.ThrowBowAt = 1;
        else if (exception == 1) s.ThrowReserveAt = 1;
        else if (exception == 2) s.ThrowCanPayAt = 1;
        else if (exception == 3) s.ThrowSetAt = 1;
        return s;
    }

    private static int Compare(int seed, int cases)
    {
        var rng = new Random(seed);
        int exceptions = 0, mismatches = 0, increases = 0, reduced = 0;
        var firstDiffs = new List<string>();
        for (int i = 0; i < cases; i++)
        {
            var s = RandomScenario(rng);
            var p = RunPair(s);
            if (p.B.Exception != "none") exceptions++;
            if (p.C.BowCalls > p.B.BowCalls || p.C.AnyBusyCalls > p.B.AnyBusyCalls) increases++;
            if (p.C.BowCalls < p.B.BowCalls || p.C.AnyBusyCalls < p.B.AnyBusyCalls) reduced++;
            // A later exception may follow idle runs whose Bow/AnyBusy reads were memoized;
            // the prefix of writes/Reserve/CanPay must still match exactly, and queries may not increase.
            var d = Diff(p.B, p.C, false);
            if (d.Length > 0)
            {
                mismatches++;
                if (firstDiffs.Count < 3) firstDiffs.Add("#" + i + "\n" + d.TrimEnd());
            }
        }
        Console.WriteLine($"compare: seed={seed} cases={cases} exceptionCases={exceptions} mismatches={mismatches} queryIncreases={increases} reducedCases={reduced}");
        if (mismatches == 0)
        {
            Console.WriteLine("GREEN: deterministic mixed matrix preserves values, write order, per-kind Reserve and busy CanPay sequences; Bow/AnyBusy never increased.");
            return 0;
        }
        Console.Error.WriteLine("first mismatches:");
        foreach (var d in firstDiffs) Console.Error.WriteLine(d);
        return 1;
    }

    private static long Measure(ShellKind which, int iterations)
    {
        for (int i = 0; i < 5000; i++) { Box.ResetCountersOnly(); Invoke(which); }
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++) { Box.ResetCountersOnly(); Invoke(which); }
        return GC.GetAllocatedBytesForCurrentThread() - start;
    }

    private static int Alloc()
    {
        const int iterations = 200000;
        var s = Idle(5);
        var baselinePoints = BuildPoints(s);
        Configure(s, baselinePoints);
        Apply(ShellKind.Baseline, s, baselinePoints);
        long baseline = Measure(ShellKind.Baseline, iterations);

        var candidatePoints = BuildPoints(s);
        Configure(s, candidatePoints);
        Apply(ShellKind.Candidate, s, candidatePoints);
        long candidate = Measure(ShellKind.Candidate, iterations);

        var failures = new List<string>();
        var bt = typeof(BaselineShell);
        var ct = typeof(CandidateShell);
        void CheckShell(Type type)
        {
            foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                Console.WriteLine($"nested {type.FullName}: {nested.Name} isValueType={nested.IsValueType}");
                if (!nested.IsValueType) failures.Add($"{type.Name}.{nested.Name} is a heap class");
                foreach (var f in nested.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                    if (typeof(Delegate).IsAssignableFrom(f.FieldType))
                        failures.Add($"{type.Name}.{nested.Name}.{f.Name} holds a delegate");
            }
            foreach (var f in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                if (typeof(Delegate).IsAssignableFrom(f.FieldType))
                    failures.Add($"{type.Name}.{f.Name} holds a delegate");
        }
        CheckShell(bt);
        CheckShell(ct);

        Console.WriteLine($"alloc: Release x{iterations} entries, 5 idle points, preallocated stubs; baseline={baseline} bytes candidate={candidate} bytes delta={candidate - baseline}");
        if (candidate > baseline)
        {
            failures.Add($"candidate allocated {candidate} > baseline {baseline}");
        }
        if (baseline != 0)
        {
            Console.WriteLine("note: non-zero baseline delta indicates harness/stub allocation; candidate is still compared against it");
        }
        if (failures.Count == 0)
        {
            Console.WriteLine("GREEN: no new GC allocation at the extracted entry; no heap display class or delegate field in either shell.");
            return 0;
        }
        foreach (var f in failures) Console.Error.WriteLine("- " + f);
        return 1;
    }
}
