﻿namespace NServiceBus
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Pipeline;
    using Routing;
    using Transport;

    class ManualRetryNotificationBehavior : IForkConnector<ITransportReceiveContext, ITransportReceiveContext, IRoutingContext>
    {
        const string RetryUniqueMessageIdHeader = "ServiceControl.Retry.UniqueMessageId";

        readonly string errorQueue;

        public ManualRetryNotificationBehavior(string errorQueue)
        {
            this.errorQueue = errorQueue;
        }

        public async Task Invoke(ITransportReceiveContext context, Func<ITransportReceiveContext, Task> next)
        {
            var useRetryAcknowledgement = UseRetryAcknowledgement(out var id);

            if (useRetryAcknowledgement)
            {
                context.Extensions.Set(new MarkAsAcknowledgedBehavior.State());
            }

            await next(context).ConfigureAwait(false);

            if (useRetryAcknowledgement)
            {
                await ConfirmSuccessfulRetry().ConfigureAwait(false);
            }

            async Task ConfirmSuccessfulRetry()
            {
                var messageToDispatch = new OutgoingMessage(
                    CombGuid.Generate().ToString(),
                    new Dictionary<string, string>
                    {
                        { "ServiceControl.Retry.Successful", DateTimeOffset.UtcNow.ToString("O") },
                        { RetryUniqueMessageIdHeader, id },
                        { Headers.ControlMessageHeader, bool.TrueString }
                    },
                    new byte[0]);
                var routingContext = new RoutingContext(messageToDispatch, new UnicastRoutingStrategy(errorQueue), context);
                await this.Fork(routingContext).ConfigureAwait(false);
            }

            bool UseRetryAcknowledgement(out string retryUniqueMessageId)
            {
                // check if the message is coming from a manual retry attempt
                if (context.Message.Headers.TryGetValue(RetryUniqueMessageIdHeader, out var uniqueMessageId) &&
                    // The SC version that supports the confirmation message also started to add the SC version header
                    context.Message.Headers.ContainsKey("ServiceControl.Version"))
                {
                    retryUniqueMessageId = uniqueMessageId;
                    return true;
                }

                retryUniqueMessageId = null;
                return false;
            }
        }
    }
}