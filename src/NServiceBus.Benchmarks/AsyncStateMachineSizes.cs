namespace NServiceBus.Benchmarks;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

/// <summary>
/// Reports the size of the compiler-generated async state machine for hot pipeline methods.
/// </summary>
/// <remarks>
/// An <c>async</c> method that suspends has its state machine boxed on the heap, and every awaiter and
/// hoisted local in the method is a field in that box. For a method on the per-message path this is a
/// fixed allocation charged to every message, so growing one is a regression that a timing benchmark is
/// far too noisy to reveal. These numbers are deterministic and reproduce on any machine.
/// </remarks>
static class AsyncStateMachineSizes
{
    static readonly string[] TypesToReport =
    [
        "NServiceBus.TransportReceiveToPhysicalMessageConnector",
        "NServiceBus.MainPipelineExecutor",
        "NServiceBus.LoadHandlersConnector",
        "NServiceBus.DeserializeMessageConnector",
        "NServiceBus.SerializeMessageConnector"
    ];

    public static void Report()
    {
        var assembly = typeof(EndpointConfiguration).Assembly;

        Console.WriteLine($"Async state machine sizes for {assembly.GetName().Name}");
        Console.WriteLine("A method only pays for its box when it actually suspends.");
        Console.WriteLine();

        foreach (var typeName in TypesToReport)
        {
            var type = assembly.GetType(typeName, throwOnError: false, ignoreCase: false);
            if (type is null)
            {
                Console.WriteLine($"{typeName}  (not found - renamed or removed?)");
                continue;
            }

            Console.WriteLine(typeName);

            foreach (var (name, size) in StateMachineSizes(type))
            {
                var boxed = (size + (IntPtr.Size * 2) + 7) / 8 * 8;
                Console.WriteLine($"    {name,-34} struct {size,4} B    boxed ~{boxed,4} B");
            }

            Console.WriteLine();
        }
    }

    static IEnumerable<(string Name, int Size)> StateMachineSizes(Type type)
    {
        var sizeOf = typeof(Unsafe).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == nameof(Unsafe.SizeOf) && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);

        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .OrderBy(m => m.Name, StringComparer.Ordinal);

        foreach (var method in methods)
        {
            var stateMachine = method.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType;
            if (stateMachine is null)
            {
                continue;
            }

            yield return (method.Name, (int)sizeOf.MakeGenericMethod(stateMachine).Invoke(null, null)!);
        }
    }
}
