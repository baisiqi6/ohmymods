using System;
using System.Reflection;
using KingdomEnhancedMod;
using UnityEngine;

// Source-linked host boundary: Damageable stub invokes the production Harmony
// Prefix/Finalizer. NativeCalls distinguishes a permitted native no-op from a skip.
// Portal is intentionally a bare gameObject, as current 2.4.0 OnAttack passes its
// own object (no Enemy/source Damageable) with Ogre=32. No OverlapArea is simulated.
internal static class RuntimeUniversalDamageRegression
{
    const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Static;
    internal static void Run(Action<bool, string> check)
    {
        var cases = new (string, Action<Action<bool,string>>)[] {
            ("Portal and shared source window", PortalWindow),
            ("back same-x null source and all DamageSource", Universal),
            ("equip stow idle and facing transition", Poses),
            ("native no-damage requests", NativeNoDamage),
            ("false source mask does not reject long-entry damage", FalseMask),
            ("native Character inspiration immunity", Inspiration),
            ("nonshield disabled and stale life", Invalid),
            ("current world attachment and visual ownership", OwnershipGates),
            ("ninetieth block and expired career", Break),
            ("foreign source cannot mark Troll witness", ForeignWitness),
            ("persistence refusal cannot undo block", PersistenceFailure),
        };
        int failures = 0;
        foreach (var (label, run) in cases)
        {
            try { run(check); Console.WriteLine("PASS universal " + label); }
            catch (Exception ex) { failures++; Console.WriteLine("FAIL universal " + label + ": " + ex.Message); }
        }
        check(failures == 0, $"universal damage: {failures} failed scenarios");
    }
    static void Prepare(Fixture f, int wear = 90)
    {
        f.Damage.Health=10000;
        if (!f.Attach(new(wear, false, false))) throw new Exception("fixture attach");
        HeavyShieldActorVisuals.Tick(f.Handle.GoId);
    }
    static int Wear(Fixture f)
    { if (!HeavyShieldRuntime.CaptureCombat(f.Handle, out var state)) throw new Exception("capture career"); return state.Durability; }
    static GameObject Source(string name, float x)
    { var go = new GameObject(name); go.transform.SetParent(Managers.Inst.world.gameLayer, false); go.transform.position=new(x,0); return go; }
    static void Advance(double dt=1d)
    { Time.unscaledTimeAsDouble+=dt; Time.unscaledTime+=(float)dt; }
    static void Guard(Fixture f)
    {
        HeavyShieldRuntime.Tick();
        for (int i=0;i<8;i++) { Time.time+=.25f;Advance(.25);Time.frameCount++;HeavyShieldActorVisuals.Tick(f.Handle.GoId);HeavyShieldRuntime.Tick(); }
    }
    static void PortalWindow(Action<bool,string> check)
    {
        using var f=new Fixture();Prepare(f);Guard(f);
        var portal=Source("portal",8);
        check(portal.GetComponent<Enemy>()==null && portal.GetComponent<Damageable>()==null && (int)DamageSource.Ogre==32,"Portal source contract");
        f.Damage.ReceiveDamage(999,portal,DamageSource.Ogre);
        check(f.Damage.NativeHits==0 && Wear(f)==89,"Portal Ogre positive range hit must skip native and charge one");
        Advance(.5);var rear=Source("rear",-10);
        f.Damage.ReceiveDamage(1,rear,DamageSource.GreedProjectile);
        check(f.Damage.NativeHits==0 && Wear(f)==89,"different source and kind share same free window");
        Advance(.499);f.Damage.ReceiveDamage(1,null,DamageSource.Fire);
        check(Wear(f)==89 && f.Damage.NativeHits==0,"window hits do not slide the charge stamp");
        Advance(.001);f.Damage.ReceiveDamage(1,portal,DamageSource.Ogre);
        check(Wear(f)==88 && f.Damage.NativeHits==0,"one natural second boundary charges once");
        check(f.Attach(new(90,false,false)),"duplicate attach");
        f.Damage.ReceiveDamage(1,null,DamageSource.Fire);
        check(Wear(f)==88,"repeat attach does not refill or reset clock");
    }
    static void Universal(Action<bool,string> check)
    {
        using var f=new Fixture();Prepare(f);Guard(f);
        var rear=Source("rear",-1);var same=Source("same x",f.Go.transform.position.x);
        foreach(var source in new GameObject[]{rear,same,null})
        { Advance();f.Damage.ReceiveDamage(1,source,DamageSource.Troll); }
        check(f.Damage.NativeHits==0 && Wear(f)==87,"back same-x and absent source are protected");
        foreach(var kind in Enum.GetValues<DamageSource>())
        { int before=Wear(f);Advance();f.Damage.ReceiveDamage(1,null,kind);check(f.Damage.NativeHits==0 && Wear(f)==before-1,"all source enum values block: "+kind); }
        Advance();f.Damage.ReceiveDamage(1,null,(DamageSource)0);
        Advance();f.Damage.ReceiveDamage(1,null,DamageSource.Fire|DamageSource.Ogre);
        check(f.Damage.NativeHits==0,"zero and combined source flags have no whitelist");
        var inactive=Source("dead and inactive source",0);inactive.AddComponent<Damageable>().isDead=true;inactive.SetActive(false);
        int beforeInactive=Wear(f);f.Damage.ReceiveDamage(1,inactive,DamageSource.Fire);
        check(f.Damage.NativeHits==0 && Wear(f)==beforeInactive,"source life is irrelevant to target protection and shared clock");
    }
    static void Poses(Action<bool,string> check)
    {
        using var f=new Fixture();Prepare(f);
        check(HeavyShieldActorVisuals.HasQualifiedVisual(f.Archer,f.Handle.Life) && !HeavyShieldActorVisuals.GuardReady(f.Archer,f.Handle.Life),"fixture has qualified ownership before GuardReady");
        f.Damage.ReceiveDamage(1,null,DamageSource.Ogre);
        check(f.Damage.NativeHits==0 && Wear(f)==89,"unfinished equip still protects");
        foreach(var action in new[]{HeavyShieldAction.Stow,HeavyShieldAction.Equip})
        { HeavyShieldActorVisuals.Trigger(f.Archer,f.Handle.Life,action);Advance();f.Damage.ReceiveDamage(1,null,DamageSource.Fire);check(f.Damage.NativeHits==0,"pose transition still protects: "+action); }
        HeavyShieldActorVisuals.SetStance(f.Archer,f.Handle.Life,HeavyShieldStance.BackIdle);
        HeavyShieldActorVisuals.Trigger(f.Archer,f.Handle.Life,HeavyShieldAction.Relax);
        Advance();f.Damage.ReceiveDamage(1,null,DamageSource.Fire);
        check(f.Damage.NativeHits==0,"relaxed back idle still protects");
        Guard(f);
        var entries=(System.Collections.IDictionary)typeof(HeavyShieldRuntime).GetField("Entries",Hidden).GetValue(null);
        typeof(HeavyShieldRuntime).GetMethod("SetFacing",Hidden).Invoke(null,new[]{entries[f.Handle.GoId],(object)(-1)});
        Advance();f.Damage.ReceiveDamage(1,Source("other side",100),DamageSource.Arrow);
        check(f.Damage.NativeHits==0,"turning facing does not open protection gap");
    }
    static void NativeNoDamage(Action<bool,string> check)
    {
        using var f=new Fixture();Prepare(f);Guard(f);
        foreach(int amount in new[]{0,-1}) { f.Damage.ReceiveDamage(amount,null,DamageSource.Ogre);check(Wear(f)==90,"nonpositive no wear"); }
        f.Damage.invulnerable=true;f.Damage.ReceiveDamage(1,null,DamageSource.Ogre);f.Damage.invulnerable=false;
        check(f.Damage.NativeCalls==3 && Wear(f)==90,"invulnerable passes through native and consumes no shield");
        // IsDamagedBy is a caller query, not a no-damage gate in the long entry.
        // The formerly asserted mask-immunity case now lives in FalseMask below.
    }
    static void FalseMask(Action<bool,string> check)
    {
        var ordinary=Source("ordinary false-mask control",1).AddComponent<Damageable>();
        ordinary.Allowed=false;int nativeHealth=ordinary.Health;
        ordinary.ReceiveDamage(2,null,DamageSource.Ogre);
        check(ordinary.NativeCalls==1 && ordinary.Health==nativeHealth-2,
            "ordinary false-mask long entry actually loses HP");
        Console.WriteLine("PASS false-mask native HP control");
        using var f=new Fixture();Prepare(f);Guard(f);f.Damage.Allowed=false;
        if(f.Damage.IsDamagedBy(DamageSource.Ogre))f.Damage.ReceiveDamage(1,null,DamageSource.Ogre);
        check(f.Damage.NativeCalls==0 && Wear(f)==90,
            "caller query can prevent invocation, with no shield consumption");
        int health=f.Damage.Health;
        f.Damage.ReceiveDamage(2,Source("portal false-mask",-10),DamageSource.Ogre);
        check(f.Damage.NativeCalls==0 && f.Damage.Health==health && Wear(f)==89,
            "false-mask Ogre long entry must block and charge instead of leaking HP");
        var trollGo=Source("witness owner",6);var troll=trollGo.AddComponent<Troll>();
        troll.Type=EnemyType.TrollWeak;trollGo.AddComponent<Damageable>();
        HeavyShieldCombat.ObserveTrollIntent(troll,f.Damage);
        using(var outer=HeavyShieldCombat.EnterTroll(troll,f.Damage))
        {
            foreach(var kind in new[]{(DamageSource)(1<<25),DamageSource.Fire|DamageSource.Ogre})
            {
                f.Damage.ReceiveDamage(2,null,kind);
                check(f.Damage.NativeCalls==0 && f.Damage.Health==health && Wear(f)==89,
                    "false-mask unknown/combined kinds and null source share charge window: "+kind);
            }
            check(outer is HeavyShieldTrollScope scope && !scope.Blocked,
                "false-mask foreign kinds cannot acquire an outer Troll witness");
        }
        f.Damage.Allowed=true;f.Damage.ReceiveDamage(2,Source("other source",10),DamageSource.Fire);
        check(f.Damage.NativeCalls==0 && Wear(f)==89,"mask changes and another source do not reset the charge window");
        Advance();f.Damage.Allowed=false;f.Damage.ReceiveDamage(2,null,DamageSource.Ogre);
        check(f.Damage.NativeCalls==0 && Wear(f)==88,"false-mask next natural second charges exactly once");
    }
    static void Inspiration(Action<bool,string> check)
    {
        using var f=new Fixture();Prepare(f);Guard(f);
        var character=f.Archer._character;var portal=Source("portal",-10);
        int health=f.Damage.Health;
        character._inspiredUntil=Time.time+1f;
        f.Damage.ReceiveDamage(999,portal,DamageSource.Ogre);
        check(f.Damage.NativeCalls==1 && character.InspiredDamageCallbacks==1 && f.Damage.Health==health && Wear(f)==90,
            "inspired target executes native pre-damage callback without spending shield");
        Time.time=character._inspiredUntil;
        f.Damage.ReceiveDamage(999,portal,DamageSource.Ogre);
        check(f.Damage.NativeCalls==1 && character.InspiredDamageCallbacks==1 && Wear(f)==89,
            "inspiration exact expiry returns to ordinary all-direction shield protection");
        character._inspiredUntil=Time.time+1f;
        f.Damage.ReceiveDamage(999,null,DamageSource.Fire);
        check(f.Damage.NativeCalls==2 && character.InspiredDamageCallbacks==2 && f.Damage.Health==health && Wear(f)==89,
            "renewed inspiration remains native and free with absent damage source");
        Time.time=character._inspiredUntil;
        f.Damage.ReceiveDamage(999,null,DamageSource.Fire);
        check(f.Damage.NativeCalls==2 && Wear(f)==89,
            "native immunity does not reset the shared unscaled charge stamp");
    }
    static void Invalid(Action<bool,string> check)
    {
        using var f=new Fixture();Prepare(f);Guard(f);
        ModConfig.HeavyShieldEnabled.Value=false;
        try { f.Damage.ReceiveDamage(1,null,DamageSource.Fire);check(f.Damage.NativeCalls==1 && Wear(f)==90,"feature off passes native"); }
        finally { ModConfig.HeavyShieldEnabled.Value=true; }
        var go=Source("ordinary resident",1);var d=go.AddComponent<Damageable>();d.ReceiveDamage(1,null,DamageSource.Fire);
        check(d.NativeHits==1,"ordinary target native behavior");
        HeavyShieldIdentity.Lives[f.Handle.Root]=f.Handle.Life+1;
        try { f.Damage.ReceiveDamage(1,null,DamageSource.Ogre);
        check(f.Damage.NativeCalls==2 && Wear(f)==90,"old native life cannot protect"); }
        finally { HeavyShieldIdentity.Lives[f.Handle.Root]=f.Handle.Life; }
    }
    static void OwnershipGates(Action<bool,string> check)
    {
        using(var unmounted=new Fixture())
        {
            unmounted.Damage.ReceiveDamage(1,null,DamageSource.Ogre);
            check(unmounted.Damage.NativeCalls==1,"paid but unattached target remains native");
        }
        using(var f=new Fixture())
        {
            Prepare(f);Guard(f);
            Managers.Inst.game.state=Game.State.Other;
            try { f.Damage.ReceiveDamage(1,null,DamageSource.Ogre); }
            finally { Managers.Inst.game.state=Game.State.Playing; }
            check(f.Damage.NativeCalls==1 && Wear(f)==90,"nonPlaying passes native without wear");
            Time.timeScale=0;
            try { f.Damage.ReceiveDamage(1,null,DamageSource.Ogre); }
            finally { Time.timeScale=1; }
            check(f.Damage.NativeCalls==2 && Wear(f)==90,"paused behavior remains unchanged");
            var world=Managers.Inst.world.gameLayer;
            Managers.Inst.world.gameLayer=new GameObject("foreign world").transform;
            try { f.Damage.ReceiveDamage(1,null,DamageSource.Ogre); }
            finally { Managers.Inst.world.gameLayer=world; }
            check(f.Damage.NativeCalls==3 && Wear(f)==90,"old world target cannot protect in a different current world");
            HeavyShieldActorVisuals.Suspend(f.Archer,f.Handle.Life);
            f.Damage.ReceiveDamage(1,null,DamageSource.Ogre);
            check(f.Damage.NativeCalls==4 && Wear(f)==90,"suspended visual ownership cannot authorize protection");
        }
    }
    static void Break(Action<bool,string> check)
    {
        using var f=new Fixture();Prepare(f);Guard(f);
        for(int i=0;i<90;i++) { Advance();f.Damage.ReceiveDamage(1,null,DamageSource.Ogre); }
        check(f.Damage.NativeHits==0 && Wear(f)==0,"ninetieth charged hit blocked before pending demote");
        f.Damage.ReceiveDamage(1,null,DamageSource.Fire);
        check(f.Damage.NativeHits==1,"pending broken career cannot block ninety-first hit");
    }
    static void ForeignWitness(Action<bool,string> check)
    {
        foreach(bool portal in new[]{false,true})
        {
            using var f=new Fixture();Prepare(f);Guard(f);
            var go=Source("ordinary troll",6);var troll=go.AddComponent<Troll>();troll.Type=EnemyType.TrollWeak;go.AddComponent<Damageable>();
            HeavyShieldCombat.ObserveTrollIntent(troll,f.Damage);
            using var outer=HeavyShieldCombat.EnterTroll(troll,f.Damage);
            var source=Source(portal?"portal":"different troll",7);
            if(!portal) { source.AddComponent<Troll>().Type=EnemyType.TrollWeak;source.AddComponent<Damageable>(); }
            f.Damage.ReceiveDamage(1,source,portal?DamageSource.Ogre:DamageSource.Troll);
            check(f.Damage.NativeHits==0,"foreign source still protected");
            check(outer is HeavyShieldTrollScope scope && !scope.Blocked,"foreign source cannot acquire outer witness");
        }
    }
    static void PersistenceFailure(Action<bool,string> check)
    {
        foreach(bool throws in new[]{false,true})
        {
            using var f=new Fixture();Prepare(f);Guard(f);
            HeavyShieldIdentity.FailUpdate=!throws;HeavyShieldIdentity.ThrowCombatUpdate=throws;
            try { f.Damage.ReceiveDamage(1,null,DamageSource.Ogre);check(f.Damage.NativeHits==0,"successful core block survives persistence failure, throws="+throws); }
            finally { HeavyShieldIdentity.FailUpdate=false;HeavyShieldIdentity.ThrowCombatUpdate=false; }
            f.Damage.ReceiveDamage(1,null,DamageSource.Ogre);
            check(f.Damage.NativeHits==1,"failed persistence suspends later protection");
        }
    }
}
