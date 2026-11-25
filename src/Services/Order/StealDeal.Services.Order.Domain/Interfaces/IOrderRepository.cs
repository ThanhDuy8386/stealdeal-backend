using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StealDeal.Services.Order.Domain.Models;

namespace StealDeal.Services.Order.Domain.Interfaces
{
    public interface IOrderRepository
    {
        Task AddAsync(OrderProfile order);
        Task<int> CountPendingForUpdateAsync(Guid userId, CancellationToken cancellationToken = default);
        Task<OrderProfile?> GetByIdAsync(Guid id);
        Task<IEnumerable<OrderProfile>> GetByUserIdAsync(Guid userId);
        Task<IEnumerable<OrderProfile>> GetByStoreIdAsync(Guid storeId);
        void Update(OrderProfile order);
        Task<bool> TryUpdatePendingStatusAsync(Guid orderId, string status, DateTime updatedAtUtc, CancellationToken cancellationToken = default);
        Task<bool> TryMarkNoShowAsync(Guid orderId, DateTime pickupDeadlineUtc, DateTime occurredAtUtc, CancellationToken cancellationToken = default);
    }
}
