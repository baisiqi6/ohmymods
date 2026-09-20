internal static class P5
{
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

            Archer partialGhost = Fixture.AddMusketeer(1f); // registered without a binding
            partialGhost._character.inert = true;           // keep the top-up from refilling it
            formation.units[partialSeat] = partialGhost;

            Archer pooled = Fixture.AddArcher(1f);          // pooled new life (unmarked, unbound)
            formation.units[pooledSeat] = pooled;

            Archer member = Fixture.AddMusketeer(1f);       // legitimate member
            member.BindForTests(formation);
            formation.units[memberSeat] = member;

            Fixture.Tick();

            Check.True(formation.units[foreignSeat] == null, "foreign reference dropped");
            Check.Equal(0, foreign.OnLeaveCalls, "never OnLeave a foreign member");
            Check.True(formation.units[partialSeat] == null, "partial ghost reference dropped");
            Check.Equal(0, partialGhost.OnLeaveCalls, "no captured lease: reference drop only, never OnLeave");
            Check.True(formation.units[pooledSeat] == null, "pooled new-life reference dropped");
            Check.Equal(0, pooled.OnLeaveCalls, "never OnLeave a pooled new life");
            Check.True(ReferenceEquals(formation.units[memberSeat], member), "legitimate member kept");
            Check.Equal(0, member.OnLeaveCalls, "member untouched");
        });
    }
}
