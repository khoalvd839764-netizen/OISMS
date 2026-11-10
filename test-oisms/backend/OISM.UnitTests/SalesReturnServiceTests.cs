using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Inventory.Services;
using OISM.Application.Features.SalesReturns.Dtos;
using OISM.Application.Features.SalesReturns.Services;
using OISM.Domain.Common;
using OISM.Domain.Entities;
using Xunit;

namespace OISM.UnitTests;

public class SalesReturnServiceTests
{
    private static (OISM.Infrastructure.Persistence.AppDbContext Context, Mock<ICurrentTenantService> TenantMock, Guid TenantId) CreateContext()
    {
        return TestDbContextFactory.CreateInMemoryContext();
    }

    private static (Order Order, Warehouse Warehouse, OrderItem OrderItem1, OrderItem OrderItem2) SeedOrder(
        OISM.Infrastructure.Persistence.AppDbContext context, Guid tenantId, string channel = "POS", string status = OrderStatus.Confirmed)
    {
        var branch = new Branch
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Chi nhánh Quận 1",
            IsActive = true
        };
        context.Branches.Add(branch);

        var warehouse = new Warehouse
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branch.Id,
            Name = "Kho Tổng Q1",
            IsActive = true
        };
        context.Warehouses.Add(warehouse);

        var sku1 = new ProductVariant
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SKU = "SKU-001",
            RetailPrice = 100_000m
        };
        var sku2 = new ProductVariant
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SKU = "SKU-002",
            RetailPrice = 50_000m
        };
        context.ProductVariants.AddRange(sku1, sku2);

        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branch.Id,
            Number = "ORD-20261109-0001",
            Channel = channel,
            Status = status,
            TotalAmount = 250_000m,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var item1 = new OrderItem
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderId = order.Id,
            SkuId = sku1.Id,
            SkuCodeSnapshot = "SKU-001",
            SkuNameSnapshot = "Sản phẩm A",
            Quantity = 2m,
            FactorSnapshot = 1m,
            UnitPrice = 100_000m,
            CostPrice = 60_000m,
            PriceSource = "PriceList"
        };

        var item2 = new OrderItem
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderId = order.Id,
            SkuId = sku2.Id,
            SkuCodeSnapshot = "SKU-002",
            SkuNameSnapshot = "Sản phẩm B",
            Quantity = 1m,
            FactorSnapshot = 1m,
            UnitPrice = 50_000m,
            CostPrice = 30_000m,
            PriceSource = "PriceList"
        };

        order.Items.Add(item1);
        order.Items.Add(item2);
        context.Orders.Add(order);
        context.SaveChanges();

        return (order, warehouse, item1, item2);
    }

    [Fact]
    public async Task GetReturnableItems_ReturnsCorrectRemainingQuantity_OnlyConfirmedReturnsCount()
    {
        var (context, tenantMock, tenantId) = CreateContext();
        var (order, warehouse, item1, item2) = SeedOrder(context, tenantId);

        // 1 Confirmed return for 1 unit of item1
        var confirmedReturn = new SalesReturn
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RequestKey = Guid.NewGuid(),
            Number = "RT-20261109-0001",
            OrderId = order.Id,
            BranchId = order.BranchId,
            WarehouseId = warehouse.Id,
            Status = SalesReturnStatus.Confirmed,
            Reason = "Khách đổi ý",
            TotalRefundAmount = 100_000m,
            CreatedBy = Guid.NewGuid(),
            ConfirmedBy = Guid.NewGuid(),
            ConfirmedAt = DateTimeOffset.UtcNow
        };
        confirmedReturn.Items.Add(new SalesReturnItem
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SalesReturnId = confirmedReturn.Id,
            OrderId = order.Id,
            OrderItemId = item1.Id,
            SkuId = item1.SkuId,
            Quantity = 1m,
            RefundAmount = 100_000m,
            OriginalUnitCost = 60_000m
        });
        context.SalesReturns.Add(confirmedReturn);

        // 1 Draft return for 1 unit of item2 (should NOT reduce returnable quantity)
        var draftReturn = new SalesReturn
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RequestKey = Guid.NewGuid(),
            Number = "RT-20261109-0002",
            OrderId = order.Id,
            BranchId = order.BranchId,
            WarehouseId = warehouse.Id,
            Status = SalesReturnStatus.Draft,
            Reason = "Lỗi sản phẩm",
            TotalRefundAmount = 50_000m,
            CreatedBy = Guid.NewGuid()
        };
        draftReturn.Items.Add(new SalesReturnItem
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SalesReturnId = draftReturn.Id,
            OrderId = order.Id,
            OrderItemId = item2.Id,
            SkuId = item2.SkuId,
            Quantity = 1m,
            RefundAmount = 50_000m,
            OriginalUnitCost = 30_000m
        });
        context.SalesReturns.Add(draftReturn);
        await context.SaveChangesAsync();

        var service = new SalesReturnService(context, tenantMock.Object, new WacCalculator());

        var items = await service.GetReturnableItemsAsync(order.Id);

        items.Should().HaveCount(2);

        var rItem1 = items.Single(i => i.OrderItemId == item1.Id);
        rItem1.SoldQuantity.Should().Be(2m);
        rItem1.ReturnedQuantity.Should().Be(1m);
        rItem1.ReturnableQuantity.Should().Be(1m);

        var rItem2 = items.Single(i => i.OrderItemId == item2.Id);
        rItem2.SoldQuantity.Should().Be(1m);
        rItem2.ReturnedQuantity.Should().Be(0m);
        rItem2.ReturnableQuantity.Should().Be(1m);
    }

    [Fact]
    public async Task CreateAsync_DefaultRefundAmount_CalculatedFromQuantityTimesUnitPrice()
    {
        var (context, tenantMock, tenantId) = CreateContext();
        var (order, warehouse, item1, _) = SeedOrder(context, tenantId);

        var service = new SalesReturnService(context, tenantMock.Object, new WacCalculator());

        var requestKey = Guid.NewGuid();
        var (result, isReplay) = await service.CreateAsync(new CreateSalesReturnDto
        {
            OrderId = order.Id,
            WarehouseId = warehouse.Id,
            Reason = "Hàng lỗi",
            Items =
            [
                new CreateSalesReturnItemDto
                {
                    OrderItemId = item1.Id,
                    Quantity = 2m,
                    RefundAmount = null // Omitted -> defaults to 2 * 100_000 = 200_000
                }
            ]
        }, requestKey, Guid.NewGuid());

        isReplay.Should().BeFalse();
        result.TotalRefundAmount.Should().Be(200_000m);
        result.Items.Single().RefundAmount.Should().Be(200_000m);
        result.Status.Should().Be(SalesReturnStatus.Draft);
    }

    [Theory]
    [InlineData(1.0001, null)]
    [InlineData(1, 50_000.001)]
    public async Task CreateAsync_TooManyDecimalPlaces_Throws400(double quantity, double? refundAmount)
    {
        var (context, tenantMock, tenantId) = CreateContext();
        var (order, warehouse, item1, _) = SeedOrder(context, tenantId);
        var service = new SalesReturnService(context, tenantMock.Object, new WacCalculator());

        var act = () => service.CreateAsync(new CreateSalesReturnDto
        {
            OrderId = order.Id,
            WarehouseId = warehouse.Id,
            Reason = "Hàng lỗi",
            Items =
            [
                new CreateSalesReturnItemDto
                {
                    OrderItemId = item1.Id,
                    Quantity = (decimal)quantity,
                    RefundAmount = refundAmount.HasValue ? (decimal)refundAmount.Value : null
                }
            ]
        }, Guid.NewGuid(), Guid.NewGuid());

        var ex = await act.Should().ThrowAsync<OismException>();
        ex.Which.ErrorCode.Should().Be("VALIDATION_ERROR");
        ex.Which.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task CreateAsync_CustomRefundAmount_AllowedWhenLowerThanMax()
    {
        var (context, tenantMock, tenantId) = CreateContext();
        var (order, warehouse, item1, _) = SeedOrder(context, tenantId);

        var service = new SalesReturnService(context, tenantMock.Object, new WacCalculator());

        var (result, _) = await service.CreateAsync(new CreateSalesReturnDto
        {
            OrderId = order.Id,
            WarehouseId = warehouse.Id,
            Reason = "Khách đổi ý (thu phí 20k)",
            Items =
            [
                new CreateSalesReturnItemDto
                {
                    OrderItemId = item1.Id,
                    Quantity = 1m,
                    RefundAmount = 80_000m // Max is 100_000 -> 80_000 is allowed
                }
            ]
        }, Guid.NewGuid(), Guid.NewGuid());

        result.TotalRefundAmount.Should().Be(80_000m);
        result.Items.Single().RefundAmount.Should().Be(80_000m);
    }

    [Fact]
    public async Task CreateAsync_RefundAmountExceededLinePrice_Throws422()
    {
        var (context, tenantMock, tenantId) = CreateContext();
        var (order, warehouse, item1, _) = SeedOrder(context, tenantId);

        var service = new SalesReturnService(context, tenantMock.Object, new WacCalculator());

        var act = () => service.CreateAsync(new CreateSalesReturnDto
        {
            OrderId = order.Id,
            WarehouseId = warehouse.Id,
            Reason = "Hoàn dư tiền",
            Items =
            [
                new CreateSalesReturnItemDto
                {
                    OrderItemId = item1.Id,
                    Quantity = 1m,
                    RefundAmount = 150_000m // Max is 100_000
                }
            ]
        }, Guid.NewGuid(), Guid.NewGuid());

        var ex = await act.Should().ThrowAsync<OismException>();
        ex.Which.ErrorCode.Should().Be("REFUND_AMOUNT_EXCEEDED");
        ex.Which.StatusCode.Should().Be(422);
    }

    [Fact]
    public async Task CreateAsync_TotalRefundExceededOrderTotal_Throws422()
    {
        var (context, tenantMock, tenantId) = CreateContext();
        var (order, warehouse, item1, item2) = SeedOrder(context, tenantId);

        // Previous confirmed return total = 200_000
        var prevReturn = new SalesReturn
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RequestKey = Guid.NewGuid(),
            Number = "RT-20261109-0001",
            OrderId = order.Id,
            BranchId = order.BranchId,
            WarehouseId = warehouse.Id,
            Status = SalesReturnStatus.Confirmed,
            Reason = "Đợt 1",
            TotalRefundAmount = 200_000m,
            CreatedBy = Guid.NewGuid(),
            ConfirmedBy = Guid.NewGuid(),
            ConfirmedAt = DateTimeOffset.UtcNow
        };
        prevReturn.Items.Add(new SalesReturnItem
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SalesReturnId = prevReturn.Id,
            OrderId = order.Id,
            OrderItemId = item1.Id,
            SkuId = item1.SkuId,
            Quantity = 2m,
            RefundAmount = 200_000m,
            OriginalUnitCost = 60_000m
        });
        context.SalesReturns.Add(prevReturn);
        await context.SaveChangesAsync();

        // Total order is 250_000. Already refunded 200_000. New return wants 60_000 -> 260_000 > 250_000!
        var service = new SalesReturnService(context, tenantMock.Object, new WacCalculator());

        var act = () => service.CreateAsync(new CreateSalesReturnDto
        {
            OrderId = order.Id,
            WarehouseId = warehouse.Id,
            Reason = "Đợt 2",
            Items =
            [
                new CreateSalesReturnItemDto
                {
                    OrderItemId = item2.Id,
                    Quantity = 1m,
                    RefundAmount = 60_000m // Exceeds line and order total
                }
            ]
        }, Guid.NewGuid(), Guid.NewGuid());

        var ex = await act.Should().ThrowAsync<OismException>();
        ex.Which.ErrorCode.Should().Be("REFUND_AMOUNT_EXCEEDED");
    }

    [Fact]
    public async Task CreateAsync_MarketplaceOrder_MustHaveZeroRefund()
    {
        var (context, tenantMock, tenantId) = CreateContext();
        var (order, warehouse, item1, _) = SeedOrder(context, tenantId, channel: "Shopee");

        var service = new SalesReturnService(context, tenantMock.Object, new WacCalculator());

        // 1. If user passes refund > 0 -> throws 422 REFUND_AMOUNT_EXCEEDED
        var act = () => service.CreateAsync(new CreateSalesReturnDto
        {
            OrderId = order.Id,
            WarehouseId = warehouse.Id,
            Reason = "Khách trả hàng sàn",
            Items =
            [
                new CreateSalesReturnItemDto
                {
                    OrderItemId = item1.Id,
                    Quantity = 1m,
                    RefundAmount = 50_000m
                }
            ]
        }, Guid.NewGuid(), Guid.NewGuid());

        var ex = await act.Should().ThrowAsync<OismException>();
        ex.Which.ErrorCode.Should().Be("REFUND_AMOUNT_EXCEEDED");

        // 2. If user leaves RefundAmount null -> sets 0 automatically
        var (res, _) = await service.CreateAsync(new CreateSalesReturnDto
        {
            OrderId = order.Id,
            WarehouseId = warehouse.Id,
            Reason = "Khách trả hàng sàn",
            Items =
            [
                new CreateSalesReturnItemDto
                {
                    OrderItemId = item1.Id,
                    Quantity = 1m,
                    RefundAmount = null
                }
            ]
        }, Guid.NewGuid(), Guid.NewGuid());

        res.TotalRefundAmount.Should().Be(0m);
        res.Items.Single().RefundAmount.Should().Be(0m);
    }

    [Fact]
    public async Task CreateAsync_QuantityExceededReturnable_Throws422()
    {
        var (context, tenantMock, tenantId) = CreateContext();
        var (order, warehouse, item1, _) = SeedOrder(context, tenantId);

        var service = new SalesReturnService(context, tenantMock.Object, new WacCalculator());

        // Sold was 2, tries to return 3
        var act = () => service.CreateAsync(new CreateSalesReturnDto
        {
            OrderId = order.Id,
            WarehouseId = warehouse.Id,
            Reason = "Trả quá số bán",
            Items =
            [
                new CreateSalesReturnItemDto
                {
                    OrderItemId = item1.Id,
                    Quantity = 3m
                }
            ]
        }, Guid.NewGuid(), Guid.NewGuid());

        var ex = await act.Should().ThrowAsync<OismException>();
        ex.Which.ErrorCode.Should().Be("RETURN_QUANTITY_EXCEEDED");
        ex.Which.StatusCode.Should().Be(422);
    }

    [Fact]
    public async Task CreateAsync_WarehouseMismatchBranch_Throws422()
    {
        var (context, tenantMock, tenantId) = CreateContext();
        var (order, _, item1, _) = SeedOrder(context, tenantId);

        // Create warehouse belonging to another branch
        var otherBranch = new Branch
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Chi nhánh Quận 3",
            IsActive = true
        };
        var otherWarehouse = new Warehouse
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = otherBranch.Id,
            Name = "Kho Quận 3",
            IsActive = true
        };
        context.Branches.Add(otherBranch);
        context.Warehouses.Add(otherWarehouse);
        await context.SaveChangesAsync();

        var service = new SalesReturnService(context, tenantMock.Object, new WacCalculator());

        var act = () => service.CreateAsync(new CreateSalesReturnDto
        {
            OrderId = order.Id,
            WarehouseId = otherWarehouse.Id,
            Reason = "Trả về kho khác chi nhánh",
            Items =
            [
                new CreateSalesReturnItemDto
                {
                    OrderItemId = item1.Id,
                    Quantity = 1m
                }
            ]
        }, Guid.NewGuid(), Guid.NewGuid());

        var ex = await act.Should().ThrowAsync<OismException>();
        ex.Which.ErrorCode.Should().Be("RETURN_WAREHOUSE_MISMATCH");
        ex.Which.StatusCode.Should().Be(422);
    }

    [Fact]
    public async Task CancelAsync_OnlyDraftCanBeCancelled()
    {
        var (context, tenantMock, tenantId) = CreateContext();
        var (order, warehouse, item1, _) = SeedOrder(context, tenantId);

        var service = new SalesReturnService(context, tenantMock.Object, new WacCalculator());

        var (draft, _) = await service.CreateAsync(new CreateSalesReturnDto
        {
            OrderId = order.Id,
            WarehouseId = warehouse.Id,
            Reason = "Lập nhầm",
            Items = [new CreateSalesReturnItemDto { OrderItemId = item1.Id, Quantity = 1m }]
        }, Guid.NewGuid(), Guid.NewGuid());

        // Cancel draft -> OK
        var cancelled = await service.CancelAsync(draft.Id, Guid.NewGuid());
        cancelled.Status.Should().Be(SalesReturnStatus.Cancelled);

        // Cancel again -> 409 INVALID_STATE_TRANSITION
        var act = () => service.CancelAsync(draft.Id, Guid.NewGuid());
        var ex = await act.Should().ThrowAsync<OismException>();
        ex.Which.ErrorCode.Should().Be("INVALID_STATE_TRANSITION");
        ex.Which.StatusCode.Should().Be(409);
    }
}
