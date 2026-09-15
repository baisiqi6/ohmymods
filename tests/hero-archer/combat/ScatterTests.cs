using System;
using System.Collections.Generic;
using System.Linq;
using KingdomEnhancedMod;
using UnityEngine;
using Xunit;

namespace KingdomArcherOptions.Combat.Tests
{
    public class ScatterTests
    {
        private static Fixture Scatter(int volley = 3, bool registerPool = true, bool prefabRigidbody = true, bool prefabSoftSim = true, int poolCapacity = int.MaxValue)
        {
            Fixture fixture = new Fixture(registerPool, prefabRigidbody, prefabSoftSim, poolCapacity);
            ModConfig.ArcherScatterEnabled.Value = true;
            ModConfig.ArcherVolleyCount.Value = volley;
            return fixture;
        }

        private static float AngleDegrees(Vector2 from, Vector2 to)
        {
            double a = Math.Atan2(from.y, from.x);
            double b = Math.Atan2(to.y, to.x);
            double delta = Math.Abs(b - a) * 180.0 / Math.PI;
            return (float)(delta > 180.0 ? 360.0 - delta : delta);
        }

        [Fact]
        public void AllOptionsOff_SpawnsOnlyTheNativeArrow()
        {
            Fixture f = new Fixture();
            Arrow main = f.Shot(f.NewArcher());

            Assert.NotNull(main);
            Assert.Empty(f.Extras(main));
            Assert.Single(f.HandedOutArrows());
            Assert.Equal(new Vector2(12f, 2f), main.GetComponent<Rigidbody2D>().Impulses.Single());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void MalformedCachedInstance_RemainsCountedUntilNativeRetirement(bool perfectFailure)
        {
            Fixture f = Scatter(5);
            Archer owner = f.NewArcher();
            Pool.RegisterForTests(f.PrefabGo, () =>
            {
                Arrow arrow = f.MakeArrowInstance();
                if (perfectFailure) arrow.ThrowOnPerfectShot = true;
                else arrow.gameObject.Components.Remove(arrow.GetComponent<Rigidbody2D>());
                return arrow;
            });
            for (int i = 0; i < 70; i++)
            {
                PatchBridge.ArrowFired(f.Data, owner.gameObject, Vector3.zero, perfectFailure, new Vector2(12, 2));
                f.AdvanceFrame(1f);
                f.Tick();
            }
            Assert.Equal(64, f.PoolRef.HandedOut.Count);
            Assert.Equal(0, FakeOps.Count("despawn"));
            Assert.All(f.PoolRef.HandedOut, arrow => Assert.True(arrow.gameObject.activeSelf));
        }

        [Fact]
        public void Enabled_SpawnsVolleyMinusOne_AndLeavesTheNativeShotUntouched()
        {
            Fixture f = Scatter(3);
            Archer archer = f.NewArcher();
            Vector2 baseForce = new Vector2(12f, 2f);

            Arrow main = f.Shot(archer, force: baseForce);
            List<Arrow> extras = f.Extras(main);

            Assert.Equal(2, extras.Count);
            Assert.Equal(3, f.HandedOutArrows().Count);
            // native arrow: exactly the native force, exactly one impulse, untouched by the module
            Assert.Equal(new[] { baseForce }, main.GetComponent<Rigidbody2D>().Impulses.ToArray());
            Assert.Same(archer.gameObject, main.archer);

            foreach (Arrow extra in extras)
            {
                Assert.Same(archer.gameObject, extra.archer);
                Assert.False(extra.Perfect);
                NetworkSoftSimulator sim = extra.GetComponent<NetworkSoftSimulator>();
                Vector2 impulse = extra.GetComponent<Rigidbody2D>().Impulses.Single();
                Assert.Equal(1, sim.SendCount);
                Assert.Equal(impulse, sim.LastVelocity);

                float ratio = impulse.magnitude / baseForce.magnitude;
                Assert.InRange(ratio, 0.9f - 1e-4f, 1.1f + 1e-4f);
                Assert.InRange(AngleDegrees(baseForce, impulse), 0f, 10f + 1e-3f);
            }

            // native velocity sequence for the last spawn: prep -> write(perfect) -> send
            IReadOnlyList<string> ops = FakeOps.All;
            Assert.Equal(new[] { "prep", "write:False", "write-byte:83", "write-byte:67", "write-byte:84", "write-byte:49", "write-byte:1", "send" }, ops.Skip(ops.Count - 8).ToArray());
            Assert.Equal(Color.white, main._spriteRenderer.color);
            Assert.All(extras, extra => Assert.NotEqual(Color.white, extra._spriteRenderer.color));
        }

        [Fact]
        public void PerfectShot_PropagatesPerfectStateToExtras()
        {
            Fixture f = Scatter(2);
            Arrow main = f.Shot(f.NewArcher(), perfect: true);

            Assert.True(main.Perfect);
            List<Arrow> extras = f.Extras(main);
            Assert.Single(extras);
            Assert.True(extras[0].Perfect);
            Assert.Contains("write:True", FakeOps.All);
        }

        [Theory]
        [InlineData(9, 2)]
        [InlineData(5, 2)]
        [InlineData(4, 2)]
        [InlineData(3, 2)]
        [InlineData(2, 1)]
        [InlineData(1, 0)]
        [InlineData(0, 0)]
        [InlineData(-2, 0)]
        public void VolleyCount_IsTotalArrowsIncludingOriginal_ClampedTo1Through3(int configured, int expectedExtras)
        {
            Fixture f = Scatter(configured);
            Arrow main = f.Shot(f.NewArcher());

            Assert.Equal(expectedExtras, f.Extras(main).Count);
        }

        [Fact]
        public void WorldScopeOff_SpawnsNoExtras()
        {
            Fixture f = Scatter(3);
            ArcherOptionsScope.Active = false;

            Arrow main = f.Shot(f.NewArcher());

            Assert.Empty(f.Extras(main));
        }

        [Fact]
        public void ArcherOutsideTheCurrentWorldLayer_SpawnsNoExtras()
        {
            Fixture f = Scatter(3);
            Arrow main = f.Shot(f.NewArcher(currentLayer: false));

            Assert.Empty(f.Extras(main));
        }

        [Fact]
        public void ClientWithoutWorldAuthority_SpawnsNoExtrasAndSendsNothing()
        {
            Fixture f = Scatter(3);
            NetworkBigBoss.HasWorldAuth = false;
            NetworkBigBoss.IsOnline = true;
            NetworkBigBoss.HasClientCaughtUp = true;

            Arrow main = f.Shot(f.NewArcher());

            Assert.Empty(f.Extras(main));
            Assert.Equal(0, FakeOps.Count("send"));
        }

        [Fact]
        public void OnlineClientNotReadyYet_SpawnsNoExtras()
        {
            Fixture f = Scatter(3);
            NetworkBigBoss.HasWorldAuth = true;
            NetworkBigBoss.IsOnline = true;
            NetworkBigBoss.HasClientCaughtUp = false;

            Arrow main = f.Shot(f.NewArcher());

            Assert.Empty(f.Extras(main));
            Assert.Equal(0, FakeOps.Count("send"));
        }

        [Fact]
        public void OnlineClientReady_SendsTheNativeVelocitySequenceForEachExtra()
        {
            Fixture f = Scatter(3);
            NetworkBigBoss.IsOnline = true;
            NetworkBigBoss.HasClientCaughtUp = true;

            Arrow main = f.Shot(f.NewArcher());
            List<Arrow> extras = f.Extras(main);

            Assert.Equal(2, extras.Count);
            Assert.Equal(1 + extras.Count, FakeOps.Count("send"));
            foreach (Arrow extra in extras)
                Assert.Equal(1, extra.GetComponent<NetworkSoftSimulator>().SendCount);
        }

        [Fact]
        public void NonArcherSource_SpawnsNoExtras()
        {
            Fixture f = Scatter(3);
            GameObject source = new GameObject("Peasant");
            source.transform.SetParent(f.LayerGo.transform);

            PatchBridge.ArrowFired(f.Data, source, new Vector3(0f, 1f, 0f), false, new Vector2(12f, 2f));

            Assert.Equal(0, FakeOps.Count("spawn"));
        }

        [Fact]
        public void UnresolvedPrefabPool_SkipsExtrasWithoutInstantiating()
        {
            Fixture f = Scatter(3, registerPool: false);
            Archer archer = f.NewArcher();

            PatchBridge.ArrowFired(f.Data, archer.gameObject, new Vector3(0f, 1f, 0f), false, new Vector2(12f, 2f));

            Assert.Equal(0, FakeOps.Count("spawn"));
        }

        [Fact]
        public void PrefabWithoutRigidbody_SkipsExtras()
        {
            Fixture f = Scatter(3, prefabRigidbody: false);
            Arrow main = f.Shot(f.NewArcher());

            Assert.Empty(f.Extras(main));
            Assert.Equal(new Vector2(12f, 2f), main.GetComponent<Rigidbody2D>().Impulses.Single());
        }

        [Fact]
        public void PrefabWithoutSoftSimulator_SkipsExtras()
        {
            Fixture f = Scatter(3, prefabSoftSim: false);
            Arrow main = f.Shot(f.NewArcher());

            Assert.Empty(f.Extras(main));
        }

        [Fact]
        public void ExtrasUseTheFiringScriptableObjectPrefab()
        {
            Fixture f = Scatter(3);
            GameObject boltPrefabGo = new GameObject("BoltPrefab");
            boltPrefabGo.SetActive(false);
            Arrow boltPrefab = boltPrefabGo.AddComponent<Arrow>();
            boltPrefabGo.AddComponent<Rigidbody2D>();
            boltPrefabGo.AddComponent<NetworkSoftSimulator>();
            Pool.RegisterForTests(boltPrefabGo, () =>
            {
                GameObject go = new GameObject("Bolt");
                go.transform.SetParent(f.LayerGo.transform);
                go.AddComponent<Rigidbody2D>();
                go.AddComponent<NetworkSoftSimulator>();
                return go.AddComponent<Arrow>();
            });
            ArrowAttack fireSo = new ArrowAttack { _arrowPrefab = boltPrefab };

            Arrow main = f.Shot(f.NewArcher(), data: fireSo);

            Pool boltPool = Pool.ForPrefab(boltPrefabGo);
            Assert.Equal(3, boltPool.HandedOut.Count);          // native arrow + 2 extras from the firing SO's prefab pool
            Assert.Equal(2, f.Extras(main).Count);
            Assert.Empty(f.PoolRef.HandedOut);                   // the fixture's own prefab pool stays untouched
        }

        [Theory]
        [InlineData(true)]      // damageable present but dead
        [InlineData(false)]     // damageable missing entirely (unknown -> fail closed)
        public void DeadOrDamageableMissingArcher_SpawnsNoExtras(bool withDamageable)
        {
            Fixture f = Scatter(3);
            Archer archer = f.NewArcher();
            if (withDamageable) archer._damageable.isDead = true;
            else archer._damageable = null;

            Arrow main = f.Shot(archer);

            Assert.NotNull(main);
            Assert.Empty(f.Extras(main));
            Assert.Equal(new[] { new Vector2(12f, 2f) }, main.GetComponent<Rigidbody2D>().Impulses.ToArray());
        }

        [Fact]
        public void FrameBudget_CapsAtEightExtrasPerFrame()
        {
            Fixture f = Scatter(5);
            Archer archer = f.NewArcher();

            Arrow first = f.Shot(archer);
            Arrow second = f.Shot(archer);
            Arrow third = f.Shot(archer);

            Arrow fourth = f.Shot(archer);
            Arrow fifth = f.Shot(archer);
            Assert.Equal(2, f.Extras(first).Count);
            Assert.Equal(2, f.Extras(second).Count);
            Assert.Equal(2, f.Extras(third).Count);
            Assert.Equal(2, f.Extras(fourth).Count);
            Assert.Empty(f.Extras(fifth));
            Assert.Equal(8, f.HandedOutArrows().Count - 5);
        }

        [Fact]
        public void SecondBudget_CapsAtFortyExtrasPerSecond()
        {
            Fixture f = Scatter(5);
            Archer archer = f.NewArcher();
            int extras = 0;
            for (int shot = 0; shot < 24; shot++)
            {
                Arrow main = f.Shot(archer);
                extras += f.Extras(main).Count;
                f.AdvanceFrame(0f);   // frame budget refreshes, the one-second window does not
            }

            Assert.Equal(40, extras);
        }

        [Fact]
        public void TrackedLedger_CapsAtSixtyFourLiveExtras()
        {
            Fixture f = Scatter(5, poolCapacity: 200);
            Archer archer = f.NewArcher();
            int extras = 0;
            for (int shot = 0; shot < 34; shot++)
            {
                Arrow main = f.Shot(archer);
                extras += f.Extras(main).Count;
                f.AdvanceFrame(0.2f);
            }

            Assert.Equal(64, extras);
        }

        [Fact]
        public void HitExtras_AreRetiredSoTheLedgerKeepsServing()
        {
            Fixture f = Scatter(5, poolCapacity: 200);
            Archer archer = f.NewArcher();
            int extras = 0;
            for (int shot = 0; shot < 40; shot++)
            {
                Arrow main = f.Shot(archer);
                List<Arrow> batch = f.Extras(main);
                extras += batch.Count;
                foreach (Arrow extra in batch) extra._hasHit = true;
                f.AdvanceFrame(0.2f);
                f.Tick();
            }

            Assert.Equal(80, extras);                       // 80 > 64 proves hit-retirement releases leases
            Assert.Equal(0, FakeOps.Count("despawn"));       // the module never despawns gameplay arrows
        }

        [Fact]
        public void PoolReuse_EndsThePreviousLeaseInsteadOfSaturatingTheLedger()
        {
            Fixture f = Scatter(3, poolCapacity: 4);
            Archer archer = f.NewArcher();
            int extras = 0;
            for (int shot = 0; shot < 40; shot++)
            {
                Arrow main = f.Shot(archer);
                Assert.NotNull(main);
                extras += f.Extras(main).Count;

                List<Component> live = new List<Component>(f.PoolRef.Active);
                foreach (Component instance in live) Pool.DespawnForTests(instance);
                f.AdvanceFrame(0.2f);
                f.Tick();
            }

            Assert.Equal(80, extras);                        // 80 > 64 proves reuse released old leases
            Assert.Equal(120, FakeOps.Count("despawn"));      // 40 shots x (main + 2 extras), test-driven only
        }
    }
}
