internal static class P3
{
    private static void M()
    {
        Managers.Inst.world = new World { gameLayer = new GameObject().transform };
    }

    private static void M2()
    {
        Formation other = Fixture.Create<Formation>(Fixture.Root, 0f);
    }

    private static void M3()
    {
        int foreignSeat = RowSeat(0, 3);
        Check.Equal(0, foreign.OnLeaveCalls, "never OnLeave a foreign member");
    }
}
