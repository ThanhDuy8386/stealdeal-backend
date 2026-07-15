using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Identity.Application.DTOs.Requests
{
    public class AdminUpdateUserRequest
    {
        public string? FullName { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public bool? IsActive { get; set; }
        public List<String>?Roles { get; set; }
    }
}
