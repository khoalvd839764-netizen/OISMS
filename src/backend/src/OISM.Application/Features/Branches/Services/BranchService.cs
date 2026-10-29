using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Branches;
using OISM.Domain.Entities;

namespace OISM.Application.Features.Branches.Services;

/// <summary>
/// Hiện thực hóa nghiệp vụ Quản lý Chi nhánh (FR-AUTH-04, BE2-08).
/// </summary>
public class BranchService : IBranchService
{
    private readonly IAppDbContext _context;

    public BranchService(IAppDbContext context)
    {
        _context = context;
    }

    public async Task<List<BranchResponse>> GetBranchesAsync(CancellationToken ct = default)
    {
        return await _context.Branches
            .Select(b => new BranchResponse
            {
                Id = b.Id,
                Name = b.Name,
                Address = b.Address,
                Phone = b.Phone,
                IsActive = b.IsActive
            })
            .ToListAsync(ct);
    }

    public async Task<BranchResponse?> GetBranchByIdAsync(Guid id, CancellationToken ct = default)
    {
        var branch = await _context.Branches.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (branch == null)
        {
            return null;
        }

        return new BranchResponse
        {
            Id = branch.Id,
            Name = branch.Name,
            Address = branch.Address,
            Phone = branch.Phone,
            IsActive = branch.IsActive
        };
    }

    public async Task<BranchResponse> CreateBranchAsync(CreateBranchRequest request, CancellationToken ct = default)
    {
        var branch = new Branch
        {
            Name = request.Name.Trim(),
            Address = request.Address?.Trim(),
            Phone = request.Phone?.Trim(),
            IsActive = true
        };

        _context.Branches.Add(branch);
        await _context.SaveChangesAsync(ct);

        return new BranchResponse
        {
            Id = branch.Id,
            Name = branch.Name,
            Address = branch.Address,
            Phone = branch.Phone,
            IsActive = branch.IsActive
        };
    }

    public async Task<BranchResponse?> UpdateBranchAsync(Guid id, UpdateBranchRequest request, CancellationToken ct = default)
    {
        var branch = await _context.Branches.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (branch == null)
        {
            return null;
        }

        branch.Name = request.Name.Trim();
        branch.Address = request.Address?.Trim();
        branch.Phone = request.Phone?.Trim();
        branch.IsActive = request.IsActive;

        await _context.SaveChangesAsync(ct);

        return new BranchResponse
        {
            Id = branch.Id,
            Name = branch.Name,
            Address = branch.Address,
            Phone = branch.Phone,
            IsActive = branch.IsActive
        };
    }

    public async Task<BranchToggleResult?> ToggleStatusAsync(Guid id, CancellationToken ct = default)
    {
        var branch = await _context.Branches.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (branch == null)
        {
            return null;
        }

        branch.IsActive = !branch.IsActive;
        await _context.SaveChangesAsync(ct);

        return new BranchToggleResult(branch.IsActive, $"Đã {(branch.IsActive ? "kích hoạt" : "ngưng hoạt động")} chi nhánh thành công.");
    }
}
