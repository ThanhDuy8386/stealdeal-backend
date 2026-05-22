using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Identity.Domain.Models
{
    public class UserRole
    {
        public Guid Id { get;  set; } = Guid.NewGuid();
        public Guid UserId { get;  set; }
        public string Role { get;  set; } = null!;
        public DateTime AssignedAt { get;  set; } = DateTime.UtcNow;

        public User User { get;  set; } = null!;
    }
}
