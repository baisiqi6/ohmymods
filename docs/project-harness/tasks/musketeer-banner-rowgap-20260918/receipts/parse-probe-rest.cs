internal static class PipelineTestsRest
{
    private static void Recruitment()
    {
        Case.Run("directed recruits land in the row and native bow/pike seats stay free", () =>
        {
            Activate(0, 4);
            int[] map = BaselineSeatMap(0, true);
            for (int seat = 0; seat < RowLength; seat++)
            {
                Check.True(Fixture.Formation.units[RowSeat(0, seat)] is Archer, "row seat holds a musketeer");
                Check.Equal(Formation.UnitTypes.Squire, Fixture.Formation.unitTypes[RowSeat(0, seat)],
                    "row seat type is restored after the directed recruit");
            }
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
            {
                Check.True(Fixture.Formation.units[RowSeat(0, seat)] is Archer,
                    "row untouched by ordinary archers");
            }
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

            Check.Equal(0, CountOccupied(RowStart(0), RowLength), "seat must be rolled back");
            Check.True(musketeer.GetFormation() == null, "archer must not stay bound");
            Check.Equal(1, musketeer.OnLeaveCalls, "native leave ran exactly once");
            Check.False(Fixture.HasInfo("directed recruit rollback pending"), "no receipt when the rollback completes");

            musketeer.ThrowOnConvertToSoldier = false;
            Fixture.Tick();
            Check.Equal(1, CountOccupied(RowStart(0), RowLength), "seat is usable again on the next pass");
        });

        Case.Run("false with a partial registration rolls back through native leave", () =>
        {
            Fixture.Reset(10f);
            Archer musketeer = Fixture.AddMusketeer(20f);
            musketeer.ReturnFalseAfterRegister = true;
            Fixture.Player.ActivateFormation();

            Check.Equal(0, CountOccupied(RowStart(0), RowLength), "seat must be rolled back");
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

            Check.Equal(1, CountOccupied(RowStart(0), RowLength),
                "state-complete join must be kept despite the false return");
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

            Check.Equal(1, CountOccupied(RowStart(0), RowLength), "seat still occupied after the failed rollback");
            Check.True(musketeer.GetFormation() == null, "archer must not stay bound");
            Check.True(Fixture.HasInfo("directed recruit rollback pending"), "receipt canary");

            musketeer._character.inert = true;    // keep the top-up from refilling the seat
            Fixture.Formation.ThrowOnUnregister = 0;
            Fixture.Tick();
            Check.Equal(0, CountOccupied(RowStart(0), RowLength), "receipt retry released the seat");
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
            Check.Equal(1, CountOccupied(RowStart(0), RowLength), "no second member");

            Il2CppStructArray<Formation.UnitTypes>.ApplyThenThrowOnAttempt = 0;
            Il2CppStructArray<Formation.UnitTypes>.ThrowBeforeApplyOnAttempt = 0;
            Fixture.Tick();
            Check.Equal(Formation.UnitTypes.Archer, Fixture.Formation.unitTypes[8], "receipt retry restored the cell");
            Check.Equal(2, CountOccupied(RowStart(0), RowLength), "seat refilled after the restore");
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
            Fixture.Player.ActivateFormation();               // bow-side row seat holds `first`
            Archer second = Fixture.AddMusketeer(2f);

            Il2CppStructArray<Formation.UnitTypes>.ResetSetAttempts();
            Il2CppStructArray<Formation.UnitTypes>.FailRestoreFromAttempt = 7;   // target write lands, restore keeps throwing
            Fixture.Tick();

            Check.Equal(1, CountOccupied(RowStart(0), RowLength), "no second member");
            Check.Equal(Formation.UnitTypes.Archer, Fixture.Formation.unitTypes[RowSeat(0, 2)],
                "dirty target seat left open");

            Archer ordinary = Fixture.AddArcher(1f);
            Check.False(ordinary.TryRecruit(Fixture.Formation), "ordinary archer refused while dirty");
            Check.True(ordinary.GetFormation() == null, "ordinary archer stays outside");
            Check.Equal(1, CountOccupied(RowStart(0), RowLength), "the open seat stays empty");

            Fixture.Tick();                                    // the receipt retry still keeps failing
            Check.False(ordinary.TryRecruit(Fixture.Formation), "still refused while the restore keeps failing");

            Il2CppStructArray<Formation.UnitTypes>.FailRestoreFromAttempt = 0;
            Fixture.Tick();
            Check.Equal(Formation.UnitTypes.Squire, Fixture.Formation.unitTypes[RowSeat(0, 2)],
                "receipt restored the cell");
            Check.Equal(2, CountOccupied(RowStart(0), RowLength), "the waiting musketeer took the seat afterwards");

            Archer lateArcher = Fixture.AddArcher(1f);
            Check.True(lateArcher.TryRecruit(Fixture.Formation), "ordinary archers are native again");
            int[] map = BaselineSeatMap(0, true);
            Check.Equal(1, CountOccupied(map[3], 4), "ordinary archer fills a native bow seat");
            for (int seat = 0; seat < RowLength; seat++)
            {
                var occupant = Fixture.Formation.units[RowSeat(0, seat)];
                Check.True(occupant == null || (occupant is Archer rifleman && MusketeerIdentity.IsUnit(rifleman)),
                    "each occupied row seat still holds a marked musketeer; unfilled seats stay empty");
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
            Check.Equal(1, CountOccupied(RowStart(0), RowLength), "half-registered seat still occupied");
            long capturedLease = MusketeerRuntime.BindingLease(reused);
            Check.True(capturedLease > 0L, "the half-registration captured a live lease");

            reused.ThrowOnConvertToSoldier = false;
            Fixture.Formation.ThrowOnUnregister = 0;
            reused._character.inert = true;                   // keep the top-up out of the seat
            MusketeerRuntime.ArmNewLife(reused);              // same GameObject re-armed: new lease, still marked
            Check.True(MusketeerIdentity.IsUnit(reused), "counterexample needs a marked new life");
            Check.False(MusketeerRuntime.MatchesBindingLease(reused, capturedLease), "the life changed");

            Fixture.Tick();
            Check.Equal(0, CountOccupied(RowStart(0), RowLength), "stale seat reference cleared");
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
            Check.Near(Fixture.Spacing24[(int)Formation.UnitTypes.Squire],
                Fixture.Formation.UnitSpacing[(int)Formation.UnitTypes.Squire], 1e-6,
                "Squire spacing restored");
        });
    }

    private static void Lifecycle()
    {
        Case.Run("consecutive activate is idempotent and furl restores the exact baseline", () =>
        {
            Activate(2, 3, Side.Right);
            int length = Fixture.Formation.unitTypes.Length;
            int[] map = BaselineSeatMap(2, true);
            int rowStart = RowStart(2);

            Fixture.Player.ActivateFormation();
            Check.Equal(length, Fixture.Formation.unitTypes.Length, "no double expansion");
            Check.Equal(3, CountOccupied(rowStart, RowLength), "no duplicate musketeers");
            Check.Equal(2, CountOccupied(map[0], 2), "no duplicate boats");

            Fixture.Furl();
            Check.Equal(Fixture.Baseline24.Length, Fixture.Formation.unitTypes.Length, "baseline length");
            for (int i = 0; i < Fixture.Baseline24.Length; i++)
                Check.Equal(Fixture.Baseline24[i], Fixture.Formation.unitTypes[i], "baseline type order");
            Check.Equal(0f, Fixture.Formation.startOffset, "baseline startOffset");
            Check.Equal(0, CountOccupied(0, Fixture.Baseline24.Length), "all units left");
            Check.Near(Fixture.Spacing24[(int)Formation.UnitTypes.Squire],
                Fixture.Formation.UnitSpacing[(int)Formation.UnitTypes.Squire], 1e-6,
                "Squire spacing restored by the furl");
            Check.Near(Fixture.Spacing24[(int)Formation.UnitTypes.FleetBoat],
                Fixture.Formation.UnitSpacing[(int)Formation.UnitTypes.FleetBoat], 1e-6,
                "fleet spacing restored by the furl");
            Fixture.Tick();
            Check.Equal(Fixture.Baseline24.Length, Fixture.Formation.unitTypes.Length, "furl stays restored");

            Fixture.Player.ActivateFormation();
            AssertSeatMap(2, true);
            Check.Equal(3, CountOccupied(rowStart, RowLength), "re-raise refills the row");
            Check.Near(Fixture.Spacing24[(int)Formation.UnitTypes.Archer],
                Fixture.Formation.UnitSpacing[(int)Formation.UnitTypes.Squire], 1e-6,
                "row step re-applied on re-raise");
            Check.Near(1f, Fixture.Formation.UnitSpacing[(int)Formation.UnitTypes.FleetBoat], 1e-6,
                "multi-boat spacing re-applied on re-raise");
        });

        Case.Run("death frees the row seat and the maintenance pass refills it", () =>
        {
            Fixture.Reset(10f);
            var musketeers = new List<Archer>();
            for (int i = 0; i < 4; i++) musketeers.Add(Fixture.AddMusketeer(20f + i * 2f));
            Fixture.Player.ActivateFormation();
            int rowStart = RowStart(0);
            Check.Equal(4, CountOccupied(rowStart, RowLength), "row full");

            Archer spare = Fixture.AddMusketeer(2f);
            Archer dying = musketeers[0];              // first recruit took the bow-side row seat
            dying._damageable.isDead = true;
            Fixture.Formation.UnregisterUnit(dying);   // native death path
            Check.Equal(3, CountOccupied(rowStart, RowLength), "seat freed");

            Fixture.Tick();
            Check.Equal(4, CountOccupied(rowStart, RowLength), "seat refilled");
            Check.True(ReferenceEquals(spare.GetFormation(), Fixture.Formation), "the spare took the seat");
        });
    }

    private static void AuthorityAndScene()
    {
        Case.Run("authority loss and scene change never write", () =>
        {
            Activate(1, 3);
            int rowStart = RowStart(1);
            List<Archer> members = RowMembers(1);
            Check.Equal(3, members.Count, "row full");

            NetworkBigBoss.HasWorldAuth = false;
            Fixture.Tick();
            Check.Equal(3, CountOccupied(rowStart, RowLength), "no release without authority");
            for (int i = 0; i < members.Count; i++)
                Check.Equal(0, members[i].OnLeaveCalls, "no leave without authority");

            NetworkBigBoss.HasWorldAuth = true;
            Managers.Inst.world = new World { gameLayer = new GameObject().transform };
            Fixture.Tick();
            Check.Equal(3, CountOccupied(rowStart, RowLength), "no write into a new scene");
            for (int i = 0; i < members.Count; i++)
                Check.Equal(0, members[i].OnLeaveCalls, "no leave across scenes");
        });
    }
}
