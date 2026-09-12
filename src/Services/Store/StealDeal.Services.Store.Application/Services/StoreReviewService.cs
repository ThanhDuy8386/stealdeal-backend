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
        private readonly IUnitOfWork _unitOfWork;

        public StoreReviewService(
            IStoreReviewRepository reviewRepository,
            ISurpriseBagRepository bagRepository,
            IStoreProfileRepository storeRepository,
            IUnitOfWork unitOfWork)
        {
            _reviewRepository = reviewRepository;
            _bagRepository = bagRepository;
            _storeRepository = storeRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<StoreReviewResponse> CreateAsync(Guid buyerId, string buyerName, CreateReviewRequest request)
        {
            // Validate rating range
            if (request.RatingScore < 1 || request.RatingScore > 5)
                throw new BadRequestException("Rating score must be between 1 and 5.");

            // TODO: Validate order ownership & completion status using IOrderVerificationService
            // once Order Service checkout/saga statuses are stabilized.
            // Example:
            // var isEligible = await _orderVerificationService.VerifyOwnershipAsync(request.OrderId, buyerId, request.BagId);
            // if (!isEligible)
            //     throw new ForbiddenException("You are not eligible to review this order item.");

            // Prevent duplicate review per bag for this order
            var existing = await _reviewRepository.GetByOrderAndBagAsync(request.OrderId, request.BagId);
            if (existing is not null)
                throw new ConflictException("You have already reviewed this bag for this order.");

            // Resolve storeId from the bag being reviewed
            var bag = await _bagRepository.GetByIdAsync(request.BagId);
            if (bag is null)
                throw new NotFoundException("Bag not found.");

            var review = request.ToEntity(buyerId, buyerName, storeId: bag.StoreId);

            // Update Store RatingScore and ReviewCount (Incremental formula)
            var store = await _storeRepository.GetByIdAsync(bag.StoreId);
            if (store is not null)
            {
                var newCount = store.ReviewCount + 1;
                var newScore = ((store.RatingScore * store.ReviewCount) + request.RatingScore) / newCount;

                store.ReviewCount = newCount;
                store.RatingScore = Math.Round(newScore, 2);
                _storeRepository.Update(store);
            }

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

        public async Task<PagedResult<StoreReviewResponse>> GetByStoreIdAsync(Guid storeId, int page, int pageSize)
        {
            page = page < 1 ? 1 : page;
            pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

            var (items, totalCount) = await _reviewRepository.GetByStoreIdAsync(storeId, page, pageSize);

            return new PagedResult<StoreReviewResponse>
            {
                Items = items.Select(r => r.ToResponse()).ToList(),
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount
            };
        }

        public async Task<PagedResult<StoreReviewResponse>> GetByBagIdAsync(Guid bagId, int page, int pageSize)
        {
            page = page < 1 ? 1 : page;
            pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

            var (items, totalCount) = await _reviewRepository.GetByBagIdAsync(bagId, page, pageSize);

            return new PagedResult<StoreReviewResponse>
            {
                Items = items.Select(r => r.ToResponse()).ToList(),
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount
            };
        }
    }
}
