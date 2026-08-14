namespace StealDeal.Services.Payment.Application.DTOs.Gateways
{
    public class CreatePaymentResult
    {
        public string PaymentMethod { get; set; } = null!;
        public string GatewayRef { get; set; } = null!;
        public string CheckoutUrl { get; set; } = null!;
        public DateTime ExpiresAtUtc { get; set; }
    }
}
