using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OISM.Application.Features.Warehouses;
using OISM.Domain.Entities;
using OISM.Infrastructure.Persistence;

namespace OISM.WebApi.Controllers;

/// <summary>
/// API Quản lý Kho hàng thuộc Chi nhánh (BE2-08, FR-AUTH-04).
/// </summary>
[ApiController]
[Route("api/warehouses")]
[Authorize]
public class WarehousesController : ControllerBase
{
    private readonly AppDbContext _context;

    public WarehousesController(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Lấy danh sách kho hàng thuộc về Cửa hàng của phiên đăng nhập (Hỗ trợ lọc theo Chi nhánh - branchId).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetWarehouses([FromQuery] Guid? branchId)
    {
        var query = _context.Warehouses
            .Include(w => w.Branch)
            .AsQueryable();

        if (branchId.HasValue)
        {
            query = query.Where(w => w.BranchId == branchId.Value);
        }

        var warehouses = await query
            .OrderBy(w => w.Name)
            .Select(w => new WarehouseResponse
            {
                Id = w.Id,
                BranchId = w.BranchId,
                BranchName = w.Branch != null ? w.Branch.Name : string.Empty,
                Name = w.Name,
                IsActive = w.IsActive,
                CreatedAt = w.CreatedAt
            })
            .ToListAsync();

        return Ok(warehouses);
    }

    /// <summary>
    /// Tạo kho hàng mới gắn với Chi nhánh (Yêu cầu quyền Owner hoặc Staff).
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> CreateWarehouse([FromBody] CreateWarehouseRequest request)
    {
        var validator = new CreateWarehouseValidator();
        var validationResult = await validator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        // Kiểm tra chi nhánh có tồn tại và thuộc Tenant hiện tại không
        var branch = await _context.Branches.FirstOrDefaultAsync(b => b.Id == request.BranchId);
        if (branch == null)
        {
            return BadRequest(new { message = "Chi nhánh được chọn không tồn tại hoặc không thuộc cửa hàng của bạn." });
        }

        var warehouse = new Warehouse
        {
            BranchId = request.BranchId,
            Name = request.Name.Trim(),
            IsActive = true
            // TenantId được tự động gán bởi AppDbContext thông qua ICurrentTenantService
        };

        _context.Warehouses.Add(warehouse);
        await _context.SaveChangesAsync();

        var response = new WarehouseResponse
        {
            Id = warehouse.Id,
            BranchId = warehouse.BranchId,
            BranchName = branch.Name,
            Name = warehouse.Name,
            IsActive = warehouse.IsActive,
            CreatedAt = warehouse.CreatedAt
        };

        return StatusCode(201, response);
    }

    /// <summary>
    /// Bật/Tắt trạng thái hoạt động của kho hàng (Yêu cầu quyền Owner hoặc Staff).
    /// </summary>
    [HttpPut("{id}/toggle-status")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> ToggleStatus(Guid id)
    {
        // Sử dụng LINQ FirstOrDefaultAsync theo chuẩn Composite Key và Tenant Filter
        var warehouse = await _context.Warehouses.FirstOrDefaultAsync(w => w.Id == id);
        if (warehouse == null)
        {
            return NotFound(new { message = "Không tìm thấy kho hàng." });
        }

        warehouse.IsActive = !warehouse.IsActive;
        await _context.SaveChangesAsync();

        return Ok(new
        {
            message = $"Đã {(warehouse.IsActive ? "kích hoạt" : "ngưng hoạt động")} kho hàng thành công.",
            id = warehouse.Id,
            isActive = warehouse.IsActive
        });
    }
}
