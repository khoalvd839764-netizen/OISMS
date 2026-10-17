using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OISM.Application.Features.Suppliers;
using OISM.Domain.Entities;
using OISM.Infrastructure.Persistence;

namespace OISM.WebApi.Controllers;

[ApiController]
[Route("api/suppliers")]
[Authorize]
public class SuppliersController : ControllerBase
{
    private readonly AppDbContext _context;

    public SuppliersController(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Lấy danh sách Nhà cung cấp hàng hóa cho Cửa hàng (FR-INV-02).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetSuppliers([FromQuery] string? search)
    {
        var query = _context.Suppliers.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.Trim().ToLower();
            query = query.Where(s => s.Name.ToLower().Contains(searchLower) ||
                                     (s.Code != null && s.Code.ToLower().Contains(searchLower)) ||
                                     (s.Phone != null && s.Phone.Contains(searchLower)));
        }

        var suppliers = await query
            .OrderBy(s => s.Name)
            .Select(s => new SupplierResponse
            {
                Id = s.Id,
                Name = s.Name,
                Code = s.Code,
                ContactName = s.ContactName,
                Phone = s.Phone,
                Email = s.Email,
                Address = s.Address,
                IsActive = s.IsActive
            })
            .ToListAsync();

        return Ok(suppliers);
    }

    /// <summary>
    /// Tạo mới Nhà cung cấp.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> CreateSupplier([FromBody] CreateSupplierRequest request)
    {
        var validator = new CreateSupplierValidator();
        var validationResult = await validator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        var supplier = new Supplier
        {
            Name = request.Name.Trim(),
            Code = request.Code?.Trim().ToUpper(),
            ContactName = request.ContactName?.Trim(),
            Phone = request.Phone?.Trim(),
            Email = request.Email?.Trim(),
            Address = request.Address?.Trim(),
            IsActive = true
        };

        _context.Suppliers.Add(supplier);
        await _context.SaveChangesAsync();

        return StatusCode(201, new SupplierResponse
        {
            Id = supplier.Id,
            Name = supplier.Name,
            Code = supplier.Code,
            ContactName = supplier.ContactName,
            Phone = supplier.Phone,
            Email = supplier.Email,
            Address = supplier.Address,
            IsActive = supplier.IsActive
        });
    }
}
