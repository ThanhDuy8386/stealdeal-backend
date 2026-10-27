namespace StealDeal.Services.Store.Application.DTOs.Requests
{
    public class ChatRequest
    {
        public string? SessionId { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
