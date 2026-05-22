using StealDeal.Services.Identity.Application.DTOs.Requests;
using StealDeal.Services.Identity.Application.DTOs.Responses;

namespace StealDeal.Services.Identity.Application.Services.Interfaces
{
    public interface IAuthService
    {
        Task<TokenResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);
        Task<TokenResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
        Task<TokenResponse> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);
    }
}