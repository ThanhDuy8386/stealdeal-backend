using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StealDeal.Services.Order.Application.DTOs.Requests;
using StealDeal.Services.Order.Application.DTOs.Response;

namespace StealDeal.Services.Order.Application.Services.Interfaces
{
    public interface IOrderService
    {
        Task<OrderResponse> CreateOrderAsync(Guid userId, string accessToken, CreateOrderRequest request, CancellationToken cancellationToken = default);
        Task<OrderResponse> GetOrderByIdAsync(Guid orderId, Guid userId, IEnumerable<string> roles, CancellationToken cancellationToken = default);
        Task<IEnumerable<OrderResponse>> GetMyOrdersAsync(Guid userId);
        Task<IEnumerable<OrderResponse>> GetStoreOrdersAsync(Guid storeId, Guid userId, IEnumerable<string> roles, CancellationToken cancellationToken = default);
        Task<OrderResponse> UpdateOrderStatusAsync(Guid orderId, Guid userId, IEnumerable<string> roles, UpdateOrderStatusRequest request, CancellationToken cancellationToken = default);
        Task<OrderResponse> MarkNoShowAsync(Guid orderId, Guid userId, IEnumerable<string> roles, MarkOrderNoShowRequest? request, CancellationToken cancellationToken = default);
        Task<OrderReviewEligibilityResponse> CheckReviewEligibilityAsync(Guid orderId, Guid buyerId, Guid bagId);
    }
}
