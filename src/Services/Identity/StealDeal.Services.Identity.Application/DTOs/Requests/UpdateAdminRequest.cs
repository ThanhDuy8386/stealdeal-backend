namespace StealDeal.Services.Identity.Application.DTOs.Requests
{
    public class UpdateAdminRequest
    {
        public string? Email { get; set; }
        public string? Password { get; set; }
        public string? FullName { get; set; }
        public string? Phone { get; set; }
        public string? AvatarUrl { get; set; }
        public bool? IsActive { get; set; }
        public List<string>? Roles { get; set; }
    }
}
