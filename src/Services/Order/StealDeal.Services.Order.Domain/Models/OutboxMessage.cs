using System;

namespace StealDeal.Services.Order.Domain.Models
{
    public class OutboxMessage
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string EventType { get; set; } = null!;
        public string Payload { get; set; } = null!;
        public string Status { get; set; } = "Pending";
        public int RetryCount { get; set; } = 0;
        public string ExchangeName { get; set; } = null!;
        public string ExchangeType { get; set; } = "topic";
        public string RoutingKey { get; set; } = null!;
        public DateTime? ProcessedAt { get; set; }
        public string? Error { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
