using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.PaymentAccounts.Dtos;
using OISM.Domain.Entities;

namespace OISM.Application.Features.PaymentAccounts.Services;

/// <summary>
/// Quản lý tài khoản nhận tiền VietQR của cửa hàng (CS-05).
/// Mọi thao tác ghi chạy trong một transaction và giữ khóa advisory theo tenant ({tenant}/payment_accounts),
/// để thêm, đặt mặc định và ngừng dùng diễn ra tuần tự, luôn còn tối đa một tài khoản mặc định.
/// </summary>
public class PaymentAccountService : IPaymentAccountService
{
    private const string DefaultActiveIndex = "uq_payment_accounts_tenant_default_active";
    private const string DuplicateAccountConstraint = "uq_payment_accounts_2";

    private readonly IAppDbContext _context;
    private readonly ICurrentTenantService _currentTenantService;
    private readonly ISqlLockService _lockService;

    public PaymentAccountService(IAppDbContext context, ICurrentTenantService currentTenantService, ISqlLockService lockService)
    {
        _context = context;
        _currentTenantService = currentTenantService;
        _lockService = lockService;
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

    private static void EnsureUserId(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            throw new UnauthorizedAccessException("Không xác định được người dùng từ phiên đăng nhập.");
        }
    }

    public async Task<List<PaymentAccountDto>> GetAccountsAsync(CancellationToken ct = default)
    {
        var tenantId = EnsureTenantId();

        var accounts = await _context.PaymentAccounts
            .AsNoTracking()
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
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.IsActive)
            .OrderByDescending(a => a.IsDefault)
            .ThenBy(a => a.BankShortName)
            .ThenBy(a => a.CreatedAt)
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
        EnsureUserId(userId);
        PaymentAccount.ValidateFields(request.BankBin, request.BankName, request.BankShortName, request.AccountNumber, request.AccountName);

        var cleanBin = request.BankBin.Trim();
        var cleanAccount = request.AccountNumber.Trim();

        return await ExecuteLockedAsync(tenantId, async () =>
        {
            var exists = await _context.PaymentAccounts
                .AnyAsync(a => a.TenantId == tenantId && a.BankBin == cleanBin && a.AccountNumber == cleanAccount, ct);
            if (exists)
            {
                throw DuplicateAccount(cleanBin, cleanAccount);
            }

            var hasActiveDefault = await _context.PaymentAccounts
                .AnyAsync(a => a.TenantId == tenantId && a.IsActive && a.IsDefault, ct);
            var shouldBeDefault = request.IsDefault || !hasActiveDefault;

            if (shouldBeDefault)
            {
                // Bỏ mặc định cũ và lưu trước, rồi mới thêm tài khoản mặc định mới: không lúc nào có hai mặc định.
                await UnsetOtherDefaultsAsync(tenantId, exceptId: null, userId, ct);
            }

            var account = new PaymentAccount
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                BankBin = cleanBin,
                BankName = request.BankName.Trim(),
                BankShortName = request.BankShortName.Trim().ToUpperInvariant(),
                AccountNumber = cleanAccount,
                AccountName = request.AccountName.Trim().ToUpperInvariant(),
                IsDefault = shouldBeDefault,
                IsActive = true,
                UpdatedByUserId = userId,
                CreatedAt = DateTimeOffset.UtcNow
            };

            _context.PaymentAccounts.Add(account);
            await SaveAsync(cleanBin, cleanAccount, ct);
            return MapToDto(account);
        }, ct);
    }

    public async Task<PaymentAccountDto> UpdateAccountAsync(Guid id, UpdatePaymentAccountRequest request, Guid userId, CancellationToken ct = default)
    {
        var tenantId = EnsureTenantId();
        EnsureUserId(userId);

        return await ExecuteLockedAsync(tenantId, async () =>
        {
            var account = await FindAsync(tenantId, id, ct);

            // Q-R2: BIN và số tài khoản không đổi được, vì chứng từ và mã QR đang chờ đều dựa vào chúng.
            var requestedBin = (request.BankBin ?? string.Empty).Trim();
            var requestedAccount = (request.AccountNumber ?? string.Empty).Trim();
            if ((requestedBin.Length > 0 && requestedBin != account.BankBin) ||
                (requestedAccount.Length > 0 && requestedAccount != account.AccountNumber))
            {
                throw new OismException(
                    "PAYMENT_ACCOUNT_IDENTITY_IMMUTABLE",
                    "Không được đổi ngân hàng hoặc số tài khoản của tài khoản đã tạo. Hãy thêm tài khoản mới và ngừng dùng tài khoản cũ.",
                    400);
            }

            account.Rename(request.BankName, request.BankShortName, request.AccountName, userId);
            await SaveAsync(account.BankBin, account.AccountNumber, ct);
            return MapToDto(account);
        }, ct);
    }

    public async Task<PaymentAccountDto> SetDefaultAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        var tenantId = EnsureTenantId();
        EnsureUserId(userId);

        return await ExecuteLockedAsync(tenantId, async () =>
        {
            var account = await FindAsync(tenantId, id, ct);
            if (!account.IsActive)
            {
                throw new OismException("PAYMENT_ACCOUNT_INACTIVE", "Không thể đặt một tài khoản đã ngừng hoạt động làm mặc định.", 400);
            }

            if (account.IsDefault)
            {
                return MapToDto(account);
            }

            await UnsetOtherDefaultsAsync(tenantId, exceptId: id, userId, ct);
            account.SetDefault(userId);
            await SaveAsync(account.BankBin, account.AccountNumber, ct);
            return MapToDto(account);
        }, ct);
    }

    public async Task<PaymentAccountDto> DeactivateAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        var tenantId = EnsureTenantId();
        EnsureUserId(userId);

        return await ExecuteLockedAsync(tenantId, async () =>
        {
            var account = await FindAsync(tenantId, id, ct);
            if (!account.IsActive)
            {
                return MapToDto(account);
            }

            var wasDefault = account.IsDefault;
            account.Deactivate(userId);
            await SaveAsync(account.BankBin, account.AccountNumber, ct);

            // Tài khoản bị ngừng là mặc định thì chuyển mặc định sang tài khoản đang dùng mới nhất (nếu còn).
            if (wasDefault)
            {
                var nextActive = await _context.PaymentAccounts
                    .Where(a => a.TenantId == tenantId && a.Id != id && a.IsActive)
                    .OrderByDescending(a => a.CreatedAt)
                    .FirstOrDefaultAsync(ct);

                if (nextActive != null)
                {
                    nextActive.SetDefault(userId);
                    await SaveAsync(nextActive.BankBin, nextActive.AccountNumber, ct);
                }
            }

            return MapToDto(account);
        }, ct);
    }

    private async Task<TResult> ExecuteLockedAsync<TResult>(Guid tenantId, Func<Task<TResult>> operation, CancellationToken ct)
    {
        return await _context.UnitOfWork.ExecuteWithRetryAsync(async () =>
        {
            await using var tx = await _context.UnitOfWork.BeginTransactionAsync(ct);
            try
            {
                await _lockService.LockPaymentAccountsAsync(tenantId, ct);
                _context.UnitOfWork.ResetTracking();

                var result = await operation();
                await tx.CommitAsync(ct);
                return result;
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        });
    }

    private async Task<PaymentAccount> FindAsync(Guid tenantId, Guid id, CancellationToken ct)
    {
        var account = await _context.PaymentAccounts.FirstOrDefaultAsync(a => a.TenantId == tenantId && a.Id == id, ct);
        return account ?? throw new OismException("PAYMENT_ACCOUNT_NOT_FOUND", $"Không tìm thấy tài khoản thanh toán '{id}'.", 404);
    }

    private async Task UnsetOtherDefaultsAsync(Guid tenantId, Guid? exceptId, Guid userId, CancellationToken ct)
    {
        var others = await _context.PaymentAccounts
            .Where(a => a.TenantId == tenantId && a.IsDefault && (!exceptId.HasValue || a.Id != exceptId.Value))
            .ToListAsync(ct);
        if (others.Count == 0) return;

        foreach (var other in others)
        {
            other.UnsetDefault(userId);
        }
        await _context.SaveChangesAsync(ct);
    }

    private async Task SaveAsync(string bankBin, string accountNumber, CancellationToken ct)
    {
        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (_context.DatabaseErrors.IsUniqueViolation(ex, DuplicateAccountConstraint))
        {
            throw DuplicateAccount(bankBin, accountNumber);
        }
        catch (DbUpdateException ex) when (_context.DatabaseErrors.IsUniqueViolation(ex, DefaultActiveIndex))
        {
            throw new OismException("PAYMENT_ACCOUNT_CONFLICT", "Tài khoản mặc định vừa được thay đổi bởi thao tác khác. Vui lòng tải lại và thử lại.", 409);
        }
    }

    private static OismException DuplicateAccount(string bankBin, string accountNumber) =>
        new("PAYMENT_ACCOUNT_DUPLICATE", $"Tài khoản ngân hàng {bankBin} - {accountNumber} đã tồn tại trong cửa hàng.", 409);

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
