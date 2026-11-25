using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Order.Application.Exceptions
{
    public class ConflictException : Exception
    {
        public string? ReasonCode { get; }

        public ConflictException(string message, string? reasonCode = null) : base(message)
        {
            ReasonCode = reasonCode;
        }
    }
}
