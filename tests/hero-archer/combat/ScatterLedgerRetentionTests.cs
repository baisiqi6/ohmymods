using System;
using System.Collections.Generic;
using KingdomEnhancedMod;
using UnityEngine;
using Xunit;

namespace KingdomArcherOptions.Combat.Tests
{
    /// <summary>
    /// The scatter ledger must survive every non-conclusive event: toggling the option off,
    /// transient field-read failures and world/layer deactivation. Only proven events (hit,
    /// despawn/recycle, destroy, pool reuse) may release a lease, so the 64-alive cap cannot be
    /// bypassed by toggling scattering off and on again.
    /// </summary>
    public class ScatterLedgerRetentionTests
    {
        private const int ShotsToFillLedger = 32;      // 32 x (clamped volley 3 minus original = 2) == 64 alive extras

        private static Fixture ScatterLedger(int volley = 5, int poolCapacity = int.MaxValue)
        {
            Fixture fixture = new Fixture(poolCapacity: poolCapacity);
            ModConfig.ArcherScatterEnabled.Value = true;
            ModConfig.ArcherVolleyCount.Value = volley;
            return fixture;
        }

        private static List<Arrow> FillLedger(Fixture f, Archer archer, out int extras)
        {
            List<Arrow> live = new List<Arrow>();
            extras = 0;
            for (int shot = 0; shot < ShotsToFillLedger; shot++)
            {
                List<Arrow> batch = f.Extras(f.Shot(archer));
                extras += batch.Count;
                live.AddRange(batch);
                f.AdvanceFrame(0.2f);
            }
            return live;
        }

        [Fact]
        public void ToggleOffOn_KeepsLiveLeasesSoTheAliveCapHolds()
        {
            Fixture f = ScatterLedger(poolCapacity: 200);
            Archer archer = f.NewArcher();
            FillLedger(f, archer, out int extras);
            Assert.Equal(64, extras);

            ModConfig.ArcherScatterEnabled.Value = false;          // off: only stops new spawns
            for (int frame = 0; frame < 3; frame++)
            {
                f.AdvanceFrame(0.2f);
                f.Tick();
            }
            ModConfig.ArcherScatterEnabled.Value = true;           // back on

            Arrow after = f.Shot(archer);

            Assert.Empty(f.Extras(after));                         // the 64 live leases are still accounted
        }

        [Fact]
        public void TransientReadFailure_KeepsTheLeaseUntilAProvenEvent()
        {
            Fixture f = ScatterLedger(poolCapacity: 200);
            Archer archer = f.NewArcher();
            List<Arrow> live = FillLedger(f, archer, out int extras);
            Assert.Equal(64, extras);

            foreach (Arrow arrow in live) arrow.ThrowOnHasHitRead = true;   // transient interop read failure
            for (int frame = 0; frame < 3; frame++)
            {
                f.AdvanceFrame(0.2f);
                f.Tick();
            }

            Arrow blocked = f.Shot(archer);
            Assert.Empty(f.Extras(blocked));                       // leases retained, budget still consumed

            foreach (Arrow arrow in live) arrow.ThrowOnHasHitRead = false;
            foreach (Arrow arrow in live) Pool.DespawnForTests(arrow);      // proven despawn/recycle
            f.AdvanceFrame(0.2f);
            f.Tick();

            Arrow after = f.Shot(archer);
            Assert.Equal(2, f.Extras(after).Count);                // released only on the proven event
        }

        [Fact]
        public void DeactivatedWorldLayer_DoesNotForgetLiveLeases()
        {
            Fixture f = ScatterLedger(poolCapacity: 200);
            Archer archer = f.NewArcher();
            FillLedger(f, archer, out int extras);
            Assert.Equal(64, extras);

            f.LayerGo.SetActive(false);                            // world/layer off: arrows stay activeSelf
            for (int frame = 0; frame < 3; frame++)
            {
                f.AdvanceFrame(0.2f);
                f.Tick();
            }
            f.LayerGo.SetActive(true);

            Arrow after = f.Shot(archer);

            Assert.Empty(f.Extras(after));                         // live leases were not mistaken for despawns
        }

        [Fact]
        public void ProvenHit_ReleasesTheLease()
        {
            Fixture f = ScatterLedger(volley: 3, poolCapacity: 20);
            Archer archer = f.NewArcher();

            List<Arrow> live = new List<Arrow>();
            for (int shot = 0; shot < 4; shot++)
            {
                live.AddRange(f.Extras(f.Shot(archer)));
                f.AdvanceFrame(0.2f);
            }
            Assert.Equal(8, live.Count);

            foreach (Arrow arrow in live) arrow._hasHit = true;     // proven hit
            f.AdvanceFrame(0.2f);
            f.Tick();

            Arrow after = f.Shot(archer);
            Assert.Equal(2, f.Extras(after).Count);                 // the retired slots are free again
        }
    }
}
