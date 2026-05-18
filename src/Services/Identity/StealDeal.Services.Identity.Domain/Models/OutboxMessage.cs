using System;

namespace StealDeal.Services.Identity.Domain.Models
{
    public class OutboxMessage
    {
        public Guid Id { get; protected set; } = Guid.NewGuid();
        public string EventType { get; protected set; } = null!;
        public string Payload { get; protected set; } = null!;
        public DateTime? ProcessedAt { get; protected set; }
        public string? Error { get; protected set; }
        public DateTime CreatedAt { get; protected set; } = DateTime.UtcNow;
    }
}
