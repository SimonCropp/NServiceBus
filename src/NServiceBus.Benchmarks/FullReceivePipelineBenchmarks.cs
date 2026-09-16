namespace NServiceBus.Benchmarks;

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Extensibility;
using Microsoft.Extensions.DependencyInjection;
using Transport;

/// <summary>
/// Drives the full receive pipeline of a real, started endpoint for a single incoming message:
/// the pipeline executor, every registered behavior, deserialization, handler invocation, and the
/// complete outgoing pipeline for anything the handler sends.
/// </summary>
/// <remarks>
/// Use this to put a change measured in isolation into proportion. A saving that looks large against a
/// single connector is usually a small share of what a message costs end to end.
/// </remarks>
[MemoryDiagnoser(false)]
public class FullReceivePipelineBenchmarks
{
    ServiceProvider provider;
    byte[] body;
    Dictionary<string, string> headers;

    /// <summary>
    /// How many messages the handler sends while processing this one.
    /// </summary>
    [Params(0, 1)]
    public int OutgoingMessages { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        BenchmarkHandler.Outgoing = OutgoingMessages;

        var config = new EndpointConfiguration("benchmarks");
        var routing = config.UseTransport(new BenchmarkTransport());
        routing.RouteToEndpoint(typeof(BenchmarkMessage), "benchmarks");
        config.SendFailedMessagesTo("error");
        config.UseSerialization<SystemJsonSerializer>();
        config.Recoverability().Delayed(s => s.NumberOfRetries(0));
        config.EnableInstallers();
        config.UsePersistence<LearningPersistence>();
        config.AddHandler<BenchmarkHandler>();
        config.AddMessageType<BenchmarkMessage>();

        var services = new ServiceCollection();
        services.AddNServiceBusEndpoint(config);
        provider = services.BuildServiceProvider();
        provider.GetRequiredService<IEndpointLifecycle>().CreateAndStart().AsTask().GetAwaiter().GetResult();

        body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new BenchmarkMessage { Value = 42 }));
        headers = new Dictionary<string, string>
        {
            [Headers.EnclosedMessageTypes] = typeof(BenchmarkMessage).FullName,
            [Headers.MessageId] = Guid.NewGuid().ToString(),
            [Headers.MessageIntent] = nameof(MessageIntent.Send),
            [Headers.ContentType] = "application/json"
        };

        // Fail loudly rather than silently benchmarking a pipeline that never reaches the handler.
        BenchmarkHandler.Invocations = 0;
        ProcessOneMessage().GetAwaiter().GetResult();
        if (BenchmarkHandler.Invocations != 1)
        {
            throw new Exception($"Handler ran {BenchmarkHandler.Invocations} times, expected exactly 1.");
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (provider is null)
        {
            return;
        }

        provider.GetRequiredService<IEndpointLifecycle>().Stop().AsTask().GetAwaiter().GetResult();
        // The endpoint lifecycle is IAsyncDisposable only, so the container has to be disposed asynchronously.
        provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

#pragma warning disable PS0018 // BenchmarkDotNet requires parameterless benchmark methods
    [Benchmark(Description = "full receive pipeline, one message")]
    public Task ProcessOneMessage()
#pragma warning restore PS0018
    {
        var context = new MessageContext(
            Guid.NewGuid().ToString(),
            new Dictionary<string, string>(headers),
            body,
            new TransportTransaction(),
            "benchmarks",
            new ContextBag());

        return BenchmarkTransport.Push(context);
    }

    public class BenchmarkMessage : IMessage
    {
        public int Value { get; set; }
    }

    public class BenchmarkHandler : IHandleMessages<BenchmarkMessage>
    {
        internal static int Outgoing { get; set; }

        internal static int Invocations { get; set; }

        public async Task Handle(BenchmarkMessage message, IMessageHandlerContext context)
        {
            Invocations++;

            for (var i = 0; i < Outgoing; i++)
            {
                await context.Send(new BenchmarkMessage { Value = i }).ConfigureAwait(false);
            }
        }
    }
}
