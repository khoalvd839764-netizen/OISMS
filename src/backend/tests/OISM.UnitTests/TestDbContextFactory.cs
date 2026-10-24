using Microsoft.EntityFrameworkCore;
using Moq;
using OISM.Application.Common.Interfaces;
using OISM.Infrastructure.Persistence;

namespace OISM.UnitTests;

public static class TestDbContextFactory
{
    public static (AppDbContext context, Mock<ICurrentTenantService> tenantServiceMock, Guid tenantId) CreateInMemoryContext(Guid? tenantId = null)
    {
        var resolvedTenantId = tenantId ?? Guid.NewGuid();
        var mock = new Mock<ICurrentTenantService>();
        mock.Setup(t => t.TenantId).Returns(resolvedTenantId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var context = new AppDbContext(options, mock.Object);
        return (context, mock, resolvedTenantId);
    }
}
