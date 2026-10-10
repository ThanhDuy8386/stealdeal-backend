namespace StealDeal.Services.Store.Infrastructure.Configuration
{
    public class GeoapifySettings
    {
        public string BaseUrl { get; set; } = "https://api.geoapify.com";
        public string ApiKey { get; set; } = string.Empty;
        public int TimeoutSeconds { get; set; } = 10;
        public int CacheMinutes { get; set; } = 5;
    }
}
