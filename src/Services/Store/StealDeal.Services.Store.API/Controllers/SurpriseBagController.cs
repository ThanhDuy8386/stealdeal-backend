using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StealDeal.Services.Store.Application.DTOs.Requests;
using StealDeal.Services.Store.Application.Services.Interfaces;
using System.Security.Claims;

namespace StealDeal.Services.Store.API.Controllers
{
    [ApiController]
    [Route("api/bags")]
    public class SurpriseBagController : ControllerBase
    {
        private readonly ISurpriseBagService _bagService;

        public SurpriseBagController(ISurpriseBagService bagService)
        {
            _bagService = bagService;
        }

        // GET api/bags
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var result = await _bagService.GetAllAsync();
            return Ok(result);
        }

        // GET api/bags/{id}
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var result = await _bagService.GetByIdAsync(id);
            return Ok(result);
        }

        // GET api/bags/store/{storeId}
        [HttpGet("store/{storeId:guid}")]
        public async Task<IActionResult> GetByStore(Guid storeId)
        {
            var result = await _bagService.GetByStoreIdAsync(storeId);
            return Ok(result);
        }

        // POST api/bags  [Seller only]
        [HttpPost]
        //[Authorize(Roles = "Seller")]
        public async Task<IActionResult> Create([FromBody] CreateBagRequest request)
        {
            var ownerId = GetCurrentUserId();
            var result = await _bagService.CreateAsync(ownerId, request);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }

        // PUT api/bags/{id}  [Seller only]
        [HttpPut("{id:guid}")]
        //[Authorize(Roles = "Seller")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateBagRequest request)
        {
            var ownerId = GetCurrentUserId();
            var result = await _bagService.UpdateAsync(id, ownerId, request);
            return Ok(result);
        }

        // DELETE api/bags/{id}  [Seller only]
        [HttpDelete("{id:guid}")]
        //[Authorize(Roles = "Seller")]
        public async Task<IActionResult> Delete(Guid id)
        {
            await _bagService.DeleteAsync(id);
            return NoContent();
        }

        // PATCH api/bags/{id}/status  [Seller only]
        [HttpPatch("{id:guid}/status")]
        //[Authorize(Roles = "Seller")]
        public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateBagStatusRequest request)
        {
            var ownerId = GetCurrentUserId();
            await _bagService.UpdateStatusAsync(id, ownerId, request.Status);
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
