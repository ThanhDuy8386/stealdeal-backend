namespace StealDeal.Services.Store.Infrastructure.Configuration
{
    public class GoongSettings
    {
        public string BaseUrl { get; set; } = "https://rsapi.goong.io";
        public string ApiKey { get; set; } = string.Empty;
        public int TimeoutSeconds { get; set; } = 10;
        public int CacheMinutes { get; set; } = 5;
    }
}
