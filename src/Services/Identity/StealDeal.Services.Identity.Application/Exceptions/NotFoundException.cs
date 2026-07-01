using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Identity.Application.Exceptions
{
    public class NotFoundException : Exception
    {
        public NotFoundException(string message) : base(message) { }
    }
}
