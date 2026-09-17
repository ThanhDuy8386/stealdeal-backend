namespace StealDeal.Services.Cart.Application.DTOs.Responses
{
    public class CartItemResponse
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
        public decimal LineTotal => UnitPriceSnapshot * Quantity;
    }
}
