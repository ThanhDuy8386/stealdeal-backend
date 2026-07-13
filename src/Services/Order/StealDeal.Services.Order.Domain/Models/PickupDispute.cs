using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Order.Domain.Models
{
    public class PickupDispute
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid OrderId { get; set; }
        public Guid ReporterId { get; set; }

        public string DisputeType { get; set; } = null!;

        // List of evidence links (images, videos...)
        public List<string> EvidenceUrls { get; set; } = new();

        public string Description { get; set; } = null!;
        public string Status { get; set; } = null!;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public OrderProfile OrderProfile { get; set; } = null!;
    }
}
