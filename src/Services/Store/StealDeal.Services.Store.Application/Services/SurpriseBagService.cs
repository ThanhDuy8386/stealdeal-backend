using StealDeal.Services.Store.Application.DTOs.Requests;
using StealDeal.Services.Store.Application.DTOs.Responses;
using StealDeal.Services.Store.Application.Exceptions;
using StealDeal.Services.Store.Application.Mappings;
using StealDeal.Services.Store.Application.Services.Interfaces;
using StealDeal.Services.Store.Domain.Interfaces;
using StealDeal.Services.Store.Domain.Models;

namespace StealDeal.Services.Store.Application.Services
{
    public class SurpriseBagService : ISurpriseBagService
    {
        private readonly ISurpriseBagRepository _bagRepository;
        private readonly IStoreProfileRepository _storeRepository;
        private readonly ICategoryRepository _categoryRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IS3StorageService _s3StorageService;

        public SurpriseBagService(
            ISurpriseBagRepository bagRepository,
            IStoreProfileRepository storeRepository,
            ICategoryRepository categoryRepository,
            IUnitOfWork unitOfWork,
            IS3StorageService s3StorageService)
        {
            _bagRepository = bagRepository;
            _storeRepository = storeRepository;
            _categoryRepository = categoryRepository;
            _unitOfWork = unitOfWork;
            _s3StorageService = s3StorageService;
        }

        public async Task<SurpriseBagResponse> CreateAsync(Guid ownerId, CreateBagRequest request,
            FileUploadRequest? image = null,
            CancellationToken cancellationToken = default)
        {
            // Resolve store from owner
            var store = await _storeRepository.GetByOwnerIdAsync(ownerId);
            if (store is null)
                throw new NotFoundException("You do not have a store. Create a store first.");

            if (!store.IsActive || !store.IsVerify)
                throw new ForbiddenException("Your store must be verified and active to create bags.");

            // Assign N:N categories & Validate categories
            var categories = new List<Category>();
            foreach (var categoryId in request.CategoryIds)
            {
                var category = await _categoryRepository.GetByIdAsync(categoryId);
                if (category is null)
                    throw new BadRequestException($"Category '{categoryId}' not found.");

                categories.Add(category);
            }

            // Build entity
            var bag = request.ToEntity(store.Id);
            // add categories to bag
            foreach (var category in categories)
            {
                bag.Categories.Add(category);
            }

            // If an image was attached, upload it now under surprise-bags/{storeId}/{bagId}
            if (image != null)
            {
                using (image.Stream)
                {
                    var folder = $"surprise-bags/{store.Id}/{bag.Id}";
                    var imageUrl = await _s3StorageService.UploadImageAsync(
                        image.Stream,
                        image.FileName,
                        image.ContentType,
                        image.Length,
                        folder,
                        cancellationToken);
                    bag.ImageUrl = imageUrl;
                }
            }

            await _bagRepository.AddAsync(bag);
            await _unitOfWork.SaveChangesAsync();

            // Attach store for mapping (navigation property not loaded after insert)
            bag.Store = store;

            return bag.ToResponse();
        }

        public async Task<SurpriseBagResponse> UpdateAsync(Guid bagId, Guid ownerId, UpdateBagRequest request, 
            FileUploadRequest? image = null,
            CancellationToken cancellationToken = default)
        {
            var bag = await _bagRepository.GetByIdAsync(bagId);
            if (bag is null)
                throw new NotFoundException("Bag not found.");

            var store = await _storeRepository.GetByOwnerIdAsync(ownerId);
            if (store is null || bag.StoreId != store.Id)
                throw new ForbiddenException("You do not own this bag.");

            request.UpdateEntity(bag);

            // If a new image was uploaded, upload to S3 and update ImageUrl
            if (image != null)
            {
                using (image.Stream)
                {
                    var folder = $"surprise-bags/{store.Id}/{bag.Id}";
                    var imageUrl = await _s3StorageService.UploadImageAsync(
                        image.Stream,
                        image.FileName,
                        image.ContentType,
                        image.Length,
                        folder,
                        cancellationToken);
                    bag.ImageUrl = imageUrl;
                }
            }

            // Re-assign categories if provided
            if (request.CategoryIds is { Count: > 0 })
            {
                bag.Categories.Clear();

                foreach (var categoryId in request.CategoryIds)
                {
                    var category = await _categoryRepository.GetByIdAsync(categoryId);
                    if (category is null)
                        throw new BadRequestException($"Category '{categoryId}' not found.");

                    bag.Categories.Add(category);
                }
            }

            _bagRepository.Update(bag);
            await _unitOfWork.SaveChangesAsync();

            return bag.ToResponse();
        }

        public async Task DeleteAsync(Guid bagId)
        {
            var bag = await _bagRepository.GetByIdAsync(bagId);
            if (bag is null)
                throw new NotFoundException("Bag not found.");

            _bagRepository.Delete(bag);
            await _unitOfWork.SaveChangesAsync();
        }

        public async Task<SurpriseBagResponse> GetByIdAsync(Guid id)
        {
            var bag = await _bagRepository.GetByIdAsync(id);
            if (bag is null)
                throw new NotFoundException("Bag not found.");

            return bag.ToResponse();
        }

        public async Task<List<SurpriseBagResponse>> GetAllAsync()
        {
            var bags = await _bagRepository.GetAllAsync();
            return bags.Select(b => b.ToResponse()).ToList();
        }

        public async Task<List<SurpriseBagResponse>> GetByStoreIdAsync(Guid storeId)
        {
            var bags = await _bagRepository.GetByStoreIdAsync(storeId);
            return bags.Select(b => b.ToResponse()).ToList();
        }

        public async Task UpdateStatusAsync(Guid bagId, Guid ownerId, string status)
        {
            var bag = await _bagRepository.GetByIdAsync(bagId);
            if (bag is null)
                throw new NotFoundException("Bag not found.");

            var store = await _storeRepository.GetByOwnerIdAsync(ownerId);
            if (store is null || bag.StoreId != store.Id)
                throw new ForbiddenException("You do not own this bag.");

            bag.Status = status;
            bag.UpdatedAt = DateTime.UtcNow;

            _bagRepository.Update(bag);
            await _unitOfWork.SaveChangesAsync();
        }
    }
}
