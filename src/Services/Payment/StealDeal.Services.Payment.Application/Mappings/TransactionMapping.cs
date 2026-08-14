using System;
using System.Linq;
using StealDeal.Services.Payment.Application.DTOs.Requests;
using StealDeal.Services.Payment.Application.DTOs.Response;
using StealDeal.Services.Payment.Domain.Constants;
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
                Status = TransactionStatuses.Pending,
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
                CheckoutUrl = transaction.CheckoutUrl,
                GatewayTransactionNo = transaction.GatewayTransactionNo,
                GatewayResponseCode = transaction.GatewayResponseCode,
                GatewayTransactionStatus = transaction.GatewayTransactionStatus,
                Status = transaction.Status,
                FailureReason = transaction.FailureReason,
                PaidAt = transaction.PaidAt,
                ExpiresAt = transaction.ExpiresAt,
                CreatedAt = transaction.CreatedAt,
                UpdatedAt = transaction.UpdatedAt,
                Refunds = transaction.Refunds?
                    .Select(r => r.ToResponse())
                    .ToList() ?? new()
            };
        }
    }
}
