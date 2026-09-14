using System;
using System.Collections.Generic;
using System.Linq;
using KingdomEnhancedMod;
using UnityEngine;
using Xunit;

namespace KingdomArcherOptions.Combat.Tests
{
    /// <summary>
    /// Per-arrow pool capacity gate. The module must never let the native pool fast-spawn path
    /// relocate live arrows, so every extra is gated on the resolved prefab's pool state.
    /// </summary>
    public class ScatterPoolGateTests
    {
        private static Fixture Scatter(int volley = 5, int poolCapacity = int.MaxValue, bool registerPool = true)
        {
            Fixture fixture = new Fixture(registerPool, poolCapacity: poolCapacity);
            ModConfig.ArcherScatterEnabled.Value = true;
            ModConfig.ArcherVolleyCount.Value = volley;
            return fixture;
        }

        private static Arrow SpawnRaw(Fixture f)
            => Pool.Spawn<Arrow>(f.Data._arrowPrefab, Vector3.zero, Quaternion.identity, f.LayerGo.transform, true);

        [Fact]
        public void PoolAtCapacityWithNoCache_SkipsEveryExtraAndLeavesLiveArrowsAlone()
        {
            Fixture f = Scatter(poolCapacity: 1);
            Archer archer = f.NewArcher();

            Arrow main = f.Shot(archer);

            Assert.NotNull(main);
            Assert.Empty(f.Extras(main));
            Assert.True(main.gameObject.activeInHierarchy);
            Assert.Equal(new[] { new Vector2(12f, 2f) }, main.GetComponent<Rigidbody2D>().Impulses.ToArray());
            Assert.Equal(1, f.PoolRef._total);
            Assert.Empty(f.PoolRef._cache);
            Assert.Equal(0, FakeOps.Count("despawn"));
        }

        [Fact]
        public void PoolCapacityIsCheckedPerArrow_CacheExhaustionStopsMidVolley()
        {
            Fixture f = Scatter(poolCapacity: 3);
            Archer archer = f.NewArcher();
            Pool pool = f.PoolRef;

            Arrow live = SpawnRaw(f);
            Arrow cachedA = SpawnRaw(f);
            Arrow cachedB = SpawnRaw(f);
            Pool.DespawnForTests(cachedA);
            Pool.DespawnForTests(cachedB);
            Assert.Equal(3, pool._total);
            Assert.Equal(2, pool._cache.Count);
            int despawnsBeforeShot = FakeOps.Count("despawn");

            Arrow main = f.Shot(archer);            // main reuses one cached instance, one stays cached

            List<Arrow> extras = f.Extras(main);
            Assert.Single(extras);                  // exactly the last cached instance, then the gate stops
            Assert.True(ReferenceEquals(extras[0], cachedA) || ReferenceEquals(extras[0], cachedB));
            Assert.Empty(pool._cache);
            Assert.True(live.gameObject.activeInHierarchy);          // live instance untouched
            Assert.Empty(live.GetComponent<Rigidbody2D>().Impulses); // and never repositioned/forced
            Assert.Equal(despawnsBeforeShot, FakeOps.Count("despawn")); // only the two explicit fixture returns
        }

        [Fact]
        public void ResolvedBiomeSwapPrefabPoolIsWhatTheGateChecks()
        {
            Fixture f = Scatter(volley: 3, registerPool: false);
            GameObject swappedGo = new GameObject("SwappedArrowPrefab");
            swappedGo.SetActive(false);
            swappedGo.AddComponent<Arrow>();
            swappedGo.AddComponent<Rigidbody2D>();
            swappedGo.AddComponent<NetworkSoftSimulator>();
            Pool.RegisterForTests(swappedGo, () =>
            {
                GameObject go = new GameObject("SwappedArrow");
                go.transform.SetParent(f.LayerGo.transform);
                go.AddComponent<Rigidbody2D>();
                go.AddComponent<NetworkSoftSimulator>();
                return go.AddComponent<Arrow>();
            });
            f.Biome.RegisterSwapForTests(f.PrefabGo, swappedGo);

            Arrow main = f.Shot(f.NewArcher());

            Assert.NotNull(main);
            Assert.Equal(3, Pool.ForPrefab(swappedGo).HandedOut.Count);   // native arrow + 2 extras
            Assert.Null(Pool.ForPrefab(f.PrefabGo));                      // no pool invented for the original prefab
        }

        [Fact]
        public void ResolvedBiomeSwapPoolMissing_SkipsExtrasWithoutInstantiating()
        {
            Fixture f = Scatter(volley: 3, registerPool: false);
            GameObject swappedGo = new GameObject("UnpooledArrowPrefab");
            swappedGo.SetActive(false);
            swappedGo.AddComponent<Arrow>();
            swappedGo.AddComponent<Rigidbody2D>();
            swappedGo.AddComponent<NetworkSoftSimulator>();
            f.Biome.RegisterSwapForTests(f.PrefabGo, swappedGo);
            Archer archer = f.NewArcher();

            PatchBridge.ArrowFired(f.Data, archer.gameObject, new Vector3(0f, 1f, 0f), false, new Vector2(12f, 2f));

            Assert.Equal(0, FakeOps.Count("spawn"));
        }

        [Theory]
        [InlineData(false, true, true)]     // resolved prefab has no Arrow component
        [InlineData(true, false, true)]     // ... no Rigidbody2D
        [InlineData(true, true, false)]     // ... no NetworkSoftSimulator
        public void ResolvedPrefabMissingDependencies_SkipsExtras(bool withArrow, bool withRigidbody, bool withSoftSim)
        {
            Fixture f = Scatter(volley: 3, registerPool: false);
            GameObject swappedGo = new GameObject("SwappedArrowPrefab");
            swappedGo.SetActive(false);
            if (withArrow) swappedGo.AddComponent<Arrow>();
            if (withRigidbody) swappedGo.AddComponent<Rigidbody2D>();
            if (withSoftSim) swappedGo.AddComponent<NetworkSoftSimulator>();
            // the resolved prefab *does* have a working pool: only the missing dependency may block
            Pool.RegisterForTests(swappedGo, () =>
            {
                GameObject go = new GameObject("SwappedArrow");
                go.transform.SetParent(f.LayerGo.transform);
                go.AddComponent<Rigidbody2D>();
                go.AddComponent<NetworkSoftSimulator>();
                return go.AddComponent<Arrow>();
            });
            f.Biome.RegisterSwapForTests(f.PrefabGo, swappedGo);
            Archer archer = f.NewArcher();

            PatchBridge.ArrowFired(f.Data, archer.gameObject, new Vector3(0f, 1f, 0f), false, new Vector2(12f, 2f));

            Assert.Equal(0, FakeOps.Count("spawn"));
        }
    }
}
