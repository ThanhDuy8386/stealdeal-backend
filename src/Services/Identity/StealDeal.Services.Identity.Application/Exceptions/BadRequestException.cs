using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Identity.Application.Exceptions
{
    public class BadRequestException : Exception
    {
        public BadRequestException(string message) : base(message) { }
    }
}
