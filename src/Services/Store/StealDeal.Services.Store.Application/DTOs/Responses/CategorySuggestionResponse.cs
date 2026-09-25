using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Store.Application.DTOs.Responses
{
    public class CategorySuggestionResponse
    {
        public Guid Id { get; set; }
        public Guid StoreId { get; set; }
        public string StoreName { get; set; } = null!;
        public string SuggestedName { get; set; } = null!;
        public string Status { get; set; } = null!; // Pending, Approved, Rejected
        public string? AdminComment { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
