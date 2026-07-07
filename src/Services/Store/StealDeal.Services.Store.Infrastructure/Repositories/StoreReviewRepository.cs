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

        public async Task<IEnumerable<StoreReview>> GetByBagId(Guid bagId)
        {
            return await _context.StoreReviews
                .Where(x => x.BagId == bagId)
                .ToListAsync();
        }

        public async Task<StoreReview?> GetByIdAsync(Guid id)
        {
            return await _context.StoreReviews.FirstOrDefaultAsync(x => x.Id == id);
        }

        public async Task<StoreReview?> GetByOrderIdAsync(Guid orderId)
        {
            return await _context.StoreReviews.FirstOrDefaultAsync(x => x.OrderId == orderId);
        }

        public async Task<IEnumerable<StoreReview>> GetByStoreId(Guid storeId)
        {
            return await _context.StoreReviews
                .Where(x => x.StoreId == storeId)
                .ToListAsync();
        }

        public void Update(StoreReview entity)
        {
            _context.StoreReviews.Update(entity);
        }
    }
}
