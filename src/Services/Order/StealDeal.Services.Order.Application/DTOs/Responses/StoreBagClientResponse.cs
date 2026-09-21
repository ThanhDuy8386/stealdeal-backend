namespace StealDeal.Services.Order.Application.DTOs.Response
{
    public class StoreBagClientResponse
    {
        public Guid Id { get; set; }
        public Guid StoreId { get; set; }
        public string StoreName { get; set; } = null!;
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
