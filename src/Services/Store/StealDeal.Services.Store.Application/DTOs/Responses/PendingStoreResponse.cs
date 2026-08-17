namespace StealDeal.Services.Store.Application.DTOs.Responses
{
    public class PendingStoreResponse
    {
        public Guid Id { get; set; }
        public Guid OwnerId { get; set; }
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        public string? Address { get; set; }
        public decimal Latitude { get; set; }
        public decimal Longitude { get; set; }
        public string? Phone { get; set; }
        public string? AvatarUrl { get; set; }
        public string? LicenseUrl { get; set; }
        public bool IsVerify { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
