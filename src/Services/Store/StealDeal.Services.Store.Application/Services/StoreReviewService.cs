using StealDeal.Services.Store.Application.DTOs.Requests;
using StealDeal.Services.Store.Application.DTOs.Responses;
using StealDeal.Services.Store.Application.Exceptions;
using StealDeal.Services.Store.Application.Mappings;
using StealDeal.Services.Store.Application.Services.Interfaces;
using StealDeal.Services.Store.Domain.Interfaces;

namespace StealDeal.Services.Store.Application.Services
{
    public class StoreReviewService : IStoreReviewService
    {
        private const int MaxPageSize = 50;

        private readonly IStoreReviewRepository _reviewRepository;
        private readonly ISurpriseBagRepository _bagRepository;
        private readonly IStoreProfileRepository _storeRepository;
        private readonly IOrderVerificationService _orderVerificationService;
        private readonly IUnitOfWork _unitOfWork;

        public StoreReviewService(
            IStoreReviewRepository reviewRepository,
            ISurpriseBagRepository bagRepository,
            IStoreProfileRepository storeRepository,
            IOrderVerificationService orderVerificationService,
            IUnitOfWork unitOfWork)
        {
            _reviewRepository = reviewRepository;
            _bagRepository = bagRepository;
            _storeRepository = storeRepository;
            _orderVerificationService = orderVerificationService;
            _unitOfWork = unitOfWork;
        }

        public async Task<StoreReviewResponse> CreateAsync(Guid buyerId, string buyerName, CreateReviewRequest request)
        {
            // Validate rating range
            if (request.RatingScore < 1 || request.RatingScore > 5)
                throw new BadRequestException("Rating score must be between 1 and 5.");

            // Prevent duplicate review per bag for this order
            var existing = await _reviewRepository.GetByOrderAndBagAsync(request.OrderId, request.BagId);
            if (existing is not null)
                throw new ConflictException("You have already reviewed this bag for this order.");

            // cross service verification to ensure the buyer actually owns the order and is eligible to review
            var isEligible = await _orderVerificationService.VerifyOwnershipAsync(request.OrderId, buyerId, request.BagId);
            if (!isEligible)
                throw new ForbiddenException("You are not eligible to review this order item.");

            // Resolve storeId from the bag being reviewed
            var bag = await _bagRepository.GetByIdAsync(request.BagId);
            if (bag is null)
                throw new NotFoundException("Bag not found.");


            // create entity
            var review = request.ToEntity(buyerId, buyerName, storeId: bag.StoreId);

            await UpdateStoreRatingAsync(bag.StoreId, request.RatingScore, isAdding: true);
            await _reviewRepository.AddAsync(review);
            await _unitOfWork.SaveChangesAsync();

            // Attach bag reference for response mapping
            review.Bag = bag;

            return review.ToResponse();
        }

        public async Task ReplyAsync(Guid reviewId, Guid ownerId, ReplyReviewRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.StoreReply))
                throw new BadRequestException("Reply content cannot be empty.");

            var review = await _reviewRepository.GetByIdAsync(reviewId);
            if (review is null)
                throw new NotFoundException("Review not found.");

            // Verify the owner actually owns the reviewed store
            var store = await _storeRepository.GetByOwnerIdAsync(ownerId);
            if (store is null || review.StoreId != store.Id)
                throw new ForbiddenException("You do not own the store of this review.");

            review.StoreReply = request.StoreReply.Trim();
            review.RepliedAt = DateTime.UtcNow;

            _reviewRepository.Update(review);
            await _unitOfWork.SaveChangesAsync();
        }

        public async Task ReportAsync(Guid reviewId, Guid userId)
        {
            var review = await _reviewRepository.GetByIdAsync(reviewId);
            if (review is null)
                throw new NotFoundException("Review not found.");

            if (review.IsReported)
                throw new ConflictException("This review has already been reported.");

            review.IsReported = true;

            _reviewRepository.Update(review);
            await _unitOfWork.SaveChangesAsync();
        }

        public async Task<PagedResult<StoreReviewResponse>> GetByStoreIdAsync(Guid storeId, ReviewFilterRequest filter)
        {
            ValidateFilter(filter);

            var (items, totalCount) = await _reviewRepository.GetByStoreIdAsync(storeId,
                filter.Page,filter.PageSize,
                filter.RatingScore, filter.HasReply, filter.Search, isReported : null
            );

            return new PagedResult<StoreReviewResponse>
            {
                Items = items.Select(r => r.ToResponse()).ToList(),
                Page = filter.Page,
                PageSize = filter.PageSize,
                TotalCount = totalCount
            };
        }

        public async Task<PagedResult<StoreReviewResponse>> GetByBagIdAsync(Guid bagId, ReviewFilterRequest filter)
        {
            ValidateFilter(filter);

            var (items, totalCount) = await _reviewRepository.GetByBagIdAsync(bagId,
                filter.Page, filter.PageSize,
                filter.RatingScore, filter.HasReply, filter.Search);

            return new PagedResult<StoreReviewResponse>
            {
                Items = items.Select(r => r.ToResponse()).ToList(),
                Page = filter.Page,
                PageSize = filter.PageSize,
                TotalCount = totalCount
            };
        }

        public async Task<PagedResult<StoreReviewResponse>> GetByStoreOwnerAsync(Guid ownerId, ReviewFilterRequest filter)
        {
            var store = await _storeRepository.GetByOwnerIdAsync(ownerId);
            if (store is null)
                throw new NotFoundException("You do not have a store yet.");

            ValidateFilter(filter);

            var (items, totalCount) = await _reviewRepository.GetByStoreIdAsync(store.Id,
                filter.Page, filter.PageSize,
                filter.RatingScore, filter.HasReply, filter.Search, filter.IsReported);

            return new PagedResult<StoreReviewResponse>
            {
                Items = items.Select(r => r.ToResponse(includeReportStatus: true)).ToList(),
                Page = filter.Page,
                PageSize = filter.PageSize,
                TotalCount = totalCount
            };
        }

        public async Task<PagedResult<StoreReviewResponse>> GetReportedAsync(int page, int pageSize)
        {
            page = page < 1 ? 1 : page;
            pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

            var (items, totalCount) = await _reviewRepository.GetReportedAsync(page, pageSize);

            return new PagedResult<StoreReviewResponse>
            {
                Items = items.Select(r => r.ToResponse(includeReportStatus: true)).ToList(),
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount
            };
        }

        public async Task DeleteReplyAsync(Guid reviewId, Guid ownerId)
        {
            var review = await _reviewRepository.GetByIdAsync(reviewId);
            if (review is null)
                throw new NotFoundException("Review not found.");

            var store = await _storeRepository.GetByOwnerIdAsync(ownerId);
            if (store is null || review.StoreId != store.Id)
                throw new ForbiddenException("You do not own the store of this review.");

            review.StoreReply = null;
            review.RepliedAt = null;

            _reviewRepository.Update(review);
            await _unitOfWork.SaveChangesAsync();
        }

        public async Task UnreportAsync(Guid reviewId, Guid userId, bool isAdmin)
        {
            var review = await _reviewRepository.GetByIdAsync(reviewId);
            if (review is null)
                throw new NotFoundException("Review not found.");
            
            if (!review.IsReported)
                throw new ConflictException("This review is not currently reported.");

            if (!isAdmin)
            {
                var store = await _storeRepository.GetByOwnerIdAsync(userId);
                if (store is null || review.StoreId != store.Id)
                    throw new ForbiddenException("You do not have permission to act on this review.");
            }

            review.IsReported = false;
            _reviewRepository.Update(review);
            await _unitOfWork.SaveChangesAsync();
        }

        public async Task DeleteReviewAsync(Guid reviewId)
        {
            var review = await _reviewRepository.GetByIdAsync(reviewId);
            if (review is null)
                throw new NotFoundException("Review not found.");

            var storeId = review.StoreId;

            await UpdateStoreRatingAsync(storeId, review.RatingScore, isAdding: false);
            _reviewRepository.Delete(review);

            await _unitOfWork.SaveChangesAsync();
        }

        private async Task UpdateStoreRatingAsync(Guid storeId, int ratingScore, bool isAdding = true)
        {
            var (count, sum) = await _reviewRepository.GetRatingStatsAsync(storeId);

            var newCount = isAdding ? count + 1 : count - 1;
            var newSum = isAdding ? sum + ratingScore : sum - ratingScore;

            var newAverage = newCount > 0
                ? Math.Round(newSum / (decimal)newCount, 2)
                : 0m;

            var store = await _storeRepository.GetByIdAsync(storeId);
            if (store is not null)
            {
                store.ReviewCount = Math.Max(0, newCount);
                store.RatingScore = newAverage;
                _storeRepository.Update(store);
            }
        }

        private static void ValidateFilter(ReviewFilterRequest filter)
        {
            filter.Page = filter.Page < 1 ? 1 : filter.Page;
            filter.PageSize = Math.Clamp(filter.PageSize, 1, MaxPageSize);
            if (filter.RatingScore.HasValue && (filter.RatingScore < 1 || filter.RatingScore > 5))
            {
                throw new BadRequestException("Rating score must be between 1 and 5.");
            }
        }
    }
}
