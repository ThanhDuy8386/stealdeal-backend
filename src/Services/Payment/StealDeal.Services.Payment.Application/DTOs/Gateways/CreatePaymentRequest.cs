namespace StealDeal.Services.Payment.Application.DTOs.Gateways
{
    public class CreatePaymentRequest
    {
        public Guid TransactionId { get; set; }
        public Guid OrderId { get; set; }
        public Guid UserId { get; set; }
        public decimal Amount { get; set; }
        public string? OrderInfo { get; set; }
        public string OrderType { get; set; } = "other";
        public string? ClientIpAddress { get; set; }
        public string? BankCode { get; set; }
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
