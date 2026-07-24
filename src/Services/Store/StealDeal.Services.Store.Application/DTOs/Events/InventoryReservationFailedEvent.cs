namespace StealDeal.Services.Store.Application.DTOs.Events
{
    public class InventoryReservationFailedEvent
    {
        public Guid MessageId { get; set; }
        public DateTime OccurredAtUtc { get; set; }
        public Guid OrderId { get; set; }
        public Guid StoreId { get; set; }
        public string ReasonCode { get; set; } = null!;
        public string Reason { get; set; } = null!;
    }
}
