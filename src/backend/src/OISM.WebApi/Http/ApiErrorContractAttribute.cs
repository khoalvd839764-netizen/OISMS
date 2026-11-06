using Microsoft.AspNetCore.Mvc.Filters;

namespace OISM.WebApi.Http;

// Selects the response contract already published by an action. Unmapped exceptions
// continue to the global middleware.
[AttributeUsage(AttributeTargets.Method)]
internal sealed class ApiErrorContractAttribute(ApiErrorProfile profile) : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        if (context.HttpContext.Response.HasStarted ||
            context.Exception is OperationCanceledException &&
            context.HttpContext.RequestAborted.IsCancellationRequested)
        {
            return;
        }

        var result = ApiErrorResponse.ForAction(context.HttpContext, profile, context.Exception);
        if (result is not null)
        {
            context.Result = result;
            context.ExceptionHandled = true;
        }
    }
}

internal enum ApiErrorProfile
{
    Default,
    Register,
    Login,
    RefreshToken,
    MessageBadRequest,
    MessageValidation,
    ProductMutation,
    WarehouseUpdate,
    GoodsReceiptCreate,
    GoodsReceiptRead,
    GoodsReceipts,
    PurchaseOrders,
    Reserve,
    InventoryTrace,
    Orders,
    OrderProcess,
    PosCheckout,
    Pricing,
    SalesInvoices,
    Simulator,
    StockCountPeriods,
    StockCountSchedules,
    StockCounts,
    StockTransfers,
    Units,
    Webhooks
}
