using System.Reflection;
using KingdomEnhancedMod;
using UnityEngine;
internal static partial class Program
{
 static float Tick(Mover m) { SquadFollowGuard.BeforeMoverUpdate(m); return m.DynamicDestination; }
 static void RunFrameTests()
 {
  foreach(var side in new[]{Side.Left,Side.Right})
  {
   float sign=(float)side;
   Test("Live frame clamps Stand dash across "+side+" wall without SetGoal",()=>{
    var(k,a,m)=Pair(side);var wait=Follow(k,m);int calls=m.ObjectCalls;
    for(int i=0;i<100;i++){k.transform.position=new(sign*(101+i));Near(sign*95.8f,Tick(m),"every native frame sees inside target");}
    Eq(calls,m.ObjectCalls,"frame loop has no SetGoal");Check(ReferenceEquals(wait,m.LastWait),"same native Wait for entire dash");
    Eq(Mover.GoalMode.Object,m.goalMode,"real leader remains Object target");Near(5,m._goalSpeed,"speed untouched");
    Check(SquadFollowGuard.IsWallFollower(a,Managers.Inst.kingdom),"confirmed Stand remains defender outside wall");
   });
   Test("Facing flips recompute original offset instead of compounded clamp "+side,()=>{
    var(k,a,m)=Pair(side);Follow(k,m);k.transform.position=new(sign*120);
    foreach(float facing in new[]{1f,-1f,1f,-1f}){k.transform.localScale=new(facing,1,1);Near(sign*95.8f,Tick(m),"target stays inside after flip");}
    k.transform.position=new(sign*90);k.transform.localScale=new(sign,1,1);Near(sign*89,Tick(m),"deep safe position restores original relative offset");Near(-1,m._goalOffset,"no accumulated clamp drift");
   });
   Test("Native GoToWall retreat can recover initial outside save "+side,()=>{
    var(k,a,m)=Pair(side,sign*145);k._fsm.Current=Knight.State.GoToWall;k.isRetreating=true;Follow(k,m);
    Near(sign*95.8f,m.DynamicDestination,"explicit home task acquired from outside");k.transform.position=new(sign*155);Near(sign*95.8f,Tick(m),"retreat pose does not cancel guard");
   });
   Test("Confirmed defender retains lease entering GoToWall retreat "+side,()=>{
    var(k,a,m)=Pair(side);Follow(k,m);k._fsm.Current=Knight.State.GoToWall;k.isRetreating=true;k.transform.position=new(sign*140);Near(sign*95.8f,Tick(m),"return path stays home");
   });
   Test("Moving intact wall recomputes safe anchor "+side,()=>{
    var(k,a,m)=Pair(side);Follow(k,m);k.transform.position=new(sign*120);
    if(side==Side.Left)Managers.Inst.kingdom.Left=-80;else Managers.Inst.kingdom.Right=80;
    Near(sign*75.8f,Tick(m),"new intact wall applies on very next frame");
   });
   Test("Initially deep unchanged offset still registers live receipt "+side,()=>{
    var(k,a,m)=Pair(side);Follow(k,m,-8);Near(-8,m._goalOffset,"initial deep offset unchanged");
    k.transform.position=new(sign*135);Near(sign*95.8f,Tick(m),"registered even though initial clamp unnecessary");
    Managers.Inst.kingdom.isDaytime=true;Tick(m);Near(-8,m._goalOffset,"deep native baseline returned at dawn");
   });
   Test("Initial outside Assemble and Charge cannot acquire confirmation "+side,()=>{
    foreach(int state in new[]{Knight.State.Assemble,Knight.State.Charge}){var(k,a,m)=Pair(side,sign*130);k._fsm.Current=state;Follow(k,m);Near(-1,m._goalOffset,"mission offset unchanged");Tick(m);Near(-1,m._goalOffset,"no frame acquisition for expedition");}
   });
   Test("Daytime one-shot Follow2 activates at dusk without SetGoal "+side,()=>{
    Managers.Inst.kingdom.isDaytime=true;var(k,a,m)=Pair(side);var wait=Follow(k,m);Near(-1,m._goalOffset,"daytime tuple observed unchanged");
    Managers.Inst.kingdom.isDaytime=false;Near(sign*95.8f,Tick(m),"first night frame acquires wall guard");
    k.transform.position=new(sign*140);Near(sign*95.8f,Tick(m),"dusk activation protects next dash");Eq(1,m.ObjectCalls,"no synthetic follow call at dusk");Check(ReferenceEquals(wait,m.LastWait),"dusk preserves Wait");
   });
  }
  Test("Counterexample: one-time prefix alone follows leader past wall",()=>{
   var(k,a,m)=Pair();Follow(k,m);k.transform.position=new(125);Check(m.DynamicDestination>100,"old one-shot adjustment reproduces escape");
   Near(95.8f,Tick(m),"live callback repairs same native goal");Eq(1,m.ObjectCalls,"repair writes offset rather than SetGoal");
  });
  Test("Frame callback touches only the passed mover",()=>{
   var(k,a,m)=Pair();var(l,b,n)=Pair();Follow(k,m);Follow(l,n);k.transform.position=new(130);l.transform.position=new(130);
   Near(95.8f,Tick(m),"requested mover updated");Check(n.DynamicDestination>100,"other mover is not scanned or updated");Near(95.8f,Tick(n),"other native frame updates itself");
  });
  Test("Frame native new offset argument replaces old baseline while outside",()=>{
   var(k,a,m)=Pair();Follow(k,m);k.transform.position=new(130);Tick(m);Follow(k,m,-.25f,7);
   Near(95.8f,Tick(m),"new legitimate tuple retains confirmed defense");k.isCharging=true;Tick(m);Near(-.25f,m._goalOffset,"latest original offset restored");Near(7,m._goalSpeed,"latest native speed retained");Eq(2,m.ObjectCalls,"only native caller reasserts");
  });
  foreach(var entry in new (string,Action<Knight,Archer,Mover>)[]{
   ("raw new offset",(k,a,m)=>m._goalOffset=-.25f),("tiny new offset",(k,a,m)=>m._goalOffset+=.000001f),
   ("raw speed",(k,a,m)=>m._goalSpeed=8),("tiny speed",(k,a,m)=>m._goalSpeed+=.000001f),
   ("Position goal",(k,a,m)=>m.SetGoal(222,9)),("other Object goal",(k,a,m)=>m.SetGoal(new GameObject(),8,-3,Mover.OffsetMode.Formation)),
   ("different offset mode",(k,a,m)=>m._goalOffsetMode=Mover.OffsetMode.Strict),
   ("new leader",(k,a,m)=>a._knight=Pair().k),("new mover",(k,a,m)=>a._mover=Pair().m)})
   Test("Frame exact ownership protects "+entry.Item1,()=>{
    var(k,a,m)=Pair();Follow(k,m);entry.Item2(k,a,m);float offset=m._goalOffset,speed=m._goalSpeed;var goal=m._goalObject;var mode=m.goalMode;int calls=m.ObjectCalls;
    k.transform.position=new(140);Tick(m);Near(offset,m._goalOffset,"external offset retained");Near(speed,m._goalSpeed,"external speed retained");Check(ReferenceEquals(goal,m._goalObject),"external goal retained");Eq(mode,m.goalMode,"external mode retained");Eq(calls,m.ObjectCalls,"no calls after takeover");
    Managers.Inst.kingdom.isDaytime=true;SquadFollowGuard.Reconcile();Near(offset,m._goalOffset,"no deferred stale restore");
   });
  foreach(var entry in new (string,Action<Knight,Archer,Mover>)[]{
   ("Day",(k,a,m)=>Managers.Inst.kingdom.isDaytime=true),("mod off",(k,a,m)=>ModConfig.Enabled.Value=false),
   ("Charge",(k,a,m)=>k._fsm.Current=Knight.State.Charge),("charging flag",(k,a,m)=>k.isCharging=true),
   ("pending charge",(k,a,m)=>k._shouldCharge=true),("Assemble",(k,a,m)=>k._fsm.Current=Knight.State.Assemble),
   ("knight formation",(k,a,m)=>k.Formation=new()),("archer formation",(k,a,m)=>a.Formation=new()),
   ("knight embarked",(k,a,m)=>k._embarkee.IsEmbarked=true),("archer boat target",(k,a,m)=>a._embarkee.IsTargetingEmbarkable=true),
   ("knight manual",(k,a,m)=>k._beingControlled=true),("archer manual",(k,a,m)=>a.ControlRequested=true),
   ("pillar task",(k,a,m)=>k.helPuzzlePillar=new()),("tower",(k,a,m)=>a.inGuardSlot=true),
   ("archer task",(k,a,m)=>Behaviour(a).latestGoto=10),("inactive pooled archer",(k,a,m)=>a.gameObject.activeInHierarchy=false),
   ("dead knight",(k,a,m)=>k._damageable.isDead=true)})
   Test("Frame restores owned original on "+entry.Item1,()=>{
    var(k,a,m)=Pair();var wait=Follow(k,m);entry.Item2(k,a,m);Tick(m);Near(-1,m._goalOffset,"original returned before native update");Near(5,m._goalSpeed,"speed not mutated");Eq(1,m.ObjectCalls,"release uses field only");Check(ReferenceEquals(wait,m.LastWait),"original Wait retained");
   });
  Test("Frame does not clear mover pause and releases on resume",()=>{
   var(k,a,m)=Pair();Follow(k,m);float offset=m._goalOffset;m._pauseTimeout=3;Managers.Inst.kingdom.isDaytime=true;Tick(m);Near(offset,m._goalOffset,"paused receipt retained");Near(3,m._pauseTimeout,"pause unchanged");m._pauseTimeout=0;Tick(m);Near(-1,m._goalOffset,"resumed departure returns original");
  });
  Test("Frame does not write during global pause",()=>{
   var(k,a,m)=Pair();Follow(k,m);float offset=m._goalOffset;Time.timeScale=0;k.transform.position=new(140);Tick(m);Near(offset,m._goalOffset,"paused tuple unchanged");Time.timeScale=1;Near(95.8f,Tick(m),"resume recalculates dash target");
  });
  Test("Dormant day-night-day-night follows without repeated native goals",()=>{
   var(k,a,m)=Pair();Follow(k,m);Managers.Inst.kingdom.isDaytime=true;Tick(m);Near(-1,m._goalOffset,"day baseline");Managers.Inst.kingdom.isDaytime=false;Near(95.8f,Tick(m),"second night reacquires");Eq(1,m.ObjectCalls,"no follow restart across cycle");
  });
  Test("Departure clears confirmation before outside Stand",()=>{
   var(k,a,m)=Pair();Follow(k,m);k.isCharging=true;k.transform.position=new(140);Tick(m);k.isCharging=false;Tick(m);Near(-1,m._goalOffset,"arbitrary outside Stand cannot inherit old defense confirmation");k._fsm.Current=Knight.State.GoToWall;Near(95.8f,Tick(m),"explicit home return reacquires");
  });
  Test("Frame loses authority without touching owned fields",()=>{
   var(k,a,m)=Pair();Follow(k,m);float offset=m._goalOffset;NetworkBigBoss.HasWorldAuth=false;k.transform.position=new(140);Tick(m);Near(offset,m._goalOffset,"client no field writes");NetworkBigBoss.HasWorldAuth=true;Tick(m);Near(offset,m._goalOffset,"retired lease cannot revive after authority gap");
  });
  Test("World clear drops frame receipt without world actor mutations",()=>{
   var(k,a,m)=Pair();Follow(k,m);float offset=m._goalOffset;SquadFollowGuard.Clear();k.transform.position=new(140);Tick(m);Near(offset,m._goalOffset,"departing world untouched and no stale frame write");
  });
  Test("Frame invalid facing restores safely instead of invalid arithmetic",()=>{
   var(k,a,m)=Pair();Follow(k,m);k.transform.localScale=new(0,1,1);Tick(m);Near(-1,m._goalOffset,"finite native baseline recovered");
  });
  Test("Frame catches missing wall failure and returns still-owned offset",()=>{
   var(k,a,m)=Pair();Follow(k,m);Managers.Inst.kingdom.ThrowBorder=true;Tick(m);Near(-1,m._goalOffset,"failure returns valid baseline without throwing into native Update");
  });
  Test("Frame accepts null or unrelated mover with no work",()=>{
   SquadFollowGuard.BeforeMoverUpdate(null);var(k,a,m)=Pair();m.SetGoal(77,3);Near(77,Tick(m),"unleased mover unaffected");
  });
  Test("Bounded clamp diagnostics report real crossings at most eight times",()=>{
   typeof(SquadFollowGuard).GetField("ClampLogCount",BindingFlags.NonPublic|BindingFlags.Static).SetValue(null,0);
   ((HashSet<IntPtr>)typeof(SquadFollowGuard).GetField("LoggedClampLeaders",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null)).Clear();
   ((HashSet<string>)typeof(SquadFollowGuard).GetField("Logged",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null)).Clear();
   for(int i=0;i<12;i++){var(k,a,m)=Pair();Follow(k,m,-8);Eq(Math.Min(i,8),KingdomEnhancedPlugin.Instance.LogSource.Lines.Count,"deep registration never logs clamp");k.transform.position=new(130);for(int j=0;j<20;j++)Tick(m);}
   Eq(8,KingdomEnhancedPlugin.Instance.LogSource.Lines.Count,"strict session bound");Check(KingdomEnhancedPlugin.Instance.LogSource.Lines.All(s=>s.Contains("leaderX=")&&s.Contains("facing=")&&s.Contains("nativeX=")&&s.Contains("clampedX=")&&s.Contains("wall=")&&s.Contains("state=")&&s.Contains("retreat=")),"diagnostics contain observed native facts");
  });
 }
 static (Knight k,Archer a,Mover m) ChargePair(Side side=Side.Right,float station=250)
 {
  Managers.Inst.kingdom.isDaytime=true;
  var p=Pair(side,(float)side*140);p.k.isCharging=true;p.k._fsm.Current=Knight.State.Charge;
  p.k._mover.SetGoal((float)side*station,5);p.k._mover._moveSpeed=5;Follow(p.k,p.m);return p;
 }
 static void Arrive(Knight k)
 {
  k.transform.position=new(k._mover._goalPosition);k._mover.movingToGoal=false;k._mover._moveSpeed=0;
 }
 static void ResetDefenseLogs()
 {
  typeof(SquadFollowGuard).GetField("ClampLogCount",BindingFlags.NonPublic|BindingFlags.Static).SetValue(null,0);
  typeof(SquadFollowGuard).GetField("StationLogCount",BindingFlags.NonPublic|BindingFlags.Static).SetValue(null,0);
  ((HashSet<IntPtr>)typeof(SquadFollowGuard).GetField("LoggedClampLeaders",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null)).Clear();
  ((HashSet<string>)typeof(SquadFollowGuard).GetField("Logged",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null)).Clear();
  KingdomEnhancedPlugin.Instance.LogSource.Lines.Clear();
 }
 static void RunStationTests()
 {
  foreach(var side in new[]{Side.Left,Side.Right})foreach(var attack in new[]{("portal",250f),("serpent",340f)})
   Test("Shared Charge arrival anchor "+attack.Item1+" "+side,()=>{
    var(k,a,m)=ChargePair(side,attack.Item2);float sign=(float)side;var wait=m.LastWait;
    Near(sign*139,Tick(m),"outbound march follows current leader");Near(-1,m._goalOffset,"march has no station clamp");
    Arrive(k);Near(sign*(attack.Item2-1),Tick(m),"arrival captures native attack site");
    k.transform.position=new(sign*(attack.Item2+25));k.transform.localScale=new(-sign,1,1);
    Near(sign*(attack.Item2-1),Tick(m),"personal movement and flip cannot drag archers forward");
    Check(MathF.Abs(m.DynamicDestination)>100,"expedition anchor stays at attack site outside castle wall");
    Eq(1,m.ObjectCalls,"march and station preserve one original follow call");Check(ReferenceEquals(wait,m.LastWait),"same native Wait");
   });
  Test("Station requires arrival as well as native not-moving flag",()=>{
   var(k,a,m)=ChargePair();k._mover.movingToGoal=false;Tick(m);k.transform.position=new(160);Near(159,Tick(m),"far-away false flag never captures station");
   k.transform.position=new(249.9f);Tick(m);k.transform.position=new(260);Near(259,Tick(m),"outside native .0625 tolerance never captures");
  });
  Test("Station captures goal position within exact native arrival tolerance",()=>{
   var(k,a,m)=ChargePair();k._mover.movingToGoal=false;k.transform.position=new(249.94f);Near(249,Tick(m),"anchor uses goal x not incidental actor x");
   k.transform.position=new(270);Near(249,Tick(m),"captured tolerance target remains fixed");
  });
  Test("Station requires completed Position rather than near-but-moving goal",()=>{
   var(k,a,m)=ChargePair();k.transform.position=new(250);Tick(m);k.transform.position=new(270);Near(269,Tick(m),"movingToGoal=true never captures");
  });
  Test("En-route Block.Stop cannot invent a station",()=>{
   var(k,a,m)=ChargePair();k._mover.Stop();Near(139,Tick(m),"intermediate stopped leader still followed");
   k.transform.position=new(165);Near(164,Tick(m),"unconfirmed stop never fixes old goal target");
   k._mover.SetSpeed(5);k.transform.position=new(180);Near(179,Tick(m),"native resumed march remains dynamic");
  });
  Test("Unconfirmed Stop even at old goal requires new explicit Position arrival",()=>{
   var(k,a,m)=ChargePair();k.transform.position=new(250);k._mover.Stop();Tick(m);k.transform.position=new(265);Near(264,Tick(m),"Off alone is never proof of arrival");
  });
  Test("Confirmed station survives native Block.Stop and leader flip",()=>{
   var(k,a,m)=ChargePair();Arrive(k);Tick(m);k._mover.Stop();k.transform.position=new(280);k.transform.localScale=new(-1,1,1);
   Near(249,Tick(m),"confirmed native Block holds site");Near(0,k._mover._moveSpeed,"guard leaves native leader stop intact");
  });
  Test("Native SetSpeed releases confirmed station before resumed march",()=>{
   var(k,a,m)=ChargePair();Arrive(k);Tick(m);k.transform.position=new(280);Tick(m);k._mover.SetSpeed(5);Near(279,Tick(m),"native Off positive speed resumes dynamic follower");Near(-1,m._goalOffset,"original restored");
   k._mover.Stop();k.transform.position=new(290);Near(289,Tick(m),"later Off Stop cannot revive departed station");
  });
  Test("New Position goal releases old station then captures only after new arrival",()=>{
   var(k,a,m)=ChargePair();Arrive(k);Tick(m);k.transform.position=new(270);Tick(m);k._mover.SetGoal(320,5);
   Near(269,Tick(m),"new target begins march and releases old anchor");k.transform.position=new(300);Near(299,Tick(m),"follows en route to second site");
   Arrive(k);Near(319,Tick(m),"new station reached");k.transform.position=new(345);Near(319,Tick(m),"second station replaces first");
  });
  Test("Same-position native goal reissue waits for fresh arrival",()=>{
   var(k,a,m)=ChargePair();Arrive(k);Tick(m);k.transform.position=new(275);Tick(m);k._mover.SetGoal(250,5);Near(274,Tick(m),"movingToGoal reissue releases even same coordinate");
   Arrive(k);Tick(m);k.transform.position=new(280);Near(249,Tick(m),"same coordinate recaptures after arrival");
  });
  Test("Changed goal coordinate with false moving flag still requires physical arrival",()=>{
   var(k,a,m)=ChargePair();Arrive(k);Tick(m);k._mover._goalPosition=350;k.transform.position=new(280);Near(279,Tick(m),"direct target change cannot drag old station");
   Arrive(k);Tick(m);k.transform.position=new(370);Near(349,Tick(m),"changed target acquires only on arrival");
  });
  Test("Leader Object mission immediately releases Position station",()=>{
   var(k,a,m)=ChargePair();Arrive(k);Tick(m);k.transform.position=new(280);Tick(m);k._mover.SetGoal(new GameObject(),5,0,Mover.OffsetMode.Strict);
   Near(279,Tick(m),"leader Object goal is another task");
  });
  Test("Leader mover replacement cannot retain old station",()=>{
   var(k,a,m)=ChargePair();Arrive(k);Tick(m);k.transform.position=new(280);Tick(m);k._mover=new GameObject().AddComponent<Mover>();Near(279,Tick(m),"new leader mover has no arrival proof");
  });
  Test("New native follower offset respects captured station baseline and facing",()=>{
   var(k,a,m)=ChargePair();Arrive(k);Tick(m);k.transform.position=new(280);k.transform.localScale=new(-1,1,1);Tick(m);
   Follow(k,m,-3,8);Near(247,Tick(m),"new follower baseline stays relative to captured native facing");Near(8,m._goalSpeed,"new native follower speed untouched");
   k.isCharging=false;Tick(m);Near(-3,m._goalOffset,"latest native baseline returned on end");
  });
  Test("Exact native reassertion preserves original station offset",()=>{
   var(k,a,m)=ChargePair();Arrive(k);Tick(m);k.transform.position=new(280);Tick(m);Follow(k,m,m._goalOffset);Near(249,Tick(m),"exact tuple reassert keeps original station spacing");
   k.isCharging=false;Tick(m);Near(-1,m._goalOffset,"original baseline retained");
  });
  Test("Station can acquire when native follow first starts after leader arrival",()=>{
   var(k,a,m)=Pair(x:250);k.isCharging=true;k._fsm.Current=Knight.State.Charge;k._mover.SetGoal(250,5);Arrive(k);Follow(k,m);
   k.transform.position=new(280);Near(249,Tick(m),"SetGoal prefix registers already arrived site");
  });
  foreach(var entry in new (string,Action<Knight,Archer,Mover>)[]{
   ("enemy invalidated charge flag",(k,a,m)=>k.isCharging=false),("Charge state ended",(k,a,m)=>k._fsm.Current=Knight.State.Stand),
   ("new pending mission",(k,a,m)=>k._shouldCharge=true),("knight formation",(k,a,m)=>k.Formation=new()),
   ("archer formation",(k,a,m)=>a.Formation=new()),("knight boat",(k,a,m)=>k._embarkee.IsEmbarked=true),
   ("archer boat",(k,a,m)=>a._embarkee.IsTargetingEmbarkable=true),("knight manual",(k,a,m)=>k._beingControlled=true),
   ("archer manual",(k,a,m)=>a.ControlRequested=true),("follower task ended",(k,a,m)=>Behaviour(a).latestGoto=10),
   ("tower slot",(k,a,m)=>a._guardSlot=new()),("mod off",(k,a,m)=>ModConfig.Enabled.Value=false)})
   Test("Station returns owned offset on "+entry.Item1,()=>{
    var(k,a,m)=ChargePair();Arrive(k);Tick(m);k.transform.position=new(280);Tick(m);entry.Item2(k,a,m);Near(279,Tick(m),"departed task dynamically follows its native leader");Near(-1,m._goalOffset,"original owned baseline returned");Eq(1,m.ObjectCalls,"task release no SetGoal");
   });
  foreach(var entry in new (string,Action<Knight,Archer,Mover>)[]{
   ("external Position",(k,a,m)=>m.SetGoal(444,7)),("external Object",(k,a,m)=>m.SetGoal(new GameObject(),7,2,Mover.OffsetMode.Strict)),
   ("external speed",(k,a,m)=>m._goalSpeed=7),("external offset",(k,a,m)=>m._goalOffset=-7),
   ("new leader",(k,a,m)=>a._knight=Pair().k)})
   Test("Station preserves "+entry.Item1+" takeover",()=>{
    var(k,a,m)=ChargePair();Arrive(k);Tick(m);k.transform.position=new(280);Tick(m);entry.Item2(k,a,m);float offset=m._goalOffset,speed=m._goalSpeed;var goal=m._goalObject;var mode=m.goalMode;
    k.isCharging=false;Tick(m);Near(offset,m._goalOffset,"foreign offset unchanged");Near(speed,m._goalSpeed,"foreign speed unchanged");Check(ReferenceEquals(goal,m._goalObject),"foreign target unchanged");Eq(mode,m.goalMode,"foreign mode retained");
   });
  Test("Station archer pause preserves receipt then resumes fixed target",()=>{
   var(k,a,m)=ChargePair();Arrive(k);Tick(m);m._pauseTimeout=4;float offset=m._goalOffset;k.transform.position=new(280);Tick(m);Near(offset,m._goalOffset,"pause defers writes");Near(4,m._pauseTimeout,"native pause retained");m._pauseTimeout=0;Near(249,Tick(m),"resumed receipt still owns same station");
  });
  Test("Station leader native pause retains confirmed site",()=>{
   var(k,a,m)=ChargePair();Arrive(k);Tick(m);k._mover._pauseTimeout=4;k.transform.position=new(280);Near(249,Tick(m),"leader pause does not discard prior arrival");Near(4,k._mover._pauseTimeout,"leader pause untouched");
  });
  Test("Global pause does not clear station receipt",()=>{
   var(k,a,m)=ChargePair();Arrive(k);Tick(m);float offset=m._goalOffset;Time.timeScale=0;k.transform.position=new(280);Tick(m);Near(offset,m._goalOffset,"global pause defers write");Time.timeScale=1;Near(249,Tick(m),"same station resumes");
  });
  Test("Station remains active through day/night without wall conversion",()=>{
   var(k,a,m)=ChargePair();Arrive(k);Tick(m);Managers.Inst.kingdom.isDaytime=false;k.transform.position=new(280);Near(249,Tick(m),"night does not send ongoing Charge site back to wall");
  });
  Test("World clear removes station receipt without native mutation",()=>{
   var(k,a,m)=ChargePair();Arrive(k);Tick(m);k.transform.position=new(280);Tick(m);float offset=m._goalOffset;SquadFollowGuard.Clear();k.transform.position=new(300);Tick(m);Near(offset,m._goalOffset,"world clear writes nothing and cannot replay station");
  });
  Test("Night diagnostic budget ignores initial spacing and duplicate followers",()=>{
   ResetDefenseLogs();var(k,a,m)=Pair();Follow(k,m);Eq(0,KingdomEnhancedPlugin.Instance.LogSource.Lines.Count,"inside leader never consumes excursion log");
   var(other,b,n)=Pair();b._knight=k;Follow(k,n);k.transform.position=new(130);Tick(m);Tick(n);Eq(1,KingdomEnhancedPlugin.Instance.LogSource.Lines.Count,"same Knight logs once across followers");
   Check(KingdomEnhancedPlugin.Instance.LogSource.Lines[0].Contains("archerX="),"natural-night log contains follower actor position");
  });
  Test("Station diagnostics have independent strict four-message session budget",()=>{
   ResetDefenseLogs();for(int i=0;i<8;i++){var(k,a,m)=ChargePair();Arrive(k);Tick(m);k.transform.position=new(280);for(int j=0;j<10;j++)Tick(m);}
   Eq(4,KingdomEnhancedPlugin.Instance.LogSource.Lines.Count,"station acquisition logs at most four");
   Check(KingdomEnhancedPlugin.Instance.LogSource.Lines.All(s=>s.Contains("charge station")&&s.Contains("stationX=")&&s.Contains("stationFacing=")&&s.Contains("targetX=")),"logs report captured native site and follower goal");
   Managers.Inst.kingdom.isDaytime=false;var(l,b,n)=Pair();Follow(l,n);l.transform.position=new(130);Tick(n);Eq(5,KingdomEnhancedPlugin.Instance.LogSource.Lines.Count,"station logs leave night excursion budget available");
  });
 }
 static void RawFollow(Knight k,Mover m,float offset=-1,float speed=5,Mover.OffsetMode mode=Mover.OffsetMode.Formation)
 {
  var intercept=Mover.Intercept;Mover.Intercept=null;try{Follow(k,m,offset,speed,mode);}finally{Mover.Intercept=intercept;}
 }
 static int ReceiptCount()=>((System.Collections.IDictionary)typeof(SquadFollowGuard).GetField("Ledger",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null)).Count;
 static void RunObserveTests()
 {
  Test("Reconcile failure returns owned large offset before retiring receipt",()=>{
   var(k,a,m)=Pair();Follow(k,m);k.transform.position=new(150);Tick(m);Check(m._goalOffset < -50,"test owns a large moving-leader correction");
   Managers.Inst.kingdom.ThrowBorder=true;SquadFollowGuard.Reconcile();Near(-1,m._goalOffset,"wall failure restores original instead of stranding large correction");Eq(0,ReceiptCount(),"failed receipt retired after restoration");Eq(1,m.ObjectCalls,"exception recovery does not call SetGoal");
   Managers.Inst.kingdom.ThrowBorder=false;k.transform.position=new(160);Near(159,Tick(m),"no failed receipt writes on next native frame");
  });
  foreach(var side in new[]{Side.Left,Side.Right})Test("Loading Clear then roster observation restores night registration "+side,()=>{
   var(k,a,m)=Pair(side);RawFollow(k,m);var wait=m.LastWait;SquadFollowGuard.Clear();Near(-1,m._goalOffset,"Clear writes nothing into native load tuple");
   SquadFollowGuard.ObserveCurrentFollow(a);Near((float)side*95.8f,m.DynamicDestination,"roster seed adjusts current native field");
   k.transform.position=new((float)side*135);Near((float)side*95.8f,Tick(m),"seed keeps following frame behind intact wall");
   Eq(1,m.ObjectCalls,"seed never reissues native SetGoal");Eq(0,m.PositionCalls,"seed retains Object target");Check(ReferenceEquals(wait,m.LastWait),"same native Wait after loading seed");
  });
  Test("Daytime roster observation activates at night without native follow restart",()=>{
   Managers.Inst.kingdom.isDaytime=true;var(k,a,m)=Pair();RawFollow(k,m);SquadFollowGuard.Clear();SquadFollowGuard.ObserveCurrentFollow(a);Near(-1,m._goalOffset,"day seed is dormant");Eq(1,ReceiptCount(),"unchanged day tuple is registered");
   Managers.Inst.kingdom.isDaytime=false;Near(95.8f,Tick(m),"dusk activates seeded tuple");k.transform.position=new(130);Near(95.8f,Tick(m),"seed protects subsequent dash");Eq(1,m.ObjectCalls,"no replay of one-shot Follow2");
  });
  Test("Roster observation never replaces an existing active baseline",()=>{
   var(k,a,m)=Pair();Follow(k,m);k.transform.position=new(140);Tick(m);for(int i=0;i<5;i++)SquadFollowGuard.ObserveCurrentFollow(a);
   Managers.Inst.kingdom.isDaytime=true;Tick(m);Near(-1,m._goalOffset,"repeated observer cannot promote our written correction to original");Eq(1,m.ObjectCalls,"observer no native setters");
  });
  Test("Roster observation never replaces an existing dormant baseline",()=>{
   Managers.Inst.kingdom.isDaytime=true;var(k,a,m)=Pair();Follow(k,m,-8);var wait=m.LastWait;SquadFollowGuard.ObserveCurrentFollow(a);Managers.Inst.kingdom.isDaytime=false;Tick(m);k.transform.position=new(140);Tick(m);
   Managers.Inst.kingdom.isDaytime=true;Tick(m);Near(-8,m._goalOffset,"original dormant spacing survives observer");Check(ReferenceEquals(wait,m.LastWait),"dormant seed preserves native Wait");
  });
  foreach(var entry in new (string,Action<Knight,Archer,Mover>)[]{
   ("Position goal",(k,a,m)=>m.SetGoal(300,7)),("different Knight Object",(k,a,m)=>m._goalObject=Pair().k.gameObject),
   ("new Archer task",(k,a,m)=>Behaviour(a).latestGoto=10),("non Formation mode",(k,a,m)=>m._goalOffsetMode=Mover.OffsetMode.Strict),
   ("tower",(k,a,m)=>a.inGuardSlot=true),("archer formation",(k,a,m)=>a.Formation=new()),
   ("boat task",(k,a,m)=>a._embarkee.IsTargetingEmbarkable=true),("manual control",(k,a,m)=>a.ControlRequested=true),
   ("invalid offset",(k,a,m)=>m._goalOffset=float.NaN),("invalid speed",(k,a,m)=>m._goalSpeed=float.PositiveInfinity),
   ("zero speed",(k,a,m)=>m._goalSpeed=0),("wrong Archer Mover",(k,a,m)=>a._mover=Pair().m)})
   Test("Roster observer rejects "+entry.Item1,()=>{
    var(k,a,m)=Pair();RawFollow(k,m);entry.Item2(k,a,m);float offset=m._goalOffset,speed=m._goalSpeed;var wait=m.LastWait;int calls=m.ObjectCalls;SquadFollowGuard.ObserveCurrentFollow(a);
    Eq(0,ReceiptCount(),"ineligible current tuple never registered");Eq(offset,m._goalOffset,"native offset untouched");Eq(speed,m._goalSpeed,"native speed untouched");Eq(calls,m.ObjectCalls,"no native SetGoal");Check(ReferenceEquals(wait,m.LastWait),"current native Wait retained");
   });
  Test("Roster observer respects unprocessed external writer receipt",()=>{
   var(k,a,m)=Pair();Follow(k,m);m._goalOffset=-.25f;SquadFollowGuard.ObserveCurrentFollow(a);Near(-.25f,m._goalOffset,"existing invalid receipt is not silently replaced");Eq(1,ReceiptCount(),"original receipt waits for its ownership reconciliation");
  });
  foreach(var external in new[]{"offset","speed","owner"})Test("Roster observer cannot re-adopt retired external "+external+" writer",()=>{
   var(k,a,m)=Pair();Follow(k,m);if(external=="offset")m._goalOffset=-.25f;else if(external=="speed")m._goalSpeed=7;else a._knight=Pair().k;
   Tick(m);Eq(0,ReceiptCount(),"ownership loss retires receipt");float offset=m._goalOffset,speed=m._goalSpeed;
   for(int i=0;i<3;i++){SquadFollowGuard.ObserveCurrentFollow(a);SquadFollowGuard.Reconcile();}
   Near(offset,m._goalOffset,"periodic seed cannot overwrite external offset");Near(speed,m._goalSpeed,"periodic seed cannot overwrite external speed");Eq(0,ReceiptCount(),"external tuple remains unowned after seed attempts");
  });
  Test("Fresh native follow SetGoal permits registration after external takeover",()=>{
   var(k,a,m)=Pair();Follow(k,m);m._goalOffset=-.25f;Tick(m);SquadFollowGuard.ObserveCurrentFollow(a);Eq(0,ReceiptCount(),"retired takeover suppressed");
   Follow(k,m,-.5f);Eq(1,ReceiptCount(),"explicit native call authorizes fresh baseline");Near(95.8f,m.DynamicDestination,"fresh native call may clamp");Managers.Inst.kingdom.isDaytime=true;Tick(m);Near(-.5f,m._goalOffset,"fresh baseline restored");
  });
  Test("World Clear drops seed suppression without touching current tuple",()=>{
   var(k,a,m)=Pair();Follow(k,m);m._goalOffset=-.25f;Tick(m);SquadFollowGuard.Clear();Near(-.25f,m._goalOffset,"Clear remains bookkeeping-only");SquadFollowGuard.ObserveCurrentFollow(a);Eq(1,ReceiptCount(),"new-world roster can adopt current native baseline");Managers.Inst.kingdom.isDaytime=true;Tick(m);Near(-.25f,m._goalOffset,"observer only knows and restores current native baseline");
  });
  Test("Roster seed can recover an already arrived Charge attack site",()=>{
   var(k,a,m)=Pair(x:250);k.isCharging=true;k._fsm.Current=Knight.State.Charge;k._mover.SetGoal(250,5);Arrive(k);RawFollow(k,m);var wait=m.LastWait;SquadFollowGuard.Clear();SquadFollowGuard.ObserveCurrentFollow(a);
   k.transform.position=new(280);Near(249,Tick(m),"seed preserves native arrived station");Eq(1,m.ObjectCalls,"station seed no SetGoal");Check(ReferenceEquals(wait,m.LastWait),"station seed preserves Wait");
  });
  Test("Paused roster seed records native tuple without clearing pause",()=>{
   var(k,a,m)=Pair();RawFollow(k,m);m._pauseTimeout=3;SquadFollowGuard.ObserveCurrentFollow(a);Near(-1,m._goalOffset,"paused seed does not adjust field");Near(3,m._pauseTimeout,"native pause retained");Eq(1,ReceiptCount(),"paused native tuple observed");m._pauseTimeout=0;Near(95.8f,Tick(m),"resume activates observed defender");
  });
  Test("Roster observer handles null and lacks authority without registration",()=>{
   SquadFollowGuard.ObserveCurrentFollow(null);var(k,a,m)=Pair();RawFollow(k,m);NetworkBigBoss.HasWorldAuth=false;SquadFollowGuard.ObserveCurrentFollow(a);Eq(0,ReceiptCount(),"no client seed");Near(-1,m._goalOffset,"no client write");
  });
 }
 static World NewWorld()=>new GameObject().AddComponent<World>();
 static int SuppressionCount()=>((System.Collections.IDictionary)typeof(SquadFollowGuard).GetField("SuppressedObservers",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null)).Count;
 static void RunWorldTests()
 {
  Test("Real loading Follow baseline survives same-world supervisor boundary",()=>{
   var(k,a,m)=Pair();Follow(k,m);var wait=m.LastWait;k.transform.position=new(150);Tick(m);Check(m._goalOffset < -50,"actual production Follow already owns large correction");float written=m._goalOffset;
   SquadFollowGuard.BeginWorld(Managers.Inst.world); // Replaces the legacy loading supervisor Clear in the red regression.
   Near(written,m._goalOffset,"load boundary never writes any actor");Eq(1,ReceiptCount(),"current load receipt survives");
   SquadFollowGuard.ObserveCurrentFollow(a);Managers.Inst.kingdom.isDaytime=true;Tick(m);
   Near(-1,m._goalOffset,"dawn restores true original native Follow(-1), not an earlier clamp");Eq(1,m.ObjectCalls,"no new native follow call");Check(ReferenceEquals(wait,m.LastWait),"same original Wait");
  });
  Test("New World drops old actors without writes and preserves new load receipts",()=>{
   var(k,a,m)=Pair();Follow(k,m);k.transform.position=new(150);Tick(m);float oldOffset=m._goalOffset;
   Managers.Inst.world=NewWorld();var(l,b,n)=Pair();Follow(l,n);l.transform.position=new(150);Tick(n);float newOffset=n._goalOffset;
   SquadFollowGuard.BeginWorld(Managers.Inst.world);Eq(1,ReceiptCount(),"only actual current-world receipt retained");Near(oldOffset,m._goalOffset,"old actor untouched");Near(newOffset,n._goalOffset,"new actor not rewritten at boundary");
   SquadFollowGuard.ObserveCurrentFollow(a);Managers.Inst.kingdom.isDaytime=true;Tick(m);Tick(n);Near(oldOffset,m._goalOffset,"old actor cannot be reseeded or restored");Near(-1,n._goalOffset,"new actor retains its real baseline");
  });
  Test("Same World with new gameLayer drops old actors and keeps current load baseline",()=>{
   var(k,a,m)=Pair();Follow(k,m);k.transform.position=new(150);Tick(m);float oldOffset=m._goalOffset;var world=Managers.Inst.world;
   world.gameLayer=new GameObject().transform;var(l,b,n)=Pair();Follow(l,n);l.transform.position=new(150);Tick(n);SquadFollowGuard.BeginWorld(world);
   Eq(1,ReceiptCount(),"World pointer equality cannot hide a replaced gameLayer");SquadFollowGuard.ObserveCurrentFollow(a);Managers.Inst.kingdom.isDaytime=true;Tick(m);Tick(n);
   Near(oldOffset,m._goalOffset,"departing layer actor never written");Near(-1,n._goalOffset,"current layer actor original preserved");
  });
  Test("Changed World with reused layer refuses stale actor rebasing",()=>{
   var(k,a,m)=Pair();Follow(k,m);k.transform.position=new(150);Tick(m);float written=m._goalOffset;var layer=Managers.Inst.world.gameLayer;
   Managers.Inst.world=NewWorld();Managers.Inst.world.gameLayer=layer;SquadFollowGuard.BeginWorld(Managers.Inst.world);Eq(0,ReceiptCount(),"different World identity retires old receipt even on same layer");
   SquadFollowGuard.ObserveCurrentFollow(a);Managers.Inst.kingdom.isDaytime=true;Tick(m);Near(written,m._goalOffset,"same physical actor cannot promote stale written offset to new original");Eq(0,ReceiptCount(),"no automatic stale adoption");
   Follow(k,m,-3);Tick(m);Near(-3,m._goalOffset,"fresh native SetGoal explicitly provides new-world baseline");
  });
  Test("Frame retiring old World on reused layer still prevents later stale seed",()=>{
   var(k,a,m)=Pair();Follow(k,m);k.transform.position=new(150);Tick(m);float written=m._goalOffset;var layer=Managers.Inst.world.gameLayer;
   Managers.Inst.world=NewWorld();Managers.Inst.world.gameLayer=layer;Tick(m);SquadFollowGuard.BeginWorld(Managers.Inst.world);SquadFollowGuard.ObserveCurrentFollow(a);Managers.Inst.kingdom.isDaytime=true;Tick(m);
   Near(written,m._goalOffset,"ordering of old frame before boundary cannot cause stale rebasing");Eq(0,ReceiptCount(),"no old baseline is revived");
  });
  Test("World changes before BeginWorld cannot trigger old actor field writes",()=>{
   var(k,a,m)=Pair();Follow(k,m);float written=m._goalOffset;Managers.Inst.world=NewWorld();Managers.Inst.kingdom.isDaytime=true;Tick(m);Near(written,m._goalOffset,"current-world ownership rejects old actor before boundary arrives");Eq(0,ReceiptCount(),"old receipt retired without restoration");SquadFollowGuard.BeginWorld(Managers.Inst.world);Eq(0,SuppressionCount(),"old-scope refusal markers dropped");
  });
  foreach(var change in new (string,Action<Knight,Archer,Mover>)[]{
   ("inactive Archer",(k,a,m)=>a.gameObject.activeInHierarchy=false),("dead Archer",(k,a,m)=>a._damageable.isDead=true),
   ("dead Knight",(k,a,m)=>k._damageable.isDead=true),("actor outside current layer",(k,a,m)=>a.transform.parent=new GameObject().transform),
   ("external offset",(k,a,m)=>m._goalOffset=-.25f),("external Position",(k,a,m)=>m.SetGoal(300,7))})
   Test("BeginWorld rejects "+change.Item1+" without restoration",()=>{
    var(k,a,m)=Pair();Follow(k,m);change.Item2(k,a,m);float written=m._goalOffset;SquadFollowGuard.BeginWorld(Managers.Inst.world);
    Near(written,m._goalOffset,"load filtering is bookkeeping-only");Eq(0,ReceiptCount(),"not a live exact owned load receipt");SquadFollowGuard.ObserveCurrentFollow(a);Near(written,m._goalOffset,"observer cannot silently rebase rejected receipt");
   });
  Test("BeginWorld preserves current-world external writer refusal",()=>{
   var(k,a,m)=Pair();Follow(k,m);m._goalOffset=-.25f;Tick(m);Eq(1,SuppressionCount(),"external writer is refused");SquadFollowGuard.BeginWorld(Managers.Inst.world);SquadFollowGuard.ObserveCurrentFollow(a);
   Near(-.25f,m._goalOffset,"same-world boundary does not undo writer protection");Eq(0,ReceiptCount(),"current-scope refusal still applies");
  });
  Test("BeginWorld clears old-world refusal and allows legitimate new actors",()=>{
   var(k,a,m)=Pair();Follow(k,m);m._goalOffset=-.25f;Tick(m);Managers.Inst.world=NewWorld();SquadFollowGuard.BeginWorld(Managers.Inst.world);Eq(0,SuppressionCount(),"old-world marker cleared");
   var(l,b,n)=Pair();RawFollow(l,n);SquadFollowGuard.ObserveCurrentFollow(b);Near(95.8f,n.DynamicDestination,"new native actor gets normal seed");
  });
  foreach(string missing in new[]{"World","gameLayer"})Test("Registration waits for actual "+missing+" identity",()=>{
   var(k,a,m)=Pair();var world=Managers.Inst.world;RawFollow(k,m);if(missing=="World")Managers.Inst.world=null;else world.gameLayer=null;
   SquadFollowGuard.ObserveCurrentFollow(a);Follow(k,m);SquadFollowGuard.BeginWorld(world);Near(-1,m._goalOffset,"unready scope cannot write a clamp");Eq(0,ReceiptCount(),"no guessed world or layer registration");
  });
  foreach(string missing in new[]{"World","gameLayer"})Test("Temporary missing "+missing+" defers boundary and retains true original",()=>{
   var(k,a,m)=Pair();Follow(k,m);k.transform.position=new(150);Tick(m);float written=m._goalOffset;var world=Managers.Inst.world;var layer=world.gameLayer;
   if(missing=="World")Managers.Inst.world=null;else world.gameLayer=null;
   SquadFollowGuard.BeginWorld(world);Tick(m);SquadFollowGuard.Reconcile();SquadFollowGuard.ObserveCurrentFollow(a);Near(written,m._goalOffset,"unready scope performs no field writes");Eq(1,ReceiptCount(),"pending exact baseline kept for verification");
   Managers.Inst.world=world;world.gameLayer=layer;SquadFollowGuard.BeginWorld(world);Managers.Inst.kingdom.isDaytime=true;Tick(m);Near(-1,m._goalOffset,"restored same scope returns real native original");
  });
  Test("Mismatched incoming World does not discard Managers current legitimate receipt",()=>{
   var(k,a,m)=Pair();Follow(k,m);k.transform.position=new(150);Tick(m);float written=m._goalOffset;SquadFollowGuard.BeginWorld(NewWorld());Near(written,m._goalOffset,"foreign incoming boundary writes nothing");Eq(1,ReceiptCount(),"current Managers world receipt retained");Managers.Inst.kingdom.isDaytime=true;Tick(m);Near(-1,m._goalOffset,"original baseline unaffected by stale boundary callback");
  });
  Test("Null incoming boundary does not erase current-world receipt",()=>{
   var(k,a,m)=Pair();Follow(k,m);SquadFollowGuard.BeginWorld(null);Eq(1,ReceiptCount(),"unidentified callback cannot drop current data");Managers.Inst.kingdom.isDaytime=true;Tick(m);Near(-1,m._goalOffset,"baseline retained");
  });
  Test("Exact native reassertion while World unavailable retains baseline",()=>{
   var(k,a,m)=Pair();Follow(k,m);k.transform.position=new(150);Tick(m);var world=Managers.Inst.world;float written=m._goalOffset;Managers.Inst.world=null;Follow(k,m,written);SquadFollowGuard.BeginWorld(world);Near(written,m._goalOffset,"unready prefix passes native reassertion unchanged");
   Managers.Inst.world=world;SquadFollowGuard.BeginWorld(world);Managers.Inst.kingdom.isDaytime=true;Tick(m);Near(-1,m._goalOffset,"unready prefix must not remove original receipt");
  });
  Test("External writer while World unavailable still defeats old receipt on recovery",()=>{
   var(k,a,m)=Pair();Follow(k,m);var world=Managers.Inst.world;Managers.Inst.world=null;m._goalOffset=-.25f;SquadFollowGuard.BeginWorld(world);Tick(m);
   Managers.Inst.world=world;SquadFollowGuard.BeginWorld(world);SquadFollowGuard.ObserveCurrentFollow(a);Managers.Inst.kingdom.isDaytime=true;Tick(m);Near(-.25f,m._goalOffset,"deferred validation does not overwrite external ownership");Eq(0,ReceiptCount(),"invalid receipt retired");
  });
  Test("Unavailable scope followed by genuinely new World retires old receipt without writes",()=>{
   var(k,a,m)=Pair();Follow(k,m);k.transform.position=new(150);Tick(m);float written=m._goalOffset;var old=Managers.Inst.world;Managers.Inst.world=null;SquadFollowGuard.BeginWorld(old);Tick(m);
   Managers.Inst.world=NewWorld();SquadFollowGuard.BeginWorld(Managers.Inst.world);Tick(m);Near(written,m._goalOffset,"pending old-world actor never restored into new world");Eq(0,ReceiptCount(),"deferred record validated and dropped on real switch");
  });
  Test("Current-layer nested hierarchy counts as current live actors",()=>{
   var(k,a,m)=Pair();var branch=new GameObject().transform;branch.parent=Managers.Inst.world.gameLayer;k.transform.parent=branch;a.transform.parent=branch;Follow(k,m);k.transform.position=new(150);Tick(m);SquadFollowGuard.BeginWorld(Managers.Inst.world);Managers.Inst.kingdom.isDaytime=true;Tick(m);Near(-1,m._goalOffset,"IsChildOf handles current nested actors");
  });
  Test("Dormant current-world receipt survives BeginWorld and later night activation",()=>{
   Managers.Inst.kingdom.isDaytime=true;var(k,a,m)=Pair();Follow(k,m,-8);SquadFollowGuard.BeginWorld(Managers.Inst.world);Managers.Inst.kingdom.isDaytime=false;Tick(m);k.transform.position=new(150);Tick(m);Managers.Inst.kingdom.isDaytime=true;Tick(m);Near(-8,m._goalOffset,"dormant real baseline preserved across load and night");
  });
 }
}
