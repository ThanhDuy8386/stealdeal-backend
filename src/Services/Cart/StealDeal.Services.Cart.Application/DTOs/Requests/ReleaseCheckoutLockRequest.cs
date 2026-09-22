namespace StealDeal.Services.Cart.Application.DTOs.Requests
{
    public class ReleaseCheckoutLockRequest
    {
        public string LockToken { get; set; } = null!;
    }
}
