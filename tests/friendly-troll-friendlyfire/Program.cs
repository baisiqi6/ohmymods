using System;

namespace FriendlyTrollFriendlyFireTests
{
    internal static class Program
    {
        private static int Main()
        {
            Console.WriteLine("friendly-troll-friendlyfire tests (issue #84 红测夹具；离线模型，非实机)");
            Console.WriteLine();

            FriendlyFireTests.Run();

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
