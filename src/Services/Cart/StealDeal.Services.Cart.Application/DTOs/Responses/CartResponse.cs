namespace StealDeal.Services.Cart.Application.DTOs.Responses
{
    public class CartResponse
    {
        public Guid UserId { get; set; }
        public Guid StoreId { get; set; }
        public string Currency { get; set; } = "VND";
        public List<CartItemResponse> Items { get; set; } = new();
        public int Version { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
        public int TotalQuantity => Items.Sum(item => item.Quantity);
        public decimal Subtotal => Items.Sum(item => item.LineTotal);
    }
}
