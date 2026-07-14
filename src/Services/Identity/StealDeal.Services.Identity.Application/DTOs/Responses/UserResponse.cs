using StealDeal.Services.Identity.Domain.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Identity.Application.DTOs.Responses
{
    // removed fields that may not be necessary for the response, such as PasswordHash
    public class UserResponse
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Email { get; set; } = null!;
        public string? Phone { get; set; }
        public string FullName { get; set; } = null!;
        public string? AvatarUrl { get; set; }
        public bool IsEmailVerified { get; set; }
        public bool IsActive { get; set; }
        // public bool IsDeleted { get; set; }
        public DateTime CreatedAt { get; set; }

        //public ICollection<UserAddress> UserAddresses { get; set; } = new List<UserAddress>();
        public UserTrustScore? UserTrustScore { get; set; }
        public List<string> Roles { get; set; } = [];
    }
}
