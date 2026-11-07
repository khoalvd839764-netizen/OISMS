using Microsoft.AspNetCore.Mvc;
using OISM.Application.Common.Exceptions;
using OISM.Domain.Common;

namespace OISM.WebApi.Http;

internal static class ApiErrorResponse
{
    private const string ValidationCode = "VALIDATION_ERROR";
    private const string ValidationTitle = "Lỗi xác thực dữ liệu";

    public static ObjectResult? ForAction(HttpContext context, ApiErrorProfile profile, Exception exception)
    {
        if (exception is OismException business)
        {
            return ForBusinessError(context, profile, business);
        }

        if (exception is InvalidStateTransitionException)
        {
            return null;
        }

        return (profile, exception) switch
        {
            (ApiErrorProfile.GoodsReceiptRead or ApiErrorProfile.GoodsReceiptCreate or ApiErrorProfile.GoodsReceipts,
                KeyNotFoundException) => Problem(context, profile, "NOT_FOUND", "Không tìm thấy dữ liệu", 404, exception.Message),
            (ApiErrorProfile.GoodsReceiptCreate or ApiErrorProfile.GoodsReceipts, ArgumentException) =>
                Problem(context, profile, ValidationCode, ValidationTitle, 400, exception.Message),
            (ApiErrorProfile.GoodsReceiptCreate, InvalidOperationException) =>
                Problem(context, profile, ValidationCode, "Xung đột dữ liệu", 409, exception.Message),
            (ApiErrorProfile.GoodsReceipts, InvalidOperationException) =>
                Problem(context, profile, "INVALID_STATE_TRANSITION", "Chuyển trạng thái không hợp lệ", 409, exception.Message),
            (ApiErrorProfile.Reserve or ApiErrorProfile.OrderProcess, KeyNotFoundException) => Message(404, exception.Message),
            (ApiErrorProfile.Reserve or ApiErrorProfile.OrderProcess, InvalidOperationException) => Message(409, exception.Message),
            (ApiErrorProfile.Reserve, ArgumentException) => Message(400, exception.Message),
            (ApiErrorProfile.PaymentAccounts, ArgumentException) => Problem(context, profile, ValidationCode, ValidationTitle, 400, exception.Message),
            (ApiErrorProfile.Register, _) => RegistrationFailure(context, exception),
            (ApiErrorProfile.Simulator, _) => Message(400, exception.Message),
            (ApiErrorProfile.Webhooks, _) => WebhookFailure(context, exception),
            _ => null
        };
    }

    private static ObjectResult? ForBusinessError(HttpContext context, ApiErrorProfile profile, OismException exception)
    {
        var (status, code, detail) = (exception.StatusCode, exception.ErrorCode, exception.Message);
        switch (profile)
        {
            case ApiErrorProfile.Register:
                return status is 400 or 409 ? Message(status, detail) : RegistrationFailure(context, exception);
            case ApiErrorProfile.Login:
                return status == 401 ? Message(status, detail) : null;
            case ApiErrorProfile.RefreshToken:
                return status is 400 or 401 ? Message(status, detail) : null;
            case ApiErrorProfile.MessageBadRequest:
                return Message(400, detail);
            case ApiErrorProfile.MessageValidation:
                return status == 400 ? Message(status, detail) : null;
            case ApiErrorProfile.ProductMutation:
                return status is 400 or 404 or 409 or 422
                    ? Result(status, new { errorCode = code, message = detail })
                    : null;
            case ApiErrorProfile.WarehouseUpdate:
                return Message(status == 409 ? 409 : 400, detail);
            case ApiErrorProfile.GoodsReceiptRead:
            case ApiErrorProfile.Reserve:
                return null;
        }

        var title = profile == ApiErrorProfile.GoodsReceiptCreate && status == 409 && code == ValidationCode
            ? "Xung đột dữ liệu"
            : ApiErrorTitles.Get(profile, code);
        var priceMismatch = profile is ApiErrorProfile.Orders or ApiErrorProfile.PosCheckout
            ? exception as PriceMismatchException
            : null;
        return Result(status, CreateProblem(context, profile, code, title, status, detail, priceMismatch));
    }

    public static ObjectResult Problem(HttpContext? context, ApiErrorProfile profile, string code, string title, int status, string detail) =>
        Result(status, CreateProblem(context, profile, code, title, status, detail));

    public static ObjectResult Problem(HttpContext context, ApiErrorProfile profile, string code, int status, string detail) =>
        Problem(context, profile, code, ApiErrorTitles.Get(profile, code), status, detail);

    public static ProblemDetails CreateProblemDetails(
        HttpContext context, string code, string title, int status, string detail, PriceMismatchException? priceMismatch = null) =>
        CreateProblem(context, ApiErrorProfile.Default, code, title, status, detail, priceMismatch);

    private static ProblemDetails CreateProblem(
        HttpContext? context, ApiErrorProfile profile, string code, string title, int status, string detail,
        PriceMismatchException? priceMismatch = null)
    {
        var orderProblem = profile is ApiErrorProfile.Orders or ApiErrorProfile.OrderProcess;
        var type = profile switch
        {
            ApiErrorProfile.InventoryTrace or ApiErrorProfile.SalesInvoices => null,
            _ when orderProblem && priceMismatch is null => $"https://oisms.local/errors/{code.ToLowerInvariant().Replace('_', '-')}",
            _ => $"https://oisms.local/errors/{code}"
        };
        var problem = new ProblemDetails
        {
            Type = type,
            Title = title,
            Status = status,
            Detail = detail,
            Instance = priceMismatch is null
                ? context?.Request.Path.ToString() ?? string.Empty
                : context?.Request.Path.Value ?? string.Empty
        };
        problem.Extensions["errorCode"] = code;
        if (priceMismatch is not null)
        {
            problem.Extensions["currentPrices"] = priceMismatch.CurrentPrices;
        }
        if (!orderProblem || priceMismatch is not null)
        {
            problem.Extensions["timestamp"] = DateTimeOffset.UtcNow;
        }
        if (profile is ApiErrorProfile.GoodsReceipts or ApiErrorProfile.GoodsReceiptCreate or ApiErrorProfile.GoodsReceiptRead)
        {
            problem.Extensions["message"] = detail;
        }
        return problem;
    }

    private static ObjectResult RegistrationFailure(HttpContext context, Exception exception)
    {
        LogFailure(context, exception);
        return Message(500, "Đã xảy ra lỗi trong quá trình xử lý đăng ký.");
    }

    private static ObjectResult WebhookFailure(HttpContext context, Exception exception)
    {
        LogFailure(context, exception);
        return Problem(context, ApiErrorProfile.Webhooks, "INTERNAL_SERVER_ERROR", "Lỗi xử lý nội bộ", 500,
            "Đã xảy ra lỗi trong quá trình xử lý đơn hàng webhook.");
    }

    private static void LogFailure(HttpContext context, Exception exception) =>
        context.RequestServices.GetRequiredService<ILogger<ApiErrorContractAttribute>>()
            .LogError(exception, "Ngoại lệ chưa xử lý phát sinh tại endpoint: {Path}", context.Request.Path);

    private static ObjectResult Message(int status, string message) => Result(status, new { message });

    private static ObjectResult Result(int status, object body) => new(body) { StatusCode = status };
}
