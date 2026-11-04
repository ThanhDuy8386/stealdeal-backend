using StealDeal.Services.Store.Application.DTOs.Requests;
using StealDeal.Services.Store.Application.DTOs.Responses;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StealDeal.Services.Store.Application.Services.Interfaces
{
    public interface IChatAssistantService
    {
        Task<ChatResponse> SendMessageAsync(Guid userId, ChatRequest request, CancellationToken cancellationToken = default);
        Task<List<ChatTurn>> GetSessionHistoryAsync(Guid userId, CancellationToken cancellationToken = default);
        Task ClearSessionAsync(Guid userId, CancellationToken cancellationToken = default);
    }
}
