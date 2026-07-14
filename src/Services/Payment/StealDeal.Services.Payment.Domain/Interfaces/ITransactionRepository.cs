using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StealDeal.Services.Payment.Domain.Models;

namespace StealDeal.Services.Payment.Domain.Interfaces
{
    public interface ITransactionRepository
    {
        Task AddAsync(Transaction transaction);
        Task<Transaction?> GetByIdAsync(Guid id);
        Task<Transaction?> GetByOrderIdAsync(Guid orderId);
        Task<IEnumerable<Transaction>> GetByUserIdAsync(Guid userId);
        void Update(Transaction transaction);
    }
}
