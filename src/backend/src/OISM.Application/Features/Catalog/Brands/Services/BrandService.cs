using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Catalog.Brands;
using OISM.Domain.Entities;

namespace OISM.Application.Features.Catalog.Brands.Services;

/// <summary>
/// Hiện thực hóa nghiệp vụ Quản lý Thương hiệu sản phẩm (FR-PROD-01).
/// </summary>
public class BrandService : IBrandService
{
    private readonly IAppDbContext _context;

    public BrandService(IAppDbContext context)
    {
        _context = context;
    }

    public async Task<List<BrandResponse>> GetBrandsAsync(CancellationToken ct = default)
    {
        return await _context.Brands
            .OrderBy(b => b.Name)
            .Select(b => new BrandResponse
            {
                Id = b.Id,
                Name = b.Name,
                Description = b.Description,
                IsActive = b.IsActive
            })
            .ToListAsync(ct);
    }

    public async Task<BrandResponse> CreateBrandAsync(CreateBrandRequest request, CancellationToken ct = default)
    {
        var brand = new Brand
        {
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            IsActive = true
        };

        _context.Brands.Add(brand);
        await _context.SaveChangesAsync(ct);

        return new BrandResponse
        {
            Id = brand.Id,
            Name = brand.Name,
            Description = brand.Description,
            IsActive = brand.IsActive
        };
    }

    public async Task<BrandResponse?> UpdateBrandAsync(Guid id, UpdateBrandRequest request, CancellationToken ct = default)
    {
        var brand = await _context.Brands.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (brand == null)
        {
            return null;
        }

        brand.Name = request.Name.Trim();
        brand.Description = request.Description?.Trim();
        brand.IsActive = request.IsActive;

        await _context.SaveChangesAsync(ct);

        return new BrandResponse
        {
            Id = brand.Id,
            Name = brand.Name,
            Description = brand.Description,
            IsActive = brand.IsActive
        };
    }

    public async Task<bool> DeleteBrandAsync(Guid id, CancellationToken ct = default)
    {
        var brand = await _context.Brands.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (brand == null)
        {
            return false;
        }

        brand.IsActive = false;
        await _context.SaveChangesAsync(ct);
        return true;
    }
}
