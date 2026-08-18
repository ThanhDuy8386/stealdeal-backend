using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StealDeal.Services.Identity.Application.Messaging
{
    public interface IIntegrationEventHandler<in TEvent>
    {
        Task HandleAsync(TEvent @event, IntegrationEventContext context, CancellationToken cancellationToken = default);
    }
}
