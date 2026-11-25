using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using StealDeal.Services.Order.Application.Exceptions;
using StealDeal.Services.Order.Application.Constants;

namespace StealDeal.Services.Order.API.Middlewares
{
    public class GlobalExceptionHandlerMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<GlobalExceptionHandlerMiddleware> _logger;

        public GlobalExceptionHandlerMiddleware(RequestDelegate next, ILogger<GlobalExceptionHandlerMiddleware> logger)
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
            var (statusCode, title) = ex switch
            {
                PurchaseRestrictionException { IsSuspended: true } => (403, "Forbidden"),
                PurchaseRestrictionException => (409, "Conflict"),
                BadRequestException => (400, "Bad Request"),
                UnauthorizedException => (401, "Unauthorized"),
                ForbiddenException => (403, "Forbidden"),
                NotFoundException => (404, "Not Found"),
                ConflictException => (409, "Conflict"),
                DependencyUnavailableException => (503, "Service Unavailable"),
                _ => (500, "Internal Server Error")
            };

            if (statusCode == 500)
                _logger.LogError(ex, "Unhandled exception: {Message}", ex.Message);
            else
                _logger.LogWarning("Business exception [{Status}]: {Message}", statusCode, ex.Message);

            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";

            var problemDetails = new ProblemDetails
            {
                Status = statusCode,
                Title = title,
                Detail = statusCode == 500 ? "An unexpected error occurred. Please try again later." : ex.Message,
                Instance = context.Request.Path
            };

            if (ex is ForbiddenException { ReasonCode: not null } forbidden)
                problemDetails.Extensions["reasonCode"] = forbidden.ReasonCode;
            if (ex is ConflictException { ReasonCode: not null } conflict)
                problemDetails.Extensions["reasonCode"] = conflict.ReasonCode;
            if (ex is DependencyUnavailableException)
                problemDetails.Extensions["reasonCode"] = OrderReasonCodes.DependencyUnavailable;
            if (ex is PurchaseRestrictionException restriction)
            {
                problemDetails.Extensions["reasonCode"] = restriction.ReasonCode;
                problemDetails.Extensions["score"] = restriction.Score;
                problemDetails.Extensions["restrictedUntilUtc"] = restriction.RestrictedUntilUtc;
                problemDetails.Extensions["maxPendingOrders"] = restriction.MaxPendingOrders;
            }

            await context.Response.WriteAsJsonAsync(problemDetails);
        }
    }
}
