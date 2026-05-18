using System;

namespace StealDeal.Services.Identity.Domain.Models
{
    public class UserAddress
    {
        public Guid Id { get; protected set; } = Guid.NewGuid();
        public Guid UserId { get; protected set; }
        public string Label { get; protected set; } = null!;
        public string Address { get; protected set; } = null!;
        public string District { get; protected set; } = null!;
        public string City { get; protected set; } = null!;
        public decimal Longitude { get; protected set; }
        public decimal Latitude { get; protected set; }
        public bool IsDefault { get; protected set; }
        public DateTime CreatedAt { get; protected set; } = DateTime.UtcNow;

        public User User { get; protected set; } = null!;
    }
}
