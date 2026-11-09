using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.PaymentAccounts.Dtos;
using OISM.Application.Features.PaymentAccounts.Services;
using OISM.Domain.Entities;
using Xunit;

namespace OISM.UnitTests;

public class PaymentAccountServiceTests
{
    private sealed class MockTenantService : ICurrentTenantService
    {
        public Guid? TenantId { get; set; } = Guid.NewGuid();
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    private static (OISM.Infrastructure.Persistence.AppDbContext Context, MockTenantService TenantService, Guid TenantId) CreateContext()
    {
        var tenantService = new MockTenantService();
        var (context, _, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        tenantService.TenantId = tenantId;
        return (context, tenantService, tenantId);
    }

    [Fact]
    public async Task CreateAccount_FirstAccount_AutomaticallySetsDefault()
    {
        var (context, tenantService, tenantId) = CreateContext();
        var service = new PaymentAccountService(context, tenantService, new OISM.Infrastructure.Services.SqlLockService(context));
        var userId = Guid.NewGuid();

        var result = await service.CreateAccountAsync(new CreatePaymentAccountRequest
        {
            BankBin = "970422",
            BankName = "Ngân hàng TMCP Quân Đội",
            BankShortName = "MB",
            AccountNumber = "0123456789",
            AccountName = "NGUYEN VAN A",
            IsDefault = false // First account should still become default
        }, userId);

        result.BankBin.Should().Be("970422");
        result.BankShortName.Should().Be("MB");
        result.AccountNumber.Should().Be("0123456789");
        result.AccountName.Should().Be("NGUYEN VAN A");
        result.IsDefault.Should().BeTrue();
        result.IsActive.Should().BeTrue();

        var stored = await context.PaymentAccounts.SingleAsync();
        stored.TenantId.Should().Be(tenantId);
        stored.IsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task CreateAccount_DuplicateBinAndNumber_Throws409()
    {
        var (context, tenantService, _) = CreateContext();
        var service = new PaymentAccountService(context, tenantService, new OISM.Infrastructure.Services.SqlLockService(context));
        var userId = Guid.NewGuid();

        await service.CreateAccountAsync(new CreatePaymentAccountRequest
        {
            BankBin = "970422",
            BankName = "MB Bank",
            BankShortName = "MB",
            AccountNumber = "0123456789",
            AccountName = "NGUYEN VAN A"
        }, userId);

        var act = () => service.CreateAccountAsync(new CreatePaymentAccountRequest
        {
            BankBin = "970422",
            BankName = "MB Bank Duplicate",
            BankShortName = "MB",
            AccountNumber = "0123456789",
            AccountName = "NGUYEN VAN A"
        }, userId);

        var ex = await act.Should().ThrowAsync<OismException>();
        ex.Which.ErrorCode.Should().Be("PAYMENT_ACCOUNT_DUPLICATE");
        ex.Which.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task CreateAccount_InvalidBinOrAccountNumber_ThrowsArgumentException()
    {
        var (context, tenantService, _) = CreateContext();
        var service = new PaymentAccountService(context, tenantService, new OISM.Infrastructure.Services.SqlLockService(context));
        var userId = Guid.NewGuid();

        // BIN must be 6 digits
        var act1 = () => service.CreateAccountAsync(new CreatePaymentAccountRequest
        {
            BankBin = "12345",
            BankName = "Bank",
            BankShortName = "B",
            AccountNumber = "12345678",
            AccountName = "A"
        }, userId);
        await act1.Should().ThrowAsync<ArgumentException>();

        // Account number must be 6-19 chars
        var act2 = () => service.CreateAccountAsync(new CreatePaymentAccountRequest
        {
            BankBin = "970422",
            BankName = "Bank",
            BankShortName = "B",
            AccountNumber = "1234",
            AccountName = "A"
        }, userId);
        await act2.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SetDefault_SwitchesDefaultAccountAtomically()
    {
        var (context, tenantService, _) = CreateContext();
        var service = new PaymentAccountService(context, tenantService, new OISM.Infrastructure.Services.SqlLockService(context));
        var userId = Guid.NewGuid();

        var acc1 = await service.CreateAccountAsync(new CreatePaymentAccountRequest
        {
            BankBin = "970422",
            BankName = "MB Bank",
            BankShortName = "MB",
            AccountNumber = "0123456789",
            AccountName = "NGUYEN VAN A"
        }, userId);

        var acc2 = await service.CreateAccountAsync(new CreatePaymentAccountRequest
        {
            BankBin = "970436",
            BankName = "Vietcombank",
            BankShortName = "VCB",
            AccountNumber = "9876543210",
            AccountName = "NGUYEN VAN A",
            IsDefault = false
        }, userId);

        acc1.IsDefault.Should().BeTrue();
        acc2.IsDefault.Should().BeFalse();

        // Set acc2 as default
        var updatedAcc2 = await service.SetDefaultAsync(acc2.Id, userId);
        updatedAcc2.IsDefault.Should().BeTrue();

        var accounts = await service.GetAccountsAsync();
        accounts.Single(a => a.Id == acc1.Id).IsDefault.Should().BeFalse();
        accounts.Single(a => a.Id == acc2.Id).IsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task Deactivate_UnsetsDefault_AndHidesFromActiveList()
    {
        var (context, tenantService, _) = CreateContext();
        var service = new PaymentAccountService(context, tenantService, new OISM.Infrastructure.Services.SqlLockService(context));
        var userId = Guid.NewGuid();

        var acc1 = await service.CreateAccountAsync(new CreatePaymentAccountRequest
        {
            BankBin = "970422",
            BankName = "MB Bank",
            BankShortName = "MB",
            AccountNumber = "0123456789",
            AccountName = "NGUYEN VAN A"
        }, userId);

        var acc2 = await service.CreateAccountAsync(new CreatePaymentAccountRequest
        {
            BankBin = "970436",
            BankName = "Vietcombank",
            BankShortName = "VCB",
            AccountNumber = "9876543210",
            AccountName = "NGUYEN VAN A"
        }, userId);

        // Deactivate acc1 (current default)
        var deactivated = await service.DeactivateAsync(acc1.Id, userId);
        deactivated.IsActive.Should().BeFalse();
        deactivated.IsDefault.Should().BeFalse();

        var activeList = await service.GetActiveAccountsAsync();
        activeList.Should().HaveCount(1);
        activeList[0].Id.Should().Be(acc2.Id);
        activeList[0].IsDefault.Should().BeTrue(); // acc2 promoted to default
    }

    [Fact]
    public async Task TenantIsolation_DoesNotReturnAccountsOfAnotherTenant()
    {
        var (context, tenantService, tenantId) = CreateContext();
        var otherTenantId = Guid.NewGuid();

        // Seed account directly for other tenant
        using (context.OpenPrivilegedScope())
        {
            context.PaymentAccounts.Add(new PaymentAccount
            {
                Id = Guid.NewGuid(),
                TenantId = otherTenantId,
                BankBin = "970415",
                BankName = "VietinBank",
                BankShortName = "CTG",
                AccountNumber = "111222333444",
                AccountName = "OTHER SHOP",
                IsActive = true,
                IsDefault = true
            });
            await context.SaveChangesAsync();
        }

        context.ChangeTracker.Clear();

        var service = new PaymentAccountService(context, tenantService, new OISM.Infrastructure.Services.SqlLockService(context));
        var accounts = await service.GetAccountsAsync();
        accounts.Should().BeEmpty();

        var active = await service.GetActiveAccountsAsync();
        active.Should().BeEmpty();
    }

    private static CreatePaymentAccountRequest MbAccount(bool isDefault = false) => new()
    {
        BankBin = "970422",
        BankName = "MB Bank",
        BankShortName = "MB",
        AccountNumber = "0123456789",
        AccountName = "NGUYEN VAN A",
        IsDefault = isDefault
    };

    [Fact]
    public async Task UpdateAccount_ChangingBinOrAccountNumber_IsRejected_RenameIsAllowed()
    {
        var (context, tenantService, _) = CreateContext();
        var service = new PaymentAccountService(context, tenantService, new OISM.Infrastructure.Services.SqlLockService(context));
        var userId = Guid.NewGuid();
        var created = await service.CreateAccountAsync(MbAccount(), userId);

        var changeNumber = () => service.UpdateAccountAsync(created.Id, new UpdatePaymentAccountRequest
        {
            BankBin = "970422", BankName = "MB Bank", BankShortName = "MB", AccountNumber = "9999999999", AccountName = "NGUYEN VAN A"
        }, userId);
        (await changeNumber.Should().ThrowAsync<OismException>()).Which.ErrorCode.Should().Be("PAYMENT_ACCOUNT_IDENTITY_IMMUTABLE");

        var changeBank = () => service.UpdateAccountAsync(created.Id, new UpdatePaymentAccountRequest
        {
            BankBin = "970436", BankName = "Vietcombank", BankShortName = "VCB", AccountNumber = "0123456789", AccountName = "NGUYEN VAN A"
        }, userId);
        (await changeBank.Should().ThrowAsync<OismException>()).Which.StatusCode.Should().Be(400);

        var renamed = await service.UpdateAccountAsync(created.Id, new UpdatePaymentAccountRequest
        {
            BankBin = "970422", BankName = "Ngân hàng Quân Đội", BankShortName = "mbb", AccountNumber = "0123456789", AccountName = "cua hang a"
        }, userId);
        renamed.AccountNumber.Should().Be("0123456789");
        renamed.BankBin.Should().Be("970422");
        renamed.BankShortName.Should().Be("MBB");
        renamed.AccountName.Should().Be("CUA HANG A");
    }

    [Fact]
    public async Task Mutations_RequireAuthenticatedUserId()
    {
        var (context, tenantService, _) = CreateContext();
        var service = new PaymentAccountService(context, tenantService, new OISM.Infrastructure.Services.SqlLockService(context));

        var create = () => service.CreateAccountAsync(MbAccount(), Guid.Empty);
        await create.Should().ThrowAsync<UnauthorizedAccessException>();
        (await service.GetAccountsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task CreateDefault_ReplacesPreviousDefault_AndLeavesExactlyOneDefault()
    {
        var (context, tenantService, _) = CreateContext();
        var service = new PaymentAccountService(context, tenantService, new OISM.Infrastructure.Services.SqlLockService(context));
        var userId = Guid.NewGuid();

        var first = await service.CreateAccountAsync(MbAccount(), userId);
        var second = await service.CreateAccountAsync(new CreatePaymentAccountRequest
        {
            BankBin = "970436", BankName = "Vietcombank", BankShortName = "VCB", AccountNumber = "9876543210", AccountName = "NGUYEN VAN A", IsDefault = true
        }, userId);

        var accounts = await service.GetAccountsAsync();
        accounts.Count(a => a.IsDefault).Should().Be(1);
        accounts.Single(a => a.IsDefault).Id.Should().Be(second.Id);
        accounts.Single(a => a.Id == first.Id).IsDefault.Should().BeFalse();
    }

    [Fact]
    public async Task SetDefault_InactiveAccount_IsRejected_AndDeactivateTwice_IsIdempotent()
    {
        var (context, tenantService, _) = CreateContext();
        var service = new PaymentAccountService(context, tenantService, new OISM.Infrastructure.Services.SqlLockService(context));
        var userId = Guid.NewGuid();
        var created = await service.CreateAccountAsync(MbAccount(), userId);

        await service.DeactivateAsync(created.Id, userId);
        var again = await service.DeactivateAsync(created.Id, userId);
        again.IsActive.Should().BeFalse();

        var setDefault = () => service.SetDefaultAsync(created.Id, userId);
        (await setDefault.Should().ThrowAsync<OismException>()).Which.ErrorCode.Should().Be("PAYMENT_ACCOUNT_INACTIVE");
        (await service.GetActiveAccountsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task OtherTenantAccount_IsNotFound_ForMutations()
    {
        var (context, tenantService, _) = CreateContext();
        var foreignId = Guid.NewGuid();
        using (context.OpenPrivilegedScope())
        {
            context.PaymentAccounts.Add(new PaymentAccount
            {
                Id = foreignId, TenantId = Guid.NewGuid(), BankBin = "970415", BankName = "VietinBank", BankShortName = "CTG",
                AccountNumber = "111222333444", AccountName = "OTHER SHOP", IsActive = true, IsDefault = true
            });
            await context.SaveChangesAsync();
        }
        context.ChangeTracker.Clear();

        var service = new PaymentAccountService(context, tenantService, new OISM.Infrastructure.Services.SqlLockService(context));
        var userId = Guid.NewGuid();
        foreach (var call in new Func<Task>[]
        {
            () => service.SetDefaultAsync(foreignId, userId),
            () => service.DeactivateAsync(foreignId, userId),
            () => service.UpdateAccountAsync(foreignId, new UpdatePaymentAccountRequest
            {
                BankBin = "970415", BankName = "X", BankShortName = "X", AccountNumber = "111222333444", AccountName = "X"
            }, userId)
        })
        {
            (await call.Should().ThrowAsync<OismException>()).Which.StatusCode.Should().Be(404);
        }
    }
}
