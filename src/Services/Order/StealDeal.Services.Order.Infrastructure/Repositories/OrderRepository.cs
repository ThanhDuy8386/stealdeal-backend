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

        public async Task<OrderProfile?> GetByIdAsync(Guid id)
        {
            return await _context.OrderProfiles
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

        public async Task<bool> TryCancelPendingAsync(Guid orderId, DateTime updatedAtUtc,
            CancellationToken cancellationToken = default)
        {
            var affected = await _context.OrderProfiles
                .Where(order => order.Id == orderId && order.Status == OrderStatuses.Pending)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(order => order.Status, OrderStatuses.Cancelled)
                    .SetProperty(order => order.UpdatedAt, updatedAtUtc), cancellationToken);
            return affected == 1;
        }
    }
}
