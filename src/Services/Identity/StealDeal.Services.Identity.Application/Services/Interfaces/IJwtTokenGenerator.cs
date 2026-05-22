using StealDeal.Services.Identity.Domain.Models;

namespace StealDeal.Services.Identity.Application.Services.Interfaces
{
    public interface IJwtTokenGenerator
    {
        string GenerateAccessToken(User user, IReadOnlyCollection<string> roles);
        string GenerateRefreshToken();
        string HashRefreshToken(string refreshToken);
        DateTime GetAccessTokenExpiresAt();
        DateTime GetRefreshTokenExpiresAt();
    }
}
