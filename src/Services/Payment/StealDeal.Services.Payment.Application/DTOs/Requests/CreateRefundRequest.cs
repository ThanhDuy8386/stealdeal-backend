using System;

namespace StealDeal.Services.Payment.Application.DTOs.Requests
{
    public class CreateRefundRequest
    {
        public Guid TransactionId { get; set; }
        public decimal Amount { get; set; }
        public string Reason { get; set; } = null!;
    }

    public class UpdateRefundStatusRequest
    {
        public string Status { get; set; } = null!;
        public string? GatewayRefundRef { get; set; }
        public string? GatewayResponseCode { get; set; }
        public string? FailureReason { get; set; }
        public DateTime? ProcessedAt { get; set; }
    }
}
