internal static class P4
{
    private static void M()
    {
        Archer partial = Fixture.AddMusketeer(1f);
        partial._character.inert = true;
        formation.units[partialSeat] = partial;
        Check.True(formation.units[partialSeat] == null, "partial ghost reference dropped");
    }
}
