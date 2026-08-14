namespace StealDeal.Services.Notification.Infrastructure.Configuration
{
    public class BrevoSettings
    {
        public string BaseUrl { get; set; } = "https://api.brevo.com";
        public string ApiKey { get; set; } = null!;
        public string FromEmail { get; set; } = null!;
        public string FromName { get; set; } = "StealDeal";
    }
}