using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.PaymentAccounts.Dtos;
using OISM.Domain.Entities;

namespace OISM.Application.Features.PaymentAccounts.Services;

public class PaymentAccountService : IPaymentAccountService
{
    private readonly IAppDbContext _context;
    private readonly ICurrentTenantService _currentTenantService;

    public PaymentAccountService(IAppDbContext context, ICurrentTenantService currentTenantService)
    {
        _context = context;
        _currentTenantService = currentTenantService;
    }

    private Guid EnsureTenantId()
    {
        var tenantId = _currentTenantService.TenantId;
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            throw new UnauthorizedAccessException("Không xác định được TenantId hợp lệ từ ngữ cảnh xác thực.");
        }
        return tenantId.Value;
    }

    public async Task<List<PaymentAccountDto>> GetAccountsAsync(CancellationToken ct = default)
    {
        var tenantId = EnsureTenantId();

        var accounts = await _context.PaymentAccounts
            .Where(a => a.TenantId == tenantId)
            .OrderByDescending(a => a.IsDefault)
            .ThenByDescending(a => a.IsActive)
            .ThenByDescending(a => a.CreatedAt)
            .ToListAsync(ct);

        return accounts.Select(MapToDto).ToList();
    }

    public async Task<List<ActivePaymentAccountDto>> GetActiveAccountsAsync(CancellationToken ct = default)
    {
        var tenantId = EnsureTenantId();

        var accounts = await _context.PaymentAccounts
            .Where(a => a.TenantId == tenantId && a.IsActive)
            .OrderByDescending(a => a.IsDefault)
            .ThenBy(a => a.BankShortName)
            .ToListAsync(ct);

        return accounts.Select(a => new ActivePaymentAccountDto
        {
            Id = a.Id,
            BankBin = a.BankBin,
            BankName = a.BankName,
            BankShortName = a.BankShortName,
            AccountNumber = a.AccountNumber,
            AccountName = a.AccountName,
            IsDefault = a.IsDefault
        }).ToList();
    }

    public async Task<PaymentAccountDto> CreateAccountAsync(CreatePaymentAccountRequest request, Guid userId, CancellationToken ct = default)
    {
        var tenantId = EnsureTenantId();

        PaymentAccount.ValidateFields(request.BankBin, request.BankName, request.BankShortName, request.AccountNumber, request.AccountName);

        var cleanBin = request.BankBin.Trim();
        var cleanAccount = request.AccountNumber.Trim();

        // Kiểm tra trùng BIN + Số TK trong cùng tenant
        var exists = await _context.PaymentAccounts
            .AnyAsync(a => a.TenantId == tenantId && a.BankBin == cleanBin && a.AccountNumber == cleanAccount, ct);

        if (exists)
        {
            throw new OismException("PAYMENT_ACCOUNT_DUPLICATE", $"Tài khoản ngân hàng {cleanBin} - {cleanAccount} đã tồn tại trong cửa hàng.", 409);
        }

        // Kiểm tra xem đã có tài khoản nào chưa
        var hasExistingAccounts = await _context.PaymentAccounts.AnyAsync(a => a.TenantId == tenantId && a.IsActive, ct);
        var shouldBeDefault = request.IsDefault || !hasExistingAccounts;

        var account = new PaymentAccount
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BankBin = cleanBin,
            BankName = request.BankName.Trim(),
            BankShortName = request.BankShortName.Trim().ToUpperInvariant(),
            AccountNumber = cleanAccount,
            AccountName = request.AccountName.Trim().ToUpperInvariant(),
            IsDefault = false,
            IsActive = true,
            UpdatedByUserId = userId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        if (shouldBeDefault)
        {
            // Bỏ default của các tài khoản khác trước
            var existingDefaults = await _context.PaymentAccounts
                .Where(a => a.TenantId == tenantId && a.IsDefault)
                .ToListAsync(ct);

            foreach (var d in existingDefaults)
            {
                d.UnsetDefault(userId);
            }

            account.SetDefault(userId);
        }

        _context.PaymentAccounts.Add(account);
        await _context.SaveChangesAsync(ct);

        return MapToDto(account);
    }

    public async Task<PaymentAccountDto> UpdateAccountAsync(Guid id, UpdatePaymentAccountRequest request, Guid userId, CancellationToken ct = default)
    {
        var tenantId = EnsureTenantId();

        PaymentAccount.ValidateFields(request.BankBin, request.BankName, request.BankShortName, request.AccountNumber, request.AccountName);

        var account = await _context.PaymentAccounts
            .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.Id == id, ct);

        if (account == null)
        {
            throw new OismException("PAYMENT_ACCOUNT_NOT_FOUND", $"Không tìm thấy tài khoản thanh toán '{id}'.", 404);
        }

        var cleanBin = request.BankBin.Trim();
        var cleanAccount = request.AccountNumber.Trim();

        // Kiểm tra trùng lặp với tài khoản khác
        var duplicate = await _context.PaymentAccounts
            .AnyAsync(a => a.TenantId == tenantId && a.Id != id && a.BankBin == cleanBin && a.AccountNumber == cleanAccount, ct);

        if (duplicate)
        {
            throw new OismException("PAYMENT_ACCOUNT_DUPLICATE", $"Tài khoản ngân hàng {cleanBin} - {cleanAccount} đã tồn tại trong cửa hàng.", 409);
        }

        account.Update(cleanBin, request.BankName, request.BankShortName, cleanAccount, request.AccountName, userId);
        await _context.SaveChangesAsync(ct);

        return MapToDto(account);
    }

    public async Task<PaymentAccountDto> SetDefaultAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        var tenantId = EnsureTenantId();

        var account = await _context.PaymentAccounts
            .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.Id == id, ct);

        if (account == null)
        {
            throw new OismException("PAYMENT_ACCOUNT_NOT_FOUND", $"Không tìm thấy tài khoản thanh toán '{id}'.", 404);
        }

        if (!account.IsActive)
        {
            throw new OismException("PAYMENT_ACCOUNT_INACTIVE", "Không thể đặt một tài khoản đã ngừng hoạt động làm mặc định.", 400);
        }

        // Bỏ cờ mặc định của tất cả tài khoản khác trong tenant
        var otherDefaults = await _context.PaymentAccounts
            .Where(a => a.TenantId == tenantId && a.Id != id && a.IsDefault)
            .ToListAsync(ct);

        foreach (var other in otherDefaults)
        {
            other.UnsetDefault(userId);
        }

        account.SetDefault(userId);
        await _context.SaveChangesAsync(ct);

        return MapToDto(account);
    }

    public async Task<PaymentAccountDto> DeactivateAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        var tenantId = EnsureTenantId();

        var account = await _context.PaymentAccounts
            .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.Id == id, ct);

        if (account == null)
        {
            throw new OismException("PAYMENT_ACCOUNT_NOT_FOUND", $"Không tìm thấy tài khoản thanh toán '{id}'.", 404);
        }

        var wasDefault = account.IsDefault;
        account.Deactivate(userId);

        // Nếu tài khoản bị hủy kích hoạt là default, thử gán default cho một tài khoản active khác (nếu còn)
        if (wasDefault)
        {
            var nextActive = await _context.PaymentAccounts
                .Where(a => a.TenantId == tenantId && a.Id != id && a.IsActive)
                .OrderByDescending(a => a.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (nextActive != null)
            {
                nextActive.SetDefault(userId);
            }
        }

        await _context.SaveChangesAsync(ct);
        return MapToDto(account);
    }

    private static PaymentAccountDto MapToDto(PaymentAccount a) => new()
    {
        Id = a.Id,
        TenantId = a.TenantId,
        BankBin = a.BankBin,
        BankName = a.BankName,
        BankShortName = a.BankShortName,
        AccountNumber = a.AccountNumber,
        AccountName = a.AccountName,
        IsDefault = a.IsDefault,
        IsActive = a.IsActive,
        CreatedAt = a.CreatedAt,
        UpdatedAt = a.UpdatedAt
    };
}
