using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace OISM.WebApi.Middlewares;

/// <summary>
/// Middleware toàn cục xử lý các ngoại lệ chưa được bắt (Unhandled Exceptions).
/// Tuân thủ nghiêm ngặt chuẩn RFC 7807 Problem Details cho hệ thống RESTful API OISMS.
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
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
            _logger.LogError(ex, "Ngoại lệ chưa xử lý phát sinh tại endpoint: {Path}", context.Request.Path);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/problem+json";

        var statusCode = StatusCodes.Status500InternalServerError;
        var title = "Lỗi máy chủ nội bộ";
        var errorCode = "INTERNAL_SERVER_ERROR";
        var detail = "Đã xảy ra sự cố ngoài dự kiến trong quá trình xử lý yêu cầu. Vui lòng liên hệ quản trị viên.";

        // Xử lý các loại ngoại lệ đặc thù
        if (exception is KeyNotFoundException)
        {
            statusCode = StatusCodes.Status404NotFound;
            title = "Tài nguyên không tìm thấy";
            errorCode = "NOT_FOUND";
            detail = exception.Message;
        }
        else if (exception is UnauthorizedAccessException)
        {
            statusCode = StatusCodes.Status401Unauthorized;
            title = "Yêu cầu quyền truy cập";
            errorCode = "UNAUTHORIZED";
            detail = exception.Message;
        }
        else if (exception is InvalidOperationException || exception is ArgumentException)
        {
            statusCode = StatusCodes.Status400BadRequest;
            title = "Yêu cầu không hợp lệ";
            errorCode = "BAD_REQUEST";
            detail = exception.Message;
        }

        context.Response.StatusCode = statusCode;

        var problemDetails = new ProblemDetails
        {
            Type = $"https://oisms.local/errors/{errorCode.ToLowerInvariant()}",
            Title = title,
            Status = statusCode,
            Detail = detail,
            Instance = context.Request.Path,
            Extensions =
            {
                ["errorCode"] = errorCode,
                ["timestamp"] = DateTimeOffset.UtcNow
            }
        };

        var json = JsonSerializer.Serialize(problemDetails, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        return context.Response.WriteAsync(json);
    }
}
