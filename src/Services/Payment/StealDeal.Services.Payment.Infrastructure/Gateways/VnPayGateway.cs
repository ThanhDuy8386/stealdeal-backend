using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using StealDeal.Services.Payment.Application.DTOs.Gateways;
using StealDeal.Services.Payment.Application.Gateways;
using StealDeal.Services.Payment.Domain.Constants;
using StealDeal.Services.Payment.Infrastructure.Configuration;

namespace StealDeal.Services.Payment.Infrastructure.Gateways
{
    public class VnPayGateway : IPaymentGateway
    {
        private const string SuccessCode = "00";
        private const string DateFormat = "yyyyMMddHHmmss";
        private static readonly TimeZoneInfo VietnamTimeZone = CreateVietnamTimeZone();

        private readonly VnPaySettings _settings;

        public VnPayGateway(IOptions<VnPaySettings> settings)
        {
            _settings = settings.Value;
        }

        public string Method => PaymentMethods.VnPay;

        public Task<CreatePaymentResult> CreatePaymentAsync(
            CreatePaymentRequest request,
            CancellationToken cancellationToken = default)
        {
            ValidateCreatePaymentRequest(request);
            ValidateSettings();

            var createdAtUtc = NormalizeUtc(request.CreatedAtUtc);
            var createdAtVietnamTime = TimeZoneInfo.ConvertTimeFromUtc(createdAtUtc, VietnamTimeZone);
            var expiresAtUtc = createdAtUtc.AddMinutes(_settings.ExpireMinutes);
            var expiresAtVietnamTime = TimeZoneInfo.ConvertTimeFromUtc(expiresAtUtc, VietnamTimeZone);
            var gatewayRef = request.TransactionId.ToString("N");

            var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["vnp_Version"] = _settings.Version,
                ["vnp_Command"] = _settings.Command,
                ["vnp_TmnCode"] = _settings.TmnCode,
                ["vnp_Amount"] = ToVnPayAmount(request.Amount),
                ["vnp_CreateDate"] = createdAtVietnamTime.ToString(DateFormat, CultureInfo.InvariantCulture),
                ["vnp_CurrCode"] = _settings.CurrCode,
                ["vnp_IpAddr"] = string.IsNullOrWhiteSpace(request.ClientIpAddress)
                    ? "127.0.0.1"
                    : request.ClientIpAddress.Trim(),
                ["vnp_Locale"] = _settings.Locale,
                ["vnp_OrderInfo"] = string.IsNullOrWhiteSpace(request.OrderInfo)
                    ? $"Thanh toan don hang {request.OrderId}"
                    : request.OrderInfo.Trim(),
                ["vnp_OrderType"] = request.OrderType,
                ["vnp_ReturnUrl"] = _settings.ReturnUrl,
                ["vnp_TxnRef"] = gatewayRef,
                ["vnp_ExpireDate"] = expiresAtVietnamTime.ToString(DateFormat, CultureInfo.InvariantCulture)
            };

            if (!string.IsNullOrWhiteSpace(request.BankCode))
            {
                parameters["vnp_BankCode"] = request.BankCode.Trim();
            }

            var queryString = BuildQueryString(parameters);
            var secureHash = ComputeHmacSha512(_settings.HashSecret, queryString);
            var separator = _settings.PaymentUrl.Contains('?') ? "&" : "?";

            return Task.FromResult(new CreatePaymentResult
            {
                PaymentMethod = Method,
                GatewayRef = gatewayRef,
                CheckoutUrl = $"{_settings.PaymentUrl}{separator}{queryString}&vnp_SecureHash={secureHash}",
                ExpiresAtUtc = expiresAtUtc
            });
        }

        public Task<PaymentCallbackResult> VerifyIpnAsync(
            IReadOnlyDictionary<string, string> parameters,
            CancellationToken cancellationToken = default)
        {
            ValidateSettings();

            var secureHash = GetValue(parameters, "vnp_SecureHash");
            var signedData = BuildQueryString(parameters, excludeSecureHash: true);
            var expectedHash = ComputeHmacSha512(_settings.HashSecret, signedData);
            var isValidSignature = IsSameHash(secureHash, expectedHash);
            var responseCode = GetValue(parameters, "vnp_ResponseCode");
            var transactionStatus = GetValue(parameters, "vnp_TransactionStatus");

            return Task.FromResult(new PaymentCallbackResult
            {
                IsValidSignature = isValidSignature,
                IsSuccess = isValidSignature &&
                    responseCode == SuccessCode &&
                    transactionStatus == SuccessCode,
                PaymentMethod = Method,
                GatewayRef = GetValue(parameters, "vnp_TxnRef"),
                Amount = ParseVnPayAmount(GetValue(parameters, "vnp_Amount")),
                GatewayTransactionNo = GetValue(parameters, "vnp_TransactionNo"),
                GatewayResponseCode = responseCode,
                GatewayTransactionStatus = transactionStatus,
                PaidAtUtc = ParseVnPayDate(GetValue(parameters, "vnp_PayDate")),
                ReasonCode = isValidSignature ? responseCode : "InvalidSignature",
                Reason = isValidSignature
                    ? $"VNPAY response code: {responseCode}, transaction status: {transactionStatus}."
                    : "VNPAY secure hash is invalid."
            });
        }

        private static void ValidateCreatePaymentRequest(CreatePaymentRequest request)
        {
            if (request.TransactionId == Guid.Empty)
            {
                throw new InvalidOperationException("Transaction id is required.");
            }

            if (request.OrderId == Guid.Empty)
            {
                throw new InvalidOperationException("Order id is required.");
            }

            if (request.UserId == Guid.Empty)
            {
                throw new InvalidOperationException("User id is required.");
            }

            if (request.Amount <= 0)
            {
                throw new InvalidOperationException("Payment amount must be greater than zero.");
            }
        }

        private void ValidateSettings()
        {
            if (string.IsNullOrWhiteSpace(_settings.TmnCode))
            {
                throw new InvalidOperationException("VNPAY TmnCode is required.");
            }

            if (string.IsNullOrWhiteSpace(_settings.HashSecret))
            {
                throw new InvalidOperationException("VNPAY HashSecret is required.");
            }

            if (string.IsNullOrWhiteSpace(_settings.PaymentUrl))
            {
                throw new InvalidOperationException("VNPAY PaymentUrl is required.");
            }

            if (string.IsNullOrWhiteSpace(_settings.ReturnUrl))
            {
                throw new InvalidOperationException("VNPAY ReturnUrl is required.");
            }

            if (_settings.ExpireMinutes <= 0)
            {
                throw new InvalidOperationException("VNPAY ExpireMinutes must be greater than zero.");
            }
        }

        private static string BuildQueryString(
            IReadOnlyDictionary<string, string> parameters,
            bool excludeSecureHash = false)
        {
            var filteredParameters = parameters
                .Where(parameter =>
                    !string.IsNullOrWhiteSpace(parameter.Value) &&
                    (!excludeSecureHash || !IsSecureHashParameter(parameter.Key)))
                .OrderBy(parameter => parameter.Key, StringComparer.Ordinal);

            return string.Join(
                "&",
                filteredParameters.Select(parameter =>
                    $"{WebUtility.UrlEncode(parameter.Key)}={WebUtility.UrlEncode(parameter.Value)}"));
        }

        private static bool IsSecureHashParameter(string key)
        {
            return key.Equals("vnp_SecureHash", StringComparison.OrdinalIgnoreCase) ||
                   key.Equals("vnp_SecureHashType", StringComparison.OrdinalIgnoreCase);
        }

        private static string ComputeHmacSha512(string key, string data)
        {
            var keyBytes = Encoding.UTF8.GetBytes(key);
            var dataBytes = Encoding.UTF8.GetBytes(data);

            using var hmac = new HMACSHA512(keyBytes);
            var hashBytes = hmac.ComputeHash(dataBytes);

            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        private static bool IsSameHash(string? actualHash, string expectedHash)
        {
            if (string.IsNullOrWhiteSpace(actualHash))
            {
                return false;
            }

            var actualBytes = Encoding.UTF8.GetBytes(actualHash.Trim().ToLowerInvariant());
            var expectedBytes = Encoding.UTF8.GetBytes(expectedHash.ToLowerInvariant());

            return actualBytes.Length == expectedBytes.Length &&
                   CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
        }

        private static string ToVnPayAmount(decimal amount)
        {
            var vnpayAmount = decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);
            return vnpayAmount.ToString("0", CultureInfo.InvariantCulture);
        }

        private static decimal? ParseVnPayAmount(string? amount)
        {
            if (!long.TryParse(amount, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedAmount))
            {
                return null;
            }

            return parsedAmount / 100m;
        }

        private static DateTime? ParseVnPayDate(string? value)
        {
            if (!DateTime.TryParseExact(
                    value,
                    DateFormat,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var vietnamTime))
            {
                return null;
            }

            return TimeZoneInfo.ConvertTimeToUtc(vietnamTime, VietnamTimeZone);
        }

        private static DateTime NormalizeUtc(DateTime dateTime)
        {
            return dateTime.Kind switch
            {
                DateTimeKind.Utc => dateTime,
                DateTimeKind.Local => dateTime.ToUniversalTime(),
                _ => DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
            };
        }

        private static string? GetValue(IReadOnlyDictionary<string, string> parameters, string key)
        {
            return parameters.TryGetValue(key, out var value)
                ? value
                : null;
        }

        private static TimeZoneInfo CreateVietnamTimeZone()
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            }
            catch (TimeZoneNotFoundException)
            {
                return FindIanaOrCreateCustomTimeZone();
            }
            catch (InvalidTimeZoneException)
            {
                return FindIanaOrCreateCustomTimeZone();
            }
        }

        private static TimeZoneInfo FindIanaOrCreateCustomTimeZone()
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
            }
            catch (TimeZoneNotFoundException)
            {
                return TimeZoneInfo.CreateCustomTimeZone(
                    "ICT",
                    TimeSpan.FromHours(7),
                    "Indochina Time",
                    "Indochina Time");
            }
            catch (InvalidTimeZoneException)
            {
                return TimeZoneInfo.CreateCustomTimeZone(
                    "ICT",
                    TimeSpan.FromHours(7),
                    "Indochina Time",
                    "Indochina Time");
            }
        }
    }
}
