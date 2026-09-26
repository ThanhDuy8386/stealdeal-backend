using StealDeal.Services.Store.Application.DTOs.Requests;
using StealDeal.Services.Store.Application.DTOs.Responses;
using StealDeal.Services.Store.Domain.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Store.Application.Mappings
{
    public static class CategorySuggestionMapping
    {
        public static CategorySuggestionResponse ToResponse(this CategorySuggestion entity)
        {
            return new CategorySuggestionResponse
            {
                Id = entity.Id,
                StoreId = entity.StoreId,
                StoreName = entity.Store?.Name ?? string.Empty,
                SuggestedName = entity.SuggestedName,
                Status = entity.Status,
                AdminComment = entity.AdminComment,
                CreatedAt = entity.CreatedAt
            };
        }

        public static CategorySuggestion ToEntity(this CreateCategorySuggestionRequest request, Guid storeId)
        {
            return new CategorySuggestion
            {
                StoreId = storeId,
                SuggestedName = request.SuggestedName,
            };
        }
    }
}
