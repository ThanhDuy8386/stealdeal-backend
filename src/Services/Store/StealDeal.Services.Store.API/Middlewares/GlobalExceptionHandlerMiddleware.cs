using Microsoft.AspNetCore.Mvc;
using StealDeal.Services.Identity.Application.Exceptions;
using System;
using System.Text.Json;
namespace StealDeal.Services.Identity.API.Middlewares
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
                await _next(context);  // Gọi tiếp pipeline
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Đã xảy ra lỗi hệ thống: {Message}", ex.Message);
                await HandleExceptionAsync(context, ex);
            }
        }

        private async Task HandleExceptionAsync(HttpContext context, Exception ex)
        {
            // Map exception type → HTTP status code
            var (statusCode, message) = ex switch
            {
                BadRequestException => (400, ex.Message),
                UnauthorizedException => (401, ex.Message),
                NotFoundException => (404, ex.Message),
                ConflictException => (409, ex.Message),
                _ => (500, "An unexpected error occurred.")
            };
            // Log: chỉ log chi tiết cho 500 (unexpected), còn lại log warning
            if (statusCode == 500)
                _logger.LogError(ex, "Unhandled exception");
            else
                _logger.LogWarning("Business exception: {Message}", ex.Message);
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";

            var problemDetails = new ProblemDetails
            {
                Status = statusCode,
                Title = GetTitle(statusCode),
                Detail = statusCode == 500 ? "Lỗi máy chủ nội bộ. Vui lòng thử lại sau." : ex.Message,
                Instance = context.Request.Path
            };
            var jsonResponse = JsonSerializer.Serialize(problemDetails);
            await context.Response.WriteAsync(jsonResponse);
        }

        private static string GetTitle(int statusCode) => statusCode switch
        {
            StatusCodes.Status400BadRequest => "Lỗi dữ liệu đầu vào.",
            StatusCodes.Status401Unauthorized => "Không có quyền truy cập.",
            StatusCodes.Status404NotFound => "Không tìm thấy dữ liệu.",
            _ => "Lỗi hệ thống."
        };
    }
}
