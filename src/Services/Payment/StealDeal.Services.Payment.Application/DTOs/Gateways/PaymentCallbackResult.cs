namespace StealDeal.Services.Payment.Application.DTOs.Gateways
{
    public class PaymentCallbackResult
    {
        public bool IsValidSignature { get; set; }
        public bool IsSuccess { get; set; }
        public string PaymentMethod { get; set; } = null!;
        public string? GatewayRef { get; set; }
        public decimal? Amount { get; set; }
        public string? GatewayTransactionNo { get; set; }
        public string? GatewayResponseCode { get; set; }
        public string? GatewayTransactionStatus { get; set; }
        public DateTime? PaidAtUtc { get; set; }
        public string? ReasonCode { get; set; }
        public string? Reason { get; set; }
    }
}
