using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StealDeal.Services.Identity.Application.DTOs.Requests;
using StealDeal.Services.Identity.Application.DTOs.Responses;
using StealDeal.Services.Identity.Application.Services.Interfaces;

namespace StealDeal.Services.Identity.API.Controllers
{
    [ApiController]
    [Route("api/admin-auth")]
    public class AdminAuthController : ControllerBase
    {
        private const string RefreshTokenCookieName = "admin_refresh_token";
        private const string RefreshTokenCookiePath = "/api/admin-auth";
        private readonly IAdminAuthService _adminAuthService;

        public AdminAuthController(IAdminAuthService adminAuthService)
        {
            _adminAuthService = adminAuthService;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
        {
            var tokenResponse = await _adminAuthService.LoginAsync(request, cancellationToken);
            SetRefreshTokenCookie(tokenResponse);

            return Ok(new AccessTokenResponse
            {
                AccessToken = tokenResponse.AccessToken,
                AccessTokenExpiresAt = tokenResponse.AccessTokenExpiresAt
            });
        }

        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
        {
            var refreshToken = Request.Cookies[RefreshTokenCookieName];

            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                return Unauthorized(new
                {
                    message = "Refresh token is missing."
                });
            }

            var tokenResponse = await _adminAuthService.RefreshAsync(
                new RefreshTokenRequest
                {
                    RefreshToken = refreshToken
                },
                cancellationToken);

            SetRefreshTokenCookie(tokenResponse);

            return Ok(new AccessTokenResponse
            {
                AccessToken = tokenResponse.AccessToken,
                AccessTokenExpiresAt = tokenResponse.AccessTokenExpiresAt
            });
        }

        [Authorize(Roles = "Admin,SuperAdmin")]
        [HttpGet("me")]
        public IActionResult Me()
        {
            return Ok(new
            {
                AdminId = User.FindFirst("sub")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
                Email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value,
                Name = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value,
                Roles = User.FindAll(System.Security.Claims.ClaimTypes.Role).Select(role => role.Value).ToList()
            });
        }

        [HttpPost("logout")]
        public async Task<IActionResult> Logout(CancellationToken cancellationToken)
        {
            var refreshToken = Request.Cookies[RefreshTokenCookieName];

            if (!string.IsNullOrWhiteSpace(refreshToken))
            {
                await _adminAuthService.LogoutAsync(refreshToken, cancellationToken);
            }

            Response.Cookies.Delete(RefreshTokenCookieName, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                Path = RefreshTokenCookiePath,
                IsEssential = true
            });

            return Ok(new { message = "Logged out successfully." });
        }

        private void SetRefreshTokenCookie(TokenResponse tokenResponse)
        {
            Response.Cookies.Append(
                RefreshTokenCookieName,
                tokenResponse.RefreshToken,
                new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Lax,
                    Expires = tokenResponse.RefreshTokenExpiresAt,
                    Path = RefreshTokenCookiePath,
                    IsEssential = true
                });
        }
    }
}
