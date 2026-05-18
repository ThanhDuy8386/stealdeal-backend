using System;

namespace StealDeal.Services.Identity.Domain.Models
{
    public class TrustScoreEvent
    {
        public Guid Id { get; protected set; } = Guid.NewGuid();
        public Guid UserId { get; protected set; }
        public string EventType { get; protected set; } = null!;
        public int ScoreDelta { get; protected set; }
        public int ScoreAfter { get; protected set; }
        public string? ReferenceId { get; protected set; }
        public string? ReferenceType { get; protected set; }
        public string? Note { get; protected set; }
        public DateTime CreatedAt { get; protected set; } = DateTime.UtcNow;

        public User User { get; protected set; } = null!;
    }
}
