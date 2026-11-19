using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OISM.Application.Features.GoodsIssues.Dtos;
using OISM.Application.Features.GoodsIssues.Services;
using OISM.WebApi.Http;

namespace OISM.WebApi.Controllers;

/// <summary>
/// Controller điều phối truy vấn Phiếu xuất kho 02-VT và Bảng kê xuất bán POS theo ngày (Upgrade 13 - P1..P5).
/// </summary>
[ApiController]
[Route("api/goods-issues")]
[Authorize(Roles = "Owner,Staff")]
public class GoodsIssuesController : ControllerBase
{
    private readonly IGoodsIssueService _goodsIssueService;

    public GoodsIssuesController(IGoodsIssueService goodsIssueService)
    {
        _goodsIssueService = goodsIssueService;
    }

    private bool CanViewCost => User.IsInRole("Owner");

    /// <summary>
    /// Danh sách phiếu xuất kho 02-VT có phân trang và bộ lọc.
    /// </summary>
    [HttpGet]
    [ApiErrorContract(ApiErrorProfile.SalesInvoices)]
    public async Task<IActionResult> GetList([FromQuery] GoodsIssueFilterDto filter, CancellationToken ct)
    {
        var result = await _goodsIssueService.GetGoodsIssuesAsync(filter, CanViewCost, ct);
        return Ok(result);
    }

    /// <summary>
    /// Xem chi tiết một phiếu xuất kho 02-VT theo ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ApiErrorContract(ApiErrorProfile.SalesInvoices)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return ApiErrorResponse.Problem(
                HttpContext,
                ApiErrorProfile.SalesInvoices,
                "VALIDATION_ERROR",
                "Lỗi xác thực dữ liệu",
                StatusCodes.Status400BadRequest,
                "Mã phiếu xuất kho không được để trống.");
        }

        var result = await _goodsIssueService.GetByIdAsync(id, CanViewCost, ct);
        return Ok(result);
    }

    /// <summary>
    /// Xem danh sách phiếu xuất kho 02-VT của một đơn hàng (sắp theo thứ tự kho).
    /// </summary>
    [HttpGet("by-order/{orderId:guid}")]
    [HttpGet("/api/orders/{orderId:guid}/goods-issues")]
    [ApiErrorContract(ApiErrorProfile.SalesInvoices)]
    public async Task<IActionResult> GetByOrderId(Guid orderId, CancellationToken ct)
    {
        if (orderId == Guid.Empty)
        {
            return ApiErrorResponse.Problem(
                HttpContext,
                ApiErrorProfile.SalesInvoices,
                "VALIDATION_ERROR",
                "Lỗi xác thực dữ liệu",
                StatusCodes.Status400BadRequest,
                "Mã đơn hàng không được để trống.");
        }

        var result = await _goodsIssueService.GetByOrderIdAsync(orderId, CanViewCost, ct);
        return Ok(result);
    }

    /// <summary>
    /// Lấy danh sách phiếu xuất kho 02-VT của nhiều đơn hàng để in hàng loạt (GET query hoặc POST body).
    /// </summary>
    [HttpGet("by-orders")]
    [ApiErrorContract(ApiErrorProfile.SalesInvoices)]
    public async Task<IActionResult> GetByOrderIdsQuery([FromQuery] string? orderIds, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(orderIds))
        {
            return ApiErrorResponse.Problem(
                HttpContext,
                ApiErrorProfile.SalesInvoices,
                "VALIDATION_ERROR",
                "Lỗi xác thực dữ liệu",
                StatusCodes.Status400BadRequest,
                "Tham số orderIds không được để trống.");
        }

        var parsedIds = new List<Guid>();
        foreach (var part in orderIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Guid.TryParse(part, out var guid) || guid == Guid.Empty)
            {
                return ApiErrorResponse.Problem(
                    HttpContext,
                    ApiErrorProfile.SalesInvoices,
                    "VALIDATION_ERROR",
                    "Lỗi xác thực dữ liệu",
                    StatusCodes.Status400BadRequest,
                    $"Mã đơn hàng '{part}' không phải là GUID hợp lệ.");
            }
            parsedIds.Add(guid);
        }

        var result = await _goodsIssueService.GetByOrderIdsAsync(parsedIds, CanViewCost, ct);
        return Ok(result);
    }

    /// <summary>
    /// Lấy danh sách phiếu xuất kho 02-VT của nhiều đơn hàng để in hàng loạt (POST body).
    /// </summary>
    [HttpPost("by-orders")]
    [ApiErrorContract(ApiErrorProfile.SalesInvoices)]
    public async Task<IActionResult> GetByOrderIdsPost([FromBody] BulkGoodsIssueByOrdersRequestDto? request, CancellationToken ct)
    {
        if (request == null || request.OrderIds == null || request.OrderIds.Count == 0)
        {
            return ApiErrorResponse.Problem(
                HttpContext,
                ApiErrorProfile.SalesInvoices,
                "VALIDATION_ERROR",
                "Lỗi xác thực dữ liệu",
                StatusCodes.Status400BadRequest,
                "Danh sách mã đơn hàng không được để trống.");
        }

        var result = await _goodsIssueService.GetByOrderIdsAsync(request.OrderIds, CanViewCost, ct);
        return Ok(result);
    }

    /// <summary>
    /// Bảng kê xuất bán POS theo ngày nghiệp vụ (UTC+07:00) và chi nhánh (Upgrade 13 - P2, P4).
    /// </summary>
    [HttpGet("pos-daily-summary")]
    [HttpGet("pos-statement")]
    [ApiErrorContract(ApiErrorProfile.SalesInvoices)]
    public async Task<IActionResult> GetPosDailySummary([FromQuery] PosDailyGoodsIssueQueryDto query, CancellationToken ct)
    {
        var result = await _goodsIssueService.GetPosDailySummaryAsync(query, CanViewCost, ct);
        return Ok(result);
    }
}
