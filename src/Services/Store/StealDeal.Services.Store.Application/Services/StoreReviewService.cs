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

        public async Task<StoreReviewResponse> CreateAsync(Guid buyerId, CreateReviewRequest request)
        {
            // Validate rating range
            if (request.RatingScore < 1 || request.RatingScore > 5)
                throw new BadRequestException("Rating score must be between 1 and 5.");

            // Prevent duplicate review per order
            var existing = await _reviewRepository.GetByOrderIdAsync(request.OrderId);
            if (existing is not null)
                throw new ConflictException("You have already reviewed this order.");

            // Resolve storeId from the bag being reviewed
            var bag = await _bagRepository.GetByIdAsync(request.BagId);
            if (bag is null)
                throw new NotFoundException("Bag not found.");

            var review = request.ToEntity(buyerId, storeId: bag.StoreId);

            await _reviewRepository.AddAsync(review);
            await _unitOfWork.SaveChangesAsync();

            return review.ToResponse();
        }

        public async Task ReplyAsync(Guid reviewId, Guid ownerId, ReplyReviewRequest request)
        {
            var review = await _reviewRepository.GetByIdAsync(reviewId);
            if (review is null)
                throw new NotFoundException("Review not found.");

            // Verify the owner actually owns the reviewed store
            var store = await _storeRepository.GetByOwnerIdAsync(ownerId);
            if (store is null || review.StoreId != store.Id)
                throw new ForbiddenException("You do not own the store of this review.");

            review.StoreReply = request.StoreReply.Trim();

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

        public async Task<List<StoreReviewResponse>> GetByStoreIdAsync(Guid storeId)
        {
            var reviews = await _reviewRepository.GetByStoreId(storeId);
            return reviews.Select(r => r.ToResponse()).ToList();
        }

        public async Task<List<StoreReviewResponse>> GetByBagIdAsync(Guid bagId)
        {
            var reviews = await _reviewRepository.GetByBagId(bagId);
            return reviews.Select(r => r.ToResponse()).ToList();
        }
    }
}
