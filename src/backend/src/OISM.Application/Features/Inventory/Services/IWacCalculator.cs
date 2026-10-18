namespace OISM.Application.Features.Inventory.Services;

/// <summary>
/// Giao diện tính toán Giá vốn bình quân gia quyền (Weighted Average Cost - WAC).
/// Đáp ứng yêu cầu BE1-07 cho chi nhánh và SKU.
/// </summary>
public interface IWacCalculator
{
    /// <summary>
    /// Tính toán giá vốn bình quân mới theo công thức:
    /// NewCost = (OldStock * OldCost + ImportQty * ImportPrice) / (OldStock + ImportQty)
    /// </summary>
    /// <param name="oldStock">Số lượng tồn kho trước khi nhập (on_hand của chi nhánh).</param>
    /// <param name="oldCost">Giá vốn WAC hiện tại của chi nhánh (average_cost).</param>
    /// <param name="importQty">Số lượng nhập mới trong phiếu (quantity > 0).</param>
    /// <param name="importPrice">Đơn giá nhập mới trong phiếu (unit_price >= 0).</param>
    /// <returns>Giá vốn bình quân mới làm tròn 6 chữ số thập phân.</returns>
    decimal CalculateWac(decimal oldStock, decimal oldCost, decimal importQty, decimal importPrice);
}

public class WacCalculator : IWacCalculator
{
    public const int DecimalPrecision = 6;

    public decimal CalculateWac(decimal oldStock, decimal oldCost, decimal importQty, decimal importPrice)
    {
        if (importQty <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(importQty), "Số lượng nhập phải lớn hơn 0.");
        }

        if (importPrice < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(importPrice), "Đơn giá nhập không được âm.");
        }

        // Trường hợp biên: Tồn ban đầu bằng 0 hoặc âm (do dữ liệu cũ/điều chỉnh)
        // Giá vốn mới chính là đơn giá nhập của lô này
        if (oldStock <= 0)
        {
            return Math.Round(importPrice, DecimalPrecision, MidpointRounding.AwayFromZero);
        }

        // Tồn cũ dương: Áp dụng công thức bình quân gia quyền
        var totalOldValue = oldStock * oldCost;
        var totalImportValue = importQty * importPrice;
        var totalStock = oldStock + importQty;

        var newCost = (totalOldValue + totalImportValue) / totalStock;

        return Math.Round(newCost, DecimalPrecision, MidpointRounding.AwayFromZero);
    }
}
