using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RunicFlow.Tests;

internal static class Program
{
    private static async Task<int> Main()
    {
        IReadOnlyList<ContractTest> tests = ContractTestCatalog.All;
        int failures = 0;

        foreach (ContractTest test in tests)
        {
            try
            {
                await test.Run().ConfigureAwait(false);
                Console.WriteLine($"PASS {test.Name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"FAIL {test.Name}");
                Console.Error.WriteLine(exception);
            }
        }

        Console.WriteLine($"Executed {tests.Count} Flow contract tests; {failures} failed.");
        return failures == 0 ? 0 : 1;
    }
}

internal sealed record ContractTest(string Name, Func<ValueTask> Run);
