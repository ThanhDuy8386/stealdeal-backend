using StealDeal.Services.Identity.Application.DTOs.Responses;
using StealDeal.Services.Identity.Domain.Models;

namespace StealDeal.Services.Identity.Application.Mappings
{
    public static class AdminMapping
    {
        public static AdminResponse ToAdminResponse(this Admin admin)
        {
            return new AdminResponse
            {
                Id = admin.Id,
                Email = admin.Email,
                Phone = admin.Phone,
                FullName = admin.FullName,
                AvatarUrl = admin.AvatarUrl,
                IsEmailVerified = admin.IsEmailVerified,
                IsActive = admin.IsActive,
                CreatedAt = admin.CreatedAt,
                Roles = admin.Roles.Select(role => role.Name).ToList()
            };
        }

        public static AdminDetailResponse ToAdminDetailResponse(this Admin admin)
        {
            return new AdminDetailResponse
            {
                Id = admin.Id,
                Email = admin.Email,
                Phone = admin.Phone,
                FullName = admin.FullName,
                AvatarUrl = admin.AvatarUrl,
                IsEmailVerified = admin.IsEmailVerified,
                IsActive = admin.IsActive,
                CreatedAt = admin.CreatedAt,
                Roles = admin.Roles.Select(role => role.Name).ToList()
            };
        }
    }
}
