using StealDeal.Services.Order.Domain.Constants;
using StealDeal.Services.Order.Domain.Models;

namespace StealDeal.Services.Order.Domain.Policies;

public static class OrderNoShowPolicy
{
    // Business eligibility only. The command must also authorize the seller/admin.
    public static string? GetIneligibleReason(OrderProfile order, DateTime nowUtc)
    {
        if (!string.Equals(order.DeliveryType, "Pickup", StringComparison.OrdinalIgnoreCase))
            return "Only pickup orders can be marked as NoShow.";
        if (!string.Equals(order.Status, OrderStatuses.Confirmed, StringComparison.OrdinalIgnoreCase))
            return "Only confirmed orders can be marked as NoShow.";
        if (!order.PickupDeadlineVerified || !order.PickupDeadline.HasValue)
            return "The pickup deadline has not been verified for this order.";
        if (nowUtc <= order.PickupDeadline.Value)
            return "The pickup deadline has not passed yet.";
        return null;
    }
}
