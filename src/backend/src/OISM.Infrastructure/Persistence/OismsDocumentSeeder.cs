using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Inventory.Dtos;
using OISM.Application.Features.Inventory.Services;
using OISM.Application.Features.Orders.Dtos;
using OISM.Application.Features.Orders.Services;
using OISM.Application.Features.PurchaseOrders.Dtos;
using OISM.Application.Features.PurchaseOrders.Services;
using OISM.Application.Features.SalesReturns.Dtos;
using OISM.Application.Features.SalesReturns.Services;
using OISM.Application.Features.StockCounts.Dtos;
using OISM.Application.Features.StockCounts.Services;
using OISM.Application.Features.StockTransfers.Dtos;
using OISM.Application.Features.StockTransfers.Services;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Persistence;

/// <summary>
/// Khởi tạo dữ liệu chứng từ mẫu qua các Application service thật (trong transaction và khóa thật)
/// cho môi trường Development/Test sau khi OismsDatabaseSeeder tạo xong danh mục (Upgrade 14 - Q12).
/// Hoàn toàn idempotent: chạy lại nhiều lần không tạo trùng chứng từ hay dòng sổ kho.
/// </summary>
public static class OismsDocumentSeeder
{
    public static async Task SeedAsync(IServiceProvider rootServiceProvider, CancellationToken ct = default)
    {
        var tenants = new[]
        {
            (TenantId: OismsDatabaseSeeder.TenantAId, SecondWhName: "Kho Phụ Quận 1", SecondSkuCode: "POLO-NAM-TRANG-L", SecondBarcode: "POLONAMTRANGL", SecondAttrs: "{\"color\":\"Trắng\",\"size\":\"L\",\"variant_name\":\"Trắng / Size L\"}"),
            (TenantId: OismsDatabaseSeeder.TenantBId, SecondWhName: "Kho Phụ Đống Đa", SecondSkuCode: "SERUM-B5-50ML", SecondBarcode: "SERUMB550ML", SecondAttrs: "{\"capacity\":\"50ml\",\"variant_name\":\"Chai 50ml\"}"),
        };

        foreach (var cfg in tenants)
        {
            using var scope = rootServiceProvider.CreateScope();
            var sp = scope.ServiceProvider;
            var tenantService = sp.GetRequiredService<ICurrentTenantService>();
            tenantService.SetTenant(cfg.TenantId);

            var logger = sp.GetService<ILoggerFactory>()?.CreateLogger("OismsDocumentSeeder");
            await SeedTenantDocumentsAsync(sp, cfg.TenantId, cfg.SecondWhName, cfg.SecondSkuCode, cfg.SecondBarcode, cfg.SecondAttrs, logger, ct);
        }
    }

    private static async Task SeedTenantDocumentsAsync(
        IServiceProvider sp,
        Guid tenantId,
        string secondWarehouseName,
        string secondSkuCode,
        string secondBarcode,
        string secondAttrs,
        ILogger? logger,
        CancellationToken ct)
    {
        var db = sp.GetRequiredService<AppDbContext>();

        var owner = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.TenantId == tenantId && u.Role == "Owner", ct);
        var cashier = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.TenantId == tenantId && u.Role == "Cashier", ct) ?? owner;
        var branch = await db.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.TenantId == tenantId && b.IsActive, ct);
        var supplier = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.TenantId == tenantId && s.IsActive, ct);
        var sku1 = await db.ProductVariants.AsNoTracking().FirstOrDefaultAsync(v => v.TenantId == tenantId && v.IsActive, ct);

        if (owner == null || cashier == null || branch == null || supplier == null || sku1 == null)
        {
            logger?.LogWarning("Bỏ qua seed chứng từ cho tenant {TenantId} do thiếu dữ liệu danh mục nền.", tenantId);
            return;
        }

        // Bảo đảm có 2 kho để thực hiện chuyển kho 2 bước
        var warehouses = await db.Warehouses.AsNoTracking()
            .Where(w => w.TenantId == tenantId && w.BranchId == branch.Id && w.IsActive)
            .OrderBy(w => w.CreatedAt)
            .ToListAsync(ct);

        if (warehouses.Count == 0) return;

        var wh1 = warehouses[0];
        Warehouse wh2;
        if (warehouses.Count > 1)
        {
            wh2 = warehouses[1];
        }
        else
        {
            var secondWhId = DeterministicGuid(tenantId, "u14-warehouse-2");
            wh2 = await db.Warehouses.FirstOrDefaultAsync(w => w.TenantId == tenantId && w.Id == secondWhId, ct)
                ?? new Warehouse
                {
                    Id = secondWhId,
                    TenantId = tenantId,
                    BranchId = branch.Id,
                    Name = secondWarehouseName,
                    IsActive = true,
                    CreatedAt = DateTimeOffset.UtcNow
                };
            if (!await db.Warehouses.AnyAsync(w => w.TenantId == tenantId && w.Id == secondWhId, ct))
            {
                db.Warehouses.Add(wh2);
                await db.SaveChangesAsync(ct);
            }
        }

        // Bảo đảm có SKU thứ 2 để kiểm kê có cả chênh lệch thừa (+) và thiếu (-)
        var sku2Id = DeterministicGuid(tenantId, "u14-sku-2");
        var sku2 = await db.ProductVariants.AsNoTracking()
            .FirstOrDefaultAsync(v => v.TenantId == tenantId && v.Id != sku1.Id && v.IsActive, ct);

        if (sku2 == null)
        {
            var baseUnitId = sku1.BaseUnitId ?? await db.Units.Where(u => u.TenantId == tenantId).Select(u => u.Id).FirstAsync(ct);
            var existingSku2 = await db.ProductVariants.AsNoTracking().FirstOrDefaultAsync(v => v.TenantId == tenantId && v.Id == sku2Id, ct);
            if (existingSku2 == null)
            {
                existingSku2 = new ProductVariant
                {
                    Id = sku2Id,
                    TenantId = tenantId,
                    ProductId = sku1.ProductId,
                    Code = secondSkuCode,
                    Unit = sku1.Unit,
                    BaseUnitId = baseUnitId,
                    BarcodeType = "Code128",
                    Barcode = secondBarcode,
                    RetailPrice = Math.Max(150000m, sku1.RetailPrice - 20000m),
                    WholesalePrice = Math.Max(100000m, sku1.WholesalePrice - 20000m),
                    AttributesJson = secondAttrs,
                    IsActive = true,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                db.ProductVariants.Add(existingSku2);
                await db.SaveChangesAsync(ct);
            }

            if (!await db.SkuUnits.AnyAsync(su => su.TenantId == tenantId && su.SkuId == sku2Id && su.UnitId == baseUnitId, ct))
            {
                db.SkuUnits.Add(new SkuUnit
                {
                    Id = DeterministicGuid(tenantId, "u14-skuunit-2"),
                    TenantId = tenantId,
                    SkuId = sku2Id,
                    UnitId = baseUnitId,
                    FactorToBase = 1m,
                    CanPurchase = true,
                    CanSell = true,
                    IsDefaultPurchase = true,
                    IsDefaultSale = true,
                    QuantityDecimalPlaces = 3,
                    Version = 1,
                    IsActive = true,
                    CreatedAt = DateTimeOffset.UtcNow
                });
                await db.SaveChangesAsync(ct);
            }

            var openReq = await db.PriceChangeRequests.AsNoTracking()
                .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Kind == "Opening", ct);
            if (openReq == null)
            {
                openReq = new PriceChangeRequest
                {
                    Id = DeterministicGuid(tenantId, "u14-opening-pcr"),
                    TenantId = tenantId,
                    RequestKey = DeterministicGuid(tenantId, "u14-opening-pcr-key"),
                    Kind = "Opening",
                    Status = "Approved",
                    Reason = "Giá đầu kỳ",
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
                    ReviewedAt = DateTimeOffset.UtcNow.AddDays(-1)
                };
                db.PriceChangeRequests.Add(openReq);
                await db.SaveChangesAsync(ct);
            }

            var priceLists = await db.PriceLists.AsNoTracking().Where(p => p.TenantId == tenantId).ToListAsync(ct);
            foreach (var pl in priceLists)
            {
                var hasPrice = await db.SkuUnitPrices.AnyAsync(
                    p => p.TenantId == tenantId && p.PriceListId == pl.Id && p.SkuId == sku2Id && p.UnitId == baseUnitId, ct);
                if (!hasPrice)
                {
                    var priceVal = pl.Code == "wholesale" ? existingSku2.WholesalePrice : existingSku2.RetailPrice;
                    var reqItemId = DeterministicGuid(tenantId, $"u14-pcr-item-{pl.Code}");
                    if (!await db.PriceChangeRequestItems.AnyAsync(i => i.TenantId == tenantId && i.Id == reqItemId, ct))
                    {
                        db.PriceChangeRequestItems.Add(new PriceChangeRequestItem
                        {
                            Id = reqItemId,
                            TenantId = tenantId,
                            RequestId = openReq.Id,
                            PriceListId = pl.Id,
                            SkuId = sku2Id,
                            UnitId = baseUnitId,
                            NewPrice = priceVal,
                            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
                        });
                    }
                    db.SkuUnitPrices.Add(new SkuUnitPrice
                    {
                        Id = DeterministicGuid(tenantId, $"u14-price-{pl.Code}"),
                        TenantId = tenantId,
                        PriceListId = pl.Id,
                        SkuId = sku2Id,
                        UnitId = baseUnitId,
                        Price = priceVal,
                        ValidFrom = DateTimeOffset.UtcNow.AddMinutes(-5),
                        SourceItemId = reqItemId,
                        CreatedAt = DateTimeOffset.UtcNow
                    });
                }
            }
            await db.SaveChangesAsync(ct);
            sku2 = existingSku2;
        }

        // 1. Đơn mua duyệt -> Phiếu nhập xác nhận (WAC)
        var poKey = DeterministicGuid(tenantId, "u14-po-1");
        var poEntity = await db.PurchaseOrders.AsNoTracking()
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.RequestKey == poKey, ct);

        if (poEntity == null)
        {
            var poService = sp.GetRequiredService<IPurchaseOrderService>();
            var poDto = await poService.CreateAsync(new CreatePurchaseOrderDto
            {
                BranchId = branch.Id,
                SupplierId = supplier.Id,
                OrderDate = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)),
                Note = "Đơn mua mẫu Upgrade 14",
                Items = new List<CreatePurchaseOrderItemDto>
                {
                    new() { SkuId = sku1.Id, Quantity = 40m, UnitPrice = 120000m },
                    new() { SkuId = sku2.Id, Quantity = 30m, UnitPrice = 80000m }
                }
            }, poKey, owner.Id, ct, isOwner: true);

            await poService.ApproveAsync(poDto.Id, confirmZeroPrice: false, owner.Id, ct, version: poDto.Version, confirmLossPrice: false);

            poEntity = await db.PurchaseOrders.AsNoTracking()
                .Include(p => p.Items)
                .FirstAsync(p => p.TenantId == tenantId && p.Id == poDto.Id, ct);
        }

        var grExists = await db.GoodsReceipts.AsNoTracking()
            .AnyAsync(g => g.TenantId == tenantId && g.PurchaseOrderId == poEntity.Id, ct);
        if (!grExists)
        {
            var grService = sp.GetRequiredService<IGoodsReceiptService>();
            var grDto = await grService.CreateReceiptAsync(new CreateGoodsReceiptDto
            {
                PurchaseOrderId = poEntity.Id,
                WarehouseId = wh1.Id,
                Note = "Phiếu nhập kho mẫu Upgrade 14",
                Items = poEntity.Items.Select(i => new CreateGoodsReceiptItemDto
                {
                    PurchaseOrderItemId = i.Id,
                    Quantity = i.Quantity
                }).ToList()
            }, owner.Id, ct, isOwner: true);

            await grService.ConfirmReceiptAsync(grDto.Id, owner.Id, ct, isOwner: true);
        }

        // 2. POS checkout -> sinh Order, SalesInvoice, GoodsIssue 02-VT, Ledger OUT
        var posKey = DeterministicGuid(tenantId, "u14-pos-1");
        var posOrder = await db.Orders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.RequestKey == posKey, ct);

        if (posOrder == null)
        {
            var posService = sp.GetRequiredService<IPosCheckoutService>();
            var posResult = await posService.CheckoutPosAsync(new PosCheckoutRequestDto
            {
                BranchId = branch.Id,
                RequestKey = posKey,
                PaymentMethod = "Cash",
                AmountGiven = sku1.RetailPrice * 3m,
                CustomerName = "Khách lẻ POS Demo",
                Items = new List<OrderItemRequestDto>
                {
                    new() { SkuId = sku1.Id, SkuCode = sku1.SKU, Quantity = 3m, UnitPrice = sku1.RetailPrice }
                }
            }, cashier.Id, ct);

            posOrder = await db.Orders.AsNoTracking()
                .FirstAsync(o => o.TenantId == tenantId && o.Id == posResult.OrderId, ct);
        }

        // 3. Đơn online confirm -> sinh Order, SalesInvoice, GoodsIssue 02-VT, Ledger OUT
        var onlineKey = DeterministicGuid(tenantId, "u14-online-1");
        var onlineOrderExists = await db.Orders.AsNoTracking()
            .AnyAsync(o => o.TenantId == tenantId && o.RequestKey == onlineKey, ct);
        if (!onlineOrderExists)
        {
            var orderService = sp.GetRequiredService<IOrderService>();
            var createdOnline = await orderService.CreateOrderAsync(new CreateOrderRequestDto
            {
                BranchId = branch.Id,
                Channel = "Admin",
                RequestKey = onlineKey,
                CustomerName = "Khách Online Demo",
                CustomerPhone = "0901234567",
                ShippingAddress = "123 Lê Lợi, Q.1",
                Items = new List<OrderItemRequestDto>
                {
                    new() { SkuId = sku1.Id, SkuCode = sku1.SKU, Quantity = 2m, UnitPrice = sku1.RetailPrice }
                }
            }, owner.Id, ct);

            await orderService.ConfirmOrderAsync(createdOnline.Id, new ConfirmOrderRequestDto
            {
                ConfirmedBy = owner.Id,
                Note = "Xác nhận đơn online mẫu Upgrade 14"
            }, owner.Id, ct);
        }

        // 4. Chuyển kho 2 bước giữa hai kho (OUT kho 1 -> IN kho 2)
        var transferKey = DeterministicGuid(tenantId, "u14-transfer-1");
        var transferExists = await db.StockTransfers.AsNoTracking()
            .AnyAsync(t => t.TenantId == tenantId && t.RequestKey == transferKey, ct);
        if (!transferExists)
        {
            var stService = sp.GetRequiredService<IStockTransferService>();
            var stDto = await stService.CreateTransferAsync(new CreateStockTransferRequestDto
            {
                RequestKey = transferKey,
                FromBranchId = branch.Id,
                ToBranchId = branch.Id,
                FromWarehouseId = wh1.Id,
                ToWarehouseId = wh2.Id,
                Note = "Chuyển kho mẫu 2 bước Upgrade 14",
                Items = new List<StockTransferItemRequestDto>
                {
                    new() { SkuId = sku1.Id, Quantity = 5m }
                }
            }, owner.Id, ct);

            await stService.DispatchTransferAsync(stDto.Id, new DispatchStockTransferRequestDto { Note = "Xuất chuyển kho demo" }, owner.Id, ct);
            await stService.ReceiveTransferAsync(stDto.Id, new ReceiveStockTransferRequestDto { Note = "Nhận chuyển kho demo" }, owner.Id, ct);
        }

        // 5. Kiểm kê có chênh lệch thừa và thiếu được duyệt (tạo luôn InventoryAdjustment)
        var countKey = DeterministicGuid(tenantId, "u14-stockcount-1");
        var countExists = await db.StockCounts.AsNoTracking()
            .AnyAsync(c => c.TenantId == tenantId && c.RequestKey == countKey, ct);
        if (!countExists)
        {
            var scService = sp.GetRequiredService<IStockCountService>();
            var scDto = await scService.CreateStockCountAsync(new CreateStockCountRequestDto
            {
                RequestKey = countKey,
                BranchId = branch.Id,
                WarehouseId = wh1.Id,
                SkuIds = new List<Guid> { sku1.Id, sku2.Id },
                Reason = "Kiểm kê định kỳ mẫu Upgrade 14 (có thừa và thiếu)"
            }, owner.Id, ct);

            var item1 = scDto.Items.First(i => i.SkuId == sku1.Id);
            var item2 = scDto.Items.First(i => i.SkuId == sku2.Id);

            var deficitQty = Math.Max(0m, item1.ExpectedQuantity - 2m);
            var surplusQty = item2.ExpectedQuantity + 3m;

            await scService.RecordCountsAsync(scDto.Id, new RecordStockCountBatchRequestDto
            {
                Items = new List<RecordStockCountItemRequestDto>
                {
                    new() { SkuId = sku1.Id, CountedQuantity = deficitQty },
                    new() { SkuId = sku2.Id, CountedQuantity = surplusQty, AdjustmentUnitCost = 80000m }
                }
            }, owner.Id, ct);

            await scService.SubmitStockCountAsync(scDto.Id, new SubmitStockCountRequestDto { Note = "Nộp duyệt kiểm kê mẫu" }, owner.Id, ct);
            await scService.ConfirmStockCountAsync(scDto.Id, new ConfirmStockCountRequestDto { Note = "Duyệt cân bằng kiểm kê mẫu Upgrade 14" }, owner.Id, ct);
        }

        // 6. Trả hàng một phần (1 / 3 sản phẩm của đơn POS) -> sinh SalesReturn IN
        var returnKey = DeterministicGuid(tenantId, "u14-salesreturn-1");
        var returnExists = await db.SalesReturns.AsNoTracking()
            .AnyAsync(r => r.TenantId == tenantId && r.RequestKey == returnKey, ct);
        if (!returnExists)
        {
            var orderItemId = await db.OrderItems.AsNoTracking()
                .Where(oi => oi.TenantId == tenantId && oi.OrderId == posOrder.Id)
                .Select(oi => oi.Id)
                .FirstAsync(ct);

            var srService = sp.GetRequiredService<ISalesReturnService>();
            var (srDto, _) = await srService.CreateAsync(new CreateSalesReturnDto
            {
                RequestKey = returnKey,
                OrderId = posOrder.Id,
                WarehouseId = wh1.Id,
                Reason = "Khách đổi size (mẫu Upgrade 14)",
                Items = new List<CreateSalesReturnItemDto>
                {
                    new() { OrderItemId = orderItemId, Quantity = 1m, RefundAmount = sku1.RetailPrice }
                }
            }, returnKey, owner.Id, ct);

            await srService.ConfirmAsync(srDto.Id, "Cash", owner.Id, ct);
        }

        logger?.LogInformation("Đã seed thành công bộ chứng từ mẫu Upgrade 14 cho tenant {TenantId}.", tenantId);
    }

    private static Guid DeterministicGuid(Guid tenantId, string name)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes($"{tenantId:N}:{name}"));
        return new Guid(bytes);
    }
}
