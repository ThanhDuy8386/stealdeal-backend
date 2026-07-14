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
    [Route("api/transactions")]
    public class TransactionController : ControllerBase
    {
        private readonly ITransactionService _transactionService;

        public TransactionController(ITransactionService transactionService)
        {
            _transactionService = transactionService;
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> Create([FromBody] CreateTransactionRequest request)
        {
            var userId = GetCurrentUserId();
            var result = await _transactionService.CreateTransactionAsync(userId, request);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }

        [HttpGet("{id:guid}")]
        [Authorize]
        public async Task<IActionResult> GetById(Guid id)
        {
            var userId = GetCurrentUserId();
            var role = GetCurrentUserRole();
            var result = await _transactionService.GetTransactionByIdAsync(id, userId, role);
            return Ok(result);
        }

        [HttpGet("order/{orderId:guid}")]
        [Authorize]
        public async Task<IActionResult> GetByOrderId(Guid orderId)
        {
            var userId = GetCurrentUserId();
            var role = GetCurrentUserRole();
            var result = await _transactionService.GetTransactionByOrderIdAsync(orderId, userId, role);
            return Ok(result);
        }

        [HttpGet("my-transactions")]
        [Authorize]
        public async Task<IActionResult> GetMyTransactions()
        {
            var userId = GetCurrentUserId();
            var result = await _transactionService.GetMyTransactionsAsync(userId);
            return Ok(result);
        }

        [HttpPatch("{id:guid}/status")]
        [Authorize(Roles = "Admin")] // Gateway/IPN calls or internal system calls are usually Admin-authorized.
        public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateTransactionStatusRequest request)
        {
            var result = await _transactionService.UpdateTransactionStatusAsync(id, request);
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
