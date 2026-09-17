// 英雄守墙朝向 + 有界邻居诊断的无游戏进程回归（production files 直接编译进来，跑真实代码路径）。
// review 修订：写失败半途/读失败/归还失败重试/停用重试/未知状态/无邻居/重复 setup 预算/换世界。
using System;
using System.Collections.Generic;
using KingdomEnhancedMod;
using UnityEngine;

int passed = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); passed++; }
void Eq<T>(T expected, T actual, string name)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception("FAIL: " + name + " expected=" + expected + " actual=" + actual);
    passed++;
}

Kingdom kingdom = null;
World world = null;

Transform MakeTransform()
{
    var go = new GameObject();
    return go.Add(new Transform());
}

Archer MakeArcher(float x, Side side, float goalX, int gotoState = 8)
{
    Archer archer = new GameObject().Add(new Archer());
    archer.transform.position = new Vector3(x, 0.875f, 0f);
    Mover mover = archer.gameObject.Add(new Mover());
    archer._mover = mover;
    mover.goalMode = Mover.GoalMode.Position;
    mover._goalPosition = goalX;
    mover._movingToGoal.value = false;
    archer._guardSide = side;
    archer.behaviour = new Coatsink.Common.Haglet { started = true, latestGoto = gotoState };
    archer.shoot = new Coatsink.Common.Haglet { started = false };
    archer._damageable = new Damageable();
    HeroArcherRuntime.Lives[archer.gameObject.GetInstanceID()] = 1;
    return archer;
}

void Reset()
{
    HeroArcherGuardFacing.Clear(); // Restore while the previous life's identity still exists.
    Managers.Inst = new Managers();
    kingdom = Managers.Inst.kingdom;
    world = Managers.Inst.world;
    Managers.Inst.director = new Director { currentTime = 22f, IsNight = true };
    world.gameLayer = MakeTransform();
    kingdom.campfirePosition = 0f;
    kingdom.Left = -50f;
    kingdom.Right = 50f;
    kingdom.Archers = new Il2CppSystem.Collections.Generic.HashSet<Archer>();
    HeroArcherRuntime.Enabled = true;
    HeroArcherRuntime.Lives.Clear();
    HeroArcherRuntime.Denied.Clear();
    HeroArcherGuardFacing.Clear();
    HeroArcherLiveDiagnostics.Clear();
    KingdomEnhancedPlugin.Instance.LogSource.Lines.Clear();
    Time.time = 100f;
}

void Frame(Archer archer)
{
    HeroArcherGuardFacing.Tick();
    HeroArcherGuardFacing.Evaluate(archer);
}

bool LogContains(string needle) => KingdomEnhancedPlugin.Instance.LogSource.Lines.Exists(l => l.Contains(needle, StringComparison.Ordinal));
int LogCount(string needle)
{
    int count = 0;
    foreach (string line in KingdomEnhancedPlugin.Instance.LogSource.Lines) if (line.Contains(needle, StringComparison.Ordinal)) count++;
    return count;
}
Coatsink.Common.Haglet Goto(Archer archer) => (Coatsink.Common.Haglet)archer.behaviour;

// ============================================================
// 1) 学习 + 持有：goto==8 的原生守位目标点
// ============================================================
Reset();
Archer right = MakeArcher(46f, Side.Right, 46f);
HeroArcherGuardFacing.Tick();
Check(HeroArcherGuardFacing.OwnedCount == 0, "no ownership before any evaluate");
HeroArcherGuardFacing.Evaluate(right);
Eq(Mover.FacingMode.Right, right._mover.facingMode, "right-side wall guard faces outward");
Eq(1, right._mover.SetFacingCalls, "exactly one facing write on takeover");
Check(HeroArcherGuardFacing.OwnsFacing(right), "facing responsibility recorded");
Eq(1, HeroArcherGuardFacing.OwnedCount, "owned count is one");
for (int i = 0; i < 5; i++) Frame(right);
Eq(1, right._mover.SetFacingCalls, "holding does not rewrite every frame");
Eq(Mover.FacingMode.Right, right._mover.facingMode, "still outward while holding");

Reset();
Archer left = MakeArcher(-46f, Side.Left, -46f);
Frame(left);
Eq(Mover.FacingMode.Left, left._mover.facingMode, "left-side wall guard faces outward");
Check(HeroArcherGuardFacing.OwnsFacing(left), "left responsibility recorded");

Reset();
Archer neutralRight = MakeArcher(46f, Side.Neutral, 46f);
Frame(neutralRight);
Eq(Mover.FacingMode.Right, neutralRight._mover.facingMode, "neutral guard side uses campfire-relative right");
Reset();
Archer neutralLeft = MakeArcher(-46f, Side.Neutral, -46f);
Frame(neutralLeft);
Eq(Mover.FacingMode.Left, neutralLeft._mover.facingMode, "neutral guard side uses campfire-relative left");

// 学习期 sanity：明显退化的目标不学、不写
Reset();
Archer degenerate = MakeArcher(46f, Side.Right, 250f);
Frame(degenerate);
Eq(0, degenerate._mover.SetFacingCalls, "degenerate guard goal is not learned");

// ============================================================
// 2) 租约跨 8→1：守位点不变则持有；换目的地/未知状态/白天释放并作废租约
// ============================================================
Reset();
Archer parked = MakeArcher(46f, Side.Right, 46f);
Frame(parked);
Eq(Mover.FacingMode.Right, parked._mover.facingMode, "learned in wall state");
Goto(parked).latestGoto = 1;                       // 原生 8→1 停留
Frame(parked);
Eq(Mover.FacingMode.Right, parked._mover.facingMode, "lease retained across 8->1 at the guard point");
Check(HeroArcherGuardFacing.OwnsFacing(parked), "still owned after 8->1");
Eq(1, parked._mover.SetFacingCalls, "8->1 retention issues no extra write");
parked._mover._goalPosition = 30f;                 // 原生给了新的目的地（捡币等）
Frame(parked);
Eq(Mover.FacingMode.Ahead, parked._mover.facingMode, "new destination releases and restores Ahead");
Check(!HeroArcherGuardFacing.OwnsFacing(parked), "new destination drops ownership");
Eq(Mover.FacingMode.Ahead, parked._mover.facingMode, "lease invalidated by destination change");

Reset();
Archer noEvidence = MakeArcher(46f, Side.Right, 46f, gotoState: 1);
Frame(noEvidence);
Eq(0, noEvidence._mover.SetFacingCalls, "idle state without prior wall evidence never writes");
noEvidence._mover.facingMode = Mover.FacingMode.Left;   // 原生/第三方朝向不被覆盖
Frame(noEvidence);
Eq(Mover.FacingMode.Left, noEvidence._mover.facingMode, "no-evidence idle does not fight native facing");

Reset();
Archer taskLeft = MakeArcher(46f, Side.Right, 46f);
Frame(taskLeft);
Goto(taskLeft).latestGoto = 16;                    // 狩猎
Frame(taskLeft);
Eq(Mover.FacingMode.Ahead, taskLeft._mover.facingMode, "non-whitelisted task releases and restores");
Goto(taskLeft).latestGoto = 8;                     // 回到城墙态：重新学习
Frame(taskLeft);
Eq(Mover.FacingMode.Right, taskLeft._mover.facingMode, "wall state re-learns after a task gap");

Reset();
foreach (int state in new[] { 2, 5, 16, 32, 64, 1024, 2048, 8192, 16384, 65536 })
{
    Archer unknown = MakeArcher(46f, Side.Right, 46f, gotoState: state);
    Frame(unknown);
    Eq(0, unknown._mover.SetFacingCalls, "unknown/blacklisted state " + state + " never writes");
}

Reset();
Archer dawn = MakeArcher(46f, Side.Right, 46f);
Frame(dawn);
Managers.Inst.director.IsNight = false;
Frame(dawn);
Eq(Mover.FacingMode.Ahead, dawn._mover.facingMode, "daylight restores Ahead");
Check(!HeroArcherGuardFacing.OwnsFacing(dawn), "daylight drops ownership");
Managers.Inst.director.IsNight = true;
Frame(dawn);
Eq(Mover.FacingMode.Right, dawn._mover.facingMode, "next night re-learns and takes over");

Reset();
Archer objectFollow = MakeArcher(46f, Side.Right, 46f);
Frame(objectFollow);
objectFollow._mover.goalMode = Mover.GoalMode.Object;   // 跟随对象
Frame(objectFollow);
Eq(Mover.FacingMode.Ahead, objectFollow._mover.facingMode, "Object follow goal releases the lease");

// ============================================================
// 3) 静止/任务挂起：离位、移动、暂停、射击、骑士/编队/登船/塔位/玩家控制
// ============================================================
Reset();
Archer displaced = MakeArcher(46f, Side.Right, 46f);
Frame(displaced);
displaced.transform.position = new Vector3(40f, 0.875f, 0f);   // 离开守位点
Frame(displaced);
Eq(Mover.FacingMode.Ahead, displaced._mover.facingMode, "leaving the guard point restores Ahead");
displaced.transform.position = new Vector3(46f, 0.875f, 0f);
Frame(displaced);
Eq(Mover.FacingMode.Right, displaced._mover.facingMode, "returning to the guard point re-asserts");

Reset();
Archer moving = MakeArcher(46f, Side.Right, 46f);
Frame(moving);
moving._mover._movingToGoal.value = true;
Frame(moving);
Eq(Mover.FacingMode.Ahead, moving._mover.facingMode, "moving restores native Ahead");
Check(!HeroArcherGuardFacing.OwnsFacing(moving), "moving drops ownership");

Reset();
Archer paused = MakeArcher(46f, Side.Right, 46f);
Frame(paused);
paused._mover._pauseTimeout = 5f;
Frame(paused);
Eq(Mover.FacingMode.Ahead, paused._mover.facingMode, "native pause restores Ahead");
paused._mover._pauseTimeout = 0f;
Frame(paused);
Eq(Mover.FacingMode.Right, paused._mover.facingMode, "pause expiry re-asserts outward");

Reset();
Archer shooter = MakeArcher(46f, Side.Right, 46f);
Frame(shooter);
Eq(Mover.FacingMode.Right, shooter._mover.facingMode, "outward before the volley");
((Coatsink.Common.Haglet)shooter.shoot).started = true;        // shoot 协程运行 = 射击/准备
Frame(shooter);
Eq(Mover.FacingMode.Ahead, shooter._mover.facingMode, "shooting restores native aiming");
((Coatsink.Common.Haglet)shooter.shoot).started = false;
Frame(shooter);
Eq(Mover.FacingMode.Right, shooter._mover.facingMode, "volley end re-asserts outward");

Action<Archer>[] excluded =
{
    a => a._knight = new Knight(),
    a => a._currentFormation = new Formation(),
    a => a._embarkee = new Embarkee { IsEmbarked = true },
    a => a._embarkee = new Embarkee { EmbarkableTarget = new object() },
    a => a._guardSlot = new GuardSlot(),
    a => a.inGuardSlot = true,
    a => a.PlayerControlled = true,
    a => a._huntingTarget = new object(),
};
foreach (Action<Archer> change in excluded)
{
    Reset();
    Archer excludedArcher = MakeArcher(46f, Side.Right, 46f);
    change(excludedArcher);
    Frame(excludedArcher);
    Eq(0, excludedArcher._mover.SetFacingCalls, "excluded hero state never writes facing");
}

// ============================================================
// 4) 外来朝向 & 非英雄
// ============================================================
Reset();
Archer aiming = MakeArcher(46f, Side.Right, 46f);
aiming._mover.facingMode = Mover.FacingMode.Target;
Frame(aiming);
Eq(Mover.FacingMode.Target, aiming._mover.facingMode, "native Target is never overwritten");
Eq(0, aiming._mover.SetFacingCalls, "no write against Target");

Reset();
Archer foreign = MakeArcher(46f, Side.Right, 46f);
Frame(foreign);
foreign._mover.facingMode = Mover.FacingMode.Left;    // 第三方固定侧
Frame(foreign);
Eq(Mover.FacingMode.Left, foreign._mover.facingMode, "foreign fixed side preserved");
Check(!HeroArcherGuardFacing.OwnsFacing(foreign), "foreign write drops ownership");
Eq(1, foreign._mover.SetFacingCalls, "no rewrite after foreign write");
Frame(foreign);
Eq(Mover.FacingMode.Left, foreign._mover.facingMode, "still never fights the foreign value");

Reset();
Archer reassert = MakeArcher(46f, Side.Right, 46f);
Frame(reassert);
reassert._mover.facingMode = Mover.FacingMode.Ahead;  // 原生重置回 Ahead
Frame(reassert);
Eq(Mover.FacingMode.Right, reassert._mover.facingMode, "Ahead reset is re-asserted");
Check(HeroArcherGuardFacing.OwnsFacing(reassert), "ownership retained across re-assert");
Eq(2, reassert._mover.SetFacingCalls, "re-assert writes once");

Reset();
Archer notHero = MakeArcher(46f, Side.Right, 46f);
HeroArcherRuntime.Denied.Add(notHero);
Frame(notHero);
Eq(0, notHero._mover.SetFacingCalls, "non-hero is not taken over");
HeroArcherRuntime.Denied.Clear();
Frame(notHero);
Eq(Mover.FacingMode.Right, notHero._mover.facingMode, "hero again after eligibility returns");

Reset();
Archer revoked = MakeArcher(46f, Side.Right, 46f);
Frame(revoked);
HeroArcherRuntime.Denied.Add(revoked);
Frame(revoked);
Eq(Mover.FacingMode.Ahead, revoked._mover.facingMode, "revoke restores Ahead");
Check(!HeroArcherGuardFacing.OwnsFacing(revoked), "revoke drops ownership");

// ============================================================
// 5) 失败路径：写失败半途 / 读失败 / 归还失败重试 / 停用重试 / 身份变化
// ============================================================
// 5a) setter 未写入就抛错：可能已写入 → 责任保留；下一帧读回证明字段未变 → 重试写入
Reset();
Archer brokenSet = MakeArcher(46f, Side.Right, 46f);
brokenSet._mover.ThrowOnSetFacing = true;
Frame(brokenSet);
Eq(Mover.FacingMode.Ahead, brokenSet._mover.facingMode, "failed setter left the native value");
Check(HeroArcherGuardFacing.OwnsFacing(brokenSet), "setter exception keeps possible-write responsibility");
brokenSet._mover.ThrowOnSetFacing = false;
Frame(brokenSet);
Eq(Mover.FacingMode.Right, brokenSet._mover.facingMode, "recovers once the write works");
Eq(2, brokenSet._mover.SetFacingCalls, "recovery writes once more");
Managers.Inst.director.IsNight = false;
Frame(brokenSet);
Eq(Mover.FacingMode.Ahead, brokenSet._mover.facingMode, "release after the failed-then-retried write");

// 5b) setter 已写入后抛错（半途失败）：责任必须保留，下一帧读回即确认
Reset();
Archer halfWrite = MakeArcher(46f, Side.Right, 46f);
halfWrite._mover.ThrowAfterSetFacing = true;
Frame(halfWrite);
Eq(Mover.FacingMode.Right, halfWrite._mover.facingMode, "half-time write did apply");
Check(HeroArcherGuardFacing.OwnsFacing(halfWrite), "half-time write keeps responsibility");
Eq(1, halfWrite._mover.SetFacingCalls, "half-time write happened once");
halfWrite._mover.ThrowAfterSetFacing = false;
Frame(halfWrite);
Check(HeroArcherGuardFacing.OwnsFacing(halfWrite), "readback confirms the half-time write");
Eq(1, halfWrite._mover.SetFacingCalls, "confirmation issues no extra write");
Managers.Inst.director.IsNight = false;
Frame(halfWrite);
Eq(Mover.FacingMode.Ahead, halfWrite._mover.facingMode, "half-time write is restored on release");

// 5c) 读 facingMode 抛错：接管前读失败 → 不写、不持有
Reset();
Archer brokenRead = MakeArcher(46f, Side.Right, 46f);
brokenRead._mover.ThrowOnReadFacing = true;
Frame(brokenRead);
Eq(0, brokenRead._mover.SetFacingCalls, "read failure blocks the takeover write");
Check(!HeroArcherGuardFacing.OwnsFacing(brokenRead), "read failure claims no responsibility");
brokenRead._mover.ThrowOnReadFacing = false;
Frame(brokenRead);
Eq(Mover.FacingMode.Right, brokenRead._mover.facingMode, "read recovery allows takeover");

// 5d) 归还失败（setter 抛错）：责任保留，下一帧重试成功
Reset();
Archer restoreFail = MakeArcher(46f, Side.Right, 46f);
Frame(restoreFail);
restoreFail._mover.OnSetFacing = (mode, target) =>
{
    if (mode == Mover.FacingMode.Ahead) throw new Exception("restore failed");
    restoreFail._mover.facingMode = mode;
};
Managers.Inst.director.IsNight = false;
Frame(restoreFail);
Eq(Mover.FacingMode.Right, restoreFail._mover.facingMode, "failed restore leaves the field as-is");
Check(HeroArcherGuardFacing.OwnsFacing(restoreFail), "failed restore keeps responsibility");
Check(HeroArcherGuardFacing.ReleasePending(restoreFail), "failed restore is pending retry");
HeroArcherGuardFacing.Tick();
Check(HeroArcherGuardFacing.OwnsFacing(restoreFail), "pending retry is still owned after a failing tick");
restoreFail._mover.OnSetFacing = null;
HeroArcherGuardFacing.Tick();
Eq(Mover.FacingMode.Ahead, restoreFail._mover.facingMode, "next tick retry completes the restore");
Check(!HeroArcherGuardFacing.OwnsFacing(restoreFail), "completed retry clears responsibility");

// 5e) 停用状态也重试未清的归还责任
Reset();
Archer offRetry = MakeArcher(46f, Side.Right, 46f);
Frame(offRetry);
offRetry._mover.OnSetFacing = (mode, target) =>
{
    if (mode == Mover.FacingMode.Ahead) throw new Exception("restore failed");
    offRetry._mover.facingMode = mode;
};
HeroArcherRuntime.Enabled = false;
HeroArcherGuardFacing.Tick();
Check(HeroArcherGuardFacing.OwnsFacing(offRetry), "disabled tick keeps the failed restore pending");
offRetry._mover.OnSetFacing = null;
HeroArcherGuardFacing.Tick();
Eq(Mover.FacingMode.Ahead, offRetry._mover.facingMode, "disabled tick retries and completes the restore");
Check(!HeroArcherGuardFacing.OwnsFacing(offRetry), "disabled retry clears responsibility");
HeroArcherRuntime.Enabled = true;

// 5f) Tick 连续两帧没有 Evaluate：归还（stale），失败则继续保留重试
Reset();
Archer stale = MakeArcher(46f, Side.Right, 46f);
Frame(stale);
HeroArcherGuardFacing.Tick();
HeroArcherGuardFacing.Tick();
Eq(Mover.FacingMode.Ahead, stale._mover.facingMode, "stale receipt is released after two missed ticks");
Check(!HeroArcherGuardFacing.OwnsFacing(stale), "stale receipt is dropped");

Reset();
Archer staleFail = MakeArcher(46f, Side.Right, 46f);
Frame(staleFail);
staleFail._mover.OnSetFacing = (mode, target) =>
{
    if (mode == Mover.FacingMode.Ahead) throw new Exception("restore failed");
    staleFail._mover.facingMode = mode;
};
HeroArcherGuardFacing.Tick();
HeroArcherGuardFacing.Tick();
Check(HeroArcherGuardFacing.OwnsFacing(staleFail), "stale release failure keeps responsibility for retry");
staleFail._mover.OnSetFacing = null;
HeroArcherGuardFacing.Tick();
Eq(Mover.FacingMode.Ahead, staleFail._mover.facingMode, "stale retry completes once interop recovers");

// 5g) 身份变化（池复用）：不写新所有者；Runtime 桥 Restore 立即归还
Reset();
Archer reused = MakeArcher(46f, Side.Right, 46f);
Frame(reused);
HeroArcherRuntime.Lives[reused.gameObject.GetInstanceID()] = 2;   // 池复用 = 新 life
int writesBeforeReuse = reused._mover.SetFacingCalls;
HeroArcherGuardFacing.Tick();
HeroArcherGuardFacing.Tick();
Check(!HeroArcherGuardFacing.OwnsFacing(reused), "life change invalidates responsibility");
Eq(writesBeforeReuse, reused._mover.SetFacingCalls, "no write is issued to a reused life");

Reset();
Archer bridged = MakeArcher(46f, Side.Right, 46f);
Frame(bridged);
Eq(Mover.FacingMode.Right, bridged._mover.facingMode, "bridge test takeover");
HeroArcherGuardFacing.Restore(bridged);                     // Runtime 撤销路径直调
Eq(Mover.FacingMode.Ahead, bridged._mover.facingMode, "runtime bridge restores immediately (no 2-frame stale)");
Check(!HeroArcherGuardFacing.OwnsFacing(bridged), "runtime bridge clears responsibility");
HeroArcherGuardFacing.Restore(bridged);                     // 幂等
Eq(Mover.FacingMode.Ahead, bridged._mover.facingMode, "runtime bridge is idempotent");

Reset();
Archer bridgeFail = MakeArcher(46f, Side.Right, 46f);
Frame(bridgeFail);
bridgeFail._mover.OnSetFacing = (mode, target) =>
{
    if (mode == Mover.FacingMode.Ahead) throw new Exception("restore failed");
    bridgeFail._mover.facingMode = mode;
};
HeroArcherGuardFacing.Restore(bridgeFail);
Check(HeroArcherGuardFacing.OwnsFacing(bridgeFail), "bridge failure keeps responsibility for retry");
bridgeFail._mover.OnSetFacing = null;
HeroArcherGuardFacing.Tick();
Eq(Mover.FacingMode.Ahead, bridgeFail._mover.facingMode, "bridge-pending retry completes");

// 5h) 世界缺失：fail-closed（不写、且已持有则归还）
Reset();
Archer orphan = MakeArcher(46f, Side.Right, 46f);
Frame(orphan);
Managers.Inst = null;
Frame(orphan);
Eq(Mover.FacingMode.Ahead, orphan._mover.facingMode, "missing world restores the native value");
Check(!HeroArcherGuardFacing.OwnsFacing(orphan), "missing world fails closed");
Managers.Inst = null;

// ============================================================
// 6) 有界邻居诊断：绑定/节拍/批数/身份/世界/预算/无邻居
// ============================================================
Archer MakeNeighbor(float x)
{
    Archer neighbor = MakeArcher(x, Side.Right, x);
    neighbor.gameObject.SetParent(world.gameLayer.gameObject);
    SpriteRenderer body = neighbor.gameObject.Add(new SpriteRenderer());
    body.sprite = new Sprite { name = "archer_body" };
    body.bounds = new Bounds(new Vector3(x, 0.9f, 0f), new Vector3(0.8f, 1.1f, 1f));
    return neighbor;
}

Reset();
Archer hero = MakeArcher(46f, Side.Right, 46f);
hero.gameObject.SetParent(world.gameLayer.gameObject);
SpriteRenderer heroBody = hero.gameObject.Add(new SpriteRenderer());
heroBody.sprite = new Sprite { name = "hero_body" };
var population = new Il2CppSystem.Collections.Generic.HashSet<Archer>();
kingdom.Archers = population;
population.Add(hero);
population.Add(MakeNeighbor(45f));
population.Add(MakeNeighbor(44f));
population.Add(MakeNeighbor(60f));
Archer deadNeighbor = MakeNeighbor(45.5f);        // 死者不得占用邻居名额
deadNeighbor._damageable.isDead = true;
population.Add(deadNeighbor);

HeroArcherLiveDiagnostics.OnHeroSetup(hero);
Eq(1, HeroArcherLiveDiagnostics.Sessions, "setup consumes one session at arm time");
Check(!HeroArcherLiveDiagnostics.Active, "diagnostics bind is deferred to the tick bridge");
HeroArcherLiveDiagnostics.Tick();
Check(HeroArcherLiveDiagnostics.Active, "diagnostics bind on the existing tick bridge");
Eq(2, HeroArcherLiveDiagnostics.BoundNeighbors, "at most two neighbors bound");
Check(LogContains("neighbors=2"), "bind line reports the bound neighbors");
Check(LogContains("session=1/3"), "bind line reports the consumed session budget");
Eq(0, GameObject.FindObjectsCalls, "diagnostics never use FindObjects");

HeroArcherLiveDiagnostics.Tick();
Eq(1, HeroArcherLiveDiagnostics.Batches, "first sample after bind");
Time.time += 0.05f;
HeroArcherLiveDiagnostics.Tick();
Eq(1, HeroArcherLiveDiagnostics.Batches, "0.1s cadence not yet reached");
Time.time += 0.05f;
HeroArcherLiveDiagnostics.Tick();
Eq(2, HeroArcherLiveDiagnostics.Batches, "0.1s cadence reached");
Check(LogContains("bb=["), "batch line carries native body bounds");
Check(LogContains("camY="), "batch line carries camera Y");

population.Clear();
HeroArcherLiveDiagnostics.Tick();
Check(HeroArcherLiveDiagnostics.Active, "bound session survives population changes");
Eq(1, LogCount("HeroGuardDiag] bind"), "exactly one bind scan per session");

for (int i = 0; i < 30; i++) { Time.time += 0.1f; HeroArcherLiveDiagnostics.Tick(); }
Check(!HeroArcherLiveDiagnostics.Active, "diagnostics stop after the batch budget");
Check(LogContains("stop reason=complete"), "completion is reported");
Eq(12, LogCount("HeroGuardDiag] b="), "exactly twelve batches were emitted");
Eq(1, HeroArcherLiveDiagnostics.Sessions, "session budget is not reset by stopping");

// 无邻居：扫描完成即停（不再有界等待）
Reset();
hero = MakeArcher(46f, Side.Right, 46f);
hero.gameObject.SetParent(world.gameLayer.gameObject);
kingdom.Archers = new Il2CppSystem.Collections.Generic.HashSet<Archer>();
HeroArcherLiveDiagnostics.OnHeroSetup(hero);
HeroArcherLiveDiagnostics.Tick();
Check(!HeroArcherLiveDiagnostics.Active, "completed scan with no neighbor stops immediately");
Check(LogContains("stop reason=no-neighbor"), "no-neighbor stop is reported");
Eq(1, HeroArcherLiveDiagnostics.Sessions, "no-neighbor still consumed its session");

// 重复 setup 预算：同一 hero 只 arm 一次；新 life 直到 3 次；第 4 次被拒
Reset();
Archer budgetHero = MakeArcher(46f, Side.Right, 46f);
budgetHero.gameObject.SetParent(world.gameLayer.gameObject);
kingdom.Archers = new Il2CppSystem.Collections.Generic.HashSet<Archer>();
HeroArcherLiveDiagnostics.OnHeroSetup(budgetHero);
Eq(1, HeroArcherLiveDiagnostics.Sessions, "first arm consumes one");
HeroArcherLiveDiagnostics.OnHeroSetup(budgetHero);
Eq(1, HeroArcherLiveDiagnostics.Sessions, "same hero life never re-arms");
HeroArcherLiveDiagnostics.Tick();                     // no-neighbor → 结束会话，保留预算
Eq(1, HeroArcherLiveDiagnostics.Sessions, "stop keeps the consumed budget");
HeroArcherLiveDiagnostics.OnHeroSetup(budgetHero);
Eq(1, HeroArcherLiveDiagnostics.Sessions, "same hero life still deduped after a stop");
HeroArcherRuntime.Lives[budgetHero.gameObject.GetInstanceID()] = 2;   // 新生命
HeroArcherLiveDiagnostics.OnHeroSetup(budgetHero);
Eq(2, HeroArcherLiveDiagnostics.Sessions, "new hero life may arm again");
HeroArcherLiveDiagnostics.Tick();
HeroArcherRuntime.Lives[budgetHero.gameObject.GetInstanceID()] = 3;
HeroArcherLiveDiagnostics.OnHeroSetup(budgetHero);
Eq(3, HeroArcherLiveDiagnostics.Sessions, "third session allowed");
HeroArcherLiveDiagnostics.Tick();
HeroArcherRuntime.Lives[budgetHero.gameObject.GetInstanceID()] = 4;
HeroArcherLiveDiagnostics.OnHeroSetup(budgetHero);
Eq(3, HeroArcherLiveDiagnostics.Sessions, "fourth session refused (budget ceiling)");
HeroArcherLiveDiagnostics.Tick();
Check(!HeroArcherLiveDiagnostics.Active, "refused arm never binds");
HeroArcherLiveDiagnostics.Clear();
Eq(0, HeroArcherLiveDiagnostics.Sessions, "explicit Clear resets the session budget");

// 会话中止：邻居 life 变化 / 英雄 life 变化 / 世界切换
Reset();
hero = MakeArcher(46f, Side.Right, 46f);
hero.gameObject.SetParent(world.gameLayer.gameObject);
population = new Il2CppSystem.Collections.Generic.HashSet<Archer>();
kingdom.Archers = population;
Archer neighbor = MakeNeighbor(45f);
population.Add(hero);
population.Add(neighbor);
HeroArcherLiveDiagnostics.OnHeroSetup(hero);
HeroArcherLiveDiagnostics.Tick();
HeroArcherLiveDiagnostics.Tick();
Eq(1, HeroArcherLiveDiagnostics.Batches, "one batch before the identity break");
HeroArcherRuntime.Lives[neighbor.gameObject.GetInstanceID()] = 2;
Time.time += 0.1f;
HeroArcherLiveDiagnostics.Tick();
Check(!HeroArcherLiveDiagnostics.Active, "neighbor identity break aborts the session");
Check(LogContains("stop reason=neighbor-identity-changed"), "abort reason is reported");

Reset();
hero = MakeArcher(46f, Side.Right, 46f);
hero.gameObject.SetParent(world.gameLayer.gameObject);
population = new Il2CppSystem.Collections.Generic.HashSet<Archer>();
kingdom.Archers = population;
population.Add(hero);
population.Add(MakeNeighbor(45f));
HeroArcherLiveDiagnostics.OnHeroSetup(hero);
HeroArcherLiveDiagnostics.Tick();
HeroArcherLiveDiagnostics.Tick();
HeroArcherRuntime.Lives[hero.gameObject.GetInstanceID()] = 2;
Time.time += 0.1f;
HeroArcherLiveDiagnostics.Tick();
Check(!HeroArcherLiveDiagnostics.Active, "hero identity break aborts the session");
Check(LogContains("stop reason=hero-identity-changed"), "hero abort reason is reported");

Reset();
hero = MakeArcher(46f, Side.Right, 46f);
hero.gameObject.SetParent(world.gameLayer.gameObject);
population = new Il2CppSystem.Collections.Generic.HashSet<Archer>();
kingdom.Archers = population;
population.Add(hero);
population.Add(MakeNeighbor(45f));
HeroArcherLiveDiagnostics.OnHeroSetup(hero);
HeroArcherLiveDiagnostics.Tick();
HeroArcherLiveDiagnostics.Tick();
Eq(1, HeroArcherLiveDiagnostics.Batches, "one batch before the world switch");
Managers.Inst = new Managers();                        // 换世界（新的 world/gameLayer 指针）
Managers.Inst.kingdom.Archers = new Il2CppSystem.Collections.Generic.HashSet<Archer>();
Time.time += 0.1f;
HeroArcherLiveDiagnostics.Tick();
Check(!HeroArcherLiveDiagnostics.Active, "world switch aborts the session");
Check(LogContains("stop reason=world-changed"), "world switch reason is reported");

// 诊断与守墙朝向共用同一 Tick 桥；诊断零写入
Reset();
hero = MakeArcher(46f, Side.Right, 46f);
hero.gameObject.SetParent(world.gameLayer.gameObject);
population = new Il2CppSystem.Collections.Generic.HashSet<Archer>();
kingdom.Archers = population;
Archer watched = MakeNeighbor(45f);
population.Add(hero);
population.Add(watched);
HeroArcherLiveDiagnostics.OnHeroSetup(hero);
Frame(hero);
Sprite spriteBefore = watched.GetComponent<SpriteRenderer>().sprite;
Vector3 positionBefore = watched.transform.position;
HeroArcherLiveDiagnostics.Tick();
HeroArcherLiveDiagnostics.Tick();
Eq(1, HeroArcherLiveDiagnostics.Batches, "guard tick drives the diagnostic bridge");
Check(ReferenceEquals(spriteBefore, watched.GetComponent<SpriteRenderer>().sprite), "diagnostics never touch neighbor sprites");
Check(positionBefore.x == watched.transform.position.x, "diagnostics never move neighbors");
Eq(Mover.FacingMode.Right, hero._mover.facingMode, "guard facing still applied with diagnostics active");

Eq(0, GameObject.FindObjectsCalls, "no FindObjects anywhere in the suite");
Reset();
Archer shotGuard = MakeArcher(46f, Side.Right, 46f);
Frame(shotGuard);
Goto(shotGuard).latestGoto = 1;
Frame(shotGuard);
((Coatsink.Common.Haglet)shotGuard.shoot).started = true;
Frame(shotGuard);
Eq(Mover.FacingMode.Ahead, shotGuard._mover.facingMode, "shooting returns native facing");
((Coatsink.Common.Haglet)shotGuard.shoot).started = false;
Frame(shotGuard);
Eq(Mover.FacingMode.Right, shotGuard._mover.facingMode, "guard goal survives shot and resumes outward in idle1");
int shotId = shotGuard.gameObject.GetInstanceID();
HeroArcherRuntime.Lives[shotId] = 0;
HeroArcherGuardFacing.Restore(shotGuard);
Check(HeroArcherGuardFacing.HasOutstanding(shotId), "unknown life retains recovery responsibility");
Eq(Mover.FacingMode.Right, shotGuard._mover.facingMode, "unknown life never writes facing");
HeroArcherRuntime.Lives[shotId] = 1;
HeroArcherGuardFacing.Tick();
Eq(Mover.FacingMode.Ahead, shotGuard._mover.facingMode, "known life retry restores facing");
Check(!HeroArcherGuardFacing.HasOutstanding(shotId), "known life retry finishes");
Reset();
Archer noOpRestore = MakeArcher(46f, Side.Right, 46f);
Frame(noOpRestore);
noOpRestore._mover.OnSetFacing = (m,t) => { };
HeroArcherGuardFacing.Restore(noOpRestore);
Check(HeroArcherGuardFacing.HasOutstanding(noOpRestore.gameObject.GetInstanceID()), "no-op setter retains responsibility");
noOpRestore._mover.OnSetFacing = null;
HeroArcherGuardFacing.Tick();
Eq(Mover.FacingMode.Ahead, noOpRestore._mover.facingMode, "no-op recovery retries successfully");
Console.WriteLine("PASS " + passed + " assertions (real guard-facing and diagnostics code paths)");
