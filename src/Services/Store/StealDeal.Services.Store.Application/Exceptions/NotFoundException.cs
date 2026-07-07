using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Store.Application.Exceptions
{
    public class NotFoundException : Exception
    {
        public NotFoundException(string message) : base(message) { }
    }
}
