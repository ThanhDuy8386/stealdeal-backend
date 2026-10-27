using StealDeal.Services.Store.Application.DTOs.Requests;
using StealDeal.Services.Store.Application.DTOs.Responses;
using StealDeal.Services.Store.Application.Exceptions;
using StealDeal.Services.Store.Application.Services.Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace StealDeal.Services.Store.Application.Services
{
    public class ChatAssistantService : IChatAssistantService
    {
        private readonly IChatSessionStore _sessionStore;
        private readonly IGeminiClient _geminiClient;

        public ChatAssistantService(
            IChatSessionStore sessionStore,
            IGeminiClient geminiClient)
        {
            _sessionStore = sessionStore;
            _geminiClient = geminiClient;
        }

        public async Task<ChatResponse> SendMessageAsync(ChatRequest request, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(request.Message))
            {
                throw new BadRequestException("Message cannot be empty.");
            }

            var trimmedMessage = request.Message.Trim();
            if (trimmedMessage.Length > 500)
            {
                throw new BadRequestException("Message must not exceed 500 characters.");
            }

            var sessionId = string.IsNullOrWhiteSpace(request.SessionId)
                ? Guid.NewGuid().ToString()
                : request.SessionId.Trim();

            var history = await _sessionStore.GetRecentTurnsAsync(sessionId, cancellationToken);

            var reply = await _geminiClient.GenerateReplyAsync(
                history,
                trimmedMessage,
                cancellationToken: cancellationToken);

            await _sessionStore.AppendTurnsAsync(
                sessionId,
                new[]
                {
                    new ChatTurn("user", trimmedMessage),
                    new ChatTurn("model", reply)
                },
                cancellationToken);

            return new ChatResponse
            {
                SessionId = sessionId,
                Reply = reply,
                SuggestedBags = new()
            };
        }

        public async Task ClearSessionAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return;
            }

            await _sessionStore.ClearSessionAsync(sessionId.Trim(), cancellationToken);
        }
    }
}
