using System.Collections.Generic;

namespace StealDeal.Services.Store.Application.DTOs.Responses
{
    public class ChatResponse
    {
        public string SessionId { get; set; } = string.Empty;
        public string Reply { get; set; } = string.Empty;
        public List<SurpriseBagResponse> SuggestedBags { get; set; } = new();
    }
}
