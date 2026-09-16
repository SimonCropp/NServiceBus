namespace NServiceBus.Benchmarks;

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Extensibility;
using Transport;
using Unicast.Messages;

/// <summary>
/// Minimal in-memory transport. It captures the core's <see cref="OnMessage"/> callback so a benchmark
/// can push messages straight into the real receive pipeline, and discards anything dispatched.
/// </summary>
/// <remarks>
/// Doing no real I/O keeps transport latency out of the measurement. It also means a real endpoint does
/// strictly more work per message than this harness reports, so any proportional figure derived from it
/// is an upper bound on the share attributable to core.
/// </remarks>
public class BenchmarkTransport() : TransportDefinition(
    TransportTransactionMode.ReceiveOnly,
    supportsDelayedDelivery: false,
    supportsPublishSubscribe: true,
    supportsTTBR: false)
{
    /// <summary>
    /// The core's incoming message callback, captured when the main receiver is initialized.
    /// </summary>
    public static OnMessage Push { get; private set; }

    public override Task<TransportInfrastructure> Initialize(HostSettings hostSettings, ReceiveSettings[] receivers, string[] sendingAddresses, CancellationToken cancellationToken = default)
        => Task.FromResult<TransportInfrastructure>(new BenchmarkInfrastructure(receivers));

    public override IReadOnlyCollection<TransportTransactionMode> GetSupportedTransactionModes()
        => [TransportTransactionMode.None, TransportTransactionMode.ReceiveOnly];

    sealed class BenchmarkInfrastructure : TransportInfrastructure
    {
        public BenchmarkInfrastructure(ReceiveSettings[] receivers)
        {
            Dispatcher = new BenchmarkDispatcher();
            Receivers = receivers.ToDictionary(r => r.Id, r => (IMessageReceiver)new BenchmarkReceiver(r));
        }

        public override Task Shutdown(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public override string ToTransportAddress(QueueAddress address) => address.BaseAddress;
    }

    sealed class BenchmarkDispatcher : IMessageDispatcher
    {
        public Task Dispatch(TransportOperations outgoingMessages, TransportTransaction transaction, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    sealed class BenchmarkSubscriptionManager : ISubscriptionManager
    {
        public Task SubscribeAll(MessageMetadata[] eventTypes, ContextBag context, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task Unsubscribe(MessageMetadata eventType, ContextBag context, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    sealed class BenchmarkReceiver(ReceiveSettings settings) : IMessageReceiver
    {
        public Task Initialize(PushRuntimeSettings limitations, OnMessage onMessage, OnError onError, CancellationToken cancellationToken = default)
        {
            if (settings.Id == "Main")
            {
                Push = onMessage;
            }

            return Task.CompletedTask;
        }

        public Task StartReceive(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ChangeConcurrency(PushRuntimeSettings limitations, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopReceive(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ISubscriptionManager Subscriptions { get; } = new BenchmarkSubscriptionManager();

        public string Id => settings.Id;

        public string ReceiveAddress => settings.ReceiveAddress.BaseAddress;
    }
}
