namespace StealDeal.Services.Payment.Application.DTOs.Events
{
    public class InventoryReleaseRequestedEvent
    {
        public Guid MessageId { get; set; }
        public DateTime OccurredAtUtc { get; set; }
        public Guid OrderId { get; set; }
        public Guid StoreId { get; set; }
        public string ReasonCode { get; set; } = null!;
        public string Reason { get; set; } = null!;
        public List<InventoryReleaseRequestedItemDto> Items { get; set; } = new();
    }

    public class InventoryReleaseRequestedItemDto
    {
        public Guid SurpriseBagId { get; set; }
        public int Quantity { get; set; }
    }
}
