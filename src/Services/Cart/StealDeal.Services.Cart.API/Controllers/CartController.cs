using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StealDeal.Services.Cart.Application.DTOs.Requests;
using StealDeal.Services.Cart.Application.DTOs.Responses;
using StealDeal.Services.Cart.Application.Exceptions;
using StealDeal.Services.Cart.Application.Services.Interfaces;

namespace StealDeal.Services.Cart.API.Controllers
{
    [ApiController]
    [Route("api/cart")]
    [Authorize]
    public class CartController : ControllerBase
    {
        private readonly ICartService _cartService;

        public CartController(ICartService cartService)
        {
            _cartService = cartService;
        }

        [HttpGet]
        public async Task<IActionResult> GetCarts(CancellationToken cancellationToken)
        {
            var result = await _cartService.GetCartsAsync(
                GetCurrentUserId(),
                cancellationToken);

            return Ok(result);
        }

        [HttpGet("stores/{storeId:guid}")]
        public async Task<IActionResult> GetCart(
            Guid storeId,
            CancellationToken cancellationToken)
        {
            var result = await _cartService.GetCartAsync(
                GetCurrentUserId(),
                storeId,
                cancellationToken);

            return Ok(result);
        }

        [HttpPost("items")]
        public async Task<IActionResult> AddItem(
            [FromBody] AddCartItemRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _cartService.AddItemAsync(
                GetCurrentUserId(),
                request,
                cancellationToken);

            return Ok(result);
        }

        [HttpPatch("items/{bagId:guid}")]
        public Task<IActionResult> UpdateQuantity(
            Guid bagId,
            [FromQuery] Guid storeId,
            [FromBody] UpdateCartItemRequest request,
            CancellationToken cancellationToken)
        {
            return UpdateQuantityForStore(storeId, bagId, request, cancellationToken);
        }

        [HttpPatch("stores/{storeId:guid}/items/{bagId:guid}")]
        public async Task<IActionResult> UpdateQuantityForStore(
            Guid storeId,
            Guid bagId,
            [FromBody] UpdateCartItemRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _cartService.UpdateQuantityAsync(
                GetCurrentUserId(),
                storeId,
                bagId,
                request,
                cancellationToken);

            return Ok(result);
        }

        [HttpDelete("items/{bagId:guid}")]
        public Task<IActionResult> RemoveItem(
            Guid bagId,
            [FromQuery] Guid storeId,
            CancellationToken cancellationToken)
        {
            return RemoveItemFromStore(storeId, bagId, cancellationToken);
        }

        [HttpDelete("stores/{storeId:guid}/items/{bagId:guid}")]
        public async Task<IActionResult> RemoveItemFromStore(
            Guid storeId,
            Guid bagId,
            CancellationToken cancellationToken)
        {
            var result = await _cartService.RemoveItemAsync(
                GetCurrentUserId(),
                storeId,
                bagId,
                cancellationToken);

            return Ok(result);
        }

        [HttpDelete("stores/{storeId:guid}")]
        public async Task<IActionResult> ClearStoreCart(
            Guid storeId,
            CancellationToken cancellationToken)
        {
            await _cartService.ClearCartAsync(
                GetCurrentUserId(),
                storeId,
                cancellationToken);

            return NoContent();
        }

        [HttpDelete]
        public async Task<IActionResult> ClearAllCarts(CancellationToken cancellationToken)
        {
            await _cartService.ClearCartAsync(
                GetCurrentUserId(),
                storeId: null,
                cancellationToken);

            return NoContent();
        }

        [HttpPost("stores/{storeId:guid}/checkout-lock")]
        public async Task<IActionResult> AcquireCheckoutLock(
            Guid storeId,
            CancellationToken cancellationToken)
        {
            var lockToken = await _cartService.AcquireCheckoutLockAsync(
                GetCurrentUserId(),
                storeId,
                cancellationToken);

            return Ok(new CheckoutLockResponse
            {
                LockToken = lockToken
            });
        }

        [HttpDelete("stores/{storeId:guid}/checkout-lock")]
        public async Task<IActionResult> ReleaseCheckoutLock(
            Guid storeId,
            [FromBody] ReleaseCheckoutLockRequest request,
            CancellationToken cancellationToken)
        {
            await _cartService.ReleaseCheckoutLockAsync(
                GetCurrentUserId(),
                storeId,
                request.LockToken,
                cancellationToken);

            return NoContent();
        }

        private Guid GetCurrentUserId()
        {
            var subject = User.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? User.FindFirstValue("sub");

            if (!Guid.TryParse(subject, out var userId))
            {
                throw new UnauthorizedException("User is not authenticated.");
            }

            return userId;
        }
    }
}
