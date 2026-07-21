using StealDeal.Services.Identity.Application.DTOs.Responses;
using StealDeal.Services.Identity.Domain.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Identity.Application.Mappings
{
    public static class UserMapping
    {
        public static UserResponse ToUserResponse(this User user)
        {
            return new UserResponse
            {
                Id = user.Id,
                Email = user.Email,
                Phone = user.Phone,
                FullName = user.FullName,
                AvatarUrl = user.AvatarUrl,
                IsEmailVerified = user.IsEmailVerified,
                IsActive = user.IsActive,
                CreatedAt = user.CreatedAt,
                Roles = user.Roles.Select(r => r.Name).ToList()
            };
        }

        public static UserDetailResponse ToUserDetailResponse(this User user)
        {
            return new UserDetailResponse
            {
                Id = user.Id,
                Email = user.Email,
                Phone = user.Phone,
                FullName = user.FullName,
                AvatarUrl = user.AvatarUrl,
                IsEmailVerified = user.IsEmailVerified,
                IsActive = user.IsActive,
                CreatedAt = user.CreatedAt,
                UserAddresses = user.UserAddresses.Select(a => new UserAddressResponse
                {
                    Id = a.Id,
                    Label = a.Label,
                    Address = a.Address,
                    District = a.District,
                    City = a.City,
                    IsDefault = a.IsDefault
                }).ToList(),
                UserTrustScore = user.UserTrustScore != null ? new UserTrustScoreResponse
                {
                    Id = user.UserTrustScore.Id,
                    Score = user.UserTrustScore.Score,
                    TotalOrders = user.UserTrustScore.TotalOrders,
                    SuccessfulPickups = user.UserTrustScore.SuccessfulPickups,
                    NoShowCount = user.UserTrustScore.NoShowCount,
                    DisputeCount = user.UserTrustScore.DisputeCount,
                    LastCalculatedAt = user.UserTrustScore.LastCalculatedAt
                } : null,
                Roles = user.Roles.Select(r => r.Name).ToList()
            };
        }
    }
}
