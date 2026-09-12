using StealDeal.Services.Payment.Application.DTOs.Gateways;

namespace StealDeal.Services.Payment.Application.Services.Interfaces
{
    public interface IPaymentCallbackService
    {
        Task<VnPayIpnHandleResult> HandleVnPayIpnAsync(
            IReadOnlyDictionary<string, string> parameters,
            CancellationToken cancellationToken = default);
    }
}
