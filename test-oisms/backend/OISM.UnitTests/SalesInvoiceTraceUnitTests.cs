using FluentAssertions;
using OISM.Application.Features.SalesInvoices.Services;
using OISM.Domain.Common;
using OISM.Domain.Entities;
using Xunit;

namespace OISM.UnitTests;

public class SalesInvoiceTraceUnitTests
{
    [Fact]
    public async Task TraceByOrderId_ShouldIncludeSalesReturns_AndInLedgerLines()
    {
        // Arrange
        var (context, mockTenant, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var service = new SalesInvoiceService(context, mockTenant.Object);

        var branch = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Chi nhánh Quận 1", IsActive = true };
        var warehouse = new Warehouse { Id = Guid.NewGuid(), TenantId = tenantId, BranchId = branch.Id, Name = "Kho Tổng", IsActive = true };
        var sku = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, SKU = "SKU-TRACE", IsActive = true };
        var staff = new User { Id = Guid.NewGuid(), TenantId = tenantId, FullName = "Nhân viên Kho", Email = "staff@test.local", Role = "Staff" };

        context.Branches.Add(branch);
        context.Warehouses.Add(warehouse);
        context.Skus.Add(sku);
        context.Users.Add(staff);

        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Number = "ORD-TRACE-01",
            BranchId = branch.Id,
            Channel = "POS",
            Status = OrderStatus.Completed,
            TotalAmount = 500_000m,
            CreatedAt = DateTimeOffset.UtcNow,
            ConfirmedBy = staff.Id,
            ConfirmedAt = DateTimeOffset.UtcNow
        };
        var orderItem = new OrderItem
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderId = order.Id,
            SkuId = sku.Id,
            Quantity = 5,
            FactorSnapshot = 1m,
            UnitPrice = 100_000m,
            CostPrice = 60_000m,
            UnitNameSnapshot = "Cái"
        };
        order.Items.Add(orderItem);
        context.Orders.Add(order);

        // Dòng sổ OUT khi bán
        var outTx = new InventoryTransaction
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branch.Id,
            WarehouseId = warehouse.Id,
            SkuId = sku.Id,
            Type = "OUT",
            Quantity = -5,
            UnitCost = 60_000m,
            BalanceAfter = 10,
            SequenceNo = 1,
            OrderItemId = orderItem.Id,
            CreatedAt = DateTimeOffset.UtcNow
        };
        context.InventoryTransactions.Add(outTx);

        // Phiếu trả hàng Confirmed
        var salesReturn = new SalesReturn
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Number = "RT-TRACE-01",
            OrderId = order.Id,
            BranchId = branch.Id,
            WarehouseId = warehouse.Id,
            Status = SalesReturnStatus.Confirmed,
            Reason = "Lỗi sản phẩm",
            RefundMethod = "Cash",
            TotalRefundAmount = 200_000m,
            ConfirmedBy = staff.Id,
            Confirmer = staff,
            ConfirmedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var returnItem = new SalesReturnItem
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SalesReturnId = salesReturn.Id,
            OrderId = order.Id,
            OrderItemId = orderItem.Id,
            OrderItem = orderItem,
            SkuId = sku.Id,
            Quantity = 2,
            RefundAmount = 200_000m,
            OriginalUnitCost = 60_000m
        };
        salesReturn.Items.Add(returnItem);
        context.SalesReturns.Add(salesReturn);

        // Dòng sổ IN khi trả hàng
        var inTx = new InventoryTransaction
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branch.Id,
            WarehouseId = warehouse.Id,
            SkuId = sku.Id,
            Type = "IN",
            Quantity = 2,
            UnitCost = 60_000m,
            BalanceAfter = 12,
            SequenceNo = 2,
            SalesReturnItemId = returnItem.Id,
            CreatedAt = DateTimeOffset.UtcNow
        };
        context.InventoryTransactions.Add(inTx);

        await context.SaveChangesAsync();

        // Act 1: Trace theo OrderId
        var trace = await service.TraceByOrderIdAsync(order.Id);

        // Assert 1
        trace.Should().NotBeNull();
        trace.Source.Type.Should().Be("Order");
        trace.Source.Id.Should().Be(order.Id);
        trace.Lines.Should().ContainSingle(l => l.LedgerId == outTx.Id && l.Type == "OUT");

        trace.SalesReturns.Should().HaveCount(1);
        var retDto = trace.SalesReturns[0];
        retDto.Id.Should().Be(salesReturn.Id);
        retDto.Number.Should().Be("RT-TRACE-01");
        retDto.Status.Should().Be("Confirmed");
        retDto.TotalRefundAmount.Should().Be(200_000m);
        retDto.ConfirmedByName.Should().Be("Nhân viên Kho");

        retDto.Lines.Should().ContainSingle();
        var inLine = retDto.Lines[0];
        inLine.LedgerId.Should().Be(inTx.Id);
        inLine.Type.Should().Be("IN");
        inLine.Quantity.Should().Be(2);
        inLine.UnitCost.Should().Be(60_000m);
        inLine.SalesReturnItemId.Should().Be(returnItem.Id);
        inLine.OrderItemQuantity.Should().Be(2);

        // Act 2: Trace theo dòng sổ trả hàng (IN)
        var traceFromLedger = await service.TraceByLedgerIdAsync(inTx.Id);

        // Assert 2: Phải truy vết ngược về đơn hàng gốc và có đủ phiếu trả
        traceFromLedger.Source.Id.Should().Be(order.Id);
        traceFromLedger.SalesReturns.Should().HaveCount(1);
        traceFromLedger.SalesReturns[0].Lines.Should().ContainSingle(l => l.LedgerId == inTx.Id);
    }
}
