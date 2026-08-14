namespace StealDeal.Services.Payment.Domain.Constants
{
    public static class TransactionStatuses
    {
        public const string Pending = "Pending";
        public const string Success = "Success";
        public const string Failed = "Failed";
        public const string Expired = "Expired";
        public const string RefundPending = "RefundPending";
        public const string Refunded = "Refunded";
        public const string RefundFailed = "RefundFailed";
    }
}
