using StealDeal.Services.Payment.Application.Gateways;

namespace StealDeal.Services.Payment.Infrastructure.Gateways
{
    public class PaymentGatewayFactory : IPaymentGatewayFactory
    {
        private readonly IReadOnlyDictionary<string, IPaymentGateway> _gateways;

        public PaymentGatewayFactory(IEnumerable<IPaymentGateway> gateways)
        {
            _gateways = gateways.ToDictionary(
                gateway => gateway.Method,
                gateway => gateway,
                StringComparer.OrdinalIgnoreCase);
        }

        public IPaymentGateway GetGateway(string paymentMethod)
        {
            if (_gateways.TryGetValue(paymentMethod, out var gateway))
            {
                return gateway;
            }

            throw new NotSupportedException($"Payment method '{paymentMethod}' is not supported.");
        }
    }
}
