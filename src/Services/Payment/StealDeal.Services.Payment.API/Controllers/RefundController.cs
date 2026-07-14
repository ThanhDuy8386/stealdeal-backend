using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StealDeal.Services.Payment.Application.DTOs.Requests;
using StealDeal.Services.Payment.Application.Services.Interfaces;

namespace StealDeal.Services.Payment.API.Controllers
{
    [ApiController]
    [Route("api/refunds")]
    public class RefundController : ControllerBase
    {
        private readonly IRefundService _refundService;

        public RefundController(IRefundService refundService)
        {
            _refundService = refundService;
        }

        [HttpPost]
        [Authorize(Roles = "Seller,Admin")] // Buyers can request, but usually refunds are initialized by Seller/Admin.
        public async Task<IActionResult> Create([FromBody] CreateRefundRequest request)
        {
            var result = await _refundService.CreateRefundAsync(request);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }

        [HttpGet("{id:guid}")]
        [Authorize]
        public async Task<IActionResult> GetById(Guid id)
        {
            var userId = GetCurrentUserId();
            var role = GetCurrentUserRole();
            var result = await _refundService.GetRefundByIdAsync(id, userId, role);
            return Ok(result);
        }

        [HttpGet("transaction/{transactionId:guid}")]
        [Authorize]
        public async Task<IActionResult> GetByTransactionId(Guid transactionId)
        {
            var userId = GetCurrentUserId();
            var role = GetCurrentUserRole();
            var result = await _refundService.GetRefundsByTransactionIdAsync(transactionId, userId, role);
            return Ok(result);
        }

        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetAll()
        {
            var result = await _refundService.GetAllRefundsAsync();
            return Ok(result);
        }

        [HttpPatch("{id:guid}/status")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateRefundStatusRequest request)
        {
            var result = await _refundService.UpdateRefundStatusAsync(id, request);
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
