using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Store.Application.DTOs.Events
{
    public class StoreVerifiedEvent
    {
        public Guid StoreId { get; set; }
        public Guid OwnerId { get; set; }
    }
}
