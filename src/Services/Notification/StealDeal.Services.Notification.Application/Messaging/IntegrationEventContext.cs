using System;

namespace StealDeal.Services.Notification.Application.Messaging
{
    public class IntegrationEventContext
    {
        public Guid MessageId { get; set; }
        public string ConsumerName { get; set; } = null!;
        public string EventType { get; set; } = null!;
        public string RoutingKey { get; set; } = null!;
        public DateTime? OccurredAt { get; set; }
    }
}
