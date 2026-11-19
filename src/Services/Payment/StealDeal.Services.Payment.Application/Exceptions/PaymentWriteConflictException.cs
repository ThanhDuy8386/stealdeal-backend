namespace StealDeal.Services.Payment.Application.Exceptions;

public sealed class PaymentWriteConflictException : Exception
{
    public PaymentWriteConflictException(Exception innerException)
        : base("The payment changed while it was being processed.", innerException)
    {
    }
}
