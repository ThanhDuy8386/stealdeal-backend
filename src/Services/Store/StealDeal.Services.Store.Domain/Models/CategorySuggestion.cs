using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Store.Domain.Models
{
    public class CategorySuggestion
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid StoreId { get; set; }
        public string SuggestedName { get; set; } = null!;
        public string Status { get; set; } = "Pending"; // Pending, Approved, Rejected
        public string? AdminComment { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation property
        public StoreProfile Store { get; set; } = null!;
    }
}
