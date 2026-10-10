using StealDeal.Services.Store.Application.DTOs.Responses;

namespace StealDeal.Services.Store.Application.Services.Interfaces
{
    public interface ILocationService
    {
        Task<List<AutocompleteSuggestionResponse>> AutocompleteAsync(
            string input,
            string? sessionToken = null,
            decimal? latitude = null,
            decimal? longitude = null,
            CancellationToken cancellationToken = default);

        Task<PlaceDetailResponse> GetPlaceDetailAsync(
            string placeId,
            string? sessionToken = null,
            CancellationToken cancellationToken = default);
    }
}
