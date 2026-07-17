using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StealDeal.Services.Notification.Application.DTOs.Requests;
using StealDeal.Services.Notification.Application.Services.Interfaces;

namespace StealDeal.Services.Notification.API.Controllers
{
    [ApiController]
    [Route("api/notifications")]
    public class NotificationController : ControllerBase
    {
        private readonly INotificationService _notificationService;

        public NotificationController(INotificationService notificationService)
        {
            _notificationService = notificationService;
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetMyNotifications()
        {
            var userId = GetCurrentUserId();
            var result = await _notificationService.GetMyNotificationsAsync(userId);
            return Ok(result);
        }

        [HttpGet("unread-count")]
        [Authorize]
        public async Task<IActionResult> GetUnreadCount()
        {
            var userId = GetCurrentUserId();
            var result = await _notificationService.GetUnreadCountAsync(userId);
            return Ok(result);
        }

        [HttpPatch("{id:guid}/read")]
        [Authorize]
        public async Task<IActionResult> MarkAsRead(Guid id)
        {
            var userId = GetCurrentUserId();
            var result = await _notificationService.MarkAsReadAsync(id, userId);
            return Ok(result);
        }

        [HttpPatch("read-all")]
        [Authorize]
        public async Task<IActionResult> MarkAllAsRead()
        {
            var userId = GetCurrentUserId();
            await _notificationService.MarkAllAsReadAsync(userId);
            return NoContent();
        }

        [HttpPost]
        // [Authorize(Roles = "Admin")] // External creation (like microservice events) would usually be done by rabbitmq consumer, admin endpoint is just for testing/system use.
        public async Task<IActionResult> Create([FromBody] CreateNotificationRequest request)
        {
            var result = await _notificationService.CreateNotificationAsync(request);
            return StatusCode(201, result);
        }

        [HttpDelete("{id:guid}")]
        [Authorize]
        public async Task<IActionResult> Delete(Guid id)
        {
            var userId = GetCurrentUserId();
            await _notificationService.DeleteNotificationAsync(id, userId);
            return NoContent();
        }

        private Guid GetCurrentUserId()
        {
            var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? User.FindFirstValue("sub");

            if (string.IsNullOrEmpty(sub))
                throw new Application.Exceptions.UnauthorizedException("User is not authenticated.");

            return Guid.Parse(sub);
        }
    }
}
