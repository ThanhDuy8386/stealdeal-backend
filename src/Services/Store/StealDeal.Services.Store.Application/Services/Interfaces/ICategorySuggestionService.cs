using StealDeal.Services.Store.Application.DTOs.Requests;
using StealDeal.Services.Store.Application.DTOs.Responses;
using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Store.Application.Services.Interfaces
{
    public interface ICategorySuggestionService
    {
        Task<CategorySuggestionResponse> CreateCategorySuggestionAsync(Guid ownerId, CreateCategorySuggestionRequest request);
        Task<IEnumerable<CategorySuggestionResponse>> GetMyCategorySuggestionsAsync(Guid ownerId);
        Task<IEnumerable<CategorySuggestionResponse>> GetPendingSuggestionsAsync();
        Task<CategorySuggestionResponse> ReviewSuggestionAsync(Guid suggestionId, ReviewCategorySuggestionRequest request);
    }
}
