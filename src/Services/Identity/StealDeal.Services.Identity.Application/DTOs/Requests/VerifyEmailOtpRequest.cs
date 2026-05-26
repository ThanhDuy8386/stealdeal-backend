namespace StealDeal.Services.Identity.Application.DTOs.Requests
{
    public class VerifyEmailOtpRequest
    {
        public string Email { get; set; } = null!;
        public string Otp { get; set; } = null!;
    }
}