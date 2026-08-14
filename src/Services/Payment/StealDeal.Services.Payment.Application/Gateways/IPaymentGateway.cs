using StealDeal.Services.Payment.Application.DTOs.Gateways;

namespace StealDeal.Services.Payment.Application.Gateways
{
    public interface IPaymentGateway
    {
        string Method { get; }
        Task<CreatePaymentResult> CreatePaymentAsync(
            CreatePaymentRequest request,
            CancellationToken cancellationToken = default);

        Task<PaymentCallbackResult> VerifyIpnAsync(
            IReadOnlyDictionary<string, string> parameters,
            CancellationToken cancellationToken = default);
    }
}
