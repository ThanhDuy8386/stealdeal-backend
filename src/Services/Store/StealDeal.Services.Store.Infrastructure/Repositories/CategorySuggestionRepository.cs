using Microsoft.EntityFrameworkCore;
using StealDeal.Services.Store.Domain.Interfaces;
using StealDeal.Services.Store.Domain.Models;
using StealDeal.Services.Store.Infrastructure.Persistence;
using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Store.Infrastructure.Repositories
{
    public class CategorySuggestionRepository : ICategorySuggestionRepository
    {
        private ApplicationDbContext _context;
        public CategorySuggestionRepository(ApplicationDbContext context) 
        {
            _context = context;
        }
        public async Task AddAsync(CategorySuggestion entity)
        {
            await _context.CategorySuggestions.AddAsync(entity);
        }
        public void Update(CategorySuggestion categorySuggestion)
        {
            _context.CategorySuggestions.Update(categorySuggestion);
        }

        public void Delete(CategorySuggestion categorySuggestion)
        {
            _context.CategorySuggestions.Remove(categorySuggestion);
        }

        public async Task<CategorySuggestion?> GetByIdAsync(Guid id)
        {
            return await _context.CategorySuggestions
                .Include(cs => cs.Store)
                .FirstOrDefaultAsync(cs => cs.Id == id);
        }

        public async Task<IEnumerable<CategorySuggestion>> GetByStatusAsync(string status)
        {
            return await _context.CategorySuggestions
                .Include(cs => cs.Store)
                .Where(cs => cs.Status == status)
                .OrderBy(cs => cs.CreatedAt)
                .ToListAsync();
        }

        public async Task<IEnumerable<CategorySuggestion>> GetByStoreIdAsync(Guid storeId)
        {
            return await _context.CategorySuggestions
                .Include(cs => cs.Store)
                .Where(cs => cs.StoreId == storeId)
                .OrderByDescending(cs => cs.CreatedAt)
                .ToListAsync();
        }
    }
}
