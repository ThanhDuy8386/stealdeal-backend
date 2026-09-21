using System.Net;
using System.Text.Json;
using StealDeal.Services.Order.Application.Exceptions;

namespace StealDeal.Services.Order.Infrastructure.Clients
{
    internal static class HttpClientErrorMapper
    {
        public static async Task EnsureSuccessOrThrowAsync(
            HttpResponseMessage response,
            string fallbackMessage,
            CancellationToken cancellationToken = default)
        {
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            var detail = await ReadProblemDetailAsync(response, cancellationToken)
                ?? fallbackMessage;

            throw response.StatusCode switch
            {
                HttpStatusCode.BadRequest => new BadRequestException(detail),
                HttpStatusCode.Unauthorized => new UnauthorizedException(detail),
                HttpStatusCode.Forbidden => new ForbiddenException(detail),
                HttpStatusCode.NotFound => new NotFoundException(detail),
                HttpStatusCode.Conflict => new ConflictException(detail),
                _ => new InvalidOperationException(detail)
            };
        }

        private static async Task<string?> ReadProblemDetailAsync(
            HttpResponseMessage response,
            CancellationToken cancellationToken)
        {
            if (response.Content is null)
            {
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(content))
            {
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(content);
                if (document.RootElement.TryGetProperty("detail", out var detail) &&
                    detail.ValueKind == JsonValueKind.String)
                {
                    return detail.GetString();
                }

                if (document.RootElement.TryGetProperty("message", out var message) &&
                    message.ValueKind == JsonValueKind.String)
                {
                    return message.GetString();
                }
            }
            catch (JsonException)
            {
                return content;
            }

            return content;
        }
    }
}
