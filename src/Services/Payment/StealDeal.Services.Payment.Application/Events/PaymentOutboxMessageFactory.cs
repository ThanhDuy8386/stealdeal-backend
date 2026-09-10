using System.Text.Json;
using StealDeal.Services.Payment.Application.DTOs.Events;
using StealDeal.Services.Payment.Domain.Models;

namespace StealDeal.Services.Payment.Application.Events
{
    public static class PaymentOutboxMessageFactory
    {
        private const string EventsExchangeName = "stealdeal.events";
        private const string EventsExchangeType = "topic";

        private static readonly JsonSerializerOptions EventJsonSerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public static OutboxMessage CreatePaymentCompleted(Transaction transaction)
        {
            var messageId = Guid.NewGuid();
            var integrationEvent = new PaymentCompletedEvent
            {
                MessageId = messageId,
                OccurredAtUtc = DateTime.UtcNow,
                OrderId = transaction.OrderId,
                PaymentId = transaction.Id,
                Amount = transaction.Amount,
                PaymentMethod = transaction.PaymentMethod,
                GatewayRef = transaction.GatewayRef
            };

            return CreateOutboxMessage(
                messageId,
                PaymentEventTypes.PaymentCompleted,
                integrationEvent);
        }

        public static OutboxMessage CreatePaymentFailed(
            Transaction transaction,
            string reasonCode,
            string reason)
        {
            var messageId = Guid.NewGuid();
            var integrationEvent = new PaymentFailedEvent
            {
                MessageId = messageId,
                OccurredAtUtc = DateTime.UtcNow,
                OrderId = transaction.OrderId,
                PaymentId = transaction.Id,
                ReasonCode = reasonCode,
                Reason = reason
            };

            return CreateOutboxMessage(
                messageId,
                PaymentEventTypes.PaymentFailed,
                integrationEvent);
        }

        public static OutboxMessage CreateInventoryReleaseRequested(
            Transaction transaction,
            IEnumerable<InventoryReleaseRequestedItemDto> items,
            string reasonCode,
            string reason)
        {
            var messageId = Guid.NewGuid();
            var integrationEvent = new InventoryReleaseRequestedEvent
            {
                MessageId = messageId,
                OccurredAtUtc = DateTime.UtcNow,
                OrderId = transaction.OrderId,
                StoreId = transaction.StoreId!.Value,
                ReasonCode = reasonCode,
                Reason = reason,
                Items = items.ToList()
            };

            return CreateOutboxMessage(
                messageId,
                PaymentEventTypes.InventoryReleaseRequested,
                integrationEvent);
        }

        private static OutboxMessage CreateOutboxMessage<TEvent>(
            Guid messageId,
            string eventType,
            TEvent integrationEvent)
        {
            return new OutboxMessage
            {
                Id = messageId,
                EventType = eventType,
                Payload = JsonSerializer.Serialize(integrationEvent, EventJsonSerializerOptions),
                ExchangeName = EventsExchangeName,
                ExchangeType = EventsExchangeType,
                RoutingKey = eventType,
                Status = "Pending"
            };
        }
    }
}
