namespace StealDeal.Services.Store.Application.DTOs.Responses
{
    public class AutocompleteSuggestionResponse
    {
        public string PlaceId { get; set; } = null!;
        public string Description { get; set; } = null!;
        public string? MainText { get; set; }
        public string? SecondaryText { get; set; }
        public string? Commune { get; set; }
        public string? Province { get; set; }
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
    }
}
