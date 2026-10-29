using Microsoft.EntityFrameworkCore;
using StealDeal.Services.Store.Domain.Interfaces;
using StealDeal.Services.Store.Domain.Models;
using StealDeal.Services.Store.Infrastructure.Persistence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace StealDeal.Services.Store.Infrastructure.Repositories
{
    public class StoreProfileRepository : IStoreProfileRepository
    {
        private readonly ApplicationDbContext _context;

        public StoreProfileRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task AddAsync(StoreProfile entity)
        {
            await _context.StoreProfiles.AddAsync(entity);
        }

        public async Task<StoreProfile?> GetByOwnerIdAsync(Guid ownerId)
        {
            return await _context.StoreProfiles
                .FirstOrDefaultAsync(x => x.OwnerId == ownerId);
        }

        public async Task<bool> ExistsByOwnerIdAsync(Guid ownerId)
        {
            return await _context.StoreProfiles
                .AnyAsync(x => x.OwnerId == ownerId);
        }

        public async Task<IEnumerable<StoreProfile>> GetAllAsync()
        {
            return await _context.StoreProfiles.ToListAsync();
        }

        public async Task<StoreProfile?> GetByIdAsync(Guid id)
        {
            return await _context.StoreProfiles.FirstOrDefaultAsync(x => x.Id == id);
        }

        public async Task<List<StoreProfile>> GetNearbyCandidatesAsync(
            decimal minLat,
            decimal maxLat,
            decimal minLng,
            decimal maxLng,
            string? query = null,
            string? category = null)
        {
            var dbQuery = _context.StoreProfiles
                .AsNoTracking()
                .Where(s => s.IsActive && s.IsVerify
                    && s.Latitude.HasValue && s.Longitude.HasValue
                    && s.Latitude.Value >= minLat && s.Latitude.Value <= maxLat
                    && s.Longitude.Value >= minLng && s.Longitude.Value <= maxLng);

            if (!string.IsNullOrWhiteSpace(query))
            {
                var trimmedQuery = query.Trim().ToLower();
                dbQuery = dbQuery.Where(s => s.Name.ToLower().Contains(trimmedQuery)
                    || (s.Description != null && s.Description.ToLower().Contains(trimmedQuery)));
            }

            if (!string.IsNullOrWhiteSpace(category))
            {
                var trimmedCategory = category.Trim().ToLower();
                dbQuery = dbQuery.Where(s => s.SurpriseBags.Any(b => b.Categories.Any(c =>
                    c.Name.ToLower() == trimmedCategory || c.Slug.ToLower() == trimmedCategory)));
            }

            return await dbQuery.ToListAsync();
        }

        public void Update(StoreProfile entity)
        {
            _context.StoreProfiles.Update(entity);
        }

        public void Delete(StoreProfile entity)
        {
            _context.StoreProfiles.Remove(entity);
        }

        public async Task<IEnumerable<StoreProfile>> GetPendingVerificationAsync()
        {
            return await _context.StoreProfiles
                .Where(s => s.IsVerify == false)
                .OrderBy(s => s.CreatedAt)
                .ToListAsync();
        }
    }
}
