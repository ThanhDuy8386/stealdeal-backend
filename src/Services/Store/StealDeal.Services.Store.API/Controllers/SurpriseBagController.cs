using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using StealDeal.Services.Store.Application.DTOs.Requests;
using StealDeal.Services.Store.Application.Services.Interfaces;

namespace StealDeal.Services.Store.API.Controllers
{
    [ApiController]
    [Route("api/store/bags")]
    public class SurpriseBagController : ControllerBase
    {
        private readonly ISurpriseBagService _bagService;

        public SurpriseBagController(ISurpriseBagService bagService)
        {
            _bagService = bagService;
        }

        // GET api/store/bags
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var result = await _bagService.GetAllAsync();
            return Ok(result);
        }

        // GET api/store/bags/nearby?latitude=10.76&longitude=106.66&radiusInKm=10&limit=20
        [HttpGet("nearby")]
        public async Task<IActionResult> GetNearby([FromQuery] NearbyQueryRequest request)
        {
            var result = await _bagService.GetNearbyAsync(request);
            return Ok(result);
        }

        // GET api/store/bags/{id}
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var result = await _bagService.GetByIdAsync(id);
            return Ok(result);
        }

        // GET api/store/bags/store/{storeId}
        [HttpGet("store/{storeId:guid}")]
        public async Task<IActionResult> GetByStore(Guid storeId)
        {
            var result = await _bagService.GetByStoreIdAsync(storeId);
            return Ok(result);
        }

        // POST api/store/bags  [Seller only]
        [HttpPost]
        [Authorize(Roles = "Seller")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> Create([FromForm] CreateBagRequest request,
            IFormFile? image,
            CancellationToken cancellationToken)
        {
            var ownerId = GetCurrentUserId();
            FileUploadRequest? fileDto = null;
            if (image != null && image.Length > 0)
            {
                fileDto = new FileUploadRequest(
                    image.OpenReadStream(),
                    image.FileName,
                    image.ContentType,
                    image.Length);
            }
            var result = await _bagService.CreateAsync(ownerId, request, fileDto, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }

        // PUT api/store/bags/{id}  [Seller only]
        [HttpPut("{id:guid}")]
        [Authorize(Roles = "Seller")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> Update(Guid id, [FromForm] UpdateBagRequest request,
            IFormFile? image,
            CancellationToken cancellationToken)
        {
            var ownerId = GetCurrentUserId();
            FileUploadRequest? fileDto = null;
            if (image != null && image.Length > 0)
            {
                fileDto = new FileUploadRequest(
                    image.OpenReadStream(),
                    image.FileName,
                    image.ContentType,
                    image.Length);
            }
            var result = await _bagService.UpdateAsync(id, ownerId, request, fileDto, cancellationToken);
            return Ok(result);
        }

        // DELETE api/store/bags/{id}  [Seller only]
        [HttpDelete("{id:guid}")]
        [Authorize(Roles = "Seller")]
        public async Task<IActionResult> Delete(Guid id)
        {
            await _bagService.DeleteAsync(id);
            return NoContent();
        }

        // PATCH api/store/bags/{id}/status  [Seller only]
        [HttpPatch("{id:guid}/status")]
        [Authorize(Roles = "Seller")]
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
