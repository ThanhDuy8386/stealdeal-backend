namespace StealDeal.Services.Identity.Infrastructure.Configuration
{
    public class RabbitMqSettings
    {
        public string HostName { get; set; } = null!;
        public int Port { get; set; }
        public string UserName { get; set; } = null!;
        public string Password { get; set; } = null!;
    }
}