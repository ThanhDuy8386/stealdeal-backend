using System.Text.Json;
using StealDeal.Services.Store.Application.DTOs.Events;
using StealDeal.Services.Store.Application.Messaging;
using StealDeal.Services.Store.Domain.Interfaces;
using StealDeal.Services.Store.Domain.Models;

namespace StealDeal.Services.Store.Application.EventHandlers
{
    public class CreateOrderEventHandler : IIntegrationEventHandler<CreateOrderEvent>
    {
        private const string EventsExchangeName = "stealdeal.events";
        private const string EventsExchangeType = "topic";
        private const string InventoryReservedEventType = "inventory.reserved";
        private const string InventoryReservationFailedEventType = "inventory.reservation_failed";

        private static readonly JsonSerializerOptions EventJsonSerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly ISurpriseBagRepository _surpriseBagRepository;
        private readonly IProcessedMessageRepository _processedMessageRepository;
        private readonly IOutboxMessageRepository _outboxMessageRepository;
        private readonly IUnitOfWork _unitOfWork;

        public CreateOrderEventHandler(
            ISurpriseBagRepository surpriseBagRepository,
            IProcessedMessageRepository processedMessageRepository,
            IOutboxMessageRepository outboxMessageRepository,
            IUnitOfWork unitOfWork)
        {
            _surpriseBagRepository = surpriseBagRepository;
            _processedMessageRepository = processedMessageRepository;
            _outboxMessageRepository = outboxMessageRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task HandleAsync(
            CreateOrderEvent @event,
            IntegrationEventContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await _unitOfWork.ExecuteInTransactionAsync(
                    () => HandleInsideTransactionAsync(@event, context, cancellationToken),
                    cancellationToken);
            }
            catch (InventoryReservationFailedException ex)
            {
                await _unitOfWork.ExecuteInTransactionAsync(
                    () => MarkReservationFailedAsync(@event, context, ex.ReasonCode, ex.Message),
                    cancellationToken);
            }
        }

        private async Task HandleInsideTransactionAsync(
            CreateOrderEvent @event,
            IntegrationEventContext context,
            CancellationToken cancellationToken)
        {
            if (await _processedMessageRepository.ExistsAsync(context.MessageId, context.ConsumerName))
            {
                return;
            }

            var requestedItems = @event.Items
                .GroupBy(item => item.SurpriseBagId)
                .Select(group => new RequestedOrderItem
                {
                    SurpriseBagId = group.Key,
                    Quantity = group.Sum(item => item.Quantity)
                })
                .ToList();

            foreach (var item in requestedItems)
            {
                if (item.Quantity <= 0)
                {
                    await MarkReservationFailedAsync(
                        @event,
                        context,
                        "InvalidQuantity",
                        $"Order item '{item.SurpriseBagId}' has invalid quantity.");

                    return;
                }

                var bag = await _surpriseBagRepository.GetByIdAsync(item.SurpriseBagId);

                if (bag == null)
                {
                    await MarkReservationFailedAsync(
                        @event,
                        context,
                        "BagNotFound",
                        $"Surprise bag '{item.SurpriseBagId}' was not found.");

                    return;
                }

                if (bag.StoreId != @event.StoreId)
                {
                    await MarkReservationFailedAsync(
                        @event,
                        context,
                        "StoreMismatch",
                        $"Surprise bag '{item.SurpriseBagId}' does not belong to store '{@event.StoreId}'.");

                    return;
                }

                if (bag.QuantityRemaining < item.Quantity)
                {
                    await MarkReservationFailedAsync(
                        @event,
                        context,
                        "InsufficientStock",
                        $"Surprise bag '{item.SurpriseBagId}' does not have enough stock.");

                    return;
                }
            }

            foreach (var item in requestedItems)
            {
                var reserved = await _surpriseBagRepository.TryReserveQuantityAsync(
                    item.SurpriseBagId,
                    @event.StoreId,
                    item.Quantity,
                    cancellationToken);

                if (!reserved)
                {
                    throw new InventoryReservationFailedException(
                        "InsufficientStock",
                        $"Surprise bag '{item.SurpriseBagId}' no longer has enough stock.");
                }
            }

            await _outboxMessageRepository.AddAsync(CreateInventoryReservedOutboxMessage(@event, requestedItems));
            await AddProcessedMessageAsync(@event, context);
        }

        private async Task MarkReservationFailedAsync(
            CreateOrderEvent @event,
            IntegrationEventContext context,
            string reasonCode,
            string reason)
        {
            if (await _processedMessageRepository.ExistsAsync(context.MessageId, context.ConsumerName))
            {
                return;
            }

            await _outboxMessageRepository.AddAsync(
                CreateInventoryReservationFailedOutboxMessage(@event, reasonCode, reason));

            await AddProcessedMessageAsync(@event, context);
        }

        private async Task AddProcessedMessageAsync(CreateOrderEvent @event, IntegrationEventContext context)
        {
            await _processedMessageRepository.AddAsync(new ProcessedMessage
            {
                MessageId = context.MessageId,
                ConsumerName = context.ConsumerName,
                EventType = context.EventType,
                AggregateId = @event.OrderId,
                ProcessedAt = DateTime.UtcNow
            });
        }

        private static OutboxMessage CreateInventoryReservedOutboxMessage(
            CreateOrderEvent @event,
            IEnumerable<RequestedOrderItem> requestedItems)
        {
            var messageId = Guid.NewGuid();
            var integrationEvent = new InventoryReservedEvent
            {
                MessageId = messageId,
                OccurredAtUtc = DateTime.UtcNow,
                OrderId = @event.OrderId,
                UserId = @event.UserId,
                StoreId = @event.StoreId,
                TotalAmount = @event.TotalAmount,
                Items = requestedItems.Select(item => new InventoryReservedItemDto
                {
                    SurpriseBagId = item.SurpriseBagId,
                    Quantity = item.Quantity
                }).ToList()
            };

            return new OutboxMessage
            {
                Id = messageId,
                EventType = InventoryReservedEventType,
                Payload = JsonSerializer.Serialize(integrationEvent, EventJsonSerializerOptions),
                ExchangeName = EventsExchangeName,
                ExchangeType = EventsExchangeType,
                RoutingKey = InventoryReservedEventType,
                Status = "Pending"
            };
        }

        private static OutboxMessage CreateInventoryReservationFailedOutboxMessage(
            CreateOrderEvent @event,
            string reasonCode,
            string reason)
        {
            var messageId = Guid.NewGuid();
            var integrationEvent = new InventoryReservationFailedEvent
            {
                MessageId = messageId,
                OccurredAtUtc = DateTime.UtcNow,
                OrderId = @event.OrderId,
                StoreId = @event.StoreId,
                ReasonCode = reasonCode,
                Reason = reason
            };

            return new OutboxMessage
            {
                Id = messageId,
                EventType = InventoryReservationFailedEventType,
                Payload = JsonSerializer.Serialize(integrationEvent, EventJsonSerializerOptions),
                ExchangeName = EventsExchangeName,
                ExchangeType = EventsExchangeType,
                RoutingKey = InventoryReservationFailedEventType,
                Status = "Pending"
            };
        }

        private sealed class RequestedOrderItem
        {
            public Guid SurpriseBagId { get; init; }
            public int Quantity { get; init; }
        }

        private sealed class InventoryReservationFailedException : Exception
        {
            public InventoryReservationFailedException(string reasonCode, string message)
                : base(message)
            {
                ReasonCode = reasonCode;
            }

            public string ReasonCode { get; }
        }
    }
}
