internal static class PipelineTestsSga
{
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
}
