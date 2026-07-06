using System;
using System.Collections.Generic;

namespace StealDeal.Services.Store.Domain.Models
{
    public class Category
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = null!;
        public string Slug { get; set; } = null!;
        public string? IconUrl { get; set; }
        public bool IsActive { get; set; }

        // Navigation properties
        public ICollection<SurpriseBag> SurpriseBags { get; set; } = new List<SurpriseBag>();
    }
}
