using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Identity.Application.DTOs.Requests
{
    public class GetUsersQueryRequest
    {
        public string? SearchTerm { get; set; }
        public string? Role { get; set; }
        public string? AccountStatus { get; set; }
        public int? Page { get; set; }
        public int? PageSize { get; set; }
    }
}
