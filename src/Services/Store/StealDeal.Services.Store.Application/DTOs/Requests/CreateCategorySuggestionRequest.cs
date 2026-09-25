using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Store.Application.DTOs.Requests
{
    public class CreateCategorySuggestionRequest
    {
        public string SuggestedName { get; set; } = null!;
    }
}
