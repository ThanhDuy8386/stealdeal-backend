using System.ComponentModel.DataAnnotations;

namespace StealDeal.Services.Order.Application.DTOs.Requests;

public sealed class MarkOrderNoShowRequest
{
    // Leave room in the 1000-character ledger note for the actor and audit context.
    public const int MaximumReasonLength = 800;
    public const string DefaultReason = "Customer did not collect the order before the pickup deadline.";

    [StringLength(MaximumReasonLength)]
    public string? Reason { get; set; }
}
