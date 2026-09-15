using System.Reflection;
using KingdomEnhancedMod;
using UnityEngine;

/// <summary>
/// 直接源回归：把生产 il2cpp/SamuraiDashDiagnostics.cs 与本目录的 Unity/BepInEx 桩一起编译，
/// 只断言可观察契约（准入、行数上限、格式字段、故障吞掉），不镜像日志字符串。
/// </summary>
internal static class Program
{
    private static List<string> Log => KingdomEnhancedPlugin.Instance.LogSource.Lines;
    private static int passed, failed;

    private static int Main()
    {
        Test("bucket admits eight dashes then reports suppression on the next start", () =>
        {
            var k = NewKnight(3.5f);
            int setupReads = k.gameObject.ContextReads; // 建桩时的 transform 写入不算诊断读取
            for (int i = 1; i <= 8; i++)
            {
                var t = SamuraiDashDiagnostics.Begin(k, false, 100f);
                Check(t != null, "dash " + i + " should be admitted");
                Eq(i, t.Id, "admitted dash ordinal");
                Eq(1, t.Lines, "start line spends one unit of budget");
            }
            Eq(setupReads + 16, k.gameObject.ContextReads, "each admitted dash reads the owner id and x once");
            Check(SamuraiDashDiagnostics.Begin(k, true, 100f) == null, "the ninth dash at the same instant is refused");
            Eq(setupReads + 16, k.gameObject.ContextReads, "a refused dash must not touch Unity");
            Eq(8, Log.Count, "a refused dash writes nothing");

            var next = SamuraiDashDiagnostics.Begin(k, true, 101f);
            Check(next != null, "one second refills exactly one dash");
            Eq(9, next.Id, "ordinal continues over admitted dashes");
            Eq(42, next.KnightId, "owner instance id captured on the trace");
            Eq(101f, next.StartedAt, "lease start stamp kept as passed");
            Check(next.Returning, "mode flag captured on the trace");
            var f = Fields(Last());
            Eq("start", f["event"], "start event");
            Eq("return", f["mode"], "return dash mode");
            Eq("9", f["dash"], "dash id in the line");
            Eq("42", f["knight"], "owner instance id in the line");
            Eq("3.50", f["x"], "owner x read once for context");
            Eq("1", f["suppressedSinceLast"], "refused dashes reported on the next start");
            Eq("10", f["totalDashCount"], "every real attempt counts, admitted or not");
        });

        Test("refill is one per second and capped at capacity", () =>
        {
            var k = NewKnight(0f);
            for (int i = 0; i < 8; i++) Check(SamuraiDashDiagnostics.Begin(k, false, 200f) != null, "drain the bucket");
            Check(SamuraiDashDiagnostics.Begin(k, false, 200.5f) == null, "half a second is not a token");
            Check(SamuraiDashDiagnostics.Begin(k, false, 200.75f) == null, "three quarters is not a token");
            Check(SamuraiDashDiagnostics.Begin(k, false, 201f) != null, "one whole second is one token");
            Check(SamuraiDashDiagnostics.Begin(k, false, 201f) == null, "the token was spent");
            for (int i = 0; i < 8; i++) Check(SamuraiDashDiagnostics.Begin(k, false, 400f) != null, "idle refill fills the bucket");
            Check(SamuraiDashDiagnostics.Begin(k, false, 400f) == null, "capacity is eight, not two hundred");
            Check(SamuraiDashDiagnostics.Begin(k, false, 400.5f) == null, "no partial token is admitted");
            Check(SamuraiDashDiagnostics.Begin(k, false, 401f) != null, "the next whole second admits again");
        });

        Test("a backwards clock restarts the bucket", () =>
        {
            var k = NewKnight(0f);
            for (int i = 0; i < 8; i++) Check(SamuraiDashDiagnostics.Begin(k, false, 300f) != null, "drain the bucket");
            Check(SamuraiDashDiagnostics.Begin(k, false, 300f) == null, "bucket is empty");
            var t = SamuraiDashDiagnostics.Begin(k, false, 290f);
            Check(t != null, "a stamp earlier than the last refill restarts the bucket");
            Eq("1", Fields(Last())["suppressedSinceLast"], "the refused dash is still reported");
            for (int i = 0; i < 7; i++) Check(SamuraiDashDiagnostics.Begin(k, false, 290f) != null, "eight again after the restart");
            Check(SamuraiDashDiagnostics.Begin(k, false, 290f) == null, "and still no more than eight");
        });

        Test("invalid timestamps are refused without corrupting the bucket", () =>
        {
            var k = NewKnight(1f);
            int setupReads = k.gameObject.ContextReads;
            for (int i = 0; i < 8; i++) Check(SamuraiDashDiagnostics.Begin(k, false, 400f) != null, "drain the bucket");
            Check(SamuraiDashDiagnostics.Begin(k, false, float.NaN) == null, "NaN stamp refused");
            Check(SamuraiDashDiagnostics.Begin(k, false, float.PositiveInfinity) == null, "positive infinity refused");
            Check(SamuraiDashDiagnostics.Begin(k, false, float.NegativeInfinity) == null, "negative infinity refused");
            Eq(setupReads + 16, k.gameObject.ContextReads, "invalid stamps are refused before any Unity read");
            Check(SamuraiDashDiagnostics.Begin(k, false, 400f) == null, "the bucket is still empty at the same instant");
            var t = SamuraiDashDiagnostics.Begin(k, false, 401f);
            Check(t != null, "a valid stamp still works after invalid ones");
            var f = Fields(Last());
            Eq("4", f["suppressedSinceLast"], "invalid stamps and the empty bucket all count as suppressed");
            Eq("13", f["totalDashCount"], "invalid stamps still count as real attempts");
        });

        Test("each dash is capped at twelve lines", () =>
        {
            var k = NewKnight(0f);
            var first = SamuraiDashDiagnostics.Begin(k, false, 500f);
            Check(first != null, "first dash admitted");
            for (int i = 0; i < 40; i++) SamuraiDashDiagnostics.Write(first, "visual-skipped", "reason=backoff");
            Eq(12, first.Lines, "budget includes the start line");
            Eq(29, first.Dropped, "refused events are counted, not logged");
            Eq(12, Log.Count, "sink saw exactly the cap");
            SamuraiDashDiagnostics.Write(null, "visual-skipped", "reason=backoff");
            Eq(12, Log.Count, "a null trace is a no-op");

            var second = SamuraiDashDiagnostics.Begin(k, false, 500.5f);
            Check(second != null, "second dash has its own budget");
            for (int i = 0; i < 40; i++) SamuraiDashDiagnostics.Write(second, "tail-cleared", "slot=0");
            Eq(12, second.Lines, "cap is per trace, not global");
            Eq(24, Log.Count, "both traces are bounded");
            Eq(29, first.Dropped, "the first trace kept its own refused count");
        });

        Test("a failing log sink never breaks the dash", () =>
        {
            var k = NewKnight(0f);
            KingdomEnhancedPlugin.Instance.LogSource.OnInfo = _ => throw new InvalidOperationException("log sink");
            var t = SamuraiDashDiagnostics.Begin(k, false, 600f);
            Check(t != null, "start is still admitted and returned");
            for (int i = 0; i < 40; i++) SamuraiDashDiagnostics.Write(t, "visual-skipped", "reason=backoff");
            Eq(12, t.Lines, "budget is spent even when the sink throws");

            KingdomEnhancedPlugin.Instance.LogSource.OnInfo = null;
            var later = SamuraiDashDiagnostics.Begin(k, false, 601f);
            Check(later != null, "a healthy sink keeps admitting");
            Eq(1, Log.Count, "later lines reach the healthy sink");
            KingdomEnhancedPlugin.Instance = null;
            var orphan = SamuraiDashDiagnostics.Begin(k, false, 602f);
            Check(orphan != null, "a missing plugin instance is not an error");
            SamuraiDashDiagnostics.Write(orphan, "end", "elapsed=0");
        });

        Test("native context failures are swallowed and degrade the line", () =>
        {
            var k = NewKnight(0f);
            k.gameObject.ThrowOnContext = true;
            var t = SamuraiDashDiagnostics.Begin(k, false, 700f);
            Check(t != null, "a throwing native read still returns a trace");
            var f = Fields(Log[0]);
            Eq("0", f["knight"], "unreadable owner id degrades to zero");
            Eq("NaN", f["x"], "unreadable x degrades to NaN");
            Eq("1", f["dash"], "the dash is still numbered");
            SamuraiDashDiagnostics.Write(t, "visual-skipped", "reason=exception");

            var nullOwner = SamuraiDashDiagnostics.Begin(null, false, 700f);
            Check(nullOwner != null, "a null owner is not an error");
            Eq("0", Fields(Log[2])["knight"], "null owner id");
            Eq("NaN", Fields(Log[2])["x"], "null owner x");
        });

        Test("Trace keeps only primitive state and no Unity object", () =>
        {
            var type = typeof(SamuraiDashDiagnostics.Trace);
            Check(type.IsSealed, "Trace is sealed");
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                Check(!typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType),
                    field.Name + " must not pin a Unity object");
                Check(field.FieldType.IsPrimitive || field.FieldType.IsEnum || field.FieldType == typeof(string),
                    field.Name + " must be primitive/string, was " + field.FieldType);
            }
            foreach (var name in new[] { "Id", "KnightId", "Returning", "StartedAt", "FirstUpdateLogged", "TailLogged", "Lines", "Dropped" })
                Check(type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null,
                    "operator-facing field " + name + " is missing");
        });

        Test("events append after the shared header", () =>
        {
            var k = NewKnight(2f);
            var t = SamuraiDashDiagnostics.Begin(k, false, 800f);
            Check(t != null, "admitted");
            SamuraiDashDiagnostics.Write(t, "visual-skipped", "reason=backoff");
            SamuraiDashDiagnostics.Write(t, "end", null);
            Eq(3, Log.Count, "one accepted Write is one line");
            var f = Fields(Log[1]);
            Eq("visual-skipped", f["event"], "event name");
            Eq("backoff", f["reason"], "details are appended as key=value pairs");
            Eq("1", f["dash"], "dash id repeated on every line");
            Eq("42", f["knight"], "knight id repeated on every line");
            Check(!Log[1].EndsWith(" "), "no trailing space after details");
            Check(Fields(Log[2])["event"] == "end" && Log[2].EndsWith("event=end"), "null details leave no trailing space");
        });

        Console.WriteLine((failed == 0 ? "ALL PASS: " : "FAILURES: ") + passed + " passed, " + failed + " failed");
        return failed == 0 ? 0 : 1;
    }

    private static void Test(string name, Action body)
    {
        Reset();
        try { body(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + e.GetBaseException().Message); }
    }

    private static void Reset()
    {
        KingdomEnhancedPlugin.Instance = new PluginStub();
        Set("_nextId", 0);
        Set("_totalDashes", 0);
        Set("_suppressedSinceLast", 0);
        Set("_tokens", 8f);
        Set("_refilledAt", float.NegativeInfinity); // 与进程首次调用一致：bucket 满
    }

    private static void Set(string name, object value) =>
        typeof(SamuraiDashDiagnostics).GetField(name, BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, value);

    private static Knight NewKnight(float x)
    {
        var go = new GameObject { Id = 42 };
        go.transform.position = new Vector3(x);
        return new Knight { gameObject = go };
    }

    private static string Last() => Log[Log.Count - 1];

    private static Dictionary<string, string> Fields(string line)
    {
        Check(line.StartsWith("[SamuraiDiag] "), "line must start with the shared header: " + line);
        var map = new Dictionary<string, string>();
        foreach (var token in line.Substring("[SamuraiDiag] ".Length).Split(' '))
        {
            int eq = token.IndexOf('=');
            if (eq > 0) map[token.Substring(0, eq)] = token.Substring(eq + 1);
        }
        return map;
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    private static void Eq<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception(message + ": expected " + expected + ", got " + actual);
    }
}
