using Microsoft.AspNetCore.Mvc;
using StealDeal.Services.Store.Application.DTOs.Requests;
using StealDeal.Services.Store.Application.Services.Interfaces;
using System.Threading;
using System.Threading.Tasks;

namespace StealDeal.Services.Store.API.Controllers
{
    [ApiController]
    [Route("api/store/chat")]
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
            var response = await _chatService.SendMessageAsync(request, cancellationToken);
            return Ok(response);
        }

        [HttpDelete("{sessionId}")]
        public async Task<IActionResult> ClearSession(string sessionId, CancellationToken cancellationToken)
        {
            await _chatService.ClearSessionAsync(sessionId, cancellationToken);
            return NoContent();
        }
    }
}
