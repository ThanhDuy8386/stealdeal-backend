using System.Net;
using System.Net.Http.Json;
using StealDeal.Services.Store.Application.Exceptions;
using StealDeal.Services.Store.Application.Services.Interfaces;

namespace StealDeal.Services.Store.Infrastructure.Services
{
    public class OrderVerificationService : IOrderVerificationService
    {
        private readonly HttpClient _httpClient;

        public OrderVerificationService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<bool> VerifyOwnershipAsync(Guid orderId, Guid buyerId, Guid bagId)
        {
            var url = $"api/orders/{orderId}/review-eligibility?bagId={bagId}&buyerId={buyerId}";
            HttpResponseMessage response;

            try
            {
                response = await _httpClient.GetAsync(url);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new BadRequestException("Order Service is currently unavailable. Please try again later.");
            }

            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            // Extract the exact detail message from OrderService's ProblemDetails
            string errorMessage = "Order is not eligible for review.";
            try
            {
                var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsDto>();
                if (!string.IsNullOrWhiteSpace(problem?.Detail))
                {
                    errorMessage = problem.Detail;
                }
            }
            catch
            {
                // Fallback to default message if response body cannot be parsed
            }

            throw response.StatusCode switch
            {
                HttpStatusCode.NotFound => new NotFoundException(errorMessage),
                HttpStatusCode.Forbidden => new ForbiddenException(errorMessage),
                _ => new BadRequestException(errorMessage)
            };
        }

        private class ProblemDetailsDto
        {
            public string? Detail { get; set; }
        }
    }
}