using System;

namespace KnightStylePanelTests
{
    internal static class Program
    {
        private static int Main()
        {
            Console.WriteLine("knight style panel tests (console / serial)");
            Console.WriteLine();

            FirstSeenTests.Run();
            PanelTests.Run();
            DesignCTests.Run();
            RevisionTests.Run();

            Console.WriteLine();
            Console.WriteLine("total: passed=" + Case.Passed + " failed=" + Case.Failed);
            for (int i = 0; i < Case.Failures.Count; i++)
            {
                Console.WriteLine("FAILED: " + Case.Failures[i]);
            }
            return Case.Failed == 0 && Case.Passed > 0 ? 0 : 1;
        }
    }
}
