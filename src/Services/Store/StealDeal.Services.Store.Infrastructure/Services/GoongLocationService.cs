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
    public class GoongLocationService : IGoongLocationService
    {
        private readonly HttpClient _httpClient;
        private readonly GoongSettings _settings;
        private readonly IMemoryCache _cache;

        public GoongLocationService(
            HttpClient httpClient,
            IOptions<GoongSettings> settings,
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

            ValidateSessionToken(sessionToken);
            EnsureApiKeyConfigured();

            var normalizedInput = trimmedInput.ToLowerInvariant();
            var locationParam = (latitude.HasValue && longitude.HasValue)
                ? $"{latitude.Value.ToString(CultureInfo.InvariantCulture)},{longitude.Value.ToString(CultureInfo.InvariantCulture)}"
                : null;

            // Check in-memory cache first to save Goong quota
            var cacheKey = $"goong:autocomplete:{normalizedInput}:{locationParam}";
            if (_cache.TryGetValue(cacheKey, out List<AutocompleteSuggestionResponse>? cached) && cached is not null)
                return cached;

            var baseUrl = _settings.BaseUrl.TrimEnd('/');
            var url = $"{baseUrl}/v2/place/autocomplete?api_key={Uri.EscapeDataString(_settings.ApiKey)}&input={Uri.EscapeDataString(trimmedInput)}";

            if (!string.IsNullOrEmpty(locationParam))
                url += $"&location={Uri.EscapeDataString(locationParam)}";

            if (!string.IsNullOrWhiteSpace(sessionToken))
                url += $"&sessiontoken={Uri.EscapeDataString(sessionToken.Trim())}";

            var response = await SendGoongRequestAsync(url, cancellationToken);
            var payload = await response.Content.ReadFromJsonAsync<GoongAutocompletePayload>(cancellationToken: cancellationToken);

            if (payload is null || string.Equals(payload.Status, "ZERO_RESULTS", StringComparison.OrdinalIgnoreCase))
                return [];

            EnsureGoongStatusOk(payload.Status);

            var suggestions = (payload.Predictions ?? [])
                .Where(p => !string.IsNullOrWhiteSpace(p.PlaceId))
                .Select(p => new AutocompleteSuggestionResponse
                {
                    PlaceId = p.PlaceId!,
                    Description = p.Description ?? string.Empty,
                    MainText = p.StructuredFormatting?.MainText,
                    SecondaryText = p.StructuredFormatting?.SecondaryText,
                    Commune = p.Compound?.Commune,
                    Province = p.Compound?.Province
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

            ValidateSessionToken(sessionToken);
            EnsureApiKeyConfigured();

            var cacheKey = $"goong:detail:{trimmedPlaceId}";
            if (_cache.TryGetValue(cacheKey, out PlaceDetailResponse? cached) && cached is not null)
                return cached;

            var baseUrl = _settings.BaseUrl.TrimEnd('/');
            var url = $"{baseUrl}/v2/place/detail?api_key={Uri.EscapeDataString(_settings.ApiKey)}&place_id={Uri.EscapeDataString(trimmedPlaceId)}";

            if (!string.IsNullOrWhiteSpace(sessionToken))
                url += $"&sessiontoken={Uri.EscapeDataString(sessionToken.Trim())}";

            var response = await SendGoongRequestAsync(url, cancellationToken);
            var payload = await response.Content.ReadFromJsonAsync<GoongPlaceDetailPayload>(cancellationToken: cancellationToken);

            if (payload is null || string.Equals(payload.Status, "NOT_FOUND", StringComparison.OrdinalIgnoreCase) || payload.Result is null)
                throw new NotFoundException("Place not found.");

            EnsureGoongStatusOk(payload.Status);

            var location = payload.Result.Geometry?.Location;
            if (location is null)
                throw new NotFoundException("Coordinates not found for the selected place.");

            var result = new PlaceDetailResponse
            {
                PlaceId = payload.Result.PlaceId ?? trimmedPlaceId,
                Name = payload.Result.Name,
                FormattedAddress = payload.Result.FormattedAddress,
                Latitude = location.Lat,
                Longitude = location.Lng,
                Commune = payload.Result.Compound?.Commune,
                Province = payload.Result.Compound?.Province
            };

            _cache.Set(cacheKey, result, TimeSpan.FromMinutes(_settings.CacheMinutes));
            return result;
        }

        private async Task<HttpResponseMessage> SendGoongRequestAsync(string url, CancellationToken cancellationToken)
        {
            HttpResponseMessage response;
            try
            {
                response = await _httpClient.GetAsync(url, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // Do not rethrow raw HttpRequestException so the request URL containing api_key is never logged
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
                throw new InvalidOperationException("Goong API key is not configured.");
        }

        private static void EnsureGoongStatusOk(string? status)
        {
            if (string.IsNullOrWhiteSpace(status) || string.Equals(status, "OK", StringComparison.OrdinalIgnoreCase))
                return;

            if (string.Equals(status, "OVER_QUERY_LIMIT", StringComparison.OrdinalIgnoreCase))
                throw new BadRequestException("Location service quota exceeded. Please try again later.");

            throw new BadRequestException("Unable to process location request.");
        }

        private static void ValidateSessionToken(string? sessionToken)
        {
            if (string.IsNullOrWhiteSpace(sessionToken))
                return;

            var trimmed = sessionToken.Trim();
            if (trimmed.Length > 100 || !trimmed.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'))
                throw new BadRequestException("Invalid sessionToken format.");
        }

        // Internal DTOs matching Goong V2 JSON structure
        private class GoongAutocompletePayload
        {
            [JsonPropertyName("predictions")]
            public List<GoongPrediction>? Predictions { get; set; }

            [JsonPropertyName("status")]
            public string? Status { get; set; }
        }

        private class GoongPrediction
        {
            [JsonPropertyName("place_id")]
            public string? PlaceId { get; set; }

            [JsonPropertyName("description")]
            public string? Description { get; set; }

            [JsonPropertyName("structured_formatting")]
            public GoongStructuredFormatting? StructuredFormatting { get; set; }

            [JsonPropertyName("compound")]
            public GoongCompound? Compound { get; set; }
        }

        private class GoongStructuredFormatting
        {
            [JsonPropertyName("main_text")]
            public string? MainText { get; set; }

            [JsonPropertyName("secondary_text")]
            public string? SecondaryText { get; set; }
        }

        private class GoongPlaceDetailPayload
        {
            [JsonPropertyName("result")]
            public GoongPlaceResult? Result { get; set; }

            [JsonPropertyName("status")]
            public string? Status { get; set; }
        }

        private class GoongPlaceResult
        {
            [JsonPropertyName("place_id")]
            public string? PlaceId { get; set; }

            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("formatted_address")]
            public string? FormattedAddress { get; set; }

            [JsonPropertyName("geometry")]
            public GoongGeometry? Geometry { get; set; }

            [JsonPropertyName("compound")]
            public GoongCompound? Compound { get; set; }
        }

        private class GoongGeometry
        {
            [JsonPropertyName("location")]
            public GoongLatLng? Location { get; set; }
        }

        private class GoongLatLng
        {
            [JsonPropertyName("lat")]
            public decimal Lat { get; set; }

            [JsonPropertyName("lng")]
            public decimal Lng { get; set; }
        }

        private class GoongCompound
        {
            [JsonPropertyName("commune")]
            public string? Commune { get; set; }

            [JsonPropertyName("province")]
            public string? Province { get; set; }
        }
    }
}
