using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Identity.Application.DTOs.Responses
{
    // generic page result class to be used for paginated responses
    public class PagedResult<T>
    {
        public List<T> Items { get; set; } = [];
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);

    }
}
