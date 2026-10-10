namespace StealDeal.Services.Store.Application.DTOs.Responses
{
    public class PlaceDetailResponse
    {
        public string PlaceId { get; set; } = null!;
        public string? Name { get; set; }
        public string? FormattedAddress { get; set; }
        public decimal Latitude { get; set; }
        public decimal Longitude { get; set; }
        public string? Commune { get; set; }
        public string? Province { get; set; }
    }
}
