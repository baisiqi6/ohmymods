using System;
using System.Collections.Generic;
using System.Reflection;
using KingdomEnhancedMod;
using UnityEngine;

[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace KingdomArcherOptions.Combat.Tests
{
    /// <summary>
    /// Invokes the production Harmony wrapper methods exactly the way Harmony does
    /// (attribute-declared private statics, prefix → original → postfix; original throws →
    /// finalizers run and the postfix is skipped, then the exception is rethrown).
    /// Result flags are recorded so tests can assert which side ran.
    /// </summary>
    internal static class PatchBridge
    {
        private const BindingFlags PatchFlags = BindingFlags.NonPublic | BindingFlags.Static;

        internal static void ArrowFired(ArrowAttack data, GameObject source, Vector3 position, bool perfect, Vector2 force)
            => Invoke(typeof(ArrowAttack_FireArrowInternal_Scatter_Patch), "Postfix", data, source, position, perfect, force);

        /// <summary>Unity activation → Harmony prefix → native OnEnable body, for pooled/reused components.</summary>
        internal static void RaiseNativeOnEnable(Component component)
        {
            if (component is Arrow arrow)
            {
                Invoke(typeof(Arrow_OnEnable_ExtraLedger_Patch), "Prefix", arrow);
                arrow._hasHit = false;          // modeled native body (harmony prefix runs first)
            }
            else if (component is Archer archer) Invoke(typeof(Archer_OnEnable_RateLifetime_Patch), "Prefix", archer);
        }

        internal static void RaiseArcherOnEnable(Archer archer)
            => Invoke(typeof(Archer_OnEnable_RateLifetime_Patch), "Prefix", archer);

        internal static UpdateRun RunArcherUpdate(Archer archer, Action preNative = null, Action postNative = null, bool throwFromNative = false)
        {
            UpdateRun run = new UpdateRun();
            Invoke(typeof(Archer_Update_RateCadence_Patch), "Prefix", archer);
            Exception thrown = null;
            try
            {
                preNative?.Invoke();
                NativeUpdateBody(archer);
                postNative?.Invoke();
                if (throwFromNative) throw new InvalidOperationException("simulated native Archer.Update failure");
                Invoke(typeof(Archer_Update_RateCadence_Patch), "Postfix", archer);
                run.PostfixRan = true;
            }
            catch (Exception exception)
            {
                thrown = exception;
            }
            finally
            {
                // Harmony runs finalizers on success (null exception) and on failure, then rethrows the original.
                Invoke(typeof(Archer_Update_RateCadence_Patch), "Finalizer", archer, thrown);
                run.FinalizerRan = true;
                run.FinalizerException = thrown;
            }
            run.Thrown = thrown;
            return run;
        }

        /// <summary>Modeled native Archer.Update body: player-control early return + cooldown countdown.</summary>
        internal static void NativeUpdateBody(Archer archer)
        {
            if (archer._unitController != null) return;
            if (archer._cooldown > 0f) archer._cooldown -= Time.deltaTime;
            archer.NativeBodyRan = true;
        }

        /// <summary>MoveNext prefix of the temporary-cadence patch (mutates its ref __state).</summary>
        internal static PatchArcher_Options.CadenceBorrow ShootMoveNextEnter(Archer._Shoot_d__225 iterator)
        {
            object[] args = { default(PatchArcher_Options.CadenceBorrow), iterator };
            Invoke(typeof(Archer_ShootCoroutine_OptionsCadence_Patch), "Prefix", args);
            return (PatchArcher_Options.CadenceBorrow)args[0];
        }

        /// <summary>MoveNext finalizer: always invoked (success and exception); null exception = success.</summary>
        internal static void ShootMoveNextExit(PatchArcher_Options.CadenceBorrow state, Archer._Shoot_d__225 iterator, Exception exception)
            => Invoke(typeof(Archer_ShootCoroutine_OptionsCadence_Patch), "Finalizer", state, exception, iterator);

        private static void Invoke(Type patchClass, string methodName, params object[] args)
        {
            MethodInfo method = patchClass.GetMethod(methodName, PatchFlags);
            if (method == null) throw new MissingMethodException(patchClass.FullName + "." + methodName);
            method.Invoke(null, args);
        }
    }

    internal sealed class UpdateRun
    {
        internal bool PostfixRan;
        internal bool FinalizerRan;
        internal Exception Thrown;
        internal Exception FinalizerException;
    }

    /// <summary>One modeled Shoot-coroutine MoveNext: DL prefix → our prefix → native reads → our finalizer → DL finalizer.</summary>
    internal sealed class CoroutineRun
    {
        internal float ReadPrep;
        internal Vector2 ReadInterval;
        internal Vector2 ReadFormation;
        internal float AfterOurReturn;
        internal Exception Thrown;
        internal bool BorrowEntered;
    }

    /// <summary>
    /// Model of the existing Deadlands interval patch on the same native target
    /// (Archer._Shoot_d__225.MoveNext, default priority 400): its prefix temporarily halves
    /// prep/interval and its finalizer restores its own snapshot unconditionally.
    /// </summary>
    internal static class DeadlandsSim
    {
        internal struct DlBoost
        {
            internal bool Applied;
            internal IntPtr ArcherPtr;
            internal float Prep;
            internal Vector2 Interval;
            internal Vector2 Formation;
        }

        internal static DlBoost Prefix(Archer archer)
        {
            DlBoost state = default;
            if (archer == null) return state;
            state = new DlBoost
            {
                Applied = true,
                ArcherPtr = archer.Pointer,
                Prep = archer.shootPrepTime,
                Interval = archer._shootIntervalRange,
                Formation = archer._shootIntervalRangeFormation
            };
            archer.shootPrepTime = state.Prep * 0.5f;
            archer._shootIntervalRange = Halve(state.Interval);
            archer._shootIntervalRangeFormation = Halve(state.Formation);
            return state;
        }

        internal static void Finalizer(DlBoost state, Archer archer)
        {
            if (!state.Applied || archer == null || archer.Pointer != state.ArcherPtr) return;
            archer.shootPrepTime = state.Prep;
            archer._shootIntervalRange = state.Interval;
            archer._shootIntervalRangeFormation = state.Formation;
        }

        internal static Vector2 Halve(Vector2 value) => new Vector2(value.x * 0.5f, value.y * 0.5f);
    }

    internal sealed class FakeUnitController : IUnitController
    {
    }

    /// <summary>Minimal native world for one test: layer, arrow prefab + pool, archers, modeled native calls.</summary>
    internal sealed class Fixture
    {
        internal readonly GameObject LayerGo;
        internal readonly Managers ManagersInst;
        internal readonly GameObject PrefabGo;
        internal readonly Arrow PrefabArrow;
        internal readonly ArrowAttack Data = new ArrowAttack();
        internal readonly BiomeData Biome = new BiomeData();

        private readonly List<ShotRecord> _shots = new List<ShotRecord>();

        /// <summary>Handout window of one modeled shot: index Start is the main arrow, (Start, End) its extras.</summary>
        private sealed class ShotRecord
        {
            internal Pool Pool;
            internal Arrow Main;
            internal int Start;
            internal int End;
        }

        internal Fixture(bool registerPool = true, bool prefabRigidbody = true, bool prefabSoftSim = true, int poolCapacity = int.MaxValue)
        {
            PatchArcher_Options.ResetForTests();
            PatchArcher_Options.SeedForTests(20260914);
            HeroArcherRuntime.ResetForTests();
            HeroArcherCombat.ResetLogForTests();
            HeroArcherRange.ResetForTests();
            UnityEngine.Object.DestroyCalls = 0;
            UnityEngine.Object.Destroyed.Clear();
            FakeOps.Reset();
            Pool.ClearForTests();
            KingdomEnhancedPlugin.Instance = new KingdomEnhancedPlugin();

            NetworkBigBoss.HasWorldAuth = true;
            NetworkBigBoss.IsOnline = false;
            NetworkBigBoss.HasClientCaughtUp = true;
            NetworkBigBoss.IsClientPresent = false;

            Time.deltaTime = 1f / 60f;
            Time.unscaledTime = 10f;
            Time.timeScale = 1f;
            Time.frameCount = 1;

            ArcherOptionsScope.Active = true;
            ModConfig.ArcherScatterEnabled.Value = false;
            ModConfig.ArcherVolleyCount.Value = 3;
            ModConfig.ArcherRateEnabled.Value = false;
            ModConfig.ArcherRateMultiplier.Value = 1.5f;

            LayerGo = new GameObject("gameLayer");
            ArcherOptionsScope.Layer = LayerGo.transform;
            ScatterArrowTint.Tick();

            ManagersInst = new Managers();
            ManagersInst.world = new World { gameLayer = LayerGo.transform };
            SingletonMonoBehaviour<Managers>.Inst = ManagersInst;
            BiomeData.Current = Biome;

            PrefabGo = new GameObject("ArrowPrefab");
            PrefabGo.SetActive(false);
            PrefabArrow = PrefabGo.AddComponent<Arrow>();
            if (prefabRigidbody) PrefabGo.AddComponent<Rigidbody2D>();
            if (prefabSoftSim) PrefabGo.AddComponent<NetworkSoftSimulator>();
            Data._arrowPrefab = PrefabArrow;

            if (registerPool) Pool.RegisterForTests(PrefabGo, MakeArrowInstance, poolCapacity);
        }

        internal Pool PoolRef => Pool.ForPrefab(PrefabGo);

        internal Arrow MakeArrowInstance()
        {
            GameObject go = new GameObject("Arrow");
            go.transform.SetParent(LayerGo.transform);
            go.AddComponent<Rigidbody2D>();
            go.AddComponent<NetworkSoftSimulator>();
            Arrow arrow = go.AddComponent<Arrow>();
            arrow._spriteRenderer = go.AddComponent<SpriteRenderer>();
            return arrow;
        }

        internal Archer NewArcher(bool currentLayer = true)
        {
            GameObject go = new GameObject("Archer");
            if (currentLayer) go.transform.SetParent(LayerGo.transform);
            Character character = go.AddComponent<Character>();
            Damageable damageable = go.AddComponent<Damageable>();
            Archer archer = go.AddComponent<Archer>();
            archer._character = character;
            archer._damageable = damageable;
            GameObject leader = new GameObject("Knight") { tag = "Knight" };
            leader.transform.SetParent(LayerGo.transform);
            archer._knight = leader.AddComponent<Knight>();
            archer._knight._damageable = leader.AddComponent<Damageable>();
            GameObject enemy = new GameObject("Enemy") { layer = 8, tag = "Enemy" };
            enemy.transform.SetParent(LayerGo.transform); enemy.AddComponent<Damageable>();
            archer._shootingTarget = enemy;
            return archer;
        }

        /// <summary>Modeled native <c>ArrowAttack.FireArrowInternal</c> followed by the production postfix.</summary>
        internal Arrow Shot(Archer archer, bool perfect = false, Vector2? force = null, ArrowAttack data = null, bool invokePostfix = true)
        {
            ArrowAttack attack = data ?? Data;
            Vector2 shotForce = force ?? new Vector2(12f, 2f);
            Vector3 position = new Vector3(0f, 1f, 0f);

            Pool pool = Pool.ForSpawn(attack._arrowPrefab != null ? attack._arrowPrefab.gameObject : null);
            int start = pool != null ? pool.HandedOut.Count : -1;

            Arrow main = Pool.Spawn<Arrow>(attack._arrowPrefab, position, Quaternion.identity, LayerGo.transform, true);
            if (main == null) return null;
            main.archer = archer.gameObject;
            if (perfect) main.PerfectShot();
            Rigidbody2D body = main.GetComponent<Rigidbody2D>();
            body.AddForce(shotForce, ForceMode2D.Impulse);
            if (NetworkBigBoss.HasWorldAuth && NetworkBigBoss.HasClientCaughtUp)
            {
                ByteBuffer.PrepWriteBuffer();
                ByteBuffer.Write(perfect);
                main.GetComponent<NetworkSoftSimulator>().SendVelocity(shotForce, body.angularVelocity);
            }

            if (invokePostfix) PatchBridge.ArrowFired(attack, archer.gameObject, position, perfect, shotForce);
            if (pool != null && start >= 0)
                _shots.Add(new ShotRecord { Pool = pool, Main = main, Start = start, End = pool.HandedOut.Count });
            return main;
        }

        /// <summary>Modeled native Shoot-coroutine step with the DL nesting order the contract fixes.</summary>
        internal CoroutineRun MoveNext(Archer archer, bool deadlands = false, bool throwFromNative = false, Action duringNative = null, Action<Archer._Shoot_d__225> beforeExit = null)
        {
            CoroutineRun run = new CoroutineRun();
            Archer._Shoot_d__225 iterator = archer.NewShootIterator();
            DeadlandsSim.DlBoost dl = deadlands ? DeadlandsSim.Prefix(archer) : default(DeadlandsSim.DlBoost);

            PatchArcher_Options.CadenceBorrow borrow = PatchBridge.ShootMoveNextEnter(iterator);
            run.BorrowEntered = borrow.Entered;

            Exception thrown = null;
            try
            {
                if (throwFromNative) throw new InvalidOperationException("simulated native Shoot.MoveNext failure");
                run.ReadPrep = archer.shootPrepTime;              // native constructs its Wait immediately
                run.ReadInterval = archer._shootIntervalRange;
                run.ReadFormation = archer._shootIntervalRangeFormation;
            }
            catch (Exception exception) { thrown = exception; }

            duringNative?.Invoke();                                   // inject failures before the finalizer runs
            beforeExit?.Invoke(iterator);                             // e.g. reassign the iterator owner

            PatchBridge.ShootMoveNextExit(borrow, iterator, thrown);   // our finalizer (Priority.First)
            run.AfterOurReturn = archer.shootPrepTime;
            if (deadlands) DeadlandsSim.Finalizer(dl, archer);         // DL finalizer (default priority)
            run.Thrown = thrown;
            return run;
        }

        internal List<Arrow> HandedOutArrows()
        {
            List<Arrow> arrows = new List<Arrow>();
            Pool pool = PoolRef;
            if (pool == null) return arrows;
            for (int i = 0; i < pool.HandedOut.Count; i++)
                if (pool.HandedOut[i] is Arrow arrow) arrows.Add(arrow);
            return arrows;
        }

        /// <summary>
        /// Extras of the shot that produced <paramref name="main"/>, taken from that shot's own handout window.
        /// Scanning the whole handout history would mix in later shots and earlier generations of reused instances.
        /// </summary>
        internal List<Arrow> Extras(Arrow main)
        {
            List<Arrow> extras = new List<Arrow>();
            ShotRecord record = null;
            for (int i = _shots.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(_shots[i].Main, main))
                {
                    record = _shots[i];
                    break;
                }
            }
            if (record == null) return extras;
            for (int i = record.Start + 1; i < record.End; i++)
                if (record.Pool.HandedOut[i] is Arrow arrow) extras.Add(arrow);
            return extras;
        }

        internal void Tick() => PatchArcher_Options.Tick();

        internal void AdvanceFrame(float seconds = 1f / 60f)
        {
            Time.frameCount++;
            Time.unscaledTime += seconds;
        }
    }
}
