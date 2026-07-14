using System;
using StealDeal.Services.Payment.Application.DTOs.Requests;
using StealDeal.Services.Payment.Application.DTOs.Response;
using StealDeal.Services.Payment.Domain.Models;

namespace StealDeal.Services.Payment.Application.Mappings
{
    public static class RefundMapping
    {
        public static Refund ToEntity(this CreateRefundRequest request, Guid orderId)
        {
            return new Refund
            {
                Id = Guid.NewGuid(),
                TransactionId = request.TransactionId,
                OrderId = orderId,
                Amount = request.Amount,
                Reason = request.Reason.Trim(),
                Status = "Pending", // Default initial status
                CreatedAt = DateTime.UtcNow
            };
        }

        public static RefundResponse ToResponse(this Refund refund)
        {
            if (refund == null) return null!;

            return new RefundResponse
            {
                Id = refund.Id,
                TransactionId = refund.TransactionId,
                OrderId = refund.OrderId,
                Amount = refund.Amount,
                Reason = refund.Reason,
                Status = refund.Status,
                CreatedAt = refund.CreatedAt,
                ProcessedAt = refund.ProcessedAt
            };
        }
    }
}
