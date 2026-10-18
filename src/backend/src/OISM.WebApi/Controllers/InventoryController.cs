using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OISM.Application.Features.Inventory;
using OISM.Domain.Entities;
using OISM.Infrastructure.Persistence;

namespace OISM.WebApi.Controllers;

/// <summary>
/// API Quản lý Tồn kho, Phiếu nhập kho nhiều dòng (Multi-line Goods Receipt),
/// Tính giá vốn bình quân gia quyền (WAC) và Sổ kho bất biến (Append-Only Ledger).
/// </summary>
[ApiController]
[Route("api/inventory")]
[Authorize]
public class InventoryController : ControllerBase
{
    private readonly AppDbContext _context;

    public InventoryController(AppDbContext context)
    {
        _context = context;
    }

    private Guid GetCurrentUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(sub, out var userId) ? userId : Guid.Empty;
    }

    private static string ExtractVariantName(string? attributesJson, string fallbackCode)
    {
        if (string.IsNullOrWhiteSpace(attributesJson)) return fallbackCode;
        try
        {
            using var doc = JsonDocument.Parse(attributesJson);
            if (doc.RootElement.TryGetProperty("variant_name", out var vn) && vn.ValueKind == JsonValueKind.String)
            {
                return vn.GetString() ?? fallbackCode;
            }
            var parts = new List<string>();
            if (doc.RootElement.TryGetProperty("color", out var c) && c.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(c.GetString()))
            {
                parts.Add(c.GetString()!);
            }
            if (doc.RootElement.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(s.GetString()))
            {
                parts.Add(s.GetString()!);
            }
            return parts.Count > 0 ? string.Join(" / ", parts) : fallbackCode;
        }
        catch
        {
            return fallbackCode;
        }
    }

    /// <summary>
    /// 1. Lấy danh sách Tồn kho theo Kho/Chi nhánh & SKU kèm Giá vốn bình quân (WAC) (FR-INV-01, FR-COST-01).
    /// </summary>
    [HttpGet("balances")]
    public async Task<IActionResult> GetBalances(
        [FromQuery] Guid? branchId,
        [FromQuery] Guid? warehouseId,
        [FromQuery] string? search,
        [FromQuery] bool? lowStockOnly)
    {
        // Lấy danh sách tồn theo kho; nếu chưa có dòng warehouse_inventory_balances thì kết hợp từ inventory_balances + kho mặc định của chi nhánh
        var warehouses = await _context.Warehouses
            .Include(w => w.Branch)
            .ToListAsync();

        var branchBalances = await _context.InventoryBalances
            .Include(b => b.Branch)
            .Include(b => b.Sku)
                .ThenInclude(s => s!.Product)
            .ToListAsync();

        var warehouseBalances = await _context.WarehouseInventoryBalances
            .Include(wb => wb.Warehouse)
            .Include(wb => wb.Branch)
            .Include(wb => wb.Sku)
                .ThenInclude(s => s!.Product)
            .ToListAsync();

        var result = new List<InventoryBalanceResponse>();

        if (warehouseBalances.Count > 0)
        {
            foreach (var wb in warehouseBalances)
            {
                var branchBal = branchBalances.FirstOrDefault(b => b.BranchId == wb.BranchId && b.SkuId == wb.SkuId);
                var avgCost = branchBal?.AverageCost ?? 0m;
                var reorderPt = branchBal?.ReorderPoint ?? 0m;
                var skuCode = wb.Sku?.Code ?? string.Empty;
                var prodName = wb.Sku?.Product?.Name ?? string.Empty;
                var varName = ExtractVariantName(wb.Sku?.AttributesJson, skuCode);

                result.Add(new InventoryBalanceResponse
                {
                    Id = wb.Id,
                    BranchId = wb.BranchId,
                    BranchName = wb.Branch?.Name ?? string.Empty,
                    WarehouseId = wb.WarehouseId,
                    WarehouseName = wb.Warehouse?.Name ?? string.Empty,
                    SkuId = wb.SkuId,
                    SkuCode = skuCode,
                    ProductName = prodName,
                    VariantName = varName,
                    OnHand = wb.OnHand,
                    Reserved = wb.Reserved,
                    AverageCost = Math.Round(avgCost, 2),
                    ReorderPoint = reorderPt
                });
            }
        }

        // Bổ sung các dòng từ branchBalances chưa có trong warehouseBalances
        foreach (var bb in branchBalances)
        {
            var alreadyIncluded = result.Any(r => r.BranchId == bb.BranchId && r.SkuId == bb.SkuId);
            if (alreadyIncluded) continue;

            var defaultWh = warehouses.FirstOrDefault(w => w.BranchId == bb.BranchId);
            var skuCode = bb.Sku?.Code ?? string.Empty;
            var prodName = bb.Sku?.Product?.Name ?? string.Empty;
            var varName = ExtractVariantName(bb.Sku?.AttributesJson, skuCode);

            result.Add(new InventoryBalanceResponse
            {
                Id = bb.Id,
                BranchId = bb.BranchId,
                BranchName = bb.Branch?.Name ?? string.Empty,
                WarehouseId = defaultWh?.Id ?? Guid.Empty,
                WarehouseName = defaultWh?.Name ?? "Kho mặc định",
                SkuId = bb.SkuId,
                SkuCode = skuCode,
                ProductName = prodName,
                VariantName = varName,
                OnHand = bb.OnHand,
                Reserved = bb.Reserved,
                AverageCost = Math.Round(bb.AverageCost, 2),
                ReorderPoint = bb.ReorderPoint
            });
        }

        if (branchId.HasValue)
        {
            result = result.Where(r => r.BranchId == branchId.Value).ToList();
        }

        if (warehouseId.HasValue)
        {
            result = result.Where(r => r.WarehouseId == warehouseId.Value).ToList();
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim().ToLower();
            result = result.Where(r =>
                r.SkuCode.ToLower().Contains(q) ||
                r.ProductName.ToLower().Contains(q) ||
                r.VariantName.ToLower().Contains(q)).ToList();
        }

        if (lowStockOnly == true)
        {
            result = result.Where(r => r.Available <= r.ReorderPoint).ToList();
        }

        return Ok(result.OrderBy(r => r.SkuCode).ToList());
    }

    /// <summary>
    /// 2. Cập nhật ngưỡng cảnh báo tồn kho tối thiểu (Reorder Point).
    /// </summary>
    [HttpPut("balances/reorder-point")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> UpdateReorderPoint([FromBody] UpdateReorderPointRequest request)
    {
        if (request.ReorderPoint < 0)
        {
            return BadRequest(new { message = "Ngưỡng cảnh báo tồn không được nhỏ hơn 0." });
        }

        Guid? targetBranchId = request.BranchId;
        if (!targetBranchId.HasValue && request.WarehouseId.HasValue)
        {
            var wh = await _context.Warehouses.FirstOrDefaultAsync(w => w.Id == request.WarehouseId.Value);
            targetBranchId = wh?.BranchId;
        }

        var balance = await _context.InventoryBalances
            .FirstOrDefaultAsync(b =>
                b.SkuId == request.SkuId &&
                (!targetBranchId.HasValue || b.BranchId == targetBranchId.Value));

        if (balance == null)
        {
            if (!targetBranchId.HasValue)
            {
                return NotFound(new { message = "Không tìm thấy bản ghi tồn kho của SKU này." });
            }

            balance = new InventoryBalance
            {
                BranchId = targetBranchId.Value,
                SkuId = request.SkuId,
                OnHand = 0,
                Reserved = 0,
                AverageCost = 0,
                ReorderPoint = request.ReorderPoint
            };
            _context.InventoryBalances.Add(balance);
        }
        else
        {
            balance.ReorderPoint = request.ReorderPoint;
        }

        await _context.SaveChangesAsync();
        return Ok(new { message = "Đã cập nhật ngưỡng cảnh báo tồn kho thành công." });
    }

    /// <summary>
    /// 3. Lấy danh sách Phiếu nhập kho nhiều dòng (Multi-line Goods Receipts).
    /// </summary>
    [HttpGet("receipts")]
    public async Task<IActionResult> GetReceipts(
        [FromQuery] Guid? warehouseId,
        [FromQuery] Guid? supplierId,
        [FromQuery] string? status)
    {
        var query = _context.GoodsReceipts
            .Include(r => r.Warehouse)
            .Include(r => r.Supplier)
            .Include(r => r.Items)
                .ThenInclude(i => i.Sku)
                    .ThenInclude(s => s!.Product)
            .AsQueryable();

        if (warehouseId.HasValue)
        {
            query = query.Where(r => r.WarehouseId == warehouseId.Value);
        }

        if (supplierId.HasValue)
        {
            query = query.Where(r => r.SupplierId == supplierId.Value);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(r => r.Status == status);
        }

        var receipts = await query
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        var response = receipts.Select(MapReceiptResponse).ToList();
        return Ok(response);
    }

    /// <summary>
    /// 4. Tạo Phiếu nhập kho nhiều dòng ở trạng thái Nháp (Draft).
    /// </summary>
    [HttpPost("receipts")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> CreateReceipt([FromBody] CreateGoodsReceiptRequest request)
    {
        var validator = new CreateGoodsReceiptValidator();
        var validationResult = await validator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        var rawLines = (request.Lines != null && request.Lines.Count > 0)
            ? request.Lines
            : request.Items ?? new List<CreateGoodsReceiptLineRequest>();

        var warehouse = await _context.Warehouses.FirstOrDefaultAsync(w => w.Id == request.WarehouseId);
        if (warehouse == null)
        {
            return BadRequest(new { message = "Kho nhận hàng không tồn tại hoặc không thuộc cửa hàng của bạn." });
        }

        var supplier = await _context.Suppliers.FirstOrDefaultAsync(s => s.Id == request.SupplierId);
        if (supplier == null)
        {
            return BadRequest(new { message = "Nhà cung cấp không tồn tại hoặc không thuộc cửa hàng của bạn." });
        }

        // Gộp các dòng trùng SKU trong cùng 1 phiếu để đảm bảo uq_goods_receipt_items_2 (tenant_id, receipt_id, sku_id)
        var groupedLines = rawLines
            .GroupBy(l => l.SkuId)
            .Select(g =>
            {
                var totalQty = g.Sum(x => x.Quantity);
                var weightedPrice = totalQty > 0
                    ? g.Sum(x => x.Quantity * x.UnitCost) / totalQty
                    : g.First().UnitCost;
                return new CreateGoodsReceiptLineRequest
                {
                    SkuId = g.Key,
                    Quantity = totalQty,
                    UnitCost = weightedPrice
                };
            })
            .ToList();

        foreach (var line in groupedLines)
        {
            if (line.Quantity <= 0)
            {
                return BadRequest(new { message = "Số lượng nhập của từng dòng SKU phải lớn hơn 0." });
            }
            if (line.UnitCost < 0)
            {
                return BadRequest(new { message = "Đơn giá nhập không được nhỏ hơn 0." });
            }
        }

        var skuIds = groupedLines.Select(l => l.SkuId).ToList();
        var skus = await _context.ProductVariants
            .Include(v => v.Product)
            .Where(v => skuIds.Contains(v.Id))
            .ToListAsync();

        if (skus.Count != skuIds.Count)
        {
            return BadRequest(new { message = "Một hoặc nhiều mã SKU trong phiếu nhập không tồn tại." });
        }

        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            var fallbackUser = await _context.Users.FirstOrDefaultAsync();
            userId = fallbackUser?.Id ?? Guid.NewGuid();
        }

        var receiptNumber = $"PN-{DateTimeOffset.UtcNow:yyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}";

        var receipt = new GoodsReceipt
        {
            Id = Guid.NewGuid(),
            Number = receiptNumber,
            BranchId = warehouse.BranchId,
            WarehouseId = warehouse.Id,
            SupplierId = supplier.Id,
            Status = "Draft",
            Note = request.Note?.Trim(),
            CreatedBy = userId
        };

        foreach (var line in groupedLines)
        {
            receipt.Items.Add(new GoodsReceiptItem
            {
                Id = Guid.NewGuid(),
                ReceiptId = receipt.Id,
                SkuId = line.SkuId,
                Quantity = line.Quantity,
                UnitPrice = line.UnitCost
            });
        }

        _context.GoodsReceipts.Add(receipt);
        await _context.SaveChangesAsync();

        // Reload navigation properties for response
        receipt.Warehouse = warehouse;
        receipt.Supplier = supplier;
        foreach (var item in receipt.Items)
        {
            item.Sku = skus.FirstOrDefault(s => s.Id == item.SkuId);
        }

        return StatusCode(201, MapReceiptResponse(receipt));
    }

    /// <summary>
    /// 5. Xác nhận Phiếu nhập kho (Atomic Confirm):
    /// - Chuyển trạng thái phiếu sang Confirmed
    /// - Cộng tồn kho thực tế (OnHand) tại Kho và Chi nhánh
    /// - Tính lại Giá vốn bình quân gia quyền (WAC)
    /// - Ghi bút toán IN bất biến vào Sổ kho (inventory_transactions)
    /// </summary>
    [HttpPost("receipts/{id}/confirm")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> ConfirmReceipt(Guid id)
    {
        var strategy = _context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync<IActionResult>(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();

            var receipt = await _context.GoodsReceipts
                .Include(r => r.Warehouse)
                .Include(r => r.Supplier)
                .Include(r => r.Items)
                    .ThenInclude(i => i.Sku)
                        .ThenInclude(s => s!.Product)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (receipt == null)
            {
                return NotFound(new { message = "Không tìm thấy phiếu nhập kho." });
            }

            if (receipt.Status == "Confirmed")
            {
                return BadRequest(new { message = "Phiếu nhập kho này đã được xác nhận và ghi sổ trước đó." });
            }

            if (receipt.Status == "Cancelled")
            {
                return BadRequest(new { message = "Không thể xác nhận phiếu nhập kho đã hủy." });
            }

            var userId = GetCurrentUserId();
            if (userId == Guid.Empty)
            {
                userId = receipt.CreatedBy;
            }

            receipt.Status = "Confirmed";
            receipt.ConfirmedAt = DateTimeOffset.UtcNow;
            receipt.ConfirmedBy = userId;

            foreach (var item in receipt.Items)
            {
                // 1. Cập nhật tổng tồn chi nhánh & Giá vốn bình quân gia quyền (WAC)
                var branchBalance = await _context.InventoryBalances
                    .FirstOrDefaultAsync(b => b.BranchId == receipt.BranchId && b.SkuId == item.SkuId);

                if (branchBalance == null)
                {
                    branchBalance = new InventoryBalance
                    {
                        Id = Guid.NewGuid(),
                        BranchId = receipt.BranchId,
                        SkuId = item.SkuId,
                        OnHand = 0,
                        Reserved = 0,
                        AverageCost = 0,
                        ReorderPoint = 5,
                        LastSequence = 0
                    };
                    _context.InventoryBalances.Add(branchBalance);
                }

                var oldOnHand = branchBalance.OnHand;
                var oldWac = branchBalance.AverageCost;
                var newOnHand = oldOnHand + item.Quantity;
                var newWac = newOnHand > 0
                    ? ((oldOnHand * oldWac) + (item.Quantity * item.UnitPrice)) / newOnHand
                    : item.UnitPrice;
                var nextSequence = branchBalance.LastSequence + 1;

                branchBalance.OnHand = newOnHand;
                branchBalance.AverageCost = Math.Round(newWac, 6);
                branchBalance.LastSequence = nextSequence;

                // 2. Cập nhật tồn vật lý tại Kho nhận
                var whBalance = await _context.WarehouseInventoryBalances
                    .FirstOrDefaultAsync(wb => wb.WarehouseId == receipt.WarehouseId && wb.SkuId == item.SkuId);

                if (whBalance == null)
                {
                    whBalance = new WarehouseInventoryBalance
                    {
                        Id = Guid.NewGuid(),
                        BranchId = receipt.BranchId,
                        WarehouseId = receipt.WarehouseId,
                        SkuId = item.SkuId,
                        OnHand = item.Quantity,
                        Reserved = 0,
                        LastSequence = nextSequence
                    };
                    _context.WarehouseInventoryBalances.Add(whBalance);
                }
                else
                {
                    whBalance.OnHand += item.Quantity;
                    whBalance.LastSequence = nextSequence;
                }

                // 3. Ghi bút toán IN vào Sổ kho bất biến (Append-Only Ledger)
                var ledgerTx = new InventoryTransaction
                {
                    Id = Guid.NewGuid(),
                    BranchId = receipt.BranchId,
                    WarehouseId = receipt.WarehouseId,
                    SkuId = item.SkuId,
                    Type = "IN",
                    Quantity = item.Quantity,
                    UnitCost = item.UnitPrice,
                    BalanceAfter = newOnHand,
                    SequenceNo = nextSequence,
                    ReceiptItemId = item.Id,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                _context.InventoryTransactions.Add(ledgerTx);
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return Ok(new
            {
                message = $"Đã xác nhận và ghi sổ kho thành công phiếu nhập {receipt.Number}.",
                receipt = MapReceiptResponse(receipt)
            });
        });
    }

    /// <summary>
    /// 6. Tra cứu Sổ kho chỉ đọc (Append-Only Inventory Ledger - FR-INV-01).
    /// </summary>
    [HttpGet("ledger")]
    public async Task<IActionResult> GetLedger(
        [FromQuery] Guid? warehouseId,
        [FromQuery] Guid? skuId,
        [FromQuery] string? direction,
        [FromQuery] string? operationType,
        [FromQuery] string? search)
    {
        var query = _context.InventoryTransactions
            .Include(t => t.Warehouse)
            .Include(t => t.Sku)
                .ThenInclude(s => s!.Product)
            .Include(t => t.ReceiptItem)
                .ThenInclude(ri => ri!.Receipt)
            .AsQueryable();

        if (warehouseId.HasValue)
        {
            query = query.Where(t => t.WarehouseId == warehouseId.Value);
        }

        if (skuId.HasValue)
        {
            query = query.Where(t => t.SkuId == skuId.Value);
        }

        if (!string.IsNullOrWhiteSpace(direction) && (direction == "IN" || direction == "OUT"))
        {
            query = query.Where(t => t.Type == direction);
        }

        var transactions = await query
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.SequenceNo)
            .Take(250)
            .ToListAsync();

        var entries = transactions.Select(t =>
        {
            string opType = "Adjustment";
            string refCode = $"TX-{t.SequenceNo:D5}";

            if (t.ReceiptItemId.HasValue)
            {
                opType = "GoodsReceipt";
                refCode = t.ReceiptItem?.Receipt?.Number ?? $"PN-{t.ReceiptItemId.Value.ToString("N")[..6].ToUpper()}";
            }
            else if (t.OrderItemId.HasValue)
            {
                opType = "SaleOut";
                refCode = $"SO-{t.OrderItemId.Value.ToString("N")[..6].ToUpper()}";
            }
            else if (t.TransferItemId.HasValue)
            {
                opType = t.Type == "IN" ? "TransferIn" : "TransferOut";
                refCode = $"TF-{t.TransferItemId.Value.ToString("N")[..6].ToUpper()}";
            }
            else if (t.SalesReturnItemId.HasValue)
            {
                opType = "ReturnIn";
                refCode = $"RT-{t.SalesReturnItemId.Value.ToString("N")[..6].ToUpper()}";
            }

            return new InventoryLedgerEntryResponse
            {
                Id = t.Id,
                CreatedAt = t.CreatedAt,
                WarehouseId = t.WarehouseId,
                WarehouseName = t.Warehouse?.Name ?? string.Empty,
                SkuId = t.SkuId,
                SkuCode = t.Sku?.Code ?? string.Empty,
                ProductName = t.Sku?.Product?.Name ?? string.Empty,
                Direction = t.Type,
                OperationType = opType,
                QuantityDelta = t.Quantity,
                BalanceAfter = t.BalanceAfter,
                ReferenceCode = refCode
            };
        }).ToList();

        if (!string.IsNullOrWhiteSpace(operationType))
        {
            entries = entries
                .Where(e => string.Equals(e.OperationType, operationType, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim().ToLower();
            entries = entries
                .Where(e =>
                    e.SkuCode.ToLower().Contains(q) ||
                    e.ReferenceCode.ToLower().Contains(q) ||
                    e.ProductName.ToLower().Contains(q))
                .ToList();
        }

        return Ok(entries);
    }

    private static GoodsReceiptResponse MapReceiptResponse(GoodsReceipt r)
    {
        var lines = r.Items.Select(i => new GoodsReceiptLineResponse
        {
            Id = i.Id,
            SkuId = i.SkuId,
            SkuCode = i.Sku?.Code ?? string.Empty,
            ProductName = i.Sku?.Product?.Name ?? string.Empty,
            Quantity = i.Quantity,
            UnitCost = Math.Round(i.UnitPrice, 2)
        }).ToList();

        return new GoodsReceiptResponse
        {
            Id = r.Id,
            Code = r.Number,
            SupplierId = r.SupplierId,
            SupplierName = r.Supplier?.Name ?? string.Empty,
            WarehouseId = r.WarehouseId,
            WarehouseName = r.Warehouse?.Name ?? string.Empty,
            Status = r.Status,
            Note = r.Note,
            TotalAmount = Math.Round(lines.Sum(l => l.LineTotal), 2),
            CreatedAt = r.CreatedAt,
            ConfirmedAt = r.ConfirmedAt,
            Lines = lines
        };
    }
}
