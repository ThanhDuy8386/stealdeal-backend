using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Store.Application.DTOs.Requests
{
    public class ReviewCategorySuggestionRequest
    {
        public string Status { get; set; } = null!; // Approved or Rejected
        public string? AdminComment { get; set; }
        public string? IconUrl { get; set; } // Optional: URL for the category icon if approved
    }
}
