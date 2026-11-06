using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OISM.Application.Features.Orders.Dtos;
using OISM.Application.Features.Orders.Services;
using OISM.Application.Features.SalesInvoices.Services;
using OISM.WebApi.Http;

namespace OISM.WebApi.Controllers;

/// <summary>
/// API Quản lý Đơn hàng đa kênh và Điểm bán hàng (Order Management & POS Controller).
/// Hỗ trợ tra cứu danh sách, xem chi tiết, thanh toán POS, tạo đơn admin, duyệt, hoàn tất và hủy đơn.
/// </summary>
[ApiController]
[Route("api/orders")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly IPosCheckoutService _posCheckoutService;
    private readonly IOrderService _orderService;
    private readonly ISalesInvoiceService _salesInvoiceService;

    public OrdersController(
        IPosCheckoutService posCheckoutService,
        IOrderService orderService,
        ISalesInvoiceService salesInvoiceService)
    {
        _posCheckoutService = posCheckoutService;
        _orderService = orderService;
        _salesInvoiceService = salesInvoiceService;
    }

    /// <summary>
    /// Tra cứu danh sách đơn hàng đa kênh với bộ lọc (Trạng thái, Kênh bán, Chi nhánh, Thời gian, Từ khóa) và phân trang.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> GetList([FromQuery] OrderFilterDto filter, CancellationToken ct)
    {
        var result = await _orderService.GetOrdersAsync(filter, ct);
        return Ok(result);
    }

    /// <summary>
    /// Xem chi tiết một đơn hàng cụ thể kèm danh sách mặt hàng SKU, giá bán, giá vốn và thông tin khách hàng.
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var order = await _orderService.GetOrderByIdAsync(id, ct);
        if (order == null)
        {
            return NotFound(new { message = $"Không tìm thấy Đơn hàng với ID '{id}'." });
        }
        return Ok(order);
    }

    /// <summary>
    /// Truy vết toàn bộ chuỗi chứng từ và dòng sổ kho bắt đầu từ đơn hàng.
    /// </summary>
    [HttpGet("{id:guid}/trace")]
    [Authorize(Roles = "Owner,Staff")]
    [ApiErrorContract(ApiErrorProfile.Orders)]
    public async Task<IActionResult> TraceById(Guid id, CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.Orders, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Mã đơn hàng không được để trống.");
        }

        var trace = await _salesInvoiceService.TraceByOrderIdAsync(id, ct);
        return Ok(trace);
    }

    /// <summary>
    /// Tuyến đường alias thanh toán POS: POST /api/orders/checkout.
    /// Chia sẻ cùng lõi nghiệp vụ nguyên tử với POST /api/pos/checkout.
    /// Hỗ trợ vai trò Owner, Staff, Cashier.
    /// </summary>
    [HttpPost("checkout")]
    [Authorize(Roles = "Owner,Staff,Cashier")]
    [ApiErrorContract(ApiErrorProfile.PosCheckout)]
    public async Task<IActionResult> Checkout(
        [FromBody] PosCheckoutRequestDto dto,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKeyHeader,
        [FromHeader(Name = "X-Request-Key")] string? xRequestKeyHeader,
        CancellationToken ct)
    {
        return await PosCheckoutActionHelper.ExecuteCheckoutAsync(this, _posCheckoutService, dto, idempotencyKeyHeader, xRequestKeyHeader, ct);
    }

    /// <summary>
    /// Tạo đơn hàng thủ công trên Admin Portal (POST /api/orders).
    /// Khởi tạo trạng thái Reserved và giữ chỗ tồn kho 15 phút.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Owner,Staff")]
    [ApiErrorContract(ApiErrorProfile.Orders)]
    public async Task<IActionResult> CreateOrder(
        [FromBody] CreateOrderRequestDto dto,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKeyHeader,
        [FromHeader(Name = "X-Request-Key")] string? xRequestKeyHeader,
        CancellationToken ct)
    {
        if (dto == null)
        {
            return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.Orders, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Dữ liệu yêu cầu không được để trống.");
        }

        var rawHeaderKey = !string.IsNullOrWhiteSpace(idempotencyKeyHeader) ? idempotencyKeyHeader : xRequestKeyHeader;
        if (!string.IsNullOrWhiteSpace(rawHeaderKey))
        {
            if (Guid.TryParse(rawHeaderKey.Trim(), out var parsedHeaderGuid))
            {
                if (dto.RequestKey != Guid.Empty && dto.RequestKey != parsedHeaderGuid)
                {
                    return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.Orders, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "RequestKey trong payload không khớp với Idempotency-Key trong header.");
                }
            }
            else
            {
                return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.Orders, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Giá trị Idempotency-Key trong header không phải là GUID hợp lệ.");
            }
        }

        var userId = User.GetUserId();
        var result = await _orderService.CreateOrderAsync(dto, userId, ct);

        if (result.IsReplay)
        {
            return Ok(result);
        }

        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>
    /// Duyệt đơn hàng trực tuyến (POST /api/orders/{id}/confirm).
    /// Trừ tồn on_hand, giảm reserved, ghi Sổ cái OUT và chốt giá vốn WAC snapshot.
    /// </summary>
    [HttpPost("{id:guid}/confirm")]
    [Authorize(Roles = "Owner,Staff")]
    [ApiErrorContract(ApiErrorProfile.Orders)]
    public async Task<IActionResult> ConfirmOrder(
        [FromRoute] Guid id,
        [FromBody] ConfirmOrderRequestDto? dto,
        CancellationToken ct)
    {
        var userId = User.GetUserId();
        var result = await _orderService.ConfirmOrderAsync(id, dto ?? new ConfirmOrderRequestDto(), userId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Bắt đầu xử lý đóng gói đơn hàng (Confirmed -> Processing).
    /// </summary>
    [HttpPost("{id:guid}/process")]
    [Authorize(Roles = "Owner,Staff")]
    [ApiErrorContract(ApiErrorProfile.OrderProcess)]
    public async Task<IActionResult> Process(Guid id, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var result = await _orderService.ProcessOrderAsync(id, userId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Hoàn tất đơn hàng trực tuyến (POST /api/orders/{id}/complete).
    /// Ghi nhận giao hàng thành công, tuyệt đối không trừ tồn hay ghi ledger lần 2.
    /// </summary>
    [HttpPost("{id:guid}/complete")]
    [Authorize(Roles = "Owner,Staff")]
    [ApiErrorContract(ApiErrorProfile.Orders)]
    public async Task<IActionResult> CompleteOrder(
        [FromRoute] Guid id,
        [FromBody] CompleteOrderRequestDto? dto,
        CancellationToken ct)
    {
        var userId = User.GetUserId();
        var result = await _orderService.CompleteOrderAsync(id, dto ?? new CompleteOrderRequestDto(), userId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Hủy đơn hàng (POST /api/orders/{id}/cancel).
    /// Giải phóng tồn giữ chỗ (Reserved) về lại quỹ tồn khả dụng (Available).
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Roles = "Owner,Staff")]
    [ApiErrorContract(ApiErrorProfile.Orders)]
    public async Task<IActionResult> CancelOrder(
        [FromRoute] Guid id,
        [FromBody] CancelOrderRequestDto? dto,
        CancellationToken ct)
    {
        var userId = User.GetUserId();
        var result = await _orderService.CancelOrderAsync(id, dto ?? new CancelOrderRequestDto(), userId, ct);
        return Ok(result);
    }
}
