namespace StealDeal.Services.Identity.Application.DTOs.Responses;

public class AccessTokenResponse
{
    public string AccessToken { get; set; } = null!;
    public DateTime AccessTokenExpiresAt { get; set; }
}