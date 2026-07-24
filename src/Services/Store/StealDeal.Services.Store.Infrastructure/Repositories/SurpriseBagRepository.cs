using Microsoft.EntityFrameworkCore;
using StealDeal.Services.Store.Domain.Interfaces;
using StealDeal.Services.Store.Domain.Models;
using StealDeal.Services.Store.Infrastructure.Persistence;
using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Store.Infrastructure.Repositories
{
    public class SurpriseBagRepository : ISurpriseBagRepository
    {
        private ApplicationDbContext _context;

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
    }
}
