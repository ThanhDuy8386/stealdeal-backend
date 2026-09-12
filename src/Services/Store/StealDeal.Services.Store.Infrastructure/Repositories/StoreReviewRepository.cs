using Microsoft.EntityFrameworkCore;
using StealDeal.Services.Store.Domain.Interfaces;
using StealDeal.Services.Store.Domain.Models;
using StealDeal.Services.Store.Infrastructure.Persistence;
using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Store.Infrastructure.Repositories
{
    public class StoreReviewRepository : IStoreReviewRepository
    {
        private ApplicationDbContext _context;

        public StoreReviewRepository(ApplicationDbContext context)
        {
            _context = context;
        }
        public async Task AddAsync(StoreReview entity)
        {
            await _context.StoreReviews.AddAsync(entity);
        }

        public async Task<StoreReview?> GetByIdAsync(Guid id)
        {
            return await _context.StoreReviews
                .Include(x => x.Bag)
                .FirstOrDefaultAsync(x => x.Id == id);
        }

        public async Task<StoreReview?> GetByOrderAndBagAsync(Guid orderId, Guid bagId)
        {
            return await _context.StoreReviews
                .FirstOrDefaultAsync(x => x.OrderId == orderId && x.BagId == bagId);
        }

        public async Task<(List<StoreReview> Items, int TotalCount)> GetByStoreIdAsync(Guid storeId, int page, int pageSize)
        {
            var query = _context.StoreReviews
                .Include(x => x.Bag)
                .Where(x => x.StoreId == storeId);

            var totalCount = await query.CountAsync();
            var items = await query
                .OrderByDescending(x => x.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (items, totalCount);
        }

        public async Task<(List<StoreReview> Items, int TotalCount)> GetByBagIdAsync(Guid bagId, int page, int pageSize)
        {
            var query = _context.StoreReviews
                .Include(x => x.Bag)
                .Where(x => x.BagId == bagId);

            var totalCount = await query.CountAsync();
            var items = await query
                .OrderByDescending(x => x.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (items, totalCount);
        }

        public void Update(StoreReview entity)
        {
            _context.StoreReviews.Update(entity);
        }
    }
}
