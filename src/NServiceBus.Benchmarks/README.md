# NServiceBus.Benchmarks

Benchmarks for the message processing pipeline. These exist to make performance claims checkable, and to
put a change measured in isolation into proportion against what a message actually costs.

## Running

```
dotnet run -c Release --project src/NServiceBus.Benchmarks -- --filter *
```

Release is required; BenchmarkDotNet refuses to produce meaningful numbers from a Debug build.

Useful arguments:

| Argument | Effect |
| --- | --- |
| `--filter *ReceiveConnector*` | Run one benchmark class |
| `--job Short` | Fewer iterations, wider error bars, much faster |
| `--sizes` | Report async state machine sizes instead of running benchmarks |

## What is here

**`ReceiveConnectorBenchmarks`** drives `TransportReceiveToPhysicalMessageConnector` for a single incoming
message, with the outbox both enabled and disabled, and with a handler that sends zero, one or two
messages. Narrow enough to attribute a change to one connector.

**`FullReceivePipelineBenchmarks`** starts a real endpoint against an in-memory transport and pushes
messages through the core's own `OnMessage` callback, so the whole receive pipeline runs: the pipeline
executor, every registered behavior, deserialization, handler invocation, and the complete outgoing
pipeline for anything the handler sends. Use it to turn "this connector got 60ns faster" into "this is
N% of a message".

**`--sizes`** reports the size of the compiler-generated async state machine for hot pipeline methods. An
`async` method that suspends has its state machine boxed on the heap, and every awaiter and hoisted local
is a field in that box, so for a method on the per-message path it is a fixed allocation charged to every
message. Growing one is a regression that timing benchmarks are far too noisy to catch.

## Reading the results

**Trust the allocation column; be sceptical of the timings.** Allocation is deterministic and reproduces
exactly across runs and machines. Timings at these scales routinely come back with error bars larger than
the effect being measured — a 20-90ns change cannot be resolved inside a multi-microsecond operation. If
you need a timing comparison, run both variants back to back on an otherwise idle machine and treat
anything under a few percent as noise.

**The absolute numbers overstate the real cost.** The transport does no I/O and nothing is persisted, so a
production endpoint does strictly more work per message than these report. Deltas are comparable; a
proportion derived from them is an upper bound on the share attributable to core.

**A benchmark that measures nothing is worse than none.** Both classes assert in `[GlobalSetup]` that the
code under test actually ran — that the connector dispatched the expected operations, that the handler was
invoked — and throw rather than silently benchmark a no-op. Keep that property when adding benchmarks.

## Known issue

`NServiceBus.Core.Analyzer` currently fails its own Release build on two `IDE0055` formatting diagnostics
in `Sagas/SagaAnalyzer.cs`, which are promoted to errors outside Debug. This is unrelated to this project
but it blocks BenchmarkDotNet's default out-of-process toolchain, which builds the whole project chain in
Release. Until that is fixed, either build with `-p:TreatWarningsAsErrors=false` and run in process:

```
dotnet build -c Release -p:TreatWarningsAsErrors=false src/NServiceBus.Benchmarks
dotnet run -c Release --no-build --project src/NServiceBus.Benchmarks -- --filter * --inProcess
```

or fix the analyzer first. In-process runs share the host process and are less isolated, so prefer the
default toolchain once the build is clean.
