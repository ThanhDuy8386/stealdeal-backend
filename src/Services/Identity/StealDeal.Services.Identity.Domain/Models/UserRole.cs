using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Identity.Domain.Models
{
    public class UserRole
    {
        public Guid Id { get; protected set; } = Guid.NewGuid();
        public Guid UserId { get; protected set; }
        public string Role { get; protected set; }
        public DateTime AssignedAt { get; protected set; } = DateTime.UtcNow;

        public User User { get; protected set; }
    }
}
