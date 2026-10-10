using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using StealDeal.Services.Store.Application.DTOs.Responses;
using StealDeal.Services.Store.Application.Exceptions;
using StealDeal.Services.Store.Application.Services.Interfaces;
using StealDeal.Services.Store.Infrastructure.Configuration;

namespace StealDeal.Services.Store.Infrastructure.Services
{
    public class GeoapifyLocationService : ILocationService
    {
        private readonly HttpClient _httpClient;
        private readonly GeoapifySettings _settings;
        private readonly IMemoryCache _cache;

        public GeoapifyLocationService(
            HttpClient httpClient,
            IOptions<GeoapifySettings> settings,
            IMemoryCache cache)
        {
            _httpClient = httpClient;
            _settings = settings.Value;
            _cache = cache;
        }

        public async Task<List<AutocompleteSuggestionResponse>> AutocompleteAsync(
            string input,
            string? sessionToken = null,
            decimal? latitude = null,
            decimal? longitude = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(input))
                throw new BadRequestException("Search input is required.");

            var trimmedInput = input.Trim();
            if (trimmedInput.Length < 3)
                throw new BadRequestException("Search input must be at least 3 characters.");

            if (trimmedInput.Length > 200)
                throw new BadRequestException("Search input must not exceed 200 characters.");

            EnsureApiKeyConfigured();

            var normalizedInput = trimmedInput.ToLowerInvariant();
            var proximityParam = (latitude.HasValue && longitude.HasValue)
                ? $"{longitude.Value.ToString(CultureInfo.InvariantCulture)},{latitude.Value.ToString(CultureInfo.InvariantCulture)}"
                : null;

            var cacheKey = $"geoapify:autocomplete:{normalizedInput}:{proximityParam}";
            if (_cache.TryGetValue(cacheKey, out List<AutocompleteSuggestionResponse>? cached) && cached is not null)
                return cached;

            var baseUrl = _settings.BaseUrl.TrimEnd('/');
            var url = $"{baseUrl}/v1/geocode/autocomplete?text={Uri.EscapeDataString(trimmedInput)}&filter=countrycode:vn&lang=vi&apiKey={Uri.EscapeDataString(_settings.ApiKey)}";

            if (!string.IsNullOrEmpty(proximityParam))
                url += $"&bias=proximity:{Uri.EscapeDataString(proximityParam)}";

            var response = await SendRequestAsync(url, cancellationToken);
            var payload = await response.Content.ReadFromJsonAsync<GeoapifyFeatureCollection>(cancellationToken: cancellationToken);

            var features = payload?.Features ?? [];
            var suggestions = features
                .Where(f => !string.IsNullOrWhiteSpace(f.Properties?.PlaceId))
                .Select(f =>
                {
                    var props = f.Properties!;
                    var mainText = props.AddressLine1 ?? props.Street ?? props.Name ?? props.Formatted ?? string.Empty;
                    var secondaryText = props.AddressLine2 ?? BuildSecondaryText(props);

                    return new AutocompleteSuggestionResponse
                    {
                        PlaceId = props.PlaceId!,
                        Description = props.Formatted ?? $"{mainText}, {secondaryText}".Trim(',', ' '),
                        MainText = mainText,
                        SecondaryText = secondaryText,
                        Commune = props.Suburb ?? props.Quarter,
                        Province = props.State ?? props.City,
                        Latitude = props.Lat ?? (decimal?)f.Geometry?.Coordinates?[1],
                        Longitude = props.Lon ?? (decimal?)f.Geometry?.Coordinates?[0]
                    };
                })
                .ToList();

            _cache.Set(cacheKey, suggestions, TimeSpan.FromMinutes(_settings.CacheMinutes));
            return suggestions;
        }

        public async Task<PlaceDetailResponse> GetPlaceDetailAsync(
            string placeId,
            string? sessionToken = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(placeId))
                throw new BadRequestException("placeId is required.");

            var trimmedPlaceId = placeId.Trim();
            if (trimmedPlaceId.Length > 200)
                throw new BadRequestException("placeId is invalid.");

            EnsureApiKeyConfigured();

            var cacheKey = $"geoapify:detail:{trimmedPlaceId}";
            if (_cache.TryGetValue(cacheKey, out PlaceDetailResponse? cached) && cached is not null)
                return cached;

            var baseUrl = _settings.BaseUrl.TrimEnd('/');
            var url = $"{baseUrl}/v2/place-details?id={Uri.EscapeDataString(trimmedPlaceId)}&lang=vi&apiKey={Uri.EscapeDataString(_settings.ApiKey)}";

            var response = await SendRequestAsync(url, cancellationToken);
            var payload = await response.Content.ReadFromJsonAsync<GeoapifyFeatureCollection>(cancellationToken: cancellationToken);

            var feature = payload?.Features?.FirstOrDefault();
            if (feature?.Properties is null)
                throw new NotFoundException("Place not found.");

            var props = feature.Properties;
            var lat = props.Lat ?? (decimal?)feature.Geometry?.Coordinates?[1];
            var lon = props.Lon ?? (decimal?)feature.Geometry?.Coordinates?[0];

            if (!lat.HasValue || !lon.HasValue)
                throw new NotFoundException("Coordinates not found for the selected place.");

            var result = new PlaceDetailResponse
            {
                PlaceId = props.PlaceId ?? trimmedPlaceId,
                Name = props.Name ?? props.AddressLine1 ?? props.Formatted,
                FormattedAddress = props.Formatted ?? props.AddressLine2,
                Latitude = lat.Value,
                Longitude = lon.Value,
                Commune = props.Suburb ?? props.Quarter,
                Province = props.State ?? props.City
            };

            _cache.Set(cacheKey, result, TimeSpan.FromMinutes(_settings.CacheMinutes));
            return result;
        }

        private async Task<HttpResponseMessage> SendRequestAsync(string url, CancellationToken cancellationToken)
        {
            HttpResponseMessage response;
            try
            {
                response = await _httpClient.GetAsync(url, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // Do not rethrow raw HttpRequestException to prevent apiKey query param leakage in logs
                throw new BadRequestException("Location service is currently unavailable. Please try again later.");
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new NotFoundException("Requested location was not found.");

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new BadRequestException("Location service rate limit exceeded. Please try again shortly.");

            if (!response.IsSuccessStatusCode)
                throw new BadRequestException("Location service returned an error. Please try again later.");

            return response;
        }

        private void EnsureApiKeyConfigured()
        {
            if (string.IsNullOrWhiteSpace(_settings.ApiKey))
                throw new InvalidOperationException("Geoapify API key is not configured.");
        }

        private static string? BuildSecondaryText(GeoapifyProperties props)
        {
            var parts = new[] { props.Suburb ?? props.Quarter, props.District, props.State ?? props.City, props.Country }
                .Where(p => !string.IsNullOrWhiteSpace(p));

            var text = string.Join(", ", parts);
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        private class GeoapifyFeatureCollection
        {
            [JsonPropertyName("features")]
            public List<GeoapifyFeature>? Features { get; set; }
        }

        private class GeoapifyFeature
        {
            [JsonPropertyName("properties")]
            public GeoapifyProperties? Properties { get; set; }

            [JsonPropertyName("geometry")]
            public GeoapifyGeometry? Geometry { get; set; }
        }

        private class GeoapifyProperties
        {
            [JsonPropertyName("place_id")]
            public string? PlaceId { get; set; }

            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("formatted")]
            public string? Formatted { get; set; }

            [JsonPropertyName("address_line1")]
            public string? AddressLine1 { get; set; }

            [JsonPropertyName("address_line2")]
            public string? AddressLine2 { get; set; }

            [JsonPropertyName("street")]
            public string? Street { get; set; }

            [JsonPropertyName("suburb")]
            public string? Suburb { get; set; }

            [JsonPropertyName("quarter")]
            public string? Quarter { get; set; }

            [JsonPropertyName("district")]
            public string? District { get; set; }

            [JsonPropertyName("city")]
            public string? City { get; set; }

            [JsonPropertyName("state")]
            public string? State { get; set; }

            [JsonPropertyName("country")]
            public string? Country { get; set; }

            [JsonPropertyName("lat")]
            public decimal? Lat { get; set; }

            [JsonPropertyName("lon")]
            public decimal? Lon { get; set; }
        }

        private class GeoapifyGeometry
        {
            [JsonPropertyName("coordinates")]
            public List<double>? Coordinates { get; set; }
        }
    }
}
