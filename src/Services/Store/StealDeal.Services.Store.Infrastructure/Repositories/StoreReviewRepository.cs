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

        public async Task<(List<StoreReview> Items, int TotalCount)> GetByStoreIdAsync(Guid storeId, int page, int pageSize,
            int? ratingScore = null, bool? hasReply = null, string? search = null, bool? isReported = null)
        {
            var query = _context.StoreReviews
                .Include(x => x.Bag)
                .Where(x => x.StoreId == storeId);

            query = ApplyFilters(query, ratingScore, hasReply, search, isReported);

            var totalCount = await query.CountAsync();
            var items = await query
                .OrderByDescending(x => x.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (items, totalCount);
        }

        public async Task<(List<StoreReview> Items, int TotalCount)> GetByBagIdAsync(Guid bagId, int page, int pageSize,
            int? ratingScore = null, bool? hasReply = null, string? search = null)
        {
            var query = _context.StoreReviews
                .Include(x => x.Bag)
                .Where(x => x.BagId == bagId);

            query = ApplyFilters(query, ratingScore, hasReply, search, isReported: null);

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

        public void Delete(StoreReview entity)
        {
            _context.StoreReviews.Remove(entity);
        }

        public async Task<(List<StoreReview> Items, int TotalCount)> GetReportedAsync(int page, int pageSize)
        {
            var query = _context.StoreReviews
                .Include(x => x.Bag)
                .Where(x => x.IsReported);

            var totalCount = await query.CountAsync();
            var items = await query
                .OrderByDescending(x => x.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (items, totalCount);
        }

        // return count and sum of all ratings for a store
        public async Task<(int Count, decimal Sum)> GetRatingStatsAsync(Guid storeId)
        {
            var stats = await _context.StoreReviews
                .Where(r => r.StoreId == storeId)
                .GroupBy(r => 1)
                .Select(g => new
                {
                    Count = g.Count(),
                    Sum = g.Sum(r => r.RatingScore)
                })
                .FirstOrDefaultAsync();

            return (stats?.Count ?? 0, stats?.Sum ?? 0m);
        }

        private static IQueryable<StoreReview> ApplyFilters(
            IQueryable<StoreReview> query,
            int? ratingScore = null,
            bool? hasReply = null,
            string? search = null,
            bool? isReported = null)
        {
            if (ratingScore.HasValue)
                query = query.Where(x => x.RatingScore == ratingScore.Value);

            if (hasReply.HasValue)
            {
                if (hasReply.Value)
                {
                    query = query.Where(x => x.StoreReply != null && x.StoreReply != string.Empty);
                }else
                {
                    query = query.Where(x => x.StoreReply == null || x.StoreReply == string.Empty);
                }
            }

            // search term look into comment, buyername, bag name, store reply
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                query = query.Where(x =>
                    x.Comment != null && x.Comment.ToLower().Contains(term) ||
                    x.BuyerName.ToLower().Contains(term) ||
                    x.Bag != null && x.Bag.Name.ToLower().Contains(term) ||
                    x.StoreReply != null && x.StoreReply.ToLower().Contains(term));
            }

            if (isReported.HasValue)
            {
                query = query.Where(x => x.IsReported == isReported.Value);
            }

            return query;
        }
    }
}
