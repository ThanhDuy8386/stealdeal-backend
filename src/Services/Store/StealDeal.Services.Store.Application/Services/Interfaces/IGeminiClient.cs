using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StealDeal.Services.Store.Application.Services.Interfaces
{
    public interface IGeminiClient
    {
        Task<string> GenerateReplyAsync(
            IReadOnlyList<ChatTurn> history,
            string userMessage,
            string? systemInstruction = null,
            CancellationToken cancellationToken = default);
    }
}
