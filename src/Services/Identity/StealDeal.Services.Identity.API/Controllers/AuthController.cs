using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StealDeal.Services.Identity.Application.DTOs.Requests;
using StealDeal.Services.Identity.Application.Services.Interfaces;
using StealDeal.Services.Identity.Application.DTOs.Responses;

namespace Identity.StealDeal.Services.Identity.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;
        private const string RefreshTokenCookieName = "refresh_token";
        public AuthController(IAuthService authService)
        {
            _authService = authService;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register(RegisterRequest request, CancellationToken cancellationToken)
        {
            var response = await _authService.RegisterAsync(request, cancellationToken);
            //SetRefreshTokenCookie(tokenResponse);
            //return Ok(new AccessTokenResponse
            //{
            //    AccessToken = tokenResponse.AccessToken,
            //    AccessTokenExpiresAt = tokenResponse.AccessTokenExpiresAt
            //});
            return Ok(response);
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
        {
            var tokenResponse = await _authService.LoginAsync(request, cancellationToken);
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

            var tokenResponse = await _authService.RefreshAsync(
                new RefreshTokenRequest
                {
                    RefreshToken = refreshToken,
                },
                cancellationToken);

            SetRefreshTokenCookie(tokenResponse);

            return Ok(new AccessTokenResponse
            {
                AccessToken = tokenResponse.AccessToken,
                AccessTokenExpiresAt = tokenResponse.AccessTokenExpiresAt
            });
        }

        [HttpPost("verify-email")]
        public async Task<IActionResult> VerifyEmail(VerifyEmailOtpRequest request, CancellationToken cancellationToken)
        {
            await _authService.VerifyEmailOtpAsync(request, cancellationToken);
            return Ok(new { message = "Email verified successfully." });
        }

        [HttpPost("resend-otp")]
        public async Task<IActionResult> ResendOtp(ResendOtpRequest request, CancellationToken cancellationToken)
        {
            await _authService.ResendOtpAsync(request, cancellationToken);
            return Ok(new { message = "OTP resent successfully." });
        }

        [Authorize]
        [HttpGet("me")]
        public IActionResult Me()
        {
            return Ok(new
            {
                UserId = User.FindFirst("sub")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
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
                await _authService.LogoutAsync(refreshToken, cancellationToken);
            }

            Response.Cookies.Delete(RefreshTokenCookieName, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                Path = "/api/auth",
                IsEssential = true,
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
                    Path = "/api/auth",
                    IsEssential = true,
                });
        }
    }
}
