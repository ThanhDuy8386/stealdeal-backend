using StealDeal.Services.Identity.Domain.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Identity.Application.DTOs.Responses
{
    public class UserDetailResponse
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? FullName { get; set; }
        public string? AvatarUrl { get; set; }
        public bool IsEmailVerified { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<UserAddressResponse> UserAddresses { get; set; } = [];
        public UserTrustScoreResponse? UserTrustScore { get; set; }
        public List<String> Roles { get; set; } = [];
    }
}
