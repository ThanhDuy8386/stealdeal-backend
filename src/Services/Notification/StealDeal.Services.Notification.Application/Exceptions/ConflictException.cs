using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Notification.Application.Exceptions
{
    public class ConflictException : Exception
    {
        public ConflictException(string message) : base(message) { }
    }
}
