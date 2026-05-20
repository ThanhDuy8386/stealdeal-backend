using System;

namespace StealDeal.Services.Identity.Domain.Models
{
    public class UserTrustScore
    {
        public Guid Id { get;  set; } = Guid.NewGuid();
        public Guid UserId { get;  set; }
        public int Score { get;  set; }
        public int TotalOrders { get;  set; }
        public int SuccessfulPickups { get;  set; }
        public int NoShowCount { get;  set; }
        public int DisputeCount { get;  set; }
        public DateTime? LastCalculatedAt { get;  set; }

        public User User { get;  set; } = null!;
    }
}
