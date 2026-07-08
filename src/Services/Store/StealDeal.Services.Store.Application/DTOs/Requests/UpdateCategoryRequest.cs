using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Store.Application.DTOs.Requests
{
    public class UpdateCategoryRequest
    {
        public string Name { get; set; } = null!;
        public string Slug { get; set; } = null!;
        public string? IconUrl { get; set; }
        public bool IsActive { get; set; }
    }
}
