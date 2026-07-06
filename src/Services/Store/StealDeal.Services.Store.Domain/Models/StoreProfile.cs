using System;
using System.Collections.Generic;

namespace StealDeal.Services.Store.Domain.Models
{
    public class StoreProfile
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid OwnerId { get; set; }
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        public string? Address { get; set; }
        public decimal Latitude { get; set; }
        public decimal Longitude { get; set; }
        public string? AvatarUrl { get; set; }
        public string? Phone { get; set; }
        public string? BankAccount { get; set; }
        public decimal RatingScore { get; set; }
        public string? LicenseUrl { get; set; }
        public bool IsVerify { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        // Navigation properties
        public ICollection<SurpriseBag> SurpriseBags { get; set; } = new List<SurpriseBag>();
        public ICollection<StoreReview> StoreReviews { get; set; } = new List<StoreReview>();
    }
}
