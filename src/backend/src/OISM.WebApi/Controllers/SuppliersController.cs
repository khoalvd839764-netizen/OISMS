using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OISM.Application.Features.Suppliers;
using OISM.Application.Features.Suppliers.Services;

namespace OISM.WebApi.Controllers;

[ApiController]
[Route("api/suppliers")]
[Authorize]
public class SuppliersController : ControllerBase
{
    private readonly ISupplierService _supplierService;

    public SuppliersController(ISupplierService supplierService)
    {
        _supplierService = supplierService;
    }

    /// <summary>
    /// Lấy danh sách Nhà cung cấp hàng hóa cho Cửa hàng (FR-INV-02).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetSuppliers([FromQuery] string? search, CancellationToken ct)
    {
        var suppliers = await _supplierService.GetSuppliersAsync(search, ct);
        return Ok(suppliers);
    }

    /// <summary>
    /// Lấy chi tiết thông tin một Nhà cung cấp theo Id.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetSupplierById(Guid id, CancellationToken ct)
    {
        var supplier = await _supplierService.GetSupplierByIdAsync(id, ct);
        if (supplier == null)
        {
            return NotFound(new { message = "Không tìm thấy nhà cung cấp." });
        }

        return Ok(supplier);
    }

    /// <summary>
    /// Tạo mới Nhà cung cấp.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> CreateSupplier([FromBody] CreateSupplierRequest request, CancellationToken ct)
    {
        var validator = new CreateSupplierValidator();
        var validationResult = await validator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        var response = await _supplierService.CreateSupplierAsync(request, ct);
        return StatusCode(201, response);
    }
}
