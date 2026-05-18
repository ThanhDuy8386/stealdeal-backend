using System;

namespace StealDeal.Services.Identity.Domain.Models
{
    public class RefreshToken
    {
        public Guid Id { get; protected set; } = Guid.NewGuid();
        public Guid UserId { get; protected set; }
        public string TokenHash { get; protected set; } = null!;
        public DateTime ExpiresAt { get; protected set; }
        public bool IsRevoked { get; protected set; }
        public DateTime CreatedAt { get; protected set; } = DateTime.UtcNow;

        public User User { get; protected set; } = null!;
    }
}
