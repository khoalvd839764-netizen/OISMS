using System.Text.Json;
using OISM.Application.Common.Exceptions;
using OISM.Domain.Common;
using OISM.WebApi.Http;

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
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client đã ngắt kết nối, không còn ai nhận phản hồi.
        }
        catch (Exception ex) when (ex is OismException or InvalidStateTransitionException)
        {
            await HandleExceptionAsync(context, ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ngoại lệ chưa xử lý phát sinh tại endpoint: {Path}", context.Request.Path);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        if (context.Response.HasStarted)
        {
            return Task.FromException(exception);
        }

        context.Response.ContentType = "application/problem+json";

        var statusCode = StatusCodes.Status500InternalServerError;
        var title = "Lỗi máy chủ nội bộ";
        var errorCode = "INTERNAL_SERVER_ERROR";
        var detail = "Đã xảy ra sự cố ngoài dự kiến trong quá trình xử lý yêu cầu. Vui lòng liên hệ quản trị viên.";

        if (exception is OismException oismException)
        {
            statusCode = oismException.StatusCode;
            title = GetTitleForStatus(statusCode);
            errorCode = oismException.ErrorCode;
            detail = oismException.Message;
        }
        else if (exception is InvalidStateTransitionException)
        {
            statusCode = StatusCodes.Status409Conflict;
            title = GetTitleForStatus(statusCode);
            errorCode = "INVALID_STATE_TRANSITION";
            detail = exception.Message;
        }
        else if (exception is UnauthorizedAccessException)
        {
            statusCode = StatusCodes.Status401Unauthorized;
            title = GetTitleForStatus(statusCode);
            errorCode = "UNAUTHORIZED";
            detail = exception.Message;
        }

        context.Response.StatusCode = statusCode;

        var problemDetails = ApiErrorResponse.CreateProblemDetails(
            context, errorCode, title, statusCode, detail, exception as PriceMismatchException);

        var json = JsonSerializer.Serialize(problemDetails, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        return context.Response.WriteAsync(json);
    }

    private static string GetTitleForStatus(int statusCode) => statusCode switch
    {
        StatusCodes.Status401Unauthorized => "Yêu cầu quyền truy cập",
        StatusCodes.Status403Forbidden => "Không có quyền truy cập",
        StatusCodes.Status404NotFound => "Tài nguyên không tìm thấy",
        StatusCodes.Status409Conflict => "Xung đột dữ liệu",
        _ => "Yêu cầu không hợp lệ"
    };
}
