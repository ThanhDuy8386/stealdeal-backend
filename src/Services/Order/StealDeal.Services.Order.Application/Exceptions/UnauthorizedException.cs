using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Order.Application.Exceptions
{
    public class UnauthorizedException : Exception
    {
        public UnauthorizedException(string message) : base(message) { }
    }
}
