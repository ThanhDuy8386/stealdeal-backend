namespace StealDeal.Services.Order.Domain.Constants;

public static class OrderStatuses
{
    public const string Pending = "Pending";
    public const string Confirmed = "Confirmed";
    public const string NoShow = "NoShow";
    public const string Cancelled = "Cancelled";
    public const string InventoryReservationFailed = "InventoryReservationFailed";
    public const string PaymentFailed = "PaymentFailed";
}
