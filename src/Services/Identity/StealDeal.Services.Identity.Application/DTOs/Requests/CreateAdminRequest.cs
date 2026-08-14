namespace StealDeal.Services.Identity.Application.DTOs.Requests
{
    public class CreateAdminRequest
    {
        public string Email { get; set; } = null!;
        public string Password { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public string? Phone { get; set; }
        public string? AvatarUrl { get; set; }
        public List<string> Roles { get; set; } = [];
    }
}
