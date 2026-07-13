using StealDeal.Services.Store.Application.DTOs.Requests;
using StealDeal.Services.Store.Application.DTOs.Responses;
using StealDeal.Services.Store.Domain.Models;

namespace StealDeal.Services.Store.Application.Mappings
{
    public static class StoreReviewMapping
    {
        // CreateRequest -> new Entity
        public static StoreReview ToEntity(this CreateReviewRequest request, Guid buyerId, Guid storeId)
        {
            return new StoreReview
            {
                OrderId = request.OrderId,
                BuyerId = buyerId,
                StoreId = storeId,
                BagId = request.BagId,
                RatingScore = request.RatingScore,
                Comment = request.Comment?.Trim(),
                IsReported = false,
                CreatedAt = DateTime.UtcNow
            };
        }

        // Entity -> Response DTO
        public static StoreReviewResponse ToResponse(this StoreReview review)
        {
            return new StoreReviewResponse
            {
                Id = review.Id,
                OrderId = review.OrderId,
                BuyerId = review.BuyerId,
                RatingScore = review.RatingScore,
                Comment = review.Comment,
                StoreReply = review.StoreReply,
                CreatedAt = review.CreatedAt
            };
        }
    }
}
