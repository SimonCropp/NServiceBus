﻿namespace NServiceBus.AcceptanceTests.Routing;

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AcceptanceTesting;
using AcceptanceTesting.Customization;
using Configuration.AdvancedExtensibility;
using EndpointTemplates;
using NServiceBus.Routing;
using NUnit.Framework;

public class When_extending_command_routing : NServiceBusAcceptanceTest
{
    static string ReceiverEndpoint => Conventions.EndpointNamingConvention(typeof(Receiver));

    [Test]
    public async Task Should_route_commands_correctly()
    {
        var ctx = await Scenario.Define<Context>()
            .WithEndpoint<Sender>(b =>
                b.When(async session =>
                {
                    await session.Send(new MyCommand());
                    await session.Send(new MyCommand());
                    await session.Send(new MyCommand());
                    await session.Send(new MyCommand());
                })
            )
            .WithEndpoint<Receiver>()
            .Done(c => c.MessageDelivered >= 4)
            .Run();

        Assert.That(ctx.MessageDelivered, Is.GreaterThanOrEqualTo(4));
    }

    public class Context : ScenarioContext
    {
        public int MessageDelivered;
    }

    public class Sender : EndpointConfigurationBuilder
    {
        public Sender()
        {
            EndpointSetup<DefaultServer>(c =>
            {
                c.GetSettings().GetOrCreate<UnicastRoutingTable>()
                    .AddOrReplaceRoutes("CustomRoutingFeature",
                    [
                        new RouteTableEntry(typeof(MyCommand), UnicastRoute.CreateFromEndpointName(ReceiverEndpoint))
                    ]);
                c.GetSettings().GetOrCreate<EndpointInstances>()
                    .AddOrReplaceInstances("CustomRoutingFeature",
                    [
                        new EndpointInstance(ReceiverEndpoint, "XYZ"),
                        new EndpointInstance(ReceiverEndpoint, "ABC")
                    ]);
                c.GetSettings().GetOrCreate<DistributionPolicy>()
                    .SetDistributionStrategy(new XyzDistributionStrategy(ReceiverEndpoint));
            });
        }

        class XyzDistributionStrategy : DistributionStrategy
        {
            public XyzDistributionStrategy(string endpoint) : base(endpoint, DistributionStrategyScope.Send)
            {
            }

            public override string SelectDestination(DistributionContext context)
            {
                var address = context.ToTransportAddress(new EndpointInstance(ReceiverEndpoint, "XYZ"));
                return context.ReceiverAddresses.First(x => x == address);
            }
        }
    }

    public class Receiver : EndpointConfigurationBuilder
    {
        public Receiver()
        {
            EndpointSetup<DefaultServer>(c => c.MakeInstanceUniquelyAddressable("XYZ"));
        }

        public class MyCommandHandler : IHandleMessages<MyCommand>
        {
            public MyCommandHandler(Context context)
            {
                testContext = context;
            }

            public Task Handle(MyCommand evnt, IMessageHandlerContext context)
            {
                Interlocked.Increment(ref testContext.MessageDelivered);
                return Task.CompletedTask;
            }

            Context testContext;
        }
    }

    public class MyCommand : ICommand
    {
    }
}