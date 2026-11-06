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
        var service = new PaymentAccountService(context, tenantService);
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
        var service = new PaymentAccountService(context, tenantService);
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
        var service = new PaymentAccountService(context, tenantService);
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
        var service = new PaymentAccountService(context, tenantService);
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
        var service = new PaymentAccountService(context, tenantService);
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

        var service = new PaymentAccountService(context, tenantService);
        var accounts = await service.GetAccountsAsync();
        accounts.Should().BeEmpty();

        var active = await service.GetActiveAccountsAsync();
        active.Should().BeEmpty();
    }
}
