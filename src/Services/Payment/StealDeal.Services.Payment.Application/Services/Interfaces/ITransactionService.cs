using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StealDeal.Services.Payment.Application.DTOs.Requests;
using StealDeal.Services.Payment.Application.DTOs.Response;

namespace StealDeal.Services.Payment.Application.Services.Interfaces
{
    public interface ITransactionService
    {
        Task<TransactionResponse> CreateTransactionAsync(Guid userId, CreateTransactionRequest request);
        Task<TransactionResponse> GetTransactionByIdAsync(Guid id, Guid userId, string role);
        Task<TransactionResponse> GetTransactionByOrderIdAsync(Guid orderId, Guid userId, string role);
        Task<IEnumerable<TransactionResponse>> GetMyTransactionsAsync(Guid userId);
        Task<TransactionResponse> UpdateTransactionStatusAsync(Guid id, UpdateTransactionStatusRequest request);
    }
}
