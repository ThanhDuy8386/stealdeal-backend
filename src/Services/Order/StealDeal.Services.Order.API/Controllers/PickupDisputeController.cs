using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StealDeal.Services.Order.Application.DTOs.Requests;
using StealDeal.Services.Order.Application.Services.Interfaces;

namespace StealDeal.Services.Order.API.Controllers
{
    [ApiController]
    [Route("api/pickup-disputes")]
    public class PickupDisputeController : ControllerBase
    {
        private readonly IPickupDisputeService _disputeService;

        public PickupDisputeController(IPickupDisputeService disputeService)
        {
            _disputeService = disputeService;
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> Create([FromBody] CreateDisputeRequest request)
        {
            var userId = GetCurrentUserId();
            var result = await _disputeService.CreateDisputeAsync(userId, request);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }

        [HttpGet("{id:guid}")]
        [Authorize]
        public async Task<IActionResult> GetById(Guid id)
        {
            var userId = GetCurrentUserId();
            var role = GetCurrentUserRole();
            var result = await _disputeService.GetDisputeByIdAsync(id, userId, role);
            return Ok(result);
        }

        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetAll()
        {
            var result = await _disputeService.GetAllDisputesAsync();
            return Ok(result);
        }

        [HttpPatch("{id:guid}/status")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateDisputeStatusRequest request)
        {
            var result = await _disputeService.UpdateDisputeStatusAsync(id, request);
            return Ok(result);
        }

        private Guid GetCurrentUserId()
        {
            var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)

                      ?? User.FindFirstValue("sub");


            if (string.IsNullOrEmpty(sub))
                throw new Application.Exceptions.UnauthorizedException("User is not authenticated.");


            return Guid.Parse(sub);
        }

        private string GetCurrentUserRole()
        {
            var role = User.FindFirstValue(ClaimTypes.Role)
                       ?? User.FindFirstValue("role");


            if (string.IsNullOrEmpty(role))
                throw new Application.Exceptions.UnauthorizedException("User role is missing.");


            return role;
        }
    }
}
