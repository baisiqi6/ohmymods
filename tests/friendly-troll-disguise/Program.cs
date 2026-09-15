using System;

namespace FriendlyTrollDisguiseTests
{
    internal static class Program
    {
        private static int Main()
        {
            Console.WriteLine("friendly-troll-disguise core tests (静态资格判定 + 后缀重选)");
            Console.WriteLine();

            IsProtectedTests.Run();
            ReselectTests.Run();

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
