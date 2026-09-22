namespace StealDeal.Services.Cart.Domain.Models
{
    public class CartItem
    {
        public Guid BagId { get; set; }
        public Guid StoreId { get; set; }
        public string BagNameSnapshot { get; set; } = null!;
        public decimal UnitPriceSnapshot { get; set; }
        public int Quantity { get; set; }
        public string? ImageUrlSnapshot { get; set; }
        public DateTime PickupStartUtc { get; set; }
        public DateTime PickupEndUtc { get; set; }
        public DateTime AddedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
