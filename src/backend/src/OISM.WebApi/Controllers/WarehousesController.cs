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
    /// Lấy chi tiết thông tin một kho hàng theo Id.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetWarehouseById(Guid id)
    {
        var warehouse = await _context.Warehouses
            .Include(w => w.Branch)
            .FirstOrDefaultAsync(w => w.Id == id);

        if (warehouse == null)
        {
            return NotFound(new { message = "Không tìm thấy kho hàng." });
        }

        return Ok(new WarehouseResponse
        {
            Id = warehouse.Id,
            BranchId = warehouse.BranchId,
            BranchName = warehouse.Branch != null ? warehouse.Branch.Name : string.Empty,
            Name = warehouse.Name,
            IsActive = warehouse.IsActive,
            CreatedAt = warehouse.CreatedAt
        });
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
    /// Cập nhật thông tin kho hàng (Yêu cầu quyền Owner hoặc Staff - BE2-08).
    /// Quy tắc:
    /// 1. Nếu thay đổi BranchId:
    ///    - Bắt buộc kiểm tra dữ liệu phụ thuộc (phiếu nhập kho, số dư tồn kho, sổ cái giao dịch, giữ chỗ tồn kho).
    ///    - Nếu kho đã có bất kỳ dữ liệu phụ thuộc nào (kể cả khi tồn kho hiện tại đã về 0): CHẶN và trả 409 Conflict,
    ///      không cho phép đổi chi nhánh để bảo vệ tính toàn vẹn dữ liệu lịch sử và ràng buộc foreign key (fk_goods_receipts_warehouse_branch).
    ///    - Nếu kho hoàn toàn chưa phát sinh dữ liệu: Cho phép đổi chi nhánh sang chi nhánh đích cùng tenant.
    /// 2. Nếu BranchId không đổi: Cho phép cập nhật tên kho và trạng thái hoạt động (IsActive) bình thường.
    /// 3. Khóa bi quan kho hàng trên PostgreSQL chống tranh chấp đồng thời và bắt ngoại lệ FK có kiểm soát.
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> UpdateWarehouse(Guid id, [FromBody] UpdateWarehouseRequest request)
    {
        var validator = new UpdateWarehouseValidator();
        var validationResult = await validator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync<IActionResult>(async () =>
        {
            await using var tx = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);

            if (_context.Database.IsNpgsql())
            {
                // Khóa bi quan hàng kho hàng trên PostgreSQL để chống xung đột đồng thời
                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT id FROM warehouses WHERE id = {id} FOR UPDATE");
            }

            var warehouse = await _context.Warehouses
                .Include(w => w.Branch)
                .FirstOrDefaultAsync(w => w.Id == id);

            if (warehouse == null)
            {
                await tx.RollbackAsync();
                return NotFound(new { message = "Không tìm thấy kho hàng." });
            }

            var branch = warehouse.Branch;

            // Nếu người dùng yêu cầu đổi Chi nhánh của kho
            if (request.BranchId != warehouse.BranchId)
            {
                // 1. Kiểm tra toàn diện các dữ liệu phụ thuộc lịch sử
                var hasReceipts = await _context.GoodsReceipts.AnyAsync(g => g.WarehouseId == id);
                var hasBalances = await _context.WarehouseInventoryBalances.AnyAsync(b => b.WarehouseId == id);
                var hasTransactions = await _context.InventoryTransactions.AnyAsync(t => t.WarehouseId == id);
                var hasReservations = await _context.Reservations.AnyAsync(r => r.WarehouseId == id);

                if (hasReceipts || hasBalances || hasTransactions || hasReservations)
                {
                    await tx.RollbackAsync();
                    return Conflict(new
                    {
                        message = "Không thể thay đổi chi nhánh của kho hàng đã có lịch sử chứng từ, tồn kho hoặc giao dịch liên kết."
                    });
                }

                // 2. Nếu kho chưa có dữ liệu phụ thuộc: Xác minh chi nhánh đích tồn tại và cùng Tenant
                var targetBranch = await _context.Branches.FirstOrDefaultAsync(b => b.Id == request.BranchId);
                if (targetBranch == null)
                {
                    await tx.RollbackAsync();
                    return BadRequest(new { message = "Chi nhánh được chọn không tồn tại hoặc không thuộc cửa hàng của bạn." });
                }

                warehouse.BranchId = request.BranchId;
                branch = targetBranch;
            }

            warehouse.Name = request.Name.Trim();
            warehouse.IsActive = request.IsActive;

            try
            {
                await _context.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch (DbUpdateException ex)
            {
                await tx.RollbackAsync();
                // Bắt chính xác lỗi vi phạm khóa ngoại PostgreSQL 23503 liên quan đến đổi chi nhánh của kho
                if (ex.InnerException is Npgsql.PostgresException pgEx && pgEx.SqlState == "23503")
                {
                    return Conflict(new
                    {
                        message = "Không thể thay đổi chi nhánh của kho hàng do vi phạm ràng buộc dữ liệu liên kết."
                    });
                }
                throw;
            }

            return Ok(new WarehouseResponse
            {
                Id = warehouse.Id,
                BranchId = warehouse.BranchId,
                BranchName = branch?.Name ?? string.Empty,
                Name = warehouse.Name,
                IsActive = warehouse.IsActive,
                CreatedAt = warehouse.CreatedAt
            });
        });
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
