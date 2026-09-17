using System;

internal static class Program
{
    private static int Main()
    {
        PipelineTests.Run();

        Console.WriteLine();
        Console.WriteLine("passed=" + Case.Passed + " failed=" + Case.Failed);
        for (int i = 0; i < Case.Failures.Count; i++)
            Console.WriteLine("  FAIL " + Case.Failures[i]);
        return Case.Failed == 0 ? 0 : 1;
    }
}
