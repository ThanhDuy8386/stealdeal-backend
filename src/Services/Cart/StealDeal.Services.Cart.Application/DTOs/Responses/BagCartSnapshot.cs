namespace StealDeal.Services.Cart.Application.DTOs.Responses
{
    public class BagCartSnapshot
    {
        public Guid BagId { get; set; }
        public Guid StoreId { get; set; }
        public string StoreNameSnapshot { get; set; } = string.Empty;
        public string BagNameSnapshot { get; set; } = null!;
        public decimal UnitPriceSnapshot { get; set; }
        public int QuantityRemaining { get; set; }
        public string? ImageUrlSnapshot { get; set; }
        public DateTime PickupStartUtc { get; set; }
        public DateTime PickupEndUtc { get; set; }
        public DateTime ExpiryUtc { get; set; }
        public string Status { get; set; } = string.Empty;
        public bool IsPurchasable { get; set; }
    }
}
