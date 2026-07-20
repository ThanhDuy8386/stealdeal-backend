namespace StealDeal.Services.Identity.Application.DTOs.Requests
{
    public class UpdateMyProfileRequest
    {
        public string FullName { get; set; } = null!;
        public string? Phone { get; set; }
        public string? AvatarUrl { get; set; }
    }
}
