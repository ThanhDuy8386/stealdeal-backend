using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StealDeal.Services.Order.Domain.Models;

namespace StealDeal.Services.Order.Domain.Interfaces
{
    public interface IPickupDisputeRepository
    {
        Task AddAsync(PickupDispute dispute);
        Task<PickupDispute?> GetByIdAsync(Guid id);
        Task<PickupDispute?> GetByOrderIdAsync(Guid orderId);
        Task<IEnumerable<PickupDispute>> GetAllAsync();
        void Update(PickupDispute dispute);
    }
}
