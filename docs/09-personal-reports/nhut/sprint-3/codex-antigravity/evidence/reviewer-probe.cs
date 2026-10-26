using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OISM.Application.Features.Orders.Dtos;
using OISM.Application.Features.Orders.Services;
using OISM.Application.Features.Reports.Dtos;
using OISM.Application.Features.Reports.Services;
using OISM.Application.Features.Simulator.Dtos;
using OISM.Application.Features.Simulator.Services;
using OISM.Domain.Entities;
using OISM.UnitTests;

var observations = new List<object>();
{
    var (db, tenant, tenantId) = TestDbContextFactory.CreateInMemoryContext();
    using var ownedDb = db;
    var branch = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Probe branch" };
    var product = new Product { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Probe product" };
    var sku = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, ProductId = product.Id, Product = product, SKU = "PROBE", RetailPrice = 10 };
    var balance = new InventoryBalance { Id = Guid.NewGuid(), TenantId = tenantId, BranchId = branch.Id, SkuId = sku.Id, OnHand = 1, AverageCost = 5 };
    db.Branches.Add(branch);
    db.Products.Add(product);
    db.Skus.Add(sku);
    db.InventoryBalances.Add(balance);
    await db.SaveChangesAsync();
    var orders = new OrderService(db, tenant.Object);
    var created = await orders.CreateOrderAsync(new CreateOrderDto {
        BranchId = branch.Id, InitialStatus = "Reserved", RequestKey = Guid.NewGuid(),
        Items = new() { new() { SkuId = sku.Id, Quantity = 2, UnitPrice = 10 } }
    }, null);
    observations.Add(new { probe = "reserve-over-stock", provider = "EF InMemory", created.Status,
        requestedQuantity = 2, balance.OnHand, balance.Reserved, reservationRows = await db.Reservations.CountAsync(),
        expected = "Reject insufficient stock; no Reserved order without real reservations" });
    var confirmed = await orders.ConfirmOrderAsync(created.Id, null);
    observations.Add(new { probe = "confirm-over-stock", provider = "EF InMemory", confirmed.Status,
        balance.OnHand, outQuantity = await db.InventoryTransactions.SumAsync(x => x.Quantity),
        emptyWarehouseLedgerRows = await db.InventoryTransactions.CountAsync(x => x.WarehouseId == Guid.Empty),
        expected = "Reject invalid/insufficient allocation, never clamp stock and write invalid warehouse ledger" });
}
{
    var (db, tenant, tenantId) = TestDbContextFactory.CreateInMemoryContext();
    using var ownedDb = db;
    var branch = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Simulator probe" };
    var product = new Product { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Simulator product" };
    var sku = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, ProductId = product.Id, Product = product, SKU = "SIM-PROBE", RetailPrice = 10 };
    db.Branches.Add(branch); db.Products.Add(product); db.Skus.Add(sku);
    await db.SaveChangesAsync();
    var service = new SimulatorService(db, tenant.Object, new OrderService(db, tenant.Object));
    var payload = new WebhookOrderPayloadDto { ExternalOrderId = "SAME-ID", ExternalShopId = "SHOP-A", BranchId = branch.Id,
        Items = new() { new() { SkuCode = sku.SKU, Quantity = 1, UnitPrice = 10 } } };
    var first = await service.IngestWebhookOrderAsync("Shopee", payload, null);
    payload.ExternalShopId = "SHOP-B";
    var second = await service.IngestWebhookOrderAsync("Shopee", payload, null);
    observations.Add(new { probe = "shop-idempotency", provider = "EF InMemory", sameOrderReturned = first.Id == second.Id,
        requestedShop = payload.ExternalShopId, returnedShop = second.ExternalShopId,
        expected = "Different shops have distinct canonical order identities" });
    payload.ExternalShopId = "SHOP-A"; payload.Items[0].Quantity = 3;
    var mismatch = await service.IngestWebhookOrderAsync("Shopee", payload, null);
    observations.Add(new { probe = "payload-mismatch", provider = "EF InMemory", requestedQuantity = 3,
        returnedQuantity = mismatch.Items.Single().Quantity,
        expected = "Same key with different payload must fail with payload mismatch" });
    var batch = await service.RunConcurrencySimulationAsync(new SimulationBatchRequestDto {
        Channel = "Shopee", BranchId = branch.Id, SkuCode = sku.SKU, ConcurrencyCount = 50,
        QuantityPerOrder = 1, UnitPrice = 10
    }, null);
    observations.Add(new { probe = "batch-without-stock", provider = "EF InMemory", batch.TotalRequests,
        batch.SuccessCount, batch.FailedConflictCount, reservationRows = await db.Reservations.CountAsync(),
        note = "Service-level sequential batch; not a PostgreSQL concurrency experiment",
        expected = "No successful reservation when no stock exists" });
}
{
    var (db, tenant, tenantId) = TestDbContextFactory.CreateInMemoryContext();
    using var ownedDb = db;
    var a = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "A" };
    var b = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "B" };
    var product = new Product { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Report product" };
    var sku = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, ProductId = product.Id, Product = product, SKU = "REPORT-PROBE" };
    db.Branches.AddRange(a, b); db.Products.Add(product); db.Skus.Add(sku);
    db.InventoryBalances.AddRange(
        new InventoryBalance { Id = Guid.NewGuid(), TenantId = tenantId, BranchId = a.Id, SkuId = sku.Id, OnHand = 3, AverageCost = 5 },
        new InventoryBalance { Id = Guid.NewGuid(), TenantId = tenantId, BranchId = b.Id, SkuId = sku.Id, OnHand = 7, AverageCost = 5 });
    Order MakeOrder(string number, string channel, string status, decimal quantity) {
        var order = new Order { Id = Guid.NewGuid(), TenantId = tenantId, BranchId = a.Id, Number = number,
            Channel = channel, Status = status, TotalAmount = quantity * 10, CreatedAt = DateTimeOffset.UtcNow };
        order.Items.Add(new OrderItem { Id = Guid.NewGuid(), TenantId = tenantId, OrderId = order.Id,
            SkuId = sku.Id, Quantity = quantity, UnitPrice = 10, CostPrice = 5 });
        return order;
    }
    db.Orders.AddRange(MakeOrder("S", "Shopee", "Completed", 1), MakeOrder("P", "POS", "Completed", 2));
    await db.SaveChangesAsync();
    var service = new ReportService(db, tenant.Object);
    var velocity = await service.GetInventoryVelocityReportAsync(new ReportFilterDto { Channel = "Shopee" });
    observations.Add(new { probe = "velocity-multibranch-channel", provider = "EF InMemory",
        totalOnHand = velocity.TotalOnHandQuantity, bestSellerOnHand = velocity.BestSellers.Single().CurrentOnHand,
        bestSellerSold = velocity.BestSellers.Single().QuantitySold,
        slowMovingRowsForSameSku = velocity.SlowMovingItems.Count(x => x.SkuId == sku.Id),
        slowMovingDays = velocity.SlowMovingItems.Select(x => x.DaysWithoutSale).ToArray(),
        expected = "SKU aggregate: OnHand=10, Shopee sold=1, one row per SKU, elapsed days from real sale time" });
    db.Orders.Add(MakeOrder("C", "Shopee", "Confirmed", 10)); await db.SaveChangesAsync();
    var revenue = await service.GetRevenueProfitReportAsync(new ReportFilterDto());
    observations.Add(new { probe = "revenue-status", provider = "EF InMemory", revenue.TotalNetRevenue, revenue.TotalOrders,
        expectedUnderFrRep01 = "Completed orders only: revenue=30, orders=2; confirm accounting policy before implementation" });
}
Console.WriteLine(JsonSerializer.Serialize(observations, new JsonSerializerOptions { WriteIndented = true }));
