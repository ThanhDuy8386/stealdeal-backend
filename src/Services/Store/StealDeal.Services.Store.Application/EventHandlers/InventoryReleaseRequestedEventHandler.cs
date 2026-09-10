using StealDeal.Services.Store.Application.DTOs.Events;
using StealDeal.Services.Store.Application.Messaging;
using StealDeal.Services.Store.Domain.Interfaces;
using StealDeal.Services.Store.Domain.Models;

namespace StealDeal.Services.Store.Application.EventHandlers
{
    public class InventoryReleaseRequestedEventHandler : IIntegrationEventHandler<InventoryReleaseRequestedEvent>
    {
        private readonly ISurpriseBagRepository _surpriseBagRepository;
        private readonly IProcessedMessageRepository _processedMessageRepository;
        private readonly IUnitOfWork _unitOfWork;

        public InventoryReleaseRequestedEventHandler(
            ISurpriseBagRepository surpriseBagRepository,
            IProcessedMessageRepository processedMessageRepository,
            IUnitOfWork unitOfWork)
        {
            _surpriseBagRepository = surpriseBagRepository;
            _processedMessageRepository = processedMessageRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task HandleAsync(
            InventoryReleaseRequestedEvent @event,
            IntegrationEventContext context,
            CancellationToken cancellationToken = default)
        {
            await _unitOfWork.ExecuteInTransactionAsync(
                () => HandleInsideTransactionAsync(@event, context, cancellationToken),
                cancellationToken);
        }

        private async Task HandleInsideTransactionAsync(
            InventoryReleaseRequestedEvent @event,
            IntegrationEventContext context,
            CancellationToken cancellationToken)
        {
            if (await _processedMessageRepository.ExistsAsync(context.MessageId, context.ConsumerName))
            {
                return;
            }

            var releaseItems = @event.Items
                .GroupBy(item => item.SurpriseBagId)
                .Select(group => new ReleaseItem
                {
                    SurpriseBagId = group.Key,
                    Quantity = group.Sum(item => item.Quantity)
                })
                .ToList();

            foreach (var item in releaseItems)
            {
                if (item.Quantity <= 0)
                {
                    throw new InvalidOperationException(
                        $"Release item '{item.SurpriseBagId}' has invalid quantity.");
                }

                var released = await _surpriseBagRepository.TryReleaseQuantityAsync(
                    item.SurpriseBagId,
                    @event.StoreId,
                    item.Quantity,
                    cancellationToken);

                if (!released)
                {
                    throw new InvalidOperationException(
                        $"Surprise bag '{item.SurpriseBagId}' could not be released for store '{@event.StoreId}'.");
                }
            }

            await _processedMessageRepository.AddAsync(new ProcessedMessage
            {
                MessageId = context.MessageId,
                ConsumerName = context.ConsumerName,
                EventType = context.EventType,
                AggregateId = @event.OrderId,
                ProcessedAt = DateTime.UtcNow
            });
        }

        private sealed class ReleaseItem
        {
            public Guid SurpriseBagId { get; init; }
            public int Quantity { get; init; }
        }
    }
}
