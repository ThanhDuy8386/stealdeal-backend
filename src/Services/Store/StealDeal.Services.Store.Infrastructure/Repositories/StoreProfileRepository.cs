using Microsoft.EntityFrameworkCore;
using StealDeal.Services.Store.Domain.Interfaces;
using StealDeal.Services.Store.Domain.Models;
using StealDeal.Services.Store.Infrastructure.Persistence;
using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Store.Infrastructure.Repositories
{
    public class StoreProfileRepository : IStoreProfileRepository
    {
        private ApplicationDbContext _context;

        public StoreProfileRepository(ApplicationDbContext context)
        {
            _context = context;
        }
        public async Task AddAsync(StoreProfile entity)
        {
            await _context.StoreProfiles.AddAsync(entity);
        }

        public void ToggleActive(StoreProfile entity)
        {
            entity.IsActive = !entity.IsActive;
            _context.StoreProfiles.Update(entity);
        }

        public async Task<IEnumerable<StoreProfile>> GetAllAsync()
        {
            return await _context.StoreProfiles.ToListAsync();
        }

        public async Task<StoreProfile?> GetByIdAsync(Guid id)
        {
            return await _context.StoreProfiles.FirstOrDefaultAsync(x => x.Id == id);
        }

        public void Update(StoreProfile entity)
        {
            _context.StoreProfiles.Update(entity);
        }
    }
}
