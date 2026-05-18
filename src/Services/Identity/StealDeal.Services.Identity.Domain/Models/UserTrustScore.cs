using System;

namespace StealDeal.Services.Identity.Domain.Models
{
    public class UserTrustScore
    {
        public Guid Id { get; protected set; } = Guid.NewGuid();
        public Guid UserId { get; protected set; }
        public int Score { get; protected set; }
        public int TotalOrders { get; protected set; }
        public int SuccessfulPickups { get; protected set; }
        public int NoShowCount { get; protected set; }
        public int DisputeCount { get; protected set; }
        public DateTime? LastCalculatedAt { get; protected set; }

        public User User { get; protected set; } = null!;
    }
}
