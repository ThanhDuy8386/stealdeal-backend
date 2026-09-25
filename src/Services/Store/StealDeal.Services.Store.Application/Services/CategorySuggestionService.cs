using StealDeal.Services.Store.Application.DTOs.Requests;
using StealDeal.Services.Store.Application.DTOs.Responses;
using StealDeal.Services.Store.Application.Exceptions;
using StealDeal.Services.Store.Application.Mappings;
using StealDeal.Services.Store.Application.Services.Interfaces;
using StealDeal.Services.Store.Domain.Interfaces;
using StealDeal.Services.Store.Domain.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace StealDeal.Services.Store.Application.Services
{
    public class CategorySuggestionService : ICategorySuggestionService
    {
        private readonly ICategorySuggestionRepository _categorySuggestionRepository;
        private readonly IStoreProfileRepository _storeRepository;
        private readonly ICategoryRepository _categoryRepository;
        private readonly IUnitOfWork _unitOfWork;

        public CategorySuggestionService(ICategorySuggestionRepository categorySuggestionRepository, IStoreProfileRepository storeRepository, ICategoryRepository categoryRepository, IUnitOfWork unitOfWork)
        {
            _categorySuggestionRepository = categorySuggestionRepository;
            _storeRepository = storeRepository;
            _categoryRepository = categoryRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<CategorySuggestionResponse> CreateCategorySuggestionAsync(Guid ownerId, CreateCategorySuggestionRequest request)
        {
            var store = await _storeRepository.GetByOwnerIdAsync(ownerId);
            if (store is null)
                throw new NotFoundException("You do not have a store. Create a store first.");

            if (!store.IsActive || !store.IsVerify)
                throw new ForbiddenException("Your store must be active and verified to perform this action");


            if (string.IsNullOrWhiteSpace(request.SuggestedName))
                throw new BadRequestException("Suggested name cannot be empty.");

            var trimmedName = request.SuggestedName.Trim();
            if (trimmedName.Length > 100)
                throw new BadRequestException("Suggested name cannot exceed 100 characters.");

            // Check if seller already has a pending suggestion for the same name
            var mySuggestions = await _categorySuggestionRepository.GetByStoreIdAsync(store.Id);
            var duplicateSuggestion = mySuggestions.Any(s =>
                    s.SuggestedName.Equals(trimmedName, StringComparison.OrdinalIgnoreCase) &&
                    s.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase));

            // Check if an official category with this name already exists
            var slug = GenerateSlug(trimmedName);
            var isSlugUnique = await _categoryRepository.IsSlugUnique(slug);
            if (!isSlugUnique)
                throw new ConflictException("An official category with this name already exists.");

            if (duplicateSuggestion)
                throw new ConflictException("You already have a pending suggestion for this category name.");

            var suggestion = request.ToEntity(store.Id);
            suggestion.SuggestedName = trimmedName;

            await _categorySuggestionRepository.AddAsync(suggestion);
            await _unitOfWork.SaveChangesAsync();
            suggestion.Store = store; // Assign the store to the suggestion for the response mapping
            return suggestion.ToResponse();
        }

        public async Task<IEnumerable<CategorySuggestionResponse>> GetMyCategorySuggestionsAsync(Guid ownerId)
        {
            var store = await _storeRepository.GetByOwnerIdAsync(ownerId);
            if (store is null)
                throw new NotFoundException("You do not have a store.");
            var suggestions = await _categorySuggestionRepository.GetByStoreIdAsync(store.Id);
            return suggestions.Select(s => s.ToResponse());
        }

        public async Task<IEnumerable<CategorySuggestionResponse>> GetPendingSuggestionsAsync()
        {
            var pendingSuggestions = await _categorySuggestionRepository.GetByStatusAsync("Pending");
            return pendingSuggestions.Select(s => s.ToResponse());
        }

        public async Task<CategorySuggestionResponse> ReviewSuggestionAsync(Guid suggestionId, ReviewCategorySuggestionRequest request)
        {
            var suggestion = await _categorySuggestionRepository.GetByIdAsync(suggestionId);
            if (suggestion is null)
                throw new NotFoundException("Category suggestion not found.");

            if (!suggestion.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase))
                throw new BadRequestException($"Suggestion has already been reviewed ({suggestion.Status}).");

            var status = request.Status?.Trim();
            if (string.IsNullOrWhiteSpace(status) || 
                (!status.Equals("Approved", StringComparison.OrdinalIgnoreCase) && 
                 !status.Equals("Rejected", StringComparison.OrdinalIgnoreCase)))
            {
                throw new BadRequestException("Status must be either 'Approved' or 'Rejected'.");
            }

            if (status.Equals("Approved", StringComparison.OrdinalIgnoreCase))
            {
                
                var officialName = !string.IsNullOrWhiteSpace(request.OfficialCategoryName)
                    ? request.OfficialCategoryName.Trim()
                    : suggestion.SuggestedName.Trim();

                var slug = GenerateSlug(officialName);

                var isSlugUnique = await _categoryRepository.IsSlugUnique(slug);
                if (!isSlugUnique)
                    throw new ConflictException($"An official category with slug '{slug}' already exists.");

                var newCategory = new Category
                {
                    Id = Guid.NewGuid(),
                    Name = officialName,
                    Slug = slug,
                    IconUrl = request.IconUrl,
                    IsActive = true
                };
                await _categoryRepository.AddAsync(newCategory);
                suggestion.Status = "Approved";
            } else
            {
                suggestion.Status = "Rejected";
            }

            suggestion.AdminComment = request.AdminComment?.Trim();

            _categorySuggestionRepository.Update(suggestion);
            await _unitOfWork.SaveChangesAsync();

            return suggestion.ToResponse();
        }

        // Helper: Convert "Bánh Mì & Cà Phê" -> "banh-mi-ca-phe"
        private static string GenerateSlug(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            // Convert "Đ/đ"
            var cleanText = text.Replace("đ", "d").Replace("Đ", "d");
            // Remove diacritics / accents
            var normalizedString = cleanText.Normalize(NormalizationForm.FormD);
            var stringBuilder = new StringBuilder();
            foreach (var c in normalizedString)
            {
                var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
                if (unicodeCategory != UnicodeCategory.NonSpacingMark)
                {
                    stringBuilder.Append(c);
                }
            }
            cleanText = stringBuilder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
            // Remove invalid characters and replace spaces with hyphens
            cleanText = Regex.Replace(cleanText, @"[^a-z0-9\s-]", "");
            cleanText = Regex.Replace(cleanText, @"\s+", "-").Trim('-');
            return cleanText;
        }
    }
}
