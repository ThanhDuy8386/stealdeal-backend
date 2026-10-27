using StealDeal.Services.Store.Application.DTOs.Requests;
using StealDeal.Services.Store.Application.DTOs.Responses;
using System.Threading;
using System.Threading.Tasks;

namespace StealDeal.Services.Store.Application.Services.Interfaces
{
    public interface IChatAssistantService
    {
        Task<ChatResponse> SendMessageAsync(ChatRequest request, CancellationToken cancellationToken = default);
        Task ClearSessionAsync(string sessionId, CancellationToken cancellationToken = default);
    }
}
