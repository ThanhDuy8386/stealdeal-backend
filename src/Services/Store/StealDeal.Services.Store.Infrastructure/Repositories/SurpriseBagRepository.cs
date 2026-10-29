using Microsoft.EntityFrameworkCore;
using StealDeal.Services.Store.Domain.Interfaces;
using StealDeal.Services.Store.Domain.Models;
using StealDeal.Services.Store.Infrastructure.Persistence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StealDeal.Services.Store.Infrastructure.Repositories
{
    public class SurpriseBagRepository : ISurpriseBagRepository
    {
        private readonly ApplicationDbContext _context;

        public SurpriseBagRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task AddAsync(SurpriseBag entity)
        {
            await _context.SurpriseBags.AddAsync(entity);
        }

        public async Task<IEnumerable<SurpriseBag>> GetAllAsync()
        {
            return await _context.SurpriseBags
                .Include(x => x.Store)
                .Include(x => x.Categories)
                .ToListAsync();
        }

        public async Task<SurpriseBag?> GetByIdAsync(Guid id)
        {
            return await _context.SurpriseBags
                .Include(x => x.Store)
                .Include(x => x.Categories)
                .FirstOrDefaultAsync(x => x.Id == id);
        }

        public async Task<IEnumerable<SurpriseBag>> GetByStoreIdAsync(Guid storeId)
        {
            return await _context.SurpriseBags
                .Where(x => x.StoreId == storeId)
                .ToListAsync();
        }

        public async Task<List<SurpriseBag>> GetNearbyCandidatesAsync(
            decimal minLat,
            decimal maxLat,
            decimal minLng,
            decimal maxLng,
            string? status = null,
            string? query = null,
            string? category = null)
        {
            var dbQuery = _context.SurpriseBags
                .AsNoTracking()
                .Include(b => b.Store)
                .Include(b => b.Categories)
                .Where(b => b.Store.IsActive && b.Store.IsVerify
                    && b.Store.Latitude.HasValue && b.Store.Longitude.HasValue
                    && b.Store.Latitude.Value >= minLat && b.Store.Latitude.Value <= maxLat
                    && b.Store.Longitude.Value >= minLng && b.Store.Longitude.Value <= maxLng
                    && b.QuantityRemaining > 0);

            if (!string.IsNullOrWhiteSpace(status))
            {
                dbQuery = dbQuery.Where(b => b.Status == status);
            }

            if (!string.IsNullOrWhiteSpace(query))
            {
                var trimmedQuery = query.Trim().ToLower();
                dbQuery = dbQuery.Where(b => b.Name.ToLower().Contains(trimmedQuery)
                    || b.Store.Name.ToLower().Contains(trimmedQuery)
                    || (b.Description != null && b.Description.ToLower().Contains(trimmedQuery)));
            }

            if (!string.IsNullOrWhiteSpace(category))
            {
                var trimmedCategory = category.Trim().ToLower();
                dbQuery = dbQuery.Where(b => b.Categories.Any(c =>
                    c.Name.ToLower() == trimmedCategory || c.Slug.ToLower() == trimmedCategory));
            }

            return await dbQuery.ToListAsync();
        }

        public void Update(SurpriseBag entity)
        {
            _context.SurpriseBags.Update(entity);
        }

        public void Delete(SurpriseBag entity)
        {
            _context.SurpriseBags.Remove(entity);
        }

        public async Task<bool> TryReserveQuantityAsync(
            Guid surpriseBagId,
            Guid storeId,
            int quantity,
            CancellationToken cancellationToken = default)
        {
            var affectedRows = await _context.SurpriseBags
                .Where(x =>
                    x.Id == surpriseBagId &&
                    x.StoreId == storeId &&
                    x.QuantityRemaining >= quantity)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(
                            bag => bag.QuantityRemaining,
                            bag => bag.QuantityRemaining - quantity)
                        .SetProperty(
                            bag => bag.UpdatedAt,
                            DateTime.UtcNow),
                    cancellationToken);

            return affectedRows == 1;
        }

        public async Task<bool> TryReleaseQuantityAsync(
            Guid surpriseBagId,
            Guid storeId,
            int quantity,
            CancellationToken cancellationToken = default)
        {
            var affectedRows = await _context.SurpriseBags
                .Where(x =>
                    x.Id == surpriseBagId &&
                    x.StoreId == storeId)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(
                            bag => bag.QuantityRemaining,
                            bag => bag.QuantityRemaining + quantity)
                        .SetProperty(
                            bag => bag.UpdatedAt,
                            DateTime.UtcNow),
                    cancellationToken);

            return affectedRows == 1;
        }
    }
}
