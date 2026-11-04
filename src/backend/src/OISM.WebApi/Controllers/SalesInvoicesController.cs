using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OISM.Application.Common.Exceptions;
using OISM.Application.Features.SalesInvoices.Dtos;
using OISM.Application.Features.SalesInvoices.Services;

namespace OISM.WebApi.Controllers;

/// <summary>
/// Controller điều phối truy vấn hóa đơn bán hàng và truy vết chứng từ xuất kho (OISMS-47-U02).
/// </summary>
[ApiController]
[Route("api/sales-invoices")]
[Authorize]
public class SalesInvoicesController : ControllerBase
{
    private readonly ISalesInvoiceService _salesInvoiceService;

    public SalesInvoicesController(ISalesInvoiceService salesInvoiceService)
    {
        _salesInvoiceService = salesInvoiceService;
    }

    /// <summary>
    /// Danh sách hóa đơn bán hàng có phân trang và bộ lọc.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> GetList([FromQuery] SalesInvoiceFilterDto filter, CancellationToken ct)
    {
        var result = await _salesInvoiceService.GetSalesInvoicesAsync(filter, ct);
        return Ok(result);
    }

    /// <summary>
    /// Xem tóm tắt thông tin một hóa đơn bán hàng theo ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return CreateProblemResult("VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Mã hóa đơn không được để trống.");
        }

        try
        {
            var invoice = await _salesInvoiceService.GetSalesInvoiceByIdAsync(id, ct);
            return Ok(invoice);
        }
        catch (OismException ex)
        {
            return CreateProblemResult(ex.ErrorCode, GetTitleForErrorCode(ex.ErrorCode), ex.StatusCode, ex.Message);
        }
    }

    /// <summary>
    /// Dữ liệu in lại hóa đơn theo số hóa đơn (dành cho Thu ngân và Quản trị).
    /// </summary>
    [HttpGet("receipt")]
    [Authorize(Roles = "Owner,Staff,Cashier")]
    public async Task<IActionResult> GetReceiptByNumber([FromQuery] string? number, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(number))
        {
            return CreateProblemResult("VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Số hóa đơn (number) không được để trống.");
        }

        try
        {
            var receipt = await _salesInvoiceService.GetReceiptByNumberAsync(number, ct);
            return Ok(receipt);
        }
        catch (OismException ex)
        {
            return CreateProblemResult(ex.ErrorCode, GetTitleForErrorCode(ex.ErrorCode), ex.StatusCode, ex.Message);
        }
    }

    /// <summary>
    /// Dữ liệu in lại hóa đơn theo ID hóa đơn (dành cho Thu ngân và Quản trị).
    /// </summary>
    [HttpGet("{id:guid}/receipt")]
    [Authorize(Roles = "Owner,Staff,Cashier")]
    public async Task<IActionResult> GetReceiptById(Guid id, CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return CreateProblemResult("VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Mã hóa đơn không được để trống.");
        }

        try
        {
            var receipt = await _salesInvoiceService.GetReceiptByIdAsync(id, ct);
            return Ok(receipt);
        }
        catch (OismException ex)
        {
            return CreateProblemResult(ex.ErrorCode, GetTitleForErrorCode(ex.ErrorCode), ex.StatusCode, ex.Message);
        }
    }

    /// <summary>
    /// Truy vết toàn bộ chuỗi chứng từ và dòng sổ kho bắt đầu từ hóa đơn bán hàng.
    /// </summary>
    [HttpGet("{id:guid}/trace")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> TraceById(Guid id, CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return CreateProblemResult("VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Mã hóa đơn không được để trống.");
        }

        try
        {
            var trace = await _salesInvoiceService.TraceByInvoiceIdAsync(id, ct);
            return Ok(trace);
        }
        catch (OismException ex)
        {
            return CreateProblemResult(ex.ErrorCode, GetTitleForErrorCode(ex.ErrorCode), ex.StatusCode, ex.Message);
        }
    }

    private IActionResult CreateProblemResult(string errorCode, string title, int statusCode, string detail)
    {
        var problem = new ProblemDetails
        {
            Title = title,
            Status = statusCode,
            Detail = detail,
            Instance = HttpContext.Request.Path,
            Extensions =
            {
                ["errorCode"] = errorCode,
                ["timestamp"] = DateTimeOffset.UtcNow
            }
        };
        return StatusCode(statusCode, problem);
    }

    private static string GetTitleForErrorCode(string errorCode) => errorCode switch
    {
        "SALES_INVOICE_NOT_FOUND" => "Không tìm thấy hóa đơn bán hàng",
        "ORDER_NOT_FOUND" => "Không tìm thấy đơn hàng",
        "LEDGER_ENTRY_NOT_FOUND" => "Không tìm thấy dòng sổ kho",
        "VALIDATION_ERROR" => "Lỗi xác thực dữ liệu",
        "UNAUTHORIZED" => "Chưa xác thực",
        _ => "Lỗi xử lý nghiệp vụ"
    };
}
