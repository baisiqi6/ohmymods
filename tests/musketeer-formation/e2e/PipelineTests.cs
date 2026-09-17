using System;
using System.Collections.Generic;
using Harness;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using KingdomEnhancedMod;
using UnityEngine;

internal static class PipelineTests
{
    private const int RowLength = 4;

    internal static void Run()
    {
        Composition();
        Recruitment();
        TransactionFailures();
        Lifecycle();
        FeatureSwitch();
        Ghosts();
        AuthorityAndScene();
    }

    // ---- helpers ------------------------------------------------------------

    private static void Activate(int boats, int musketeers, Side side = Side.Right, bool featureOn = true)
    {
        Fixture.Reset(side == Side.Right ? 10f : -10f);
        ModConfig.MusketeerEnabled.Value = featureOn;
        for (int i = 1; i <= boats; i++) Fixture.AddBoat(i, side);
        for (int i = 0; i < musketeers; i++) Fixture.AddMusketeer(20f + i * 2f);
        Fixture.Player.ActivateFormation();
    }

    /// <summary>Expected composite seat map: [row?] + baseline with the single fleet seat expanded.</summary>
    private static void AssertSeatMap(int boats, bool row)
    {
        Formation formation = Fixture.Formation;
        int expected = Fixture.Baseline24.Length + (boats > 0 ? boats - 1 : 0) + (row ? RowLength : 0);
        Check.Equal(expected, formation.unitTypes.Length, "composite length");
        int write = 0;
        if (row)
        {
            for (int i = 0; i < RowLength; i++)
                Check.Equal(Formation.UnitTypes.Gap, formation.unitTypes[write++], "row seat type");
        }
        for (int read = 0; read < Fixture.Baseline24.Length; read++)
        {
            if (read != 0)
            {
                Check.Equal(Fixture.Baseline24[read], formation.unitTypes[write++], "baseline order");
                continue;
            }
            if (boats == 0)
            {
                Check.Equal(Formation.UnitTypes.Gap, formation.unitTypes[write++], "closed fleet seat");
                continue;
            }
            for (int boat = 0; boat < boats; boat++)
                Check.Equal(Formation.UnitTypes.FleetBoat, formation.unitTypes[write++], "boat seat");
        }
        Check.Equal(write, formation.unitTypes.Length, "no extra seats");
    }

    /// <summary>Composite index of every original baseline seat.</summary>
    private static int[] BaselineSeatMap(int boats, bool row)
    {
        var map = new int[Fixture.Baseline24.Length];
        int write = row ? RowLength : 0;
        for (int read = 0; read < Fixture.Baseline24.Length; read++)
        {
            map[read] = write;
            write += read == 0 ? (boats > 0 ? boats : 1) : 1;
        }
        return map;
    }

    private static int CountOccupied(int from, int count)
    {
        int total = 0;
        for (int i = from; i < from + count; i++)
        {
            if (Fixture.Formation.units[i] != null) total++;
        }
        return total;
    }

    private static float[] SnapshotPositions()
    {
        var result = new float[Fixture.Formation.unitTypes.Length];
        for (int i = 0; i < result.Length; i++) result[i] = Fixture.Formation.GetXPosForIndex(i);
        return result;
    }

    private static List<Archer> RowMembers()
    {
        var members = new List<Archer>();
        for (int seat = 0; seat < RowLength; seat++)
        {
            if (Fixture.Formation.units[seat] is Archer archer) members.Add(archer);
        }
        return members;
    }

    // ---- cases --------------------------------------------------------------

    private static void Composition()
    {
        Case.Run("activation composes the 2.4 seat map for 0..4 boats and 0..4 musketeers", () =>
        {
            for (int boats = 0; boats <= 4; boats++)
            {
                for (int muskets = 0; muskets <= 4; muskets++)
                {
                    Activate(boats, muskets);
                    AssertSeatMap(boats, true);
                    int[] map = BaselineSeatMap(boats, true);
                    for (int seat = 0; seat < RowLength; seat++)
                    {
                        Check.Equal(seat >= RowLength - muskets, Fixture.Formation.units[seat] != null,
                            "row occupancy seat=" + seat + " boats=" + boats + " muskets=" + muskets);
                    }
                    for (int i = 1; i < Fixture.Baseline24.Length; i++)
                    {
                        Check.True(Fixture.Formation.units[map[i]] == null,
                            "original seat must stay free (slot " + i + ", boats=" + boats + ")");
                    }
                    if (boats > 0)
                        Check.Equal(boats, CountOccupied(map[0], boats), "boats seated");
                }
            }
        });

        Case.Run("the rear row never moves an original coordinate (2.4 baseline, both sides)", () =>
        {
            foreach (Side side in new[] { Side.Right, Side.Left })
            {
                foreach (int boats in new[] { 0, 1, 4 })
                {
                    Activate(boats, 4, side);
                    float[] withRow = SnapshotPositions();
                    int[] withMap = BaselineSeatMap(boats, true);

                    Activate(boats, 4, side, featureOn: false);
                    float[] noRow = SnapshotPositions();
                    int[] noMap = BaselineSeatMap(boats, false);

                    Check.Equal(withRow.Length, noRow.Length + RowLength, "row adds exactly four seats");
                    for (int i = 0; i < Fixture.Baseline24.Length; i++)
                    {
                        Check.Near(noRow[noMap[i]], withRow[withMap[i]], 1e-4,
                            "original coordinate moved (side=" + side + ", boats=" + boats + ", slot=" + i + ")");
                    }
                    for (int seat = 0; seat < RowLength; seat++)
                    {
                        // Positions from the real production run (row on), mirrored by the side the
                        // native body picked: the row must sit behind the native rear line.
                        float seatX = withRow[seat];
                        float rearX = withRow[withMap[0]];
                        Check.True(side == Side.Right ? seatX < rearX - 1e-4 : -seatX < -rearX - 1e-4,
                            "row must stay behind the native rear line (seat=" + seat + ")");
                    }
                }
            }
        });
    }

    private static void Recruitment()
    {
        Case.Run("directed recruits land in the row and native bow/pike seats stay free", () =>
        {
            Activate(0, 4);
            int[] map = BaselineSeatMap(0, true);
            for (int seat = 0; seat < RowLength; seat++)
                Check.True(Fixture.Formation.units[seat] is Archer, "row seat holds a musketeer");
            Check.Equal(0, CountOccupied(map[0], 1), "fleet seat free");
            Check.Equal(0, CountOccupied(map[3], 4), "bow seats free");
            Check.Equal(0, CountOccupied(map[8], 4), "pike seats free");
        });

        Case.Run("an extra musketeer cannot take a native bow seat while the row is full", () =>
        {
            Activate(0, 4);
            int[] map = BaselineSeatMap(0, true);
            Archer extra = Fixture.AddMusketeer(1f);
            Check.False(extra.TryRecruit(Fixture.Formation), "guard must refuse the native recruit");
            Check.True(extra.GetFormation() == null, "must stay outside the formation");
            Check.Equal(0, CountOccupied(map[3], 4), "bow seats stay free");
        });

        Case.Run("ordinary archers keep filling the native bow seats", () =>
        {
            Activate(0, 4);
            int[] map = BaselineSeatMap(0, true);
            var archers = new List<Archer>();
            for (int i = 0; i < 4; i++) archers.Add(Fixture.AddArcher(5f + i));
            for (int i = 0; i < 4; i++)
                Check.True(archers[i].TryRecruit(Fixture.Formation), "native bow recruit");
            Check.Equal(4, CountOccupied(map[3], 4), "bow seats filled");
            for (int seat = 0; seat < RowLength; seat++)
                Check.True(Fixture.Formation.units[seat] is Archer, "row untouched by ordinary archers");
        });
    }

    private static void TransactionFailures()
    {
        Case.Run("native throw after RegisterUnit rolls the seat back and releases the archer", () =>
        {
            Fixture.Reset(10f);
            Archer musketeer = Fixture.AddMusketeer(20f);
            musketeer.ThrowOnConvertToSoldier = true;
            Fixture.Player.ActivateFormation();

            Check.Equal(0, CountOccupied(0, RowLength), "seat must be rolled back");
            Check.True(musketeer.GetFormation() == null, "archer must not stay bound");
            Check.Equal(1, musketeer.OnLeaveCalls, "native leave ran exactly once");
            Check.False(Fixture.HasInfo("directed recruit rollback pending"), "no receipt when the rollback completes");

            musketeer.ThrowOnConvertToSoldier = false;
            Fixture.Tick();
            Check.Equal(1, CountOccupied(0, RowLength), "seat is usable again on the next pass");
        });

        Case.Run("false with a partial registration rolls back through native leave", () =>
        {
            Fixture.Reset(10f);
            Archer musketeer = Fixture.AddMusketeer(20f);
            musketeer.ReturnFalseAfterRegister = true;
            Fixture.Player.ActivateFormation();

            Check.Equal(0, CountOccupied(0, RowLength), "seat must be rolled back");
            Check.True(musketeer.GetFormation() == null, "archer must not stay bound");
            Check.Equal(1, musketeer.OnLeaveCalls, "native leave ran exactly once");
            Check.False(Fixture.HasInfo("directed recruit rollback pending"), "no receipt when the rollback completes");
        });

        Case.Run("false with a full binding is verified by state and kept", () =>
        {
            Fixture.Reset(10f);
            Archer musketeer = Fixture.AddMusketeer(20f);
            musketeer.ReturnFalseAfterBind = true;
            Fixture.Player.ActivateFormation();

            Check.Equal(1, CountOccupied(0, RowLength), "state-complete join must be kept despite the false return");
            Check.True(ReferenceEquals(musketeer.GetFormation(), Fixture.Formation), "bound to the formation");
            Check.Equal(0, musketeer.OnLeaveCalls, "no leave on a completed join");
        });

        Case.Run("rollback failure leaves a receipt and the next pass completes it", () =>
        {
            Fixture.Reset(10f);
            Archer musketeer = Fixture.AddMusketeer(20f);
            musketeer.ThrowOnConvertToSoldier = true;
            Fixture.Formation.ThrowOnUnregister = 1;
            Fixture.Player.ActivateFormation();

            Check.Equal(1, CountOccupied(0, RowLength), "seat still occupied after the failed rollback");
            Check.True(musketeer.GetFormation() == null, "archer must not stay bound");
            Check.True(Fixture.HasInfo("directed recruit rollback pending"), "receipt canary");

            musketeer._character.inert = true;    // keep the top-up from refilling the seat
            Fixture.Formation.ThrowOnUnregister = 0;
            Fixture.Tick();
            Check.Equal(0, CountOccupied(0, RowLength), "receipt retry released the seat");
            Check.Equal(1, musketeer.OnLeaveCalls, "native leave ran exactly once");
        });

        Case.Run("a throw in the type write is rolled back; a failed restore is finished by the next pass", () =>
        {
            Activate(0, 1);
            Archer second = Fixture.AddMusketeer(2f);

            Il2CppStructArray<Formation.UnitTypes>.ResetSetAttempts();
            Il2CppStructArray<Formation.UnitTypes>.ApplyThenThrowOnAttempt = 9;      // write #9 applies index 8 then throws
            Il2CppStructArray<Formation.UnitTypes>.ThrowBeforeApplyOnAttempt = 12;   // the restore of index 8 never applies

            Fixture.Tick();
            Check.Equal(Formation.UnitTypes.Gap, Fixture.Formation.unitTypes[8], "restore residual is visible");
            Check.True(second.GetFormation() == null, "failed transaction must not bind the archer");
            Check.Equal(1, CountOccupied(0, RowLength), "no second member");

            Il2CppStructArray<Formation.UnitTypes>.ApplyThenThrowOnAttempt = 0;
            Il2CppStructArray<Formation.UnitTypes>.ThrowBeforeApplyOnAttempt = 0;
            Fixture.Tick();
            Check.Equal(Formation.UnitTypes.Archer, Fixture.Formation.unitTypes[8], "receipt retry restored the cell");
            Check.Equal(2, CountOccupied(0, RowLength), "seat refilled after the restore");
        });

        Case.Run("an array replaced mid-transaction is never overwritten", () =>
        {
            Fixture.Reset(10f);
            Archer musketeer = Fixture.AddMusketeer(20f);
            musketeer.ReplaceArraysOnRecruit = formation =>
            {
                var replacement = new Il2CppStructArray<Formation.UnitTypes>(16);
                for (int i = 0; i < 16; i++) replacement[i] = Formation.UnitTypes.Player;
                formation.unitTypes = replacement;
                formation.units = new Il2CppReferenceArray<Formation.IFormationUnit>(16);
            };
            Fixture.Player.ActivateFormation();

            Check.Equal(Formation.UnitTypes.Player, Fixture.Formation.unitTypes[3], "replacement array untouched");
            Check.True(musketeer.GetFormation() == null, "bound-but-seatless actor was released directly");
            Check.Equal(1, musketeer.OnLeaveCalls, "direct leave ran exactly once");
        });

        Case.Run("dirty temporary types refuse every other recruit until the restore lands", () =>
        {
            Fixture.Reset(10f);
            Archer first = Fixture.AddMusketeer(20f);
            Fixture.Player.ActivateFormation();               // seat 3 holds `first`
            Archer second = Fixture.AddMusketeer(2f);

            Il2CppStructArray<Formation.UnitTypes>.ResetSetAttempts();
            Il2CppStructArray<Formation.UnitTypes>.FailRestoreFromAttempt = 4;   // target write lands, restore keeps throwing
            Fixture.Tick();

            Check.Equal(1, CountOccupied(0, RowLength), "no second member");
            Check.Equal(Formation.UnitTypes.Archer, Fixture.Formation.unitTypes[2], "dirty target seat left open");

            Archer ordinary = Fixture.AddArcher(1f);
            Check.False(ordinary.TryRecruit(Fixture.Formation), "ordinary archer refused while dirty");
            Check.True(ordinary.GetFormation() == null, "ordinary archer stays outside");
            Check.Equal(1, CountOccupied(0, RowLength), "the open seat stays empty");

            Fixture.Tick();                                    // the receipt retry still keeps failing
            Check.False(ordinary.TryRecruit(Fixture.Formation), "still refused while the restore keeps failing");

            Il2CppStructArray<Formation.UnitTypes>.FailRestoreFromAttempt = 0;
            Fixture.Tick();
            Check.Equal(Formation.UnitTypes.Gap, Fixture.Formation.unitTypes[2], "receipt restored the cell");
            Check.Equal(2, CountOccupied(0, RowLength), "the waiting musketeer took the seat afterwards");

            Archer lateArcher = Fixture.AddArcher(1f);
            Check.True(lateArcher.TryRecruit(Fixture.Formation), "ordinary archers are native again");
            int[] map = BaselineSeatMap(0, true);
            Check.Equal(1, CountOccupied(map[3], 4), "ordinary archer fills a native bow seat");
            for (int seat = 0; seat < RowLength; seat++)
            {
                var occupant = Fixture.Formation.units[seat];
                Check.True(occupant == null || (occupant is Archer rifleman && MusketeerIdentity.IsUnit(rifleman)),
                    "each occupied rear seat still holds a marked musketeer; unfilled seats stay empty");
            }
        });

        Case.Run("a re-armed same-GO life is never sent through OnLeave by an old receipt", () =>
        {
            Fixture.Reset(10f);
            Fixture.Player.ActivateFormation();               // empty row
            Archer reused = Fixture.AddMusketeer(2f);

            reused.ThrowOnConvertToSoldier = true;            // half-registration
            Fixture.Formation.ThrowOnUnregister = 1;          // the immediate rollback fails -> receipt with the lease
            Fixture.Tick();
            Check.Equal(1, CountOccupied(0, RowLength), "half-registered seat still occupied");
            long capturedLease = MusketeerRuntime.BindingLease(reused);
            Check.True(capturedLease > 0L, "the half-registration captured a live lease");

            reused.ThrowOnConvertToSoldier = false;
            Fixture.Formation.ThrowOnUnregister = 0;
            reused._character.inert = true;                   // keep the top-up out of the seat
            MusketeerRuntime.ArmNewLife(reused);              // same GameObject re-armed: new lease, still marked
            Check.True(MusketeerIdentity.IsUnit(reused), "counterexample needs a marked new life");
            Check.False(MusketeerRuntime.MatchesBindingLease(reused, capturedLease), "the life changed");

            Fixture.Tick();
            Check.Equal(0, CountOccupied(0, RowLength), "stale seat reference cleared");
            Check.Equal(0, reused.OnLeaveCalls, "never OnLeave a different life");
            Check.True(reused.GetFormation() == null, "the new life stays untouched");
            Check.True(MusketeerRuntime.BindingLease(reused) == capturedLease + 1L, "the new life keeps its own lease");
        });

        Case.Run("a failed seatless leave is retried by the maintenance pass", () =>
        {
            Fixture.Reset(10f);
            Archer musketeer = Fixture.AddMusketeer(20f);
            musketeer.ThrowOnLeave = 1;                       // the direct leave fails once
            musketeer.ReplaceArraysOnRecruit = formation =>
            {
                var replacement = new Il2CppStructArray<Formation.UnitTypes>(16);
                for (int i = 0; i < 16; i++) replacement[i] = Formation.UnitTypes.Player;
                formation.unitTypes = replacement;
                formation.units = new Il2CppReferenceArray<Formation.IFormationUnit>(16);
            };
            Fixture.Player.ActivateFormation();

            Check.True(ReferenceEquals(musketeer.GetFormation(), Fixture.Formation),
                "actor stays bound while the seat is already gone");
            Check.Equal(0, musketeer.OnLeaveCalls, "no completed leave yet");
            Check.Equal(Formation.UnitTypes.Player, Fixture.Formation.unitTypes[3], "replacement array untouched");

            musketeer._character.inert = true;                // keep the top-up out of the new array
            Fixture.Tick();
            Check.True(musketeer.GetFormation() == null, "maintenance finished the seatless leave");
            Check.Equal(1, musketeer.OnLeaveCalls, "leave ran exactly once");
            Check.Equal(Formation.UnitTypes.Player, Fixture.Formation.unitTypes[3], "new array still untouched");
        });

        Case.Run("a seatless receipt never releases a re-armed same-GO life", () =>
        {
            Fixture.Reset(10f);
            Archer musketeer = Fixture.AddMusketeer(20f);
            musketeer.ThrowOnLeave = 1;
            musketeer.ReplaceArraysOnRecruit = formation =>
            {
                var replacement = new Il2CppStructArray<Formation.UnitTypes>(16);
                for (int i = 0; i < 16; i++) replacement[i] = Formation.UnitTypes.Player;
                formation.unitTypes = replacement;
                formation.units = new Il2CppReferenceArray<Formation.IFormationUnit>(16);
            };
            Fixture.Player.ActivateFormation();

            long capturedLease = MusketeerRuntime.BindingLease(musketeer);
            Check.True(capturedLease > 0L, "the seatless receipt captured a live lease");
            Check.True(ReferenceEquals(musketeer.GetFormation(), Fixture.Formation), "bound after the failed leave");

            musketeer._character.inert = true;                // keep the top-up out
            MusketeerRuntime.ArmNewLife(musketeer);           // same GO, new life; still marked
            Check.True(MusketeerIdentity.IsUnit(musketeer), "counterexample needs a marked new life");
            Check.False(MusketeerRuntime.MatchesBindingLease(musketeer, capturedLease), "the life changed");

            Fixture.Tick();
            Check.Equal(0, musketeer.OnLeaveCalls, "never OnLeave a different life");
            Check.True(ReferenceEquals(musketeer.GetFormation(), Fixture.Formation),
                "the different life was not touched");
            Check.Equal(Formation.UnitTypes.Player, Fixture.Formation.unitTypes[3], "new array untouched");
        });

        Case.Run("native exception in ActivateFormation restores the empty baseline", () =>
        {
            Fixture.Reset(10f);
            Fixture.AddMusketeer(20f);
            Player.ThrowInActivateBody = 1;
            bool threw = false;
            try { Fixture.Player.ActivateFormation(); }
            catch (InvalidOperationException) { threw = true; }

            Check.True(threw, "the scripted native exception must surface");
            Check.Equal(Fixture.Baseline24.Length, Fixture.Formation.unitTypes.Length, "baseline restored");
            Check.Equal(0, CountOccupied(0, Fixture.Baseline24.Length), "no units left behind");
            Check.Equal(0f, Fixture.Formation.startOffset, "startOffset restored");
        });
    }

    private static void Lifecycle()
    {
        Case.Run("consecutive activate is idempotent and furl restores the exact baseline", () =>
        {
            Activate(2, 3, Side.Right);
            int length = Fixture.Formation.unitTypes.Length;
            int[] map = BaselineSeatMap(2, true);

            Fixture.Player.ActivateFormation();
            Check.Equal(length, Fixture.Formation.unitTypes.Length, "no double expansion");
            Check.Equal(3, CountOccupied(0, RowLength), "no duplicate musketeers");
            Check.Equal(2, CountOccupied(map[0], 2), "no duplicate boats");

            Fixture.Furl();
            Check.Equal(Fixture.Baseline24.Length, Fixture.Formation.unitTypes.Length, "baseline length");
            for (int i = 0; i < Fixture.Baseline24.Length; i++)
                Check.Equal(Fixture.Baseline24[i], Fixture.Formation.unitTypes[i], "baseline type order");
            Check.Equal(0f, Fixture.Formation.startOffset, "baseline startOffset");
            Check.Equal(0, CountOccupied(0, Fixture.Baseline24.Length), "all units left");
            Fixture.Tick();
            Check.Equal(Fixture.Baseline24.Length, Fixture.Formation.unitTypes.Length, "furl stays restored");

            Fixture.Player.ActivateFormation();
            AssertSeatMap(2, true);
            Check.Equal(3, CountOccupied(0, RowLength), "re-raise refills the row");
        });

        Case.Run("death frees the row seat and the maintenance pass refills it", () =>
        {
            Fixture.Reset(10f);
            var musketeers = new List<Archer>();
            for (int i = 0; i < 4; i++) musketeers.Add(Fixture.AddMusketeer(20f + i * 2f));
            Fixture.Player.ActivateFormation();
            Check.Equal(4, CountOccupied(0, RowLength), "row full");

            Archer spare = Fixture.AddMusketeer(2f);
            Archer dying = musketeers[0];              // first recruit took the front-most seat
            dying._damageable.isDead = true;
            Fixture.Formation.UnregisterUnit(dying);   // native death path
            Check.Equal(3, CountOccupied(0, RowLength), "seat freed");

            Fixture.Tick();
            Check.Equal(4, CountOccupied(0, RowLength), "seat refilled");
            Check.True(ReferenceEquals(spare.GetFormation(), Fixture.Formation), "the spare took the seat");
        });
    }

    private static void FeatureSwitch()
    {
        Case.Run("feature off releases own members without shrinking the formation", () =>
        {
            Activate(1, 4);
            int[] map = BaselineSeatMap(1, true);
            int length = Fixture.Formation.unitTypes.Length;
            List<Archer> members = RowMembers();
            Check.Equal(4, members.Count, "row full");

            ModConfig.MusketeerEnabled.Value = false;
            Fixture.Tick();

            Check.Equal(0, CountOccupied(0, RowLength), "own members released");
            for (int i = 0; i < members.Count; i++)
            {
                Check.True(members[i].GetFormation() == null, "released from the formation");
                Check.Equal(1, members[i].OnLeaveCalls, "native leave exactly once");
            }
            Check.Equal(length, Fixture.Formation.unitTypes.Length, "arrays are not hot-shrunk");
            Check.Equal(1, CountOccupied(map[0], 1), "boat untouched");
            for (int i = 1; i < Fixture.Baseline24.Length; i++)
                Check.True(Fixture.Formation.units[map[i]] == null, "original seat untouched");

            Archer late = Fixture.AddMusketeer(1f);
            Fixture.Tick();
            Check.Equal(0, CountOccupied(0, RowLength), "no recruit while the feature is off");
            Check.True(late.GetFormation() == null, "candidate stays outside");
        });

        Case.Run("a paused world defers the feature-off release", () =>
        {
            Activate(0, 2);
            ModConfig.MusketeerEnabled.Value = false;
            Time.timeScale = 0f;
            Fixture.Tick();
            Check.Equal(2, CountOccupied(0, RowLength), "nothing released while paused");

            Time.timeScale = 1f;
            Fixture.Tick();
            Check.Equal(0, CountOccupied(0, RowLength), "released after unpause");
        });
    }

    private static void Ghosts()
    {
        Case.Run("ghost seats are cleaned per owner rules", () =>
        {
            Activate(0, 0);
            Formation formation = Fixture.Formation;
            Formation other = Fixture.Create<Formation>(Fixture.Root, 0f);

            Archer foreign = Fixture.AddMusketeer(1f);      // seat 3: different owner
            foreign.BindForTests(other);
            formation.units[3] = foreign;

            Archer partial = Fixture.AddMusketeer(1f);      // seat 2: registered without a binding
            partial._character.inert = true;                // keep the top-up from refilling it
            formation.units[2] = partial;

            Archer pooled = Fixture.AddArcher(1f);          // seat 1: pooled new life (unmarked, unbound)
            formation.units[1] = pooled;

            Archer member = Fixture.AddMusketeer(1f);       // seat 0: legitimate member
            member.BindForTests(formation);
            formation.units[0] = member;

            Fixture.Tick();

            Check.True(formation.units[3] == null, "foreign reference dropped");
            Check.Equal(0, foreign.OnLeaveCalls, "never OnLeave a foreign member");
            Check.True(formation.units[2] == null, "partial ghost reference dropped");
            Check.Equal(0, partial.OnLeaveCalls, "no captured lease: reference drop only, never OnLeave");
            Check.True(formation.units[1] == null, "pooled new-life reference dropped");
            Check.Equal(0, pooled.OnLeaveCalls, "never OnLeave a pooled new life");
            Check.True(ReferenceEquals(formation.units[0], member), "legitimate member kept");
            Check.Equal(0, member.OnLeaveCalls, "member untouched");
        });
    }

    private static void AuthorityAndScene()
    {
        Case.Run("authority loss and scene change never write", () =>
        {
            Activate(1, 3);
            List<Archer> members = RowMembers();
            Check.Equal(3, members.Count, "row full");

            NetworkBigBoss.HasWorldAuth = false;
            Fixture.Tick();
            Check.Equal(3, CountOccupied(0, RowLength), "no release without authority");
            for (int i = 0; i < members.Count; i++)
                Check.Equal(0, members[i].OnLeaveCalls, "no leave without authority");

            NetworkBigBoss.HasWorldAuth = true;
            Managers.Inst.world = new World { gameLayer = new GameObject().transform };
            Fixture.Tick();
            Check.Equal(3, CountOccupied(0, RowLength), "no write into a new scene");
            for (int i = 0; i < members.Count; i++)
                Check.Equal(0, members[i].OnLeaveCalls, "no leave across scenes");
        });
    }
}
