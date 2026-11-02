using FluentAssertions;
using OISM.Application.Features.PriceLists.Dtos;
using OISM.Application.Features.PriceLists.Services;
using OISM.Application.Features.PurchaseOrders.Dtos;
using OISM.Application.Features.PurchaseOrders.Services;
using OISM.Application.Features.StockCountSchedules.Dtos;
using OISM.Application.Features.StockCountSchedules.Services;
using OISM.Application.Features.Units.Dtos;
using OISM.Application.Features.Units.Services;
using OISM.Domain.Entities;
using Xunit;

namespace OISM.UnitTests;

public class UpgradeModulesTests
{
    [Fact]
    public async Task UnitService_CreateAndRetrieveUnits_WorksSuccessfully()
    {
        // Arrange
        var (_, tenantMock, _) = TestDbContextFactory.CreateInMemoryContext();
        var service = new UnitService(tenantMock.Object);

        // Act
        var initialUnits = await service.GetUnitsAsync();
        var created = await service.CreateUnitAsync(new CreateUnitDto
        {
            Name = "Lốc 6 lon",
            Description = "Quy cách đóng gói lốc 6"
        });
        var updatedUnits = await service.GetUnitsAsync();

        // Assert
        initialUnits.Should().NotBeEmpty();
        created.Should().NotBeNull();
        created.Name.Should().Be("Lốc 6 lon");
        updatedUnits.Should().Contain(u => u.Name == "Lốc 6 lon");
    }

    [Fact]
    public async Task SkuUnit_AddConversionRate_CalculatesProperly()
    {
        // Arrange
        var (_, tenantMock, _) = TestDbContextFactory.CreateInMemoryContext();
        var service = new UnitService(tenantMock.Object);
        var units = await service.GetUnitsAsync();
        var boxUnit = units.First(u => u.Name == "Thùng");
        var skuId = Guid.NewGuid();

        // Act
        var skuUnit = await service.AddSkuUnitAsync(skuId, new CreateSkuUnitDto
        {
            UnitId = boxUnit.Id,
            ConversionRate = 24,
            Barcode = "8930000000024",
            SellingPrice = 240000
        });
        var skuUnits = await service.GetSkuUnitsAsync(skuId);

        // Assert
        skuUnit.Should().NotBeNull();
        skuUnit.ConversionRate.Should().Be(24);
        skuUnit.UnitName.Should().Be("Thùng");
        skuUnits.Should().HaveCount(1);
    }

    [Fact]
    public async Task PriceListService_CreateListAndVersion_Succeeds()
    {
        // Arrange
        var (_, tenantMock, _) = TestDbContextFactory.CreateInMemoryContext();
        var service = new PriceListService(tenantMock.Object);

        // Act
        var priceList = await service.CreatePriceListAsync(new CreatePriceListDto
        {
            Code = "PL-SUMMER-2026",
            Name = "Bảng giá khuyến mãi Hè 2026",
            Channel = "Shopee"
        });

        var skuId = Guid.NewGuid();
        var version = await service.CreateVersionAsync(priceList.Id, new CreatePriceVersionDto
        {
            VersionName = "v1.0 - Đợt 1 Hè",
            EffectiveFrom = DateTime.UtcNow,
            EffectiveTo = DateTime.UtcNow.AddMonths(1),
            Items = new List<PriceVersionItemDto>
            {
                new() { SkuId = skuId, SkuCode = "SKU-SUMMER", Price = 89000 }
            }
        });

        var versions = await service.GetVersionsAsync(priceList.Id);

        // Assert
        priceList.Code.Should().Be("PL-SUMMER-2026");
        version.Status.Should().Be("Active");
        version.Items.Should().HaveCount(1);
        versions.Should().HaveCount(1);
    }

    [Fact]
    public async Task PurchaseOrderService_CreateAndStatusUpdate_GeneratesPoNumber()
    {
        // Arrange
        var (context, tenantMock, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var branch = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Chi nhánh Tân Bình" };
        var supplier = new Supplier { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Công ty CP Nước giải khát SG", ContactName = "Anh Tuấn" };
        var sku = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, SKU = "SKU-BEV-01" };

        context.Branches.Add(branch);
        context.Suppliers.Add(supplier);
        context.Skus.Add(sku);
        await context.SaveChangesAsync();

        var service = new PurchaseOrderService(context, tenantMock.Object);

        // Act
        var po = await service.CreatePurchaseOrderAsync(new CreatePurchaseOrderDto
        {
            SupplierId = supplier.Id,
            BranchId = branch.Id,
            ExpectedDeliveryDate = DateTime.UtcNow.AddDays(3),
            Note = "Giao hàng giờ hành chính",
            Items = new List<CreatePurchaseOrderItemDto>
            {
                new() { SkuId = sku.Id, Quantity = 50, UnitCost = 15000 }
            }
        });

        var initialStatus = po.Status;

        var updatedPo = await service.UpdateStatusAsync(po.Id, new UpdatePurchaseOrderStatusDto
        {
            Status = "Ordered",
            Note = "Đã gửi email đơn hàng cho NCC"
        });

        // Assert
        po.Should().NotBeNull();
        po.PoNumber.Should().StartWith("PO-");
        initialStatus.Should().Be("Draft");
        po.TotalAmount.Should().Be(750000); // 50 * 15000

        updatedPo.Should().NotBeNull();
        updatedPo!.Status.Should().Be("Ordered");
        updatedPo.Note.Should().Be("Đã gửi email đơn hàng cho NCC");
    }

    [Fact]
    public async Task StockCountScheduleService_CreateSchedule_ListsProperly()
    {
        // Arrange
        var (context, tenantMock, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var branch = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Chi nhánh Quận 10" };
        context.Branches.Add(branch);
        await context.SaveChangesAsync();

        var service = new StockCountScheduleService(context, tenantMock.Object);

        // Act
        var schedule = await service.CreateScheduleAsync(new CreateStockCountScheduleDto
        {
            BranchId = branch.Id,
            Title = "Kiểm kê định kỳ cuối tuần",
            Frequency = "Weekly",
            StartDate = DateTime.UtcNow.AddDays(5)
        });

        var schedules = await service.GetSchedulesAsync();

        // Assert
        schedule.Should().NotBeNull();
        schedule.Title.Should().Be("Kiểm kê định kỳ cuối tuần");
        schedule.Frequency.Should().Be("Weekly");
        schedules.Should().Contain(s => s.Title == "Kiểm kê định kỳ cuối tuần");
    }
}
