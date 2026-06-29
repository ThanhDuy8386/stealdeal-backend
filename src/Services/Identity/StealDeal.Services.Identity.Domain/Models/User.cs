using System;
using System.Text;
using System.Collections.Generic;

namespace StealDeal.Services.Identity.Domain.Models
{
    public class User
    {
        public Guid Id { get;  set; } = Guid.NewGuid();
        public string Email { get;  set; } = null!;
        public string PasswordHash { get;  set; } = null!;
        public string? Phone { get;  set; }
        public string FullName { get;  set; } = null!;
        public string? AvatarUrl { get;  set; }
        public bool IsEmailVerified { get;  set; }
        public bool IsActive { get;  set; }
        public bool IsDeleted { get;  set; }
        public DateTime CreatedAt { get;  set; } = DateTime.UtcNow;

        public ICollection<RefreshToken> RefreshTokens { get;  set; } = new List<RefreshToken>();
        public ICollection<UserAddress> UserAddresses { get;  set; } = new List<UserAddress>();
        public UserTrustScore? UserTrustScore { get;  set; }
        public ICollection<TrustScoreEvent> TrustScoreEvents { get;  set; } = new List<TrustScoreEvent>();
        public ICollection<UserRole> UserRoles { get;  set; } = new List<UserRole>();
        public ICollection<EmailVerification> EmailVerifications { get; set; } = new List<EmailVerification>();
    }
}
