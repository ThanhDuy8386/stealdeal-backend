using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StealDeal.Services.Store.Application.DTOs.Requests;
using StealDeal.Services.Store.Application.Services.Interfaces;
using System.Security.Claims;

namespace StealDeal.Services.Store.API.Controllers
{
    [ApiController]
    [Route("api/category-suggestions")]
    public class CategorySuggestionController : ControllerBase
    {
        private readonly ICategorySuggestionService _categorySuggestionService;
        public CategorySuggestionController(ICategorySuggestionService categorySuggestionService)
        {
            _categorySuggestionService = categorySuggestionService;
        }

        // POST api/category-suggestions [seller only]
        [HttpPost]
        [Authorize(Roles = "Seller")]
        public async Task<IActionResult> CreateSuggestion([FromBody] CreateCategorySuggestionRequest request)
        {
            var ownerId = GetCurrentUserId();
            var result = await _categorySuggestionService.CreateCategorySuggestionAsync(ownerId, request);
            return Ok(result);
        }

        // GET api/category-suggestions/me  [Seller only]
        [HttpGet("me")]
        [Authorize(Roles = "Seller")]
        public async Task<IActionResult> GetMySuggestions()
        {
            var ownerId = GetCurrentUserId();
            var result = await _categorySuggestionService.GetMyCategorySuggestionsAsync(ownerId);
            return Ok(result);
        }

        // GET api/category-suggestions/pending  [Admin only]
        [HttpGet("pending")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetPendingSuggestions()
        {
            var result = await _categorySuggestionService.GetPendingSuggestionsAsync();
            return Ok(result);
        }

        // POST api/category-suggestions/{id}/review  [Admin only]
        [HttpPost("{id:guid}/review")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ReviewSuggestion(Guid id, [FromBody] ReviewCategorySuggestionRequest request)
        {
            var result = await _categorySuggestionService.ReviewSuggestionAsync(id, request);
            return Ok(result);
        }

        private Guid GetCurrentUserId()
        {
            var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? User.FindFirstValue("sub");
            return Guid.Parse(sub!);
        }
    }
}
