using System;
using System.Collections.Generic;

namespace StealDeal.Services.Store.Domain.Models
{
    public class ProcessedMessage
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid MessageId { get; set; }
        public string ConsumerName { get; set; } = null!;
        public string EventType { get; set; } = null!;
        public Guid? AggregateId { get; set; }
        public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
    }
}
