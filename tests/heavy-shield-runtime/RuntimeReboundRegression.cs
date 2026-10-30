using System;
using System.Reflection;
using KingdomEnhancedMod;
using UnityEngine;

// Host-boundary regression for the ApplyCollisionDamage rebound witness. It links the
// production combat/hook classes and drives the same Prefix -> ReceiveDamage -> native
// pause -> Postfix sequence through the real patch methods (Postfix is private, so the
// synthetic Harmony boundary is invoked by reflection). It does not qualify live game
// timing or actual interop wrapper behavior.
internal static class RuntimeReboundRegression
{
    private static Action<bool, string> _check;
    private static MethodInfo _postfix;
    private static MethodInfo _finalizer;
    private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Static;

    private static void Check(bool ok, string label) => _check(ok, label);

    internal static void Run(Action<bool, string> check)
    {
        _check = check;
        _postfix = typeof(HeavyShieldTrollImpact).GetMethod("Postfix", Hidden);
        _finalizer = typeof(HeavyShieldTrollImpact).GetMethod("Finalizer", Hidden);
        Console.WriteLine("rebound hook installed=" + (_postfix != null) + " finalizer=" + (_finalizer != null));

        PositiveRight();
        PositiveLeft();
        WrongNativeDirection();
        FacingDiffersFromHomeSide();
        CompletedUnblockedScopeStaysConsumed();
        SameSecondSecondBlock();
        NestedScopes();
        TwoSequentialImpactsSameFrame();
        RearPass();
        SpecialTroll();
        FailureCases();
        Console.WriteLine("PASS HeavyShield rebound boundary: right/left replace, failures preserve native pause");
    }

    private static void PositiveRight()
    {
        using var f = new Fixture();
        Prepare(f, "right");
        var troll = TrollAt(5.5f);
        troll.transform.localScale = new Vector3(1.5f, -2f, 1f);
        troll.knightBounceForce = new Vector2(2f, 3f);
        var trollPosition = troll.transform.position;
        var targetPosition = f.Go.transform.position;
        var trollScale = troll.transform.localScale;
        IDisposable state;
        var pause = BlockingImpact(f, troll, out state);
        using (state)
        {
            Check(f.Damage.NativeHits == 0, "right same-impact block holds");
            var native = pause.velocityAfter;
            bool installed = InvokePostfix(troll, f.Damage, state);
            if (!installed)
            {
                Check(SameBits(pause.velocityAfter.x, native.x) && SameBits(pause.velocityAfter.y, native.y),
                    "baseline without postfix leaves the native 0.8*velocity pause unchanged");
                Console.WriteLine("baseline rebound absent; native pause stays " + native.x + "," + native.y);
            }
            Check(installed, "ApplyCollisionDamage postfix installed");
            Check(HeavyShieldNativeHooks.Installed, "impact postfix is part of the installed hook set");
            Expect(pause, 3.6f, -7.2f, "right rebound uses force*scale*1.2 and away X");
            Check(pause.VectorWrites == 1, "right one vector write");
            Check(ReferenceEquals(troll._pauseMotion, pause), "right reuses native pause instance");
            Check(troll.PushCalls == 0 && troll.PauseCalls == 0 && troll.CoroutineCalls == 0,
                "right no OnPush/PauseMotion/StartCoroutine");
            Check(troll.transform.position.x == trollPosition.x && troll.transform.position.y == trollPosition.y
                && f.Go.transform.position.x == targetPosition.x && f.Go.transform.position.y == targetPosition.y
                && troll.transform.localScale.x == trollScale.x && troll.transform.localScale.y == trollScale.y,
                "right no transform or scale write");
            InvokePostfix(troll, f.Damage, state);
            Expect(pause, 3.6f, -7.2f, "right repeated postfix never multiplies again");
            Check(pause.VectorWrites == 1, "right repeated postfix zero writes");
        }
    }

    private static void PositiveLeft()
    {
        using var f = new Fixture(side: HeavyShieldQuota.Side.Left);
        Prepare(f, "left");
        var troll = TrollAt(-5.5f);
        troll.transform.localScale = new Vector3(1.5f, -2f, 1f);
        troll.knightBounceForce = new Vector2(-2f, 3f);
        IDisposable state;
        var pause = BlockingImpact(f, troll, out state);
        using (state)
        {
            Check(f.Damage.NativeHits == 0, "left same-impact block holds");
            Check(InvokePostfix(troll, f.Damage, state), "left postfix installed");
            Expect(pause, -3.6f, -7.2f, "left X leaves defender while Y keeps signed scale");
            Check(pause.VectorWrites == 1, "left one vector write");
        }
    }

    private static void WrongNativeDirection()
    {
        using var f = new Fixture();
        Prepare(f, "direction");
        var troll = TrollAt(5.5f);
        troll.transform.localScale = new Vector3(1.5f, -2f, 1f);
        // An inward native vector is unknown configuration; do not invent its direction.
        troll.knightBounceForce = new Vector2(-2f, 3f);
        IDisposable state;
        var pause = BlockingImpact(f, troll, out state);
        using (state)
        {
            Check(InvokePostfix(troll, f.Damage, state), "direction postfix installed");
            Expect(pause, 2.5f, 3.5f, "inward native X leaves the original pause unchanged");
            Check(pause.VectorWrites == 0, "inward native vector is not normalized");
        }
    }

    private static void FacingDiffersFromHomeSide()
    {
        using var f = new Fixture(side: HeavyShieldQuota.Side.Left);
        Prepare(f, "turned shield");
        // The production SetFacing path is also used by repair and banner orders.
        var entries = (System.Collections.IDictionary)typeof(HeavyShieldRuntime)
            .GetField("Entries", Hidden).GetValue(null);
        var entry = entries[f.Handle.GoId];
        typeof(HeavyShieldRuntime).GetMethod("SetFacing", Hidden)
            .Invoke(null, new[] { entry, (object)1 });
        var troll = TrollAt(f.Go.transform.position.x + .5f);
        troll.knightBounceForce = new Vector2(2f, 3f);
        var pause = BlockingImpact(f, troll, out var state);
        using (state)
        {
            Check(f.Damage.NativeHits == 0, "turned left-side career blocks on its actual right-facing front");
            InvokePostfix(troll, f.Damage, state);
            Expect(pause, 2.4f, 3.6f, "successful contact direction owns rebound, not home side");
        }
    }

    private static void CompletedUnblockedScopeStaysConsumed()
    {
        using var f = new Fixture();
        Prepare(f, "late block");
        var troll = TrollAt(5.5f);
        troll.knightBounceForce = new Vector2(2f, 3f);
        HeavyShieldCombat.ObserveTrollIntent(troll, f.Damage);
        using var scope = HeavyShieldCombat.EnterTroll(troll, f.Damage);
        InvokePostfix(troll, f.Damage, scope); // Normal return without a block ends this receipt.
        f.Damage.ReceiveDamage(1, troll.gameObject, DamageSource.Troll);
        var pause = NativePause(troll);
        InvokePostfix(troll, f.Damage, scope);
        Check(f.Damage.NativeHits == 0 && pause.VectorWrites == 0,
            "completed unblocked receipt cannot acquire a late block and rebound");
    }

    private static void SameSecondSecondBlock()
    {
        using var f = new Fixture();
        Prepare(f, "same-second");
        var troll = TrollAt(5.5f);
        troll.knightBounceForce = new Vector2(2f, 3f);
        float now = Time.time;
        IDisposable first;
        var pause1 = BlockingImpact(f, troll, out first);
        using (first)
        {
            Check(f.Damage.NativeHits == 0, "same-second first block holds");
            Check(InvokePostfix(troll, f.Damage, first), "same-second first postfix installed");
            Expect(pause1, 2.4f, 3.6f, "same-second first rebound");
        }
        IDisposable second;
        var pause2 = BlockingImpact(f, troll, out second);
        using (second)
        {
            Check(Time.time == now, "same-second second impact keeps the same game time");
            Check(f.Damage.NativeHits == 0, "same-second second block holds without durability proof");
            Check(InvokePostfix(troll, f.Damage, second), "same-second second postfix installed");
            Expect(pause2, 2.4f, 3.6f, "same-second second rebound");
            int durability = State(f).Durability;
            if (HasNaturalSecondPolicy())
                Check(durability == HeavyShieldBlockPolicy.InitialDurability - 1,
                    "same-second second block stays free under the natural-second policy");
            else
                Console.WriteLine("B-only staging: natural-second policy absent; A+B durability assertion deferred");
        }
    }

    private static void NestedScopes()
    {
        using var f = new Fixture();
        Prepare(f, "nested");
        var troll = TrollAt(5.5f);
        troll.knightBounceForce = new Vector2(2f, 3f);
        HeavyShieldCombat.ObserveTrollIntent(troll, f.Damage);
        var outer = HeavyShieldCombat.EnterTroll(troll, f.Damage);
        f.Damage.ReceiveDamage(1, troll.gameObject, DamageSource.Troll);
        Check(f.Damage.NativeHits == 0, "nested outer block holds");
        var pause1 = NativePause(troll);
        HeavyShieldCombat.ObserveTrollIntent(troll, f.Damage);
        var inner = HeavyShieldCombat.EnterTroll(troll, f.Damage);
        f.Damage.ReceiveDamage(1, troll.gameObject, DamageSource.Troll);
        Check(f.Damage.NativeHits == 0, "nested inner block holds");
        var pause2 = NativePause(troll);
        using (inner)
        {
            Check(InvokePostfix(troll, f.Damage, inner), "nested inner postfix installed");
            Expect(pause2, 2.4f, 3.6f, "nested inner scope consumes its own witness");
            Check(pause1.VectorWrites == 0, "nested inner never borrows outer pause");
        }
        var pause3 = NativePause(troll);
        using (outer)
        {
            Check(InvokePostfix(troll, f.Damage, outer), "nested outer postfix installed");
            Expect(pause3, 2.4f, 3.6f, "nested outer scope consumes only its own witness");
            Check(pause2.VectorWrites == 1, "nested outer leaves inner pause untouched");
        }
    }

    private static void TwoSequentialImpactsSameFrame()
    {
        using var f = new Fixture();
        Prepare(f, "two impacts");
        var a = TrollAt(5.5f);
        var b = TrollAt(6.0f);
        a.knightBounceForce = new Vector2(2f, 3f);
        b.knightBounceForce = new Vector2(2f, 3f);
        float now = Time.time;
        IDisposable first;
        var pauseA = BlockingImpact(f, a, out first);
        using (first)
        {
            Check(InvokePostfix(a, f.Damage, first), "two-impact first postfix");
            Expect(pauseA, 2.4f, 3.6f, "two-impact first rebound");
        }
        IDisposable second;
        var pauseB = BlockingImpact(f, b, out second);
        using (second)
        {
            Check(Time.time == now, "two impacts remain in one frame time");
            Check(InvokePostfix(b, f.Damage, second), "two-impact second postfix");
            Expect(pauseB, 2.4f, 3.6f, "two-impact second rebound");
            Check(a.PushCalls + b.PushCalls + a.PauseCalls + b.PauseCalls + a.CoroutineCalls + b.CoroutineCalls == 0,
                "two impacts add no native calls");
        }
    }

    private static void RearPass()
    {
        using var f = new Fixture();
        Prepare(f, "rear");
        var troll = TrollAt(4.0f);
        troll.knightBounceForce = new Vector2(2f, 3f);
        IDisposable state;
        var pause = BlockingImpact(f, troll, out state);
        using (state)
        {
            Check(f.Damage.NativeHits == 1, "rear hit reaches native damage");
            InvokePostfix(troll, f.Damage, state);
            Check(pause.VectorWrites == 0, "rear pass never rebounds");
        }
    }

    private static void SpecialTroll()
    {
        using var f = new Fixture();
        Prepare(f, "special");
        var troll = TrollAt(5.5f);
        troll.Type = EnemyType.Boss;
        troll.knightBounceForce = new Vector2(2f, 3f);
        IDisposable state;
        var pause = BlockingImpact(f, troll, out state);
        using (state)
        {
            Check(f.Damage.NativeHits == 1, "special troll reaches native damage");
            InvokePostfix(troll, f.Damage, state);
            Check(pause.VectorWrites == 0, "special troll never rebounds");
        }
    }

    private static void FailureCases()
    {
        NoChange("null pause", (troll, _) => troll._pauseMotion = null);
        NoChange("foreign wrapper", (troll, _) => troll._pauseMotion = new Il2CppSystem.Collections.IEnumerator());
        NoChange("foreign owner", (troll, pause) => pause.__4__this = TrollAt(6.2f));
        NoChange("wrong state 0", (_, pause) => pause.__1__state = 0);
        NoChange("wrong state -1", (_, pause) => pause.__1__state = -1);
        NoChange("wrong duration", (_, pause) => pause.duration = .2f);
        NoChange("nonfinite duration", (_, pause) => pause.duration = float.NaN);
        NoChange("nonfinite native vector", (_, pause) => pause.velocityAfter = new Vector2(float.NaN, 1f));
        NoChange("zero force x", (troll, _) => troll.knightBounceForce = new Vector2(0f, 3f));
        NoChange("nonfinite force", (troll, _) => troll.knightBounceForce = new Vector2(float.PositiveInfinity, 3f));
        NoChange("nonfinite force y", (troll, _) => troll.knightBounceForce = new Vector2(2f, float.NaN));
        NoChange("zero scale x", (troll, _) => troll.transform.localScale = new Vector3(0f, 1f, 1f));
        NoChange("nonfinite scale y", (troll, _) => troll.transform.localScale = new Vector3(1f, float.PositiveInfinity));
        NoChange("nonfinite source x", (troll, _) => troll.transform.position = new Vector3(float.NaN, 0f));
        NoChange("same source target x", (troll, _) => troll.transform.position = new Vector3(4.75f, 0f));
        NoChange("dead source", (troll, _) => troll.GetComponent<Damageable>().isDead = true);
        NoChange("target moved behind", (troll, _) => troll.transform.position = new Vector3(4.0f, 0f));

        // Arrow blocks never borrow the enclosing Troll scope's witness.
        {
            using var f = new Fixture();
            Prepare(f, "arrow");
            var troll = TrollAt(5.5f);
            HeavyShieldCombat.ObserveTrollIntent(troll, f.Damage);
            var outer = HeavyShieldCombat.EnterTroll(troll, f.Damage);
            var arrow = ArrowFrom(-2f);
            using (var hit = HeavyShieldCombat.EnterArrowHit(arrow))
            using (var damage = HeavyShieldCombat.EnterArrowDamage(arrow, f.Damage))
                f.Damage.ReceiveDamage(1, arrow.archer, DamageSource.GreedProjectile);
            Check(f.Damage.NativeHits == 0, "arrow block is still a block");
            var pause = NativePause(troll);
            using (outer)
            {
                InvokePostfix(troll, f.Damage, outer);
                Check(pause.VectorWrites == 0, "arrow block never borrows outer Troll witness");
            }
        }

        // The entry pause pointer is the old native instance; no replacement means no write.
        {
            using var f = new Fixture();
            Prepare(f, "old pause");
            var troll = TrollAt(5.5f);
            var old = NativePause(troll);
            old.VectorWrites = 0;
            HeavyShieldCombat.ObserveTrollIntent(troll, f.Damage);
            var state = HeavyShieldCombat.EnterTroll(troll, f.Damage);
            f.Damage.ReceiveDamage(1, troll.gameObject, DamageSource.Troll);
            Check(f.Damage.NativeHits == 0, "old-pause block holds");
            using (state)
            {
                InvokePostfix(troll, f.Damage, state);
                Check(old.VectorWrites == 0 && ReferenceEquals(troll._pauseMotion, old),
                    "unchanged entry pause is never rewritten");
            }
        }

        // Finalizer only disposes and preserves the original exception; no rebound in finally.
        {
            using var f = new Fixture();
            Prepare(f, "finalizer");
            var troll = TrollAt(5.5f);
            IDisposable state;
            var pause = BlockingImpact(f, troll, out state);
            var exception = new InvalidOperationException("controlled native failure");
            try
            {
                var returned = _finalizer == null ? null : (Exception)_finalizer.Invoke(null, new object[] { exception, state });
                Check(ReferenceEquals(returned, exception), "finalizer returns the original exception");
                InvokePostfix(troll, f.Damage, state);
                Check(pause.VectorWrites == 0, "exception finalizer never emits rebound");
            }
            finally { state.Dispose(); }
        }

        // Campaign generation change invalidates the prefix witness.
        {
            using var f = new Fixture();
            Prepare(f, "campaign");
            var troll = TrollAt(5.5f);
            IDisposable state;
            var pause = BlockingImpact(f, troll, out state);
            long generation = HeavyShieldIdentity.Generation;
            HeavyShieldIdentity.Generation++;
            try
            {
                using (state)
                {
                    InvokePostfix(troll, f.Damage, state);
                    Check(pause.VectorWrites == 0, "campaign generation change never rebounds");
                }
            }
            finally { HeavyShieldIdentity.Generation = generation; }
        }

        // Same-life proof fails after source/target pool life reuse.
        {
            using var f = new Fixture();
            Prepare(f, "source reuse");
            var troll = TrollAt(5.5f);
            IDisposable state;
            var pause = BlockingImpact(f, troll, out state);
            troll.gameObject.SetActive(false);
            troll.gameObject.SetActive(true);
            using (state)
            {
                InvokePostfix(troll, f.Damage, state);
                Check(pause.VectorWrites == 0, "reused source life never rebounds");
            }
        }
        {
            using var f = new Fixture();
            Prepare(f, "target reuse");
            var troll = TrollAt(5.5f);
            IDisposable state;
            var pause = BlockingImpact(f, troll, out state);
            f.Go.SetActive(false);
            f.Go.SetActive(true);
            using (state)
            {
                InvokePostfix(troll, f.Damage, state);
                Check(pause.VectorWrites == 0, "reused target life never rebounds");
            }
        }

    }

    private static void NoChange(string label, Action<Troll, Troll._PauseMotion_d__114> mutate, bool expectBlock = true)
    {
        using var f = new Fixture();
        Prepare(f, label);
        var troll = TrollAt(5.5f);
        troll.knightBounceForce = new Vector2(2f, 3f);
        IDisposable state;
        var pause = BlockingImpact(f, troll, out state);
        mutate(troll, pause);
        var before = pause.velocityAfter;
        int writes = pause.VectorWrites;
        using (state)
        {
            Check(f.Damage.NativeHits == (expectBlock ? 0 : 1), label + " native block outcome");
            InvokePostfix(troll, f.Damage, state);
            Check(pause.VectorWrites == writes && SameBits(pause.velocityAfter.x, before.x)
                && SameBits(pause.velocityAfter.y, before.y),
                label + " leaves native pause vector unchanged");
        }
    }

    private static bool HasNaturalSecondPolicy()
        => typeof(HeavyShieldBlockPolicy).GetField("ChargeIntervalSeconds",
            BindingFlags.NonPublic | BindingFlags.Static) != null;

    private static HeavyShieldSavedCombatState State(Fixture f)
    {
        Check(HeavyShieldRuntime.CaptureCombat(f.Handle, out var state), "capture exact rebound career");
        return state;
    }

    private static void Prepare(Fixture f, string label)
    {
        if (f.Handle.Side == HeavyShieldQuota.Side.Left) f.Go.transform.position = new Vector3(-4.75f, 0f);
        Check(f.Attach(new HeavyShieldSavedCombatState(HeavyShieldBlockPolicy.InitialDurability, false, false)), label + " attach");
        HeavyShieldRuntime.Tick();
        for (int i = 0; i < 8; i++) Step(f);
        Check(HeavyShieldActorVisuals.GuardReady(f.Archer, f.Handle.Life), label + " guard");
    }

    private static void Step(Fixture f, float dt = .25f)
    {
        Time.deltaTime = dt;
        Time.time += dt;
        Time.unscaledTime += dt;
        Time.unscaledTimeAsDouble += dt;
        Time.frameCount++;
        HeavyShieldActorVisuals.Tick(f.Handle.GoId);
        HeavyShieldRuntime.Tick();
    }

    private static Troll TrollAt(float x)
    {
        var go = new GameObject("troll");
        go.transform.SetParent(Managers.Inst.world.gameLayer, false);
        go.transform.position = new Vector3(x, 0f);
        var troll = go.AddComponent<Troll>();
        troll.Type = EnemyType.TrollWeak;
        go.AddComponent<Damageable>();
        return troll;
    }

    private static Arrow ArrowFrom(float vx)
    {
        var owner = new GameObject("enemy archer");
        owner.transform.SetParent(Managers.Inst.world.gameLayer, false);
        var enemy = owner.AddComponent<GreedArcher>();
        enemy.Type = EnemyType.Archer;
        owner.AddComponent<Damageable>();
        var go = new GameObject("arrow");
        go.transform.SetParent(Managers.Inst.world.gameLayer, false);
        var arrow = go.AddComponent<Arrow>();
        arrow._rigidbody = go.AddComponent<Rigidbody2D>();
        arrow._rigidbody.velocity = new Vector2(vx, 0f);
        arrow.archer = owner;
        return arrow;
    }

    private static Troll._PauseMotion_d__114 BlockingImpact(Fixture f, Troll troll, out IDisposable state)
    {
        HeavyShieldCombat.ObserveTrollIntent(troll, f.Damage);
        state = HeavyShieldCombat.EnterTroll(troll, f.Damage);
        f.Damage.ReceiveDamage(1, troll.gameObject, DamageSource.Troll);
        return NativePause(troll);
    }

    private static Troll._PauseMotion_d__114 NativePause(Troll troll)
    {
        var pause = new Troll._PauseMotion_d__114
        {
            __4__this = troll,
            __1__state = 1,
            duration = 1f / 15f,
            velocityAfter = new Vector2(2.5f, 3.5f),
        };
        pause.VectorWrites = 0; // the native 0.8*velocity construction is not a rebound write
        troll._pauseMotion = pause;
        return pause;
    }

    private static bool InvokePostfix(Troll troll, Damageable target, IDisposable state)
    {
        if (_postfix == null) return false;
        _postfix.Invoke(null, new object[] { troll, target, state });
        return true;
    }

    private static void Expect(Troll._PauseMotion_d__114 pause, float x, float y, string label)
        => Check(Near(pause.velocityAfter.x, x) && Near(pause.velocityAfter.y, y),
            label + $" (got {pause.velocityAfter.x}, {pause.velocityAfter.y}; expected {x}, {y})");

    private static bool Near(float a, float b) => Math.Abs(a - b) <= 1e-5f;
    private static bool SameBits(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);
}
