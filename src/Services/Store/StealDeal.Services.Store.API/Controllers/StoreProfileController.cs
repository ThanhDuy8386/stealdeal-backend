using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StealDeal.Services.Store.Application.DTOs.Requests;
using StealDeal.Services.Store.Application.Services.Interfaces;

namespace StealDeal.Services.Store.API.Controllers
{
    [ApiController]
    [Route("api/stores")]
    public class StoreProfileController : ControllerBase
    {
        private readonly IStoreProfileService _storeService;

        public StoreProfileController(IStoreProfileService storeService)
        {
            _storeService = storeService;
        }

        // GET api/stores
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var result = await _storeService.GetAllAsync();
            return Ok(result);
        }

        // GET api/stores/{id}
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var result = await _storeService.GetByIdAsync(id);
            return Ok(result);
        }

        // GET api/stores/me  [Seller only]
        [HttpGet("me")]
        [Authorize(Roles = "Seller")]
        public async Task<IActionResult> GetMyStore()
        {
            var ownerId = GetCurrentUserId();
            var result = await _storeService.GetMyStoreAsync(ownerId);
            return Ok(result);
        }

        // POST api/stores  [Customer or Seller]
        [HttpPost]
        [Authorize(Roles = "Customer,Seller")]
        public async Task<IActionResult> Create([FromBody] CreateStoreRequest request)
        {
            var ownerId = GetCurrentUserId();
            var result = await _storeService.CreateAsync(ownerId, request);
            // var result = await _storeService.CreateAsync(Guid.Parse("6BFE535E-E205-4031-88A8-36D8993863F7"), request);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }

        // PUT api/stores/{id}  [Seller only]
        [HttpPut("{id:guid}")]
        [Authorize(Roles = "Seller")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateStoreRequest request)
        {
            var ownerId = GetCurrentUserId();
            var result = await _storeService.UpdateAsync(id, ownerId, request);
            return Ok(result);
        }

        // PATCH api/stores/{id}/verify  [Admin only]
        [HttpPatch("{id:guid}/verify")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> Verify(Guid id)
        {
            await _storeService.VerifyStoreAsync(id);
            return NoContent();
        }

        // PATCH api/stores/{id}/toggle-active  [Admin only]
        [HttpPatch("{id:guid}/toggle-active")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ToggleActive(Guid id)
        {
            await _storeService.ToggleActiveAsync(id);
            return NoContent();
        }

        [HttpGet("pending")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> GetPendingVerification()
        {
            var result = await _storeService.GetPendingVerificationAsync();
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
