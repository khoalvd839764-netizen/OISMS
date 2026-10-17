using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OISM.Application.Common.Interfaces;
using OISM.Domain.Entities;
using OISM.Infrastructure.Persistence;

namespace OISM.IntegrationTests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = Guid.NewGuid().ToString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var descriptors = services.Where(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                     d.ServiceType == typeof(AppDbContext) ||
                     d.ServiceType == typeof(IAppDbContext)).ToList();

            foreach (var descriptor in descriptors)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseInMemoryDatabase(_databaseName);
                options.ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning));
            });

            services.AddScoped<IAppDbContext>(provider => provider.GetRequiredService<AppDbContext>());

            // Seed dữ liệu mẫu Roles vào In-Memory Db
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
            if (!db.Roles.Any())
            {
                db.Roles.AddRange(
                    new Role { Name = "Chủ cửa hàng", Code = "Owner", Description = "Toàn quyền quản trị cửa hàng" },
                    new Role { Name = "Quản lý / Nhân viên kho", Code = "Staff", Description = "Quản lý đơn hàng và nghiệp vụ kho" },
                    new Role { Name = "Thu ngân POS", Code = "Cashier", Description = "Bán hàng và thu tiền tại quầy" }
                );
                db.SaveChanges();
            }
        });
    }
}
