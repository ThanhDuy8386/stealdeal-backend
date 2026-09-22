namespace StealDeal.Services.Cart.Domain.Models
{
    public class UserCart
    {
        public Guid UserId { get; set; }
        public Guid StoreId { get; set; }
        public string Currency { get; set; } = "VND";
        public List<CartItem> Items { get; set; } = new();
        public int Version { get; set; }
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime ExpiresAtUtc { get; set; }
    }
}
