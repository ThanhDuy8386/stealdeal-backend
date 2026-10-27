namespace StealDeal.Services.Store.Infrastructure.Configuration
{
    public class GeminiSettings
    {
        public string ApiKey { get; set; } = string.Empty;
        public string Model { get; set; } = "gemini-2.5-flash";
        public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta/";
        public double Temperature { get; set; } = 0.3;
        public int MaxOutputTokens { get; set; } = 1024;
        public string SystemInstruction { get; set; } =
            "You are StealDeal Shopping Assistant, a helpful and friendly AI assistant for the StealDeal platform. " +
            "You help buyers discover surprise bags, understand store pickup windows, and answer questions about food categories. " +
            "Keep your answers concise, friendly, and focused on helping the user find deals. " +
            "Do not promise discounts or execute orders directly.";
    }
}
