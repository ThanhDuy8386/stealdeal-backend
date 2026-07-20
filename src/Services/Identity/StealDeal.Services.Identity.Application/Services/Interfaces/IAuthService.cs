using StealDeal.Services.Identity.Application.DTOs.Requests;
using StealDeal.Services.Identity.Application.DTOs.Responses;

namespace StealDeal.Services.Identity.Application.Services.Interfaces
{
    public interface IAuthService
    {
        //Task<TokenResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);
        Task<RegistrationResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);
        Task<TokenResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
        Task<TokenResponse> RefreshAsync(RefreshTokenRequest refreshToken, CancellationToken cancellationToken = default);
        Task VerifyEmailOtpAsync(VerifyEmailOtpRequest request, CancellationToken cancellationToken = default);
        Task ResendOtpAsync(ResendOtpRequest request, CancellationToken cancellationToken = default);
        Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default);
    }
}