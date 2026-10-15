using System;

internal static class Program
{
    private static int Main()
    {
        try
        {
            ClockCoreChecks.Run();
            ClockBoundaryChecks.Run();
            ClockPhaseChecks.Run();
            ClockStressChecks.Run();
            return Runner.Finish();
        }
        catch (Exception e)
        {
            Console.WriteLine("EXCEPTION " + e);
            return 3;
        }
    }
}
