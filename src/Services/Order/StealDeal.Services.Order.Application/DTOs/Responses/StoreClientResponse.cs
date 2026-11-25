namespace StealDeal.Services.Order.Application.DTOs.Response;

public sealed class StoreClientResponse
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Name { get; set; } = null!;
    public bool IsActive { get; set; }
    public bool IsVerify { get; set; }
}
