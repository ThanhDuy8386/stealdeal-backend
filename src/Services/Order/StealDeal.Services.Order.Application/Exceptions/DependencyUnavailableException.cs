namespace StealDeal.Services.Order.Application.Exceptions;

public sealed class DependencyUnavailableException : Exception
{
    public DependencyUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException) { }
}
