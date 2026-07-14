using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StealDeal.Services.Payment.Domain.Models;

namespace StealDeal.Services.Payment.Domain.Interfaces
{
    public interface IRefundRepository
    {
        Task AddAsync(Refund refund);
        Task<Refund?> GetByIdAsync(Guid id);
        Task<Refund?> GetByOrderIdAsync(Guid orderId);
        Task<IEnumerable<Refund>> GetByTransactionIdAsync(Guid transactionId);
        Task<IEnumerable<Refund>> GetAllAsync();
        void Update(Refund refund);
    }
}
