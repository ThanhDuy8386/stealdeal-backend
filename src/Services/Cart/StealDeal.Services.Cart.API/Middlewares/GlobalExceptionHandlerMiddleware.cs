using Microsoft.AspNetCore.Mvc;
using StealDeal.Services.Cart.Application.Exceptions;
using System.Text.Json;

namespace StealDeal.Services.Cart.API.Middlewares
{
    public class GlobalExceptionHandlerMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<GlobalExceptionHandlerMiddleware> _logger;

        public GlobalExceptionHandlerMiddleware(
            RequestDelegate next,
            ILogger<GlobalExceptionHandlerMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(context, ex);
            }
        }

        private async Task HandleExceptionAsync(HttpContext context, Exception ex)
        {
            var (statusCode, detail) = ex switch
            {
                BadRequestException => (StatusCodes.Status400BadRequest, ex.Message),
                UnauthorizedException => (StatusCodes.Status401Unauthorized, ex.Message),
                NotFoundException => (StatusCodes.Status404NotFound, ex.Message),
                ConflictException => (StatusCodes.Status409Conflict, ex.Message),
                _ => (StatusCodes.Status500InternalServerError, "Internal server error.")
            };

            if (statusCode == StatusCodes.Status500InternalServerError)
            {
                _logger.LogError(ex, "Unhandled exception");
            }
            else
            {
                _logger.LogWarning("Business exception: {Message}", ex.Message);
            }

            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";

            var problemDetails = new ProblemDetails
            {
                Status = statusCode,
                Title = GetTitle(statusCode),
                Detail = detail,
                Instance = context.Request.Path
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(problemDetails));
        }

        private static string GetTitle(int statusCode) => statusCode switch
        {
            StatusCodes.Status400BadRequest => "Bad request.",
            StatusCodes.Status401Unauthorized => "Unauthorized.",
            StatusCodes.Status404NotFound => "Not found.",
            StatusCodes.Status409Conflict => "Conflict.",
            _ => "System error."
        };
    }
}
