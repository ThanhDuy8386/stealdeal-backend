using StealDeal.Services.Cart.Application.DTOs.Responses;
using StealDeal.Services.Cart.Domain.Models;

namespace StealDeal.Services.Cart.Application.Mappings
{
    public static class CartMapping
    {
        public static CartResponse ToResponse(this UserCart cart)
        {
            return new CartResponse
            {
                UserId = cart.UserId,
                StoreId = cart.StoreId,
                Currency = cart.Currency,
                Items = cart.Items
                    .Select(item => item.ToResponse())
                    .ToList(),
                Version = cart.Version,
                CreatedAtUtc = cart.CreatedAtUtc,
                UpdatedAtUtc = cart.UpdatedAtUtc,
                ExpiresAtUtc = cart.ExpiresAtUtc
            };
        }

        private static CartItemResponse ToResponse(this CartItem item)
        {
            return new CartItemResponse
            {
                BagId = item.BagId,
                StoreId = item.StoreId,
                BagNameSnapshot = item.BagNameSnapshot,
                UnitPriceSnapshot = item.UnitPriceSnapshot,
                Quantity = item.Quantity,
                ImageUrlSnapshot = item.ImageUrlSnapshot,
                PickupStartUtc = item.PickupStartUtc,
                PickupEndUtc = item.PickupEndUtc,
                AddedAtUtc = item.AddedAtUtc
            };
        }
    }
}
