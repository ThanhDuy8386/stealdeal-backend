using System;
using System.Text;
using System.Collections.Generic;

namespace StealDeal.Services.Identity.Domain.Models
{
    public class OauthProvider
    {
        public Guid Id { get; protected set; } = Guid.NewGuid();
        public Guid UserId { get; protected set; }
        public string Provider { get; protected set; } = null!;
        public Guid Provider_UID { get; protected set; }

        public User User { get; protected set; } = null!;
    }
}
