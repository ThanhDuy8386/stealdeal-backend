namespace StealDeal.Services.Order.Application.Exceptions
{
    public class ForbiddenException : Exception
    {
        public string? ReasonCode { get; }

        public ForbiddenException(string message, string? reasonCode = null) : base(message)
        {
            ReasonCode = reasonCode;
        }
    }
}
