using System;
using System.Collections.Generic;
using System.Reflection;
using KingdomEnhancedMod;

internal static class Program
{
    private static int _checks, _passed, _failed;
    private static readonly Guid A = Guid.Parse("00000001-0000-0000-0000-000000000000");
    private static readonly Guid B = Guid.Parse("00000002-0000-0000-0000-000000000000");
    private static readonly Guid C = Guid.Parse("00000003-0000-0000-0000-000000000000");
    private static readonly Guid D = Guid.Parse("00000004-0000-0000-0000-000000000000");
    private static readonly Guid E = Guid.Parse("00000005-0000-0000-0000-000000000000");
    private static HeavyShieldMaintenanceSubject Subject(Guid id = default, long life = 1,
        int durability = 20, bool atShop = true, bool threatened = false, double lastThreat = 0,
        bool inBanner = false, bool valid = true) =>
        new(id == default ? A : id, life, durability, valid, inBanner, atShop, threatened, lastThreat);
    private static void Tick(HeavyShieldMaintenancePolicy policy, double at,
        params HeavyShieldMaintenanceSubject[] subjects) => policy.Tick(at, true, 1, 1, true, subjects);
    private static void Check(bool result, string message)
    {
        _checks++;
        if (!result) throw new Exception(message);
    }
    private static void Equal<T>(T expected, T actual, string message) =>
        Check(EqualityComparer<T>.Default.Equals(expected, actual), $"{message}: expected {expected}, got {actual}");
    private static void Test(string name, Action body)
    {
        try { body(); _passed++; Console.WriteLine("PASS " + name); }
        catch (Exception ex) { _failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
    }
    private static HeavyShieldMaintenancePolicy Due(out HeavyShieldMaintenanceSubject subject)
    {
        var policy = new HeavyShieldMaintenancePolicy();
        subject = Subject();
        Tick(policy, 6, subject);
        Tick(policy, 14, subject);
        Equal(HeavyShieldMaintenancePhase.CompletionDue, policy.Phase, "eight safe seconds produce due");
        return policy;
    }
    private static int Main()
    {
        Test("released version migration keeps proportional wear", () =>
        {
            foreach (var row in new[] { (1, 1, 14), (1, 2, 27), (1, 3, 40), (2, 1, 10),
                (2, 2, 20), (2, 3, 30), (2, 4, 40), (3, 1, 10), (3, 2, 20), (3, 3, 30), (3, 4, 40) })
            {
                Check(HeavyShieldDurabilityMigration.TryUpgrade(row.Item1, row.Item2, false, out int value), "valid legacy row");
                Equal(row.Item3, value, "proportional result");
            }
        });
        Test("v4 is identity including broken zero", () =>
        {
            for (int value = 0; value <= 40; value++)
            {
                Check(HeavyShieldDurabilityMigration.TryUpgrade(4, value, value == 0, out int result), "current row valid");
                Equal(value, result, "current row unchanged");
            }
            foreach (int version in new[] { 1, 2, 3 })
            {
                Check(HeavyShieldDurabilityMigration.TryUpgrade(version, 0, true, out int zero), "broken legacy valid");
                Equal(0, zero, "broken does not revive");
            }
        });
        Test("old bounds and broken consistency checked before conversion", () =>
        {
            foreach (var row in new[] { (0, 1, false), (5, 1, false), (1, 4, false), (2, 5, false),
                (3, 5, false), (4, 41, false), (1, -1, true), (4, int.MaxValue, false),
                (2, 0, false), (4, 1, true) })
            {
                Check(!HeavyShieldDurabilityMigration.TryUpgrade(row.Item1, row.Item2, row.Item3, out int result), "reject invalid migration");
                Equal(0, result, "failure output is empty");
            }
        });
        Test("wear boundary bands", () =>
        {
            foreach (var row in new[] { (-1, -1), (0, 3), (10, 3), (11, 2), (20, 2), (21, 1),
                (30, 1), (31, 0), (40, 0), (41, -1) })
                Equal(row.Item2, HeavyShieldDurabilityMigration.WearBand(row.Item1), "wear band");
        });
        Test("six seconds quiet required before joining queue", () =>
        {
            var policy = new HeavyShieldMaintenancePolicy();
            Tick(policy, 5.999, Subject(atShop: false));
            Equal(HeavyShieldMaintenancePhase.Idle, policy.Phase, "too early");
            Check(!policy.IsQueued(A, 1), "not queued early");
            Tick(policy, 6, Subject(atShop: false));
            Equal(HeavyShieldMaintenancePhase.Travelling, policy.Phase, "eligible head travels");
            Check(policy.IsQueued(A, 1), "active head included in queue");
        });
        Test("travel time never contributes to work", () =>
        {
            var policy = new HeavyShieldMaintenancePolicy();
            Tick(policy, 6, Subject(atShop: false));
            Tick(policy, 100, Subject(atShop: false));
            Equal(0d, policy.WorkedSeconds, "travelling has no work");
            Tick(policy, 101, Subject());
            Equal(HeavyShieldMaintenancePhase.Repairing, policy.Phase, "arrival starts work");
            Equal(0d, policy.WorkedSeconds, "arrival starts at zero");
            Tick(policy, 108.999, Subject());
            Check(!policy.TryTakeCompletion(out _), "under eight seconds");
            Tick(policy, 109, Subject());
            Check(policy.TryTakeCompletion(out var completion), "full work emits");
            Equal(A, completion.Receipt, "receipt belongs to worker");
            Equal(20, completion.ExpectedDurability, "original wear bound");
            Equal(1L, completion.Life, "life bound");
            Equal(1L, completion.World, "world bound");
            Equal(1L, completion.ShopLife, "shop bound");
        });
        Test("FIFO preserves older observation and sorts simultaneous receipts", () =>
        {
            var policy = new HeavyShieldMaintenancePolicy();
            Tick(policy, 6, Subject(B));
            Tick(policy, 7, Subject(C), Subject(A), Subject(B));
            Equal(B, policy.ActiveReceipt, "older B stays ahead of later A");
            Check(policy.IsQueued(A, 1) && policy.IsQueued(C, 1), "waiting rows are queued");
            Tick(policy, 14, Subject(C), Subject(A), Subject(B));
            Check(policy.TryTakeCompletion(out var b), "B done");
            Check(policy.ResolveCompletion(b.Serial, true, false), "B resolved");
            Tick(policy, 15, Subject(C), Subject(A), Subject(B));
            Equal(A, policy.ActiveReceipt, "same observation sorted by receipt");
            Equal(0d, policy.WorkedSeconds, "waiting time gives no work");
            Tick(policy, 23, Subject(C), Subject(A), Subject(B));
            Check(policy.TryTakeCompletion(out var a), "A done");
            Check(policy.ResolveCompletion(a.Serial, true, false), "A resolved");
            Tick(policy, 24, Subject(C), Subject(A), Subject(B));
            Equal(C, policy.ActiveReceipt, "C is next, stale B not requeued");
        });
        Test("full broken banner and invalid subjects never repair", () =>
        {
            foreach (var subject in new[] { Subject(durability: 0), Subject(durability: 40),
                Subject(durability: -1), Subject(durability: 41), Subject(inBanner: true), Subject(valid: false),
                Subject(threatened: true), Subject(life: 0), Subject(lastThreat: double.NaN),
                Subject(lastThreat: double.PositiveInfinity), Subject(lastThreat: 100), Subject() with { Receipt = Guid.Empty } })
            {
                var policy = new HeavyShieldMaintenancePolicy();
                Tick(policy, 20, subject);
                Equal(HeavyShieldMaintenancePhase.Idle, policy.Phase, "ineligible subject");
                Check(!policy.TryTakeCompletion(out _), "ineligible never grants");
            }
        });
        Test("duplicate receipt even different life rejects entire tick", () =>
        {
            foreach (long duplicateLife in new[] { 1L, 2L })
            {
                var policy = Due(out var subject);
                Tick(policy, 15, subject, subject with { Life = duplicateLife });
                Equal(HeavyShieldMaintenancePhase.Idle, policy.Phase, "duplicate tick clears work");
                Check(!policy.TryTakeCompletion(out _), "duplicate cannot emit old proof");
            }
        });
        Test("oversized roster fails closed", () =>
        {
            var policy = Due(out var subject);
            Tick(policy, 15, subject, Subject(B), Subject(C), Subject(D), Subject(E));
            Equal(HeavyShieldMaintenancePhase.Idle, policy.Phase, "more than current island maximum");
            Check(!policy.TryTakeCompletion(out _), "oversized roster no completion");
        });
        Test("null roster fails closed", () =>
        {
            var policy = Due(out _);
            policy.Tick(15, true, 1, 1, true, null!);
            Check(!policy.TryTakeCompletion(out _), "null snapshot invalidates ordinary proof");
        });
        Test("pause excludes elapsed time and prevents new travel", () =>
        {
            var policy = new HeavyShieldMaintenancePolicy();
            policy.Tick(6, false, 1, 1, true, new[] { Subject() });
            Equal(HeavyShieldMaintenancePhase.Idle, policy.Phase, "paused cannot start");
            Tick(policy, 10, Subject());
            Tick(policy, 12, Subject());
            Equal(2d, policy.WorkedSeconds, "two playing seconds");
            policy.Tick(100, false, 1, 1, true, new[] { Subject() });
            Equal(2d, policy.WorkedSeconds, "paused clock ignored");
            Tick(policy, 101, Subject());
            Equal(2d, policy.WorkedSeconds, "resume baseline excludes gap");
            Tick(policy, 107, Subject());
            Check(policy.TryTakeCompletion(out _), "remaining playing work completes");
        });
        Test("due completion cannot be taken while paused", () =>
        {
            var policy = Due(out var subject);
            policy.Tick(14, false, 1, 1, true, new[] { subject });
            Check(!policy.TryTakeCompletion(out _), "pause blocks already due grant");
            Tick(policy, 14, subject);
            Check(policy.TryTakeCompletion(out _), "validated resume allows due grant");
        });
        Test("resume must revalidate due subject", () =>
        {
            var policy = Due(out var subject);
            policy.Tick(14, false, 1, 1, true, new[] { subject });
            Tick(policy, 15, subject with { Durability = 19 });
            Check(!policy.TryTakeCompletion(out _), "stale durability proof revoked");
            Equal(0d, policy.WorkedSeconds, "stale work removed");
        });
        Test("leaving shop during paused observation invalidates old work", () =>
        {
            var policy = Due(out var subject);
            policy.Tick(14, false, 1, 1, true, new[] { subject with { AtShop = false } });
            Equal(0d, policy.WorkedSeconds, "leaving while paused still discards proof");
            Tick(policy, 15, subject);
            Check(!policy.TryTakeCompletion(out _), "return after pause must work again");
            Equal(0d, policy.WorkedSeconds, "return starts fresh");
        });
        Test("new threat timestamp between observations invalidates old work", () =>
        {
            var policy = new HeavyShieldMaintenancePolicy();
            Tick(policy, 6, Subject());
            Tick(policy, 10, Subject());
            // At the next observation the threat is gone and quiet time is already six seconds,
            // but the old four work seconds cannot be joined across that threat.
            Tick(policy, 18, Subject(lastThreat: 12));
            Check(!policy.TryTakeCompletion(out _), "cannot join work across unobserved threat");
            Equal(0d, policy.WorkedSeconds, "threat timestamp invalidates old work");
            Tick(policy, 19, Subject(lastThreat: 12));
            Equal(0d, policy.WorkedSeconds, "new job starts from zero");
        });
        Test("threat cancels work and restarts quiet window", () =>
        {
            var policy = new HeavyShieldMaintenancePolicy();
            Tick(policy, 6, Subject());
            Tick(policy, 10, Subject());
            Tick(policy, 11, Subject(threatened: true, lastThreat: 11));
            Equal(HeavyShieldMaintenancePhase.Idle, policy.Phase, "threat cancels");
            Tick(policy, 16.999, Subject(lastThreat: 11));
            Equal(HeavyShieldMaintenancePhase.Idle, policy.Phase, "quiet period incomplete");
            Tick(policy, 17, Subject(lastThreat: 11));
            Equal(0d, policy.WorkedSeconds, "new job starts from zero");
            Tick(policy, 25, Subject(lastThreat: 11));
            Check(policy.TryTakeCompletion(out _), "fresh quiet work completes");
        });
        Test("shop invalidation discards all queued jobs", () =>
        {
            var policy = Due(out var subject);
            policy.Tick(15, true, 1, 1, false, new[] { subject, Subject(B) });
            Equal(HeavyShieldMaintenancePhase.Idle, policy.Phase, "shop removed");
            Check(!policy.IsQueued(A, 1) && !policy.IsQueued(B, 1), "no orphan jobs");
            Tick(policy, 16, subject);
            Equal(0d, policy.WorkedSeconds, "new site must work again");
        });
        Test("new shop or world cannot inherit mature work", () =>
        {
            foreach (bool changeWorld in new[] { true, false })
            {
                var policy = Due(out var subject);
                policy.Tick(15, true, changeWorld ? 2 : 1, changeWorld ? 1 : 2, true, new[] { subject });
                Check(!policy.TryTakeCompletion(out _), "different context cannot complete old job");
                Equal(0d, policy.WorkedSeconds, "context discards work");
            }
        });
        Test("nonpositive context and invalid clock cannot emit", () =>
        {
            foreach (var row in new[] { (15d, 0L, 1L), (15d, 1L, 0L), (double.NaN, 1L, 1L),
                (double.PositiveInfinity, 1L, 1L), (double.NegativeInfinity, 1L, 1L), (-1d, 1L, 1L) })
            {
                var policy = Due(out var subject);
                policy.Tick(row.Item1, true, row.Item2, row.Item3, true, new[] { subject });
                Check(!policy.TryTakeCompletion(out _), "invalid clock/context rejects grant");
                Equal(HeavyShieldMaintenancePhase.Idle, policy.Phase, "invalid context clears ordinary work");
            }
        });
        Test("clock rollback cancels mature proof", () =>
        {
            var policy = Due(out var subject);
            Tick(policy, 13, subject);
            Equal(HeavyShieldMaintenancePhase.Idle, policy.Phase, "rollback cancels job");
            Check(!policy.TryTakeCompletion(out _), "rollback cannot grant");
            Tick(policy, 14, subject);
            Equal(0d, policy.WorkedSeconds, "new clock job starts fresh");
        });
        Test("leaving repair point requires fresh full work", () =>
        {
            var policy = new HeavyShieldMaintenancePolicy();
            Tick(policy, 6, Subject());
            Tick(policy, 10, Subject());
            Tick(policy, 11, Subject(atShop: false));
            Equal(0d, policy.WorkedSeconds, "leaving discards work");
            Tick(policy, 12, Subject());
            Equal(0d, policy.WorkedSeconds, "return arrival is zero");
            Tick(policy, 19.99, Subject());
            Check(!policy.TryTakeCompletion(out _), "must complete all eight again");
        });
        Test("receipt with new life cannot inherit progress", () =>
        {
            var policy = Due(out var subject);
            Tick(policy, 15, subject with { Life = 2 });
            Check(!policy.IsQueued(A, 1), "old life gone");
            Equal(2L, policy.ActiveLife, "new life is distinct job");
            Equal(0d, policy.WorkedSeconds, "no prior work inherited");
            Check(!policy.TryTakeCompletion(out _), "old proof discarded");
        });
        Test("removed or banner-bound subject cannot keep due job", () =>
        {
            var removed = Due(out _);
            Tick(removed, 15);
            Check(!removed.TryTakeCompletion(out _), "missing subject canceled");
            var banner = Due(out var subject);
            Tick(banner, 15, subject with { InBanner = true });
            Check(!banner.TryTakeCompletion(out _), "banner subject canceled");
        });
        Test("take moves to awaiting before returning and emits once", () =>
        {
            var policy = Due(out _);
            Check(policy.TryTakeCompletion(out var completion), "first completion");
            Check(completion.Serial > 0, "positive serial");
            Equal(HeavyShieldMaintenancePhase.AwaitingResult, policy.Phase, "transaction barrier active");
            Check(!policy.TryTakeCompletion(out _), "no duplicate grant");
            Check(!policy.ResolveCompletion(completion.Serial + 1, true, false), "wrong serial rejected");
            Equal(HeavyShieldMaintenancePhase.AwaitingResult, policy.Phase, "wrong serial no mutation");
            Check(policy.ResolveCompletion(completion.Serial, true, false), "matching serial accepted");
            Check(!policy.ResolveCompletion(completion.Serial, true, false), "double resolve rejected");
            Check(!policy.ResolveCompletion(completion.Serial, false, true), "wrong phase cannot hold again");
        });
        Test("definite rejection needs a complete new job", () =>
        {
            var policy = Due(out var subject);
            Check(policy.TryTakeCompletion(out var first), "first completion");
            Check(policy.ResolveCompletion(first.Serial, false, false), "rejected receipt resolved");
            Tick(policy, 15, subject);
            Equal(0d, policy.WorkedSeconds, "rejected attempt restarts zero");
            Tick(policy, 22.99, subject);
            Check(!policy.TryTakeCompletion(out _), "cannot recycle prior work");
            Tick(policy, 23, subject);
            Check(policy.TryTakeCompletion(out var second), "new full job completes");
            Check(second.Serial > first.Serial, "serial advances");
        });
        Test("accepted suppresses same stale snapshot until changed durability", () =>
        {
            var policy = Due(out var subject);
            Check(policy.TryTakeCompletion(out var completion), "completion taken");
            Check(policy.ResolveCompletion(completion.Serial, true, false), "accepted");
            Tick(policy, 20, subject);
            Tick(policy, 100, subject);
            Equal(HeavyShieldMaintenancePhase.Idle, policy.Phase, "unchanged old snapshot remains suppressed");
            Tick(policy, 101);
            Tick(policy, 102, subject);
            Equal(HeavyShieldMaintenancePhase.Idle, policy.Phase, "absence is not proof of changed life");
            Tick(policy, 103, subject with { Durability = 40 });
            Tick(policy, 104, subject with { Durability = 20, LastThreatAt = 104 });
            Tick(policy, 110, subject with { Durability = 20, LastThreatAt = 104 });
            Equal(HeavyShieldMaintenancePhase.Repairing, policy.Phase, "proved full then damaged can repair");
        });
        Test("accepted old life does not suppress new life", () =>
        {
            var policy = Due(out var subject);
            Check(policy.TryTakeCompletion(out var completion), "completion taken");
            Check(policy.ResolveCompletion(completion.Serial, true, false), "accepted");
            Tick(policy, 15, subject with { Life = 2 });
            Equal(2L, policy.ActiveLife, "new life can start");
            Equal(0d, policy.WorkedSeconds, "new life starts zero");
        });
        Test("ordinary invalid ticks cannot erase awaiting or unknown barriers", () =>
        {
            foreach (bool uncertain in new[] { false, true })
            {
                var policy = Due(out var subject);
                Check(policy.TryTakeCompletion(out var completion), "take before invalid observations");
                if (uncertain) Check(policy.ResolveCompletion(completion.Serial, false, true), "held unknown");
                var expected = uncertain ? HeavyShieldMaintenancePhase.HeldUnknown : HeavyShieldMaintenancePhase.AwaitingResult;
                policy.Tick(double.NaN, false, 0, 0, false, null!);
                policy.Tick(20, true, 2, 2, true, new[] { subject with { Threatened = true } });
                policy.Tick(21, true, 1, 1, true, new[] { subject, subject });
                Tick(policy, 100, subject, Subject(B));
                Equal(expected, policy.Phase, "barrier survives ordinary tick");
                Equal(A, policy.ActiveReceipt, "original transaction still held");
                Check(!policy.TryTakeCompletion(out _), "barrier cannot replay");
                if (uncertain) Check(!policy.ResolveCompletion(completion.Serial, true, false), "unknown cannot later blind-accept");
                else Check(policy.ResolveCompletion(completion.Serial, false, false), "explicit definite result releases awaiting");
            }
        });
        Test("uncertainty wins over accepted flag", () =>
        {
            var policy = Due(out _);
            Check(policy.TryTakeCompletion(out var completion), "take");
            Check(policy.ResolveCompletion(completion.Serial, true, true), "uncertain resolved");
            Equal(HeavyShieldMaintenancePhase.HeldUnknown, policy.Phase, "uncertainty dominant");
        });
        Test("reset invalidates old completion without reusing serial", () =>
        {
            var policy = Due(out var subject);
            Check(policy.TryTakeCompletion(out var old), "old completion");
            policy.Reset();
            Equal(HeavyShieldMaintenancePhase.Idle, policy.Phase, "reset clears job");
            Check(!policy.ResolveCompletion(old.Serial, true, false), "old cannot resolve after reset");
            Tick(policy, 6, subject);
            Tick(policy, 14, subject);
            Check(policy.TryTakeCompletion(out var current), "new completion");
            Check(current.Serial > old.Serial, "reset did not reuse serial");
            Check(!policy.ResolveCompletion(old.Serial, true, false), "old cannot release new outstanding job");
            Equal(HeavyShieldMaintenancePhase.AwaitingResult, policy.Phase, "new barrier preserved");
            Check(policy.ResolveCompletion(current.Serial, true, false), "new completion resolves");
        });
        Test("serial exhaustion fails closed across reset", () =>
        {
            var policy = Due(out var subject);
            // Exhaustion cannot be reached in a bounded test by iterating long.MaxValue repairs.
            typeof(HeavyShieldMaintenancePolicy).GetField("_serialCounter", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(policy, long.MaxValue);
            Check(!policy.TryTakeCompletion(out _), "counter must not wrap");
            policy.Reset();
            Tick(policy, 6, subject);
            Tick(policy, 14, subject);
            Check(!policy.TryTakeCompletion(out _), "reset cannot recycle exhausted counter");
        });
        Console.WriteLine($"{_passed} tests passed, {_failed} failed, {_checks} checks.");
        return _failed == 0 ? 0 : 1;
    }
}
