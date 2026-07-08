using StealDeal.Services.Store.Application.DTOs.Requests;
using StealDeal.Services.Store.Application.DTOs.Responses;
using StealDeal.Services.Store.Application.Exceptions;
using StealDeal.Services.Store.Application.Mappings;
using StealDeal.Services.Store.Application.Services.Interfaces;
using StealDeal.Services.Store.Domain.Interfaces;
using StealDeal.Services.Store.Domain.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Store.Application.Services
{
    public class CategoryService : ICategoryService
    {
        private readonly ICategoryRepository _categoryRepository;
        private readonly IUnitOfWork _unitOfWork;

        public CategoryService(ICategoryRepository categoryRepository, IUnitOfWork unitOfWork)
        {
            _categoryRepository = categoryRepository;
            _unitOfWork = unitOfWork;
        }
        public async Task<CategoryResponse> CreateAsync(CreateCategoryRequest request)
        {
            // Kiểm tra trùng lặp trước khi tạo
            var isUnique = await _categoryRepository.IsSlugUnique(request.Slug);
            if (!isUnique)
            {
                throw new ConflictException("Category slug already exists.");
            }
            // DTO -> Entity bằng Extension Method
            var category = request.ToEntity();
            await _categoryRepository.AddAsync(category);
            await _unitOfWork.SaveChangesAsync();
            // Entity -> Response DTO bằng Extension Method
            return category.ToResponse();
        }

        public async Task DeleteAsync(Guid id)
        {
            var category = await _categoryRepository.GetByIdAsync(id);
            if (category == null)
            {
                throw new NotFoundException("Category not found.");
            }
            _categoryRepository.Delete(category);
        }

        public async Task<List<CategoryResponse>> GetAllActiveAsync()
        {
            var categories = await _categoryRepository.GetAllAsync();
            if (categories == null)
            {
                throw new NotFoundException("There are not active category yet");
            }
            return categories.Select(x => x.ToResponse()).ToList();
        }

        public async Task<CategoryResponse> GetBySlugAsync(string slug)
        {
            var category = await _categoryRepository.GetBySlugAsync(slug);
            if (category == null)
            {
                throw new NotFoundException("Category not found.");
            }
            return category.ToResponse();
        }

        public async Task<CategoryResponse> UpdateAsync(Guid id, UpdateCategoryRequest request)
        {
            var category = await _categoryRepository.GetByIdAsync(id);
            if (category == null)
            {
                throw new NotFoundException("Category not found.");
            }
            // Cập nhật giá trị DTO -> Entity hiện có
            request.UpdateEntity(category);
            _categoryRepository.Update(category);
            await _unitOfWork.SaveChangesAsync();
            return category.ToResponse();
        }
    }
}
