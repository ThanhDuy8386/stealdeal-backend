using System;
using System.Collections.Generic;

namespace StealDeal.Services.Payment.Application.DTOs.Response
{
    public class RefundResponse
    {
        public Guid Id { get; set; }
        public Guid TransactionId { get; set; }
        public Guid OrderId { get; set; }
        public decimal Amount { get; set; }
        public string Reason { get; set; } = null!;
        public string Status { get; set; } = null!;
        public string? GatewayRefundRef { get; set; }
        public string? GatewayResponseCode { get; set; }
        public string? FailureReason { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? ProcessedAt { get; set; }
    }

    public class TransactionResponse
    {
        public Guid Id { get; set; }
        public Guid OrderId { get; set; }
        public Guid UserId { get; set; }
        public decimal Amount { get; set; }
        public string PaymentMethod { get; set; } = null!;
        public string? GatewayRef { get; set; }
        public string? CheckoutUrl { get; set; }
        public string? GatewayTransactionNo { get; set; }
        public string? GatewayResponseCode { get; set; }
        public string? GatewayTransactionStatus { get; set; }
        public string Status { get; set; } = null!;
        public string? FailureReason { get; set; }
        public DateTime? PaidAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public List<RefundResponse> Refunds { get; set; } = new();
    }
}
