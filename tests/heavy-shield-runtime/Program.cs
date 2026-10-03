using System.Reflection;
using KingdomEnhancedMod;
using UnityEngine;

int checks = 0;
void Check(bool ok, string label) { ++checks; if (!ok) throw new Exception(label); }
void Step(Fixture f, float dt=.25f) {Time.deltaTime=dt;Time.time+=dt;HeavyShieldActorVisuals.Tick(f.Handle.GoId);HeavyShieldRuntime.Tick();}
void Guard(Fixture f) {HeavyShieldRuntime.Tick();for(int i=0;i<8;i++)Step(f);Check(HeavyShieldActorVisuals.GuardReady(f.Archer,f.Handle.Life),"equip completes before guard");}
HeavyShieldSavedCombatState State(Fixture f){Check(HeavyShieldRuntime.CaptureCombat(f.Handle,out var s),"capture exact career");return s;}
void HitTroll(Fixture f, Troll troll, DamageSource kind=DamageSource.Troll, int amount=1)
{ HeavyShieldCombat.ObserveTrollIntent(troll,f.Damage);using var scope=HeavyShieldCombat.EnterTroll(troll,f.Damage);
  f.Damage.ReceiveDamage(amount,troll.gameObject,kind); }
void HitArrow(Fixture f, Arrow arrow, DamageSource? overrideKind=null)
{using var flight=HeavyShieldCombat.EnterArrowHit(arrow);using var impact=HeavyShieldCombat.EnterArrowDamage(arrow,f.Damage);
    f.Damage.ReceiveDamage(1,arrow.archer,overrideKind??arrow._damageSource);}
Troll TrollAt(float x) {var go=new GameObject("troll");go.transform.SetParent(Managers.Inst.world.gameLayer,false);go.transform.position=new(x,0);var troll=go.AddComponent<Troll>();troll.Type=EnemyType.TrollWeak;go.AddComponent<Damageable>();return troll;}
Arrow ArrowFrom(float vx)
{var owner=new GameObject("enemy archer");owner.transform.SetParent(Managers.Inst.world.gameLayer,false);var enemy=owner.AddComponent<GreedArcher>();enemy.Type=EnemyType.Archer;owner.AddComponent<Damageable>();
    var go=new GameObject("arrow");go.transform.SetParent(Managers.Inst.world.gameLayer,false);var arrow=go.AddComponent<Arrow>();arrow._rigidbody=go.AddComponent<Rigidbody2D>();arrow._rigidbody.velocity=new(vx,0);arrow.archer=owner;return arrow;}
Character PeasantResult() {var go=new GameObject("native peasant");go.transform.SetParent(Managers.Inst.world.gameLayer,false);go.AddComponent<Peasant>();return go.AddComponent<Character>();}
void NativePool(GameObject root) {HeavyShieldIntegration.BeforePoolDespawn(root,0);root.SetActive(false);HeavyShieldIntegration.AfterPoolDespawn(root,0);}
var entryRows=(System.Collections.IDictionary)typeof(HeavyShieldRuntime).GetField("Entries",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
void NoAttempt(Fixture f,HeavyShieldRuntime.AttachResult expected,string label,HeavyShieldCareerHandle? handle=null)
{
    int writes=NativeBoundaryWrites.Count,entries=entryRows.Count,markers=GameObject.LifeAdds,components=f.Go.Components.Count;
    int saves=HeavyShieldIdentity.Saved.Count,disables=f.Archer.NativeDisables,enables=f.Archer.NativeEnables;
    var native=new HeavyShieldRuntime.NativeFields(f.Archer);var move=new HeavyShieldRuntime.MoverFields(f.Archer._mover);
    bool archer=f.Archer.enabled,embark=f.Archer.Embarkee.enabled,off=f.Archer._spriteRenderer.forceRenderingOff;
    var receipt=handle??f.Handle;var wear=new HeavyShieldSavedCombatState(1,false,false);
    Check(HeavyShieldRuntime.TryAttachCarrier(f.Archer,receipt,wear)==expected,label+" result");
    Check(writes==NativeBoundaryWrites.Count&&entries==entryRows.Count&&markers==GameObject.LifeAdds&&components==f.Go.Components.Count,label+" zero engine writes/entries/markers/components");
    Check(native.Equals(new HeavyShieldRuntime.NativeFields(f.Archer))&&move.Matches(f.Archer._mover)
        &&archer==f.Archer.enabled&&embark==f.Archer.Embarkee.enabled&&off==f.Archer._spriteRenderer.forceRenderingOff,label+" native fields unchanged");
    Check(saves==HeavyShieldIdentity.Saved.Count&&disables==f.Archer.NativeDisables&&enables==f.Archer.NativeEnables,label+" zero receipt rewrite or native enable attempt");
}
void NoSourceWrites(Fixture f,Action action,string label)
{
    var native=new HeavyShieldRuntime.NativeFields(f.Archer);var move=new HeavyShieldRuntime.MoverFields(f.Archer._mover);
    int enables=f.Archer.EnabledWrites,embark=f.Archer.Embarkee.EnabledWrites,visible=f.Archer._spriteRenderer.VisibilityWrites;
    int nativeRenderer=f.Archer._spriteRenderer.EnabledWrites,mover=f.Archer._mover.CommandWrites,targets=Managers.Inst.targetCache.Writes;
    bool rendererOff=f.Archer._spriteRenderer.forceRenderingOff;action();
    Check(native.Equals(new HeavyShieldRuntime.NativeFields(f.Archer))&&move.Matches(f.Archer._mover),label+" no stale field/Mover restore");
    Check(enables==f.Archer.EnabledWrites&&embark==f.Archer.Embarkee.EnabledWrites&&visible==f.Archer._spriteRenderer.VisibilityWrites
        &&nativeRenderer==f.Archer._spriteRenderer.EnabledWrites&&rendererOff==f.Archer._spriteRenderer.forceRenderingOff,label+" no native enable/visibility write");
    Check(mover==f.Archer._mover.CommandWrites&&targets==Managers.Inst.targetCache.Writes&&entryRows.Contains(f.Handle.GoId),label+" no native Mover/target command or lost credential");
}

var stack=new HeavyShieldIngressStack<int>();
using(var outer=stack.Push(1,true))
{
    Check(stack.TryPeek(out var v)&&v==1,"outer active");
    using(var disabled=stack.Push(2,false)){Check(!stack.TryPeek(out _),"disabled nested masks outer");}
    Check(stack.TryPeek(out v)&&v==1,"nested finalizer returns outer");
    var scopes=new List<IDisposable>();
    for(int i=0;i<HeavyShieldIngressStack<int>.Capacity;i++)scopes.Add(stack.Push(3,true));
    Check(!stack.TryPeek(out _),"overflow masks active ticket");
    scopes[^1].Dispose();Check(stack.TryPeek(out v)&&v==3,"overflow closes to inner valid frame");
    for(int i=scopes.Count-2;i>=0;i--)scopes[i].Dispose();
    try {using var fault=stack.Push(4,true);throw new ApplicationException();}catch(ApplicationException){}
    Check(stack.TryPeek(out v)&&v==1,"exception finally restores outer");
}
Check(!stack.TryPeek(out _)&&stack.Depth==0,"all finalizers close");
Check(HeavyShieldDirection.ArrowFront(-1,1)&&HeavyShieldDirection.ArrowFront(1,-1),"velocity mirrored front");
foreach(float bad in new[]{0,float.NaN,float.PositiveInfinity,1})Check(!HeavyShieldDirection.ArrowFront(bad,1),"invalid or rear vx pass");

using(var loaded=new Fixture(deferBind:true))
{
    HeavyShieldIntegration.BeginPoolSpawn();try{HeavyShieldIntegration.ObserveArcherEnable(loaded.Archer);}finally{HeavyShieldIntegration.EndPoolSpawn();}
    loaded.Handle=loaded.Handle with {Life=HeavyShieldIdentity.Lives[loaded.Go.Pointer]};HeavyShieldIdentity.Careers.Add(loaded.Handle);
    Managers.Inst.game.state=Game.State.Other;
    Check(loaded.Archer.enabled&&!HeavyShieldRuntime.IsCarrierActive(loaded.Handle)&&!loaded.Attach(new(1,false,false)),"Loading native OnEnable precedes deferred carrier activation");
    Check(HeavyShieldIdentity.Careers.Contains(loaded.Handle)&&!HeavyShieldIdentity.Unresolved.Contains(loaded.Handle.Receipt),"deferred inactive Loading keeps the bound career intact");
    Managers.Inst.game.state=Game.State.Playing;
    Check(loaded.Attach(new(1,false,false))&&HeavyShieldRuntime.IsCarrierActive(loaded.Handle)&&State(loaded).Durability==1,"Playing attach uses exact new native life and preserves restored wear");
}

using(var f=new Fixture())
{
    HarmonyLib.Harmony.Installed=false;Check(!HeavyShieldRuntime.CarrierPreflightReady,"missing installed hook gate");HarmonyLib.Harmony.Installed=true;
    HeavyShieldArt.Available=false;Check(!HeavyShieldRuntime.CarrierPreflightReady,"missing approved sprite gate");HeavyShieldArt.Available=true;
    Check(f.Attach(new(1,false,false)),"attach saved worn life");
    Check(HeavyShieldRuntime.IsCarrierActive(f.Handle),"attached exact career is active");Time.timeScale=0;
    Check(!HeavyShieldRuntime.IsCarrierActive(f.Handle)&&HeavyShieldRuntime.IsControlled(f.Archer),"paused career retains ownership but is not active");Time.timeScale=1;
    Check(HeavyShieldRuntime.CarrierMutationInProgress==false,"carrier mutation stack closes");
    Check(!f.Archer.enabled&&!f.Archer.Embarkee.enabled&&f.Damage.enabled&&f.Archer.persistent.enabled&&f.Archer._mover.enabled,"only native Archer and Embarkee paused");
    Check(Managers.Inst.targetCache._trollPriorityTargets.Count(d=>d==f.Damage)==1,"one own target registration");
    Check(f.Attach(new(3,false,false))&&State(f).Durability==1,"duplicate attach never repairs worn shield");
    var troll=TrollAt(5.5f);HitTroll(f,troll);Check(State(f).Durability==1&&f.Damage.NativeHits==1,"back shield/unfinished equip cannot block");
    Guard(f);f.Damage.invulnerable=true;HitTroll(f,troll);Check(State(f).Durability==1,"native invulnerable consumes no shield");f.Damage.invulnerable=false;
    f.Damage.Allowed=false;HitTroll(f,troll);Check(State(f).Durability==1,"native kind immunity consumes no shield");f.Damage.Allowed=true;
    HitTroll(f,troll,DamageSource.Fire);Check(State(f).Durability==1,"Fire cannot borrow Troll ticket");
    f.Damage.ReceiveDamage(1,troll.gameObject,DamageSource.Troll);Check(State(f).Durability==1,"no point impact passes");
    troll.transform.position=new(4,0);HitTroll(f,troll);Check(State(f).Durability==1,"rear Troll passes");
    Check(HeavyShieldRuntime.DetachCarrier(f.Handle),"return fields");
    Check(f.Archer.enabled&&f.Archer.Embarkee.enabled&&!f.Archer._spriteRenderer.forceRenderingOff,"native AI and renderer returned");
    Check(f.Archer._guardSide==Side.Right&&f.Archer._guardDepth==3&&f.Archer._absoluteFaceIndex==9&&f.Archer._cooldown==7,"native borrowed fields restored");
    Check(f.Archer._character.outfitColor==new Color(1)&&f.Archer._animator.runtimeAnimatorController==f.BeforeAnimator,"appearance fields returned");
    Check(f.Archer._mover._goalPosition==23&&f.Archer._mover._goalSpeed==2&&f.Archer._mover.goalMode==Mover.GoalMode.Position,"mover goal returned");
    Check(Managers.Inst.targetCache._trollPriorityTargets.Count(d=>d==f.Damage)==1,"native OnEnable supplies only its registration");
}
using(var f=new Fixture())
{
    Check(f.Attach(new(3,false,false)),"arrow test attach");Guard(f);
    var off=ArrowFrom(-2);int markerAdds=GameObject.LifeAdds,bodyReads=off.BodyReads;ModConfig.HeavyShieldEnabled.Value=false;
    using(var hit=HeavyShieldCombat.EnterArrowHit(off)){Check(!HeavyShieldCombat.TryBlock(f.Damage,1,off.archer,DamageSource.GreedProjectile),"default off native pass");}
    Check(GameObject.LifeAdds==markerAdds&&off.BodyReads==bodyReads&&off.gameObject.GetComponent<CombatTargetLifeMarker>()==null,"default off zero marker/native body reads");
    ModConfig.HeavyShieldEnabled.Value=true;
    var arrow=ArrowFrom(-2);arrow.archer.transform.position=new(-100,0); // shooter position is intentionally rear
    using(var hit=HeavyShieldCombat.EnterArrowHit(arrow))
    {
        arrow._rigidbody.velocity=new(0,0); // native HitObject changes velocity before TryDamage
        using(var child=HeavyShieldCombat.EnterArrowDamage(arrow,f.Damage))f.Damage.ReceiveDamage(1,arrow.archer,DamageSource.GreedProjectile);
    }
    Check(State(f).Durability==2&&f.Damage.NativeHits==0,"pre-HitObject vx blocks despite rear owner and later zero velocity");
    arrow._rigidbody.velocity=new(2,0);HitArrow(f,arrow);Check(State(f).Durability==2&&f.Damage.NativeHits==1,"rear arrow passes");
    arrow._rigidbody.velocity=new(-2,0);HitArrow(f,arrow,DamageSource.Fire);Check(State(f).Durability==2,"arrow scope Fire passes");
    using(var hit=HeavyShieldCombat.EnterArrowHit(arrow))
    using(var child=HeavyShieldCombat.EnterArrowDamage(arrow,f.Damage))
    using(var disabled=HeavyShieldCombat.EnterArrowDamage(new Arrow(),f.Damage))
        f.Damage.ReceiveDamage(1,arrow.archer,DamageSource.GreedProjectile);
    Check(State(f).Durability==2,"invalid nested child masks outer active impact");
    using(var hit=HeavyShieldCombat.EnterArrowHit(arrow))
    using(var child=HeavyShieldCombat.EnterArrowDamage(arrow,f.Damage))
    using(var disabledHit=HeavyShieldCombat.EnterArrowHit(new Arrow()))
        f.Damage.ReceiveDamage(1,arrow.archer,DamageSource.GreedProjectile);
    Check(State(f).Durability==2,"invalid nested HitObject masks outer even without child");
    using(var hit=HeavyShieldCombat.EnterArrowHit(arrow))
    using(var child=HeavyShieldCombat.EnterArrowDamage(arrow,f.Damage))
    using(var intent=HeavyShieldCombat.EnterTrollIntent(null,f.Damage))
        f.Damage.ReceiveDamage(1,arrow.archer,DamageSource.GreedProjectile);
    Check(State(f).Durability==2,"nested non-impact Troll intent masks outer ticket");
    using(var hit=HeavyShieldCombat.EnterArrowHit(arrow))
    {arrow.gameObject.SetActive(false);arrow.gameObject.SetActive(true);using var child=HeavyShieldCombat.EnterArrowDamage(arrow,f.Damage);
        f.Damage.ReceiveDamage(1,arrow.archer,DamageSource.GreedProjectile);}
    Check(State(f).Durability==2,"pooled projectile new life cannot borrow old flight");
    using(var hit=HeavyShieldCombat.EnterArrowHit(arrow))
    using(var child=HeavyShieldCombat.EnterArrowDamage(arrow,f.Damage))
    {arrow.archer.SetActive(false);arrow.archer.SetActive(true);f.Damage.ReceiveDamage(1,arrow.archer,DamageSource.GreedProjectile);}
    Check(State(f).Durability==2,"pooled shooter new life cannot borrow child receipt");
    Check(!HeavyShieldCombat.InNativeDamageStack,"native flight/impact/damage stack restored");
    object[] prefixArgs={arrow,null};
    typeof(HeavyShieldArrowHit).GetMethod("Prefix",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,prefixArgs);
    var exception=new ApplicationException("native throw");
    var returned=typeof(HeavyShieldArrowHit).GetMethod("Finalizer",BindingFlags.NonPublic|BindingFlags.Static)
        .Invoke(null,new object[]{exception,prefixArgs[1]});
    Check(ReferenceEquals(returned,exception)&&!HeavyShieldCombat.InNativeDamageStack,"actual hook finalizer returns original exception and closes flight/mask");
    object[] damageArgs={f.Damage,1,arrow.archer,DamageSource.GreedProjectile,null};
    typeof(HeavyShieldReceiveDamage).GetMethod("Prefix",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,damageArgs);
    returned=typeof(HeavyShieldReceiveDamage).GetMethod("Finalizer",BindingFlags.NonPublic|BindingFlags.Static)
        .Invoke(null,new object[]{f.Damage,exception,damageArgs[4]});
    Check(ReferenceEquals(returned,exception)&&!HeavyShieldCombat.InNativeDamageStack,"four-arg hook finalizer returns original exception and closes damage stack");
}
using(var f=new Fixture())
{
    Check(f.Attach(new(1,false,false)),"break/death attach");Guard(f);var troll=TrollAt(5.5f);
    int before=Character.Demotes,exit=HeavyShieldIdentity.Exits;
    using(var incoming=HeavyShieldCombat.EnterTroll(troll,f.Damage)) { } // no saved intent, not trusted
    HeavyShieldCombat.ObserveTrollIntent(troll,f.Damage);
    using(var incoming=HeavyShieldCombat.EnterTroll(troll,f.Damage))
    {f.Damage.ReceiveDamage(1,troll.gameObject,DamageSource.Troll);HeavyShieldRuntime.Tick();Check(Character.Demotes==before,"deferred retire outside ingress");}
    Check(State(f).PendingBreak&&State(f).Durability==0,"third lifetime hit saves pending break");
    f.Damage.Health=1;f.Damage.ReceiveDamage(2,troll.gameObject,DamageSource.Fire);HeavyShieldRuntime.Tick();
    Check(Character.Demotes==before&&HeavyShieldIdentity.Exits==exit+1,"same-frame true death supersedes demotion");
}
foreach(bool throws in new[]{false,true})
using(var f=new Fixture())
{
    Check(f.Attach(new(0,true,false)),"restore pending break");f.Archer._character.DemoteResult=()=>throws?throw new ApplicationException():null;
    int before=Character.Demotes;for(int i=0;i<5;i++)Step(f);
    Check(Character.Demotes==before+1&&State(f).RetirementUnknown,"null/throw native Demote at most once and receipt held");
}
using(var f=new Fixture())
{
    Check(f.Attach(new(0,true,true)),"restore retirement unknown");int before=Character.Demotes;
    HeavyShieldRuntime.Tick();Check(Character.Demotes==before&&State(f).RetirementUnknown,"saved unknown never retries after load");
}
using(var f=new Fixture())
{
    Check(f.Attach(new(0,true,false)),"confirmed peasant demote attach");var successor=new GameObject("peasant");successor.transform.SetParent(Managers.Inst.world.gameLayer,false);successor.AddComponent<Peasant>();var c=successor.AddComponent<Character>();
    f.Archer._character.DemoteResult=()=>{HeavyShieldIntegration.BeforePoolDespawn(f.Go,0);f.Go.SetActive(false);HeavyShieldIntegration.AfterPoolDespawn(f.Go,0);
        Check(!HeavyShieldIdentity.Unresolved.Contains(f.Handle.Receipt),"own pool postfix retains Soldier until whole native return");return c;};int exit=HeavyShieldIdentity.Exits;for(int i=0;i<5;i++)Step(f);
    Check(HeavyShieldIdentity.Exits==exit+1&&!HeavyShieldRuntime.IsControlled(f.Archer),"exact Peasant and terminal old root release claim");
    Check(f.Archer.enabled&&f.Archer.Embarkee.enabled,"old source returned enabled states before pool reuse");
}
using(var f=new Fixture())
{
    Check(f.Attach(new(3,false,false)),"bash attach");Guard(f);var a=TrollAt(5.4f);var b=TrollAt(5.5f);var c=TrollAt(5.6f);var d=TrollAt(5.7f);
    f.Archer._enemyScanner.Rows=new[]{a.gameObject,a.gameObject,b.gameObject,c.gameObject,d.gameObject};Time.time+=1;HeavyShieldRuntime.Tick();
    Step(f);Step(f);Step(f);Check(new[]{a,b,c,d}.Sum(t=>t.gameObject.GetComponent<Damageable>().NativeHits)==3,"single impact max three unique ordinary target lives");
    int hits=new[]{a,b,c,d}.Sum(t=>t.gameObject.GetComponent<Damageable>().NativeHits);for(int i=0;i<12;i++)Step(f);
    Check(new[]{a,b,c,d}.Sum(t=>t.gameObject.GetComponent<Damageable>().NativeHits)==hits,"repeated drive does not repeat impact during cooldown");
    a.gameObject.SetActive(false);a.gameObject.SetActive(true);Time.time+=8;HeavyShieldRuntime.Tick();for(int i=0;i<3;i++)Step(f);
    Check(a.gameObject.GetComponent<Damageable>().NativeHits==2,"new bash recognizes pooled enemy new life");
}
using(var f=new Fixture())
{
    Check(f.Attach(new(3,false,false)),"intent generation attach");Guard(f);var troll=TrollAt(5.5f);
    HitTroll(f,troll);Check(State(f).Durability==2,"initial intent valid");
    var intents=(Dictionary<int,HeavyShieldCombat.Impact>)typeof(HeavyShieldCombat).GetField("TrollIntents",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
    for(int i=0;i<64;i++)intents[100000+i]=default;
    HeavyShieldIdentity.Generation++;HitTroll(f,troll);
    Check(State(f).Durability==1&&intents.Count==0,"exact campaign owner generation resets stale cap and accepts new intent");
}
using(var f=new Fixture())
{
    Check(f.Attach(new(0,true,false)),"pre-demote receipt failure attach");HeavyShieldIdentity.FailUpdate=true;int attempts=Character.Demotes;
    for(int i=0;i<5;i++)Step(f);
    Check(Character.Demotes==attempts&&State(f).RetirementUnknown&&!HeavyShieldRuntime.CarrierPreflightReady,"failed unknown receipt prevents Demote and freezes economy");
    HeavyShieldIdentity.FailUpdate=false;
}
using(var f=new Fixture())
{
    Check(f.Attach(new(3,false,false)),"release failure attach");Managers.Inst.targetCache.FailDeregister=true;
    Check(!HeavyShieldRuntime.DetachCarrier(f.Handle)&&State(f).Durability==3,"failed target return retains credentials");
    Check(HeavyShieldRuntime.IsControlled(f.Archer)&&!HeavyShieldRuntime.IsCarrierActive(f.Handle),"owned but unattached is not active");
    Check(!f.Archer.enabled,"failed detach never resumes native over own target registration");Managers.Inst.targetCache.FailDeregister=false;
    Check(HeavyShieldRuntime.DetachCarrier(f.Handle)&&f.Archer.enabled,"retry returns exact original fields");
}
using(var f=new Fixture())
{
    Check(f.Attach(new(2,false,false)),"alive pool return attach");int exits=HeavyShieldIdentity.Exits;
    HeavyShieldRuntime.BeforeNativePoolDespawn(f.Go);
    Check(f.Archer.enabled&&f.Archer.Embarkee.enabled&&!f.Archer._spriteRenderer.forceRenderingOff,"pool return restores enabled/render fields before new life");
    Check(HeavyShieldIdentity.Exits==exits&&HeavyShieldIdentity.Careers.Contains(f.Handle),"alive unload returns loans but preserves paid occupancy");
}
using(var f=new Fixture())
{
    Check(f.Attach(new(3,false,false)),"world switch attach");var old=Managers.Inst.world.gameLayer;
    Managers.Inst.world.gameLayer=new GameObject("new island").transform;HeavyShieldRuntime.Tick();
    Check(!f.Archer.enabled&&!HeavyShieldRuntime.IsControlled(f.Archer),"world switch stops control without enabling stale root");
    Check(!HeavyShieldRuntime.IsCarrierActive(f.Handle),"old world career is not active");
    Check(!HeavyShieldRuntime.DetachCarrier(f.Handle),"foreign world cannot return old borrowed fields");
    Check(f.Archer._spriteRenderer.forceRenderingOff,"world mismatch cannot restore native renderer from old world credential");
    using(var next=new Fixture()) {Check(next.Attach(new(3,false,false)),"old world credentials do not consume current island driver capacity");HeavyShieldRuntime.Tick();
        Check(next.Archer._mover._goalPosition==4.75f,"old world low-id Right does not add a current island seat offset");}
    Managers.Inst.world.gameLayer=old;Check(HeavyShieldRuntime.DetachCarrier(f.Handle),"credential survives world mismatch and may return when same context verified");
}
using(var f=new Fixture())
{
    f.Archer.StopFails=true;Check(!f.Attach(new(3,false,false)),"unconfirmed Haglet suspension rejects runtime attach");
    Check(f.Archer.enabled&&!HeavyShieldRuntime.IsControlled(f.Archer),"failed attach rolls native carrier back with life mask");
}

// R3 invokes the production typed API. Native OnDisable alone clears the three proven job memberships.
for(int jobs=1;jobs<8;jobs++)
using(var f=new Fixture())
{
    if((jobs&1)!=0)f.Archer._guardSlot=new object();if((jobs&2)!=0)f.Archer._knight=new object();if((jobs&4)!=0)f.Archer._currentFormation=new object();
    int expected=((jobs&1)!=0?1:0)+((jobs&2)!=0?1:0)+((jobs&4)!=0?1:0);
    Check(HeavyShieldRuntime.TryAttachCarrier(f.Archer,f.Handle,new(1,false,false))==HeavyShieldRuntime.AttachResult.Attached,$"Playing native jobs {jobs} attach");
    Check(f.Archer.NativeDisables==1&&f.Archer.NativeJobExits==expected&&f.Archer.NativeEnables==0,$"native OnDisable, once, exits jobs {jobs}");
    Check(f.Archer._guardSlot==null&&f.Archer._knight==null&&f.Archer._currentFormation==null,$"post-disable native jobs {jobs} proven clear");
    Check(State(f).Durability==1&&HeavyShieldRuntime.IsCarrierActive(f.Handle)&&Managers.Inst.targetCache._trollPriorityTargets.Count(d=>d==f.Damage)==1,$"jobs {jobs} retain wear and one target");
}
for(int gate=0;gate<10;gate++)
using(var f=new Fixture())
{
    HeavyShieldIdentity.Saved[f.Handle.Receipt]=new(1,false,false);
    switch(gate)
    {
        case 0:Time.timeScale=0;break;case 1:Managers.Inst.game.state=Game.State.Other;break;
        case 2:ModConfig.HeavyShieldEnabled.Value=false;break;case 3:f.Archer.Embarkee.IsTargetingEmbarkable=true;break;
        case 4:f.Archer.Embarkee.IsEmbarked=true;break;case 5:f.Archer._character.grabbed=true;break;
        case 6:f.Archer._unitController=new object();break;case 7:f.Archer._spriteRenderer.enabled=false;break;
        case 8:f.Archer._spriteRenderer.forceRenderingOff=true;break;case 9:HarmonyLib.Harmony.Installed=false;break;
    }
    NoAttempt(f,HeavyShieldRuntime.AttachResult.Deferred,$"temporary gate {gate}");
    Check(HeavyShieldIdentity.Saved[f.Handle.Receipt].Durability==1&&!HeavyShieldIdentity.Unresolved.Contains(f.Handle.Receipt),$"temporary gate {gate} preserves paid worn claim");
    switch(gate)
    {
        case 0:Time.timeScale=1;break;case 1:Managers.Inst.game.state=Game.State.Playing;break;
        case 2:ModConfig.HeavyShieldEnabled.Value=true;break;case 3:f.Archer.Embarkee.IsTargetingEmbarkable=false;break;
        case 4:f.Archer.Embarkee.IsEmbarked=false;break;case 5:f.Archer._character.grabbed=false;break;
        case 6:f.Archer._unitController=null;break;case 7:f.Archer._spriteRenderer.enabled=true;break;
        case 8:f.Archer._spriteRenderer.forceRenderingOff=false;break;case 9:HarmonyLib.Harmony.Installed=true;break;
    }
    Check(HeavyShieldRuntime.TryAttachCarrier(f.Archer,f.Handle,new(1,false,false))==HeavyShieldRuntime.AttachResult.Attached
        &&f.Archer.NativeDisables==1&&State(f).Durability==1,$"temporary gate {gate} later borrows once without repair");
    Check(HeavyShieldRuntime.TryAttachCarrier(f.Archer,f.Handle,new(3,false,false))==HeavyShieldRuntime.AttachResult.Attached
        &&f.Archer.NativeDisables==1&&State(f).Durability==1,$"temporary gate {gate} attached idempotence");
}
using(var f=new Fixture(deferBind:true)) {NoAttempt(f,HeavyShieldRuntime.AttachResult.Failed,"unpaid fake career");}
using(var f=new Fixture())
{
    NoAttempt(f,HeavyShieldRuntime.AttachResult.Failed,"old native life",f.Handle with {Life=f.Handle.Life-1});
    NoAttempt(f,HeavyShieldRuntime.AttachResult.Failed,"foreign root identity",f.Handle with {Root=(IntPtr)999999});
    NoAttempt(f,HeavyShieldRuntime.AttachResult.Failed,"foreign world identity",f.Handle with {World=f.Handle.World+1});
    HeavyShieldIdentity.Unresolved.Add(f.Handle.Receipt);NoAttempt(f,HeavyShieldRuntime.AttachResult.Failed,"unresolved paid phase");
}
foreach(bool loading in new[]{false,true})
using(var f=new Fixture())
{
    f.Archer._guardSlot=new object();f.Archer._knight=new object();f.Archer._currentFormation=new object();
    f.Archer.JobExitFails=!loading;if(loading)f.Archer.NativeAfterDisable=()=>Managers.Inst.game.state=Game.State.Other;
    Check(HeavyShieldRuntime.TryAttachCarrier(f.Archer,f.Handle,new(1,false,false))==HeavyShieldRuntime.AttachResult.Failed,"post-borrow job/Loading violation is Failed");
    Check(f.Archer.NativeDisables==1&&f.Archer.NativeEnables==1&&f.Archer.enabled&&f.Archer.Embarkee.enabled,"failed actual attempt returns same-life enables once");
    Check(f.Archer._guardSide==Side.Right&&f.Archer._guardDepth==3&&f.Archer._absoluteFaceIndex==9&&f.Archer._cooldown==7
        &&f.Archer._character.outfitColor==new Color(1)&&f.Archer._animator.runtimeAnimatorController==f.BeforeAnimator,"failed attempt restores borrowed native appearance and fields");
    Check(f.Archer._mover._goalPosition==23&&f.Archer._mover._goalSpeed==2&&!HeavyShieldRuntime.IsControlled(f.Archer)
        &&Managers.Inst.targetCache._trollPriorityTargets.Count(d=>d==f.Damage)==1,"failed attempt returns native movement/target registration");
    // This is A's terminal Failed boundary, not a C-created replacement or retry queue.
    HeavyShieldIdentity.Unresolved.Add(f.Handle.Receipt);Managers.Inst.game.state=Game.State.Playing;
    NoAttempt(f,HeavyShieldRuntime.AttachResult.Failed,"failed paid career never retried");
}
using(var f=new Fixture())
{
    var old=Managers.Inst.world.gameLayer;f.Archer.NativeAfterDisable=()=>Managers.Inst.world.gameLayer=new GameObject("mid-borrow world").transform;
    Check(HeavyShieldRuntime.TryAttachCarrier(f.Archer,f.Handle,new(1,false,false))==HeavyShieldRuntime.AttachResult.Failed,"mid-borrow world mismatch fails after actual write");
    int writes=NativeBoundaryWrites.Count;
    Check(entryRows.Contains(f.Handle.GoId)&&!f.Archer.enabled&&f.Archer.NativeEnables==0&&!HeavyShieldRuntime.DetachCarrier(f.Handle)
        &&writes==NativeBoundaryWrites.Count,"world CAS refusal retains enable/native credential and writes nothing stale");
    HeavyShieldIdentity.Unresolved.Add(f.Handle.Receipt);NoAttempt(f,HeavyShieldRuntime.AttachResult.Failed,"world failure is not retried");
    Managers.Inst.world.gameLayer=old;Check(HeavyShieldRuntime.DetachCarrier(f.Handle)&&f.Archer.enabled,"exact original world later permits owned return");
}

// Fill the real retained ledger through 64 distinct native worlds; never synthesize Entries or clear credentials.
Check(entryRows.Count==0,"capacity case starts after real owned teardown");
var baseWorld=Managers.Inst.world.gameLayer;var retained=new List<(Fixture actor,Transform world)>();
for(int i=0;i<64;i++)
{
    Managers.Inst.world.gameLayer=new GameObject("retained world "+i).transform;var f=new Fixture();
    Check(f.Attach(new(1,false,false)),"real retained world attaches below global cap");retained.Add((f,Managers.Inst.world.gameLayer));HeavyShieldRuntime.Tick();
}
int capReadWrites=NativeBoundaryWrites.Count;
Check(entryRows.Count==64&&!HeavyShieldRuntime.CarrierPreflightReady&&capReadWrites==NativeBoundaryWrites.Count,"64 retained closes readonly new-fee readiness");
Check(HeavyShieldRuntime.IsCarrierActive(retained[^1].actor.Handle),"capacity does not stop the already active current driver");
using(var f=new Fixture()){NoAttempt(f,HeavyShieldRuntime.AttachResult.Failed,"64 retained denies new borrow before writes");}
foreach(var row in retained){Managers.Inst.world.gameLayer=row.world;row.actor.Dispose();}
Managers.Inst.world.gameLayer=baseWorld;Check(entryRows.Count==0&&HeavyShieldRuntime.CarrierPreflightReady,"exact-world teardown returns every retained loan without clearing unknown credentials");

// The native Demote boundary always runs the real root Pool before+after callbacks.
using(var f=new Fixture())
{
    Check(f.Attach(new(3,false,false)),"external native demote attach");var peasant=PeasantResult();int exits=HeavyShieldIdentity.Exits;
    f.Archer._character.DemoteResult=()=>{Check(HeavyShieldRuntime.HoldsNativeDemoteProof(f.Go),"whole native scope holds exact source");NativePool(f.Go);
        Check(!HeavyShieldIdentity.Unresolved.Contains(f.Handle.Receipt)&&HeavyShieldRuntime.HoldsNativeDemoteProof(f.Go),"root Pool postfix leaves Soldier while whole source is ended");return peasant;};
    Check(ReferenceEquals(f.Archer._character.Demote(),peasant)&&HeavyShieldIdentity.Exits==exits+1,"external native Peasant return releases paid claim once");
    f.Archer._character.DemoteResult=()=>peasant;f.Archer._character.Demote();Check(HeavyShieldIdentity.Exits==exits+1,"second native call cannot release same receipt twice");
}
foreach(bool own in new[]{false,true})
using(var f=new Fixture())
{
    Check(f.Attach(own?new(0,true,false):new(3,false,false)),"native demote death priority attach");var peasant=PeasantResult();int exits=HeavyShieldIdentity.Exits;
    f.Archer._character.DemoteResult=()=>{NativePool(f.Go);f.Damage.isDead=true;return peasant;};
    if(own)for(int i=0;i<5;i++)Step(f);else f.Archer._character.Demote();
    Check(HeavyShieldIdentity.Exits==exits+1&&HeavyShieldIdentity.ConfirmedKinds[^1]==HeavyShieldExitKind.Dead,"same-life death wins over Peasant native return");
}
foreach(bool throws in new[]{false,true})
using(var f=new Fixture())
{
    Check(f.Attach(new(3,false,false)),"native unknown scope attach");var original=new ApplicationException("native uncertain");int exits=HeavyShieldIdentity.Exits;
    f.Archer._character.DemoteResult=()=>{if(throws){NativePool(f.Go);throw original;}return null;};
    try{f.Archer._character.Demote();Check(!throws,"null returns normally");}catch(ApplicationException ex){Check(ReferenceEquals(ex,original),"native exception preserved exactly");}
    Check(HeavyShieldIdentity.Exits==exits&&HeavyShieldIdentity.Unresolved.Contains(f.Handle.Receipt)&&HeavyShieldIdentity.EconomyUnknown,"whole null/throw closes scope then real A unresolved path locks money without freeing seat");
    Check(!HeavyShieldRuntime.HoldsNativeDemoteProof(f.Go),"unknown does not leave permanent Soldier proof mask");
    int calls=Character.Demotes;for(int i=0;i<5;i++)HeavyShieldRuntime.Tick();Check(Character.Demotes==calls,"unknown is never retried by driver");
}
using(var f=new Fixture())
{
    Check(f.Attach(new(3,false,false)),"active native source attach");var peasant=PeasantResult();int exits=HeavyShieldIdentity.Exits;
    f.Archer._character.DemoteResult=()=>peasant;f.Archer._character.Demote();
    Check(HeavyShieldIdentity.Exits==exits&&HeavyShieldIdentity.Unresolved.Contains(f.Handle.Receipt),"active old source with returned Peasant is unknown");
}
using(var f=new Fixture())
using(var unrelated=new Fixture())
{
    Check(f.Attach(new(3,false,false))&&unrelated.Attach(new(3,false,false)),"nested native source fixtures attach");var peasant=PeasantResult();var ordinary=new GameObject("ordinary").AddComponent<Character>();
    ordinary.DemoteResult=()=>{Check(!HeavyShieldRuntime.HoldsNativeDemoteProof(f.Go),"inactive nested ordinary scope masks outer proof");return null;};
    f.Archer._character.DemoteResult=()=>{ordinary.Demote();Check(HeavyShieldRuntime.HoldsNativeDemoteProof(f.Go),"outer exact proof restored after ordinary finalizer");
        NativePool(unrelated.Go);Check(HeavyShieldIdentity.Unresolved.Contains(unrelated.Handle.Receipt),"unrelated paid source pool is not masked by outer native scope");
        NativePool(f.Go);return peasant;};int exits=HeavyShieldIdentity.Exits;f.Archer._character.Demote();Check(HeavyShieldIdentity.Exits==exits+1,"nested ordinary call does not steal outer receipt");
}
using(var f=new Fixture())
{
    Check(f.Attach(new(3,false,false)),"native scope overflow attach");var scopes=new List<HeavyShieldRuntime.NativeDemoteCapture>();scopes.Add(HeavyShieldRuntime.BeginNativeDemote(f.Archer._character));
    for(int i=0;i<33;i++)scopes.Add(HeavyShieldRuntime.BeginNativeDemote(null));Check(!HeavyShieldRuntime.HoldsNativeDemoteProof(f.Go),"native overflow masks active source proof");
    for(int i=scopes.Count-1;i>0;i--)scopes[i].Dispose();Check(HeavyShieldRuntime.HoldsNativeDemoteProof(f.Go),"overflow unwind restores outer exact source");scopes[0].Dispose();
    Check(HeavyShieldIdentity.Unresolved.Contains(f.Handle.Receipt)&&!HeavyShieldRuntime.HoldsNativeDemoteProof(f.Go),"unproven outer closed into actual unresolved phase");
}
using(var f=new Fixture())
{
    Check(f.Attach(new(3,false,false)),"managed native finalizer attach");object[] prefix={f.Archer._character,null};
    typeof(HeavyShieldNativeDemote).GetMethod("Prefix",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,prefix);var original=new ApplicationException("native failure");
    var returned=typeof(HeavyShieldNativeDemote).GetMethod("Finalizer",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{original,prefix[1]});
    Check(ReferenceEquals(returned,original)&&!HeavyShieldRuntime.HoldsNativeDemoteProof(f.Go)&&HeavyShieldIdentity.Unresolved.Contains(f.Handle.Receipt),"actual whole-Demote finalizer keeps exception and returns scope before unknown phase");
}
using(var f=new Fixture())
{
    Check(f.Attach(new(3,false,false)),"whole native world mismatch attach");var oldWorld=Managers.Inst.world.gameLayer;var peasant=PeasantResult();int exits=HeavyShieldIdentity.Exits;
    f.Archer._character.DemoteResult=()=>{NativePool(f.Go);Managers.Inst.world.gameLayer=new GameObject("changed mid-Demote").transform;
        f.Archer._spriteRenderer.forceRenderingOff=true;return peasant;};f.Archer._character.Demote();
    Check(HeavyShieldIdentity.Exits==exits&&HeavyShieldIdentity.Careers.Contains(f.Handle)&&f.Archer._spriteRenderer.forceRenderingOff,
        "whole native world change preserves paid receipt and does not restore an old-world renderer");
    Check(!HeavyShieldRuntime.HoldsNativeDemoteProof(f.Go),"world mismatch closes native proof mask");Managers.Inst.world.gameLayer=oldWorld;
}

// Read the production visual's own renderer, then assert its final world orientation.
var visualRows=(System.Collections.IDictionary)typeof(HeavyShieldActorVisuals).GetField("Active",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
SpriteRenderer Own(Fixture f) {var row=visualRows[f.Handle.GoId];return (SpriteRenderer)row.GetType().GetField("Own",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(row);}
GameObject VisualRoot(Fixture f)=>Own(f).gameObject;
HeavyShieldPoseMachine Pose(Fixture f) {var row=visualRows[f.Handle.GoId];return (HeavyShieldPoseMachine)row.GetType().GetField("Pose",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(row);}

// New art contract: the original integer is retained, including a pending zero, and every
// stance/action selects that wear. Existing three-hit/life/CAS/native-demote negatives above remain.
foreach(int durability in new[]{4,3,2,1,0})
using(var f=new Fixture())
{
    Check(f.Attach(new(durability,durability==0,false)),"final four-wear mapping attach");
    var expected=durability==4?HeavyShieldWear.Intact:durability==3?HeavyShieldWear.Worn:durability==2?HeavyShieldWear.Critical:HeavyShieldWear.Half;
    Check(Pose(f).Wear==expected&&State(f).Durability==durability,$"remaining {durability} preserved and maps to {expected}");
    Check((Pose(f).Action==HeavyShieldAction.Break)==(durability==0),"zero only starts terminal break");
}
using(var f=new Fixture())
{
    Check(f.Attach(new(4,false,false)),"new four-hit shield attach");Guard(f);var troll=TrollAt(5.5f);int demotes=Character.Demotes,exits=HeavyShieldIdentity.Exits;
    var successor=PeasantResult();f.Archer._character.DemoteResult=()=>{NativePool(f.Go);return successor;};
    for(int hit=1;hit<=4;hit++)
    {
        HitTroll(f,troll);var state=State(f);
        Check(state.Durability==4-hit&&state.PendingBreak==(hit==4)&&f.Damage.NativeHits==0,$"new shield lifetime hit {hit} blocks and decrements exactly once");
        Check(Pose(f).Wear==(hit==1?HeavyShieldWear.Worn:hit==2?HeavyShieldWear.Critical:HeavyShieldWear.Half),$"hit {hit} retains visible damage");
        Check((Pose(f).Action==HeavyShieldAction.Break)==(hit==4),"only fourth new-shield hit is terminal");
    }
    Step(f,.25f);Check(Character.Demotes==demotes,"break does not truncate before final metadata duration");
    Step(f,.25f);Check(Character.Demotes==demotes+1&&HeavyShieldIdentity.Exits==exits+1&&!HeavyShieldRuntime.IsControlled(f.Archer),"fourth new-shield hit yields one exact native Peasant demote");
}
using(var f=new Fixture())
{
    f.Go.transform.position=new(3.5f,0);f.Archer._mover.ActualSpeedValue=2f;
    Check(f.Attach(new(3,false,false)),"native actual motion attach");HeavyShieldRuntime.Tick();
    Check(Pose(f).Stance==HeavyShieldStance.BackIdle&&Pose(f).Action==HeavyShieldAction.None
        &&f.Archer._mover.ActualSpeedReads==0&&f.Archer._mover._goalSpeed==1f,"Animator zero wins over fast fallback and nonzero goal: no fabricated walk/run");
    f.Archer._animator.Speed=.6f;HeavyShieldRuntime.Tick();Check(Pose(f).Action==HeavyShieldAction.WalkStart,"actual walk edge starts once");
    Step(f,.125f);float phase=Pose(f).Elapsed;HeavyShieldRuntime.Tick();Check(Pose(f).Elapsed==phase,"steady native walk sample does not replay start");
    for(int i=0;i<3;i++)Step(f,.125f);Check(Pose(f).Action==HeavyShieldAction.None&&Pose(f).Stance==HeavyShieldStance.BackWalk,"actual walk start enters worn walk loop");
    f.Archer._animator.Speed=-2f;HeavyShieldRuntime.Tick();Check(Pose(f).Action==HeavyShieldAction.RunStart,"signed actual native run begins once");
    Step(f,.125f);phase=Pose(f).Elapsed;HeavyShieldRuntime.Tick();Check(Pose(f).Elapsed==phase&&f.Archer._mover._goalSpeed==1f,"steady run keeps phase and original walk command speed");
    Step(f,.125f);Check(Pose(f).Action==HeavyShieldAction.None&&Pose(f).Stance==HeavyShieldStance.BackRun,"real run enters worn run loop");
    f.Archer._animator.Speed=0;HeavyShieldRuntime.Tick();Check(Pose(f).Action==HeavyShieldAction.RunStop,"actual run-to-stop brakes");
    Step(f,.125f);phase=Pose(f).Elapsed;HeavyShieldRuntime.Tick();Check(Pose(f).Elapsed==phase,"steady stop never replays run stop");
    f.Archer._mover.Body=f.Go.AddComponent<Rigidbody2D>();f.Archer._mover.Body.velocity=new(2f,0);
    f.Archer._animator.ReadFails=true;HeavyShieldRuntime.Tick();Check(Pose(f).Action==HeavyShieldAction.RunStart&&f.Archer._mover.ActualSpeedReads==0&&f.Archer._mover.BodyReads>0,"unavailable Animator falls back to same-root physical velocity, never ActualSpeed");
    f.Archer._animator.ReadFails=false;f.Archer._animator.Speed=float.NaN;f.Archer._mover.Body.velocity=new(.5f,0);HeavyShieldRuntime.Tick();
    Check(Pose(f).Stance==HeavyShieldStance.BackWalk,"nonfinite animation falls back to actual walk");
    f.Archer._mover.BodyReadFails=true;HeavyShieldRuntime.Tick();Check(Pose(f).Action==HeavyShieldAction.None&&Pose(f).Stance==HeavyShieldStance.BackIdle,"unknown native samples never impersonate run");
    for(int i=0;i<16;i++)Step(f);Check(Pose(f).Action==HeavyShieldAction.None,"unknown sample cannot authorize quiet relaxation");
}
// P2 regression: the native command can say 2 while the physical carrier is stationary.
// A finite Animator remains authoritative; only a same-root rigidbody can replace a failed read.
using(var f=new Fixture())
{
    f.Go.transform.position=new(3.5f,0);f.Archer._mover.ActualSpeedValue=2f;
    var body=f.Go.AddComponent<Rigidbody2D>();f.Archer._mover.Body=body;body.velocity=new(0,0);
    Check(f.Attach(new(3,false,false)),"physical fallback P2 attach");f.Archer._animator.ReadFails=true;HeavyShieldRuntime.Tick();
    Check(Pose(f).Stance==HeavyShieldStance.BackIdle&&Pose(f).Action==HeavyShieldAction.None&&f.Archer._mover.ActualSpeedReads==0,"unreadable Animator plus ActualSpeed2 and physical0 never fabricates Run");
    foreach(float speed in new[]{2f,-2f})
    {
        body.velocity=new(speed,0);HeavyShieldRuntime.Tick();Check(Pose(f).Stance==HeavyShieldStance.BackRun&&Pose(f).Action==HeavyShieldAction.RunStart,$"same-root physical {speed} authorizes real run");
        Step(f,.125f);float elapsed=Pose(f).Elapsed;HeavyShieldRuntime.Tick();Check(Pose(f).Elapsed==elapsed,"same physical run sample never replays start");
        body.velocity=new(0,0);HeavyShieldRuntime.Tick();Check(Pose(f).Action==HeavyShieldAction.RunStop,"real physical stop brakes once");
        for(int i=0;i<3;i++)Step(f,.125f);
    }
    body.velocity=new(.5f,0);HeavyShieldRuntime.Tick();Check(Pose(f).Stance==HeavyShieldStance.BackWalk&&Pose(f).Action==HeavyShieldAction.WalkStart,"same-root physical 0.5 selects walk");
    f.Archer._animator.ReadFails=false;f.Archer._animator.Speed=0;body.velocity=new(2f,0);int bodyReads=f.Archer._mover.BodyReads;
    HeavyShieldRuntime.Tick();Check(Pose(f).Stance==HeavyShieldStance.BackIdle&&f.Archer._mover.BodyReads==bodyReads,"finite Animator pause0 remains first and avoids body access");
    f.Archer._animator.enabled=false;HeavyShieldRuntime.Tick();Check(Pose(f).Stance==HeavyShieldStance.BackRun&&f.Archer._mover.ActualSpeedReads==0,"disabled Animator uses verified physical body");
}
for(int fault=0;fault<8;fault++)
using(var f=new Fixture())
{
    f.Go.transform.position=new(3.5f,0);f.Archer._mover.ActualSpeedValue=2f;var body=f.Go.AddComponent<Rigidbody2D>();body.velocity=new(2f,0);f.Archer._mover.Body=body;
    Check(f.Attach(new(2,false,false)),"rejected physical sample attach");f.Archer._animator.ReadFails=true;
    switch(fault)
    {
        case 0:f.Archer._mover.Body=null;break;
        case 1:f.Archer._mover.Body=new Rigidbody2D();break;
        case 2:f.Archer._mover.BodyReadFails=true;break;
        case 3:body.VelocityReadFails=true;break;
        case 4:body.velocity=new(float.NaN,0);break;
        case 5:body.velocity=new(float.PositiveInfinity,0);break;
        case 6:
            var foreignPointer=new GameObject("foreign body pointer");typeof(GameObject).GetField("Id",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(foreignPointer,f.Go.GetInstanceID());
            f.Archer._mover.Body=foreignPointer.AddComponent<Rigidbody2D>();f.Archer._mover.Body.velocity=new(2f,0);break;
        case 7:
            var foreignId=new GameObject("foreign body id"){Pointer=f.Go.Pointer};f.Archer._mover.Body=foreignId.AddComponent<Rigidbody2D>();f.Archer._mover.Body.velocity=new(2f,0);break;
    }
    HeavyShieldRuntime.Tick();Check(Pose(f).Stance==HeavyShieldStance.BackIdle&&Pose(f).Action==HeavyShieldAction.None&&f.Archer._mover.ActualSpeedReads==0,$"missing/foreign/throw/nonfinite physical fault {fault} stays unknown, never Run");
    for(int i=0;i<16;i++)Step(f);Check(Pose(f).Action==HeavyShieldAction.None&&HeavyShieldRuntime.IsCarrierActive(f.Handle),$"unknown physical fault {fault} never grants relaxation or changes career control");
    if(fault>=6)Check(f.Archer._mover.Body.VelocityReads==0,"foreign body is rejected before its velocity is read");
}
foreach(float speed in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity,0f,.05f})
    Check(HeavyShieldRuntime.ClassifyActualMotion(speed,1f)==HeavyShieldStance.BackIdle,"unknown/deadzone speed stays idle");
Check(HeavyShieldRuntime.ClassifyActualMotion(-1.1f,1f)==HeavyShieldStance.BackRun
    &&HeavyShieldRuntime.ClassifyActualMotion(1f,1f)==HeavyShieldStance.BackWalk
    &&HeavyShieldRuntime.ClassifyActualMotion(3f,float.NaN)==HeavyShieldStance.BackWalk,"run requires known native walk threshold, signed magnitudes allowed");
using(var f=new Fixture())
{
    Check(f.Attach(new(3,false,false)),"front quiet rest attach");Guard(f);var beforeController=f.Archer._animator.runtimeAnimatorController;
    for(int i=0;i<16&&Pose(f).Action!=HeavyShieldAction.Rest;i++)Step(f);
    Check(Pose(f).Action==HeavyShieldAction.Rest&&HeavyShieldActorVisuals.GuardReady(f.Archer,f.Handle.Life)
        &&HeavyShieldRuntime.IsCarrierActive(f.Handle),"stationary quiet front rest retains active shield defense");
    var troll=TrollAt(5.5f);HitTroll(f,troll);Check(State(f).Durability==2&&Pose(f).Action==HeavyShieldAction.Block,"actual block immediately replaces front rest and keeps correct wear");
    for(int i=0;i<3;i++)Step(f);HeavyShieldActorVisuals.Trigger(f.Archer,f.Handle.Life,HeavyShieldAction.RestExit);
    f.Archer._enemyScanner.Rows=new[]{troll.gameObject};Time.time+=1;HeavyShieldRuntime.Tick();
    Check(Pose(f).Action==HeavyShieldAction.Bash,"close threat immediately bashes during RestExit");
    Step(f,.249f);Check(troll.gameObject.GetComponent<Damageable>().NativeHits==0,"bash impact remains delayed to 0.25 seconds");
    Step(f,.001f);Check(troll.gameObject.GetComponent<Damageable>().NativeHits==1&&troll.gameObject.GetComponent<Damageable>().Health==9,"bash impact remains exactly one damage at 0.25 seconds");
    Check(f.Archer.NativeDisables==1&&f.Archer.NativeEnables==0&&f.Archer._animator.runtimeAnimatorController==beforeController,"rest/block/bash never add native suspension or Animator control writes");
}
foreach(var rest in new[]{HeavyShieldAction.RestEnter,HeavyShieldAction.Rest,HeavyShieldAction.RestExit})
using(var f=new Fixture())
{
    Check(f.Attach(new(2,false,false)),"front rest interrupt attach");Guard(f);HeavyShieldActorVisuals.Trigger(f.Archer,f.Handle.Life,rest);
    Check(HeavyShieldActorVisuals.GuardReady(f.Archer,f.Handle.Life),$"{rest} remains GuardReady");
    f.Archer._animator.Speed=.5f;HeavyShieldRuntime.Tick();Check(Pose(f).Action==HeavyShieldAction.None&&Pose(f).Stance==HeavyShieldStance.Advance,$"actual movement immediately replaces {rest}");
    f.Archer._animator.Speed=0;HeavyShieldActorVisuals.Trigger(f.Archer,f.Handle.Life,rest);var troll=TrollAt(6.5f);
    f.Archer._enemyScanner.Rows=new[]{troll.gameObject};Time.time+=1;HeavyShieldRuntime.Tick();Check(Pose(f).Action==HeavyShieldAction.None,$"enemy approach immediately replaces {rest} without waiting exit");
    f.Archer._enemyScanner.Rows=Array.Empty<GameObject>();Time.time+=1;f.Go.transform.position=new(3.5f,0);HeavyShieldActorVisuals.Trigger(f.Archer,f.Handle.Life,rest);HeavyShieldRuntime.Tick();
    Check(Pose(f).Action==HeavyShieldAction.Stow&&f.Archer.NativeDisables==1,$"stow immediately preempts {rest} using existing control owner");
}
using(var f=new Fixture())
{
    f.Go.transform.position=new(3.5f,0);Check(f.Attach(new(1,false,false)),"back quiet relax attach");
    for(int i=0;i<16;i++)Step(f);Check(Pose(f).Action==HeavyShieldAction.Relax&&!HeavyShieldActorVisuals.GuardReady(f.Archer,f.Handle.Life),"back quiet relaxation never grants shield readiness");
    var troll=TrollAt(5.5f);f.Archer._enemyScanner.Rows=new[]{troll.gameObject};Time.time+=1;HeavyShieldRuntime.Tick();
    Check(Pose(f).Action==HeavyShieldAction.Equip,"enemy approach immediately equips from back relaxation");
}
using(var f=new Fixture())
{
    f.Go.transform.position=new(3.5f,0);f.Archer._animator.Speed=2f;Check(f.Attach(new(4,false,false)),"paused pose attach");HeavyShieldRuntime.Tick();Step(f,.125f);
    float elapsed=Pose(f).Elapsed;Time.timeScale=0;Time.deltaTime=.25f;HeavyShieldActorVisuals.Tick(f.Handle.GoId);HeavyShieldRuntime.Tick();
    Check(Pose(f).Elapsed==elapsed&&Pose(f).Action==HeavyShieldAction.RunStart,"pause freezes actual production pose even if a callback supplies positive dt");Time.timeScale=1;
}
using(var f=new Fixture())
{
    var world=Managers.Inst.world.gameLayer;var savedWorldScale=world.localScale;var savedActorScale=f.Go.transform.localScale;
    try
    {
        world.localScale=new(-2f,.5f,1f);f.Go.transform.localScale=new(.4f,2f,1f);Check(f.Attach(new(3,false,false)),"nonunit inherited scale attaches");
        foreach(var actorScale in new[]{new Vector3(.4f,2f,1f),new Vector3(-1.5f,.7f,1f)})foreach(int facing in new[]{-1,1})
        {
            f.Go.transform.localScale=actorScale;HeavyShieldActorVisuals.SetFacing(f.Archer,f.Handle.Life,facing);HeavyShieldActorVisuals.Tick(f.Handle.GoId);var own=Own(f);
            Check(Math.Abs(Math.Abs(own.transform.lossyScale.x)-1f)<.00001f&&Math.Abs(Math.Abs(own.transform.lossyScale.y)-1f)<.00001f,"only owned child cancels inherited XY magnitude: body remains calibrated 0.7");
            Check(Math.Sign(own.transform.lossyScale.x)*(own.flipX?-1:1)==facing,"nonunit mirrored ancestors compensate world facing exactly once");
            Check(f.Go.transform.localScale.x==actorScale.x&&f.Go.transform.localScale.y==actorScale.y&&world.localScale.x==-2f&&world.localScale.y==.5f
                &&own.transform.localPosition.x==0&&own.transform.localPosition.y==0,"native actor/Mover scales and foot point remain unchanged");
        }
    }
    finally{world.localScale=savedWorldScale;f.Go.transform.localScale=savedActorScale;}
}

// Metadata mutation is test-local and restored; production fallback keeps a zero-durability
// career progressing and warns once, while the original native Demote gates remain in force.
var clips=(HeavyShieldSequence[])typeof(HeavyShieldArtLayout).GetField("SoldierSequenceTable",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
int breakSlot=Array.FindIndex(clips,c=>c.Name=="break");var savedBreak=clips[breakSlot];int warnings=KingdomEnhancedPlugin.Instance.LogSource.Warnings;
try
{
    foreach(float fps in new[]{6f,0f,-1f,float.NaN,float.PositiveInfinity})
    using(var f=new Fixture())
    {
        clips[breakSlot]=new("break",savedBreak.First,savedBreak.Count,fps,false);Check(f.Attach(new(0,true,false)),"metadata break timing attach");
        var beforePose=Pose(f);f.Archer._character.DemoteResult=()=>null;int attempts=Character.Demotes;float duration=savedBreak.Count/(float.IsFinite(fps)&&fps>0?fps:12f);
        Step(f,.125f);Check(Character.Demotes==attempts,"break holds before metadata duration");
        for(int i=0;i<12&&Character.Demotes==attempts;i++)Step(f,.125f);
        Check(Character.Demotes==attempts+1&&beforePose.Elapsed>=duration&&State(f).RetirementUnknown,"break metadata reaches native demote exactly once without a zero-durability hang");
    }
}
finally{clips[breakSlot]=savedBreak;}
Check(KingdomEnhancedPlugin.Instance.LogSource.Warnings==warnings+1,"all invalid FPS values share one bounded warning");
using(var f=new Fixture(side:HeavyShieldQuota.Side.Left))
{
    float dt=Time.deltaTime;Time.deltaTime=0;
    Check(f.Attach(new(1,false,false))&&HeavyShieldActorVisuals.HasQualifiedVisual(f.Archer,f.Handle.Life)
        &&HeavyShieldRuntime.IsCarrierActive(f.Handle),"Register synchronously qualifies first complete visual even with zero frame delta");
    var own=Own(f);Check(own.sprite!=null&&own.sharedMaterial!=null&&own.enabled&&f.Archer._spriteRenderer.forceRenderingOff
        &&Math.Sign(own.transform.lossyScale.x)*(own.flipX?-1:1)==-1,"first safe frame has usable sprite/material and the career's world facing");
    Time.deltaTime=dt;
}
foreach(HeavyShieldAction action in new[]{HeavyShieldAction.Equip,HeavyShieldAction.Stow})
using(var f=new Fixture())
{
    Check(f.Attach(new(1,false,false))&&HeavyShieldActorVisuals.Trigger(f.Archer,f.Handle.Life,action),"ordinary visual transition starts");
    Check(!HeavyShieldActorVisuals.GuardReady(f.Archer,f.Handle.Life)&&HeavyShieldRuntime.IsCarrierActive(f.Handle),"Equip/Stow is qualified activation although guard is not ready");
    HeavyShieldRuntime.Tick();Check(HeavyShieldRuntime.IsControlled(f.Archer)&&HeavyShieldRuntime.IsCarrierActive(f.Handle)
        &&f.Archer.NativeDisables==1&&f.Archer.NativeEnables==0,"ordinary Equip/Stow never detaches or retires the career");
}
for(int fault=0;fault<6;fault++)
using(var f=new Fixture())
{
    Check(f.Attach(new(1,false,false)),"visual fault attaches exact career");f.Go.transform.position=new(3.5f,0);HeavyShieldRuntime.Tick();
    Check(f.Archer._mover._goalSpeed>0&&HeavyShieldRuntime.IsCarrierActive(f.Handle),"qualified driver owns nonzero motion before visual fault");
    var own=Own(f);var ownRoot=own.gameObject;var native=f.Archer._spriteRenderer;int visualCount=visualRows.Count;
    switch(fault)
    {
        case 0:own.enabled=false;break;case 1:own.sharedMaterial=null;break;case 2:own.sprite=null;break;
        case 3:own.forceRenderingOff=true;break;case 4:native.enabled=false;break;case 5:native.forceRenderingOff=false;break;
    }
    Check(HeavyShieldActorVisuals.HasVisual(f.Archer,f.Handle.Life)&&!HeavyShieldActorVisuals.HasQualifiedVisual(f.Archer,f.Handle.Life)
        &&!HeavyShieldRuntime.IsCarrierActive(f.Handle),$"visual fault {fault} separates readonly ownership from execution qualification");
    var troll=TrollAt(4.2f);f.Archer._enemyScanner.Rows=new[]{troll.gameObject};HeavyShieldActorVisuals.Tick(f.Handle.GoId);HeavyShieldRuntime.Tick();
    Check(!own.enabled&&f.Archer._mover._goalSpeed==0&&f.Archer._mover._goalPosition==3.5f
        &&HeavyShieldRuntime.IsControlled(f.Archer)&&!HeavyShieldRuntime.IsCarrierActive(f.Handle),$"visual fault {fault} stops owned presentation and movement");
    float goal=f.Archer._mover._goalPosition;for(int i=0;i<8;i++)Step(f);
    Check(f.Archer._mover._goalSpeed==0&&f.Archer._mover._goalPosition==goal&&troll.gameObject.GetComponent<Damageable>().NativeHits==0
        &&visualRows.Count==visualCount&&VisualRoot(f).Pointer==ownRoot.Pointer&&f.Archer.NativeDisables==1,$"visual fault {fault} never drives, bashes, re-registers, or reborrows");
    Check(native.forceRenderingOff==(fault!=5)&&native.enabled==(fault!=4)
        &&(fault!=1||own.sharedMaterial==null)&&(fault!=2||own.sprite==null),$"visual fault {fault} leaves native visibility and failed owned properties untouched");
    NoAttempt(f,HeavyShieldRuntime.AttachResult.Failed,$"unqualified owned visual {fault} cannot activate by another attach");
    Check(HeavyShieldRuntime.DetachCarrier(f.Handle)&&f.Archer.enabled&&f.Archer.Embarkee.enabled&&!native.forceRenderingOff
        &&!HeavyShieldActorVisuals.HasVisual(f.Archer,f.Handle.Life)&&native.enabled==(fault!=4),$"visual fault {fault} teardown uses ownership and restores only exact native receipt");
}
using(var f=new Fixture())
{
    Check(f.Attach(new(1,false,false)),"native renderer replacement attach");f.Go.transform.position=new(3.5f,0);HeavyShieldRuntime.Tick();
    var original=f.Archer._spriteRenderer;var own=Own(f);var root=VisualRoot(f);int count=visualRows.Count;
    var replacement=f.Go.AddComponent<SpriteRenderer>();f.Archer._spriteRenderer=replacement;
    Check(HeavyShieldActorVisuals.HasVisual(f.Archer,f.Handle.Life)&&!HeavyShieldActorVisuals.HasQualifiedVisual(f.Archer,f.Handle.Life)
        &&!HeavyShieldRuntime.IsCarrierActive(f.Handle),"actual native renderer identity replacement fails readonly qualification");
    for(int i=0;i<6;i++)Step(f);
    Check(original.forceRenderingOff&&!replacement.forceRenderingOff&&!own.enabled&&f.Archer._mover._goalSpeed==0
        &&HeavyShieldActorVisuals.HasVisual(f.Archer,f.Handle.Life)&&visualRows.Count==count&&VisualRoot(f).Pointer==root.Pointer,
        "renderer replacement retains ownership, stops motion, and writes neither unknown native renderer");
    NoAttempt(f,HeavyShieldRuntime.AttachResult.Failed,"renderer replacement cannot re-register or reborrow");
    int originalWrites=original.VisibilityWrites,replacementWrites=replacement.VisibilityWrites,enables=f.Archer.NativeEnables;
    Check(!HeavyShieldRuntime.DetachCarrier(f.Handle)&&entryRows.Contains(f.Handle.GoId)&&f.Archer.NativeEnables==enables
        &&original.VisibilityWrites==originalWrites&&replacement.VisibilityWrites==replacementWrites
        &&original.forceRenderingOff&&!replacement.forceRenderingOff,"failed renderer return keeps its receipt and makes no native visibility/enable write");
    // A later exact original renderer context may return that retained receipt; no career/life proof is rewritten.
    f.Go.Components[typeof(SpriteRenderer)]=original;f.Archer._spriteRenderer=original;
    Check(HeavyShieldRuntime.DetachCarrier(f.Handle)&&!original.forceRenderingOff&&!replacement.forceRenderingOff
        &&!HeavyShieldActorVisuals.HasVisual(f.Archer,f.Handle.Life),"same recorded renderer identity allows ownership teardown after qualification loss");
}
using(var f=new Fixture())
{
    Check(f.Attach(new(3,false,false)),"world direction attach");
    foreach(int facing in new[]{-1,1})foreach(float ancestor in new[]{-1f,1f})foreach(float parent in new[]{-1f,1f})
    {
        Managers.Inst.world.gameLayer.localScale=new(ancestor,1,1);f.Go.transform.localScale=new(parent,1,1);
        HeavyShieldActorVisuals.SetFacing(f.Archer,f.Handle.Life,facing);HeavyShieldActorVisuals.Tick(f.Handle.GoId);
        var own=Own(f);int actual=Math.Sign(own.transform.lossyScale.x)*(own.flipX?-1:1);
        Check(actual==facing,$"world atlas orientation facing={facing}, ancestor={ancestor}, parent={parent}");
    }
    Managers.Inst.world.gameLayer.localScale=Vector3.one;f.Go.transform.localScale=Vector3.one;
    var driver=VisualRoot(f).GetComponent<HeavyShieldVisualDriver>();
    typeof(HeavyShieldVisualDriver).GetMethod("OnDisable",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(driver,null);
    Check(!f.Archer._spriteRenderer.forceRenderingOff&&!visualRows.Contains(f.Handle.GoId),"ordinary same-life driver disable returns native visibility");
}
using(var f=new Fixture())
{
    Check(f.Attach(new(3,false,false)),"world driver callback attach");var oldWorld=Managers.Inst.world.gameLayer;var root=VisualRoot(f);
    Managers.Inst.world.gameLayer=new GameObject("other world").transform;HeavyShieldRuntime.Tick();root.SetActive(false);root.SendMessage("OnDestroy");
    Check(f.Archer._spriteRenderer.forceRenderingOff&&visualRows.Contains(f.Handle.GoId),"real driver OnDisable/OnDestroy in wrong world retains native credential and writes no native visibility");
    Managers.Inst.world.gameLayer=oldWorld;Check(HeavyShieldRuntime.DetachCarrier(f.Handle),"same context may later return the retained visual credential");
}

// This final invalid-life case intentionally leaves its credentials held: never rewind a new native life to make cleanup pass.
using(var f=new Fixture())
{
    Check(f.Attach(new(3,false,false)),"new life native/visual callback attach");var root=VisualRoot(f);var oldRenderer=f.Archer._spriteRenderer;var peasant=PeasantResult();int exits=HeavyShieldIdentity.Exits;
    var newCareer=f.Handle with {Receipt=Guid.NewGuid(),Life=f.Handle.Life+1};
    f.Archer._character.DemoteResult=()=>{f.Go.SetActive(false);HeavyShieldIntegration.AfterPoolDespawn(f.Go,0);f.Go.SetActive(true);
        HeavyShieldIntegration.BeginPoolSpawn();try{HeavyShieldIntegration.ObserveArcherEnable(f.Archer);}finally{HeavyShieldIntegration.EndPoolSpawn();}
        HeavyShieldIdentity.Careers.Add(newCareer);oldRenderer.forceRenderingOff=true;return peasant;};f.Archer._character.Demote();
    Check(HeavyShieldIdentity.Exits==exits&&!HeavyShieldIdentity.Unresolved.Contains(newCareer.Receipt)&&HeavyShieldIdentity.Careers.Contains(f.Handle),
        "native old source new life cannot bind a new career to the old receipt or mark the new claim unknown");
    root.SetActive(false);root.SendMessage("OnDestroy");
    Check(oldRenderer.forceRenderingOff&&visualRows.Contains(f.Handle.GoId)&&!HeavyShieldRuntime.DetachCarrier(f.Handle),"new native life cannot borrow old renderer lease or discard unknown credential");
}
// A fresh world permits this final actual mid-borrow new-life case without reusing the earlier held life.
Managers.Inst.world.gameLayer=new GameObject("final mid-borrow life world").transform;
using(var f=new Fixture())
{
    f.Archer.NativeAfterDisable=()=>HeavyShieldIdentity.ObservePoolFreshLife(f.Go,true);
    Check(HeavyShieldRuntime.TryAttachCarrier(f.Archer,f.Handle,new(1,false,false))==HeavyShieldRuntime.AttachResult.Failed,"native fresh life during actual borrow fails the post-write proof");
    var borrowed=new HeavyShieldRuntime.NativeFields(f.Archer);var move=new HeavyShieldRuntime.MoverFields(f.Archer._mover);
    int visible=f.Archer._spriteRenderer.VisibilityWrites;HeavyShieldRuntime.Tick();
    Check(entryRows.Contains(f.Handle.GoId)&&f.Archer.NativeDisables==1&&f.Archer.NativeEnables==0&&!f.Archer.enabled
        &&f.Archer.Embarkee.enabled&&!HeavyShieldRuntime.DetachCarrier(f.Handle)&&borrowed.Equals(new HeavyShieldRuntime.NativeFields(f.Archer))
        &&move.Matches(f.Archer._mover)&&visible==f.Archer._spriteRenderer.VisibilityWrites,
        "actual life CAS refusal preserves old snapshot and never writes Embarkee or returns fields into the new life");
    NoAttempt(f,HeavyShieldRuntime.AttachResult.Failed,"old paid handle after actual native life mutation is never retried");
}
// R4 independent Combat marker generation must not be assumed to advance A's career epoch.
Managers.Inst.world.gameLayer=new GameObject("independent Combat life repro").transform;
using(var f=new Fixture())
{
    f.Archer.NativeAfterDisable=()=>{f.Go.SetActive(false);f.Go.SetActive(true);};
    var result=HeavyShieldRuntime.TryAttachCarrier(f.Archer,f.Handle,new(1,false,false));
    Check(result==HeavyShieldRuntime.AttachResult.Failed&&HeavyShieldIdentity.ValidateCareer(f.Handle),"independent Combat life changed while A exact career still validates");
    Check(f.Archer.NativeDisables==1&&f.Archer.NativeEnables==0&&!f.Archer.enabled&&f.Archer.Embarkee.enabled
        &&f.Archer._guardDepth==0&&entryRows.Contains(f.Handle.GoId),"independent Combat life Failed retains snapshot and does not enable/restore the old native career");
    NoSourceWrites(f,()=>{HeavyShieldRuntime.Tick();Check(!HeavyShieldRuntime.DetachCarrier(f.Handle),"independent Combat life refuses repeat old return");},"independent Combat life Failed after catch");
}

// Original X, both live and verifiably ended, remains a legal release credential.
Managers.Inst.world.gameLayer=new GameObject("same original X release world").transform;
using(var f=new Fixture())
{
    Check(f.Attach(new(1,false,false)),"R4 original X release attach");var marker=f.Go.GetComponent<CombatTargetLifeMarker>();long x=marker.Life;
    int writes=NativeBoundaryWrites.Count,adds=GameObject.LifeAdds;long next=(long)typeof(CombatTargetLife).GetField("NextLife",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
    Check(HeavyShieldRuntime.HasNativeSourceLife(f.Archer,f.Handle.Life,false)&&HeavyShieldRuntime.HasNativeSourceLife(f.Archer,f.Handle.Life,true)
        &&!HeavyShieldRuntime.HasNativeSourceLife(f.Archer,f.Handle.Life+1,true),"readonly proof accepts only exact live X career epoch");
    Check(writes==NativeBoundaryWrites.Count&&adds==GameObject.LifeAdds&&marker.Life==x&&marker.LastObservedLife==x
        &&next==(long)typeof(CombatTargetLife).GetField("NextLife",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null),"native source proof does not register/add/initialize/take a life number");
    f.Go.SetActive(false);Check(marker.Life==0&&marker.LastObservedLife==x&&marker.Observing
        &&!HeavyShieldRuntime.HasNativeSourceLife(f.Archer,f.Handle.Life,false)&&HeavyShieldRuntime.HasNativeSourceLife(f.Archer,f.Handle.Life,true),"inactive original X zero has exact stamped release proof only");
    f.Go.SetActive(false);Check(marker.LastObservedLife==x&&HeavyShieldRuntime.HasNativeSourceLife(f.Archer,f.Handle.Life,true),"repeated inactive zero preserves X stamp without a new number");
    Check(HeavyShieldRuntime.DetachCarrier(f.Handle)&&f.Archer.enabled&&f.Archer.Embarkee.enabled
        &&!f.Archer._spriteRenderer.forceRenderingOff&&!entryRows.Contains(f.Handle.GoId),"inactive same-X Detach returns exact native loans and removes only the returned entry");
}
Managers.Inst.world.gameLayer=new GameObject("same original X death world").transform;
using(var f=new Fixture())
{
    Check(f.Attach(new(1,false,false)),"R4 old-X death attach");int exits=HeavyShieldIdentity.Exits;f.Damage.isDead=true;f.Go.SetActive(false);
    HeavyShieldRuntime.ObserveNativeDeath(f.Damage);HeavyShieldRuntime.ObserveNativeDeath(f.Damage);
    Check(HeavyShieldIdentity.Exits==exits+1&&HeavyShieldIdentity.ConfirmedKinds[^1]==HeavyShieldExitKind.Dead
        &&f.Archer.enabled&&f.Archer.Embarkee.enabled&&!f.Archer._spriteRenderer.forceRenderingOff&&!entryRows.Contains(f.Handle.GoId),"same inactive X native death returns fields then confirms once");
}
Managers.Inst.world.gameLayer=new GameObject("same original X Demote world").transform;
using(var f=new Fixture())
{
    Check(f.Attach(new(1,false,false)),"R4 old-X native Demote attach");int exits=HeavyShieldIdentity.Exits;var peasant=PeasantResult();
    f.Archer._character.DemoteResult=()=>{f.Go.SetActive(false);return peasant;};f.Archer._character.Demote();
    Check(HeavyShieldIdentity.Exits==exits+1&&HeavyShieldIdentity.ConfirmedKinds[^1]==HeavyShieldExitKind.DemotedToPeasant
        &&f.Archer.enabled&&f.Archer.Embarkee.enabled&&!f.Archer._spriteRenderer.forceRenderingOff&&!entryRows.Contains(f.Handle.GoId),"same inactive X returned Peasant releases exact source once without a pre-pool return");
}
Managers.Inst.world.gameLayer=new GameObject("no native Entry proof world").transform;
using(var f=new Fixture())
{
    int writes=NativeBoundaryWrites.Count,adds=GameObject.LifeAdds;
    Check(!HeavyShieldRuntime.HasNativeSourceLife(f.Archer,f.Handle.Life,true)&&writes==NativeBoundaryWrites.Count
        &&adds==GameObject.LifeAdds&&f.Go.GetComponent<CombatTargetLifeMarker>()==null,"Entry-missing native source proof is readonly fail-closed");
}

for(int fault=0;fault<7;fault++)
{
    // Each rejected context retains its real credential; a distinct world avoids borrowing it for another fixture.
    Managers.Inst.world.gameLayer=new GameObject("native life rejected context "+fault).transform;
    using(var f=new Fixture())
    {
        Check(f.Attach(new(1,false,false)),"R4 rejected native life context attaches before transition");Guard(f);
        f.Go.transform.position=new(3.5f,0);HeavyShieldRuntime.Tick();Check(f.Archer._mover._goalSpeed>0,"native life fault starts with an owned moving goal");
        var marker=f.Go.GetComponent<CombatTargetLifeMarker>();long x=marker.Life;var own=Own(f);var root=VisualRoot(f);int demotes=Character.Demotes;
        switch(fault)
        {
            case 0:f.Go.SetActive(false);f.Go.SetActive(true);break;
            case 1:f.Go.SetActive(false);f.Go.SetActive(true);f.Go.SetActive(false);break;
            case 2:f.Go.SetActive(false);f.Go.SetActive(true);f.Go.SetActive(false);f.Go.SetActive(true);f.Go.SetActive(false);break;
            case 3:marker.enabled=false;break;
            case 4:marker.enabled=false;marker.enabled=true;break;
            case 5:marker.enabled=false;marker.enabled=true;f.Go.SetActive(false);break;
            case 6:typeof(CombatTargetLifeMarker).GetMethod("OnDestroy",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(marker,null);break;
        }
        Check(HeavyShieldIdentity.ValidateCareer(f.Handle)&&HeavyShieldRuntime.IsControlled(f.Archer)==f.Go.activeInHierarchy
            &&!HeavyShieldRuntime.HasNativeSourceLife(f.Archer,f.Handle.Life,false)&&!HeavyShieldRuntime.HasNativeSourceLife(f.Archer,f.Handle.Life,true)
            &&!HeavyShieldRuntime.IsCarrierActive(f.Handle),$"native life context {fault} rejects old X despite unchanged A epoch/ownership");
        if(fault<=2)Check((fault==0?marker.Life>0:marker.Life==0)&&marker.LastObservedLife!=x,$"native replacement context {fault} never stamps old X, including double generation");
        if(fault==5)Check(marker.Life==0&&marker.Observing&&marker.LastObservedLife==0,"recovered gap inactive zero cannot reuse stale X last-observed stamp");
        int writes=NativeBoundaryWrites.Count,adds=GameObject.LifeAdds;long beforeLife=marker.Life,beforeLast=marker.LastObservedLife;
        Check(!HeavyShieldRuntime.HasNativeSourceLife(f.Archer,f.Handle.Life,true)&&writes==NativeBoundaryWrites.Count
            &&adds==GameObject.LifeAdds&&marker.Life==beforeLife&&marker.LastObservedLife==beforeLast,$"rejected native context {fault} proof performs no observing mutation");
        var troll=TrollAt(4.2f);int hits=f.Damage.NativeHits;
        NoSourceWrites(f,()=>
        {
            Check(!HeavyShieldRuntime.TryBlock(f.Handle,false,0,4.2f),"old Combat token cannot authorize new native damage");
            HitTroll(f,troll);HeavyShieldActorVisuals.Tick(f.Handle.GoId);HeavyShieldRuntime.Tick();
            root.SetActive(false);root.SendMessage("OnDestroy");
            Check(!HeavyShieldRuntime.DetachCarrier(f.Handle),"unproven source refuses old native return");
        },$"rejected native context {fault}");
        Check(f.Damage.NativeHits==hits+1&&HeavyShieldIdentity.Saved[f.Handle.Receipt].Durability==1&&Character.Demotes==demotes&&!own.enabled
            &&f.Archer._spriteRenderer.forceRenderingOff&&HeavyShieldActorVisuals.HasVisual(f.Archer,f.Handle.Life)
            &&!HeavyShieldActorVisuals.HasQualifiedVisual(f.Archer,f.Handle.Life)&&visualRows.Contains(f.Handle.GoId),
            $"native life context {fault} passes body damage, stops only own display, and holds native/visual credentials");
    }
}
Check(!HeavyShieldRuntime.CarrierMutationInProgress,"all carrier enable/disable finally masks closed");
Console.WriteLine($"PASS HeavyShield production runtime/combat/life/visuals: {checks} assertions");

sealed class Fixture : IDisposable
{
    internal GameObject Go=new("shield");internal Archer Archer;internal Damageable Damage;internal HeavyShieldCareerHandle Handle;internal RuntimeAnimatorController BeforeAnimator;
    internal Fixture(bool deferBind=false,HeavyShieldQuota.Side side=HeavyShieldQuota.Side.Right)
    {Go.transform.SetParent(Managers.Inst.world.gameLayer,false);Go.transform.position=new(4.75f,0);Archer=Go.AddComponent<Archer>();Damage=Go.AddComponent<Damageable>();
        Archer._damageable=Damage;Archer._character=Go.AddComponent<Character>();Archer._mover=Go.AddComponent<Mover>();Archer.persistent=Go.AddComponent<Persistent>();
        Archer._spriteRenderer=Go.AddComponent<SpriteRenderer>();Archer._animator=Go.AddComponent<Animator>();Archer.Embarkee=Go.AddComponent<Embarkee>();
        Archer._guardSide=Side.Right;Archer._guardDepth=3;Archer._absoluteFaceIndex=9;Archer._cooldown=7;BeforeAnimator=Archer._animator.runtimeAnimatorController;
        Archer._mover.goalMode=Mover.GoalMode.Position;Archer._mover._goalPosition=23;Archer._mover._goalSpeed=2;
        Handle=new(Guid.NewGuid(),"campaign",side,0,Go.Pointer,Go.GetInstanceID(),Managers.Inst.world.gameLayer.Pointer.ToInt64(),Go.GetInstanceID());
        if(!deferBind)HeavyShieldIdentity.Careers.Add(Handle);HeavyShieldIdentity.Lives[Handle.Root]=Handle.Life;Managers.Inst.targetCache.RegisterPriorityTarget(Damage);
        Archer.MutationCheck=()=>{if(!HeavyShieldRuntime.CarrierMutationInProgress)throw new Exception("missing carrier mutation mask");};Embarkee.MutationCheck=Archer.MutationCheck;
    }
    internal bool Attach(HeavyShieldSavedCombatState s)=>HeavyShieldRuntime.AttachCarrier(Archer,Handle,s);
    public void Dispose(){HeavyShieldRuntime.DetachCarrier(Handle);HeavyShieldIdentity.Careers.Remove(Handle);Managers.Inst.targetCache._trollPriorityTargets.Remove(Damage);}
}
