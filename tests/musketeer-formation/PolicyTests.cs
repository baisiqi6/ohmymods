using System.Collections.Generic;
using KingdomEnhancedMod;
using UnityEngine;

namespace MusketeerFormationTests
{
    internal static class PolicyTests
    {
        internal static void Run()
        {
            Eligibility();
            Selection();
            Guard();
        }

        private static void Eligibility()
        {
            Case.Run("eligible free musketeer accepted", () =>
            {
                Fixture.Reset();
                Archer archer = Fixture.NewArcher(3f);
                Check.True(PatchMusketeerFormation.IsEligible(archer), "free marked musketeer must be eligible");
            });

            Case.Run("identity gate rejects unmarked archers", () =>
            {
                Fixture.Reset();
                Archer archer = Fixture.NewArcher(3f, marked: false);
                Check.False(PatchMusketeerFormation.IsEligible(archer), "unmarked archer must be rejected");
            });

            Case.Run("feature switch rejects everything", () =>
            {
                Fixture.Reset();
                Archer archer = Fixture.NewArcher(3f);
                MusketeerAccess.Enabled = false;
                Check.False(PatchMusketeerFormation.IsEligible(archer), "disabled feature must reject");
            });

            Case.Run("foreign world rejected", () =>
            {
                Fixture.Reset();
                Archer archer = Fixture.NewArcher(3f);
                MusketeerAccess.InWorldResult = false;
                Check.False(PatchMusketeerFormation.IsEligible(archer), "off-world unit must be rejected");
            });

            Case.Run("inactive or disabled unit rejected", () =>
            {
                Fixture.Reset();
                Archer inactive = Fixture.NewArcher(1f);
                inactive.gameObject.activeInHierarchy = false;
                Check.False(PatchMusketeerFormation.IsEligible(inactive), "inactive unit must be rejected");

                Archer disabled = Fixture.NewArcher(2f);
                disabled.enabled = false;
                Check.False(PatchMusketeerFormation.IsEligible(disabled), "disabled unit must be rejected");
            });

            Case.Run("hidden unit rejected", () =>
            {
                Fixture.Reset();
                Archer archer = Fixture.NewArcher(3f);
                archer.harmless = true;
                Check.False(PatchMusketeerFormation.IsEligible(archer), "hidden unit must be rejected");
            });

            Case.Run("formation member rejected", () =>
            {
                Fixture.Reset();
                Archer archer = Fixture.NewArcher(3f);
                archer.formation = Fixture.NewFormation(0f);
                Check.False(PatchMusketeerFormation.IsEligible(archer), "formation member must be rejected");
            });

            Case.Run("knight follower rejected", () =>
            {
                Fixture.Reset();
                Archer archer = Fixture.NewArcher(3f);
                archer._knight = new Knight();
                Check.False(PatchMusketeerFormation.IsEligible(archer), "knight follower must be rejected");
            });

            Case.Run("tower and guard post rejected", () =>
            {
                Fixture.Reset();
                Archer slotted = Fixture.NewArcher(1f);
                slotted._guardSlot = new GuardSlot();
                Check.False(PatchMusketeerFormation.IsEligible(slotted), "guard slot must be rejected");

                Archer inGuard = Fixture.NewArcher(2f);
                inGuard.inGuardSlot = true;
                Check.False(PatchMusketeerFormation.IsEligible(inGuard), "in-guard unit must be rejected");
            });

            Case.Run("boarding and embarked rejected", () =>
            {
                Fixture.Reset();
                Archer boarding = Fixture.NewArcher(1f);
                boarding._embarkee.EmbarkableTarget = new Embarkable();
                Check.False(PatchMusketeerFormation.IsEligible(boarding), "boarding unit must be rejected");

                Archer embarked = Fixture.NewArcher(2f);
                embarked._embarkee.IsEmbarked = true;
                Check.False(PatchMusketeerFormation.IsEligible(embarked), "embarked unit must be rejected");
            });

            Case.Run("grabbed or inert rejected", () =>
            {
                Fixture.Reset();
                Archer grabbed = Fixture.NewArcher(1f);
                grabbed._character.grabbed = true;
                Check.False(PatchMusketeerFormation.IsEligible(grabbed), "grabbed unit must be rejected");

                Archer inert = Fixture.NewArcher(2f);
                inert._character.inert = true;
                Check.False(PatchMusketeerFormation.IsEligible(inert), "inert unit must be rejected");
            });

            Case.Run("dead unit rejected", () =>
            {
                Fixture.Reset();
                Archer archer = Fixture.NewArcher(3f);
                archer._damageable.isDead = true;
                Check.False(PatchMusketeerFormation.IsEligible(archer), "dead unit must be rejected");
            });

            Case.Run("player-controlled unit rejected", () =>
            {
                Fixture.Reset();
                Archer archer = Fixture.NewArcher(3f);
                archer.playerControlled = true;
                Check.False(PatchMusketeerFormation.IsEligible(archer), "player-controlled unit must be rejected");
            });

            Case.Run("hero archer rejected", () =>
            {
                Fixture.Reset();
                Archer archer = Fixture.NewArcher(3f);
                HeroArcherRuntime.Heroes.Add(archer);
                Check.False(PatchMusketeerFormation.IsEligible(archer), "hero must be rejected");
            });
        }

        private static void Selection()
        {
            Case.Run("nearest candidates fill the free seats", () =>
            {
                Fixture.Reset();
                Formation formation = Fixture.NewFormation(0f);
                Fixture.NewArcher(10f);
                Archer near = Fixture.NewArcher(2f);
                Fixture.NewArcher(6f);
                Archer closer = Fixture.NewArcher(4f);
                var output = new List<Archer>();
                Check.Equal(2, PatchMusketeerFormation.Collect(formation, 2, output), "two seats");
                Check.Sequence(new[] { near, closer }, output, "nearest first");
            });

            Case.Run("row capacity hard caps at four", () =>
            {
                Fixture.Reset();
                Formation formation = Fixture.NewFormation(0f);
                for (int i = 0; i < 6; i++) Fixture.NewArcher(i + 1f);
                var output = new List<Archer>();
                Check.Equal(PatchMusketeerFormation.MaxMusketeers,
                    PatchMusketeerFormation.Collect(formation, 9, output), "hard cap");
            });

            Case.Run("no free seats collects nothing", () =>
            {
                Fixture.Reset();
                Formation formation = Fixture.NewFormation(0f);
                Fixture.NewArcher(1f);
                var output = new List<Archer>();
                Check.Equal(0, PatchMusketeerFormation.Collect(formation, 0, output), "zero seats");
                Check.Equal(0, output.Count, "output stays empty");
            });

            Case.Run("ineligible candidates are skipped, never waited on", () =>
            {
                Fixture.Reset();
                Formation formation = Fixture.NewFormation(0f);
                Archer grabbed = Fixture.NewArcher(1f);
                grabbed._character.grabbed = true;
                Archer member = Fixture.NewArcher(2f);
                member.formation = Fixture.NewFormation(100f);
                Archer free = Fixture.NewArcher(3f);
                var output = new List<Archer>();
                Check.Equal(1, PatchMusketeerFormation.Collect(formation, 4, output), "one valid candidate");
                Check.Sequence(new[] { free }, output, "invalid candidates skipped");
            });

            Case.Run("distance ties break by instance id", () =>
            {
                Fixture.Reset();
                Formation formation = Fixture.NewFormation(0f);
                Archer first = Fixture.NewArcher(5f);
                Archer second = Fixture.NewArcher(5f);
                Check.True(first.gameObject.GetInstanceID() < second.gameObject.GetInstanceID(), "fixture ids");
                var output = new List<Archer>();
                Check.Equal(2, PatchMusketeerFormation.Collect(formation, 2, output), "tie candidates");
                Check.Sequence(new[] { first, second }, output, "stable tie order");
            });

            Case.Run("disabled feature collects nothing", () =>
            {
                Fixture.Reset();
                Formation formation = Fixture.NewFormation(0f);
                Fixture.NewArcher(1f);
                MusketeerAccess.Enabled = false;
                var output = new List<Archer>();
                Check.Equal(0, PatchMusketeerFormation.Collect(formation, 4, output), "disabled collect");
            });
        }

        private static void Guard()
        {
            Case.Run("marked musketeer cannot take a native bow seat on a managed row", () =>
            {
                Fixture.Reset();
                Formation formation = Fixture.NewFormation(0f);
                Archer archer = Fixture.NewArcher(1f);
                Check.True(PatchMusketeerFormation.ShouldBlockNativeRecruit(archer, formation),
                    "native recruit must be refused");
            });

            Case.Run("directed recruit is allowed, then the guard is restored", () =>
            {
                Fixture.Reset();
                Formation formation = Fixture.NewFormation(0f);
                Archer archer = Fixture.NewArcher(1f);
                PatchMusketeerFormation.BeginDirected(archer, formation);
                Check.False(PatchMusketeerFormation.ShouldBlockNativeRecruit(archer, formation),
                    "directed call must pass");
                PatchMusketeerFormation.EndDirected();
                Check.True(PatchMusketeerFormation.ShouldBlockNativeRecruit(archer, formation),
                    "guard must come back");
            });

            Case.Run("directed bypass is pointer-exact", () =>
            {
                Fixture.Reset();
                Formation formation = Fixture.NewFormation(0f);
                Formation other = Fixture.NewFormation(0f);
                Archer directed = Fixture.NewArcher(1f);
                Archer otherArcher = Fixture.NewArcher(2f);
                PatchMusketeerFormation.BeginDirected(directed, formation);
                Check.True(PatchMusketeerFormation.ShouldBlockNativeRecruit(otherArcher, formation),
                    "another archer must stay blocked");
                Check.True(PatchMusketeerFormation.ShouldBlockNativeRecruit(directed, other),
                    "another formation must stay blocked");
                PatchMusketeerFormation.EndDirected();
            });

            Case.Run("no reserved row stays native", () =>
            {
                Fixture.Reset();
                Formation formation = Fixture.NewFormation(0f);
                Archer archer = Fixture.NewArcher(1f);
                PatchWorld_FleetBoatFormation.MusketeerRow = false;
                Check.False(PatchMusketeerFormation.ShouldBlockNativeRecruit(archer, formation),
                    "no row must not block");
            });

            Case.Run("non player formation stays native", () =>
            {
                Fixture.Reset();
                Formation formation = Fixture.NewFormation(0f, Formation.FormationType.PassiveShieldWall);
                Archer archer = Fixture.NewArcher(1f);
                Check.False(PatchMusketeerFormation.ShouldBlockNativeRecruit(archer, formation),
                    "shield wall must not block");
            });

            Case.Run("disabled feature stays native", () =>
            {
                Fixture.Reset();
                Formation formation = Fixture.NewFormation(0f);
                Archer archer = Fixture.NewArcher(1f);
                MusketeerAccess.Enabled = false;
                Check.False(PatchMusketeerFormation.ShouldBlockNativeRecruit(archer, formation),
                    "disabled feature must not block");
            });

            Case.Run("unmarked archer stays native", () =>
            {
                Fixture.Reset();
                Formation formation = Fixture.NewFormation(0f);
                Archer archer = Fixture.NewArcher(1f, marked: false);
                Check.False(PatchMusketeerFormation.ShouldBlockNativeRecruit(archer, formation),
                    "ordinary archer must not be blocked");
            });

            Case.Run("null inputs stay native", () =>
            {
                Fixture.Reset();
                Formation formation = Fixture.NewFormation(0f);
                Archer archer = Fixture.NewArcher(1f);
                Check.False(PatchMusketeerFormation.ShouldBlockNativeRecruit(null, formation), "null archer");
                Check.False(PatchMusketeerFormation.ShouldBlockNativeRecruit(archer, null), "null formation");
            });

            Case.Run("dirty temporary types refuse every archer on that formation", () =>
            {
                Fixture.Reset();
                Formation formation = Fixture.NewFormation(0f);
                Formation other = Fixture.NewFormation(0f);
                Archer marked = Fixture.NewArcher(1f);
                Archer ordinary = Fixture.NewArcher(2f, marked: false);
                PatchWorld_FleetBoatFormation.DirtyFormation = formation;

                Check.True(PatchMusketeerFormation.ShouldBlockNativeRecruit(ordinary, formation),
                    "ordinary archer must be refused while the row seat is dirty");
                Check.True(PatchMusketeerFormation.ShouldBlockNativeRecruit(marked, formation),
                    "marked archer must be refused while the row seat is dirty");
                MusketeerAccess.Enabled = false;
                Check.True(PatchMusketeerFormation.ShouldBlockNativeRecruit(ordinary, formation),
                    "dirty protection survives a disabled feature");
                Check.False(PatchMusketeerFormation.ShouldBlockNativeRecruit(ordinary, other),
                    "other formations stay native");
                PatchMusketeerFormation.BeginDirected(marked, formation);
                Check.False(PatchMusketeerFormation.ShouldBlockNativeRecruit(marked, formation),
                    "the directed pair is still allowed");
                PatchMusketeerFormation.EndDirected();
            });

            Case.Run("a directed transaction refuses every other archer", () =>
            {
                Fixture.Reset();
                Formation formation = Fixture.NewFormation(0f);
                Archer directed = Fixture.NewArcher(1f);
                Archer ordinary = Fixture.NewArcher(2f, marked: false);
                Archer otherMarked = Fixture.NewArcher(3f);
                PatchMusketeerFormation.BeginDirected(directed, formation);

                Check.True(PatchMusketeerFormation.HasActiveDirectedTransaction(formation), "transaction armed");
                Check.False(PatchMusketeerFormation.ShouldBlockNativeRecruit(directed, formation),
                    "the directed pair is allowed");
                Check.True(PatchMusketeerFormation.ShouldBlockNativeRecruit(ordinary, formation),
                    "an ordinary archer is refused mid-flight");
                Check.True(PatchMusketeerFormation.ShouldBlockNativeRecruit(otherMarked, formation),
                    "another musketeer is refused mid-flight");

                PatchMusketeerFormation.EndDirected();
                Check.False(PatchMusketeerFormation.HasActiveDirectedTransaction(formation), "transaction closed");
                Check.False(PatchMusketeerFormation.ShouldBlockNativeRecruit(ordinary, formation),
                    "ordinary archer is native again");
            });
        }
    }
}
