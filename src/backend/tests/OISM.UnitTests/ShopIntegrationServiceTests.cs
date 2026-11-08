using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Exceptions;
using OISM.Application.Features.ShopIntegrations;
using OISM.Application.Features.ShopIntegrations.Services;
using OISM.Application.Features.Webhooks.Services;
using OISM.Domain.Entities;
using Xunit;

namespace OISM.UnitTests;

public class ShopIntegrationServiceTests
{
    private static async Task<(OISM.Infrastructure.Persistence.AppDbContext Context, Guid TenantId, Branch Active, Branch Inactive, Branch OtherTenant)> SeedAsync()
    {
        var (context, _, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var active = new Branch { TenantId = tenantId, Name = "CN Quận 1", IsActive = true };
        var inactive = new Branch { TenantId = tenantId, Name = "CN Đã đóng", IsActive = false };
        var otherTenant = new Branch { TenantId = Guid.NewGuid(), Name = "CN Tenant khác", IsActive = true };

        using (context.OpenPrivilegedScope())
        {
            context.Branches.AddRange(active, inactive, otherTenant);
            await context.SaveChangesAsync();
        }

        context.ChangeTracker.Clear();
        return (context, tenantId, active, inactive, otherTenant);
    }

    [Fact]
    public async Task Create_ReturnsSecretOnce_AndStoresOnlyHash()
    {
        var (context, tenantId, active, _, _) = await SeedAsync();
        var service = new ShopIntegrationService(context);

        var result = await service.CreateIntegrationAsync(new CreateShopIntegrationRequest
        {
            Channel = "shopee",
            ExternalShopId = "  SHOP_001 ",
            BranchId = active.Id
        });

        result.WebhookSecret.Should().StartWith("whsec_").And.HaveLength("whsec_".Length + 64);
        result.Integration.Channel.Should().Be("Shopee");
        result.Integration.ExternalShopId.Should().Be("SHOP_001");
        result.Integration.BranchName.Should().Be("CN Quận 1");
        result.Integration.IsActive.Should().BeTrue();

        var stored = await context.ShopIntegrations.AsNoTracking().SingleAsync();
        stored.TenantId.Should().Be(tenantId);
        stored.SecretHash.Should().NotBe(result.WebhookSecret);
        WebhookSecretHasher.VerifySecret(result.WebhookSecret, stored.SecretHash).Should().BeTrue();
    }

    [Fact]
    public async Task Create_DuplicateShopOnSameChannel_Returns409()
    {
        var (context, _, active, _, _) = await SeedAsync();
        var service = new ShopIntegrationService(context);
        var request = new CreateShopIntegrationRequest { Channel = "TikTok", ExternalShopId = "TT_1", BranchId = active.Id };
        await service.CreateIntegrationAsync(request);

        var act = () => service.CreateIntegrationAsync(new CreateShopIntegrationRequest { Channel = "tiktok", ExternalShopId = "TT_1", BranchId = active.Id });

        var ex = await act.Should().ThrowAsync<OismException>();
        ex.Which.ErrorCode.Should().Be("SHOP_INTEGRATION_DUPLICATE");
        ex.Which.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Create_SameShopIdOnDifferentChannel_IsAllowed()
    {
        var (context, _, active, _, _) = await SeedAsync();
        var service = new ShopIntegrationService(context);
        await service.CreateIntegrationAsync(new CreateShopIntegrationRequest { Channel = "Shopee", ExternalShopId = "S1", BranchId = active.Id });

        var second = await service.CreateIntegrationAsync(new CreateShopIntegrationRequest { Channel = "Lazada", ExternalShopId = "S1", BranchId = active.Id });

        second.Integration.Channel.Should().Be("Lazada");
        (await context.ShopIntegrations.CountAsync()).Should().Be(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Create_InactiveOrOtherTenantBranch_Returns404(bool useOtherTenant)
    {
        var (context, _, _, inactive, otherTenant) = await SeedAsync();
        var service = new ShopIntegrationService(context);

        var act = () => service.CreateIntegrationAsync(new CreateShopIntegrationRequest
        {
            Channel = "Shopee",
            ExternalShopId = "S1",
            BranchId = useOtherTenant ? otherTenant.Id : inactive.Id
        });

        var ex = await act.Should().ThrowAsync<OismException>();
        ex.Which.ErrorCode.Should().Be("BRANCH_INACTIVE_OR_INVALID");
        ex.Which.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Create_UnsupportedChannel_ReturnsValidationError()
    {
        var (context, _, active, _, _) = await SeedAsync();
        var service = new ShopIntegrationService(context);

        var act = () => service.CreateIntegrationAsync(new CreateShopIntegrationRequest { Channel = "Tiki", ExternalShopId = "S1", BranchId = active.Id });

        (await act.Should().ThrowAsync<OismException>()).Which.ErrorCode.Should().Be("VALIDATION_ERROR");
    }

    [Fact]
    public async Task RotateSecret_InvalidatesOldSecret()
    {
        var (context, _, active, _, _) = await SeedAsync();
        var service = new ShopIntegrationService(context);
        var created = await service.CreateIntegrationAsync(new CreateShopIntegrationRequest { Channel = "Shopee", ExternalShopId = "S1", BranchId = active.Id });

        var rotated = await service.RotateSecretAsync(created.Integration.Id);

        rotated.Should().NotBeNull();
        rotated!.WebhookSecret.Should().NotBe(created.WebhookSecret);
        var stored = await context.ShopIntegrations.AsNoTracking().SingleAsync();
        WebhookSecretHasher.VerifySecret(created.WebhookSecret, stored.SecretHash).Should().BeFalse();
        WebhookSecretHasher.VerifySecret(rotated.WebhookSecret, stored.SecretHash).Should().BeTrue();
        stored.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Update_ChangesBranchAndActive_RejectsInactiveTargetBranch()
    {
        var (context, tenantId, active, inactive, _) = await SeedAsync();
        var second = new Branch { TenantId = tenantId, Name = "CN Quận 3", IsActive = true };
        context.Branches.Add(second);
        await context.SaveChangesAsync();
        var service = new ShopIntegrationService(context);
        var created = await service.CreateIntegrationAsync(new CreateShopIntegrationRequest { Channel = "Shopee", ExternalShopId = "S1", BranchId = active.Id });

        var updated = await service.UpdateIntegrationAsync(created.Integration.Id, new UpdateShopIntegrationRequest { BranchId = second.Id, IsActive = false });

        updated!.BranchId.Should().Be(second.Id);
        updated.BranchName.Should().Be("CN Quận 3");
        updated.IsActive.Should().BeFalse();

        var act = () => service.UpdateIntegrationAsync(created.Integration.Id, new UpdateShopIntegrationRequest { BranchId = inactive.Id, IsActive = true });
        (await act.Should().ThrowAsync<OismException>()).Which.ErrorCode.Should().Be("BRANCH_INACTIVE_OR_INVALID");
    }

    [Fact]
    public async Task GetAndMutate_IntegrationOfOtherTenant_ReturnsNull()
    {
        var (context, _, _, _, otherTenant) = await SeedAsync();
        var foreign = new ShopIntegration
        {
            TenantId = otherTenant.TenantId,
            BranchId = otherTenant.Id,
            Channel = "Shopee",
            ExternalShopId = "FOREIGN",
            SecretHash = WebhookSecretHasher.HashSecret("foreign_secret")
        };
        using (context.OpenPrivilegedScope())
        {
            context.ShopIntegrations.Add(foreign);
            await context.SaveChangesAsync();
        }
        context.ChangeTracker.Clear();
        var service = new ShopIntegrationService(context);

        (await service.GetIntegrationsAsync()).Should().BeEmpty();
        (await service.GetIntegrationByIdAsync(foreign.Id)).Should().BeNull();
        (await service.RotateSecretAsync(foreign.Id)).Should().BeNull();
        (await service.UpdateIntegrationAsync(foreign.Id, new UpdateShopIntegrationRequest { BranchId = otherTenant.Id, IsActive = false })).Should().BeNull();
    }

    [Theory]
    [InlineData("", "S1", false)]
    [InlineData("Shopee", "", false)]
    [InlineData("Tiki", "S1", false)]
    [InlineData("LAZADA", "S1", true)]
    public void CreateValidator_ChecksChannelAndShop(string channel, string shopId, bool expectedValid)
    {
        var result = new CreateShopIntegrationValidator().Validate(new CreateShopIntegrationRequest
        {
            Channel = channel,
            ExternalShopId = shopId,
            BranchId = Guid.NewGuid()
        });

        result.IsValid.Should().Be(expectedValid);
    }
}
