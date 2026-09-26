using KingdomEnhancedMod;
int checks=0;
void Check(bool value,string name){if(!value)throw new Exception(name);checks++;}
var pay=new MusketeerShopPayment();
Check(!pay.Consume(1,true,4),"unarmed payment rejected");
pay.Arm(1);Check(!pay.Consume(2,true,4),"another payer rejected");
Check(!pay.Consume(1,false,4),"partial transaction rejected");
Check(!pay.Consume(1,true,3),"underpayment rejected");
Check(!pay.Consume(1,true,8),"hero price cannot enter firearm transaction");
Check(pay.Consume(1,true,4),"four paid coins accepted");
Check(!pay.Consume(1,true,4)&&pay.AlreadySettled(1),"duplicate completion cannot spawn another gun");
pay.Clear();Check(!pay.AlreadySettled(1)&&!pay.IsArmed(1),"cancel clears receipt");
Check(MusketeerShopRules.FirstFreeSlot(Array.Empty<int>())==0,"first rack position");
Check(MusketeerShopRules.FirstFreeSlot(new[]{0,1,2})==-1,"full rack");
for(int gap=0;gap<3;gap++)
{
    var remaining=Enumerable.Range(0,3).Where(i=>i!=gap).ToArray();
    Check(MusketeerShopRules.FirstFreeSlot(remaining)==gap,"replenish actual missing slot "+gap);
}
Check(MusketeerShopRules.FirstFreeSlot(new[]{3})==-1,"unreadable slot prevents charge");
Check(MusketeerShopRules.FirstFreeSlot(new[]{1,1})==-1,"duplicate slot rejects purchase");
var probe=new HeroShopRetention.Probe{FeatureEnabled=true,Offline=true,HasShop=true,SameScene=true,SameWorld=true,SceneAlive=true,HeaderOk=true,Menu=true};
Check(HeroShopRetention.Decide(probe)==HeroShopRetention.Outcome.Keep&&!HeroShopRetention.CanServe(probe),"pause retains shop but prevents payment");
probe.Menu=false;probe.Playing=true;Check(HeroShopRetention.CanServe(probe),"playing permits eligible native transaction");
probe.SameWorld=false;Check(!HeroShopRetention.CanServe(probe),"world change blocks payment");
// 2026-09-27 rack-gate evidence: the clause assembler and its cadence are pure logic, so the
// exact contract the shop logs (and when) is pinned without the game. The healthy full rack is
// the one explained block (guns visibly waiting for pickup) and must not consume the budget.
var healthyFull=new MusketeerShopRules.RackGateProbe{Shop=true,Layer=true,TimescaleOk=true,Retention=true,State=true,Ready=true,Baseline=true,Epoch=true,Context=true,LayoutReady=true,RackItems=MusketeerShopRules.RackCapacity};
Check(!MusketeerShopRules.ShouldDumpRackGate(in healthyFull),"the explained healthy full rack is not dumped");
Check(MusketeerShopRules.DescribeRackGate(in healthyFull).Contains("failed=none"),"a healthy probe has no failed clause");
// P1-A：面板自动暂停是唯一根因时不烧取证预算（rack 读失败是暂停的下游）；暂停中的真实异常仍出证。
var paused=healthyFull;paused.TimescaleOk=false;paused.RackItems=0;
Check(!MusketeerShopRules.ShouldDumpRackGate(in paused),"a pure panel-pause probe is not dumped (budget protected)");
var pausedGhost=paused;pausedGhost.Ghost=true;
Check(MusketeerShopRules.ShouldDumpRackGate(in pausedGhost),"a paused probe with a real anomaly is still dumped");
var incomplete=healthyFull;incomplete.RackItems=MusketeerShopRules.RackCapacity-1;
Check(MusketeerShopRules.ShouldDumpRackGate(in incomplete),"a blocked read of an incomplete rack is dumped");
var residual=healthyFull;residual.ResidualSlots=1;
Check(MusketeerShopRules.ShouldDumpRackGate(in residual),"a residual rack claim is dumped");
var ghost=healthyFull;ghost.Ghost=true;
Check(MusketeerShopRules.ShouldDumpRackGate(in ghost),"a ghost rack item is dumped");
var pending=healthyFull;pending.StockRestores=1;pending.Attempts=29;
Check(MusketeerShopRules.ShouldDumpRackGate(in pending),"a pending restore is dumped with its attempts");
var stateless=new MusketeerShopRules.RackGateProbe{Shop=true,Layer=true,TimescaleOk=true,Retention=true};
Check(MusketeerShopRules.ShouldDumpRackGate(in stateless),"a missing identity state is dumped");
var dirty=new MusketeerShopRules.RackGateProbe{Shop=true,Layer=true,TimescaleOk=true,Retention=true,State=true,Ready=true,ReadOnly=true,Unresolved=true,Baseline=false,Epoch=false,Context=false,LayoutReady=false,StockRestores=2,Attempts=29,ResidualSlots=1,RackItems=0,Ghost=true};
var line=MusketeerShopRules.DescribeRackGate(in dirty);
Check(line.Contains("Unresolved=True")&&line.Contains("ReadOnly=True")&&line.Contains("HasBaseline=False")&&line.Contains("Epoch=False"),"every RackContextReady clause is named | "+line);
Check(line.Contains("stockRestores=2")&&line.Contains("attempts=29"),"the restore responsibility is named | "+line);
Check(line.Contains("residualSlots=1")&&line.Contains("ghost=True"),"the residual-slot probe and ghost marker are named | "+line);
Check(line.Contains("failed=ReadOnly,Unresolved,HasBaseline,Epoch,context,layoutReady,stockRestores,residualSlots,ghost,rackIncomplete"),"the failed list names the failing clauses in order | "+line);
var cadence=new MusketeerRackDiagPolicy();
Check(cadence.TryBegin(100f),"the first rack-gate line is allowed");
Check(!cadence.TryBegin(100f)&&!cadence.TryBegin(159.5f),"a second line inside the 60s window is throttled");
Check(cadence.TryBegin(160f),"the next 60s window reopens");
for(int i=0;i<10;i++)Check(cadence.TryBegin(1000f+i*100f),"session budget allows line "+(i+3));
Check(!cadence.TryBegin(99999f)&&cadence.Lines==MusketeerRackDiagPolicy.SessionBudget,"the session budget is hard");
Console.WriteLine($"PASS {checks} shop payment/rack/retention assertions");
