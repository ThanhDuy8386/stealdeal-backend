using StealDeal.Services.Store.Application.DTOs.Requests;
using StealDeal.Services.Store.Application.DTOs.Responses;
using StealDeal.Services.Store.Domain.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Store.Application.Mappings
{
    public static class CategoryMapping
    {
        // 1. Mapping từ Request DTO -> Entity (Dùng cho Create/Add)
        public static Category ToEntity(this CreateCategoryRequest request)
        {
            return new Category
            {
                Id = Guid.NewGuid(), // Khởi tạo ID mới
                Name = request.Name.Trim(),
                Slug = request.Slug.Trim().ToLowerInvariant(),
                IconUrl = request.IconUrl,
                IsActive = true // Trạng thái mặc định khi tạo mới
            };
        }
        // 2. Mapping từ Request DTO -> Entity có sẵn (Dùng cho Update)
        public static void UpdateEntity(this UpdateCategoryRequest request, Category category)
        {
            category.Name = request.Name.Trim();
            category.Slug = request.Slug.Trim().ToLowerInvariant();
            category.IconUrl = request.IconUrl;
            category.IsActive = request.IsActive;
        }
        // 3. Mapping từ Entity -> Response DTO (Trả về cho client)
        public static CategoryResponse ToResponse(this Category category)
        {
            if (category == null) return null!;
            return new CategoryResponse
            {
                Id = category.Id,
                Name = category.Name,
                Slug = category.Slug,
                IconUrl = category.IconUrl,
                IsActive = category.IsActive
            };
        }
    }
}
