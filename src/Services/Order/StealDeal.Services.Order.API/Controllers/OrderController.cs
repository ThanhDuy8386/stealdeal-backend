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
    [Route("api/orders")]
    public class OrderController : ControllerBase
    {
        private readonly IOrderService _orderService;
        private readonly IOrderCheckoutService _orderCheckoutService;

        public OrderController(
            IOrderService orderService,
            IOrderCheckoutService orderCheckoutService)
        {
            _orderService = orderService;
            _orderCheckoutService = orderCheckoutService;
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> Create([FromBody] CreateOrderRequest request)
        {
            var userId = GetCurrentUserId();
            var result = await _orderService.CreateOrderAsync(userId, request);
            // var result = await _orderService.CreateOrderAsync(Guid.Parse("6BFE535E-E205-4031-88A8-36D8993863F7"), request);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }

        [HttpPost("checkout-from-cart")]
        [Authorize]
        public async Task<IActionResult> CheckoutFromCart(
            [FromBody] CheckoutFromCartRequest request,
            CancellationToken cancellationToken)
        {
            var userId = GetCurrentUserId();
            var accessToken = GetBearerToken();
            var result = await _orderCheckoutService.CheckoutFromCartAsync(
                userId,
                accessToken,
                request,
                cancellationToken);

            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }

        [HttpGet("{id:guid}")]
        [Authorize]
        public async Task<IActionResult> GetById(Guid id)
        {
            var userId = GetCurrentUserId();
            var roles = GetCurrentUserRoles();
            var result = await _orderService.GetOrderByIdAsync(id, userId, roles);
            return Ok(result);
        }

        [HttpGet("my-orders")]
        [Authorize]
        public async Task<IActionResult> GetMyOrders()
        {
            var userId = GetCurrentUserId();
            var result = await _orderService.GetMyOrdersAsync(userId);
            return Ok(result);
        }

        [HttpGet("store/{storeId:guid}")]
        [Authorize(Roles = "Seller,Admin")]
        public async Task<IActionResult> GetStoreOrders(Guid storeId)
        {
            var userId = GetCurrentUserId();
            var result = await _orderService.GetStoreOrdersAsync(storeId, userId);
            return Ok(result);
        }

        [HttpPatch("{id:guid}/status")]
        [Authorize]
        public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateOrderStatusRequest request)
        {
            var userId = GetCurrentUserId();
            var roles = GetCurrentUserRoles();
            var result = await _orderService.UpdateOrderStatusAsync(id, userId, roles, request);
            return Ok(result);
        }

        // might change to be internal endpoint for other services to call, but for now it's public
        [HttpGet("{id:guid}/review-eligibility")]
        public async Task<IActionResult> CheckReviewEligibility(
            Guid id,
            [FromQuery] Guid bagId,
            [FromQuery] Guid buyerId)
        {
            var result = await _orderService.CheckReviewEligibilityAsync(id, buyerId, bagId);
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

        private string[] GetCurrentUserRoles()
        {
            var roles = User.Claims
                .Where(claim => claim.Type == ClaimTypes.Role || claim.Type == "role")
                .Select(claim => claim.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (roles.Length == 0)
                throw new Application.Exceptions.UnauthorizedException("User role is missing.");

            return roles;
        }

        private string GetBearerToken()
        {
            var authorization = Request.Headers.Authorization.ToString();
            const string bearerPrefix = "Bearer ";

            if (!authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
                throw new Application.Exceptions.UnauthorizedException("Bearer token is missing.");

            return authorization[bearerPrefix.Length..].Trim();
        }
    }
}
