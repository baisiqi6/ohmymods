// Banker fixed-foundations regression (Issue 100). Drives the production pipeline:
// Managers.OnLevelLoaded postfix → notified-context binding → structure snapshot capture →
// frozen cache lookup, plus the movement / profile / claim / wallet hard gates. All game
// surface types are the shared deterministic fixtures from the greek-bank-scope suite.
using System;
using KingdomEnhancedMod;
using UnityEngine;

static class Program
{
    private static int Main()
    {
        // ------------------------------------------------------------------ selector
        Harness.Test("unbuilt ±9 foundations publish only after a successful load", () =>
        {
            Fixture f = Fixture.BuildGreek(notify: false);
            ClearCandidates(f.Layer);
            UnbuiltFoundation(f.Layer, -9f);
            UnbuiltFoundation(f.Layer, 9f);

            Harness.False(Domain(out _, out _), "no notification => no domain");
            Harness.False(MainBankerFixedDomain.TryCapture(Managers.Inst),
                "capture without a notification is refused");
            Notify(false);
            Harness.False(Domain(out _, out _), "notification alone publishes nothing");
            Harness.True(MainBankerFixedDomain.TryCapture(Managers.Inst), "capture after successful load");
            Harness.True(Domain(out float left, out float right), "domain published");
            Harness.Eq(-9f, left, "left fixed foundation");
            Harness.Eq(9f, right, "right fixed foundation");
        });

        Harness.Test("fromSave=true and false are both successful load notifications", () =>
        {
            Fixture f = Fixture.BuildGreek(notify: false);
            Notify(true);
            Harness.True(MainBankerFixedDomain.TryCapture(Managers.Inst), "save load publishes");

            Fixture g = Fixture.BuildGreek(sceneHandle: 2, notify: false);
            Notify(false); // 新岛成功加载同样以 false 通知，必须发布
            Harness.True(MainBankerFixedDomain.TryCapture(Managers.Inst), "new island publishes");
            Harness.True(Domain(out float l, out float r) && l == -5f && r == 5f, "new island ±5");
        });

        Harness.Test("asymmetric nearest candidates win, same-x upgrade roots merge", () =>
        {
            Fixture f = Fixture.BuildGreek(notify: false);
            ClearCandidates(f.Layer);
            BuiltWall(f.Layer, -13f);
            BuiltWall(f.Layer, -6.5f);
            BuiltWall(f.Layer, 9.25f);
            UnbuiltFoundation(f.Layer, 9.25f); // 升级旧/新根同坐标
            UnbuiltFoundation(f.Layer, 17f);
            Notify(false);
            Harness.True(MainBankerFixedDomain.TryCapture(Managers.Inst), "captured");
            Harness.True(Domain(out float left, out float right), "domain");
            Harness.Eq(-6.5f, left, "nearest left");
            Harness.Eq(9.25f, right, "nearest right; duplicate coordinate merged");
        });

        Harness.Test("overgrown ±8.75 inner foundations beat outer ±17", () =>
        {
            Fixture f = Fixture.BuildGreek(notify: false);
            ClearCandidates(f.Layer);
            UnbuiltFoundation(f.Layer, -17f);
            UnbuiltFoundation(f.Layer, -8.75f);
            BuiltWall(f.Layer, 8.75f);
            BuiltWall(f.Layer, 17f);
            Notify(false);
            Harness.True(MainBankerFixedDomain.TryCapture(Managers.Inst), "captured");
            Harness.True(Domain(out float left, out float right), "domain");
            Harness.Eq(-8.75f, left, "inner left");
            Harness.Eq(8.75f, right, "inner right");
        });

        Harness.Test("cache stays frozen across upgrade, wreck and outer expansion", () =>
        {
            Fixture f = Fixture.BuildGreek(); // ±5 已发布
            Harness.True(Domain(out float l0, out float r0) && l0 == -5f && r0 == 5f, "initial ±5");

            ClearCandidates(f.Layer);
            BuiltWall(f.Layer, -13f);        // 新增外墙
            BuiltWall(f.Layer, 13f);
            UnbuiltFoundation(f.Layer, -5f); // 同坐标升级新根
            UnbuiltFoundation(f.Layer, 5f);
            Banker banker = f.AddBanker();
            Time.frameCount += 300;
            Harness.BankerFixedDomainUpdate(banker);
            Harness.True(Domain(out float l1, out float r1), "still resolved");
            Harness.Eq(-5f, l1, "frozen left");
            Harness.Eq(5f, r1, "frozen right");
            Harness.False(MainBankerFixedDomain.TryCapture(Managers.Inst), "no recapture while not pending");
        });

        Harness.Test("reload into a new context invalidates until that load is captured", () =>
        {
            Fixture f1 = Fixture.BuildGreek(sceneHandle: 1); // ±5 发布
            Harness.True(Domain(out _, out _), "first context published");

            Fixture f2 = Fixture.BuildGreek(sceneHandle: 2, notify: false);
            ClearCandidates(f2.Layer);
            BuiltWall(f2.Layer, -7f);
            BuiltWall(f2.Layer, 7f);
            Harness.False(Domain(out _, out _), "old context domain revoked on mismatch");
            Notify(false);
            Harness.True(MainBankerFixedDomain.TryCapture(Managers.Inst), "new context captured");
            Harness.True(Domain(out float l, out float r) && l == -7f && r == 7f, "new domain ±7");
        });

        Harness.Test("stale notification never authorizes a later context", () =>
        {
            Fixture f1 = Fixture.BuildGreek(notify: false);
            Notify(false); // 通知绑定 f1 context，但未捕获

            Fixture f2 = Fixture.BuildGreek(sceneHandle: 2, notify: false);
            ClearCandidates(f2.Layer);
            BuiltWall(f2.Layer, -17f);
            BuiltWall(f2.Layer, 17f);

            Harness.False(MainBankerFixedDomain.Maintain(Managers.Inst), "maintain refuses foreign context");
            Time.frameCount += 31;
            Harness.False(MainBankerFixedDomain.Maintain(Managers.Inst), "retry still refuses");
            Harness.False(MainBankerFixedDomain.TryCapture(Managers.Inst), "explicit capture refuses");
            Harness.False(Domain(out _, out _), "no domain from a stale notification");

            Notify(false); // 新上下文自己的成功通知
            Harness.True(MainBankerFixedDomain.TryCapture(Managers.Inst), "fresh notification captures");
            Harness.True(Domain(out float l, out float r) && l == -17f && r == 17f, "±17 from own load");
        });

        Harness.Test("notification with unreadable context never arms a later capture", () =>
        {
            Fixture f = Fixture.BuildGreek(notify: false);
            f.World.gameLayer = null;
            Notify(false); // 事件时 context 读不到：本次不武装捕获
            f.World.gameLayer = f.Layer;

            Harness.False(MainBankerFixedDomain.TryCapture(Managers.Inst), "no deferred capture");
            Time.frameCount += 31;
            Harness.False(MainBankerFixedDomain.Maintain(Managers.Inst), "maintain cannot arm itself");
            Harness.False(Domain(out _, out _), "domain stays unknown");

            Notify(false);
            Harness.True(MainBankerFixedDomain.TryCapture(Managers.Inst), "fresh notification arms capture");
            Harness.True(Domain(out _, out _), "published after fresh notification");
        });

        Harness.Test("missing single side never publishes; later completion recovers", () =>
        {
            Fixture f = Fixture.BuildGreek(notify: false);
            ClearCandidates(f.Layer);
            BuiltWall(f.Layer, -9f);
            Notify(false);
            Harness.False(MainBankerFixedDomain.TryCapture(Managers.Inst), "single side refused");
            Harness.False(Domain(out _, out _), "no partial domain");

            BuiltWall(f.Layer, 9f);
            Harness.True(MainBankerFixedDomain.Maintain(Managers.Inst), "bounded retry succeeds");
            Harness.True(Domain(out float l, out float r) && l == -9f && r == 9f, "±9 after completion");
        });

        Harness.Test("NaN candidate or campfire refuses the whole snapshot (no expansion)", () =>
        {
            Fixture f = Fixture.BuildGreek(notify: false);
            ClearCandidates(f.Layer);
            Wall broken = BuiltWall(f.Layer, float.NaN);
            BuiltWall(f.Layer, 9f);
            Notify(false);
            Harness.False(MainBankerFixedDomain.TryCapture(Managers.Inst), "NaN candidate refused");
            Harness.False(Domain(out _, out _), "no fallback to a farther wall");

            UnityEngine.Object.Destroy(broken.gameObject);
            BuiltWall(f.Layer, -9f);
            Harness.True(MainBankerFixedDomain.TryCapture(Managers.Inst), "healthy snapshot accepted");
            Harness.True(Domain(out float l, out float r) && l == -9f && r == 9f, "±9 exact");

            Fixture g = Fixture.BuildGreek(sceneHandle: 2, notify: false);
            ClearCandidates(g.Layer);
            BuiltWall(g.Layer, -9f);
            BuiltWall(g.Layer, 9f);
            Notify(false);
            g.Kingdom.campfirePosition = float.NaN;
            Harness.False(MainBankerFixedDomain.TryCapture(Managers.Inst), "NaN campfire refused");
            g.Kingdom.campfirePosition = 0f;
            Harness.True(MainBankerFixedDomain.TryCapture(Managers.Inst), "campfire restored => capture");
        });

        Harness.Test("capture exception publishes nothing and keeps retrying", () =>
        {
            Fixture f = Fixture.BuildGreek(notify: false);
            Notify(false);
            Sim.ThrowOnChildTraversal = true;
            Harness.False(MainBankerFixedDomain.TryCapture(Managers.Inst), "exception refused");
            Harness.False(Domain(out _, out _), "no partial publish");
            Sim.ThrowOnChildTraversal = false;
            Harness.True(MainBankerFixedDomain.TryCapture(Managers.Inst), "retry after fault");
            Harness.True(Domain(out _, out _), "published after fault cleared");
        });

        Harness.Test("same-context reload during the scan discards the stale snapshot", () =>
        {
            Fixture f = Fixture.BuildGreek(notify: false);
            Notify(false);
            // 扫描开始即注入同 context 的新成功通知：静态代数会一起推进，入场 local 才是判据。
            Sim.TraversalCallback = () => Notify(false);
            Harness.False(MainBankerFixedDomain.TryCapture(Managers.Inst), "stale entry refused");
            Harness.False(Domain(out _, out _), "nothing published by the stale entry");
            Harness.True(MainBankerFixedDomain.TryCapture(Managers.Inst), "fresh notice captured next");
            Harness.True(Domain(out float l, out float r) && l == -5f && r == 5f, "±5 published once");
        });

        Harness.Test("foreign-context capture cancels the pending notification for good", () =>
        {
            Fixture f1 = Fixture.BuildGreek(notify: false);
            Managers first = Managers.Inst;
            Notify(false); // pending 绑定 context A，尚未捕获

            Fixture f2 = Fixture.BuildGreek(sceneHandle: 2, notify: false); // context B，无新通知
            Harness.False(MainBankerFixedDomain.TryCapture(Managers.Inst), "B cannot capture under A");
            Harness.False(MainBankerFixedDomain.TryCapture(Managers.Inst), "pending cancelled, not deferred");

            Managers.Inst = first; // 回到旧 wrapper：无 fresh 通知不得复活
            Harness.False(MainBankerFixedDomain.TryCapture(Managers.Inst), "old context cannot revive");
            Harness.False(Domain(out _, out _), "no domain without a fresh notification");

            Notify(false); // fresh 成功通知
            Harness.True(MainBankerFixedDomain.TryCapture(Managers.Inst), "fresh notification captures");
            Harness.True(Domain(out float l, out float r) && l == -5f && r == 5f, "±5 after fresh notify");
        });

        Harness.Test("duplicate notification revokes and recaptures identically", () =>
        {
            Fixture f = Fixture.BuildGreek(); // ±5 已发布
            Harness.True(Domain(out _, out _), "published");
            Notify(false); // 同上下文再次成功通知：旧 proof 先失效
            Harness.False(Domain(out _, out _), "generation change revoked the proof");
            Harness.True(MainBankerFixedDomain.TryCapture(Managers.Inst), "recaptured");
            Harness.True(Domain(out float l, out float r) && l == -5f && r == 5f, "same ±5");
        });

        Harness.Test("revoked domain never revives when the old wrapper comes back", () =>
        {
            Fixture f1 = Fixture.BuildGreek(sceneHandle: 1); // ±5 发布
            Managers first = Managers.Inst;
            Harness.True(Domain(out _, out _), "context A published");

            Fixture f2 = Fixture.BuildGreek(sceneHandle: 2, notify: false);
            Harness.False(Domain(out _, out _), "context B sees no domain (revoked)");

            Managers.Inst = first; // 回到旧 wrapper/scene
            Harness.False(Domain(out _, out _), "old proof stays revoked");
        });

        Harness.Test("boundary ownership: strict inside, edges and non-finite to assistants", () =>
        {
            Fixture f = Fixture.BuildGreek(); // ±5
            Harness.True(MainBankerFixedDomain.TryGetDomain(out float l, out float r), "domain");
            Harness.True(MainBankerFixedDomain.IsInside(4.9f, l, r), "inside");
            Harness.False(MainBankerFixedDomain.IsInside(5f, l, r), "right edge assistant-owned");
            Harness.False(MainBankerFixedDomain.IsInside(-5f, l, r), "left edge assistant-owned");
            Harness.False(MainBankerFixedDomain.IsInside(float.NaN, l, r), "NaN rejected");
            Harness.False(MainBankerFixedDomain.IsInside(float.PositiveInfinity, l, r), "Infinity rejected");
            Harness.False(PatchEconomy_Banker.IsInMainBankerDomain(5f, l, r), "shared boundary semantics");
        });

        // ------------------------------------------------------- profile / scanner
        Harness.Test("profile refresh is per-banker and per-generation", () =>
        {
            Fixture f = Fixture.BuildGreek(); // ±5
            Banker current = f.AddBanker();
            Banker other = f.AddBanker("BankerOther", kingdomBound: false);

            // 新一代替换结构为 ±9；非当前本体先跑 prefix：只捕获，不刷新本体 profile
            ClearCandidates(f.Layer);
            BuiltWall(f.Layer, -9f);
            BuiltWall(f.Layer, 9f);
            Notify(false);
            Harness.BankerFixedDomainUpdate(other);
            Harness.True(Domain(out float l, out float r) && l == -9f && r == 9f,
                "captured by non-current prefix");
            Harness.Eq(Fixture.NativeWander, current.wanderRange, "current banker not refreshed by others");

            Harness.BankerFixedDomainUpdate(current);
            Harness.Eq(8.75f, current.wanderRange, "current banker refreshed from ±9 domain");
            Harness.Eq(9f, current.coinScanRange, "scanner follows the new domain");
        });

        Harness.Test("domain wander/scanner apply and native restore", () =>
        {
            Fixture f = Fixture.BuildGreek(); // ±5
            Banker banker = f.AddBanker();
            Harness.BankerFixedDomainUpdate(banker);
            Harness.Eq(Fixture.ExpectedDomainWander, banker.wanderRange, "wander clamped by ±5");
            Harness.Eq(5f, banker.coinScanRange, "scan range");
            Harness.Eq(5f, banker._coinScanner.range, "forward");
            Harness.Eq(5f, banker._coinScanner.rangeBehind, "behind");

            ModConfig.Enabled.Value = false;
            Harness.BankerUpdate(banker);
            f.AssertNativeProfile(banker, "restored");
        });

        Harness.Test("partial restore (scanner identity fault) reapplies on immediate re-enable", () =>
        {
            Fixture f = Fixture.BuildGreek();
            Banker banker = f.AddBanker();
            Harness.BankerFixedDomainUpdate(banker);
            Harness.Eq(Fixture.ExpectedDomainWander, banker.wanderRange, "domain profile applied");

            // scanner 身份暂不可读：归还流程先退回 wander，随后在 scanner 处早退，profile 保留。
            IntPtr saved = banker._coinScanner.Pointer;
            banker._coinScanner.Pointer = IntPtr.Zero;
            ModConfig.Enabled.Value = false;
            Harness.BankerUpdate(banker);
            Harness.Eq(Fixture.NativeWander, banker.wanderRange, "wander already returned");
            banker._coinScanner.Pointer = saved;

            ModConfig.Enabled.Value = true; // 同 generation 立即重新 enable
            Harness.BankerFixedDomainUpdate(banker);
            Harness.Eq(Fixture.ExpectedDomainWander, banker.wanderRange, "reapplied at the first prefix");
            Harness.Eq(5f, banker._coinScanner.range, "scanner reapplied");
        });

        Harness.Test("scanner fails closed on non-finite or out-of-domain banker pose", () =>
        {
            Fixture f = Fixture.BuildGreek();
            Banker banker = f.AddBanker();

            banker.transform.position = new Vector3(float.NaN, 0f, 0f);
            Time.frameCount += 200;
            Harness.BankerUpdate(banker); // 120 帧巡检的 scanner 刷新路径
            Harness.Eq(0f, banker.coinScanRange, "NaN position => no scan");
            Harness.Eq(0f, banker._coinScanner.range, "forward zero");

            banker.transform.position = new Vector3(8f, 0f, 0f); // 域外
            Time.frameCount += 200;
            Harness.BankerUpdate(banker);
            Harness.Eq(0f, banker._coinScanner.range, "outside domain => no scan");

            banker.transform.position = new Vector3(-2f, 0f, 0f); // 域内
            Time.frameCount += 200;
            Harness.BankerUpdate(banker);
            Harness.Eq(7f, banker._coinScanner.range, "forward to +5");
            Harness.Eq(3f, banker._coinScanner.rangeBehind, "behind to -5");

            banker.transform.localScale = new Vector3(float.NaN, 1f, 1f);
            Time.frameCount += 200;
            Harness.BankerUpdate(banker);
            Harness.Eq(0f, banker._coinScanner.range, "NaN scale => no scan");
        });

        // ----------------------------------------------------------------- movement
        Harness.Test("movement: outside idle goal canceled, inside goal left alone", () =>
        {
            Fixture f = Fixture.BuildGreek();
            Banker banker = f.AddBanker();

            StateMachine fsm = new StateMachine { Current = 1 };
            Mover mover = new Mover
            {
                movingToGoal = true,
                goalMode = Mover.GoalMode.Position,
                _goalPosition = 7f
            };
            banker._fsm = fsm;
            banker._mover = mover;
            Harness.BankerFixedDomainUpdate(banker);
            Harness.Eq(1, mover.StopCalls, "stops the outside goal");
            Harness.Eq(1, fsm.GoToStateCalls, "queues Idle");
            Harness.Eq(1, fsm._queuedState, "queued state id");

            fsm = new StateMachine { Current = 1 };
            mover = new Mover
            {
                movingToGoal = true,
                goalMode = Mover.GoalMode.Position,
                _goalPosition = 3f
            };
            banker._fsm = fsm;
            banker._mover = mover;
            Harness.BankerFixedDomainUpdate(banker);
            Harness.Eq(0, mover.StopCalls, "inside goal untouched");
            Harness.Eq(0, fsm.GoToStateCalls, "no requeue for inside goal");

            // Actor 域外但 goal 向内：自然归位，不得每帧重排
            banker.transform.position = new Vector3(8f, 0f, 0f);
            Harness.BankerFixedDomainUpdate(banker);
            Harness.Eq(0, mover.StopCalls, "out-of-domain actor with inside goal is left alone");

            // 夜间同样只按 goal 判定
            f.Kingdom.isDaytime = false;
            Harness.BankerFixedDomainUpdate(banker);
            Harness.Eq(0, mover.StopCalls, "night idle with inside goal untouched");
        });

        Harness.Test("movement: grabcoin outside goal cancels and releases only own claim", () =>
        {
            Fixture f = Fixture.BuildGreek();
            Banker banker = f.AddBanker();
            DroppableCurrency coin = f.AddCoin(7f);
            coin.friendlyClaimer = banker.gameObject;
            banker._targetCoin = coin;
            StateMachine fsm = new StateMachine { Current = 0 };
            Mover mover = new Mover
            {
                movingToGoal = true,
                goalMode = Mover.GoalMode.Position,
                _goalPosition = 7f
            };
            banker._fsm = fsm;
            banker._mover = mover;
            Harness.BankerFixedDomainUpdate(banker);
            Harness.True(coin.friendlyClaimer == null, "outside claim released");
            Harness.True(banker._targetCoin == null, "target cleared with explicit evidence");
            Harness.Eq(1, mover.StopCalls, "movement stopped");
            Harness.Eq(1, fsm.GoToStateCalls, "idle queued");

            // goal 仍内、目标币滚出域：只释放认领，目标与移动保留
            Fixture g = Fixture.BuildGreek(sceneHandle: 2);
            Banker banker2 = g.AddBanker();
            DroppableCurrency rolledOut = g.AddCoin(7f);
            rolledOut.friendlyClaimer = banker2.gameObject;
            banker2._targetCoin = rolledOut;
            StateMachine fsm2 = new StateMachine { Current = 0 };
            Mover mover2 = new Mover
            {
                movingToGoal = true,
                goalMode = Mover.GoalMode.Position,
                _goalPosition = 3f
            };
            banker2._fsm = fsm2;
            banker2._mover = mover2;
            Harness.BankerFixedDomainUpdate(banker2);
            Harness.True(rolledOut.friendlyClaimer == null, "rolled-out claim released");
            Harness.True(banker2._targetCoin == rolledOut, "target kept for the inside move");
            Harness.Eq(0, mover2.StopCalls, "inside move untouched");
        });

        Harness.Test("movement: queued switch and financial states are never overridden", () =>
        {
            Fixture f = Fixture.BuildGreek();
            Banker banker = f.AddBanker();

            StateMachine fsm = new StateMachine { Current = 1, _executeQueuedState = true };
            Mover mover = new Mover
            {
                movingToGoal = true,
                goalMode = Mover.GoalMode.Position,
                _goalPosition = 7f
            };
            banker._fsm = fsm;
            banker._mover = mover;
            Harness.BankerFixedDomainUpdate(banker);
            Harness.Eq(0, mover.StopCalls, "any queued state wins this frame");
            Harness.Eq(0, fsm.GoToStateCalls, "no override of the queue");

            fsm = new StateMachine { Current = 4 }; // DropOff
            mover = new Mover
            {
                movingToGoal = true,
                goalMode = Mover.GoalMode.Position,
                _goalPosition = 7f
            };
            banker._fsm = fsm;
            banker._mover = mover;
            Harness.BankerFixedDomainUpdate(banker);
            Harness.Eq(0, mover.StopCalls, "DropOff untouched");

            fsm = new StateMachine { Current = 5 }; // Payout
            mover = new Mover
            {
                movingToGoal = true,
                goalMode = Mover.GoalMode.Position,
                _goalPosition = 7f
            };
            banker._fsm = fsm;
            banker._mover = mover;
            Harness.BankerFixedDomainUpdate(banker);
            Harness.Eq(0, mover.StopCalls, "Payout untouched");

            fsm = new StateMachine { Current = 1 };
            mover = new Mover
            {
                movingToGoal = false,
                goalMode = Mover.GoalMode.Position,
                _goalPosition = 7f
            };
            banker._fsm = fsm;
            banker._mover = mover;
            Harness.BankerFixedDomainUpdate(banker);
            Harness.Eq(0, mover.StopCalls, "waiting idle untouched");

            mover = new Mover
            {
                movingToGoal = true,
                goalMode = Mover.GoalMode.Object,
                _goalPosition = 7f
            };
            banker._fsm = fsm;
            banker._mover = mover;
            Harness.BankerFixedDomainUpdate(banker);
            Harness.Eq(0, mover.StopCalls, "object goal untouched");

            mover = new Mover
            {
                movingToGoal = true,
                goalMode = Mover.GoalMode.Position,
                _goalPosition = float.NaN
            };
            banker._fsm = fsm;
            banker._mover = mover;
            Harness.BankerFixedDomainUpdate(banker);
            Harness.Eq(0, mover.StopCalls, "non-finite goal untouched");
        });

        // --------------------------------------------------------- claim release
        Harness.Test("release sweep returns only own outside player claims in this layer", () =>
        {
            Fixture f = Fixture.BuildGreek(); // ±5
            Banker banker = f.AddBanker();
            DroppableRegistrar registrar = Sim.NewActor("Registrar", f.Layer)
                .AddComponent<DroppableRegistrar>();
            Managers.Inst.dropManager = registrar;

            DroppableCurrency outsideMine = AddRegisteredCoin(f, registrar, 8f, banker.gameObject);
            DroppableCurrency insideMine = AddRegisteredCoin(f, registrar, 3f, banker.gameObject);
            GameObject stranger = Sim.NewActor("Stranger", f.Layer);
            DroppableCurrency outsideOther = AddRegisteredCoin(f, registrar, 9f, stranger);
            DroppableCurrency farmMine = AddRegisteredCoin(f, registrar, 8f, banker.gameObject);
            farmMine.droppedBy = DropType.Wildlife;
            DroppableCurrency target = AddRegisteredCoin(f, registrar, 8f, banker.gameObject);
            banker._targetCoin = target;
            DroppableCurrency oldLayer = AddOtherLayerCoin(f, banker.gameObject, 8f);

            Time.frameCount += 200;
            Harness.BankerUpdate(banker); // 低频巡检路径：profile 刷新 + 认领回收

            Harness.True(registrar.QueryCalls >= 1, "registrar snapshot queried");
            Harness.True(outsideMine.friendlyClaimer == null, "outside own claim released");
            Harness.True(insideMine.friendlyClaimer == banker.gameObject, "inside claim kept");
            Harness.True(outsideOther.friendlyClaimer == stranger, "other claimer untouched");
            Harness.True(farmMine.friendlyClaimer == banker.gameObject, "farm coin untouched");
            Harness.True(target.friendlyClaimer == null, "target claim released");
            Harness.True(banker._targetCoin == null, "outside target cleared");
            Harness.True(oldLayer.friendlyClaimer == banker.gameObject, "other-layer coin untouched");

            // 域未知：不得释放任何认领
            f.BreakDomain();
            DroppableCurrency unknown = AddRegisteredCoin(f, registrar, 8f, banker.gameObject);
            Time.frameCount += 200;
            Harness.BankerUpdate(banker);
            Harness.True(unknown.friendlyClaimer == banker.gameObject, "unknown domain releases nothing");
        });

        Harness.Test("target cleanup never mutates a coin in another layer", () =>
        {
            Fixture f = Fixture.BuildGreek(); // ±5
            Banker banker = f.AddBanker();
            GameObject otherLayer = Sim.NewLayer("Foreign");
            DroppableCurrency foreign = f.AddCoin(8f);
            foreign.transform.Parent = otherLayer.transform;
            foreign.friendlyClaimer = banker.gameObject;
            banker._targetCoin = foreign;
            banker._fsm = new StateMachine { Current = 0 };
            banker._mover = new Mover
            {
                movingToGoal = true,
                goalMode = Mover.GoalMode.Position,
                _goalPosition = 8f
            };
            Harness.BankerFixedDomainUpdate(banker);
            Harness.True(foreign.friendlyClaimer == banker.gameObject,
                "movement target cleanup leaves the foreign coin alone");

            // 低频补扫路径（registrar）同样不得触碰异层目标
            DroppableRegistrar registrar = Sim.NewActor("Registrar", f.Layer)
                .AddComponent<DroppableRegistrar>();
            Managers.Inst.dropManager = registrar;
            registrar.Droppables.Add(foreign);
            banker._targetCoin = foreign;
            Time.frameCount += 200;
            Harness.BankerUpdate(banker);
            Harness.True(foreign.friendlyClaimer == banker.gameObject,
                "sweep target cleanup leaves the foreign coin alone");
        });

        Harness.Test("claim entry fails closed without domain, native with domain", () =>
        {
            Fixture f = Fixture.BuildGreek();
            Banker banker = f.AddBanker();
            banker._targetCoin = f.AddCoin(3f);
            Harness.True(PatchEconomy_Banker.ClaimCoins_Prefix(banker), "inside domain => native");

            f.BreakDomain();
            banker._targetCoin = f.AddCoin(3f);
            Harness.False(PatchEconomy_Banker.ClaimCoins_Prefix(banker), "unknown domain fail-closed");
            Harness.True(banker._targetCoin == null, "target cleared on fail-closed");
        });

        // ------------------------------------------------------------------ gates
        Harness.Test("claim gate: current-layer finite player coins, strict domain", () =>
        {
            Fixture f = Fixture.BuildGreek(); // ±5
            Banker banker = f.AddBanker();

            DroppableCurrency outside = f.AddCoin(8f);
            bool result = true;
            Harness.False(Droppable_MainBankerOutsideWallClaim_Patch.Prefix(
                outside, banker.gameObject, ref result), "outside denied");
            Harness.False(result, "claim result false");

            DroppableCurrency inside = f.AddCoin(3f);
            result = true;
            Harness.True(Droppable_MainBankerOutsideWallClaim_Patch.Prefix(
                inside, banker.gameObject, ref result), "inside native");

            DroppableCurrency broken = f.AddCoin(8f);
            broken.transform.position = new Vector3(float.NaN, 0f, 0f);
            result = true;
            Harness.False(Droppable_MainBankerOutsideWallClaim_Patch.Prefix(
                broken, banker.gameObject, ref result), "NaN coin denied");

            DroppableCurrency farm = f.AddCoin(8f);
            farm.droppedBy = DropType.Wildlife;
            result = true;
            Harness.True(Droppable_MainBankerOutsideWallClaim_Patch.Prefix(
                farm, banker.gameObject, ref result), "non-player coin native");

            DroppableCurrency jade = f.AddCoin(8f);
            jade.CurrencyType = CurrencyType.Jade;
            result = true;
            Harness.True(Droppable_MainBankerOutsideWallClaim_Patch.Prefix(
                jade, banker.gameObject, ref result), "non-coin currency native");

            Banker other = f.AddBanker("BankerOther", kingdomBound: false);
            DroppableCurrency otherCoin = f.AddCoin(8f);
            result = true;
            Harness.True(Droppable_MainBankerOutsideWallClaim_Patch.Prefix(
                otherCoin, other.gameObject, ref result), "non-current banker native");

            f.BreakDomain();
            DroppableCurrency unknown = f.AddCoin(8f);
            result = true;
            Harness.False(Droppable_MainBankerOutsideWallClaim_Patch.Prefix(
                unknown, banker.gameObject, ref result), "unknown domain denied");

            Fixture foreign = Fixture.BuildForeign(sceneHandle: 3);
            Banker foreignBanker = foreign.AddBanker();
            DroppableCurrency foreignCoin = foreign.AddCoin(8f);
            result = true;
            Harness.True(Droppable_MainBankerOutsideWallClaim_Patch.Prefix(
                foreignCoin, foreignBanker.gameObject, ref result), "foreign native");
        });

        Harness.Test("wallet gate: exact authority wallet, strict domain, others native", () =>
        {
            Fixture f = Fixture.BuildGreek(); // ±5
            Banker banker = f.AddBanker();
            Wallet wallet = banker._wallet;

            DroppableCurrency outside = f.AddCoin(8f);
            bool result = true;
            Harness.False(Wallet_MainBankerFixedDomainCurrency_Patch.Prefix(
                wallet, outside, false, ref result), "outside coin blocked");
            Harness.False(result, "result false");
            Harness.Eq(0, wallet.SuckCurrencyCalls, "native pickup not reached");

            DroppableCurrency inside = f.AddCoin(3f);
            result = true;
            Harness.True(Wallet_MainBankerFixedDomainCurrency_Patch.Prefix(
                wallet, inside, false, ref result), "inside native");

            DroppableCurrency broken = f.AddCoin(8f);
            broken.transform.position = new Vector3(float.NaN, 0f, 0f);
            result = true;
            Harness.False(Wallet_MainBankerFixedDomainCurrency_Patch.Prefix(
                wallet, broken, false, ref result), "NaN blocked");

            DroppableCurrency farm = f.AddCoin(8f);
            farm.droppedBy = DropType.Wildlife;
            result = true;
            Harness.True(Wallet_MainBankerFixedDomainCurrency_Patch.Prefix(
                wallet, farm, false, ref result), "non-player coin native");

            DroppableCurrency jade = f.AddCoin(8f);
            jade.CurrencyType = CurrencyType.Jade;
            result = true;
            Harness.True(Wallet_MainBankerFixedDomainCurrency_Patch.Prefix(
                wallet, jade, false, ref result), "non-coin currency native");

            Wallet otherWallet = Sim.NewActor("OtherWallet", f.Layer).AddComponent<Wallet>();
            result = true;
            Harness.True(Wallet_MainBankerFixedDomainCurrency_Patch.Prefix(
                otherWallet, outside, false, ref result), "non-banker wallet native");

            Banker other = f.AddBanker("BankerOther", kingdomBound: false);
            result = true;
            Harness.True(Wallet_MainBankerFixedDomainCurrency_Patch.Prefix(
                other._wallet, outside, false, ref result), "non-current banker wallet native");

            result = true;
            NetworkBigBoss.HasWorldAuth = false;
            Harness.True(Wallet_MainBankerFixedDomainCurrency_Patch.Prefix(
                wallet, outside, false, ref result), "client wallet native");
            NetworkBigBoss.HasWorldAuth = true;

            f.BreakDomain();
            result = true;
            Harness.False(Wallet_MainBankerFixedDomainCurrency_Patch.Prefix(
                wallet, outside, false, ref result), "unknown domain blocked");

            DroppableCurrency otherLayer = AddOtherLayerCoin(f, null, 8f);
            result = true;
            Harness.True(Wallet_MainBankerFixedDomainCurrency_Patch.Prefix(
                wallet, otherLayer, false, ref result), "other-layer coin native");

            Fixture foreign = Fixture.BuildForeign(sceneHandle: 3);
            Banker foreignBanker = foreign.AddBanker();
            DroppableCurrency foreignCoin = foreign.AddCoin(8f);
            result = true;
            Harness.True(Wallet_MainBankerFixedDomainCurrency_Patch.Prefix(
                foreignBanker._wallet, foreignCoin, false, ref result), "foreign wallet native");
        });

        Harness.Test("qualified position read faults fail closed in both hard gates", () =>
        {
            Fixture f = Fixture.BuildGreek();
            Banker banker = f.AddBanker();

            // wallet：已证 exact 本体钱包 + 当前层 Player/Coins 后，位置 getter 抛错也必须拒绝
            DroppableCurrency walletCoin = f.AddCoin(8f);
            bool result = true;
            Sim.FaultPosition = walletCoin.transform;
            bool allow = Wallet_MainBankerFixedDomainCurrency_Patch.Prefix(
                banker._wallet, walletCoin, false, ref result);
            Sim.FaultPosition = null;
            Harness.False(allow, "wallet fault cannot run native pickup");
            Harness.False(result, "wallet result false");

            // claim：同一纪律
            DroppableCurrency claimCoin = f.AddCoin(8f);
            result = true;
            Sim.FaultPosition = claimCoin.transform;
            allow = Droppable_MainBankerOutsideWallClaim_Patch.Prefix(
                claimCoin, banker.gameObject, ref result);
            Sim.FaultPosition = null;
            Harness.False(allow, "claim fault cannot run native claim");
            Harness.False(result, "claim result false");

            // 未证 owner 的其它钱包不读位置、保持原生（不得因故障扩大到别的钱包）
            Wallet other = Sim.NewActor("OtherWallet", f.Layer).AddComponent<Wallet>();
            result = true;
            Sim.FaultPosition = walletCoin.transform;
            allow = Wallet_MainBankerFixedDomainCurrency_Patch.Prefix(
                other, walletCoin, false, ref result);
            Sim.FaultPosition = null;
            Harness.True(allow, "unqualified wallet keeps native");
            Harness.True(result, "unqualified result untouched");
        });

        Harness.Test("load notification does not lift client catchup or auth gates", () =>
        {
            Fixture f = Fixture.BuildGreek(notify: false);
            NetworkBigBoss.IsOnline = true;
            NetworkBigBoss.HasClientCaughtUp = false;
            Notify(false);
            Harness.False(NetworkBigBoss.HasClientCaughtUp, "catchup gate untouched by load notification");
            Harness.True(NetworkBigBoss.HasWorldAuth, "auth gate untouched");
            Harness.True(MainBankerFixedDomain.TryCapture(Managers.Inst), "capture itself is read-only");
            Harness.False(NetworkBigBoss.HasClientCaughtUp, "still untouched after capture");
        });

        return Harness.Finish();
    }

    // ------------------------------------------------------------------ helpers
    private static void Notify(bool fromSave)
        => Managers_OnLevelLoaded_MainBankerFixedDomain_Patch.Postfix(Managers.Inst, fromSave);

    private static bool Domain(out float left, out float right)
        => MainBankerFixedDomain.TryGetDomain(out left, out right);

    /// <summary>清掉夹具默认的 ±5 墙，测试自备固定墙基结构。</summary>
    private static void ClearCandidates(Transform layer)
    {
        foreach (Wall wall in layer.GetComponentsInChildren<Wall>(true))
            if (wall != null && wall.gameObject != null) UnityEngine.Object.Destroy(wall.gameObject);
        foreach (PayableUpgrade upgrade in layer.GetComponentsInChildren<PayableUpgrade>(true))
            if (upgrade != null && upgrade.gameObject != null) UnityEngine.Object.Destroy(upgrade.gameObject);
    }

    private static Wall BuiltWall(Transform layer, float x)
    {
        GameObject go = Sim.NewActor("Wall", layer);
        go.transform.position = new Vector3(x, 0f, 0f);
        return go.AddComponent<Wall>();
    }

    /// <summary>未建墙基（Wall0 形状）：只有 PayableUpgrade.nextPrefab 直接含 Wall。</summary>
    private static PayableUpgrade UnbuiltFoundation(Transform layer, float x)
    {
        GameObject go = Sim.NewActor("Wall0", layer);
        go.transform.position = new Vector3(x, 0f, 0f);
        PayableUpgrade upgrade = go.AddComponent<PayableUpgrade>();
        GameObject next = Sim.NewActor("Wall1");
        next.AddComponent<Wall>();
        upgrade.nextPrefab = next;
        return upgrade;
    }

    private static DroppableCurrency AddRegisteredCoin(Fixture f, DroppableRegistrar registrar,
        float x, GameObject claimer)
    {
        DroppableCurrency coin = f.AddCoin(x);
        coin.friendlyClaimer = claimer;
        registrar.Droppables.Add(coin);
        return coin;
    }

    /// <summary>旧层/异层币：不在当前 gameLayer 子树，release 与门都不得触碰。</summary>
    private static DroppableCurrency AddOtherLayerCoin(Fixture f, GameObject claimer, float x)
    {
        GameObject oldLayer = Sim.NewLayer("OldLayer");
        GameObject go = Sim.NewActor("CoinOld", oldLayer.transform);
        go.transform.position = new Vector3(x, 0f, 0f);
        DroppableCurrency coin = go.AddComponent<DroppableCurrency>();
        coin.droppedBy = DropType.Player;
        coin.CurrencyType = CurrencyType.Coins;
        coin.friendlyClaimer = claimer;
        DroppableRegistrar registrar = Managers.Inst.dropManager;
        registrar?.Droppables.Add(coin);
        return coin;
    }
}
