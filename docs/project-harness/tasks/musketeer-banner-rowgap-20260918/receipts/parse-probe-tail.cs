internal static class PipelineTestsTail
{
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

    private static void FeatureSwitch()
    {
        Case.Run("feature off releases own members without shrinking the formation", () =>
        {
            Activate(1, 4);
            int[] map = BaselineSeatMap(1, true);
            int rowStart = RowStart(1);
            int length = Fixture.Formation.unitTypes.Length;
            List<Archer> members = RowMembers(1);
            Check.Equal(4, members.Count, "row full");

            ModConfig.MusketeerEnabled.Value = false;
            Fixture.Tick();

            Check.Equal(0, CountOccupied(rowStart, RowLength), "own members released");
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
            Check.Equal(0, CountOccupied(rowStart, RowLength), "no recruit while the feature is off");
            Check.True(late.GetFormation() == null, "candidate stays outside");
        });

        Case.Run("a paused world defers the feature-off release", () =>
        {
            Activate(0, 2);
            int rowStart = RowStart(0);
            ModConfig.MusketeerEnabled.Value = false;
            Time.timeScale = 0f;
            Fixture.Tick();
            Check.Equal(2, CountOccupied(rowStart, RowLength), "nothing released while paused");

            Time.timeScale = 1f;
            Fixture.Tick();
            Check.Equal(0, CountOccupied(rowStart, RowLength), "released after unpause");
        });
    }

    private static void Ghosts()
    {
        Case.Run("ghost seats are cleaned per owner rules", () =>
        {
            Activate(0, 0);
            Formation formation = Fixture.Formation;
            Formation other = Fixture.Create<Formation>(Fixture.Root, 0f);
            int foreignSeat = RowSeat(0, 3);
            int partialSeat = RowSeat(0, 2);
            int pooledSeat = RowSeat(0, 1);
            int memberSeat = RowSeat(0, 0);

            Archer foreign = Fixture.AddMusketeer(1f);      // bow-side seat: different owner
            foreign.BindForTests(other);
            formation.units[foreignSeat] = foreign;

            Archer partial = Fixture.AddMusketeer(1f);      // registered without a binding
            partial._character.inert = true;                // keep the top-up from refilling it
            formation.units[partialSeat] = partial;

            Archer pooled = Fixture.AddArcher(1f);          // pooled new life (unmarked, unbound)
            formation.units[pooledSeat] = pooled;

            Archer member = Fixture.AddMusketeer(1f);       // legitimate member
            member.BindForTests(formation);
            formation.units[memberSeat] = member;

            Fixture.Tick();

            Check.True(formation.units[foreignSeat] == null, "foreign reference dropped");
            Check.Equal(0, foreign.OnLeaveCalls, "never OnLeave a foreign member");
            Check.True(formation.units[partialSeat] == null, "partial ghost reference dropped");
            Check.Equal(0, partial.OnLeaveCalls, "no captured lease: reference drop only, never OnLeave");
            Check.True(formation.units[pooledSeat] == null, "pooled new-life reference dropped");
            Check.Equal(0, pooled.OnLeaveCalls, "never OnLeave a pooled new life");
            Check.True(ReferenceEquals(formation.units[memberSeat], member), "legitimate member kept");
            Check.Equal(0, member.OnLeaveCalls, "member untouched");
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
