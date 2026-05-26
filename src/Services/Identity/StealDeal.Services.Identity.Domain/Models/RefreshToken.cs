using System;

namespace StealDeal.Services.Identity.Domain.Models
{
    public class RefreshToken
    {
        public Guid Id { get;  set; } = Guid.NewGuid();
        public Guid UserId { get;  set; }
        public string TokenHash { get;  set; } = null!;
        public DateTime ExpiresAt { get;  set; }
        public bool IsRevoked { get;  set; }
        public DateTime? RevokedAt { get;  set; }
        public DateTime CreatedAt { get;  set; } = DateTime.UtcNow;

        public User User { get;  set; } = null!;
    }
}
