namespace NServiceBus.Benchmarks;

using System;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Extensibility;
using Outbox;
using Pipeline;
using Routing;
using Testing;
using Transport;

/// <summary>
/// Drives the real <see cref="TransportReceiveToPhysicalMessageConnector"/> for a single incoming
/// message, the same way its unit tests do, with the outbox both enabled and disabled.
/// </summary>
/// <remarks>
/// The allocation column is the meaningful one: it is deterministic and reproduces exactly. Timings at
/// this scale carry several percent of run-to-run variance, so treat small deltas as noise.
/// </remarks>
[MemoryDiagnoser(false)]
public class ReceiveConnectorBenchmarks
{
    TransportReceiveToPhysicalMessageConnector connector;
    Func<IIncomingPhysicalMessageContext, Task> next;

    /// <summary>
    /// How many messages the handler sends or publishes while processing this one.
    /// </summary>
    [Params(0, 1, 2)]
    public int OutgoingMessages { get; set; }

    [Params(false, true)]
    public bool OutboxEnabled { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        IOutboxStorage storage = OutboxEnabled ? new BenchmarkOutboxStorage() : new NoOpOutboxStorage();
        connector = new TransportReceiveToPhysicalMessageConnector(
            storage, new IncomingPipelineMetrics(new BenchmarkMeterFactory(), "queue", "discriminator"));

        var outgoing = OutgoingMessages;
        next = ctx =>
        {
            if (outgoing > 0)
            {
                var pending = ctx.Extensions.Get<PendingTransportOperations>();
                for (var i = 0; i < outgoing; i++)
                {
                    pending.Add(new Transport.TransportOperation(
                        new OutgoingMessage($"out-{i}", [], Array.Empty<byte>()),
                        new UnicastAddressTag("destination")));
                }
            }

            return Task.CompletedTask;
        };

        // Fail loudly rather than silently benchmarking a no-op.
        var dispatched = Invoke().GetAwaiter().GetResult();
        if (dispatched != OutgoingMessages)
        {
            throw new Exception($"Expected {OutgoingMessages} dispatched operations but got {dispatched}.");
        }
    }

#pragma warning disable PS0018 // BenchmarkDotNet requires parameterless benchmark methods
    [Benchmark(Description = "TransportReceiveToPhysicalMessageConnector.Invoke, one message")]
    public async Task<int> Invoke()
#pragma warning restore PS0018
    {
        var pipeline = new BenchmarkBatchPipeline();
        var context = new TestableTransportReceiveContext
        {
            Message = new IncomingMessage("id", [], Array.Empty<byte>())
        };
        context.Extensions.Set<IPipelineCache>(new BenchmarkPipelineCache(pipeline));

        await connector.Invoke(context, next).ConfigureAwait(false);

        return pipeline.DispatchedCount;
    }

    sealed class BenchmarkPipelineCache(IPipeline<IBatchDispatchContext> pipeline) : IPipelineCache
    {
        public IPipeline<TContext> Pipeline<TContext>() where TContext : IBehaviorContext => (IPipeline<TContext>)pipeline;
    }

    sealed class BenchmarkBatchPipeline : IPipeline<IBatchDispatchContext>
    {
        public int DispatchedCount { get; private set; }

        public Task Invoke(IBatchDispatchContext context)
        {
            DispatchedCount = context.Operations.Count;
            return Task.CompletedTask;
        }
    }

    sealed class BenchmarkOutboxStorage : IOutboxStorage
    {
        public Task<OutboxMessage> Get(string messageId, ContextBag options, CancellationToken cancellationToken = default)
            => Task.FromResult<OutboxMessage>(null);

        public Task Store(OutboxMessage message, IOutboxTransaction transaction, ContextBag options, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SetAsDispatched(string messageId, ContextBag options, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IOutboxTransaction> BeginTransaction(ContextBag context, CancellationToken cancellationToken = default)
            => Task.FromResult<IOutboxTransaction>(new BenchmarkOutboxTransaction());
    }

    sealed class BenchmarkOutboxTransaction : IOutboxTransaction
    {
        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => default;

        public Task Commit(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    sealed class BenchmarkMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options);

        public void Dispose()
        {
        }
    }
}
