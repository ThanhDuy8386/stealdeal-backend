using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StealDeal.Services.Store.Application.DTOs.Requests;
using StealDeal.Services.Store.Application.Services.Interfaces;
using System.Security.Claims;

namespace StealDeal.Services.Store.API.Controllers
{
    [ApiController]
    [Route("api/reviews")]
    public class StoreReviewController : ControllerBase
    {
        private readonly IStoreReviewService _reviewService;

        public StoreReviewController(IStoreReviewService reviewService)
        {
            _reviewService = reviewService;
        }

        // GET api/reviews/store/{storeId}
        [HttpGet("store/{storeId:guid}")]
        public async Task<IActionResult> GetByStore(Guid storeId)
        {
            var result = await _reviewService.GetByStoreIdAsync(storeId);
            return Ok(result);
        }

        // GET api/reviews/bag/{bagId}
        [HttpGet("bag/{bagId:guid}")]
        public async Task<IActionResult> GetByBag(Guid bagId)
        {
            var result = await _reviewService.GetByBagIdAsync(bagId);
            return Ok(result);
        }

        // POST api/reviews  [Buyer — any authenticated user]
        [HttpPost]
       // [Authorize]
        public async Task<IActionResult> Create([FromBody] CreateReviewRequest request)
        {
            var buyerId = GetCurrentUserId();
            var result = await _reviewService.CreateAsync(buyerId, request);
            return StatusCode(201, result);
        }

        // PATCH api/reviews/{id}/reply  [Seller only]
        [HttpPatch("{id:guid}/reply")]
        //[Authorize(Roles = "Seller")]
        public async Task<IActionResult> Reply(Guid id, [FromBody] ReplyReviewRequest request)
        {
            var ownerId = GetCurrentUserId();
            await _reviewService.ReplyAsync(id, ownerId, request);
            return NoContent();
        }

        // PATCH api/reviews/{id}/report  [Any authenticated user]
        [HttpPatch("{id:guid}/report")]
        //[Authorize]
        public async Task<IActionResult> Report(Guid id)
        {
            var userId = GetCurrentUserId();
            await _reviewService.ReportAsync(id, userId);
            return NoContent();
        }

        private Guid GetCurrentUserId()
        {
            var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? User.FindFirstValue("sub");

            return Guid.Parse(sub!);
        }
    }
}
