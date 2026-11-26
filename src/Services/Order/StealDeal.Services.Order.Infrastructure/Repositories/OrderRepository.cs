using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using StealDeal.Services.Order.Domain.Interfaces;
using StealDeal.Services.Order.Domain.Models;
using StealDeal.Services.Order.Domain.Constants;
using StealDeal.Services.Order.Infrastructure.Persistency;

namespace StealDeal.Services.Order.Infrastructure.Repositories
{
    public class OrderRepository : IOrderRepository
    {
        private readonly ApplicationDbContext _context;

        public OrderRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task AddAsync(OrderProfile order)
        {
            await _context.OrderProfiles.AddAsync(order);
        }

        public Task<int> CountPendingForUpdateAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            if (_context.Database.CurrentTransaction == null)
                throw new InvalidOperationException("Pending quota must be checked inside the order creation transaction.");

            // Hold update/range locks until the order + Outbox commit, including when no Pending row exists.
            // All creators use this query, so concurrent direct/cart requests cannot both take the last slot.
            return _context.Database.SqlQuery<int>($"""
                SELECT COUNT(*) AS [Value]
                FROM [OrderProfiles] WITH (UPDLOCK, HOLDLOCK)
                WHERE [UserId] = {userId} AND [Status] = {OrderStatuses.Pending}
                """).SingleAsync(cancellationToken);
        }

        public async Task<OrderProfile?> GetByIdAsync(Guid id)
        {
            return await _context.OrderProfiles
                .AsNoTracking()
                .Include(o => o.Items)
                .Include(o => o.Disputes)
                .FirstOrDefaultAsync(o => o.Id == id);
        }

        public async Task<IEnumerable<OrderProfile>> GetByUserIdAsync(Guid userId)
        {
            return await _context.OrderProfiles
                .Include(o => o.Items)
                .Where(o => o.UserId == userId)
                .ToListAsync();
        }

        public async Task<IEnumerable<OrderProfile>> GetByStoreIdAsync(Guid storeId)
        {
            return await _context.OrderProfiles
                .Include(o => o.Items)
                .Where(o => o.StoreId == storeId)
                .ToListAsync();
        }

        public void Update(OrderProfile order)
        {
            _context.OrderProfiles.Update(order);
        }

        public async Task<bool> TryUpdatePendingStatusAsync(Guid orderId, string status, DateTime updatedAtUtc,
            CancellationToken cancellationToken = default)
        {
            var affected = await _context.OrderProfiles
                .Where(order => order.Id == orderId && order.Status == OrderStatuses.Pending)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(order => order.Status, status)
                    .SetProperty(order => order.UpdatedAt, updatedAtUtc), cancellationToken);
            return affected == 1;
        }

        public async Task<bool> TryMarkNoShowAsync(Guid orderId, DateTime pickupDeadlineUtc, DateTime occurredAtUtc,
            CancellationToken cancellationToken = default)
        {
            var affected = await _context.OrderProfiles
                .Where(order => order.Id == orderId && order.Status == OrderStatuses.Confirmed &&
                    order.DeliveryType == "Pickup" && order.PickupDeadlineVerified &&
                    order.PickupDeadline == pickupDeadlineUtc && order.PickupDeadline < occurredAtUtc)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(order => order.Status, OrderStatuses.NoShow)
                    .SetProperty(order => order.UpdatedAt, occurredAtUtc), cancellationToken);
            return affected == 1;
        }
    }
}
