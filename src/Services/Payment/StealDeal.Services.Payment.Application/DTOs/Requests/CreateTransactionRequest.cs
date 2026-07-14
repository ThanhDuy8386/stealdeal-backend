using System;

namespace StealDeal.Services.Payment.Application.DTOs.Requests
{
    public class CreateTransactionRequest
    {
        public Guid OrderId { get; set; }
        public decimal Amount { get; set; }
        public string PaymentMethod { get; set; } = null!;
    }

    public class UpdateTransactionStatusRequest
    {
        public string Status { get; set; } = null!;
        public string? FailureReason { get; set; }
        public string? GatewayRef { get; set; }
    }
}
