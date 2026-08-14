namespace StealDeal.Services.Payment.Application.Gateways
{
    public interface IPaymentGatewayFactory
    {
        IPaymentGateway GetGateway(string paymentMethod);
    }
}
