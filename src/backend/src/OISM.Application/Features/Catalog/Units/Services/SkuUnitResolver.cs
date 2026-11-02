using OISM.Application.Common.Exceptions;
using OISM.Application.Features.Orders.Services;
using OISM.Domain.Entities;

namespace OISM.Application.Features.Catalog.Units.Services;

public record ResolvedSkuUnit(
    Guid? UnitId,
    string UnitName,
    decimal Factor,
    int DecimalPlaces,
    decimal? SalePrice,
    bool IsBase,
    int Version);

/// <summary>
/// Bộ phân giải đơn vị dùng chung thuần logic, không truy vấn DB.
/// </summary>
public static class SkuUnitResolver
{
    public static ResolvedSkuUnit ResolveForSale(ProductVariant sku, IEnumerable<SkuUnit>? skuUnits, Guid? unitId)
    {
        return ResolveCore(sku, skuUnits, unitId, isSale: true);
    }

    public static ResolvedSkuUnit ResolveForPurchase(ProductVariant sku, IEnumerable<SkuUnit>? skuUnits, Guid? unitId)
    {
        return ResolveCore(sku, skuUnits, unitId, isSale: false);
    }

    private static ResolvedSkuUnit ResolveCore(ProductVariant sku, IEnumerable<SkuUnit>? skuUnits, Guid? unitId, bool isSale)
    {
        var unitsList = skuUnits?.ToList() ?? new List<SkuUnit>();

        // 1. Nếu unitId NULL hoặc trùng BaseUnitId -> Đơn vị gốc
        if (!unitId.HasValue || (sku.BaseUnitId.HasValue && unitId.Value == sku.BaseUnitId.Value))
        {
            // Có BaseUnitId thì chỉ khớp đúng dòng đó; hệ số 1 chỉ là dự phòng cho SKU chưa gán đơn vị gốc
            var baseConfig = sku.BaseUnitId.HasValue
                ? unitsList.FirstOrDefault(u => u.UnitId == sku.BaseUnitId.Value)
                : unitsList.FirstOrDefault(u => u.FactorToBase == 1m);

            var unitName = baseConfig?.Unit?.Name 
                ?? (!string.IsNullOrWhiteSpace(sku.Unit) ? sku.Unit : "piece");

            var decimalPlaces = baseConfig != null ? (int)baseConfig.QuantityDecimalPlaces : 3;
            var version = baseConfig?.Version ?? 1;

            return new ResolvedSkuUnit(
                UnitId: sku.BaseUnitId ?? baseConfig?.UnitId,
                UnitName: unitName,
                Factor: 1m,
                DecimalPlaces: decimalPlaces,
                SalePrice: sku.RetailPrice,
                IsBase: true,
                Version: version);
        }

        // 2. Tra cứu cấu hình đơn vị quy cách
        var targetConfig = unitsList.FirstOrDefault(u => u.UnitId == unitId.Value);
        if (targetConfig == null || !targetConfig.IsActive || targetConfig.Unit is { IsActive: false })
        {
            throw new OismException("UNIT_NOT_ALLOWED", $"Đơn vị tính '{unitId.Value}' chưa được kích hoạt hoặc không khả dụng cho SKU này.", 422);
        }

        if (isSale && !targetConfig.CanSell)
        {
            throw new OismException("UNIT_NOT_ALLOWED", $"Đơn vị tính '{unitId.Value}' không được phép bán cho SKU này.", 422);
        }

        if (!isSale && !targetConfig.CanPurchase)
        {
            throw new OismException("UNIT_NOT_ALLOWED", $"Đơn vị tính '{unitId.Value}' không được phép nhập cho SKU này.", 422);
        }

        var name = targetConfig.Unit?.Name ?? targetConfig.Unit?.Code ?? (!string.IsNullOrWhiteSpace(sku.Unit) ? sku.Unit : "piece");

        return new ResolvedSkuUnit(
            UnitId: targetConfig.UnitId,
            UnitName: name,
            Factor: targetConfig.FactorToBase,
            DecimalPlaces: targetConfig.QuantityDecimalPlaces,
            SalePrice: targetConfig.SalePrice,
            IsBase: false,
            Version: targetConfig.Version);
    }

    public static decimal ComputeBaseQuantity(decimal quantity, ResolvedSkuUnit resolved)
    {
        if (quantity <= 0)
        {
            throw new OismException("INVALID_UNIT_QUANTITY", "Số lượng giao dịch phải lớn hơn 0.", 400);
        }

        // Kiểm tra số lẻ của lượng giao dịch
        if (decimal.Round(quantity, resolved.DecimalPlaces) != quantity)
        {
            throw new OismException("INVALID_UNIT_QUANTITY", $"Số lượng giao dịch vượt quá {resolved.DecimalPlaces} chữ số thập phân cho phép.", 400);
        }

        var baseQuantity = quantity * resolved.Factor;

        // Lượng gốc vượt 3 chữ số lẻ -> từ chối, không làm tròn
        if (decimal.Round(baseQuantity, 3) != baseQuantity)
        {
            throw new OismException("INVALID_UNIT_QUANTITY", "Số lượng quy đổi về đơn vị gốc vượt quá 3 chữ số thập phân cho phép.", 400);
        }

        if (baseQuantity > OrderService.MaxQuantityAmount)
        {
            throw new OismException("INVALID_UNIT_QUANTITY", "Số lượng quy đổi vượt quá giới hạn tối đa cho phép của hệ thống.", 400);
        }

        if (baseQuantity <= 0)
        {
            throw new OismException("INVALID_UNIT_QUANTITY", "Số lượng quy đổi về đơn vị gốc phải lớn hơn 0.", 400);
        }

        return baseQuantity;
    }
}
