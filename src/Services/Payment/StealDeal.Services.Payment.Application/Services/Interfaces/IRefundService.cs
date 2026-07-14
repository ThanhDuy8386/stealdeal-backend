using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StealDeal.Services.Payment.Application.DTOs.Requests;
using StealDeal.Services.Payment.Application.DTOs.Response;

namespace StealDeal.Services.Payment.Application.Services.Interfaces
{
    public interface IRefundService
    {
        Task<RefundResponse> CreateRefundAsync(CreateRefundRequest request);
        Task<RefundResponse> GetRefundByIdAsync(Guid id, Guid userId, string role);
        Task<IEnumerable<RefundResponse>> GetRefundsByTransactionIdAsync(Guid transactionId, Guid userId, string role);
        Task<IEnumerable<RefundResponse>> GetAllRefundsAsync();
        Task<RefundResponse> UpdateRefundStatusAsync(Guid id, UpdateRefundStatusRequest request);
    }
}
