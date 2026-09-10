using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StealDeal.Services.Payment.Application.Gateways;
using StealDeal.Services.Payment.Application.Services.Interfaces;
using StealDeal.Services.Payment.Domain.Constants;

namespace StealDeal.Services.Payment.API.Controllers
{
    [ApiController]
    [AllowAnonymous]
    [Route("api/vnpay")]
    public class VnPayController : ControllerBase
    {
        private readonly IPaymentGatewayFactory _paymentGatewayFactory;
        private readonly IPaymentCallbackService _paymentCallbackService;
        private readonly ILogger<VnPayController> _logger;

        public VnPayController(
            IPaymentGatewayFactory paymentGatewayFactory,
            IPaymentCallbackService paymentCallbackService,
            ILogger<VnPayController> logger)
        {
            _paymentGatewayFactory = paymentGatewayFactory;
            _paymentCallbackService = paymentCallbackService;
            _logger = logger;
        }

        [HttpGet("ipn")]
        public async Task<IActionResult> Ipn(CancellationToken cancellationToken)
        {
            var parameters = GetQueryParameters();
            var result = await _paymentCallbackService.HandleVnPayIpnAsync(parameters, cancellationToken);

            _logger.LogInformation(
                "Handled VNPAY IPN with RspCode: {RspCode}, Message: {Message}",
                result.RspCode,
                result.Message);

            return Ok(new VnPayIpnResponse(result.RspCode, result.Message));
        }

        [HttpGet("return")]
        public async Task<IActionResult> Return(CancellationToken cancellationToken)
        {
            var parameters = GetQueryParameters();
            var gateway = _paymentGatewayFactory.GetGateway(PaymentMethods.VnPay);
            var result = await gateway.VerifyIpnAsync(parameters, cancellationToken);

            return Ok(new
            {
                result.IsValidSignature,
                result.IsSuccess,
                result.GatewayRef,
                result.Amount,
                result.GatewayTransactionNo,
                result.GatewayResponseCode,
                result.GatewayTransactionStatus,
                result.PaidAtUtc,
                result.Reason
            });
        }

        private Dictionary<string, string> GetQueryParameters()
        {
            return Request.Query.ToDictionary(
                parameter => parameter.Key,
                parameter => parameter.Value.ToString(),
                StringComparer.OrdinalIgnoreCase);
        }

        private sealed class VnPayIpnResponse
        {
            public VnPayIpnResponse(string rspCode, string message)
            {
                RspCode = rspCode;
                Message = message;
            }

            [JsonPropertyName("RspCode")]
            public string RspCode { get; }

            [JsonPropertyName("Message")]
            public string Message { get; }
        }
    }
}
