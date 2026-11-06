using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Webhooks.Services;
using OISM.Domain.Entities;

namespace OISM.Application.Features.ShopIntegrations.Services;

/// <summary>
/// Quản lý kết nối gian hàng sàn TMĐT. TenantId lấy từ query filter và SaveChanges của AppDbContext,
/// không nhận từ request. Secret chỉ trả về lúc tạo hoặc đổi, CSDL chỉ lưu bản băm.
/// </summary>
public class ShopIntegrationService : IShopIntegrationService
{
    private const string SecretPrefix = "whsec_";
    private const string ShopUniqueConstraint = "uq_shop_integrations_2";

    private readonly IAppDbContext _context;

    public ShopIntegrationService(IAppDbContext context)
    {
        _context = context;
    }

    public async Task<List<ShopIntegrationResponse>> GetIntegrationsAsync(CancellationToken ct = default)
    {
        var items = await _context.ShopIntegrations
            .AsNoTracking()
            .Include(si => si.Branch)
            .OrderBy(si => si.Channel)
            .ThenBy(si => si.ExternalShopId)
            .ToListAsync(ct);

        return items.Select(ToResponse).ToList();
    }

    public async Task<ShopIntegrationResponse?> GetIntegrationByIdAsync(Guid id, CancellationToken ct = default)
    {
        var integration = await _context.ShopIntegrations
            .AsNoTracking()
            .Include(si => si.Branch)
            .FirstOrDefaultAsync(si => si.Id == id, ct);

        return integration == null ? null : ToResponse(integration);
    }

    public async Task<ShopIntegrationSecretResponse> CreateIntegrationAsync(CreateShopIntegrationRequest request, CancellationToken ct = default)
    {
        var channel = WebhookCanonicalValidator.CanonicalizeChannel(request.Channel);
        var externalShopId = request.ExternalShopId.Trim();
        var branch = await RequireActiveBranchAsync(request.BranchId, ct);

        var duplicated = await _context.ShopIntegrations
            .AnyAsync(si => si.Channel == channel && si.ExternalShopId == externalShopId, ct);
        if (duplicated)
        {
            throw DuplicateShop(channel, externalShopId);
        }

        var rawSecret = GenerateSecret();
        var integration = new ShopIntegration
        {
            Channel = channel,
            ExternalShopId = externalShopId,
            BranchId = branch.Id,
            IsActive = true,
            SecretHash = WebhookSecretHasher.HashSecret(rawSecret)
        };

        _context.ShopIntegrations.Add(integration);
        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (_context.DatabaseErrors.IsUniqueViolation(ex, ShopUniqueConstraint))
        {
            throw DuplicateShop(channel, externalShopId);
        }

        integration.Branch = branch;
        return new ShopIntegrationSecretResponse
        {
            Integration = ToResponse(integration),
            WebhookSecret = rawSecret
        };
    }

    public async Task<ShopIntegrationResponse?> UpdateIntegrationAsync(Guid id, UpdateShopIntegrationRequest request, CancellationToken ct = default)
    {
        var integration = await _context.ShopIntegrations
            .FirstOrDefaultAsync(si => si.Id == id, ct);
        if (integration == null)
        {
            return null;
        }

        var branch = integration.BranchId == request.BranchId
            ? await _context.Branches.FirstOrDefaultAsync(b => b.Id == request.BranchId, ct)
            : await RequireActiveBranchAsync(request.BranchId, ct);

        integration.BranchId = request.BranchId;
        integration.IsActive = request.IsActive;
        integration.UpdatedAt = DateTimeOffset.UtcNow;
        await _context.SaveChangesAsync(ct);

        integration.Branch = branch;
        return ToResponse(integration);
    }

    public async Task<ShopIntegrationSecretResponse?> RotateSecretAsync(Guid id, CancellationToken ct = default)
    {
        var integration = await _context.ShopIntegrations
            .Include(si => si.Branch)
            .FirstOrDefaultAsync(si => si.Id == id, ct);
        if (integration == null)
        {
            return null;
        }

        var rawSecret = GenerateSecret();
        integration.SecretHash = WebhookSecretHasher.HashSecret(rawSecret);
        integration.UpdatedAt = DateTimeOffset.UtcNow;
        await _context.SaveChangesAsync(ct);

        return new ShopIntegrationSecretResponse
        {
            Integration = ToResponse(integration),
            WebhookSecret = rawSecret
        };
    }

    private async Task<Branch> RequireActiveBranchAsync(Guid branchId, CancellationToken ct)
    {
        var branch = await _context.Branches.FirstOrDefaultAsync(b => b.Id == branchId, ct);
        if (branch == null || !branch.IsActive)
        {
            throw new OismException("BRANCH_INACTIVE_OR_INVALID",
                "Chi nhánh được chọn không tồn tại, đã ngừng hoạt động hoặc không thuộc cửa hàng của bạn.", 404);
        }

        return branch;
    }

    private static OismException DuplicateShop(string channel, string externalShopId) =>
        new("SHOP_INTEGRATION_DUPLICATE",
            $"Gian hàng '{externalShopId}' trên kênh {channel} đã được kết nối.", 409);

    private static string GenerateSecret() =>
        SecretPrefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    private static ShopIntegrationResponse ToResponse(ShopIntegration si) => new()
    {
        Id = si.Id,
        Channel = si.Channel,
        ExternalShopId = si.ExternalShopId,
        BranchId = si.BranchId,
        BranchName = si.Branch?.Name ?? string.Empty,
        IsActive = si.IsActive,
        CreatedAt = si.CreatedAt,
        UpdatedAt = si.UpdatedAt
    };
}
