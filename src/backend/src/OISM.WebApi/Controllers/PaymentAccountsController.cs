using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OISM.Application.Features.PaymentAccounts.Dtos;
using OISM.Application.Features.PaymentAccounts.Services;
using OISM.WebApi.Http;

namespace OISM.WebApi.Controllers;

/// <summary>
/// Quản lý tài khoản ngân hàng thụ hưởng nhận tiền qua VietQR theo Tenant (Upgrade CS-05).
/// Chỉ Owner được thêm, sửa, đặt mặc định và ngừng dùng tài khoản.
/// Thu ngân và nhân viên bán hàng được truy cập danh sách tài khoản đang hoạt động (active) để phục vụ POS.
/// </summary>
[ApiController]
[Route("api/payment-accounts")]
[Authorize]
public class PaymentAccountsController : ControllerBase
{
    private readonly IPaymentAccountService _service;

    public PaymentAccountsController(IPaymentAccountService service)
    {
        _service = service;
    }

    /// <summary>
    /// Lấy danh sách toàn bộ tài khoản ngân hàng của cửa hàng (Dành riêng cho Owner).
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Owner")]
    public async Task<IActionResult> GetAccounts(CancellationToken ct)
    {
        var accounts = await _service.GetAccountsAsync(ct);
        return Ok(accounts);
    }

    /// <summary>
    /// Lấy danh sách tài khoản ngân hàng đang hoạt động để thu ngân chọn lúc thanh toán POS.
    /// </summary>
    [HttpGet("active")]
    [Authorize(Roles = "Owner,Manager,Staff,Cashier")]
    public async Task<IActionResult> GetActiveAccounts(CancellationToken ct)
    {
        var activeAccounts = await _service.GetActiveAccountsAsync(ct);
        return Ok(activeAccounts);
    }

    /// <summary>
    /// Thêm tài khoản ngân hàng mới cho cửa hàng (Chỉ Owner).
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Owner")]
    public async Task<IActionResult> CreateAccount([FromBody] CreatePaymentAccountRequest request, CancellationToken ct)
    {
        var userId = User.GetUserId() ?? Guid.Empty;
        var created = await _service.CreateAccountAsync(request, userId, ct);
        return StatusCode(201, created);
    }

    /// <summary>
    /// Cập nhật thông tin tài khoản ngân hàng (Chỉ Owner).
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Owner")]
    public async Task<IActionResult> UpdateAccount([FromRoute] Guid id, [FromBody] UpdatePaymentAccountRequest request, CancellationToken ct)
    {
        var userId = User.GetUserId() ?? Guid.Empty;
        var updated = await _service.UpdateAccountAsync(id, request, userId, ct);
        return Ok(updated);
    }

    /// <summary>
    /// Đặt tài khoản làm mặc định khi tạo giao dịch QR thanh toán (Chỉ Owner).
    /// </summary>
    [HttpPost("{id:guid}/set-default")]
    [Authorize(Roles = "Owner")]
    public async Task<IActionResult> SetDefault([FromRoute] Guid id, CancellationToken ct)
    {
        var userId = User.GetUserId() ?? Guid.Empty;
        var result = await _service.SetDefaultAsync(id, userId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Ngừng sử dụng tài khoản ngân hàng (Chỉ Owner - Không xóa cứng để bảo vệ chứng từ lịch sử).
    /// </summary>
    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Roles = "Owner")]
    public async Task<IActionResult> Deactivate([FromRoute] Guid id, CancellationToken ct)
    {
        var userId = User.GetUserId() ?? Guid.Empty;
        var result = await _service.DeactivateAsync(id, userId, ct);
        return Ok(result);
    }
}
