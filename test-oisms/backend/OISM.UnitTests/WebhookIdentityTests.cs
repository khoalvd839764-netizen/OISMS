using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OISM.Application.Common.Exceptions;
using OISM.Application.Features.Orders.Services;
using OISM.Application.Features.Webhooks.Services;
using OISM.Domain.Entities;
using Xunit;

namespace OISM.UnitTests;

/// <summary>
/// GET /api/webhooks/simulator/whoami: secret cho biết đúng gian hàng của mình, không lộ dữ liệu tenant.
/// </summary>
public class WebhookIdentityTests
{
    private const string Secret = "whsec_unit_identity";

    private static async Task<(WebhookService Service, OISM.Infrastructure.Persistence.AppDbContext Context, ShopIntegration Integration, Tenant Tenant, Branch Branch)> SetupAsync()
    {
        var tenant = new Tenant { Name = "Shop A", Code = "shop-a", IsActive = true };
        var (context, tenantMock, _) = TestDbContextFactory.CreateInMemoryContext(tenant.Id);
        var branch = new Branch { TenantId = tenant.Id, Name = "CN Quận 1", IsActive = true };
        var integration = new ShopIntegration
        {
            TenantId = tenant.Id,
            BranchId = branch.Id,
            Channel = "TikTok",
            ExternalShopId = "TT_SHOP_1",
            IsActive = true,
            SecretHash = WebhookSecretHasher.HashSecret(Secret)
        };

        using (context.OpenPrivilegedScope())
        {
            context.Tenants.Add(tenant);
            context.Branches.Add(branch);
            context.ShopIntegrations.Add(integration);
            await context.SaveChangesAsync();
        }
        context.ChangeTracker.Clear();

        var service = new WebhookService(context, Mock.Of<IOrderService>(), tenantMock.Object, NullLogger<WebhookService>.Instance);
        return (service, context, integration, tenant, branch);
    }

    private static async Task UpdateAsync(OISM.Infrastructure.Persistence.AppDbContext context, object entity)
    {
        using (context.OpenPrivilegedScope())
        {
            context.Update(entity);
            await context.SaveChangesAsync();
        }
        context.ChangeTracker.Clear();
    }

    [Fact]
    public async Task ValidSecret_ReturnsShopIdentityOnly()
    {
        var (service, _, _, _, _) = await SetupAsync();

        var identity = await service.GetIdentityAsync($"  {Secret} ");

        identity.Channel.Should().Be("TikTok");
        identity.ExternalShopId.Should().Be("TT_SHOP_1");
        identity.BranchName.Should().Be("CN Quận 1");
        identity.BranchIsActive.Should().BeTrue();
        typeof(OISM.Application.Features.Webhooks.Dtos.WebhookIdentityDto).GetProperties().Select(p => p.Name)
            .Should().BeEquivalentTo(["Channel", "ExternalShopId", "BranchName", "BranchIsActive"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("whsec_wrong")]
    public async Task MissingOrWrongSecret_Returns401(string? secret)
    {
        var (service, _, _, _, _) = await SetupAsync();

        var act = () => service.GetIdentityAsync(secret);

        var ex = await act.Should().ThrowAsync<OismException>();
        ex.Which.StatusCode.Should().Be(401);
        ex.Which.ErrorCode.Should().Be("UNAUTHORIZED");
    }

    [Fact]
    public async Task InactiveIntegration_Returns401()
    {
        var (service, context, integration, _, _) = await SetupAsync();
        integration.IsActive = false;
        await UpdateAsync(context, integration);

        var act = () => service.GetIdentityAsync(Secret);

        (await act.Should().ThrowAsync<OismException>()).Which.Message.Should().Contain("vô hiệu hóa");
    }

    [Fact]
    public async Task InactiveTenant_Returns401()
    {
        var (service, context, _, tenant, _) = await SetupAsync();
        tenant.IsActive = false;
        await UpdateAsync(context, tenant);

        var act = () => service.GetIdentityAsync(Secret);

        (await act.Should().ThrowAsync<OismException>()).Which.Message.Should().Contain("ngừng hoạt động");
    }

    [Fact]
    public async Task InactiveBranch_StillIdentifies_ButFlagsBranch()
    {
        var (service, context, _, _, branch) = await SetupAsync();
        branch.IsActive = false;
        await UpdateAsync(context, branch);

        var identity = await service.GetIdentityAsync(Secret);

        identity.BranchIsActive.Should().BeFalse();
        identity.ExternalShopId.Should().Be("TT_SHOP_1");
    }
}
