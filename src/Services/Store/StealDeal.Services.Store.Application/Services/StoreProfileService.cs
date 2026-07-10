using StealDeal.Services.Store.Application.DTOs.Requests;
using StealDeal.Services.Store.Application.DTOs.Responses;
using StealDeal.Services.Store.Application.Exceptions;
using StealDeal.Services.Store.Application.Mappings;
using StealDeal.Services.Store.Application.Services.Interfaces;
using StealDeal.Services.Store.Domain.Interfaces;

namespace StealDeal.Services.Store.Application.Services
{
    public class StoreProfileService : IStoreProfileService
    {
        private readonly IStoreProfileRepository _storeRepository;
        private readonly IUnitOfWork _unitOfWork;

        public StoreProfileService(
            IStoreProfileRepository storeRepository,
            IUnitOfWork unitOfWork)
        {
            _storeRepository = storeRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<StoreProfileResponse> CreateAsync(Guid ownerId, CreateStoreRequest request)
        {
            var alreadyExists = await _storeRepository.ExistsByOwnerIdAsync(ownerId);
            if (alreadyExists)
                throw new ConflictException("You already have a store.");

            var store = request.ToEntity(ownerId);

            await _storeRepository.AddAsync(store);
            await _unitOfWork.SaveChangesAsync();

            return store.ToResponse();
        }

        public async Task<StoreProfileResponse> UpdateAsync(Guid storeId, Guid ownerId, UpdateStoreRequest request)
        {
            var store = await _storeRepository.GetByIdAsync(storeId);
            if (store is null)
                throw new NotFoundException("Store not found.");

            if (store.OwnerId != ownerId)
                throw new ForbiddenException("You do not own this store.");

            request.UpdateEntity(store);

            _storeRepository.Update(store);
            await _unitOfWork.SaveChangesAsync();

            return store.ToResponse();
        }

        public async Task<StoreProfileResponse> GetByIdAsync(Guid id)
        {
            var store = await _storeRepository.GetByIdAsync(id);
            if (store is null)
                throw new NotFoundException("Store not found.");

            return store.ToResponse();
        }

        public async Task<StoreProfileResponse> GetMyStoreAsync(Guid ownerId)
        {
            var store = await _storeRepository.GetByOwnerIdAsync(ownerId);
            if (store is null)
                throw new NotFoundException("You do not have a store yet.");

            return store.ToResponse();
        }

        public async Task<List<StoreProfileResponse>> GetAllAsync()
        {
            var stores = await _storeRepository.GetAllAsync();
            return stores.Select(s => s.ToResponse()).ToList();
        }

        public async Task VerifyStoreAsync(Guid storeId)
        {
            var store = await _storeRepository.GetByIdAsync(storeId);
            if (store is null)
                throw new NotFoundException("Store not found.");

            store.IsVerify = true;
            store.UpdatedAt = DateTime.UtcNow;

            _storeRepository.Update(store);
            await _unitOfWork.SaveChangesAsync();
        }

        public async Task ToggleActiveAsync(Guid storeId)
        {
            var store = await _storeRepository.GetByIdAsync(storeId);
            if (store is null)
                throw new NotFoundException("Store not found.");

            store.IsActive = !store.IsActive;
            store.UpdatedAt = DateTime.UtcNow;

            _storeRepository.Update(store);
            await _unitOfWork.SaveChangesAsync();
        }
    }
}
