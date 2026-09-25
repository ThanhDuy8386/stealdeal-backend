using StealDeal.Services.Store.Domain.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Store.Domain.Interfaces
{
    public interface ICategorySuggestionRepository
    {
        Task AddAsync(CategorySuggestion categorySuggestion);
        Task<CategorySuggestion?> GetByIdAsync(Guid id);
        Task<IEnumerable<CategorySuggestion>> GetByStatusAsync(string status);
        Task<IEnumerable<CategorySuggestion>> GetByStoreIdAsync(Guid storeId);
        void Update(CategorySuggestion categorySuggestion);
        void Delete(CategorySuggestion categorySuggestion);
    }
}
