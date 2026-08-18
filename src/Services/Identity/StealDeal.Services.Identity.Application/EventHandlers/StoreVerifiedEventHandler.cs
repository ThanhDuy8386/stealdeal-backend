using System;
using System.Collections.Generic;
using System.Text;
using StealDeal.Services.Identity.Application.DTOs.Events;
using StealDeal.Services.Identity.Application.Messaging;
using StealDeal.Services.Identity.Application.Services.Interfaces;

namespace StealDeal.Services.Identity.Application.EventHandlers
{
    public class StoreVerifiedEventHandler : IIntegrationEventHandler<StoreVerifiedEvent>
    {
        private readonly IUserService _userService;

        public StoreVerifiedEventHandler(IUserService userService)
        {
            _userService = userService;
        }

        public async Task HandleAsync(StoreVerifiedEvent @event, IntegrationEventContext context, CancellationToken cancellationToken = default)
        {
            await _userService.PromoteToSeller(@event.OwnerId);
        }
    }
}
