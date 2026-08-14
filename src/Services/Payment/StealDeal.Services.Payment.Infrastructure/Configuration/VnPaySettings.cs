namespace StealDeal.Services.Payment.Infrastructure.Configuration
{
    public class VnPaySettings
    {
        public string TmnCode { get; set; } = null!;
        public string HashSecret { get; set; } = null!;
        public string PaymentUrl { get; set; } = null!;
        public string ReturnUrl { get; set; } = null!;
        public string IpNUrl { get; set; } = null!;
        public string Version { get; set; } = "2.1.0";
        public string Command { get; set; } = "pay";
        public string CurrCode { get; set; } = "VND";
        public string Locale { get; set; } = "vn";
        public int ExpireMinutes { get; set; } = 15;
    }
}
