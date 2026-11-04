using System;
using System.Collections.Generic;
using System.Text.Json;
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
            IEnumerable<object>? tools = null,
            Func<string, JsonElement, Task<object>>? toolHandler = null,
            CancellationToken cancellationToken = default);
    }
}
