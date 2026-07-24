namespace StealDeal.Services.Store.Application.DTOs.Events
{
    public class InventoryReservedEvent
    {
        public Guid MessageId { get; set; }
        public DateTime OccurredAtUtc { get; set; }
        public Guid OrderId { get; set; }
        public Guid UserId { get; set; }
        public Guid StoreId { get; set; }
        public decimal TotalAmount { get; set; }
        public List<InventoryReservedItemDto> Items { get; set; } = new();
    }

    public class InventoryReservedItemDto
    {
        public Guid SurpriseBagId { get; set; }
        public int Quantity { get; set; }
    }
}
