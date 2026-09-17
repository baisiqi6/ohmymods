using System;

namespace KnightStableContextTests
{
    internal static class Program
    {
        private static int Main()
        {
            Console.WriteLine("knight stable-context resolver tests (console / serial)");
            Console.WriteLine();

            ResolveTests.Run();

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
