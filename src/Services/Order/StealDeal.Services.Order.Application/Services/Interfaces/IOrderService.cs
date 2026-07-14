using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StealDeal.Services.Order.Application.DTOs.Requests;
using StealDeal.Services.Order.Application.DTOs.Response;

namespace StealDeal.Services.Order.Application.Services.Interfaces
{
    public interface IOrderService
    {
        Task<OrderResponse> CreateOrderAsync(Guid userId, CreateOrderRequest request);
        Task<OrderResponse> GetOrderByIdAsync(Guid orderId, Guid userId, string role);
        Task<IEnumerable<OrderResponse>> GetMyOrdersAsync(Guid userId);
        Task<IEnumerable<OrderResponse>> GetStoreOrdersAsync(Guid storeId, Guid ownerId);
        Task<OrderResponse> UpdateOrderStatusAsync(Guid orderId, Guid userId, string role, UpdateOrderStatusRequest request);
    }
}
