using Microsoft.Extensions.DependencyInjection;
using OISM.Application.Features.Auth.Services;
using OISM.Application.Features.Branches.Services;
using OISM.Application.Features.Catalog.Brands.Services;
using OISM.Application.Features.Catalog.Categories.Services;
using OISM.Application.Features.Catalog.Products.Services;
using OISM.Application.Features.Inventory.Services;
using OISM.Application.Features.Orders.Services;
using OISM.Application.Features.Pos;
using OISM.Application.Features.Reports.Services;
using OISM.Application.Features.Simulator.Services;
using OISM.Application.Features.StockCounts.Services;
using OISM.Application.Features.StockTransfers.Services;
using OISM.Application.Features.Suppliers.Services;
using OISM.Application.Features.Warehouses.Services;
using OISM.Application.Features.Webhooks.Services;

namespace OISM.Application;

/// <summary>
/// Đăng ký các dịch vụ thuộc tầng Application tuân thủ Clean Architecture.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton<IWacCalculator, WacCalculator>();
        services.AddScoped<IGoodsReceiptService, GoodsReceiptService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<IReservationService, ReservationService>();
        services.AddScoped<IPosCheckoutService, OrderService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<ISimulatorService, SimulatorService>();
        services.AddScoped<IStockTransferService, StockTransferService>();
        services.AddScoped<IStockCountService, StockCountService>();
        services.AddScoped<IWebhookService, WebhookService>();

        // Catalog & Management services
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IBrandService, BrandService>();
        services.AddScoped<IBranchService, BranchService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<ISupplierService, SupplierService>();
        services.AddScoped<IWarehouseService, WarehouseService>();
        services.AddScoped<IPosCatalogService, PosCatalogService>();

        return services;
    }
}
