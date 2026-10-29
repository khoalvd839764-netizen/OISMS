using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Suppliers;
using OISM.Domain.Entities;

namespace OISM.Application.Features.Suppliers.Services;

/// <summary>
/// Hiện thực hóa nghiệp vụ Quản lý Nhà cung cấp (FR-INV-02).
/// </summary>
public class SupplierService : ISupplierService
{
    private readonly IAppDbContext _context;

    public SupplierService(IAppDbContext context)
    {
        _context = context;
    }

    public async Task<List<SupplierResponse>> GetSuppliersAsync(string? search, CancellationToken ct = default)
    {
        var query = _context.Suppliers.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.Trim().ToLower();
            query = query.Where(s => s.Name.ToLower().Contains(searchLower) ||
                                     (s.Email != null && s.Email.ToLower().Contains(searchLower)) ||
                                     (s.Phone != null && s.Phone.Contains(searchLower)));
        }

        return await query
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
            .ToListAsync(ct);
    }

    public async Task<SupplierResponse?> GetSupplierByIdAsync(Guid id, CancellationToken ct = default)
    {
        var s = await _context.Suppliers.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (s == null)
        {
            return null;
        }

        return new SupplierResponse
        {
            Id = s.Id,
            Name = s.Name,
            Code = s.Code,
            ContactName = s.ContactName,
            Phone = s.Phone,
            Email = s.Email,
            Address = s.Address,
            IsActive = s.IsActive
        };
    }

    public async Task<SupplierResponse> CreateSupplierAsync(CreateSupplierRequest request, CancellationToken ct = default)
    {
        var supplier = new Supplier
        {
            Name = request.Name.Trim(),
            Code = request.Code?.Trim().ToUpper(),
            ContactName = request.ContactName?.Trim(),
            Phone = request.Phone?.Trim(),
            Email = request.Email?.Trim().ToLower(),
            Address = request.Address?.Trim(),
            IsActive = true
        };

        _context.Suppliers.Add(supplier);
        await _context.SaveChangesAsync(ct);

        return new SupplierResponse
        {
            Id = supplier.Id,
            Name = supplier.Name,
            Code = supplier.Code,
            ContactName = supplier.ContactName,
            Phone = supplier.Phone,
            Email = supplier.Email,
            Address = supplier.Address,
            IsActive = supplier.IsActive
        };
    }
}
