// parse probe: transaction constructs used by PipelineTests.cs, in isolation
internal static class P
{
    private static void M(object musketeer, Formation formation)
    {
        Il2CppStructArray<Formation.UnitTypes>.ResetSetAttempts();
        Il2CppStructArray<Formation.UnitTypes>.ApplyThenThrowOnAttempt = 9;
        Il2CppStructArray<Formation.UnitTypes>.ThrowBeforeApplyOnAttempt = 12;
        bool threw = false;
        try { Fixture.Player.ActivateFormation(); }
        catch (InvalidOperationException) { threw = true; }
        musketeer.ReplaceArraysOnRecruit = callbackFormation =>
        {
            var replacement = new Il2CppStructArray<Formation.UnitTypes>(16);
            for (int i = 0; i < 16; i++) replacement[i] = Formation.UnitTypes.Player;
            callbackFormation.unitTypes = replacement;
            callbackFormation.units = new Il2CppReferenceArray<Formation.IFormationUnit>(16);
        };
        Check.True(musketeer.GetFormation() == null, "must stay outside the formation");
        Check.False(extra.TryRecruit(Fixture.Formation), "guard must refuse the native recruit");
    }
}
