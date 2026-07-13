using StealDeal.Services.Store.Application.DTOs.Requests;
using StealDeal.Services.Store.Application.DTOs.Responses;
using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Store.Application.Services.Interfaces
{
    public interface ICategoryService
    {
        Task<CategoryResponse> CreateAsync(CreateCategoryRequest request);
        Task<CategoryResponse> UpdateAsync(Guid id, UpdateCategoryRequest request);
        Task DeleteAsync(Guid id);
        Task<CategoryResponse> GetBySlugAsync(string slug);
        Task<List<CategoryResponse>> GetAllAsync();
    }
}
