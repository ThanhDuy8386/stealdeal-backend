using System;

namespace StealDeal.Services.Identity.Domain.Models
{
    public class TrustScoreEvent
    {
        public Guid Id { get;  set; } = Guid.NewGuid();
        public Guid UserId { get;  set; }
        public string EventType { get;  set; } = null!;
        public int ScoreDelta { get;  set; }
        public int ScoreAfter { get;  set; }
        public string? ReferenceId { get;  set; }
        public string? ReferenceType { get;  set; }
        public string? Note { get;  set; }
        public DateTime CreatedAt { get;  set; } = DateTime.UtcNow;

        public User User { get;  set; } = null!;
    }
}
