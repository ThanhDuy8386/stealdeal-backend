using System;
using System.Linq;
using StealDeal.Services.Payment.Application.DTOs.Requests;
using StealDeal.Services.Payment.Application.DTOs.Response;
using StealDeal.Services.Payment.Domain.Models;

namespace StealDeal.Services.Payment.Application.Mappings
{
    public static class TransactionMapping
    {
        public static Transaction ToEntity(this CreateTransactionRequest request, Guid userId)
        {
            return new Transaction
            {
                Id = Guid.NewGuid(),
                OrderId = request.OrderId,
                UserId = userId,
                Amount = request.Amount,
                PaymentMethod = request.PaymentMethod.Trim(),
                Status = "Pending", // Default initial status
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
        }

        public static TransactionResponse ToResponse(this Transaction transaction)
        {
            if (transaction == null) return null!;

            return new TransactionResponse
            {
                Id = transaction.Id,
                OrderId = transaction.OrderId,
                UserId = transaction.UserId,
                Amount = transaction.Amount,
                PaymentMethod = transaction.PaymentMethod,
                GatewayRef = transaction.GatewayRef,
                Status = transaction.Status,
                FailureReason = transaction.FailureReason,
                PaidAt = transaction.PaidAt,
                CreatedAt = transaction.CreatedAt,
                UpdatedAt = transaction.UpdatedAt,
                Refunds = transaction.Refunds?
                    .Select(r => r.ToResponse())
                    .ToList() ?? new()
            };
        }
    }
}
