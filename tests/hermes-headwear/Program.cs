using System;

namespace HermesHeadwearTests
{
    internal static class Program
    {
        private static int Main()
        {
            Console.WriteLine("hermes-headwear core tests (console / serial / shared statics)");
            Console.WriteLine();

            CodecTests.Run();
            PersistenceTests.Run();
            SaveBridgeTests.Run();
            NetworkTests.Run();
            LifecycleTests.Run();
            DisguiseCycleIntegrationTests.Run();

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
