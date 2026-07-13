using StealDeal.Services.Store.Application.DTOs.Requests;
using StealDeal.Services.Store.Application.DTOs.Responses;
using StealDeal.Services.Store.Domain.Models;

namespace StealDeal.Services.Store.Application.Mappings
{
    public static class StoreProfileMapping
    {
        // CreateRequest -> new Entity
        public static StoreProfile ToEntity(this CreateStoreRequest request, Guid ownerId)
        {
            return new StoreProfile
            {
                OwnerId = ownerId,
                Name = request.Name.Trim(),
                Description = request.Description?.Trim(),
                Address = request.Address?.Trim(),
                Latitude = request.Latitude,
                Longitude = request.Longitude,
                Phone = request.Phone?.Trim(),
                BankAccount = request.BankAccount?.Trim(),
                LicenseUrl = request.LicenseUrl?.Trim(),
                RatingScore = 0,
                IsVerify = false,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
        }

        // UpdateRequest -> existing Entity
        public static void UpdateEntity(this UpdateStoreRequest request, StoreProfile store)
        {
            store.Name = request.Name.Trim();
            store.Description = request.Description?.Trim();
            store.Address = request.Address?.Trim();
            store.Latitude = request.Latitude;
            store.Longitude = request.Longitude;
            store.Phone = request.Phone?.Trim();
            store.BankAccount = request.BankAccount?.Trim();
            store.LicenseUrl = request.LicenseUrl?.Trim();
            store.UpdatedAt = DateTime.UtcNow;
        }

        // Entity -> Response DTO
        public static StoreProfileResponse ToResponse(this StoreProfile store)
        {
            return new StoreProfileResponse
            {
                Id = store.Id,
                OwnerId = store.OwnerId,
                Name = store.Name,
                Description = store.Description,
                Address = store.Address,
                Latitude = store.Latitude,
                Longitude = store.Longitude,
                AvatarUrl = store.AvatarUrl,
                Phone = store.Phone,
                RatingScore = store.RatingScore,
                IsVerify = store.IsVerify,
                IsActive = store.IsActive,
                CreatedAt = store.CreatedAt
            };
        }
    }
}
