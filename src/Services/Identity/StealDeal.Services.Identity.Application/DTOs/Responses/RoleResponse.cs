using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Identity.Application.DTOs.Responses
{
    public class RoleResponse
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
    }
}
