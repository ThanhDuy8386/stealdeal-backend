using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StealDeal.Services.Store.Application.DTOs.Requests;
using StealDeal.Services.Store.Application.Services.Interfaces;
using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace StealDeal.Services.Store.API.Controllers
{
    [ApiController]
    [Route("api/store/chat")]
    [Authorize]
    public class ChatController : ControllerBase
    {
        private readonly IChatAssistantService _chatService;

        public ChatController(IChatAssistantService chatService)
        {
            _chatService = chatService;
        }

        [HttpPost]
        public async Task<IActionResult> SendMessage([FromBody] ChatRequest request, CancellationToken cancellationToken)
        {
            var userId = GetCurrentUserId();
            var response = await _chatService.SendMessageAsync(userId, request, cancellationToken);
            return Ok(response);
        }

        [HttpGet]
        public async Task<IActionResult> GetHistory(CancellationToken cancellationToken)
        {
            var userId = GetCurrentUserId();
            var history = await _chatService.GetSessionHistoryAsync(userId, cancellationToken);
            return Ok(history);
        }

        [HttpDelete("{sessionId?}")]
        public async Task<IActionResult> ClearSession(string? sessionId, CancellationToken cancellationToken)
        {
            var userId = GetCurrentUserId();
            await _chatService.ClearSessionAsync(userId, cancellationToken);
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
