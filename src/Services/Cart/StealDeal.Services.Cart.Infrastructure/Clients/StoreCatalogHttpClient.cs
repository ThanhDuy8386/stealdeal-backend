using StealDeal.Services.Cart.Application.DTOs.Responses;
using StealDeal.Services.Cart.Application.Services.Interfaces;
using System.Net;
using System.Net.Http.Json;

namespace StealDeal.Services.Cart.Infrastructure.Clients
{
    public class StoreCatalogHttpClient : IStoreCatalogClient
    {
        private readonly HttpClient _httpClient;

        public StoreCatalogHttpClient(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<BagCartSnapshot?> GetBagCartSnapshotAsync(
            Guid bagId,
            CancellationToken cancellationToken = default)
        {
            var response = await _httpClient.GetAsync(
                $"api/bags/{bagId}",
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();

            var bag = await response.Content.ReadFromJsonAsync<StoreBagResponse>(
                cancellationToken: cancellationToken);

            return bag is null ? null : ToSnapshot(bag);
        }

        private static BagCartSnapshot ToSnapshot(StoreBagResponse bag)
        {
            var pickupStartUtc = ToUtc(bag.PickupStartTime);
            var pickupEndUtc = ToUtc(bag.PickupEndTime);
            var expiryUtc = ToUtc(bag.ExpiryDate);

            return new BagCartSnapshot
            {
                BagId = bag.Id,
                StoreId = bag.StoreId,
                StoreNameSnapshot = bag.StoreName ?? string.Empty,
                BagNameSnapshot = bag.Name,
                UnitPriceSnapshot = bag.SalePrice,
                QuantityRemaining = bag.QuantityRemaining,
                ImageUrlSnapshot = bag.ImageUrl,
                PickupStartUtc = pickupStartUtc,
                PickupEndUtc = pickupEndUtc,
                ExpiryUtc = expiryUtc,
                Status = bag.Status,
                IsPurchasable = IsPurchasable(bag, pickupEndUtc, expiryUtc)
            };
        }

        private static bool IsPurchasable(
            StoreBagResponse bag,
            DateTime pickupEndUtc,
            DateTime expiryUtc)
        {
            if (bag.QuantityRemaining <= 0)
            {
                return false;
            }

            var now = DateTime.UtcNow;
            if (pickupEndUtc <= now || expiryUtc <= now)
            {
                return false;
            }

            return !IsKnownUnavailableStatus(bag.Status);
        }

        private static bool IsKnownUnavailableStatus(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return false;
            }

            return status.Trim().ToLowerInvariant() switch
            {
                "inactive" => true,
                "deleted" => true,
                "soldout" => true,
                "sold_out" => true,
                "sold out" => true,
                "expired" => true,
                "cancelled" => true,
                "canceled" => true,
                "draft" => true,
                _ => false
            };
        }

        private static DateTime ToUtc(DateTime value)
        {
            return value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
            };
        }

        private sealed class StoreBagResponse
        {
            public Guid Id { get; set; }
            public Guid StoreId { get; set; }
            public string? StoreName { get; set; }
            public string Name { get; set; } = null!;
            public string? ImageUrl { get; set; }
            public decimal SalePrice { get; set; }
            public int QuantityRemaining { get; set; }
            public DateTime PickupStartTime { get; set; }
            public DateTime PickupEndTime { get; set; }
            public DateTime ExpiryDate { get; set; }
            public string Status { get; set; } = string.Empty;
        }
    }
}
