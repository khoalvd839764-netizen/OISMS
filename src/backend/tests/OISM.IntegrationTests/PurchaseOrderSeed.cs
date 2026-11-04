using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Catalog.Units.Services;
using OISM.Application.Features.Inventory.Dtos;
using OISM.Application.Features.Inventory.Services;
using OISM.Application.Features.PurchaseOrders.Dtos;
using OISM.Domain.Entities;
using OISM.Infrastructure.Persistence;
using OISM.Infrastructure.Services;

namespace OISM.IntegrationTests;

/// <summary>
/// Helper tạo nhanh Phiếu mua hàng (PurchaseOrder) đã duyệt và Phiếu nhập kho (GoodsReceipt)
/// tuân thủ đúng trình tự trigger DB: tạo Draft -> thêm dòng -> cập nhật Approved.
/// </summary>
public static class PurchaseOrderSeed
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task<(PurchaseOrder Po, IReadOnlyList<PurchaseOrderItem> Items)> CreateApprovedPurchaseOrderAsync(
        AppDbContext context,
        Guid tenantId,
        Guid branchId,
        Guid supplierId,
        Guid userId,
        params (Guid SkuId, decimal OrderedQuantity, decimal UnitPrice, Guid? UnitId)[] items)
    {
        if (items == null || items.Length == 0)
        {
            throw new ArgumentException("Cần ít nhất 1 dòng hàng để tạo phiếu mua.", nameof(items));
        }

        using var _ = context.OpenPrivilegedScope();

        var skuIds = items.Select(i => i.SkuId).Distinct().ToList();
        var skus = await context.ProductVariants
            .IgnoreQueryFilters()
            .Include(v => v.Product)
            .Where(v => v.TenantId == tenantId && skuIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id);

        var skuUnits = await context.SkuUnits
            .IgnoreQueryFilters()
            .Include(u => u.Unit)
            .Where(u => u.TenantId == tenantId && skuIds.Contains(u.SkuId))
            .ToListAsync();

        var unitsBySku = skuUnits
            .GroupBy(u => u.SkuId)
            .ToDictionary(g => g.Key, g => (IEnumerable<SkuUnit>)g.ToList());

        var now = DateTimeOffset.UtcNow;
        var po = new PurchaseOrder
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branchId,
            SupplierId = supplierId,
            Number = $"PO-{now:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
            Status = "Draft",
            Note = "Seeded approved PO for integration test",
            RequestKey = Guid.NewGuid(),
            CreatedBy = userId,
            CreatedAt = now,
            UpdatedAt = now
        };

        var poItems = new List<PurchaseOrderItem>(items.Length);

        foreach (var line in items)
        {
            skus.TryGetValue(line.SkuId, out var sku);
            unitsBySku.TryGetValue(line.SkuId, out var configuredUnits);

            Guid? resolvedUnitId = line.UnitId;
            string unitName = "piece";
            decimal factor = 1m;
            short decimalPlaces = 3;

            if (sku != null)
            {
                var resolved = SkuUnitResolver.ResolveForPurchase(sku, configuredUnits, line.UnitId);
                SkuUnitResolver.ComputeBaseQuantity(line.OrderedQuantity, resolved);
                resolvedUnitId = resolved.UnitId;
                unitName = resolved.UnitName;
                factor = resolved.Factor;
                decimalPlaces = (short)resolved.DecimalPlaces;
            }

            var poItem = new PurchaseOrderItem
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                PurchaseOrderId = po.Id,
                SkuId = line.SkuId,
                UnitId = resolvedUnitId,
                UnitNameSnapshot = unitName,
                FactorSnapshot = factor,
                QuantityDecimalPlaces = decimalPlaces,
                Quantity = line.OrderedQuantity,
                ReceivedBaseQuantity = 0m,
                UnitPrice = line.UnitPrice,
                CreatedAt = now,
                UpdatedAt = now
            };

            poItems.Add(poItem);
        }

        // Bước 1: Lưu PO ở trạng thái Draft kèm các dòng (thỏa mãn trg_purchase_order_item_guard)
        context.PurchaseOrders.Add(po);
        context.PurchaseOrderItems.AddRange(poItems);
        await context.SaveChangesAsync();

        // Bước 2: Chuyển trạng thái sang Approved (thỏa mãn trg_purchase_order_guard)
        po.Status = "Approved";
        po.ApprovedBy = userId;
        po.ApprovedAt = DateTimeOffset.UtcNow;
        po.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync();

        return (po, poItems);
    }

    public static async Task<CreateGoodsReceiptDto> CreateDraftReceiptDtoAsync(
        AppDbContext context,
        Guid tenantId,
        Guid branchId,
        Guid warehouseId,
        Guid supplierId,
        Guid userId,
        string? note,
        params (Guid SkuId, decimal Quantity, decimal UnitPrice, Guid? UnitId)[] items)
    {
        var poLines = items
            .Select(i => (i.SkuId, OrderedQuantity: i.Quantity > 0 ? i.Quantity : 1m, UnitPrice: i.UnitPrice >= 0 ? i.UnitPrice : 1m, i.UnitId))
            .ToArray();

        var (po, createdItems) = await CreateApprovedPurchaseOrderAsync(
            context,
            tenantId,
            branchId,
            supplierId,
            userId,
            poLines);

        return new CreateGoodsReceiptDto
        {
            PurchaseOrderId = po.Id,
            WarehouseId = warehouseId,
            Note = note,
            Items = items.Select((line, idx) => new CreateGoodsReceiptItemDto
            {
                PurchaseOrderItemId = createdItems[idx].Id,
                Quantity = line.Quantity
            }).ToList()
        };
    }

    public static Task<CreateGoodsReceiptDto> CreateDraftReceiptDtoAsync(
        AppDbContext context,
        Guid tenantId,
        Guid branchId,
        Guid warehouseId,
        Guid supplierId,
        Guid userId,
        params (Guid SkuId, decimal Quantity, decimal UnitPrice)[] items)
    {
        return CreateDraftReceiptDtoAsync(
            context,
            tenantId,
            branchId,
            warehouseId,
            supplierId,
            userId,
            note: "Seeded goods receipt from approved PO",
            items.Select(i => (i.SkuId, i.Quantity, i.UnitPrice, (Guid?)null)).ToArray());
    }

    public static async Task<GoodsReceiptResponseDto> ReceiveStockAsync(
        AppDbContext context,
        ICurrentTenantService tenantService,
        Guid branchId,
        Guid warehouseId,
        Guid supplierId,
        Guid userId,
        params (Guid SkuId, decimal Quantity, decimal UnitPrice, Guid? UnitId)[] items)
    {
        var tenantId = tenantService.TenantId ?? throw new InvalidOperationException("TenantId must be set before ReceiveStockAsync.");
        var dto = await CreateDraftReceiptDtoAsync(
            context,
            tenantId,
            branchId,
            warehouseId,
            supplierId,
            userId,
            "Seeded stock via PurchaseOrderSeed",
            items);

        var service = new GoodsReceiptService(context, tenantService, new WacCalculator(), new SqlLockService(context));
        var draft = await service.CreateReceiptAsync(dto, userId);
        return await service.ConfirmReceiptAsync(draft.Id, userId);
    }

    public static Task<GoodsReceiptResponseDto> ReceiveStockAsync(
        AppDbContext context,
        ICurrentTenantService tenantService,
        Guid branchId,
        Guid warehouseId,
        Guid supplierId,
        Guid userId,
        params (Guid SkuId, decimal Quantity, decimal UnitPrice)[] items)
    {
        return ReceiveStockAsync(
            context,
            tenantService,
            branchId,
            warehouseId,
            supplierId,
            userId,
            items.Select(i => (i.SkuId, i.Quantity, i.UnitPrice, (Guid?)null)).ToArray());
    }

    public static async Task<PurchaseOrderResponseDto> CreateApprovedPoViaApiAsync(
        HttpClient client,
        Guid branchId,
        Guid supplierId,
        params (Guid SkuId, decimal OrderedQuantity, decimal UnitPrice, Guid? UnitId)[] items)
    {
        var createReq = new CreatePurchaseOrderDto
        {
            BranchId = branchId,
            SupplierId = supplierId,
            Note = "Created via PurchaseOrderSeed API helper",
            Items = items.Select(i => new CreatePurchaseOrderItemDto
            {
                SkuId = i.SkuId,
                UnitId = i.UnitId,
                Quantity = i.OrderedQuantity,
                UnitPrice = i.UnitPrice
            }).ToList()
        };

        using var createMsg = new HttpRequestMessage(HttpMethod.Post, "/api/purchase-orders")
        {
            Content = JsonContent.Create(createReq)
        };
        createMsg.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var createRes = await client.SendAsync(createMsg);
        var createBody = await createRes.Content.ReadAsStringAsync();
        if (!createRes.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Failed to create PO via API ({(int)createRes.StatusCode}): {createBody}");
        }

        var po = JsonSerializer.Deserialize<PurchaseOrderResponseDto>(createBody, JsonOptions)!;
        var approveReq = new ApprovePurchaseOrderRequestDto
        {
            ConfirmZeroPrice = items.Any(i => i.UnitPrice == 0m)
        };
        var approveRes = await client.PostAsJsonAsync($"/api/purchase-orders/{po.Id}/approve", approveReq);
        var approveBody = await approveRes.Content.ReadAsStringAsync();
        if (!approveRes.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Failed to approve PO via API ({(int)approveRes.StatusCode}): {approveBody}");
        }

        return JsonSerializer.Deserialize<PurchaseOrderResponseDto>(approveBody, JsonOptions)!;
    }

    public static async Task<CreateGoodsReceiptDto> CreateDraftReceiptDtoViaApiAsync(
        HttpClient client,
        Guid branchId,
        Guid warehouseId,
        Guid supplierId,
        string? note,
        params (Guid SkuId, decimal Quantity, decimal UnitPrice, Guid? UnitId)[] items)
    {
        var po = await CreateApprovedPoViaApiAsync(
            client,
            branchId,
            supplierId,
            items.Select(i => (i.SkuId, OrderedQuantity: i.Quantity > 0 ? i.Quantity : 1m, UnitPrice: i.UnitPrice >= 0 ? i.UnitPrice : 1m, i.UnitId)).ToArray());

        return new CreateGoodsReceiptDto
        {
            PurchaseOrderId = po.Id,
            WarehouseId = warehouseId,
            Note = note,
            Items = items.Select((line, idx) => new CreateGoodsReceiptItemDto
            {
                PurchaseOrderItemId = po.Items[idx].Id,
                Quantity = line.Quantity
            }).ToList()
        };
    }
}
