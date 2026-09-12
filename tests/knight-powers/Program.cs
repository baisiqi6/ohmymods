using System.Reflection;
using KingdomEnhancedMod;
using UnityEngine;
using MN=KingdomEnhancedMod.PatchRoles_MedievalNorsePowers;
using DL=KingdomEnhancedMod.PatchRoles_DeadlandsPowers;

static partial class Program {
 static int passed,failed;
 static void Eq<T>(T expected,T actual,string label) {if(!EqualityComparer<T>.Default.Equals(expected,actual))throw new Exception($"{label}: expected {expected}, got {actual}");}
 static void Test(string name,Action action) {ModConfig.Enabled.Value=true;NetworkBigBoss.HasWorldAuth=true;CampaignSaveData.current=null;CampaignSaveData.Status=Statue.DeityStatus.Inactive;Time.time=0;Time.deltaTime=.02f;KingdomEnhancedPlugin.Logger.Errors.Clear();try{action();Eq(0,KingdomEnhancedPlugin.Logger.Errors.Count,"production errors");passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e.GetBaseException().Message);}}
 static object Hook(Type t,string name,params object[] args)=>t.GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);
 static bool SlashStep(Knight._Slash_d__168 iterator) {
  object[] prefix={true,iterator,null};
  bool run=(bool)Hook(typeof(Knight_SlashCoroutine_Deadlands_Patch),"Prefix",prefix);
  bool result=(bool)prefix[0]; var lease=(DL.SlashLease)prefix[2]; Exception error=null;
  try {
   if(run) result=iterator.NativeMoveNext();
   object[] postfix={result,iterator,lease};
   Hook(typeof(Knight_SlashCoroutine_Deadlands_Patch),"Postfix",postfix);result=(bool)postfix[0];
  } catch(Exception e){error=e.GetBaseException();}
  error=(Exception)Hook(typeof(Knight_SlashCoroutine_Deadlands_Patch),"Finalizer",error,lease);
  if(error!=null)throw error;
  return result;
 }
 static Knight NewKnight(int style) {var go=new GameObject();var k=go.AddComponent<Knight>();k.Style=style;k._originalWallet=go.AddComponent<Wallet>();k._originalWallet.TotalCapacity=5;k._originalWallet.payTaxesAbove=3;k._originalWallet.NativeLoadCoins(2);k._wallet=k._originalWallet;k._mover=go.AddComponent<Mover>();k._animator=go.AddComponent<Animator>();k._enemyScanner=go.AddComponent<Scanner>();k._enemyScanner.range=10;MN.OnKnightAwake(k);return k;}
 static Wallet PlayerWallet(){var w=new GameObject("player").AddComponent<Wallet>();w.TotalCapacity=40;w.payTaxesAbove=33;w.NativeLoadCoins(29);return w;}
 static Archer Follower(Knight k){var a=new GameObject().AddComponent<Archer>();a._knight=k;a._mover=a.gameObject.AddComponent<Mover>();a._animator=a.gameObject.AddComponent<Animator>();return a;}
 static bool ShouldSlash(Knight k){bool result=false;MN.WidenShouldSlash(k,ref result);return result;}
 static Damageable Enemy(Knight k,float x){var d=new GameObject().AddComponent<Damageable>();d.transform.position=new Vector3(x,0,0);k._enemyScanner.Closest=d.gameObject;return d;}
 static void Main(){
  Test("Norse possession own-wallet isolation, idempotence, and load",()=>{var k=NewKnight(4);var p=PlayerWallet();k._wallet=p;MN.Reconcile(k);Eq(10,k._originalWallet.TotalCapacity,"capacity");Eq(6,k._originalWallet.payTaxesAbove,"tax");int cw=k._originalWallet.CapacityWrites,tw=k._originalWallet.TaxWrites;for(int i=0;i<100;i++)MN.Reconcile(k);Eq(cw,k._originalWallet.CapacityWrites,"repeat cap writes");Eq(tw,k._originalWallet.TaxWrites,"repeat tax writes");k._originalWallet.NativeLoadCoins(14);MN.OnWalletDataApplied(k._originalWallet);MN.OnWalletDataApplied(p);Eq(14,k._originalWallet.TotalCapacity,"load protects overfill");Eq(14,k._originalWallet.Coins,"load coins unchanged");Eq(0,k._originalWallet.CoinWrites,"own coin writes");Eq(40,p.TotalCapacity,"player cap");Eq(33,p.payTaxesAbove,"player tax");Eq(29,p.Coins,"player coins");Eq(1,p.CapacityWrites,"player cap writes");Eq(1,p.TaxWrites,"player tax writes");Eq(0,p.CoinWrites,"player coin writes");});
  Test("Norse statue targets, config disable, pool reuse, style loss",()=>{var k=NewKnight(4);CampaignSaveData.current=new();CampaignSaveData.Status=Statue.DeityStatus.Activated;MN.Reconcile(k);Eq(14,k._originalWallet.TotalCapacity,"statue cap");Eq(14,k._originalWallet.payTaxesAbove,"statue tax");Eq(7,k._statueBuffMaxCoins,"statue source intact");ModConfig.Enabled.Value=false;MN.Reconcile(k);Eq(7,k._originalWallet.TotalCapacity,"disabled statue cap");ModConfig.Enabled.Value=true;MN.Reconcile(k);MN.OnKnightDisabled(k);Eq(5,k._originalWallet.TotalCapacity,"pool baseline cap");Eq(3,k._originalWallet.payTaxesAbove,"pool baseline tax");CampaignSaveData.Status=Statue.DeityStatus.Inactive;for(int i=0;i<12;i++){MN.Reconcile(k);Eq(10,k._originalWallet.TotalCapacity,"reuse no stack");MN.OnKnightDisabled(k);}MN.Reconcile(k);k.Qualified=false;MN.Reconcile(k);Eq(5,k._originalWallet.TotalCapacity,"lost qualification cap");Eq(3,k._originalWallet.payTaxesAbove,"lost qualification tax");Eq(0,k._originalWallet.CoinWrites,"no coin writes");});
  Test("Norse rejects foreign GO original wallet",()=>{var k=NewKnight(4);var p=PlayerWallet();k._originalWallet=p;MN.Reconcile(k);MN.OnKnightDisabled(k);MN.OnWalletDataApplied(p);Eq(40,p.TotalCapacity,"foreign cap");Eq(33,p.payTaxesAbove,"foreign tax");});
  Test("Norse client deterministic statue and saturation",()=>{var k=NewKnight(4);NetworkBigBoss.HasWorldAuth=false;CampaignSaveData.current=new();CampaignSaveData.Status=Statue.DeityStatus.Activated;k.statueBuffActive=false;MN.Reconcile(k);Eq(14,k._originalWallet.TotalCapacity,"client statue");k._statueBuffMaxCoins=int.MaxValue;MN.Reconcile(k);Eq(int.MaxValue,k._originalWallet.TotalCapacity,"saturating cap");Eq(int.MaxValue,k._originalWallet.payTaxesAbove,"saturating taxes");});
  Test("Medieval forward hitbox geometry and repeated lifecycle",()=>{var k=NewKnight(0);k._awarenessRange=1;k._enemyScanner.range=1;MN.Reconcile(k);Eq(3f,k._slashRange,"range");Eq(-.5f,k._hitBox.xMin,"back edge");Eq(3f,k._hitBox.xMax,"front edge");Eq(-1f,k._hitBox.yMin,"bottom");Eq(1f,k._hitBox.yMax,"top");Eq(3f,k._enemyScanner.range,"scanner coverage");for(int i=0;i<50;i++)MN.Reconcile(k);Eq(3f,k._slashRange,"no stack");ModConfig.Enabled.Value=false;MN.Reconcile(k);Eq(2f,k._slashRange,"disabled range");Eq(2f,k._hitBox.xMax,"disabled front");Eq(1f,k._enemyScanner.range,"scanner restored");ModConfig.Enabled.Value=true;MN.Reconcile(k);MN.OnKnightDisabled(k);Eq(2f,k._slashRange,"pool baseline");MN.Reconcile(k);k.Style=4;MN.Reconcile(k);Eq(2f,k._slashRange,"style loss");Eq(-.5f,k._hitBox.xMin,"rear unchanged throughout");});
  Test("Medieval qualification and native harm/cooldown/pusher/authority gates",()=>{var k=NewKnight(0);MN.Reconcile(k);var d=Enemy(k,2.9f);Eq(true,ShouldSlash(k),"extended attack");Eq(d,k._enemy,"target set");d.transform.position=new(3,0,0);Eq(false,ShouldSlash(k),"boundary excluded");d.transform.position=new(2.9f,0,0);k._harmless=true;Eq(false,ShouldSlash(k),"harmless");k._harmless=false;k._cooldown=.1f;Eq(false,ShouldSlash(k),"cooldown");k._cooldown=0;k._pusher=new(){enabled=true};Eq(false,ShouldSlash(k),"pushing");k._pusher.enabled=false;d.Vulnerable=false;Eq(false,ShouldSlash(k),"immune target");d.Vulnerable=true;NetworkBigBoss.HasWorldAuth=false;Eq(false,ShouldSlash(k),"client");NetworkBigBoss.HasWorldAuth=true;k.Qualified=false;Eq(false,ShouldSlash(k),"unqualified");k.Qualified=true;k.Style=1;Eq(false,ShouldSlash(k),"wrong style");k.Style=0;ModConfig.Enabled.Value=false;Eq(false,ShouldSlash(k),"config off");bool native=true;MN.WidenShouldSlash(k,ref native);Eq(true,native,"native true preserved");});
  Test("Deadlands movement per-call restore, no stacking, config change, callback",()=>{var k=NewKnight(1);DL.Register(k);var m=k._mover;for(int i=0;i<100;i++){object[] args={default(DL.MoverBoost),m};Hook(typeof(Mover_Update_DeadlandsSpeed_Patch),"Prefix",args);Eq(3f,m._goalSpeed,"temporary goal");Eq(6f,m._moveSpeed,"temporary speed");ModConfig.Enabled.Value=false;Hook(typeof(Mover_Update_DeadlandsSpeed_Patch),"Finalizer",args[0],new Exception("simulated native failure"),m);Eq(2f,m._goalSpeed,"goal restored");Eq(4f,m._moveSpeed,"speed restored");ModConfig.Enabled.Value=true;}object[] next={default(DL.MoverBoost),m};Hook(typeof(Mover_Update_DeadlandsSpeed_Patch),"Prefix",next);m._goalSpeed=11;m._moveSpeed=12;Hook(typeof(Mover_Update_DeadlandsSpeed_Patch),"Finalizer",next[0],null,m);Eq(11f,m._goalSpeed,"callback goal preserved");Eq(12f,m._moveSpeed,"callback speed preserved");DL.OnKnightDisabled(k);Eq(false,DL.ByMover.ContainsKey(m.Pointer),"pool registry cleanup");});
  Test("Deadlands shoot intervals scope, exception/config restore, follower isolation",()=>{var k=NewKnight(1);var a=Follower(k);var e=new Archer._Shoot_d__225{__4__this=a};for(int i=0;i<30;i++){object[] args={default(DL.ShootBoost),e};Hook(typeof(Archer_ShootCoroutine_DeadlandsInterval_Patch),"Prefix",args);Eq(2f,a.shootPrepTime,"temporary prep");Eq(1f,a._shootIntervalRange.x,"temporary minimum");Eq(3f,a._shootIntervalRange.y,"temporary maximum");Eq(2f,a._shootIntervalRangeFormation.x,"temporary formation min");Eq(4f,a._shootIntervalRangeFormation.y,"temporary formation max");ModConfig.Enabled.Value=false;k.Qualified=false;Hook(typeof(Archer_ShootCoroutine_DeadlandsInterval_Patch),"Finalizer",args[0],new Exception("simulated native failure"),e);Eq(4f,a.shootPrepTime,"prep restored");Eq(2f,a._shootIntervalRange.x,"minimum restored");Eq(6f,a._shootIntervalRange.y,"maximum restored");ModConfig.Enabled.Value=true;k.Qualified=true;}a._knight=null;object[] independent={default(DL.ShootBoost),e};Hook(typeof(Archer_ShootCoroutine_DeadlandsInterval_Patch),"Prefix",independent);Eq(4f,a.shootPrepTime,"independent archer unchanged");Eq(false,((DL.ShootBoost)independent[0]).Applied,"independent excluded");});
  Test("Deadlands animation repeated triggers do not stack; disable restores",()=>{var k=NewKnight(1);var owner=new DL.UnitRef{Knight=k,UnitPtr=k.Pointer};k._animator.speed=1.25f;for(int i=0;i<20;i++)DL.HandleAnimTrigger(k._animator,Animator.StringToHash("Slash"),owner);Eq(2.5f,k._animator.speed,"bounded boost");var o=k.gameObject.GetComponent<DeadlandsAnimObserver>();ModConfig.Enabled.Value=false;DL.ObserverTick(o);Eq(1.25f,k._animator.speed,"config restore");ModConfig.Enabled.Value=true;DL.HandleAnimTrigger(k._animator,Animator.StringToHash("Slash"),owner);k._animator.speed=0;DL.OnKnightDisabled(k);Eq(0f,k._animator.speed,"external freeze preserved");});
  Test("Slash overlap suppressed; exception preserves error and retires rooted owner",()=>{
   var k=NewKnight(1);var a=new Knight._Slash_d__168{__4__this=k};var b=new Knight._Slash_d__168{__4__this=k};
   Eq(true,SlashStep(a),"first admitted");Eq(a,DL.ActiveSlash[k.Pointer].Iterator,"actual wrapper rooted");
   Eq(false,SlashStep(b),"overlap rejected");Eq(0,b.NativeCalls,"overlap never enters native");Eq(-1,b.__1__state,"overlap terminated");
   var error=new Exception("native fault");a.NativeBody=()=>throw error;
   try{SlashStep(a);throw new Exception("expected native failure");}catch(Exception caught){Eq(error,caught,"exception preserved");}
   Eq(-1,a.__1__state,"faulted owner terminated");Eq(false,DL.ActiveSlash.ContainsKey(k.Pointer),"guard released");
   Eq(false,SlashStep(a),"late faulted owner rejected");Eq(2,a.NativeCalls,"faulted owner not reentered");
  });
  Test("Slash guard heartbeat survives style config and authority loss until normal completion",()=>{
   var k=NewKnight(1);var a=new Knight._Slash_d__168{__4__this=k};Eq(true,SlashStep(a),"owner starts");
   k.Style=4;ModConfig.Enabled.Value=false;NetworkBigBoss.HasWorldAuth=false;
   Time.time=1.5f;Eq(true,SlashStep(a),"owner continues without gameplay eligibility");Eq(1.5f,DL.ActiveSlash[k.Pointer].Heartbeat,"heartbeat despite gates");
   Time.time=3f;var b=new Knight._Slash_d__168{__4__this=k};Eq(false,SlashStep(b),"fresh heartbeat blocks overlap");
   Eq(true,DL.ActiveSlash.ContainsKey(k.Pointer),"unrelated completion retains owner");
   a.NativeResult=false;Eq(false,SlashStep(a),"normal owner completion");Eq(false,DL.ActiveSlash.ContainsKey(k.Pointer),"normal completion removes root");
  });
  Test("Slash expired A is permanently canceled after B takeover and completion",()=>{
   var k=NewKnight(1);var a=new Knight._Slash_d__168{__4__this=k};var b=new Knight._Slash_d__168{__4__this=k};
   SlashStep(a);Eq(1,a.__1__state,"A actually suspended");var old=DL.ActiveSlash[k.Pointer];
   b.NativeBody=()=>Eq(-1,a.__1__state,"A already terminal before B native body");
   Time.time=2f;Eq(true,SlashStep(b),"B takes expired lease");Eq(true,old.Retired,"A lease retired");Eq(-1,a.__1__state,"A terminated before B runs");
   Eq(b,DL.ActiveSlash[k.Pointer].Iterator,"B rooted");b.NativeResult=false;Eq(false,SlashStep(b),"B completes");
   Eq(false,DL.ActiveSlash.ContainsKey(k.Pointer),"B root removed");Eq(false,SlashStep(a),"late A rejected with fields untouched");Eq(1,a.NativeCalls,"A native never reenters");
  });
  Test("Slash expired A cannot resume when config-off B never registers",()=>{
   var k=NewKnight(1);var a=new Knight._Slash_d__168{__4__this=k};SlashStep(a);
   Time.time=2f;ModConfig.Enabled.Value=false;var b=new Knight._Slash_d__168{__4__this=k};
   Eq(true,SlashStep(b),"vanilla B starts");Eq(false,DL.ActiveSlash.ContainsKey(k.Pointer),"B not registered");
   Eq(-1,a.__1__state,"A retired without replacement lease");b.NativeResult=false;SlashStep(b);
   ModConfig.Enabled.Value=true;Eq(false,SlashStep(a),"late A rejected after config returns");Eq(1,a.NativeCalls,"no A native reentry");
  });
  Test("Slash expired A retires for style or authority loss without registering B",()=>{
   for(int mode=0;mode<2;mode++){
    NetworkBigBoss.HasWorldAuth=true;Time.time=0;var k=NewKnight(1);var a=new Knight._Slash_d__168{__4__this=k};SlashStep(a);
    Time.time=2f;if(mode==0)k.Style=4;else NetworkBigBoss.HasWorldAuth=false;
    var b=new Knight._Slash_d__168{__4__this=k};Eq(true,SlashStep(b),"B continues vanilla");Eq(false,DL.ActiveSlash.ContainsKey(k.Pointer),"B has no guard");
    Eq(false,SlashStep(a),"retired A remains canceled");Eq(1,a.NativeCalls,"A never native reenters");
   }
  });
  Test("Slash OnDisable pool reuse cannot revive old iterator",()=>{
   var k=NewKnight(1);var a=new Knight._Slash_d__168{__4__this=k};SlashStep(a);var old=DL.ActiveSlash[k.Pointer];
   DL.OnKnightDisabled(k);Eq(-1,a.__1__state,"disabled owner terminal");Eq(true,old.Retired,"old lease retired");Eq(false,DL.ActiveSlash.ContainsKey(k.Pointer),"root removed");
   var b=new Knight._Slash_d__168{__4__this=k};Eq(true,SlashStep(b),"same knight reused");
   Eq(false,SlashStep(a),"A cannot revive during B");Eq(b,DL.ActiveSlash[k.Pointer].Iterator,"A cannot erase B");
   b.NativeResult=false;SlashStep(b);Eq(false,SlashStep(a),"A cannot revive after B");Eq(1,a.NativeCalls,"no old native entry");
  });
  Test("Slash reentrant native disable then state1 write is forced terminal in postfix",()=>{
   var k=NewKnight(1);var a=new Knight._Slash_d__168{__4__this=k};
   a.NativeBody=()=>DL.OnKnightDisabled(k);
   Eq(false,SlashStep(a),"postfix overrides native true");Eq(-1,a.__1__state,"postfix overrides native state1");
   Eq(false,DL.ActiveSlash.ContainsKey(k.Pointer),"disabled root removed");Eq(false,SlashStep(a),"late A rejected");Eq(1,a.NativeCalls,"no second native entry");
  });
  Test("Slash reentrant disable and B registration cannot be undone by old A postfix",()=>{
   var k=NewKnight(1);var a=new Knight._Slash_d__168{__4__this=k};var b=new Knight._Slash_d__168{__4__this=k};
   a.NativeBody=()=>{DL.OnKnightDisabled(k);Eq(true,SlashStep(b),"B starts inside old body");};
   Eq(false,SlashStep(a),"retired A cannot return true");Eq(-1,a.__1__state,"A terminal");Eq(b,DL.ActiveSlash[k.Pointer].Iterator,"B owner survives old postfix");
   b.NativeResult=false;SlashStep(b);Eq(false,SlashStep(a),"A remains canceled after B");
  });
  Test("Slash reentrant retirement and old exception cannot erase new owner",()=>{
   var k=NewKnight(1);var a=new Knight._Slash_d__168{__4__this=k};var b=new Knight._Slash_d__168{__4__this=k};var error=new Exception("old A fails");
   a.NativeBody=()=>{DL.OnKnightDisabled(k);SlashStep(b);throw error;};
   try{SlashStep(a);throw new Exception("expected failure");}catch(Exception caught){Eq(error,caught,"original error");}
   Eq(b,DL.ActiveSlash[k.Pointer].Iterator,"new B retained");Eq(-1,a.__1__state,"A terminal");b.NativeResult=false;SlashStep(b);
  });
  Test("Slash paused scaled clock does not expire owner",()=>{
   var k=NewKnight(1);var a=new Knight._Slash_d__168{__4__this=k};Time.time=12;SlashStep(a);Time.deltaTime=0;
   for(int i=0;i<50;i++){var b=new Knight._Slash_d__168{__4__this=k};Eq(false,SlashStep(b),"paused overlap rejected");}
   Eq(a,DL.ActiveSlash[k.Pointer].Iterator,"same paused root");Eq(12f,DL.ActiveSlash[k.Pointer].Heartbeat,"scaled clock unchanged");DL.OnKnightDisabled(k);
  });
  Test("Slash heartbeat prevents timeout even after lifetime exceeds lease",()=>{
   var k=NewKnight(1);var a=new Knight._Slash_d__168{__4__this=k};SlashStep(a);
   for(int i=1;i<=5;i++){Time.time=i*1.5f;SlashStep(a);var b=new Knight._Slash_d__168{__4__this=k};Eq(false,SlashStep(b),"live owner blocks competing start");}
   Eq(a,DL.ActiveSlash[k.Pointer].Iterator,"live owner retained");DL.OnKnightDisabled(k);
  });
  Test("Slash non-deadlands iterator keeps vanilla execution and is not rooted",()=>{
   var k=NewKnight(4);var a=new Knight._Slash_d__168{__4__this=k};a.NativeBody=()=>DL.OnKnightDisabled(k);
   Eq(true,SlashStep(a),"unregistered native true preserved");Eq(1,a.__1__state,"unregistered state1 preserved");Eq(false,DL.ActiveSlash.ContainsKey(k.Pointer),"no non-deadlands root");
   Eq(true,SlashStep(a),"unregistered can resume");a.NativeResult=false;Eq(false,SlashStep(a),"vanilla completion");Eq(3,a.NativeCalls,"vanilla bodies executed");
  });
  Test("Slash same owner may renew after silence if no new iterator took over",()=>{
   var k=NewKnight(1);var a=new Knight._Slash_d__168{__4__this=k};SlashStep(a);var lease=DL.ActiveSlash[k.Pointer];
   Time.time=10;Eq(true,SlashStep(a),"owner resumes before any takeover");Eq(lease,DL.ActiveSlash[k.Pointer],"same lease");Eq(10f,lease.Heartbeat,"renewed heartbeat");
   var b=new Knight._Slash_d__168{__4__this=k};Eq(false,SlashStep(b),"fresh renewal excludes B");DL.OnKnightDisabled(k);
  });
  Test("Deadlands Haglet cadence gate and pointer replacement",()=>{var a=Follower(NewKnight(1));a.shoot=new Coatsink.Common.Haglet{started=true};a._cooldown=5;Hook(typeof(Archer_Update_DeadlandsCadence_Patch),"Prefix",a);Eq(5f,a._cooldown,"running shoot sentinel preserved");a.shoot=new Coatsink.Common.Haglet{started=false};Hook(typeof(Archer_Update_DeadlandsCadence_Patch),"Prefix",a);Eq(4.98f,a._cooldown,"new stopped shoot permits decay");a.Controlled=true;Hook(typeof(Archer_Update_DeadlandsCadence_Patch),"Prefix",a);Eq(4.98f,a._cooldown,"player controlled excluded");a.Controlled=false;a._knight.Style=4;Hook(typeof(Archer_Update_DeadlandsCadence_Patch),"Prefix",a);Eq(4.98f,a._cooldown,"style loss excluded");DL.OnArcherDisabled(a);Eq(false,DL.ByMover.ContainsKey(a._mover.Pointer),"follower registry removed");});
  Test("Medieval first visual trigger visible and fade completes",()=>{var k=NewKnight(0);MN.Reconcile(k);MN.OnKnightSlashAnim(k);var states=(System.Collections.IDictionary)typeof(MN).GetField("States",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);var state=(MN.KnightPowerState)states[k.gameObject.Id];Eq(true,state.ArcBehaviour!=null,"arc created");Eq(true,state.ArcBehaviour.Renderer.enabled,"first slash visible");Eq(0f,state.ArcBehaviour.Age,"first slash starts animation");Eq(3f,state.ArcBehaviour.Renderer.positions[^1].x,"visual matches hitbox front");Eq(1f,k.transform.localScale.x,"unit x scale untouched");Time.deltaTime=.21f;MN.TickArc(state.ArcBehaviour);Eq(false,state.ArcBehaviour.Renderer.enabled,"fade finishes");});
  Test("Norse surplus survives style loss but capacity cannot leak across pool reuse",()=>{var k=NewKnight(4);MN.Reconcile(k);k._originalWallet.NativeLoadCoins(10);MN.OnKnightAwake(k);k.Style=0;MN.Reconcile(k);Eq(10,k._originalWallet.TotalCapacity,"live surplus retained");MN.OnKnightDisabled(k);Eq(5,k._originalWallet.TotalCapacity,"serialized pool capacity");Eq(10,k._originalWallet.Coins,"mod does not remove surplus");k._originalWallet.NativeLoadCoins(0);MN.Reconcile(k);Eq(5,k._originalWallet.TotalCapacity,"new medieval life no leftover capacity");Eq(0,k._originalWallet.CoinWrites,"no currency mutation");});
  Test("Deadlands vanished movement target cannot retain temporary scale",()=>{var k=NewKnight(1);DL.Register(k);var m=k._mover;m.goalMode=Mover.GoalMode.Object;object[] args={default(DL.MoverBoost),m};Hook(typeof(Mover_Update_DeadlandsSpeed_Patch),"Prefix",args);m.goalMode=Mover.GoalMode.Off;Hook(typeof(Mover_Update_DeadlandsSpeed_Patch),"Finalizer",args[0],null,m);Eq(2f,m._goalSpeed,"goal restored");Eq(4f,m._moveSpeed,"unwritten speed restored");m.goalMode=Mover.GoalMode.Object;object[] next={default(DL.MoverBoost),m};Hook(typeof(Mover_Update_DeadlandsSpeed_Patch),"Prefix",next);m.goalMode=Mover.GoalMode.Off;m._moveSpeed=9;Hook(typeof(Mover_Update_DeadlandsSpeed_Patch),"Finalizer",next[0],null,m);Eq(9f,m._moveSpeed,"distinct callback speed retained");});
  Test("Deadlands repeated animation rebases external positive speed",()=>{var k=NewKnight(1);var owner=new DL.UnitRef{Knight=k,UnitPtr=k.Pointer};DL.HandleAnimTrigger(k._animator,Animator.StringToHash("Slash"),owner);k._animator.speed=1.25f;DL.HandleAnimTrigger(k._animator,Animator.StringToHash("Slash"),owner);Eq(2.5f,k._animator.speed,"new native speed boosted");DL.OnKnightDisabled(k);Eq(1.25f,k._animator.speed,"new native speed restored");});
  RunWindArcTests();
  Console.WriteLine($"RESULT: {passed} passed, {failed} failed");Environment.ExitCode=failed==0?0:1;
 }
}
