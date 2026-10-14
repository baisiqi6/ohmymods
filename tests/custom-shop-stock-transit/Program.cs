using System;
using System.Collections.Generic;
using KingdomEnhancedMod;

// stock worker host 回归（契约 integration-contract-v1）：CustomShopStockTransit 纯核行为。
// fake adapter 见 Fakes.cs。本套件不能证明 native/earlyAI 行为（任务记录验证边界）。

int total = 0, checks = 0;
var failures = new List<string>();

void Check(bool value, string name)
{
    checks++;
    if (!value) throw new Exception(name);
}

void Contains(string hay, string needle, string name) =>
    Check(hay != null && hay.IndexOf(needle, StringComparison.Ordinal) >= 0, name + " | " + hay);

void Test(string name, Action body)
{
    total++;
    try { body(); Console.WriteLine("ok   " + name); }
    catch (Exception e) { failures.Add(name); Console.WriteLine("FAIL " + name + " :: " + e.Message); }
}

static bool Throws<TEx>(Action a) where TEx : Exception
{
    try { a(); return false; }
    catch (TEx) { return true; }
}

// —— T1 库存数量 0..4 与容量上限 ——
Test("counts-0-to-4-and-capacity", () =>
{
    var t = new CustomShopStockTransit();
    Check(t.HeldCount == 0 && t.LastFault == null, "fresh transit is empty");
    var held = new List<FakeStockItem>();
    for (int n = 0; n < 4; n++)
    {
        var it = new FakeStockItem("gun-" + n) { EnemyPolicy = n == 0 ? 6 : 0 };
        held.Add(it);
        Check(t.Protect(it.Access()), "protect item " + n);
        Check(t.HeldCount == n + 1, "held count grows to " + (n + 1));
    }
    Check(t.IsHeld(held[2].Token), "is-held middle token");
    var fifth = new FakeStockItem("gun-4");
    Check(!t.Protect(fifth.Access()), "fifth protect refused at default capacity");
    Contains(t.LastFault, "capacity", "capacity fault is visible");
    Check(fifth.Writes == 0, "refused item is untouched natively");
    Check(t.HeldCount == 4, "count stays 4 after refusal");
    foreach (var it in held) Check(it.ProtocolOk, "read/write protocol respected for " + it.PaidId);
});

// —— T2 构造参数校验 ——
Test("ctor-validation", () =>
{
    Check(Throws<ArgumentOutOfRangeException>(() => new CustomShopStockTransit(0)), "maxItems<1 rejected");
    Check(Throws<ArgumentOutOfRangeException>(() => new CustomShopStockTransit(4, -1)), "negative blockedPolicy rejected");
});

// —— T3 adapter 委托缺失 ——
Test("protect-requires-full-adapter", () =>
{
    var t = new CustomShopStockTransit();
    var it = new FakeStockItem("g3");
    var a = it.Access(); a.Read = null;
    Check(!t.Protect(a), "null Read refused");
    Contains(t.LastFault, "adapter-missing:Read", "read delegate fault named");
    var b = it.Access(); b.SetEnemyPolicy = null;
    Check(!t.Protect(b), "null SetEnemyPolicy refused");
    Contains(t.LastFault, "adapter-missing:SetEnemyPolicy", "enemy setter fault named");
    Check(it.Writes == 0, "no writes without full adapter");
});

// —— T4 保护资格排除：友认领/敌认领/已拾取/已出库/dropped 槽 ——
Test("protect-eligibility-exclusions", () =>
{
    var cases = new (string Flag, string Expect)[] {
        ("friendly", "friendly-claim"), ("enemy", "enemy-claim"),
        ("picked", "picked"), ("stock", "not-stock"),
    };
    foreach (var (flag, expect) in cases)
    {
        var t = new CustomShopStockTransit();
        var it = new FakeStockItem("g4-" + flag);
        switch (flag)
        {
            case "friendly": it.FriendlyClaimed = true; break;
            case "enemy": it.EnemyClaimed = true; break;
            case "picked": it.PickedUp = true; break;
            default: it.StockOwned = false; break;
        }
        Check(!t.Protect(it.Access()), flag + " item excluded");
        Contains(t.LastFault, "ineligible:" + expect, flag + " fault names cause");
        Check(it.Writes == 0, flag + " item untouched");
    }
    var td = new CustomShopStockTransit();
    var drop = new FakeStockItem("g4-drop", slot: -1);
    Check(!td.Protect(drop.Access()), "dropped slot token excluded");
    Contains(td.LastFault, "bad-token", "dropped slot fault named");
    Check(drop.Writes == 0, "dropped token untouched");
});

// —— T5 捕获实际原值（敌策任意非 0）并按默认 blocked=5 双封锁+隐藏 ——
Test("protect-captures-actual-originals", () =>
{
    var t = new CustomShopStockTransit();
    var it = new FakeStockItem("g5") { FriendlyPolicy = 1, EnemyPolicy = 6 };
    Check(t.Protect(it.Access()), "protect succeeds");
    Check(it.FriendlyPolicy == 5 && it.EnemyPolicy == 5 && it.VisualParked, "block both with default 5 then hide");
    Check(t.Place(it.Access(), 5, 0, 0), "place restores");
    Check(it.FriendlyPolicy == 1 && it.EnemyPolicy == 6 && !it.VisualParked, "actual originals restored (enemy 6, not 0)");
    Check(it.X == 5, "place moved to rack");
});

// —— T6 重复 Protect 幂等（不重捕获/不重写），外来覆写字段 → 部分持有可见 ——
Test("repeat-protect-idempotent", () =>
{
    var t = new CustomShopStockTransit();
    var it = new FakeStockItem("g6") { EnemyPolicy = 6 };
    var acc = it.Access();
    Check(t.Protect(acc), "first protect");
    int w1 = it.Writes;
    Check(t.Protect(acc), "repeat protect succeeds");
    Check(it.Writes == w1, "repeat protect writes nothing");
    Check(t.LastFault == null, "repeat protect clears fault");
    it.FriendlyPolicy = 4; // 外来写者接管友策
    Check(!t.Protect(acc), "foreign field refuses repeat hold");
    Contains(t.LastFault, "foreign:FriendlyPolicy", "foreign field named");
    Check(t.HeldCount == 1 && t.IsHeld(it.Token), "partial lease retained");
    Check(!t.ReleaseAll(), "release-all reports the foreign field");
    Contains(t.LastFault, "foreign:FriendlyPolicy", "release-all foreign fault visible");
    Check(t.HeldCount == 0, "lease ended after foreign resolution");
    Check(it.FriendlyPolicy == 4 && it.EnemyPolicy == 6 && !it.VisualParked, "own fields restored, foreign value untouched");
});

// —— T7 同 PaidId 重新绑定：持有中拒绝，租约结束后允许 ——
Test("same-guid-rebind", () =>
{
    var t = new CustomShopStockTransit();
    var a = new FakeStockItem("gun-x") { EnemyPolicy = 6 };
    Check(t.Protect(a.Access()), "hold original identity");
    var b = new FakeStockItem("gun-x", pointer: 0x200) { EnemyPolicy = 6 };
    Check(!t.Protect(b.Access()), "rebind while held refused");
    Contains(t.LastFault, "identity-rebind", "rebind fault named");
    Check(b.Writes == 0 && t.IsHeld(a.Token), "original hold intact, new identity untouched");
    Check(t.Release(a.Token), "release original");
    Check(t.HeldCount == 0, "lease ended");
    var b2 = new FakeStockItem("gun-x", pointer: 0x200) { EnemyPolicy = 6 };
    Check(t.Protect(b2.Access()), "rebind allowed after lease end");
    Check(t.Place(b2.Access(), 70, 2, 0), "place rebound identity");
    Check(b2.EnemyPolicy == 6 && b2.X == 70, "rebound item restores its actual originals");
});

// —— T8 取得字段之间出现认领：中止并只恢复自己已写字段 ——
Test("claim-mid-acquire-aborts-and-rolls-back", () =>
{
    var t = new CustomShopStockTransit();
    var it = new FakeStockItem("g8") { FriendlyPolicy = 0, EnemyPolicy = 6 };
    it.FriendlyClaimBeforeWrite.Add("enemy"); // 友策已封锁后、敌策写入瞬间出现认领
    Check(!t.Protect(it.Access()), "protect aborted by mid-acquire claim");
    Contains(t.LastFault, "ineligible:friendly-claim", "claim fault named");
    Check(it.FriendlyPolicy == 0 && it.EnemyPolicy == 6, "both written fields restored to originals");
    Check(!it.Log.Exists(s => s.StartsWith("write:visual")), "visual never written");
    Check(it.FriendlyClaimed, "claim itself never cancelled");
    Check(t.HeldCount == 0, "no lease kept");
});

// —— T9 第二字段写前抛异常：回滚已写字段，不假设异常没副作用 ——
Test("partial-throw-before-field-write", () =>
{
    var t = new CustomShopStockTransit();
    var it = new FakeStockItem("g9") { FriendlyPolicy = 0, EnemyPolicy = 6 };
    it.FailBeforeWrite.Add("enemy");
    Check(!t.Protect(it.Access()), "protect fails on adapter throw");
    Contains(t.LastFault, "throw", "throw fault visible");
    Check(it.FriendlyPolicy == 0 && it.EnemyPolicy == 6, "fields back at originals after rollback");
    Check(!it.Log.Exists(s => s.StartsWith("write:enemy")), "enemy write never landed");
    Check(it.Log.Exists(s => s == "throw-before:enemy"), "hook fired as designed");
    Check(t.HeldCount == 0, "no lease kept");
});

// —— T10 最后字段写后抛异常：reread 恢复全部已写字段 ——
Test("partial-throw-after-field-write", () =>
{
    var t = new CustomShopStockTransit();
    var it = new FakeStockItem("g10") { FriendlyPolicy = 0, EnemyPolicy = 6 };
    it.FailAfterWrite.Add("visual");
    Check(!t.Protect(it.Access()), "protect fails after visual throw");
    Contains(t.LastFault, "throw", "throw fault visible");
    Check(it.FriendlyPolicy == 0 && it.EnemyPolicy == 6 && !it.VisualParked, "all three fields recovered");
    Check(t.HeldCount == 0, "no lease kept");
});

// —— T11 回滚也失败：保留 lease（不隐藏失败），ReleaseAll 稍后恢复 ——
Test("unrestorable-rollback-keeps-lease", () =>
{
    var t = new CustomShopStockTransit();
    var it = new FakeStockItem("g11") { FriendlyPolicy = 0, EnemyPolicy = 6 };
    it.FailBeforeWriteAt.Add(("enemy", 1));     // 敌策取得写入（第 1 次）失败
    it.FailBeforeWriteAt.Add(("friendly", 2)); // 取得失败后，友策回滚恢复写入（第 2 次）也抛异常
    Check(!t.Protect(it.Access()), "protect fails");
    Check(t.HeldCount == 1 && t.IsHeld(it.Token), "lease kept for retry (ownership not discarded)");
    Contains(t.LastFault, "lease-kept:FriendlyPolicy", "kept fields named");
    Check(it.FriendlyPolicy == 5 && it.EnemyPolicy == 6 && !it.VisualParked, "only the unrestored field still blocked (enemy never written, stays at original 6)");
    Check(t.ReleaseAll(), "retry resolves after fault clears");
    Check(it.FriendlyPolicy == 0 && it.EnemyPolicy == 6, "all originals restored on retry");
    Check(t.HeldCount == 0, "lease ended");
});

// —— T12 无店间隙收存 → 新货架放回（完整主流程）——
Test("no-site-holding-then-new-rack", () =>
{
    var t = new CustomShopStockTransit();
    var it = new FakeStockItem("g12") { FriendlyPolicy = 0, EnemyPolicy = 6 };
    it.X = 10; it.Y = 0; it.Z = 0;
    Check(t.Protect(it.Access()), "protect at old site");
    Check(it.FriendlyPolicy == 5 && it.EnemyPolicy == 5 && it.VisualParked, "blocked both and hidden");
    Check(t.Park(it.Access(), 50, 1, 0), "park at safe site");
    Check(it.X == 50 && it.Y == 1 && it.Z == 0, "parked at safe coordinates");
    Check(it.FriendlyPolicy == 5 && it.EnemyPolicy == 5 && it.VisualParked, "park retains blocked state");
    Check(t.HeldCount == 1 && t.IsHeld(it.Token), "still held during no-site gap");
    Check(t.Place(it.Access(), 70, 2, 0), "place at new rack");
    Check(it.X == 70 && it.Y == 2 && it.Z == 0, "moved to rack position");
    Check(it.FriendlyPolicy == 0 && it.EnemyPolicy == 6 && !it.VisualParked, "own fields CAS-restored, lease ended");
    Check(t.HeldCount == 0, "held count zero after place");
    Check(it.ProtocolOk, "read/write protocol respected");
});

// —— T13 Release 不回滚位置（永不传回不安全旧址）——
Test("release-keeps-current-position", () =>
{
    var t = new CustomShopStockTransit();
    var it = new FakeStockItem("g13") { EnemyPolicy = 6 };
    it.X = 10;
    Check(t.Protect(it.Access()), "protect");
    Check(t.Park(it.Access(), 50, 1, 0), "park");
    Check(t.Release(it.Token), "release");
    Check(it.X == 50 && it.Y == 1 && it.Z == 0, "release leaves parked position (no rollback)");
    Check(it.FriendlyPolicy == 0 && it.EnemyPolicy == 6 && !it.VisualParked, "own fields restored");
    Check(t.HeldCount == 0, "lease ended");
});

// —— T14 持有期间外来者接管字段：CAS 拒绝覆写，只恢复自己的字段 ——
Test("foreign-writer-cas-during-hold", () =>
{
    var t = new CustomShopStockTransit();
    var it = new FakeStockItem("g14") { EnemyPolicy = 6 };
    it.X = 10;
    Check(t.Protect(it.Access()), "protect");
    it.FriendlyPolicy = 0; // 外来者写友策
    Check(!t.Place(it.Access(), 70, 2, 0), "place refused under foreign field");
    Contains(t.LastFault, "foreign:FriendlyPolicy", "foreign field named");
    Check(it.X == 10, "no move while protection was taken over");
    Check(it.FriendlyPolicy == 0, "foreign value never overwritten");
    Check(it.EnemyPolicy == 6 && !it.VisualParked, "own fields restored");
    Check(t.HeldCount == 0, "own field ownership ended");
});

// —— T15 恢复写入前抛异常：lease 保留（字段仍属我们），ReleaseAll reread/CAS 后恢复 ——
Test("restore-throw-keeps-lease-then-recovers", () =>
{
    var t = new CustomShopStockTransit();
    var it = new FakeStockItem("g15") { FriendlyPolicy = 0, EnemyPolicy = 6 };
    it.X = 10;
    Check(t.Protect(it.Access()), "protect");
    it.FailBeforeWrite.Add("enemy"); // Place 恢复敌策时写前抛异常
    Check(!t.Place(it.Access(), 70, 2, 0), "place reports unresolved field");
    Contains(t.LastFault, "unresolved:EnemyPolicy", "unresolved field named");
    Check(t.HeldCount == 1 && t.IsHeld(it.Token), "lease kept for retry");
    Check(it.X == 70 && it.Y == 2, "move already verified, kept");
    Check(it.VisualParked == false && it.FriendlyPolicy == 0, "resolved fields already restored");
    Check(it.EnemyPolicy == 5, "enemy still blocked (write did not land)");
    Check(t.ReleaseAll(), "release-all recovers via reread/CAS");
    Check(it.EnemyPolicy == 6, "enemy restored to actual original");
    Check(t.HeldCount == 0, "lease ended");
});

// —— T15b 恢复写入后被外来者覆写：验证读发现，绝不再次覆写 ——
Test("foreign-after-restore-write-never-overwritten", () =>
{
    var t = new CustomShopStockTransit();
    var it = new FakeStockItem("g15b") { FriendlyPolicy = 0, EnemyPolicy = 6 };
    Check(t.Protect(it.Access()), "protect");
    it.ForeignAfterWrite.Add("enemy"); // 我们写回 6 后、验证读前，外来者改成 3
    Check(!t.Place(it.Access(), 70, 2, 0), "place reports foreign field");
    Contains(t.LastFault, "foreign:EnemyPolicy", "foreign field named");
    Check(it.EnemyPolicy == 3, "foreign value left untouched");
    Check(t.HeldCount == 0, "ownership of that field ended");
    int w = it.Writes;
    Check(t.ReleaseAll(), "release-all with no leases is a no-op");
    Check(it.Writes == w && it.EnemyPolicy == 3, "no rewrite of the foreign value");
});

Test("restore-silent-noop-retains-lease", () =>
{
    foreach (string field in new[] { "visual", "enemy", "friendly" })
    {
        var t = new CustomShopStockTransit();
        var it = new FakeStockItem("silent-" + field) { EnemyPolicy = 6 };
        var a = it.Access(); Check(t.Protect(a), "protect " + field);
        var visual = a.SetVisualParked; var enemy = a.SetEnemyPolicy; var friendly = a.SetFriendlyPolicy;
        if (field == "visual") a.SetVisualParked = (token, value) => { };
        if (field == "enemy") a.SetEnemyPolicy = (token, value) => { };
        if (field == "friendly") a.SetFriendlyPolicy = (token, value) => { };
        Check(!t.ReleaseAll() && t.IsHeld(a.Token), "silent native restore retains responsibility " + field);
        a.SetVisualParked = visual; a.SetEnemyPolicy = enemy; a.SetFriendlyPolicy = friendly;
        Check(t.ReleaseAll() && !t.IsHeld(a.Token), "later native restore closes responsibility " + field);
        Check(!it.VisualParked && it.FriendlyPolicy == 0 && it.EnemyPolicy == 6, "exact native originals restored " + field);
    }
});

// —— T16 Place 移动后瞬间出现认领：不回滚位置，恢复自己的字段并结束租约 ——
Test("claim-after-move-during-place", () =>
{
    var t = new CustomShopStockTransit();
    var it = new FakeStockItem("g16") { EnemyPolicy = 6 };
    it.X = 10;
    Check(t.Protect(it.Access()), "protect");
    it.FriendlyClaimAfterWrite.Add("move");
    Check(!t.Place(it.Access(), 70, 2, 0), "place aborted by claim after move");
    Contains(t.LastFault, "ineligible:friendly-claim", "claim fault named");
    Check(it.X == 70 && it.Y == 2, "position stays at rack (no rollback)");
    Check(it.FriendlyPolicy == 0 && it.EnemyPolicy == 6 && !it.VisualParked, "own fields restored, lease ended");
    Check(it.FriendlyClaimed, "claim never cancelled");
    Check(t.HeldCount == 0, "lease ended");
});

// —— T17 Park 移动写入瞬间出现认领：移动已发生不回退，恢复自己的字段 ——
Test("claim-before-move-during-park", () =>
{
    var t = new CustomShopStockTransit();
    var it = new FakeStockItem("g17") { EnemyPolicy = 6 };
    it.X = 10;
    Check(t.Protect(it.Access()), "protect");
    it.FriendlyClaimBeforeWrite.Add("move");
    Check(!t.Park(it.Access(), 50, 1, 0), "park aborted by claim");
    Contains(t.LastFault, "ineligible:friendly-claim", "claim fault named");
    Check(it.X == 50 && it.Y == 1, "move already applied stays (no rollback)");
    Check(it.FriendlyPolicy == 0 && it.EnemyPolicy == 6 && !it.VisualParked, "own fields restored, lease ended");
    Check(t.HeldCount == 0, "lease ended");
});

// —— T19 伪冷启动：旧 token 失效只丢弃托管租约（不触原生），新身份重捕获实际值 ——
Test("fake-cold-recapture", () =>
{
    var t = new CustomShopStockTransit();
    var it = new FakeStockItem("gun-9") { EnemyPolicy = 0 };
    it.X = 10;
    var oldAccess = it.Access();
    var oldToken = it.Token;
    Check(t.Protect(oldAccess), "protect before cold");
    int w = it.Writes;
    // 模拟冷加载：原对象失效回收，池给同 PaidId 的新对象（新 Pointer/Life/Instance，敌策初始值 6）
    it.Valid = false;
    it.Pointer = 0x999; it.Life = 99; it.Instance = 2; it.EnemyPolicy = 6;
    it.FriendlyPolicy = 0; it.VisualParked = false; it.X = 42; it.Y = 0; it.Z = 0; // 池重初始化给任意新值
    Check(t.Release(oldToken), "release of recycled token drops managed lease");
    Check(it.Writes == w, "no native writes through the stale token");
    Check(t.HeldCount == 0 && !t.IsHeld(oldToken), "stale token no longer held");
    it.Valid = true;
    Check(t.Protect(it.Access()), "rebind to the fresh native identity");
    Check(it.EnemyPolicy == 5 && it.FriendlyPolicy == 5, "fresh identity blocked");
    Check(t.Place(it.Access(), 80, 0, 0), "place fresh identity");
    Check(it.EnemyPolicy == 6 && it.FriendlyPolicy == 0, "actual cold-initialized enemy policy 6 recaptured and restored (not assumed 0)");
    Check(it.X == 80, "moved to rack");
});

// —— T20 未知 token 的 Release 拒绝 ——
Test("release-unknown-token", () =>
{
    var t = new CustomShopStockTransit();
    var tok = new ShopStockToken("nope", 1, 1, 1, 1, 1, new object());
    Check(!t.Release(tok), "unknown token refused");
    Contains(t.LastFault, "unknown-token", "unknown token fault named");
});

// —— T21 ReleaseAll 幂等 ——
Test("release-all-idempotent", () =>
{
    var t = new CustomShopStockTransit();
    Check(t.ReleaseAll(), "empty release-all succeeds");
    Check(t.LastFault == null, "no fault on empty release-all");
    var it = new FakeStockItem("g21") { EnemyPolicy = 6 };
    Check(t.Protect(it.Access()), "protect");
    Check(t.Release(it.Token), "release");
    Check(t.ReleaseAll(), "release-all after empty is still true");
    Check(t.ReleaseAll(), "and again");
});

// —— T22 ReleaseAll 混合中止分支：健康/外来接管/恢复抛异常 ——
Test("release-all-abort-branches", () =>
{
    var t = new CustomShopStockTransit();
    var a = new FakeStockItem("ga") { EnemyPolicy = 6 };
    var b = new FakeStockItem("gb") { EnemyPolicy = 6 };
    var c = new FakeStockItem("gc") { EnemyPolicy = 6 };
    Check(t.Protect(a.Access()), "protect a");
    Check(t.Protect(b.Access()), "protect b");
    Check(t.Protect(c.Access()), "protect c");
    b.EnemyPolicy = 3;          // 外来者接管 b 的敌策
    c.FailBeforeWrite.Add("enemy"); // c 恢复敌策时写前抛异常
    Check(!t.ReleaseAll(), "release-all reports mixed faults");
    Check(t.HeldCount == 1 && t.IsHeld(c.Token), "only the unresolved lease kept");
    Check(a.FriendlyPolicy == 0 && a.EnemyPolicy == 6 && !a.VisualParked, "a fully restored");
    Check(b.EnemyPolicy == 3, "b foreign value preserved");
    Check(b.FriendlyPolicy == 0 && !b.VisualParked, "b own fields restored");
    Check(c.EnemyPolicy == 5 && c.FriendlyPolicy == 0 && !c.VisualParked, "c enemy still blocked, others restored");
    Check(t.ReleaseAll(), "retry after fault clears resolves c");
    Check(t.HeldCount == 0 && c.EnemyPolicy == 6, "c fully restored on retry");
});

// —— T23 move 写入丢失：可验证拒绝并保留 lease 重试 ——
Test("move-unverified-keeps-lease", () =>
{
    var t = new CustomShopStockTransit();
    var it = new FakeStockItem("g23") { EnemyPolicy = 6 };
    it.X = 10;
    Check(t.Protect(it.Access()), "protect");
    it.LoseMoveWrite = true;
    Check(!t.Park(it.Access(), 50, 1, 0), "lost move detected");
    Contains(t.LastFault, "move-unverified", "move-unverified fault named");
    Check(t.IsHeld(it.Token) && t.HeldCount == 1, "lease kept for retry");
    Check(it.X == 10, "position unchanged");
    it.LoseMoveWrite = false;
    Check(t.Park(it.Access(), 50, 1, 0), "retry parks");
    Check(it.X == 50 && it.Y == 1, "retry moved");
});

// —— T24 非有限坐标拒绝 ——
Test("non-finite-coordinates-refused", () =>
{
    var t = new CustomShopStockTransit();
    var it = new FakeStockItem("g24") { EnemyPolicy = 6 };
    Check(t.Protect(it.Access()), "protect");
    int w = it.Writes;
    Check(!t.Park(it.Access(), float.NaN, 0, 0), "nan park refused");
    Check(!t.Park(it.Access(), 0, float.NegativeInfinity, 0), "infinite park refused");
    Check(!t.Place(it.Access(), float.PositiveInfinity, 0, 0), "infinite place refused");
    Contains(t.LastFault, "coord-invalid", "coord fault named");
    Check(it.Writes == w && t.IsHeld(it.Token), "no writes, lease kept");
});

// —— T25 完整周期后 Owner 引用更换的重绑定 ——
Test("owner-change-rebind-after-cycle", () =>
{
    var t = new CustomShopStockTransit();
    var it = new FakeStockItem("g25") { EnemyPolicy = 6 };
    Check(t.Protect(it.Access()), "first cycle protect");
    Check(t.Place(it.Access(), 30, 0, 0), "first cycle place");
    Check(t.HeldCount == 0, "first cycle done");
    it.Owner = new object(); // Root 换 adapter/owner 引用后同付身份重绑
    var acc2 = it.Access();
    Check(t.Protect(acc2), "owner-changed rebind allowed after lease end");
    Check(t.Place(acc2, 80, 0, 0), "second cycle place");
    Check(it.X == 80 && it.EnemyPolicy == 6 && it.FriendlyPolicy == 0, "second cycle restores actual values");
});

// —— T27 token 全字段 + Owner 引用相等 ——
Test("token-equality-all-fields-and-owner", () =>
{
    var o = new object();
    var t1 = new ShopStockToken("p", 1, 2, 3, 4, 5, o);
    Check(t1 == new ShopStockToken("p", 1, 2, 3, 4, 5, o), "identical tokens equal");
    Check(t1 != new ShopStockToken("p", 1, 2, 3, 4, 5, new object()), "different owner reference unequal");
    Check(t1 != new ShopStockToken("p", 1, 9, 3, 4, 5, o), "different life unequal");
    Check(t1 != new ShopStockToken("q", 1, 2, 3, 4, 5, o), "different paid id unequal");
    Check(t1 != new ShopStockToken("p", 1, 2, 3, 4, 6, o), "different slot unequal");
    Check(t1.Equals((object)new ShopStockToken("p", 1, 2, 3, 4, 5, o)), "boxed equals works");
    Check(t1.GetHashCode() == new ShopStockToken("p", 1, 2, 3, 4, 5, o).GetHashCode(), "equal tokens hash equal");
    Check(!t1.Equals(null), "null not equal");
});

// —— T28 自定义 blockedPolicy/maxItems ——
Test("custom-policy-and-capacity", () =>
{
    var t = new CustomShopStockTransit(1, 6);
    var x1 = new FakeStockItem("x1");
    Check(t.Protect(x1.Access()), "custom transit protects first");
    Check(x1.FriendlyPolicy == 6 && x1.EnemyPolicy == 6, "custom blockedPolicy=6 used for both");
    var x2 = new FakeStockItem("x2");
    Check(!t.Protect(x2.Access()), "custom max=1 refuses second");
    Contains(t.LastFault, "capacity", "custom capacity fault");
});

Console.WriteLine();
Console.WriteLine(failures.Count == 0
    ? $"PASS {checks} checks / {total} tests"
    : $"FAIL {failures.Count}/{total} tests failed ({checks} checks passed)");
return failures.Count;
