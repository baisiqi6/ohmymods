using System;

namespace MedievalScatterPolicyTests
{
    internal static class Program
    {
        private static int Main()
        {
            Console.WriteLine("medieval-scatter policy boundary tests (production source linked, boundary stubs)");
            Console.WriteLine();

            PolicyTests.Run();

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
