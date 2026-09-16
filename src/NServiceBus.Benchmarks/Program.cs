namespace NServiceBus.Benchmarks;

using System;
using System.Linq;
using BenchmarkDotNet.Running;

class Program
{
    static void Main(string[] args)
    {
        // `--sizes` reports async state machine sizes instead of running benchmarks. These are
        // deterministic and reproduce on any machine, unlike timings, so they are the cheapest way to
        // notice that a change grew the per-message allocation of a hot pipeline method.
        if (args.Contains("--sizes", StringComparer.OrdinalIgnoreCase))
        {
            AsyncStateMachineSizes.Report();
            return;
        }

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
