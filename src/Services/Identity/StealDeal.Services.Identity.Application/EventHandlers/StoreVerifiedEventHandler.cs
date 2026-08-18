using StealDeal.Services.Identity.Application.DTOs.Events;
using StealDeal.Services.Identity.Application.Messaging;
using StealDeal.Services.Identity.Application.Services.Interfaces;
using StealDeal.Services.Identity.Domain.Interfaces.Repositories;
using StealDeal.Services.Identity.Domain.Models;

namespace StealDeal.Services.Identity.Application.EventHandlers;

public class StoreVerifiedEventHandler
    : IIntegrationEventHandler<StoreVerifiedEvent>
{
    private readonly IUserService _userService;
    private readonly IProcessedMessageRepository
        _processedMessageRepository;
    private readonly IUnitOfWork _unitOfWork;

    public StoreVerifiedEventHandler(
        IUserService userService,
        IProcessedMessageRepository processedMessageRepository,
        IUnitOfWork unitOfWork)
    {
        _userService = userService;
        _processedMessageRepository =
            processedMessageRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task HandleAsync(
        StoreVerifiedEvent @event,
        IntegrationEventContext context,
        CancellationToken cancellationToken = default)
    {
        var alreadyProcessed =
            await _processedMessageRepository.ExistsAsync(
                context.MessageId,
                context.ConsumerName);

        if (alreadyProcessed)
        {
            return;
        }

        await _userService.PromoteToSeller(
            @event.OwnerId);

        await _processedMessageRepository.AddAsync(
            new ProcessedMessage
            {
                MessageId = context.MessageId,
                ConsumerName = context.ConsumerName,
                EventType = context.EventType,
                AggregateId = @event.StoreId,
                ProcessedAt = DateTime.UtcNow
            });

        await _unitOfWork.SaveChangesAsync();
    }
}