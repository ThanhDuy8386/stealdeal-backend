namespace StealDeal.Services.Payment.Application.DTOs.Events
{
    public static class PaymentEventTypes
    {
        public const string InventoryReserved = "inventory.reserved";
        public const string PaymentCompleted = "payment.completed";
        public const string PaymentFailed = "payment.failed";
        public const string InventoryReleaseRequested = "inventory.release_requested";
    }
}
