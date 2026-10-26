using System;

namespace StealDeal.Services.Identity.Domain.Models
{
    public class UserAddress
    {
        public Guid Id { get;  set; } = Guid.NewGuid();
        public Guid UserId { get;  set; }
        public string Label { get;  set; } = null!;
        public string Address { get;  set; } = null!;
        public string Commune { get;  set; } = null!;
        public string Province { get;  set; } = null!;
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
        public bool IsDefault { get;  set; }
        public DateTime CreatedAt { get;  set; } = DateTime.UtcNow;

        public User User { get;  set; } = null!;
    }
}
