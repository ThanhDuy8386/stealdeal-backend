using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Identity.Application.DTOs.Responses
{
    public class UserAddressResponse
    {
        public Guid Id { get; set; }
        public string Label { get; set; } = null!;
        public string Address { get; set; } = null!;
        public string District { get; set; } = null!;
        public string City { get; set; } = null!;
        public bool IsDefault { get; set; }
    }
}
