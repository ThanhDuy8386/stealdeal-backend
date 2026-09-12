using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Identity.Application.DTOs.Requests
{
    public class ForgotPasswordRequest
    {
        public string Email { get; set; } = null!;
    }
}
