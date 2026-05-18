using System;
using System.Text;
using System.Collections.Generic;

namespace StealDeal.Services.Identity.Domain.Models
{
    public class User
    {
        public Guid Id { get; protected set; } = Guid.NewGuid();
        public string Email { get; private set; } = null!;
        public string PasswordHash { get; private set; } = null!;
        public string? Phone { get; private set; }
        public string FullName { get; private set; } = null!;
        public string? AvatarUrl { get; private set; }
        public bool IsEmailVerify { get; private set; }
        public bool IsActive { get; private set; }
        public bool IsDeleted { get; private set; }
        public DateTime CreatedAt { get; protected set; } = DateTime.UtcNow;

        public ICollection<OauthProvider> OauthProviders { get; protected set; } = new List<OauthProvider>();
        public ICollection<RefreshToken> RefreshTokens { get; protected set; } = new List<RefreshToken>();
        public ICollection<UserAddress> UserAddresses { get; protected set; } = new List<UserAddress>();
        public UserTrustScore? UserTrustScores { get; protected set; }
        public ICollection<TrustScoreEvent> TrustScoreEvents { get; protected set; } = new List<TrustScoreEvent>();
        public ICollection<UserRole> UserRoles { get; protected set; } = new List<UserRole>();
    }
}
