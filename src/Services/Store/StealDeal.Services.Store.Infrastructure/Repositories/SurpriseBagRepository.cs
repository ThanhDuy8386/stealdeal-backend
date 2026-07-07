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
            return await _context.SurpriseBags.ToListAsync();
        }

        public async Task<SurpriseBag?> GetByIdAsync(Guid id)
        {
            return await _context.SurpriseBags.FirstOrDefaultAsync(x => x.Id == id);
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
    }
}
