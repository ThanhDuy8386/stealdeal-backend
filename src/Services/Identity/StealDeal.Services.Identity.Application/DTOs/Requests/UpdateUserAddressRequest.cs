using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Identity.Application.DTOs.Requests
{
    public class UpdateUserAddressRequest
    {
        public string Label { get; set; } = null!;
        public string Address { get; set; } = null!;
        public string Commune { get; set; } = null!;
        public string Province { get; set; } = null!;
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
        public bool IsDefault { get; set; }
    }
}
