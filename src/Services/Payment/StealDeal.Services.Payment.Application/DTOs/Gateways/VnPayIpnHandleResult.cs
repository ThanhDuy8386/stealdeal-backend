namespace StealDeal.Services.Payment.Application.DTOs.Gateways
{
    public class VnPayIpnHandleResult
    {
        public VnPayIpnHandleResult(string rspCode, string message)
        {
            RspCode = rspCode;
            Message = message;
        }

        public string RspCode { get; }
        public string Message { get; }
    }
}
