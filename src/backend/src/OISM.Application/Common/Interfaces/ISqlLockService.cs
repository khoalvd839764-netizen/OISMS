namespace OISM.Application.Common.Interfaces;

/// <summary>
/// Giao diện trừu tượng hóa các thao tác khóa bi quan và khóa đồng thời ở mức cơ sở dữ liệu.
/// Hiện thực tại tầng Infrastructure để loại bỏ SQL đặc thù của database provider khỏi Application.
/// </summary>
public interface ISqlLockService
{
    /// <summary>
    /// Acquires the stock advisory locks in ascending SKU order within the current transaction.
    /// </summary>
    Task LockSkuStockAsync(Guid tenantId, Guid branchId, IEnumerable<Guid> skuIds, CancellationToken ct = default);
    Task LockRequestKeyAsync(Guid tenantId, Guid requestKey, CancellationToken ct = default);
    Task LockGoodsReceiptAsync(Guid tenantId, Guid receiptId, CancellationToken ct = default);
    Task LockStockTransferAsync(Guid tenantId, Guid transferId, CancellationToken ct = default);
    Task LockStockTransferNumberAsync(Guid tenantId, CancellationToken ct = default);
    Task LockStockCountScheduleAsync(Guid tenantId, Guid warehouseId, string periodType, CancellationToken ct = default);
    Task LockStockCountPeriodGenerationAsync(Guid tenantId, CancellationToken ct = default);
    Task LockStockCountPeriodAsync(Guid tenantId, Guid periodId, CancellationToken ct = default);
    Task LockStockCountNumberAsync(Guid tenantId, CancellationToken ct = default);
    Task LockStockCountAsync(Guid tenantId, Guid stockCountId, CancellationToken ct = default);
    Task LockInventoryAdjustmentNumberAsync(Guid tenantId, CancellationToken ct = default);
    Task LockExternalOrderAsync(Guid tenantId, string channel, string shopId, string externalOrderId, CancellationToken ct = default);
    Task LockOrderAsync(Guid tenantId, Guid orderId, CancellationToken ct = default);
    Task LockOrderRowAsync(Guid tenantId, Guid orderId, CancellationToken ct = default);
    Task LockOrderReservationRowsAsync(Guid tenantId, Guid orderId, CancellationToken ct = default);
    Task LockBranchInventoryRowsAsync(Guid tenantId, Guid branchId, IEnumerable<Guid> skuIds, CancellationToken ct = default);
    Task LockWarehouseInventoryRowsAsync(Guid tenantId, Guid branchId, Guid warehouseId, IEnumerable<Guid> skuIds, CancellationToken ct = default);
    Task SetConstraintsImmediateAsync(CancellationToken ct = default);

    /// <summary>
    /// Khóa toàn bộ cây danh mục của Tenant bằng advisory lock để chống race condition và vòng lặp phân cấp.
    /// </summary>
    Task LockCategoryTreeAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Khóa bi quan bản ghi Sản phẩm (FOR UPDATE) để chống xung đột đồng thời khi thêm/sửa biến thể.
    /// </summary>
    Task LockProductAsync(Guid productId, CancellationToken ct = default);

    /// <summary>
    /// Khóa bi quan bản ghi Refresh Token (FOR UPDATE) để đảm bảo token rotation nguyên tử.
    /// </summary>
    Task LockRefreshTokenAsync(string tokenHash, CancellationToken ct = default);

    /// <summary>
    /// Khóa bi quan bản ghi Kho hàng (FOR UPDATE) để chống xung đột đồng thời khi đổi chi nhánh hoặc cập nhật kho.
    /// </summary>
    Task LockWarehouseAsync(Guid warehouseId, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Khóa toàn bộ danh mục đơn vị của Tenant bằng advisory lock ({tenant}/unit_catalog) để đảm bảo an toàn đồng thời khi khởi tạo hoặc sửa đổi đơn vị.
    /// </summary>
    Task LockUnitCatalogAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Khóa danh sách tài khoản nhận tiền của Tenant bằng advisory lock ({tenant}/payment_accounts) để thêm, đặt mặc định, ngừng dùng tuần tự.
    /// </summary>
    Task LockPaymentAccountsAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Khóa bi quan bản ghi Phiếu đổi giá (FOR UPDATE) có lọc tenant để chống duyệt/từ chối đồng thời.
    /// </summary>
    Task LockPriceChangeRequestAsync(Guid tenantId, Guid requestId, CancellationToken ct = default);

    /// <summary>
    /// Khóa bi quan bản ghi Giá đơn vị SKU (FOR UPDATE) có lọc tenant để chống hủy đồng thời.
    /// </summary>
    Task LockSkuUnitPriceAsync(Guid tenantId, Guid priceId, CancellationToken ct = default);

    /// <summary>
    /// Khóa Phiếu mua hàng bằng advisory lock ({tenant}/purchase_order/{id}) để tuần tự hóa duyệt, đóng, hủy và xác nhận nhập kho.
    /// </summary>
    Task LockPurchaseOrderAsync(Guid tenantId, Guid purchaseOrderId, CancellationToken ct = default);

    /// <summary>
    /// Khóa bi quan Phiếu trả hàng bằng advisory lock ({tenant}/sales_return/{id}) để tuần tự hóa xác nhận, hủy phiếu.
    /// </summary>
    Task LockSalesReturnAsync(Guid tenantId, Guid returnId, CancellationToken ct = default);

    /// <summary>
    /// Khóa số phiếu trả hàng bằng advisory lock ({tenant}/sales_return_number) để sinh số phiếu RT-yyyyMMdd-xxxx tuần tự không trùng.
    /// </summary>
    Task LockSalesReturnNumberAsync(Guid tenantId, CancellationToken ct = default);
}
