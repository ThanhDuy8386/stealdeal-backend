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

        // GET api/reviews/store/{storeId}?page=1&pageSize=10&ratingScore=5&hasReply=false&search=...
        [HttpGet("store/{storeId:guid}")]
        public async Task<IActionResult> GetByStore(
            Guid storeId,
            [FromQuery] ReviewFilterRequest filter)
        {
            var result = await _reviewService.GetByStoreIdAsync(storeId, filter);
            return Ok(result);
        }

        // GET api/reviews/bag/{bagId}?page=1&pageSize=10&ratingScore=5&hasReply=false&search=...
        [HttpGet("bag/{bagId:guid}")]
        public async Task<IActionResult> GetByBag(
            Guid bagId,
            [FromQuery] ReviewFilterRequest filter)
        {
            var result = await _reviewService.GetByBagIdAsync(bagId, filter);
            return Ok(result);
        }

        // GET api/reviews/store/me?page=1&pageSize=10&isReported=true&hasReply=false&search=...  [Seller only]
        [HttpGet("store/me")]
        [Authorize(Roles = "Seller")]
        public async Task<IActionResult> GetMyStoreReviews([FromQuery] ReviewFilterRequest filter)
        {
            var ownerId = GetCurrentUserId();
            var result = await _reviewService.GetByStoreOwnerAsync(ownerId, filter);
            return Ok(result);
        }

        // GET api/reviews/reported?page=1&pageSize=20  [Admin, SuperAdmin]
        [HttpGet("reported")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> GetReportedReviews(
            [FromQuery] int page = 1, 
            [FromQuery] int pageSize = 20)
        {
            var result = await _reviewService.GetReportedAsync(page, pageSize);
            return Ok(result);
        }

        // POST api/reviews  [Buyer — any authenticated user]
        [HttpPost]
        [Authorize]
        public async Task<IActionResult> Create([FromBody] CreateReviewRequest request)
        {
            var buyerId = GetCurrentUserId();
            var buyerName = GetCurrentUserName();
            var result = await _reviewService.CreateAsync(buyerId, buyerName, request);
            return StatusCode(201, result);
        }

        // PATCH api/reviews/{id}/reply  [Seller only]
        [HttpPatch("{id:guid}/reply")]
        [Authorize(Roles = "Seller")]
        public async Task<IActionResult> Reply(Guid id, [FromBody] ReplyReviewRequest request)
        {
            var ownerId = GetCurrentUserId();
            await _reviewService.ReplyAsync(id, ownerId, request);
            return NoContent();
        }

        // DELETE api/reviews/{id}/reply  [Seller only]
        [HttpDelete("{id:guid}/reply")]
        [Authorize(Roles = "Seller")]
        public async Task<IActionResult> DeleteReply(Guid id)
        {
            var ownerId = GetCurrentUserId();
            await _reviewService.DeleteReplyAsync(id, ownerId);
            return NoContent();
        }

        // PATCH api/reviews/{id}/report  [Any authenticated user]
        [HttpPatch("{id:guid}/report")]
        [Authorize]
        public async Task<IActionResult> Report(Guid id)
        {
            var userId = GetCurrentUserId();
            await _reviewService.ReportAsync(id, userId);
            return NoContent();
        }

        // DELETE report api/reviews/{id}/report  [Seller, Admin, SuperAdmin]
        [HttpDelete("{id:guid}/report")]
        [Authorize(Roles = "Seller,Admin,SuperAdmin")]
        public async Task<IActionResult> Unreport(Guid id)
        {
            var userId = GetCurrentUserId();
            var isAdmin = User.IsInRole("Admin") || User.IsInRole("SuperAdmin");
            await _reviewService.UnreportAsync(id, userId, isAdmin);
            return NoContent();
        }

        // DELETE review api/reviews/{id}  [Admin, SuperAdmin]
        [HttpDelete("{id:guid}")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> Delete(Guid id)
        {
            await _reviewService.DeleteReviewAsync(id);
            return NoContent();
        }

        private Guid GetCurrentUserId()
        {
            var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? User.FindFirstValue("sub");

            return Guid.Parse(sub!);
        }

        private string GetCurrentUserName()
        {
            return User.FindFirstValue(ClaimTypes.Name)
                   ?? User.FindFirstValue("name")
                   ?? "Customer";
        }
    }
}
