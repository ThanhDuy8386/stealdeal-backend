using StealDeal.Services.Order.Application.DTOs.Requests;
using StealDeal.Services.Order.Application.DTOs.Response;

namespace StealDeal.Services.Order.Application.Services.Interfaces
{
    public interface IOrderCheckoutService
    {
        Task<OrderResponse> CheckoutFromCartAsync(
            Guid userId,
            string accessToken,
            CheckoutFromCartRequest request,
            CancellationToken cancellationToken = default);
    }
}
