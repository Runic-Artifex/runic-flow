using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RunicFlow.Tests;

internal static class Program
{
    public static async Task<int> Main()
    {
        IReadOnlyList<(string Name, Func<ValueTask> Run)> tests = HeadlessFlowTests.All;
        int failures = 0;
        foreach ((string name, Func<ValueTask> run) in tests)
        {
            try
            {
                await run().ConfigureAwait(false);
                Console.WriteLine($"PASS {name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"FAIL {name}");
                Console.Error.WriteLine(exception);
            }
        }

        Console.WriteLine($"Executed {tests.Count} headless Flow tests; {failures} failed.");
        return failures == 0 ? 0 : 1;
    }
}
