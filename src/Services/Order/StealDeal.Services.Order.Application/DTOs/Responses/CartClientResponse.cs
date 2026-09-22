namespace StealDeal.Services.Order.Application.DTOs.Response
{
    public class CartClientResponse
    {
        public Guid UserId { get; set; }
        public Guid StoreId { get; set; }
        public string Currency { get; set; } = "VND";
        public List<CartItemClientResponse> Items { get; set; } = new();
        public int Version { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
    }

    public class CartItemClientResponse
    {
        public Guid BagId { get; set; }
        public Guid StoreId { get; set; }
        public string BagNameSnapshot { get; set; } = null!;
        public decimal UnitPriceSnapshot { get; set; }
        public int Quantity { get; set; }
        public string? ImageUrlSnapshot { get; set; }
        public DateTime PickupStartUtc { get; set; }
        public DateTime PickupEndUtc { get; set; }
        public DateTime AddedAtUtc { get; set; }
    }
}
