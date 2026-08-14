using System;
using System.Net;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using StealDeal.Services.Notification.Application.Services.Interfaces;
using StealDeal.Services.Notification.Infrastructure.Configuration;

namespace StealDeal.Services.Notification.Infrastructure.EmailProvider
{
    public class BrevoEmailSender : IEmailSender
    {
        private readonly HttpClient _httpClient;
        private readonly BrevoSettings _settings;

        public BrevoEmailSender(HttpClient httpClient, IOptions<BrevoSettings> settings)
        {
            _httpClient = httpClient;
            _settings = settings.Value;
        }

        public async Task SendOtpAsync(
            string toEmail,
            string fullName,
            string otp,
            DateTime expiresAt,
            CancellationToken cancellationToken = default)
        {
            var safeFullName = WebUtility.HtmlEncode(fullName);
            var expiresAtText = expiresAt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'");

            var payload = new
            {
                sender = new
                {
                    name = _settings.FromName,
                    email = _settings.FromEmail
                },
                to = new[]
                {
                    new
                    {
                        email = toEmail,
                        name = fullName
                    }
                },
                subject = "Your StealDeal verification code",
                htmlContent = $"""
                    <html>
                      <body>
                        <p>Hello {safeFullName},</p>
                        <p>Your verification code is:</p>
                        <h2>{otp}</h2>
                        <p>This code expires at {expiresAtText}.</p>
                      </body>
                    </html>
                    """,
                textContent = $"Hello {fullName}, your verification code is {otp}. It expires at {expiresAtText}.",
                tags = new[] { "email-verification-otp" }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "/v3/smtp/email");
            request.Headers.Add("api-key", _settings.ApiKey);
            request.Content = JsonContent.Create(payload);

            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException($"Brevo email sending failed: {(int)response.StatusCode} {errorBody}");
            }
        }
    }
}