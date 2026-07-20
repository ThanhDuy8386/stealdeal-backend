using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Identity.Application.DTOs.Responses
{
    public class RegistrationResponse
    {
        public string Message { get; set; } = null!;
        public bool RequiresEmailVerification { get; set; }

    }
}
