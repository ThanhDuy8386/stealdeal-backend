using StealDeal.Services.Store.Application.DTOs.Requests;
using StealDeal.Services.Store.Application.DTOs.Responses;
using StealDeal.Services.Store.Domain.Models;

namespace StealDeal.Services.Store.Application.Mappings
{
    public static class SurpriseBagMapping
    {
        // CreateRequest -> new Entity (Categories assigned separately after loading from DB)
        public static SurpriseBag ToEntity(this CreateBagRequest request, Guid storeId)
        {
            return new SurpriseBag
            {
                StoreId = storeId,
                Name = request.Name.Trim(),
                Description = request.Description?.Trim(),
                OriginalPrice = request.OriginalPrice,
                SalePrice = request.SalePrice,
                QuantityTotal = request.QuantityTotal,
                QuantityRemaining = request.QuantityTotal, // starts full
                PickupStartTime = request.PickupStartTime,
                PickupEndTime = request.PickupEndTime,
                ExpiryDate = request.ExpiryDate,
                Status = request.Status,
                CreatedAt = DateTime.UtcNow
            };
        }

        // UpdateRequest -> existing Entity (Categories re-assigned separately)
        public static void UpdateEntity(this UpdateBagRequest request, SurpriseBag bag)
        {
            bag.Name = request.Name.Trim();
            bag.Description = request.Description?.Trim();
            bag.OriginalPrice = request.OriginalPrice;
            bag.SalePrice = request.SalePrice;
            bag.QuantityTotal = request.QuantityTotal;
            bag.PickupStartTime = request.PickupStartTime;
            bag.PickupEndTime = request.PickupEndTime;
            bag.ExpiryDate = request.ExpiryDate;
            bag.UpdatedAt = DateTime.UtcNow;
        }

        // Entity -> Response DTO (requires Store + Categories navigation properties loaded)
        public static SurpriseBagResponse ToResponse(this SurpriseBag bag)
        {
            return new SurpriseBagResponse
            {
                Id = bag.Id,
                StoreId = bag.StoreId,
                StoreName = bag.Store?.Name ?? string.Empty,
                Name = bag.Name,
                Description = bag.Description,
                OriginalPrice = bag.OriginalPrice,
                SalePrice = bag.SalePrice,
                QuantityTotal = bag.QuantityTotal,
                QuantityRemaining = bag.QuantityRemaining,
                PickupStartTime = bag.PickupStartTime,
                PickupEndTime = bag.PickupEndTime,
                ExpiryDate = bag.ExpiryDate,
                Status = bag.Status,
                Categories = bag.Categories
                    .Select(c => c.ToResponse())
                    .ToList(),
                CreatedAt = bag.CreatedAt
            };
        }
    }
}
