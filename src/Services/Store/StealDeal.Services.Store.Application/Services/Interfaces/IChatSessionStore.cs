using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StealDeal.Services.Store.Application.Services.Interfaces
{
    public record ChatTurn(string Role, string Content);

    public interface IChatSessionStore
    {
        Task<List<ChatTurn>> GetRecentTurnsAsync(string sessionId, CancellationToken cancellationToken = default);
        Task AppendTurnsAsync(string sessionId, IEnumerable<ChatTurn> turns, CancellationToken cancellationToken = default);
        Task ClearSessionAsync(string sessionId, CancellationToken cancellationToken = default);
    }
}
