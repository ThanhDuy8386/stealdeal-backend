using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Identity.Domain.Models
{
    public class PasswordReset
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }
        public string OtpHash { get; set; } = null!;
        public DateTime ExpiresAt { get; set; }
        public DateTime? ConsumedAt { get; set; }
        public DateTime? RevokedAt { get; set; }
        public int AttemptCount { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public User User { get; set; } = null!;
    }
}
