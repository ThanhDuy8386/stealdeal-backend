using StealDeal.Services.Store.Domain.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Store.Domain.Interfaces
{
    public interface ICategoryRepository
    {
        Task AddAsync(Category entity);
        Task<Category?> GetByIdAsync(Guid id);
        Task<Category?> GetBySlugAsync(string slug);
        Task<bool> IsSlugUnique(string slug);
        Task<IEnumerable<Category>> GetAllAsync();
        void Update(Category entity);
        void Delete(Category entity);
    }
}
