// 金币哥布林 runtime bridge 套件：链接未修改的生产 Runtime / CoinFlight / RuntimeRules /
// CampaignState / 商店纯核 / Purse / Rules / HeroShop 纯核，用 Stubs 的边界替身按帧驱动，
// 覆盖 R2 复审的 6 个生产缺陷（落点朝向、配送距离、敌情不换人、银行站位区间、硬失效优先、
// 飞行金币生命周期）与"未接存档不得收费"基线。
//
// 未覆盖（明确声明）：商店的 PayableComponent/NetworkPostbox/ClassInjector 装配路径
// （需要真实 interop，由完整 2.4 构建与实机验证覆盖）；商店清理策略由纯核用例覆盖
// （CleanupGuard/OwnerPolicy/CancelPolicy 在 tests/coin-courier-runtime）。
using System;
using System.Collections.Generic;
using KingdomEnhancedMod;
using UnityEngine;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

int checks = 0;
void Verify(bool condition, string message)
{
    Check(condition, message);
    checks++;
}

const float HomeXFree = -3f;   // 固定主城锚：营火(0)+HomeOffsetX(-3)

Console.WriteLine("[bridge-1] landing is behind the Knight's own side");
{
    var h = new Harness();
    var right = h.MakeKnight(Side.Right, 10f);
    h.ArmVisit(right, 71, 1, 2);
    h.State.Purse = BridgeState.PurseWith(4);
    h.Step(0.05f);
    Verify(CoinCourierVisuals.Created == 1, "an owned, ready campaign creates the courier scene");
    Verify(h.RunUntil(() => CoinCourierVisuals.LastPose == CoinCourierPose.Fall, 3f),
        "the visit reaches the fall phase");
    Verify(Math.Abs(CoinCourierVisuals.LastPosition.x - 8f) < 0.01f,
        "a right-facing Knight lands at targetX - 2 (behind it)");

    var h2 = new Harness();
    var left = h2.MakeKnight(Side.Left, 10f);
    h2.ArmVisit(left, 72, -1, 2);
    h2.State.Purse = BridgeState.PurseWith(4);
    h2.Step(0.05f);
    Verify(h2.RunUntil(() => CoinCourierVisuals.LastPose == CoinCourierPose.Fall, 3f),
        "the mirrored visit reaches the fall phase");
    Verify(Math.Abs(CoinCourierVisuals.LastPosition.x - 12f) < 0.01f,
        "a left-facing Knight lands at targetX + 2 (behind it)");
}

Console.WriteLine("[bridge-2] a side change in flight cancels the stale behind-point");
{
    var h = new Harness();
    var knight = h.MakeKnight(Side.Right, 10f);
    h.ArmVisit(knight, 73, 1, 2);
    h.State.Purse = BridgeState.PurseWith(4);
    h.Step(0.05f);
    Verify(h.RunUntil(() => CoinCourierTeleportFx.BeginCalls >= 1, 3f), "the jump departure strip plays");
    Verify(CoinCourierTeleportFx.LastStyle == CoinCourierTeleportStyle.Vertical,
        "the courier's teleport stripes use the explicit vertical style");
    knight.side = Side.Left;   // 飞行中转身：原落点已失效
    h.Step(1.2f);
    Verify(CoinCourierEconomy.DeliverCalls == 0, "no coin is delivered to the stale landing");
    Verify(CoinCourierVisuals.LastPosition.x < -2f,
        "the courier returns to the bank instead of landing on the stale point");
}

Console.WriteLine("[bridge-2b] legacy Begin shapes keep the horizontal contract; only the explicit 6-argument call carries a style");
{
    CoinCourierTeleportFx.Begin(Vector3.zero, Color.white, 1f);
    Verify(CoinCourierTeleportFx.LastStyle == CoinCourierTeleportStyle.Horizontal,
        "the legacy 3-argument Begin keeps the horizontal contract");
    CoinCourierTeleportFx.Begin(Vector3.zero, Color.white, 1f, 3, 9);
    Verify(CoinCourierTeleportFx.LastStyle == CoinCourierTeleportStyle.Horizontal,
        "the legacy 5-argument Begin keeps the horizontal contract");
    CoinCourierTeleportFx.Begin(Vector3.zero, Color.white, 1f, 3, 9, CoinCourierTeleportStyle.Vertical);
    Verify(CoinCourierTeleportFx.LastStyle == CoinCourierTeleportStyle.Vertical,
        "the 6-argument Begin records the explicit style");
}

Console.WriteLine("[bridge-3] one Approach→Deliver; coins only within the 1.2 handoff range");
{
    var h = new Harness();
    CoinCourierEconomy.DeliverApplied = true;
    var knight = h.MakeKnight(Side.Right, 10f);
    h.ArmVisit(knight, 74, 1, 4);
    h.State.Purse = BridgeState.PurseWith(4);
    h.Step(0.05f);
    Verify(h.RunUntil(() => CoinCourierEconomy.DeliverAppliedCount >= 1, 3f),
        "the first coin is handed over up close");
    Verify(Math.Abs(CoinCourierEconomy.DeliverPositions[0] - knight.transform.position.x) <= 1.2f,
        "the first handover happens within the handoff range");

    // 骑士在交接范围内小幅移动：仍可继续交接，且运行时不再重新接近（姿态不回到 Run）。
    knight.transform.position = new Vector3(10.6f, SimPhysics.GroundTop, 0f);
    Verify(h.RunUntil(() => CoinCourierEconomy.DeliverAppliedCount >= 2, 3f),
        "a Knight drifting inside the handoff range still receives the next coin");
    Verify(h.TransitionCount(CoinCourierPose.Deliver, CoinCourierPose.Run) == 0,
        "the courier never re-enters the approach after Deliver (single Approach→Deliver)");

    // 骑士离开交接范围：本访问结束、回银行带余币，不再有到账（窗口小于中止冷却 2.5s）。
    int before = CoinCourierEconomy.DeliverAppliedCount;
    double purseBefore = h.State.Purse.Coins;
    knight.transform.position = new Vector3(14f, SimPhysics.GroundTop, 0f);
    h.Step(2f);
    Verify(CoinCourierEconomy.DeliverAppliedCount == before,
        "a Knight leaving the handoff range receives no further coins in this visit");
    Verify(h.State.Purse.Coins == purseBefore, "the remainder stays in the purse (no refund, no loss)");
    Verify(!h.State.Purse.IsFaulted && !h.State.Purse.HasPendingDelivery,
        "a handoff-range abort is a definite non-attempt: no fault, no dangling reserve");
    Verify(h.TransitionCount(CoinCourierPose.Deliver, CoinCourierPose.Run) == 0,
        "leaving the handoff range ends the visit instead of chasing");
    h.Step(8f);
    for (int i = 0; i < CoinCourierEconomy.DeliverCalls; i++)
    {
        float dx = Math.Abs(CoinCourierEconomy.DeliverPositions[i] - CoinCourierEconomy.DeliverTargetXs[i]);
        Verify(dx <= 1.2f, "every recorded handover stays within the handoff range (coin " + i + ")");
    }
}

Console.WriteLine("[bridge-4] enemy pressure returns to the bank instead of the next Knight");
{
    var h = new Harness();
    CoinCourierEconomy.DeliverApplied = true;
    var knight = h.MakeKnight(Side.Right, 10f);
    h.ArmVisit(knight, 75, 1, 4);
    h.State.Purse = BridgeState.PurseWith(4);
    h.Step(0.05f);
    Verify(h.RunUntil(() => CoinCourierEconomy.DeliverCalls >= 1, 3f), "the first coin is delivered");
    Scanner.Enemy = new GameObject("Enemy");   // 敌情逼近
    h.Step(4f);
    Verify(CoinCourierEconomy.DeliverCalls == 1, "enemy pressure stops further handovers");
    Verify(h.State.Purse.Coins == 3, "the remaining purse coins are kept (not spent, not refunded)");
    Verify(Math.Abs(CoinCourierVisuals.LastPosition.x - HomeXFree) < 0.01f,
        "the courier carries the remainder back to the bank and stays there under threat");
}

Console.WriteLine("[bridge-5] fixed campfire home ignores buildings, obstacles and payment reservations");
{
    // 用户新合同（取代此前窗口/建筑排除/付款间隔候选搜索）：NPC 就站在主城篝火左侧 3，
    // 与建筑/装饰图像重叠属原版语义，绝不因此拒绝创建。
    var h = new Harness();
    SimPhysics.AddObstacle(-3.9f, -2.1f);            // 覆盖固定点的建筑/装饰
    SimPhysics.AddObstacle(-5f, -1f, 8f, 10f);       // 高处装饰
    SimPhysics.AddObstacle(-4.0f, -3.5f, 0f, 6f, isTrigger: true);   // 无关 FX 触发体
    h.Step(0.1f);
    Verify(CoinCourierVisuals.Created == 1, "a covered home still creates the courier");
    Verify(Math.Abs(CoinCourierVisuals.LastPosition.x - HomeXFree) < 0.01f,
        "the courier stands at campfire-3 regardless of the overlapping building");
    Verify(Math.Abs(CoinCourierVisuals.LastPosition.y - SimPhysics.GroundTop) < 0.01f,
        "the home y is the single real ground probe at that point");

    // 单点地面不可读：等待（不编造高度、不重选点）；恢复后仍是同一固定锚。
    var missing = new Harness();
    SimPhysics.Enabled = false;
    missing.Step(0.1f);
    Verify(CoinCourierVisuals.Created == 0, "no readable ground means no scene (no fabricated y)");
    SimPhysics.Enabled = true;
    missing.Step(0.1f);
    Verify(CoinCourierVisuals.Created == 1, "readable ground creates at the fixed home");
    Verify(Math.Abs(CoinCourierVisuals.LastPosition.x - HomeXFree) < 0.01f,
        "the recovered scene uses the same fixed home");
}

Console.WriteLine("[bridge-5b] delivery still refuses a landing blocked by real obstacles");
{
    var h = new Harness();
    CoinCourierEconomy.DeliverApplied = true;
    var knight = h.MakeKnight(Side.Right, 10f);   // 右侧目标的合法落点是其后方 x=8
    SimPhysics.AddObstacle(7.5f, 8.5f);            // 唯一落点被真实障碍覆盖
    h.ArmVisit(knight, 81, 1, 2);
    h.State.Purse = BridgeState.PurseWith(4);
    h.Step(0.05f);
    h.Step(2f);
    Verify(CoinCourierEconomy.DeliverCalls == 0, "a blocked landing means no delivery");
    Verify(h.State.Purse.Coins == 4, "the purse keeps every coin when the landing is blocked");
    Verify(CoinCourierVisuals.LastPose != CoinCourierPose.Fall, "the courier never falls onto the blocked point");
    Verify(Math.Abs(CoinCourierVisuals.LastPosition.x - HomeXFree) < 0.01f,
        "the courier waits at the fixed home instead of forcing the visit");
}

Console.WriteLine("[bridge-6] hard failures clear first; a normal menu pause only freezes");
{
    var h = new Harness();
    h.Step(0.1f);
    Verify(CoinCourierVisuals.Created == 1 && CoinCourierVisuals.Destroyed == 0, "scene exists before the loss");

    // 暂停中的失权：硬失效优先 → 清场。
    Time.timeScale = 0f;
    NetworkBigBoss.HasWorldAuth = false;
    h.Step(0.2f);
    Verify(CoinCourierVisuals.Destroyed == 1, "lost authority clears the scene even while paused");
    Verify(h.State.Owned && h.State.Purse.Coins == 0, "clearing never touches Owned/purse");

    // 正常 Menu 暂停：只冻结，场景保留。
    NetworkBigBoss.HasWorldAuth = true;
    Time.timeScale = 1f;
    h.Step(0.1f);   // 重建场景
    Verify(CoinCourierVisuals.Created == 2, "the scene is rebuilt once the hard facts pass");
    Time.timeScale = 0f;
    Managers.Inst.game.state = Game.State.Menu;
    h.Step(0.2f);
    Verify(CoinCourierVisuals.Destroyed == 1, "a menu pause keeps the frozen scene instead of clearing");

    // 暂停中解除绑定：立即清场。
    CoinCourierRuntime.Bind(null);
    h.Step(0.2f);
    Verify(CoinCourierVisuals.Destroyed == 2, "an unbound owner clears the scene immediately, even paused");
    Time.timeScale = 1f;
    Managers.Inst.game.state = Game.State.Playing;
}

Console.WriteLine("[bridge-7] coin flight lives and dies with this world only");
{
    var h = new Harness();
    CoinCourierEconomy.BagApplied = true;
    h.DisarmVisit();
    Verify(h.RunUntil(() => CoinCourierEconomy.BagCalls >= 1, 2f),
        "the courier bags coins while no Knight needs service");
    h.Step(0.05f);
    Verify(CoinCourierCoinFlight.ActiveCount >= 1, "a bagged coin starts a display-only flight");

    Time.timeScale = 0f;
    Managers.Inst.game.state = Game.State.Menu;
    h.Step(0.2f);
    Verify(CoinCourierCoinFlight.ActiveCount >= 1, "a normal pause keeps the in-flight coins");

    Time.timeScale = 1f;
    Managers.Inst.game.state = Game.State.Playing;
    ModConfig.CoinCourierEnabled.Value = false;
    h.Step(0.2f);
    Verify(CoinCourierCoinFlight.ActiveCount == 0, "disabling the feature clears the display coins");
    Verify(CoinCourierTeleportFx.ClearCalls == 0,
        "the shared teleport FX pool is never cleared by this business");
    ModConfig.CoinCourierEnabled.Value = true;
}

Console.WriteLine("[bridge-8] teleport FX is cancelled per-handle, never cleared");
{
    var h = new Harness();
    var knight = h.MakeKnight(Side.Right, 10f);
    h.ArmVisit(knight, 76, 1, 2);
    h.State.Purse = BridgeState.PurseWith(4);
    h.Step(0.05f);
    Verify(h.RunUntil(() => CoinCourierTeleportFx.BeginCalls >= 1, 3f), "the courier begins its own effect");
    CoinCourierRuntime.Bind(null);
    h.Step(0.2f);
    Verify(CoinCourierTeleportFx.CancelCalls >= 1, "invalidating the scene cancels only its own handles");
    Verify(CoinCourierTeleportFx.ClearCalls == 0, "the shared pool is never cleared by this business");
}

Console.WriteLine("[bridge-9] unbound or not-ready campaigns never touch money or scene");
{
    var h = new Harness();
    CoinCourierRuntime.Bind(null);
    h.Step(1f);
    Verify(CoinCourierVisuals.Created == 0 && CoinCourierEconomy.BagCalls == 0
        && CoinCourierEconomy.DeliverCalls == 0, "an unbound campaign creates no character and no economy");

    h.State.Availability = CoinCourierAvailabilityInfo.Of(CoinCourierAvailability.Paused);
    CoinCourierRuntime.Bind(h.State);
    h.Step(1f);
    Verify(CoinCourierVisuals.Created == 0 && CoinCourierEconomy.BagCalls == 0
        && CoinCourierEconomy.DeliverCalls == 0, "a paused campaign freezes without creating or spending");
    h.State.Availability = CoinCourierAvailabilityInfo.Of(CoinCourierAvailability.Ready);
}

Console.WriteLine("[bridge-10] 60s continuous chase of a reachable moving target (1.5 u/s)");
{
    // 已批准常量下的可达上限：从计划到接近开始的延迟约 1.06s（Jump .40 + Teleport .18 + Fall .24 + Land .24），
    // 于是 2.0 + 1.06v - 3.5(1 - v/7.5) <= 1.2 → v <= ~1.77 u/s（步行速度带）。
    // 这里用 1.5 u/s 验证"可达时有首币、单访问一次 Approach→Deliver、有界移动"。
    var h = new Harness();
    CoinCourierEconomy.BagApplied = true;
    CoinCourierEconomy.DeliverApplied = true;
    ModConfig.CoinCourierPurseCapacity.Value = 4;
    var knight = h.MakeKnight(Side.Right, 10f);
    h.ArmVisit(knight, 77, 1, 4);
    h.MovingTarget = knight;
    h.MovingTargetSpeed = 1.5f;
    h.Step(60f);   // 单次连续 60 秒：不允许用小窗口重置预算/期限
    Verify(CoinCourierEconomy.DeliverAppliedCount >= 4,
        "a continuously-moving reachable target still receives coins (first coin lands)");
    Verify(h.TransitionCount(CoinCourierPose.Deliver, CoinCourierPose.Run) == 0,
        "a 60-second chase never re-enters Approach after Deliver");
    Verify(h.TransitionCount(CoinCourierPose.Run, CoinCourierPose.Deliver) == h.TransitionCount(CoinCourierPose.Fall, CoinCourierPose.Land),
        "every landed visit performs exactly one Approach→Deliver");
    var stretches = new List<(float Path, float MaxDeviation)>();
    h.RunStretches(stretches);
    Verify(stretches.Count >= 3, "the 60s run actually performed several approaches");
    foreach ((float path, float maxDeviation) in stretches)
    {
        Verify(path <= 3.5f + 0.002f, "each approach's travel stays inside the 3.5u budget");
        Verify(maxDeviation <= 3.5f + 0.002f, "each approach never wanders more than 3.5u from its landing");
    }
    Verify(CoinCourierEconomy.BagAppliedCount == CoinCourierEconomy.DeliverAppliedCount + h.State.Purse.Coins,
        "bagged coins are conserved: delivered + purse remainder");
    ModConfig.CoinCourierPurseCapacity.Value = 12;
}

Console.WriteLine("[bridge-10b] 60s continuous chase of a 4 u/s target: bounded, no re-approach, purse kept");
{
    // 4 u/s 超过上述可达上限：允许落地并在预算内追击，但绝不重入 Approach、绝不远追；
    // 币不出袋（余额守恒），每访问最多一次 Approach。
    var h = new Harness();
    CoinCourierEconomy.BagApplied = true;
    CoinCourierEconomy.DeliverApplied = true;
    ModConfig.CoinCourierPurseCapacity.Value = 4;
    var knight = h.MakeKnight(Side.Right, 10f);
    h.ArmVisit(knight, 770, 1, 4);
    h.MovingTarget = knight;
    h.MovingTargetSpeed = 4f;
    h.Step(60f);
    Verify(h.TransitionCount(CoinCourierPose.Deliver, CoinCourierPose.Run) == 0,
        "a 60-second chase of a faster target never re-enters Approach");
    Verify(h.TransitionCount(CoinCourierPose.Run, CoinCourierPose.Deliver) <= h.TransitionCount(CoinCourierPose.Fall, CoinCourierPose.Land),
        "a landed visit performs at most one Approach→Deliver");
    var stretches = new List<(float Path, float MaxDeviation)>();
    h.RunStretches(stretches);
    Verify(stretches.Count >= 3, "the 60s run actually performed several approaches");
    foreach ((float path, float maxDeviation) in stretches)
    {
        Verify(path <= 3.5f + 0.002f, "an outrun target is chased at most 3.5u per visit");
        Verify(maxDeviation <= 3.5f + 0.002f, "an outrun target is chased at most 3.5u from the landing");
    }
    Verify(CoinCourierEconomy.BagAppliedCount == CoinCourierEconomy.DeliverAppliedCount + h.State.Purse.Coins,
        "the purse is conserved even when a target outruns the courier");
    Verify(CoinCourierEconomy.DeliverAppliedCount == 0,
        "under the approved constants a 4 u/s target stays out of the 1.2 handoff range");
    ModConfig.CoinCourierPurseCapacity.Value = 12;
}

Console.WriteLine("[bridge-11] an unreachable target aborts on budget, not on an endless chase");
{
    var h = new Harness();
    CoinCourierEconomy.DeliverApplied = true;
    var knight = h.MakeKnight(Side.Right, 10f);
    h.ArmVisit(knight, 78, 1, 4);
    h.State.Purse = BridgeState.PurseWith(4);
    h.Step(0.05f);
    // 落地瞬间目标才开始狂奔（先让空降判定通过，从而真正考验预算分支）。
    Verify(h.RunUntil(() => CoinCourierVisuals.LastPose == CoinCourierPose.Land, 3f),
        "the visit lands before the target bolts");
    h.MovingTarget = knight;
    h.MovingTargetSpeed = 20f;
    h.Step(2.5f);
    Verify(CoinCourierEconomy.DeliverAppliedCount == 0,
        "a target faster than the courier never receives a coin in this visit");
    Verify(CoinCourierVisuals.LastPosition.x < -2f,
        "the courier is back at the bank well before the 8s deadline (budget abort)");
    Verify(h.State.Purse.Coins == 4, "the whole purse stays intact after a budget abort");
    Verify(!h.State.Purse.IsFaulted && !h.State.Purse.HasPendingDelivery,
        "a budget abort leaves no fault and no unconsumed reserve");
    var stretches = new List<(float Path, float MaxDeviation)>();
    h.RunStretches(stretches);
    foreach ((float path, float maxDeviation) in stretches)
    {
        Verify(path <= 3.5f + 0.002f, "an unreachable target is chased at most 3.5u");
        Verify(maxDeviation <= 3.5f + 0.002f, "an unreachable target is chased at most 3.5u from the landing");
    }
}

Console.WriteLine("[bridge-12] a static target still gets at most 4 coins per visit");
{
    var h = new Harness();
    CoinCourierEconomy.DeliverApplied = true;
    var knight = h.MakeKnight(Side.Right, 10f);
    h.ArmVisit(knight, 79, 1, 4);
    h.State.Purse = BridgeState.PurseWith(10);
    h.Step(0.05f);
    h.Step(10f);
    Verify(CoinCourierEconomy.DeliverAppliedCount == 4,
        "a static Knight receives exactly the four-coin visit quota");
    Verify(h.State.Purse.Coins == 6, "only four coins left the purse");
    Verify(h.RunStretchesCount() == 1, "the static visit performs exactly one approach");
    h.Step(6f);   // 仍在 15s 冷却内
    Verify(CoinCourierEconomy.DeliverAppliedCount == 4,
        "the same Knight is not revisited during its cooldown");
}

Console.WriteLine("[bridge-13] no authoritative bank: an empty purse waits, the character is kept");
{
    var h = new Harness();
    CoinCourierBankScope.Current = null;
    CoinCourierBankScope.Reason = CoinCourierBankReason.EntryMissing;
    h.Step(0.05f);
    Verify(CoinCourierVisuals.Created == 1, "the character is created without any authoritative bank");
    h.Step(3f);
    Verify(CoinCourierVisuals.Destroyed == 0, "a missing bank never clears the character");
    Verify(CoinCourierEconomy.BagCalls == 0, "the empty purse makes no bag attempt without an authority");
    Verify(h.State.Purse.Coins == 0 && h.State.Owned, "purse and identity are untouched");
    Verify(CoinCourierRuntime.TreasuryStatusText.Contains("装袋"),
        "the treasury line explains the wait: " + CoinCourierRuntime.TreasuryStatusText);
}

Console.WriteLine("[bridge-14] existing purse coins are delivered even with no authoritative bank");
{
    var h = new Harness();
    CoinCourierEconomy.DeliverApplied = true;
    h.State.Purse = BridgeState.PurseWith(2);
    var knight = h.MakeKnight(Side.Right, 10f);
    h.ArmVisit(knight, 80, 1, 2);
    CoinCourierBankScope.Current = null;
    CoinCourierBankScope.Reason = CoinCourierBankReason.EntryMissing;
    h.Step(0.05f);
    Verify(h.RunUntil(() => CoinCourierEconomy.DeliverAppliedCount >= 1, 3f),
        "an existing purse coin is delivered with no authoritative bank");
    Verify(CoinCourierEconomy.BagCalls == 0, "delivery never tries to bag");
    Verify(h.State.Purse.Coins == 1, "one coin left the purse");
}

Console.WriteLine("[bridge-15] a refused bag returns to wait (no freeze); restored identity resumes");
{
    var h = new Harness();
    h.DisarmVisit();
    CoinCourierEconomy.BagApplied = false;
    CoinCourierEconomy.BagRefusal = CoinCourierReason.BankGateClosed;
    h.Step(1f);
    Verify(CoinCourierEconomy.BagCalls >= 1, "the runtime attempts to bag while the identity is live");
    Verify(h.State.Purse.Coins == 0, "a definite refusal credits nothing");
    Verify(!h.State.Purse.IsFaulted, "a definite refusal is not an economy fault");
    Verify(CoinCourierVisuals.Destroyed == 0, "the scene survives a definite refusal");

    CoinCourierBankScope.Current = null;   // 取币中途身份消失
    h.Step(1.5f);
    int callsWithoutIdentity = CoinCourierEconomy.BagCalls;
    h.Step(1.5f);
    Verify(CoinCourierEconomy.BagCalls == callsWithoutIdentity,
        "with no identity the runtime waits instead of calling the economy");

    CoinCourierBankScope.Current = h.Banker;
    CoinCourierEconomy.BagApplied = true;
    Verify(h.RunUntil(() => CoinCourierEconomy.BagAppliedCount >= 1, 4f),
        "restored funds and identity resume bagging (a refusal is not a freeze)");
}

Console.WriteLine("[bridge-16] status texts: faults first, pause distinct, no implementation ids");
{
    var h = new Harness();
    h.State.Purse = BridgeState.PurseWith(3);
    Verify(CoinCourierRuntime.StatusText.Contains("钱袋 3/"), "the character line shows the purse");
    Verify(CoinCourierRuntime.RecruitmentStatusText().Contains("已招募"), "the shop line shows ownership");

    h.State.Availability = CoinCourierAvailabilityInfo.Of(CoinCourierAvailability.Menu);
    Verify(CoinCourierRuntime.StatusText.Contains("游戏已暂停"), "menu pause is reported as paused");
    Verify(!CoinCourierRuntime.StatusText.Contains("存档"), "a pause is not displayed as a save fault");
    Verify(CoinCourierRuntime.TreasuryStatusText.StartsWith("游戏已暂停", StringComparison.Ordinal),
        "the treasury line is paused too: " + CoinCourierRuntime.TreasuryStatusText);

    h.State.Availability = CoinCourierAvailabilityInfo.Of(CoinCourierAvailability.Loading);
    Verify(CoinCourierRuntime.StatusText.Contains("加载中") && !CoinCourierRuntime.StatusText.Contains("暂停"),
        "loading is never called pause");

    h.State.Availability = CoinCourierAvailabilityInfo.Of(CoinCourierAvailability.Saving);
    Verify(CoinCourierRuntime.StatusText.Contains("保存中"), "saving is named");

    h.State.Availability = CoinCourierAvailabilityInfo.Of(CoinCourierAvailability.Online);
    Verify(CoinCourierRuntime.StatusText.Contains("仅限离线单机"), "online is named");

    h.State.Availability = new CoinCourierAvailabilityInfo(CoinCourierAvailability.PersistenceFault, "capture-no-marker");
    Verify(CoinCourierRuntime.StatusText.Contains("捕获故障") && !CoinCourierRuntime.StatusText.Contains("暂停"),
        "a real persistence fault is never masked by pause");

    h.State.Availability = CoinCourierAvailabilityInfo.Of(CoinCourierAvailability.NotBound);
    Verify(!CoinCourierRuntime.StatusText.Contains("钱袋"),
        "a non-current identity never shows the old balance");

    CoinCourierRuntime.Bind(new ThrowingOwnedState());
    Verify(CoinCourierRuntime.StatusText.Contains("不可读") && !CoinCourierRuntime.StatusText.Contains("招募价"),
        "an unreadable owned state never falls into the price text: " + CoinCourierRuntime.StatusText);

    h.State.Availability = CoinCourierAvailabilityInfo.Of(CoinCourierAvailability.Ready);
    CoinCourierRuntime.Bind(h.State);
    CoinCourierBankScope.Current = h.Banker;
    Verify(CoinCourierRuntime.TreasuryStatusText == "国库已连接",
        "a connected treasury makes no funds promise: " + CoinCourierRuntime.TreasuryStatusText);
    CoinCourierBankScope.Current = null;
    CoinCourierBankScope.Reason = CoinCourierBankReason.PostboxMissing;
    Verify(!CoinCourierRuntime.TreasuryStatusText.Contains("903"),
        "product text never exposes the internal registration id");

    // 正常在途 pending（未出错）不是 unknown，两行都不得谎报冻结。
    var pendingState = new BridgeState { Owned = true, Purse = BridgeState.PurseWith(2) };
    Verify(pendingState.Purse.TryReserveOneForDelivery(41).Applied, "a normal delivery reservation is installed");
    CoinCourierRuntime.Bind(pendingState);
    Verify(!CoinCourierRuntime.StatusText.Contains("未知"),
        "a normal pending delivery is not an unknown: " + CoinCourierRuntime.StatusText);
    Verify(CoinCourierRuntime.StatusText.Contains("已招募") && CoinCourierRuntime.StatusText.Contains("钱袋 2/"),
        "the purse still shows its balance with a pending coin");
    Verify(!CoinCourierRuntime.TreasuryStatusText.Contains("未知"),
        "the treasury line treats pending as normal");

    CoinCourierRuntime.Bind(null);
    Verify(CoinCourierRuntime.StatusText.Contains("身份待确认"),
        "an unbound runtime stays at identity-pending");
}

Console.WriteLine("[bridge-17] real persistence owner drives the runtime status texts");
{
    var h = new Harness();
    GlobalSaveData global = h.BuildRealPersistence();
    Verify(CoinCourierRuntime.State != null, "the runtime is bound to the real campaign owner");
    Verify(CoinCourierRuntime.StatusText.Contains("未招募"),
        "a real Ready binding shows the normal baseline: " + CoinCourierRuntime.StatusText);
    Verify(CoinCourierRuntime.State.TryRecordRecruitment(), "recruitment is recorded through the real owner");

    // (c) Menu + recruitmentLocked：锁与暂停同时可见，锁不被暂停盖住。
    CoinCourierRuntime.LatchRecruitment("bridge-lock");
    Managers.Inst.game.state = Game.State.Menu;
    string locked = CoinCourierRuntime.StatusText;
    Verify(locked.Contains("已锁定") && locked.Contains("游戏已暂停"),
        "(c) Menu+recruitmentLocked shows both: " + locked);
    Verify(CoinCourierRuntime.RecruitmentStatusText().Contains("已锁定"),
        "(c) the shop line shows the lock as well");

    // 解锁（仅测试手法）：换绑同一 owner 会清掉上一世代的锁。
    ICoinCourierCampaignState state = CoinCourierRuntime.State;
    CoinCourierRuntime.Bind(null);
    CoinCourierRuntime.Bind(state);

    // (d) Menu + DeliveryUnknown：资金未知优先于暂停，两行都不得只显示暂停。
    state.Purse.MarkUnknown(new CoinCourierFault(CoinCourierFaultKind.DeliveryUnknown,
        CoinCourierReason.WalletWriteUnknown, -1, -1, 1, -1, 0));
    string pausedUnknown = CoinCourierRuntime.StatusText;
    Verify(pausedUnknown.Contains("配送结果未知") && pausedUnknown.Contains("游戏已暂停"),
        "(d) Menu+DeliveryUnknown shows the unknown next to pause: " + pausedUnknown);
    Verify(CoinCourierRuntime.TreasuryStatusText.Contains("配送结果未知"),
        "(d) the treasury line shows the unknown too: " + CoinCourierRuntime.TreasuryStatusText);
    Verify(CoinCourierRuntime.RecruitmentStatusText().Contains("暂不可招募"),
        "(d) the shop line still blocks on the pause: " + CoinCourierRuntime.RecruitmentStatusText());

    // (e) Playing + unknown + connected 903：不得谎报"国库已连接"。
    Managers.Inst.game.state = Game.State.Playing;
    CoinCourierBankScope.Current = h.Banker;
    CoinCourierBankScope.Reason = CoinCourierBankReason.Ready;
    string connectedLooking = CoinCourierRuntime.TreasuryStatusText;
    Verify(connectedLooking.Contains("配送结果未知") && !connectedLooking.Contains("国库已连接"),
        "(e) a connected identity never hides a frozen purse: " + connectedLooking);

    // (f) PersistenceFault + unknown：先真实落一次 staged（recruit 已让快照有内容），
    //     再让真实 Prepare 链写失败；两行都必须同时给出资金未知与持久化故障。
    IslandSaveData.Save(global.currentCampaign, 1, 0);
    global.prefs.ThrowOnSet = true;
    global.prefs.PrepareBeforeSave();
    Verify(CoinCourierRuntime.State.Availability.Kind == CoinCourierAvailability.PersistenceFault,
        "the real owner reports the persistence fault: " + CoinCourierRuntime.State.Availability.Kind);
    string both = CoinCourierRuntime.StatusText;
    Verify(both.Contains("配送结果未知") && both.Contains("存档捕获故障"),
        "(f) PersistenceFault+unknown shows both: " + both);
    Verify(CoinCourierRuntime.TreasuryStatusText.Contains("配送结果未知"),
        "(f) the treasury line keeps the unknown: " + CoinCourierRuntime.TreasuryStatusText);
    global.prefs.ThrowOnSet = false;
}

Console.WriteLine("[bridge-18] closed-global diagnostics are identity-checked at read time");
{
    var h = new Harness();
    h.BuildRealPersistence(rawKey: "{broken");
    Verify(CoinCourierPersistence.TryGetCurrentClosedDiagnostic(out CoinCourierAvailabilityInfo closed),
        "the real owner reports the closed current global");
    Verify(closed.Kind == CoinCourierAvailability.Closed && closed.Detail.Contains("corrupt"),
        "the close reason is the real record fault: " + closed.Detail);
    Verify(CoinCourierRuntime.State == null, "a closed global leaves the runtime unbound");
    Verify(CoinCourierRuntime.StatusText.Contains("记录损坏") && CoinCourierRuntime.StatusText.Contains("corrupt"),
        "the current global's close reason is shown: " + CoinCourierRuntime.StatusText);

    // 换到另一份 native global 但不 Tick：旧关闭原因必须立即不可读，也不得显示旧身份。
    var fresh = new GlobalSaveData { currentCampaign = 0, currentChallenge = 0 };
    fresh.campaigns.Add(new CampaignSaveData { CurrentLand = 1 });
    GlobalSaveData._loaded = fresh;
    CampaignSaveData.current = fresh.campaigns[0];
    Verify(!CoinCourierPersistence.TryGetCurrentClosedDiagnostic(out _),
        "a different native global is not the closed owner");
    Verify(CoinCourierRuntime.StatusText.Contains("身份待确认") && !CoinCourierRuntime.StatusText.Contains("记录损坏"),
        "the old close reason never leaks to the new global: " + CoinCourierRuntime.StatusText);

    CoinCourierPersistence.Tick();
    Verify(CoinCourierRuntime.State != null && CoinCourierRuntime.StatusText.Contains("未招募"),
        "Tick then binds the fresh global through the real owner");
}

Console.WriteLine("[bridge-19] a known empty bank allows a full local leisure segment; later funds are bagged at home");
{
    var h = new Harness();
    PatchEconomy_Banker.BalanceKnown = true;
    h.Banker._stashedCoins = 0;
    h.Step(4.2f);
    int leisureFrames = 0;
    foreach (var sample in h.Trace)
        if (sample.Pose == CoinCourierPose.Leisure) leisureFrames++;
    Verify(leisureFrames >= 100, "known empty funds allow the authored Leisure segment to play");
    Verify(CoinCourierEconomy.BagCalls == 0, "known empty funds never enter Collect");
    h.Step(1f);
    Verify(CoinCourierVisuals.LastPosition.x > HomeXFree + 0.2f,
        "the visual root takes a short daytime walk from the fixed home");
    h.Banker._stashedCoins = 2;
    CoinCourierEconomy.BagApplied = true;
    Verify(h.RunUntil(() => CoinCourierEconomy.BagAppliedCount > 0, 7f),
        "new funds resume the real bag transaction");
    Verify(Math.Abs(CoinCourierEconomy.BagPositions[0] - HomeXFree) < 0.02f,
        "bagging waits for the normal walking return to the fixed home");
}

Console.WriteLine("[bridge-20] a new visit preempts leisure directly from its current position");
{
    var h = new Harness();
    PatchEconomy_Banker.BalanceKnown = true;
    h.Banker._stashedCoins = 0;
    h.State.Purse = BridgeState.PurseWith(4);
    h.Step(5.2f);
    float walkingX = CoinCourierVisuals.LastPosition.x;
    Verify(walkingX > HomeXFree + 0.2f, "the visit is armed while away from home");
    var knight = h.MakeKnight(Side.Right, 10f);
    h.ArmVisit(knight, 871, 1, 2);
    Verify(h.RunUntil(() => CoinCourierVisuals.LastPose == CoinCourierPose.Jump, 1.5f),
        "a new Knight is selected at the existing service cadence");
    Verify(CoinCourierVisuals.LastPosition.x > HomeXFree + 0.2f
        && Math.Abs(CoinCourierVisuals.LastPosition.x - walkingX) < 0.6f,
        "visit departure uses the progressed strolling position, without a home snap");
}

Console.WriteLine("[bridge-21] pause and save do not advance the private leisure timer");
{
    var h = new Harness();
    PatchEconomy_Banker.BalanceKnown = true;
    h.Banker._stashedCoins = 0;
    h.Step(1f);
    Managers.Inst.game.state = Game.State.Menu;
    h.Step(8f); // the fixture advances Time.time even while the menu gate freezes behavior
    IslandSaveData.isSavingGame = true;
    Managers.Inst.game.state = Game.State.Playing;
    h.Step(8f);
    IslandSaveData.isSavingGame = false;
    h.Step(3f);
    Verify(Math.Abs(CoinCourierVisuals.LastPosition.x - HomeXFree) < 0.02f,
        "menu and saving time do not skip the remaining pause segment");
    h.Step(0.6f);
    Verify(CoinCourierVisuals.LastPosition.x > HomeXFree,
        "the local timer resumes on open scaled frames");
}

Console.WriteLine("[bridge-22] night returns by walking; missing ground stops on a standing frame");
{
    var h = new Harness();
    PatchEconomy_Banker.BalanceKnown = true;
    h.Banker._stashedCoins = 0;
    h.Step(5.2f);
    float awayX = CoinCourierVisuals.LastPosition.x;
    Verify(awayX > HomeXFree + 0.2f, "daytime stroll has started");
    SimPhysics.HasGround = x => x <= awayX + 0.001f;
    h.Step(0.2f);
    Verify(Math.Abs(CoinCourierVisuals.LastPosition.x - awayX) < 0.02f,
        "unverified next ground never moves the visual root");
    Verify(CoinCourierVisuals.LastPose == CoinCourierPose.Idle,
        "a blocked walk shows a standing frame rather than running in place");
    SimPhysics.HasGround = _ => true;
    h.Kingdom.isDaytime = false;
    h.Tick();
    Verify(CoinCourierVisuals.LastPosition.x > HomeXFree + 0.1f,
        "nightfall does not snap the courier to home");
    Verify(h.RunUntil(() => Math.Abs(CoinCourierVisuals.LastPosition.x - HomeXFree) < 0.02f, 4f),
        "nightfall returns to the fixed home by short steps");
    h.Step(5f);
    Verify(Math.Abs(CoinCourierVisuals.LastPosition.x - HomeXFree) < 0.02f
        && CoinCourierVisuals.LastPose == CoinCourierPose.Idle,
        "night does not begin another leisure walk or bag gesture");
}

Console.WriteLine("[bridge-23] unknown local threat sensing stops leisure; a real enemy uses the existing jump home");
{
    var noProbe = new Harness();
    PatchEconomy_Banker.BalanceKnown = true;
    noProbe.Banker._stashedCoins = 0;
    GameObject.FailCourierProbeCreation = true;
    noProbe.Step(5.2f);
    Verify(Math.Abs(CoinCourierVisuals.LastPosition.x - HomeXFree) < 0.02f,
        "an unavailable probe keeps leisure at the safe home anchor");
    Verify(CoinCourierTeleportFx.BeginCalls == 0,
        "unknown threat sensing never triggers a danger teleport");

    var badScanner = new Harness();
    PatchEconomy_Banker.BalanceKnown = true;
    badScanner.Banker._stashedCoins = 0;
    Scanner.ThrowOnScan = true;
    badScanner.Step(5.2f);
    Verify(Math.Abs(CoinCourierVisuals.LastPosition.x - HomeXFree) < 0.02f,
        "a scan fault stops the walk rather than guessing it is clear");
    Verify(CoinCourierTeleportFx.BeginCalls == 0,
        "a scan fault is not reported as a confirmed enemy");

    var lostScanner = new Harness();
    PatchEconomy_Banker.BalanceKnown = true;
    lostScanner.Banker._stashedCoins = 0;
    lostScanner.Step(5.2f);
    float awayX = CoinCourierVisuals.LastPosition.x;
    Verify(awayX > HomeXFree + 0.2f, "the scanner-loss case begins away from home");
    Scanner.ThrowOnScan = true;
    lostScanner.Tick();
    Verify(Math.Abs(CoinCourierVisuals.LastPosition.x - awayX) < 0.02f,
        "a scanner fault during the walk holds its current position");
    Verify(CoinCourierVisuals.LastPose == CoinCourierPose.Idle,
        "a scanner fault during the walk selects a standing frame");
    Verify(CoinCourierTeleportFx.BeginCalls == 0,
        "a scanner fault while away does not enter the home transfer");

    var threat = new Harness();
    PatchEconomy_Banker.BalanceKnown = true;
    threat.Banker._stashedCoins = 0;
    threat.Step(5.2f);
    Verify(CoinCourierVisuals.LastPosition.x > HomeXFree + 0.2f,
        "the real-enemy case starts during an actual stroll");
    Scanner.Enemy = new GameObject("Enemy");
    threat.Tick();
    Verify(CoinCourierVisuals.LastPose == CoinCourierPose.Jump,
        "a confirmed enemy selects the established departure animation");
    threat.Step(0.7f);
    Verify(Math.Abs(CoinCourierVisuals.LastPosition.x - HomeXFree) < 0.02f,
        "the existing bank transfer settles at the unchanged home anchor");
}

Console.WriteLine("ALL PASS — " + checks + " checks");
return 0;

internal sealed class ThrowingOwnedState : ICoinCourierCampaignState
{
    public CoinCourierAvailabilityInfo Availability => CoinCourierAvailabilityInfo.Of(CoinCourierAvailability.Ready);
    public bool Ready => true;
    public bool Owned => throw new InvalidOperationException("owned read fault");
    public CoinCourierPurse Purse => throw new InvalidOperationException("purse read fault");
    public bool TryRecordRecruitment() => throw new InvalidOperationException("record fault");
}
