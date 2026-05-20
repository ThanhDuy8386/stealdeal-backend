using System;
using System.Text;
using System.Collections.Generic;

namespace StealDeal.Services.Identity.Domain.Models
{
    public class OauthProvider
    {
        public Guid Id { get;  set; } = Guid.NewGuid();
        public Guid UserId { get;  set; }
        public string Provider { get;  set; } = null!;
        public Guid Provider_UID { get;  set; }

        public User User { get;  set; } = null!;
    }
}
